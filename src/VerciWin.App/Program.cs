using System.Runtime.InteropServices;
using Microsoft.Windows.ApplicationModel.DynamicDependency;

namespace VerciWin.App;

/// <summary>
/// Custom entry point for the unpackaged WinUI 3 app.
/// <para>
/// We call <see cref="Bootstrap.Initialize"/> here — BEFORE any WinUI type is
/// referenced — so that the Windows App SDK runtime DLLs are located and loaded.
/// The default auto-bootstrapper (disabled via WindowsAppSdkBootstrapInitialize=false
/// in the .csproj) runs inside generated code that executes too late for our
/// top-level exception handler to wrap it.
/// </para>
/// </summary>
internal sealed class Program
{
    // WinAppSDK 2.4.x major/minor packed as 0xMMMMmmmm
    private const uint WinAppSdkVersion = 0x00020004;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = false)]
    private static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);

    private const uint MB_ICONERROR = 0x10;
    private const uint MB_OK = 0x00;

    [STAThread]
    static int Main(string[] _)
    {
        // --- Step 1: Initialize the Windows App SDK runtime ---
        // Without this call the app crashes on machines that lack the matching
        // WinAppSDK runtime installation. We must do this before constructing
        // any WinUI or Win2D objects.
        try
        {
            Bootstrap.Initialize(WinAppSdkVersion);
        }
        catch (Exception ex)
        {
            // No XAML is available yet — fall back to a plain Win32 message box.
            MessageBox(
                IntPtr.Zero,
                $"VerciWin requires the Windows App SDK 2.4 runtime.\n\n" +
                $"Please install it from:\n" +
                $"https://learn.microsoft.com/windows/apps/windows-app-sdk/downloads\n\n" +
                $"Technical details: {ex.Message}",
                "VerciWin — Missing Runtime",
                MB_ICONERROR | MB_OK);
            return 1;
        }

        // --- Step 2: Launch the WinUI application ---
        Microsoft.UI.Xaml.Application.Start(_ => new App());

        // --- Step 3: Release the runtime after the message loop exits ---
        // Bootstrap.Shutdown() unloads the WinAppSDK runtime DLLs gracefully.
        Bootstrap.Shutdown();
        return 0;
    }
}
