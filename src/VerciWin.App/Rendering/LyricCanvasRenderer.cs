using System.Diagnostics;
using System.Numerics;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Brushes;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Windows.Foundation;
using Windows.UI;
using VerciWin.Core.Color;
using VerciWin.Core.Lyrics.Models;
using VerciWin.Core.Media;
using VerciWin.ViewModels;

namespace VerciWin.App.Rendering;

/// <summary>
/// GPU-accelerated Win2D renderer for kinetic typography, animated lyrics,
/// word scaling/glowing, and ambient album-art-driven backdrop gradients.
/// </summary>
public sealed class LyricCanvasRenderer : IDisposable
{
    private readonly OverlayViewModel _viewModel;
    private readonly Func<TimeSpan> _positionProvider;
    private readonly Stopwatch _runtimeStopwatch = Stopwatch.StartNew();

    private CanvasTextFormat? _wordFormat;
    private CanvasTextFormat? _secondaryFormat;
    private CanvasTextFormat? _statusFormat;
    private CanvasTextFormat? _statusSubFormat;

    private float _animatedLineYOffset = 0f;
    private int _lastActiveLineIndex = -1;

    public LyricCanvasRenderer(OverlayViewModel viewModel, Func<TimeSpan> positionProvider)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _positionProvider = positionProvider ?? throw new ArgumentNullException(nameof(positionProvider));
    }

    /// <summary>
    /// Initialises device-independent text formats and resources.
    /// Called from <see cref="CanvasControl.CreateResources"/>.
    /// </summary>
    public void CreateResources(CanvasControl sender)
    {
        _wordFormat?.Dispose();
        _secondaryFormat?.Dispose();
        _statusFormat?.Dispose();
        _statusSubFormat?.Dispose();

        _wordFormat = new CanvasTextFormat
        {
            FontFamily = "Segoe UI Variable Display, Segoe UI, sans-serif",
            FontSize = 36f,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            HorizontalAlignment = CanvasHorizontalAlignment.Left,
            VerticalAlignment = CanvasVerticalAlignment.Center,
            WordWrapping = CanvasWordWrapping.NoWrap
        };

        _secondaryFormat = new CanvasTextFormat
        {
            FontFamily = "Segoe UI Variable Text, Segoe UI, sans-serif",
            FontSize = 22f,
            FontWeight = Microsoft.UI.Text.FontWeights.Normal,
            HorizontalAlignment = CanvasHorizontalAlignment.Center,
            VerticalAlignment = CanvasVerticalAlignment.Center,
            WordWrapping = CanvasWordWrapping.NoWrap
        };

        _statusFormat = new CanvasTextFormat
        {
            FontFamily = "Segoe UI Variable Display, Segoe UI, sans-serif",
            FontSize = 28f,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            HorizontalAlignment = CanvasHorizontalAlignment.Center,
            VerticalAlignment = CanvasVerticalAlignment.Center,
            WordWrapping = CanvasWordWrapping.NoWrap
        };

        _statusSubFormat = new CanvasTextFormat
        {
            FontFamily = "Segoe UI Variable Text, Segoe UI, sans-serif",
            FontSize = 16f,
            FontWeight = Microsoft.UI.Text.FontWeights.Normal,
            HorizontalAlignment = CanvasHorizontalAlignment.Center,
            VerticalAlignment = CanvasVerticalAlignment.Center,
            WordWrapping = CanvasWordWrapping.NoWrap
        };
    }

    /// <summary>
    /// Main draw entry point for each frame rendered on the Win2D canvas.
    /// </summary>
    public void Draw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        if (_wordFormat is null || _secondaryFormat is null || _statusFormat is null || _statusSubFormat is null)
            return;

        var ds = args.DrawingSession;
        var bounds = sender.Size;
        float width = (float)bounds.Width;
        float height = (float)bounds.Height;

        if (width <= 0 || height <= 0) return;

        var state = _viewModel.CurrentState;
        var palette = _viewModel.Palette;
        double masterOpacity = Math.Clamp(_viewModel.Opacity, 0.1, 1.0);
        bool isGlowStyle = string.Equals(_viewModel.VisualStyle, "Glow", StringComparison.OrdinalIgnoreCase);

        // 1. Draw ambient floating background glow (if Glow style is active)
        if (isGlowStyle)
        {
            DrawAmbientBackdrop(ds, width, height, palette, masterOpacity);
        }

        // 2. State-dependent rendering
        if (state.IsEmpty)
        {
            DrawEmptyState(ds, width, height, palette, masterOpacity);
            return;
        }

        var lyrics = _viewModel.CurrentLyrics;
        if (lyrics is null || lyrics.Lines.Count == 0)
        {
            DrawNoLyricsState(ds, width, height, state, palette, masterOpacity);
            return;
        }

        // 3. Active lyrics rendering
        TimeSpan now = _positionProvider();
        DrawLyrics(ds, sender, width, height, lyrics, now, palette, isGlowStyle, masterOpacity);
    }

    private void DrawAmbientBackdrop(
        CanvasDrawingSession ds,
        float width,
        float height,
        TypographyPalette palette,
        double opacity)
    {
        float totalSecs = (float)_runtimeStopwatch.Elapsed.TotalSeconds;

        // Subtle floating center movement
        float driftX = (float)Math.Sin(totalSecs * 0.4) * (width * 0.12f);
        float driftY = (float)Math.Cos(totalSecs * 0.3) * (height * 0.15f);

        var center = new Vector2(width * 0.5f + driftX, height * 0.5f + driftY);
        float radiusX = width * 0.65f;
        float radiusY = height * 0.75f;

        var bgCol = palette.GlowBackground;
        var accentCol = palette.Accent;

        // Blend inner accent glow with deep background
        byte innerAlpha = (byte)(80 * opacity);
        var innerColor = Color.FromArgb(innerAlpha, accentCol.R, accentCol.G, accentCol.B);
        var outerColor = Color.FromArgb(0, bgCol.R, bgCol.G, bgCol.B);

        var gradientStops = new CanvasGradientStop[]
        {
            new() { Position = 0.0f, Color = innerColor },
            new() { Position = 0.5f, Color = Color.FromArgb((byte)(40 * opacity), bgCol.R, bgCol.G, bgCol.B) },
            new() { Position = 1.0f, Color = outerColor }
        };

        using var brush = new CanvasRadialGradientBrush(ds, gradientStops)
        {
            Center = center,
            RadiusX = radiusX,
            RadiusY = radiusY
        };

        ds.FillRectangle(0, 0, width, height, brush);
    }

    private void DrawEmptyState(
        CanvasDrawingSession ds,
        float width,
        float height,
        TypographyPalette palette,
        double opacity)
    {
        float pulse = (float)(0.6 + 0.4 * Math.Sin(_runtimeStopwatch.Elapsed.TotalSeconds * 2.0));
        byte alpha = (byte)(160 * pulse * opacity);

        var color = Color.FromArgb(alpha, palette.Primary.R, palette.Primary.G, palette.Primary.B);
        var subColor = Color.FromArgb((byte)(120 * opacity), palette.Accent.R, palette.Accent.G, palette.Accent.B);

        ds.DrawText("♫ VerciWin", width * 0.5f, height * 0.45f, color, _statusFormat!);
        ds.DrawText("Play audio in any app to see synced lyrics", width * 0.5f, height * 0.58f, subColor, _statusSubFormat!);
    }

    private void DrawNoLyricsState(
        CanvasDrawingSession ds,
        float width,
        float height,
        PlaybackState state,
        TypographyPalette palette,
        double opacity)
    {
        byte priAlpha = (byte)(230 * opacity);
        byte secAlpha = (byte)(140 * opacity);

        var priColor = Color.FromArgb(priAlpha, palette.Primary.R, palette.Primary.G, palette.Primary.B);
        var secColor = Color.FromArgb(secAlpha, palette.Accent.R, palette.Accent.G, palette.Accent.B);

        string title = string.IsNullOrWhiteSpace(state.Title) ? "Unknown Track" : state.Title;
        string artist = string.IsNullOrWhiteSpace(state.Artist) ? "Unknown Artist" : state.Artist;

        ds.DrawText(title, width * 0.5f, height * 0.42f, priColor, _statusFormat!);
        ds.DrawText($"{artist}  •  Lyrics not found", width * 0.5f, height * 0.58f, secColor, _statusSubFormat!);
    }

    private void DrawLyrics(
        CanvasDrawingSession ds,
        ICanvasResourceCreator resourceCreator,
        float width,
        float height,
        LyricDocument lyrics,
        TimeSpan now,
        TypographyPalette palette,
        bool isGlowStyle,
        double masterOpacity)
    {
        // 1. Locate current active line by timestamp
        int activeLineIndex = -1;
        for (int i = 0; i < lyrics.Lines.Count; i++)
        {
            var line = lyrics.Lines[i];
            if (now >= line.Start && (i == lyrics.Lines.Count - 1 || now < lyrics.Lines[i + 1].Start))
            {
                activeLineIndex = i;
                break;
            }
        }

        if (activeLineIndex == -1)
        {
            if (lyrics.Lines.Count > 0 && now < lyrics.Lines[0].Start)
            {
                // Intro / before first lyric line
                activeLineIndex = 0;
            }
            else
            {
                activeLineIndex = lyrics.Lines.Count - 1;
            }
        }

        // Smooth vertical sliding transition when changing lines
        if (_lastActiveLineIndex != activeLineIndex)
        {
            _lastActiveLineIndex = activeLineIndex;
            _animatedLineYOffset = 25f; // Jump slightly down and smoothly ease up
        }

        _animatedLineYOffset *= 0.85f; // Exponential decay ease to 0

        float centerY = height * 0.5f + _animatedLineYOffset;
        float lineSpacing = 52f;

        // Draw Past Line (above center, smaller, dimmed)
        if (activeLineIndex > 0)
        {
            var pastLine = lyrics.Lines[activeLineIndex - 1];
            if (!pastLine.IsEmpty)
            {
                DrawPastOrNextLine(ds, width, centerY - lineSpacing, pastLine.Text, palette, masterOpacity * 0.35);
            }
        }

        // Draw Active Line (at center focal point, with kinetic per-word highlight)
        if (activeLineIndex >= 0 && activeLineIndex < lyrics.Lines.Count)
        {
            var currentLine = lyrics.Lines[activeLineIndex];
            if (!currentLine.IsEmpty)
            {
                DrawCurrentLine(ds, resourceCreator, width, centerY, currentLine, now, palette, isGlowStyle, masterOpacity);
            }
        }

        // Draw Next Line (below center, smaller, dimmed)
        if (activeLineIndex < lyrics.Lines.Count - 1)
        {
            var nextLine = lyrics.Lines[activeLineIndex + 1];
            if (!nextLine.IsEmpty)
            {
                DrawPastOrNextLine(ds, width, centerY + lineSpacing, nextLine.Text, palette, masterOpacity * 0.35);
            }
        }
    }

    private void DrawPastOrNextLine(
        CanvasDrawingSession ds,
        float width,
        float y,
        string text,
        TypographyPalette palette,
        double opacity)
    {
        byte alpha = (byte)(255 * Math.Clamp(opacity, 0, 1));
        var color = Color.FromArgb(alpha, palette.Primary.R, palette.Primary.G, palette.Primary.B);
        ds.DrawText(text, width * 0.5f, y, color, _secondaryFormat!);
    }

    private void DrawCurrentLine(
        CanvasDrawingSession ds,
        ICanvasResourceCreator resourceCreator,
        float width,
        float centerY,
        LyricLine line,
        TimeSpan now,
        TypographyPalette palette,
        bool isGlowStyle,
        double masterOpacity)
    {
        if (line.Words.Count == 0) return;

        // Measure all word layouts to center the full line as a whole
        var layouts = new List<CanvasTextLayout>(line.Words.Count);
        float totalLineWidth = 0f;
        float wordGap = 12f;

        for (int i = 0; i < line.Words.Count; i++)
        {
            var word = line.Words[i];
            var layout = new CanvasTextLayout(resourceCreator, word.Text, _wordFormat!, width, 60f);
            layouts.Add(layout);
            totalLineWidth += (float)layout.LayoutBounds.Width;
            if (i < line.Words.Count - 1)
                totalLineWidth += wordGap;
        }

        float startX = (width - totalLineWidth) * 0.5f;
        float currentX = startX;

        for (int i = 0; i < line.Words.Count; i++)
        {
            var word = line.Words[i];
            var layout = layouts[i];
            float wordWidth = (float)layout.LayoutBounds.Width;

            bool isActiveWord = now >= word.Start && now < word.End;
            bool isPastWord = now >= word.End;

            float wordProgress = 0f;
            if (isActiveWord && word.Duration.TotalMilliseconds > 0)
            {
                wordProgress = (float)((now - word.Start).TotalMilliseconds / word.Duration.TotalMilliseconds);
                wordProgress = Math.Clamp(wordProgress, 0f, 1f);
            }

            // Word transform & colors
            float scale = 1.0f;
            Color wordColor;

            if (isActiveWord)
            {
                // Active word: cubic ease-out scale (1.0 -> 1.15) and vibrant highlight
                float easeScale = 1.0f - (float)Math.Pow(1.0f - wordProgress, 3);
                scale = 1.0f + 0.15f * (float)Math.Sin(wordProgress * Math.PI); // Pulse peak mid-word

                byte alpha = (byte)(255 * masterOpacity);
                wordColor = Color.FromArgb(alpha, palette.Accent.R, palette.Accent.G, palette.Accent.B);

                // Word outer glow effect for Glow style
                if (isGlowStyle)
                {
                    DrawWordGlow(ds, layout, currentX, centerY, palette.Accent, masterOpacity);
                }
            }
            else if (isPastWord)
            {
                // Past word in current line: slightly dimmed primary color
                byte alpha = (byte)(220 * masterOpacity);
                wordColor = Color.FromArgb(alpha, palette.Primary.R, palette.Primary.G, palette.Primary.B);
            }
            else
            {
                // Future word in current line: muted/subtle
                byte alpha = (byte)(130 * masterOpacity);
                wordColor = Color.FromArgb(alpha, palette.Primary.R, palette.Primary.G, palette.Primary.B);
            }

            // Apply scale transform centered on the word
            var wordCenter = new Vector2(currentX + wordWidth * 0.5f, centerY);
            var transform = Matrix3x2.CreateScale(scale, scale, wordCenter);

            var prevTransform = ds.Transform;
            ds.Transform = transform * prevTransform;

            ds.DrawTextLayout(layout, currentX, centerY - 30f, wordColor);

            ds.Transform = prevTransform;

            currentX += wordWidth + wordGap;
            layout.Dispose();
        }
    }

    private static void DrawWordGlow(
        CanvasDrawingSession ds,
        CanvasTextLayout layout,
        float x,
        float y,
        Color glowColor,
        double masterOpacity)
    {
        // Multi-pass feathered glow around the active word
        for (int pass = 1; pass <= 3; pass++)
        {
            float offset = pass * 1.5f;
            byte glowAlpha = (byte)((60 / pass) * masterOpacity);
            var tint = Color.FromArgb(glowAlpha, glowColor.R, glowColor.G, glowColor.B);

            ds.DrawTextLayout(layout, x - offset, y - 30f, tint);
            ds.DrawTextLayout(layout, x + offset, y - 30f, tint);
            ds.DrawTextLayout(layout, x, y - 30f - offset, tint);
            ds.DrawTextLayout(layout, x, y - 30f + offset, tint);
        }
    }

    public void Dispose()
    {
        _wordFormat?.Dispose();
        _secondaryFormat?.Dispose();
        _statusFormat?.Dispose();
        _statusSubFormat?.Dispose();
    }
}
