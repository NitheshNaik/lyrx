using CommunityToolkit.Mvvm.ComponentModel;
using VerciWin.Core.Color;
using VerciWin.Core.Lyrics.Models;
using VerciWin.Core.Media;

namespace VerciWin.ViewModels;

/// <summary>
/// ViewModel for the main overlay window. Exposes observable playback state,
/// active lyrics, visual theme palette, and presentation settings.
/// </summary>
public partial class OverlayViewModel : ObservableObject
{
    [ObservableProperty]
    private PlaybackState _currentState = PlaybackState.Empty;

    [ObservableProperty]
    private LyricDocument? _currentLyrics;

    [ObservableProperty]
    private TypographyPalette _palette = TypographyPalette.NeutralPalette;

    [ObservableProperty]
    private bool _isOverlayMode = true;

    [ObservableProperty]
    private double _opacity = 1.0;

    [ObservableProperty]
    private string _visualStyle = "Glow";

    [ObservableProperty]
    private string _overlayPosition = "LowerThird";

    /// <summary>
    /// Event triggered when window mode (Overlay vs Normal) changes, so that
    /// the view code-behind can update extended window styles and Z-order.
    /// </summary>
    public event EventHandler<bool>? ModeChanged;

    /// <summary>
    /// Event triggered when the positioning preset changes.
    /// </summary>
    public event EventHandler<string>? PositionChanged;

    partial void OnIsOverlayModeChanged(bool value)
    {
        ModeChanged?.Invoke(this, value);
    }

    partial void OnOverlayPositionChanged(string value)
    {
        PositionChanged?.Invoke(this, value);
    }
}
