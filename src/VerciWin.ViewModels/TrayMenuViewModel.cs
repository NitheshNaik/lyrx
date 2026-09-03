using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VerciWin.Core.Media;
using VerciWin.Core.Settings;

namespace VerciWin.ViewModels;

/// <summary>
/// ViewModel driving the system tray icon, context menu commands, and dynamic tooltip.
/// </summary>
public partial class TrayMenuViewModel : ObservableObject
{
    private readonly OverlayViewModel _overlayViewModel;
    private readonly SettingsStore _settingsStore;

    [ObservableProperty]
    private string _tooltipText = "VerciWin";

    [ObservableProperty]
    private bool _isOverlayMode;

    [ObservableProperty]
    private double _currentOpacity;

    [ObservableProperty]
    private string _currentStyle;

    public event EventHandler? OpenSettingsRequested;
    public event EventHandler? ExitRequested;

    public TrayMenuViewModel(OverlayViewModel overlayViewModel, SettingsStore settingsStore)
    {
        _overlayViewModel = overlayViewModel ?? throw new ArgumentNullException(nameof(overlayViewModel));
        _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));

        _isOverlayMode = _overlayViewModel.IsOverlayMode;
        _currentOpacity = _overlayViewModel.Opacity;
        _currentStyle = _overlayViewModel.VisualStyle;

        // Keep local properties synchronized with overlay state
        _overlayViewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(OverlayViewModel.IsOverlayMode))
            {
                IsOverlayMode = _overlayViewModel.IsOverlayMode;
            }
            else if (e.PropertyName == nameof(OverlayViewModel.Opacity))
            {
                CurrentOpacity = _overlayViewModel.Opacity;
            }
            else if (e.PropertyName == nameof(OverlayViewModel.VisualStyle))
            {
                CurrentStyle = _overlayViewModel.VisualStyle;
            }
            else if (e.PropertyName == nameof(OverlayViewModel.CurrentState))
            {
                UpdateTooltip(_overlayViewModel.CurrentState);
            }
        };
    }

    public void UpdateTooltip(PlaybackState state)
    {
        if (state.IsEmpty)
        {
            TooltipText = "VerciWin — Idle";
        }
        else
        {
            string title = string.IsNullOrWhiteSpace(state.Title) ? "Unknown" : state.Title;
            string artist = string.IsNullOrWhiteSpace(state.Artist) ? "Unknown Artist" : state.Artist;
            TooltipText = $"VerciWin — {title} by {artist}";
        }
    }

    [RelayCommand]
    public async Task ToggleOverlayModeAsync()
    {
        _overlayViewModel.IsOverlayMode = !_overlayViewModel.IsOverlayMode;
        IsOverlayMode = _overlayViewModel.IsOverlayMode;
        await PersistSettingsAsync();
    }

    [RelayCommand]
    public async Task SetOpacityAsync(double opacity)
    {
        _overlayViewModel.Opacity = opacity;
        CurrentOpacity = opacity;
        await PersistSettingsAsync();
    }

    [RelayCommand]
    public async Task SetStyleAsync(string style)
    {
        _overlayViewModel.VisualStyle = style;
        CurrentStyle = style;
        await PersistSettingsAsync();
    }

    [RelayCommand]
    public void OpenSettings()
    {
        OpenSettingsRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    public void Exit()
    {
        ExitRequested?.Invoke(this, EventArgs.Empty);
    }

    private async Task PersistSettingsAsync()
    {
        var settings = new AppSettings
        {
            Opacity = _overlayViewModel.Opacity,
            VisualStyle = _overlayViewModel.VisualStyle,
            IsOverlayMode = _overlayViewModel.IsOverlayMode,
            OverlayPosition = _overlayViewModel.OverlayPosition
        };
        await _settingsStore.SaveAsync(settings);
    }
}
