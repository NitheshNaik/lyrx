using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using WinRT.Interop;
using VerciWin.App.Interop;
using VerciWin.App.Rendering;
using VerciWin.ViewModels;

namespace VerciWin.App;

/// <summary>
/// Always-on-top, click-through kinetic typography overlay window backed by Win2D.
/// </summary>
public sealed partial class OverlayWindow : Window
{
    private readonly OverlayViewModel _viewModel;
    private readonly LyricCanvasRenderer _renderer;
    private readonly RenderLoop _renderLoop;

    private IntPtr _hwnd = IntPtr.Zero;
    private AppWindow? _appWindow;
    private Win32Interop.SubclassProc? _subclassProc;
    private bool _isSubclassed;

    public OverlayWindow(OverlayViewModel viewModel, Func<TimeSpan> positionProvider)
    {
        this.InitializeComponent();

        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _renderer = new LyricCanvasRenderer(_viewModel, positionProvider);

        // Render loop ticks at 60 Hz on the UI DispatcherQueue
        _renderLoop = new RenderLoop(this.DispatcherQueue, () =>
        {
            if (LyricCanvas != null && LyricCanvas.IsLoaded)
            {
                LyricCanvas.Invalidate();
            }
        });

        // Listen for window mode toggles & position preset changes
        _viewModel.ModeChanged += (s, isOverlay) =>
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                if (isOverlay)
                    ApplyOverlayMode();
                else
                    ApplyInteractiveMode();
            });
        };

        _viewModel.PositionChanged += (s, pos) =>
        {
            DispatcherQueue.TryEnqueue(PositionOverlayWindow);
        };

        // Initialize HWND and window chrome
        InitializeWindow();
    }

    private void InitializeWindow()
    {
        _hwnd = WindowNative.GetWindowHandle(this);
        var windowId = Win32InteropWrapper.GetWindowIdFromWindow(_hwnd);
        _appWindow = AppWindow.GetFromWindowId(windowId);

        // Customize AppWindow title bar
        if (_appWindow != null)
        {
            _appWindow.Title = "VerciWin Overlay";
            if (_appWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.IsResizable = false;
                presenter.IsMinimizable = false;
                presenter.IsMaximizable = false;
                presenter.SetBorderAndTitleBar(hasBorder: false, hasTitleBar: false);
            }

            _appWindow.Changed += (s, e) =>
            {
                if (e.DidDisplayChange)
                {
                    PositionOverlayWindow();
                }
            };
        }

        // Subclass window for WM_NCHITTEST click-through filtering
        _subclassProc = new Win32Interop.SubclassProc(WindowSubclassProc);
        _isSubclassed = Win32Interop.SetWindowSubclass(_hwnd, _subclassProc, 1, 0);

        // Set DWM corner preference (sharp edges for seamless borderless overlay)
        int cornerPref = ExtendedWindowStyles.DWMWCP_DONOTROUND;
        Win32Interop.DwmSetWindowAttribute(
            _hwnd,
            ExtendedWindowStyles.DWMWA_WINDOW_CORNER_PREFERENCE,
            ref cornerPref,
            sizeof(int));

        // Extend DWM frame for true transparent backdrop
        var margins = Win32Interop.MARGINS.FullWindow;
        Win32Interop.DwmExtendFrameIntoClientArea(_hwnd, ref margins);

        // Apply initial overlay styling and positioning
        ApplyOverlayMode();
        PositionOverlayWindow();

        // Start render loop
        _renderLoop.Start();
    }

    private void ApplyOverlayMode()
    {
        if (_hwnd == IntPtr.Zero) return;

        nint exStyle = Win32Interop.GetWindowLongPtr(_hwnd, ExtendedWindowStyles.GWL_EXSTYLE);

        // Overlay Mode: Click-through (WS_EX_TRANSPARENT), ToolWindow (no taskbar icon), NoRedirectionBitmap for DirectComposition alpha
        exStyle |= (nint)(ExtendedWindowStyles.WS_EX_NOREDIRECTIONBITMAP |
                          ExtendedWindowStyles.WS_EX_TRANSPARENT |
                          ExtendedWindowStyles.WS_EX_TOOLWINDOW);

        Win32Interop.SetWindowLongPtr(_hwnd, ExtendedWindowStyles.GWL_EXSTYLE, exStyle);

        // Always On Top
        Win32Interop.SetWindowPos(
            _hwnd,
            ExtendedWindowStyles.HWND_TOPMOST,
            0, 0, 0, 0,
            ExtendedWindowStyles.SWP_NOMOVE | ExtendedWindowStyles.SWP_NOSIZE | ExtendedWindowStyles.SWP_NOACTIVATE | ExtendedWindowStyles.SWP_SHOWWINDOW);
    }

    private void ApplyInteractiveMode()
    {
        if (_hwnd == IntPtr.Zero) return;

        nint exStyle = Win32Interop.GetWindowLongPtr(_hwnd, ExtendedWindowStyles.GWL_EXSTYLE);

        // Normal/Interactive Mode: Remove click-through style so user can interact/move
        exStyle &= ~(nint)ExtendedWindowStyles.WS_EX_TRANSPARENT;
        exStyle &= ~(nint)ExtendedWindowStyles.WS_EX_TOOLWINDOW;

        Win32Interop.SetWindowLongPtr(_hwnd, ExtendedWindowStyles.GWL_EXSTYLE, exStyle);

        // Place below topmost windows
        Win32Interop.SetWindowPos(
            _hwnd,
            ExtendedWindowStyles.HWND_NOTOPMOST,
            0, 0, 0, 0,
            ExtendedWindowStyles.SWP_NOMOVE | ExtendedWindowStyles.SWP_NOSIZE | ExtendedWindowStyles.SWP_SHOWWINDOW);
    }

    private void PositionOverlayWindow()
    {
        if (_hwnd == IntPtr.Zero) return;

        IntPtr hMonitor = Win32Interop.MonitorFromWindow(_hwnd, ExtendedWindowStyles.MONITOR_DEFAULTTONEAREST);
        var monitorInfo = new Win32Interop.MONITORINFO
        {
            cbSize = (uint)Marshal.SizeOf<Win32Interop.MONITORINFO>()
        };

        if (!Win32Interop.GetMonitorInfo(hMonitor, ref monitorInfo))
            return;

        var workArea = monitorInfo.rcWork;
        int screenWidth = workArea.Width;
        int screenHeight = workArea.Height;

        int overlayWidth = screenWidth;
        int overlayHeight;
        int overlayX = workArea.left;
        int overlayY;

        switch (_viewModel.OverlayPosition)
        {
            case "Center":
                overlayHeight = (int)(screenHeight * 0.40f);
                overlayY = workArea.top + (screenHeight - overlayHeight) / 2;
                break;

            case "FullScreen":
                overlayHeight = screenHeight;
                overlayY = workArea.top;
                break;

            case "LowerThird":
            default:
                // Bottom 30% of monitor work area
                overlayHeight = (int)(screenHeight * 0.30f);
                overlayY = workArea.bottom - overlayHeight;
                break;
        }

        Win32Interop.MoveWindow(_hwnd, overlayX, overlayY, overlayWidth, overlayHeight, bRepaint: true);
    }

    private nint WindowSubclassProc(IntPtr hWnd, uint uMsg, nuint wParam, nint lParam, nuint uIdSubclass, nuint dwRefData)
    {
        if (uMsg == ExtendedWindowStyles.WM_NCHITTEST)
        {
            // When in overlay mode, return HTTRANSPARENT to pass all mouse clicks to windows below
            if (_viewModel.IsOverlayMode)
            {
                return ExtendedWindowStyles.HTTRANSPARENT;
            }
        }
        else if (uMsg == ExtendedWindowStyles.WM_DPICHANGED || uMsg == ExtendedWindowStyles.WM_DISPLAYCHANGE)
        {
            PositionOverlayWindow();
        }

        return Win32Interop.DefSubclassProc(hWnd, uMsg, wParam, lParam);
    }

    // ── Win2D Canvas Handlers ────────────────────────────────────────────────

    private void LyricCanvas_CreateResources(CanvasControl sender, Microsoft.Graphics.Canvas.UI.CanvasCreateResourcesEventArgs args)
    {
        _renderer.CreateResources(sender);
    }

    private void LyricCanvas_Draw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        _renderer.Draw(sender, args);
    }

    public void CloseWindow()
    {
        _renderLoop.Dispose();
        _renderer.Dispose();
        if (_isSubclassed && _hwnd != IntPtr.Zero && _subclassProc != null)
        {
            Win32Interop.RemoveWindowSubclass(_hwnd, _subclassProc, 1);
            _isSubclassed = false;
        }
        this.Close();
    }
}

internal static class Win32InteropWrapper
{
    public static Microsoft.UI.WindowId GetWindowIdFromWindow(IntPtr hwnd)
    {
        return Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
    }
}
