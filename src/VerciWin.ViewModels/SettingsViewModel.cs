using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VerciWin.Core.Settings;

namespace VerciWin.ViewModels;

/// <summary>
/// ViewModel backing the SettingsWindow.
/// </summary>
public partial class SettingsViewModel : ObservableObject
{
    private readonly SettingsStore _settingsStore;
    private readonly OverlayViewModel _overlayViewModel;

    [ObservableProperty]
    private double _opacity;

    [ObservableProperty]
    private string _visualStyle;

    [ObservableProperty]
    private bool _isOverlayMode;

    [ObservableProperty]
    private string _overlayPosition;

    [ObservableProperty]
    private string _version = "1.0.0 (WinUI 3 + Win2D Native)";

    public SettingsViewModel(SettingsStore settingsStore, OverlayViewModel overlayViewModel)
    {
        _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
        _overlayViewModel = overlayViewModel ?? throw new ArgumentNullException(nameof(overlayViewModel));

        // Load current state
        _opacity = _overlayViewModel.Opacity;
        _visualStyle = _overlayViewModel.VisualStyle;
        _isOverlayMode = _overlayViewModel.IsOverlayMode;
        _overlayPosition = _overlayViewModel.OverlayPosition;
    }

    [RelayCommand]
    public async Task SaveAsync()
    {
        // Apply to active overlay viewmodel
        _overlayViewModel.Opacity = Opacity;
        _overlayViewModel.VisualStyle = VisualStyle;
        _overlayViewModel.IsOverlayMode = IsOverlayMode;
        _overlayViewModel.OverlayPosition = OverlayPosition;

        // Persist to settings.json
        var settings = new AppSettings
        {
            Opacity = Opacity,
            VisualStyle = VisualStyle,
            IsOverlayMode = IsOverlayMode,
            OverlayPosition = OverlayPosition
        };

        await _settingsStore.SaveAsync(settings);
    }
}
