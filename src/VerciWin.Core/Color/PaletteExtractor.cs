using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using Windows.UI;

namespace VerciWin.Core.Color;

/// <summary>
/// Extracts a vibrant typography palette from an album art stream using
/// median-cut color quantization and saturation-biased scoring.
/// <para>
/// If no artwork is provided (<paramref name="artStream"/> is <c>null</c>), or if decoding
/// fails for any reason, <see cref="TypographyPalette.NeutralPalette"/> is returned.
/// </para>
/// </summary>
public sealed class PaletteExtractor
{
    // Fast in-memory cache keyed by 64-bit stream hash
    private readonly ConcurrentDictionary<ulong, TypographyPalette> _cache = new();

    /// <summary>
    /// Extracts a <see cref="TypographyPalette"/> from the given image stream.
    /// Safe to call concurrently; never throws.
    /// </summary>
    public async Task<TypographyPalette> ExtractAsync(Stream? artStream, CancellationToken ct = default)
    {
        if (artStream is null || artStream.Length == 0)
        {
            return TypographyPalette.NeutralPalette;
        }

        ulong hash = 0;
        try
        {
            artStream.Position = 0;
            hash = ComputeFastHash(artStream);

            if (_cache.TryGetValue(hash, out var cached))
            {
                return cached;
            }

            artStream.Position = 0;
            using var randomAccessStream = new InMemoryRandomAccessStream();
            await artStream.CopyToAsync(randomAccessStream.AsStreamForWrite(), ct);
            randomAccessStream.Seek(0);

            var decoder = await BitmapDecoder.CreateAsync(randomAccessStream);

            // Downsample to 64x64 for fast and deterministic median-cut quantization
            var transform = new BitmapTransform
            {
                ScaledWidth = 64,
                ScaledHeight = 64,
                InterpolationMode = BitmapInterpolationMode.Fant
            };

            var pixelData = await decoder.GetPixelDataAsync(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Premultiplied,
                transform,
                ExifOrientationMode.IgnoreExifOrientation,
                ColorManagementMode.ColorManageToSRgb);

            byte[] pixels = pixelData.DetachPixelData();
            var palette = QuantizeAndExtractPalette(pixels);

            _cache[hash] = palette;
            return palette;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[PaletteExtractor] Failed to extract palette from artwork: {ex.Message}");
            return TypographyPalette.NeutralPalette;
        }
    }

    private static ulong ComputeFastHash(Stream stream)
    {
        // FNV-1a 64-bit on first 4KB of stream
        ulong hash = 14695981039346656037UL;
        byte[] buffer = new byte[4096];
        int read = stream.Read(buffer, 0, buffer.Length);
        for (int i = 0; i < read; i++)
        {
            hash ^= buffer[i];
            hash *= 1099511628211UL;
        }
        return hash;
    }

    private static TypographyPalette QuantizeAndExtractPalette(byte[] bgraPixels)
    {
        var rawColors = new List<RgbColor>(bgraPixels.Length / 4);

        for (int i = 0; i < bgraPixels.Length; i += 4)
        {
            byte b = bgraPixels[i];
            byte g = bgraPixels[i + 1];
            byte r = bgraPixels[i + 2];
            byte a = bgraPixels[i + 3];

            if (a < 128) continue; // Ignore transparent pixels
            rawColors.Add(new RgbColor(r, g, b));
        }

        if (rawColors.Count == 0)
        {
            return TypographyPalette.NeutralPalette;
        }

        // Perform median-cut partitioning into 8 buckets
        var buckets = MedianCut(rawColors, targetBucketCount: 8);

        // Convert bucket averages into HSL and score for vibrancy
        var candidates = new List<ScoredColor>();
        foreach (var bucket in buckets)
        {
            if (bucket.Count == 0) continue;
            long sumR = 0, sumG = 0, sumB = 0;
            foreach (var c in bucket)
            {
                sumR += c.R;
                sumG += c.G;
                sumB += c.B;
            }

            byte avgR = (byte)(sumR / bucket.Count);
            byte avgG = (byte)(sumG / bucket.Count);
            byte avgB = (byte)(sumB / bucket.Count);

            RgbToHsl(avgR, avgG, avgB, out double h, out double s, out double l);

            // Score favoring saturated colors with mid-range luminance (avoid pure black/white)
            double luminanceWeight = 1.0 - Math.Abs(l - 0.5) * 1.8;
            if (luminanceWeight < 0) luminanceWeight = 0;
            double score = s * luminanceWeight * Math.Log(bucket.Count + 1);

            candidates.Add(new ScoredColor(avgR, avgG, avgB, h, s, l, score));
        }

        if (candidates.Count == 0)
        {
            return TypographyPalette.NeutralPalette;
        }

        // Sort candidates by vibrancy score descending
        candidates.Sort((a, b) => b.Score.CompareTo(a.Score));

        var topVibrant = candidates[0];

        // Accent color is the most vibrant color
        Windows.UI.Color accent = Windows.UI.Color.FromArgb(255, topVibrant.R, topVibrant.G, topVibrant.B);

        // Primary text color: boosted lightness for readability, tinted with accent hue
        HslToRgb(topVibrant.H, Math.Min(topVibrant.S, 0.4), 0.92, out byte priR, out byte priG, out byte priB);
        Windows.UI.Color primary = Windows.UI.Color.FromArgb(255, priR, priG, priB);

        // Glow background: deep dark tint derived from the dominant hue
        HslToRgb(topVibrant.H, Math.Min(topVibrant.S, 0.6), 0.08, out byte bgR, out byte bgG, out byte bgB);
        Windows.UI.Color glowBackground = Windows.UI.Color.FromArgb(255, bgR, bgG, bgB);

        return new TypographyPalette(primary, accent, glowBackground);
    }

