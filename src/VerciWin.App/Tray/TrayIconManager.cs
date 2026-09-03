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
            ToolTipText = _viewModel.TooltipText
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

        // 1. Toggle Mode
        var toggleModeItem = new MenuFlyoutItem
        {
            Text = _viewModel.IsOverlayMode ? "Switch to Normal Window" : "Switch to Overlay Mode"
        };
        toggleModeItem.Click += async (s, e) =>
        {
            await _viewModel.ToggleOverlayModeAsync();
            toggleModeItem.Text = _viewModel.IsOverlayMode ? "Switch to Normal Window" : "Switch to Overlay Mode";
        };
        contextMenu.Items.Add(toggleModeItem);

        contextMenu.Items.Add(new MenuFlyoutSeparator());

        // 2. Opacity Submenu
        var opacitySubMenu = new MenuFlyoutSubItem { Text = "Opacity" };
        AddOpacityOption(opacitySubMenu, "25%", 0.25);
        AddOpacityOption(opacitySubMenu, "50%", 0.50);
        AddOpacityOption(opacitySubMenu, "75%", 0.75);
        AddOpacityOption(opacitySubMenu, "100%", 1.00);
        contextMenu.Items.Add(opacitySubMenu);

        // 3. Visual Style Submenu
        var styleSubMenu = new MenuFlyoutSubItem { Text = "Visual Style" };
        var glowStyle = new MenuFlyoutItem { Text = "Glow (Vibrant Glass)" };
        glowStyle.Click += async (s, e) => await _viewModel.SetStyleAsync("Glow");
        var minimalStyle = new MenuFlyoutItem { Text = "Minimal (Clean Typography)" };
        minimalStyle.Click += async (s, e) => await _viewModel.SetStyleAsync("Minimal");
        styleSubMenu.Items.Add(glowStyle);
        styleSubMenu.Items.Add(minimalStyle);
        contextMenu.Items.Add(styleSubMenu);

        contextMenu.Items.Add(new MenuFlyoutSeparator());

        // 4. Settings
        var settingsItem = new MenuFlyoutItem { Text = "Settings..." };
        settingsItem.Click += (s, e) => _viewModel.OpenSettings();
        contextMenu.Items.Add(settingsItem);

        // 5. Exit
        var exitItem = new MenuFlyoutItem { Text = "Exit VerciWin" };
        exitItem.Click += (s, e) => _viewModel.Exit();
        contextMenu.Items.Add(exitItem);

        _taskbarIcon.ContextFlyout = contextMenu;

        // Double click to toggle settings
        // _taskbarIcon.LeftClick += (s, e) =>
        // {
        //     // Left click can open settings or toggle mode
        // };

        _viewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(TrayMenuViewModel.TooltipText) && _taskbarIcon != null)
            {
                _taskbarIcon.ToolTipText = _viewModel.TooltipText;
            }
            else if (e.PropertyName == nameof(TrayMenuViewModel.IsOverlayMode))
            {
                toggleModeItem.Text = _viewModel.IsOverlayMode ? "Switch to Normal Window" : "Switch to Overlay Mode";
            }
        };

        _taskbarIcon.ForceCreate();
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