using System.Diagnostics;
using System.IO;
using H.NotifyIcon;
using Microsoft.UI.Xaml.Controls;
using VerciWin.ViewModels;

namespace VerciWin.App.Tray;

/// <summary>
/// Manages the system tray icon, notifications, and context menu using H.NotifyIcon.WinUI.
/// </summary>
public sealed class TrayIconManager : IDisposable
{
    private readonly TrayMenuViewModel _viewModel;
    private TaskbarIcon? _taskbarIcon;

    public TrayIconManager(TrayMenuViewModel viewModel)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
    }

    public void Initialize()
    {
        _taskbarIcon = new TaskbarIcon
        {
            ToolTipText = _viewModel.TooltipText,
            LeftClickCommand = _viewModel.ToggleOverlayModeCommand
        };

        // Try loading icon from Assets/TrayIcon.ico
        string iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "TrayIcon.ico");
        if (File.Exists(iconPath))
        {
            try
            {
                _taskbarIcon.IconSource = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(iconPath));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[TrayIconManager] Failed to load icon: {ex.Message}");
            }
        }

        // Build native XAML Context Menu
        var contextMenu = new MenuFlyout();

        // 1. Toggle Mode (Unlock to Drag / Lock Overlay)
        var toggleModeItem = new MenuFlyoutItem
        {
            Text = GetToggleModeText(_viewModel.IsOverlayMode)
        };
        toggleModeItem.Click += async (s, e) =>
        {
            await _viewModel.ToggleOverlayModeAsync();
            toggleModeItem.Text = GetToggleModeText(_viewModel.IsOverlayMode);
        };
        contextMenu.Items.Add(toggleModeItem);

        // 2. Position Presets Submenu
        var positionSubMenu = new MenuFlyoutSubItem { Text = "Position Presets" };
        AddPositionOption(positionSubMenu, "Lower Third (Bottom 30%)", "LowerThird");
        AddPositionOption(positionSubMenu, "Center Screen", "Center");
        AddPositionOption(positionSubMenu, "Full Screen", "FullScreen");
        contextMenu.Items.Add(positionSubMenu);

        contextMenu.Items.Add(new MenuFlyoutSeparator());

        // 3. Opacity Submenu
        var opacitySubMenu = new MenuFlyoutSubItem { Text = "Opacity" };
        AddOpacityOption(opacitySubMenu, "25%", 0.25);
        AddOpacityOption(opacitySubMenu, "50%", 0.50);
        AddOpacityOption(opacitySubMenu, "75%", 0.75);
        AddOpacityOption(opacitySubMenu, "100%", 1.00);
        contextMenu.Items.Add(opacitySubMenu);

        // 4. Visual Style Submenu
        var styleSubMenu = new MenuFlyoutSubItem { Text = "Visual Style" };
        var glowStyle = new MenuFlyoutItem { Text = "Glow (Vibrant Glass)" };
        glowStyle.Click += async (s, e) => await _viewModel.SetStyleAsync("Glow");
        var minimalStyle = new MenuFlyoutItem { Text = "Minimal (Clean Typography)" };
        minimalStyle.Click += async (s, e) => await _viewModel.SetStyleAsync("Minimal");
        styleSubMenu.Items.Add(glowStyle);
        styleSubMenu.Items.Add(minimalStyle);
        contextMenu.Items.Add(styleSubMenu);

        contextMenu.Items.Add(new MenuFlyoutSeparator());

        // 5. Settings
        var settingsItem = new MenuFlyoutItem { Text = "Settings..." };
        settingsItem.Click += (s, e) => _viewModel.OpenSettings();
        contextMenu.Items.Add(settingsItem);

        // 6. Exit
        var exitItem = new MenuFlyoutItem { Text = "Exit VerciWin" };
        exitItem.Click += (s, e) => _viewModel.Exit();
        contextMenu.Items.Add(exitItem);

        _taskbarIcon.ContextFlyout = contextMenu;

        _viewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(TrayMenuViewModel.TooltipText) && _taskbarIcon != null)
            {
                _taskbarIcon.ToolTipText = _viewModel.TooltipText;
            }
            else if (e.PropertyName == nameof(TrayMenuViewModel.IsOverlayMode))
            {
                toggleModeItem.Text = GetToggleModeText(_viewModel.IsOverlayMode);
            }
        };

        _taskbarIcon.ForceCreate();
    }

    private static string GetToggleModeText(bool isOverlayMode)
    {
        return isOverlayMode ? "🔓 Unlock Position (Drag to Move)" : "🔒 Lock Overlay (Click-Through Mode)";
    }

    private void AddPositionOption(MenuFlyoutSubItem menu, string label, string position)
    {
        var item = new MenuFlyoutItem { Text = label };
        item.Click += async (s, e) => await _viewModel.SetPositionPresetAsync(position);
        menu.Items.Add(item);
    }

    private void AddOpacityOption(MenuFlyoutSubItem menu, string label, double opacity)
    {
        var item = new MenuFlyoutItem { Text = label };
        item.Click += async (s, e) => await _viewModel.SetOpacityAsync(opacity);
        menu.Items.Add(item);
    }

    public void Dispose()
    {
        _taskbarIcon?.Dispose();
        _taskbarIcon = null;
    }
}