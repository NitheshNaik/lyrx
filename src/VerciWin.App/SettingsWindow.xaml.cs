using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinRT.Interop;
using VerciWin.ViewModels;

namespace VerciWin.App;

/// <summary>
/// Settings window for configuring VerciWin overlay presentation and behavior.
/// </summary>
public sealed partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _viewModel;
    private bool _isInitializing = true;

    public SettingsWindow(SettingsViewModel viewModel)
    {
        this.InitializeComponent();

        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));

        // Size settings window to 580 x 640
        var hwnd = WindowNative.GetWindowHandle(this);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);
        if (appWindow != null)
        {
            appWindow.Resize(new Windows.Graphics.SizeInt32(580, 640));
            if (appWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.IsResizable = false;
                presenter.IsMaximizable = false;
            }
        }

        // Populate controls from ViewModel
        OpacitySlider.Value = _viewModel.Opacity;
        OpacityValueText.Text = $"{(int)(_viewModel.Opacity * 100)}%";
        OverlayModeSwitch.IsOn = _viewModel.IsOverlayMode;

        // Select Visual Style
        foreach (ComboBoxItem item in StyleComboBox.Items)
        {
            if (item.Tag?.ToString() == _viewModel.VisualStyle)
            {
                StyleComboBox.SelectedItem = item;
                break;
            }
        }
        if (StyleComboBox.SelectedItem == null && StyleComboBox.Items.Count > 0)
            StyleComboBox.SelectedIndex = 0;

        // Select Position
        foreach (ComboBoxItem item in PositionComboBox.Items)
        {
            if (item.Tag?.ToString() == _viewModel.OverlayPosition)
            {
                PositionComboBox.SelectedItem = item;
                break;
            }
        }
        if (PositionComboBox.SelectedItem == null && PositionComboBox.Items.Count > 0)
            PositionComboBox.SelectedIndex = 0;

        _isInitializing = false;
    }

    private void OpacitySlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_isInitializing) return;
        _viewModel.Opacity = e.NewValue;
        if (OpacityValueText != null)
            OpacityValueText.Text = $"{(int)(e.NewValue * 100)}%";
    }

    private void StyleComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing) return;
        if (StyleComboBox.SelectedItem is ComboBoxItem item && item.Tag != null)
        {
            _viewModel.VisualStyle = item.Tag.ToString()!;
        }
    }

    private void PositionComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing) return;
        if (PositionComboBox.SelectedItem is ComboBoxItem item && item.Tag != null)
        {
            _viewModel.OverlayPosition = item.Tag.ToString()!;
        }
    }

    private void OverlayModeSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_isInitializing) return;
        _viewModel.IsOverlayMode = OverlayModeSwitch.IsOn;
    }

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        await _viewModel.SaveAsync();
        this.Close();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        this.Close();
    }
}