    private static List<List<RgbColor>> MedianCut(List<RgbColor> colors, int targetBucketCount)
    {
        var buckets = new List<List<RgbColor>> { colors };

        while (buckets.Count < targetBucketCount)
        {
            // Find bucket with the largest range along its widest dimension
            int splitIdx = -1;
            int maxRange = -1;
            int widestDimension = 0; // 0=R, 1=G, 2=B

            for (int i = 0; i < buckets.Count; i++)
            {
                var bucket = buckets[i];
                if (bucket.Count < 2) continue;

                byte minR = 255, maxR = 0;
                byte minG = 255, maxG = 0;
                byte minB = 255, maxB = 0;

                foreach (var c in bucket)
                {
                    if (c.R < minR) minR = c.R;
                    if (c.R > maxR) maxR = c.R;
                    if (c.G < minG) minG = c.G;
                    if (c.G > maxG) maxG = c.G;
                    if (c.B < minB) minB = c.B;
                    if (c.B > maxB) maxB = c.B;
                }

                int rangeR = maxR - minR;
                int rangeG = maxG - minG;
                int rangeB = maxB - minB;

                int bucketMaxRange = Math.Max(rangeR, Math.Max(rangeG, rangeB));
                if (bucketMaxRange > maxRange)
                {
                    maxRange = bucketMaxRange;
                    splitIdx = i;
                    widestDimension = (bucketMaxRange == rangeR) ? 0 : (bucketMaxRange == rangeG ? 1 : 2);
                }
            }

            if (splitIdx == -1 || maxRange == 0)
                break; // Cannot split further

            var targetBucket = buckets[splitIdx];

            // Sort along the widest dimension
            switch (widestDimension)
            {
                case 0:
                    targetBucket.Sort((a, b) => a.R.CompareTo(b.R));
                    break;
                case 1:
                    targetBucket.Sort((a, b) => a.G.CompareTo(b.G));
                    break;
                default:
                    targetBucket.Sort((a, b) => a.B.CompareTo(b.B));
                    break;
            }

            int mid = targetBucket.Count / 2;
            var newBucket1 = targetBucket.GetRange(0, mid);
            var newBucket2 = targetBucket.GetRange(mid, targetBucket.Count - mid);

            buckets[splitIdx] = newBucket1;
            buckets.Add(newBucket2);
        }

        return buckets;
    }

    private static void RgbToHsl(byte r, byte g, byte b, out double h, out double s, out double l)
    {
        double rd = r / 255.0;
        double gd = g / 255.0;
        double bd = b / 255.0;

        double max = Math.Max(rd, Math.Max(gd, bd));
        double min = Math.Min(rd, Math.Min(gd, bd));
        double delta = max - min;

        l = (max + min) / 2.0;

        if (Math.Abs(delta) < 0.00001)
        {
            h = 0;
            s = 0;
            return;
        }

        s = l > 0.5 ? delta / (2.0 - max - min) : delta / (max + min);

        if (Math.Abs(max - rd) < 0.00001)
        {
            h = (gd - bd) / delta + (gd < bd ? 6 : 0);
        }
        else if (Math.Abs(max - gd) < 0.00001)
        {
            h = (bd - rd) / delta + 2;
        }
        else
        {
            h = (rd - gd) / delta + 4;
        }

        h /= 6.0;
    }

    private static void HslToRgb(double h, double s, double l, out byte r, out byte g, out byte b)
    {
        if (Math.Abs(s) < 0.00001)
        {
            byte val = (byte)Math.Round(l * 255);
            r = g = b = val;
            return;
        }

        double q = l < 0.5 ? l * (1.0 + s) : l + s - l * s;
        double p = 2.0 * l - q;

        r = (byte)Math.Round(HueToRgb(p, q, h + 1.0 / 3.0) * 255);
        g = (byte)Math.Round(HueToRgb(p, q, h) * 255);
        b = (byte)Math.Round(HueToRgb(p, q, h - 1.0 / 3.0) * 255);
    }

    private static double HueToRgb(double p, double q, double t)
    {
        if (t < 0) t += 1;
        if (t > 1) t -= 1;
        if (t < 1.0 / 6.0) return p + (q - p) * 6.0 * t;
        if (t < 1.0 / 2.0) return q;
        if (t < 2.0 / 3.0) return p + (q - p) * (2.0 / 3.0 - t) * 6.0;
        return p;
    }

    private readonly record struct RgbColor(byte R, byte G, byte B);
    private readonly record struct ScoredColor(byte R, byte G, byte B, double H, double S, double L, double Score);
}