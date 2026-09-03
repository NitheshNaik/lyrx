using Windows.UI;

namespace VerciWin.Core.Color;

/// <summary>
/// Color palette extracted from album artwork (or fallback default).
/// Used by LyricCanvasRenderer to tint the glow, text highlights, and ambient backdrop.
/// </summary>
public readonly record struct TypographyPalette(
    Windows.UI.Color Primary,
    Windows.UI.Color Accent,
    Windows.UI.Color GlowBackground)
{
    /// <summary>
    /// Neutral dark-glass palette used when no album art is available or when
    /// decoding fails. Ensures the overlay always looks polished and legible.
    /// </summary>
    public static readonly TypographyPalette NeutralPalette = new(
        Primary: Windows.UI.Color.FromArgb(255, 232, 232, 240),       // #E8E8F0 (cool near-white)
        Accent: Windows.UI.Color.FromArgb(255, 144, 144, 200),        // #9090C8 (muted periwinkle)
        GlowBackground: Windows.UI.Color.FromArgb(255, 13, 13, 26)    // #0D0D1A (deep dark navy)
    );
}