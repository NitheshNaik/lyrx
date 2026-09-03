namespace VerciWin.App.Interop;

/// <summary>
/// Constants for Win32 window styles, extended styles, hit-testing, and positioning.
/// </summary>
public static class ExtendedWindowStyles
{
    // Window Long offsets
    public const int GWL_STYLE = -16;
    public const int GWL_EXSTYLE = -20;

    // Extended Window Styles (WS_EX_*)
    public const uint WS_EX_TRANSPARENT = 0x00000020;
    public const uint WS_EX_TOOLWINDOW = 0x00000080;
    public const uint WS_EX_TOPMOST = 0x00000008;
    public const uint WS_EX_LAYERED = 0x00080000;
    public const uint WS_EX_NOREDIRECTIONBITMAP = 0x00200000;

    // Window Styles (WS_*)
    public const uint WS_POPUP = 0x80000000;
    public const uint WS_VISIBLE = 0x10000000;
    public const uint WS_CAPTION = 0x00C00000;
    public const uint WS_THICKFRAME = 0x00040000;
    public const uint WS_MINIMIZEBOX = 0x00020000;
    public const uint WS_MAXIMIZEBOX = 0x00010000;

    // Window Messages
    public const uint WM_NCHITTEST = 0x0084;
    public const uint WM_ACTIVATE = 0x0006;
    public const uint WM_DISPLAYCHANGE = 0x007E;
    public const uint WM_DPICHANGED = 0x02E0;

    // Hit Test return values
    public const nint HTTRANSPARENT = -1;
    public const nint HTNOWHERE = 0;
    public const nint HTCLIENT = 1;
    public const nint HTCAPTION = 2;

    // SetWindowPos Flags
    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_NOMOVE = 0x0002;
    public const uint SWP_NOZORDER = 0x0004;
    public const uint SWP_NOREDRAW = 0x0008;
    public const uint SWP_NOACTIVATE = 0x0010;
    public const uint SWP_FRAMECHANGED = 0x0020;
    public const uint SWP_SHOWWINDOW = 0x0040;
    public const uint SWP_HIDEWINDOW = 0x0080;

    // HWND Z-Order Positions
    public static readonly IntPtr HWND_TOPMOST = new(-1);
    public static readonly IntPtr HWND_NOTOPMOST = new(-2);
    public static readonly IntPtr HWND_TOP = new(0);
    public static readonly IntPtr HWND_BOTTOM = new(1);

    // Monitor Constants
    public const uint MONITOR_DEFAULTTONULL = 0;
    public const uint MONITOR_DEFAULTTOPRIMARY = 1;
    public const uint MONITOR_DEFAULTTONEAREST = 2;

    // DPI Types
    public const int MDT_EFFECTIVE_DPI = 0;
    public const int MDT_ANGULAR_DPI = 1;
    public const int MDT_RAW_DPI = 2;
    public const int MDT_DEFAULT = MDT_EFFECTIVE_DPI;

    // DWM Window Attributes
    public const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    public const int DWMWCP_DONOTROUND = 1;
    public const int DWMWCP_ROUND = 2;
    public const int DWMWCP_ROUNDSMALL = 3;
}
