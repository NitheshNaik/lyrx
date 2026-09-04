using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using VerciWin.App.Tray;
using VerciWin.Core.Caching;
using VerciWin.Core.Color;
using VerciWin.Core.Lyrics;
using VerciWin.Core.Media;
using VerciWin.Core.Power;
using VerciWin.Core.Settings;
using VerciWin.ViewModels;

namespace VerciWin.App;

/// <summary>
/// VerciWin Application Host.
/// Manages single-instance mutex, DI container bootstrap, top-level exception logging,
/// and end-to-end event orchestration between GSMTC, Lyrics, Palette, and Overlay Window.
/// </summary>
public partial class App : Application
{
    private static Mutex? _singleInstanceMutex;
    private IServiceProvider? _serviceProvider;

    private OverlayWindow? _overlayWindow;
    private SettingsWindow? _settingsWindow;
    private TrayIconManager? _trayIconManager;

    private CancellationTokenSource? _currentTrackCts;

    public App()
    {
        this.InitializeComponent();

        // 1. Unhandled Exception Logging
        SetupGlobalExceptionLogging();

        // 2. DI Container Setup
        ConfigureServices();
    }

    private void SetupGlobalExceptionLogging()
    {
        UnhandledException += (sender, e) =>
        {
            try
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string logDir = Path.Combine(appData, "VerciWin", "logs");
                Directory.CreateDirectory(logDir);
                string logPath = Path.Combine(logDir, "error.log");

                string logEntry = $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff UTC}] Unhandled Exception:\n" +
                                 $"{e.Message}\n{e.Exception}\n\n";

                File.AppendAllText(logPath, logEntry);
            }
            catch
            {
                // Fallback: cannot write log
            }

            e.Handled = true; // Prevent application crash when recoverable
        };
    }

    private void ConfigureServices()
    {
        var services = new ServiceCollection();

        // HTTP Client for LRCLIB with required User-Agent header
        services.AddHttpClient<ILyricProvider, LrcLibProvider>(client =>
        {
            client.BaseAddress = new Uri("https://lrclib.net");
            client.DefaultRequestHeaders.Add("User-Agent", "VerciWin/1.0 (https://github.com/verciwin)");
            client.Timeout = TimeSpan.FromSeconds(8);
        });

        // Core platform-agnostic services
        services.AddSingleton<LrcParser>();
        services.AddSingleton<WordTimingInterpolator>();
        services.AddSingleton<LyricCacheStore>();
        services.AddSingleton<LyricService>();
        services.AddSingleton<PaletteExtractor>();
        services.AddSingleton<ExecutionStateManager>();
        services.AddSingleton<SettingsStore>();
        services.AddSingleton<MediaSessionWatcher>();

        // ViewModels
        services.AddSingleton<OverlayViewModel>();
        services.AddSingleton<TrayMenuViewModel>();
        services.AddTransient<SettingsViewModel>();

        // Tray
        services.AddSingleton<TrayIconManager>();

        _serviceProvider = services.BuildServiceProvider();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // 1. Single-Instance Guard (Moved here so XAML can initialize safely first)
        _singleInstanceMutex = new Mutex(true, "VerciWin_SingleInstance_Mutex_9A1F", out bool createdNew);
        if (!createdNew)
        {
            Debug.WriteLine("[App] Another instance of VerciWin is already running. Exiting.");
            Environment.Exit(0);
            return;
        }

        if (_serviceProvider == null) return;

        var overlayViewModel = _serviceProvider.GetRequiredService<OverlayViewModel>();
        var trayViewModel = _serviceProvider.GetRequiredService<TrayMenuViewModel>();
        var mediaWatcher = _serviceProvider.GetRequiredService<MediaSessionWatcher>();

        // 2. CREATE WINDOW SYNCHRONOUSLY FIRST (Fixes 0xc0000602 Crash)
        _overlayWindow = new OverlayWindow(overlayViewModel, () => mediaWatcher.GetCurrentPosition());
        _overlayWindow.Activate();

        // 3. Initialize System Tray
        _trayIconManager = _serviceProvider.GetRequiredService<TrayIconManager>();
        _trayIconManager.Initialize();

        // 4. Handle Tray menu events
        trayViewModel.OpenSettingsRequested += (s, e) =>
        {
            if (_settingsWindow == null)
            {
                var settingsVm = _serviceProvider.GetRequiredService<SettingsViewModel>();
                _settingsWindow = new SettingsWindow(settingsVm);
                _settingsWindow.Closed += (sw, ea) => _settingsWindow = null;
            }
            _settingsWindow.Activate();
        };

        trayViewModel.ExitRequested += (s, e) =>
        {
            ExitApplication();
        };

        // 5. Kick off async initialization in the background
        _ = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        if (_serviceProvider == null || _overlayWindow == null) return;

        var settingsStore = _serviceProvider.GetRequiredService<SettingsStore>();
        var overlayViewModel = _serviceProvider.GetRequiredService<OverlayViewModel>();
        var trayViewModel = _serviceProvider.GetRequiredService<TrayMenuViewModel>();
        var mediaWatcher = _serviceProvider.GetRequiredService<MediaSessionWatcher>();
        var lyricService = _serviceProvider.GetRequiredService<LyricService>();
        var paletteExtractor = _serviceProvider.GetRequiredService<PaletteExtractor>();
        var powerManager = _serviceProvider.GetRequiredService<ExecutionStateManager>();

        // Load settings asynchronously
        var settings = await settingsStore.LoadAsync();

        // Apply settings back on the UI thread
        _overlayWindow.DispatcherQueue.TryEnqueue(() =>
        {
            overlayViewModel.Opacity = settings.Opacity;
            overlayViewModel.VisualStyle = settings.VisualStyle;
            overlayViewModel.IsOverlayMode = settings.IsOverlayMode;
            overlayViewModel.OverlayPosition = settings.OverlayPosition;
        });

        // Wire GSMTC StateChanged events
        mediaWatcher.StateChanged += (sender, state) =>
        {
            _currentTrackCts?.Cancel();
            _currentTrackCts?.Dispose();
            _currentTrackCts = new CancellationTokenSource();
            var ct = _currentTrackCts.Token;

            _overlayWindow.DispatcherQueue.TryEnqueue(async () =>
            {
                overlayViewModel.CurrentState = state;
                trayViewModel.UpdateTooltip(state);

                // Sleep prevention
                if (!state.IsPaused && !state.IsEmpty) powerManager.Engage();
                else powerManager.Release();

                if (state.IsEmpty)
                {
                    overlayViewModel.CurrentLyrics = null;
                    overlayViewModel.Palette = TypographyPalette.NeutralPalette;
                    return;
                }

                // Extract palette
                try
                {
                    var palette = await paletteExtractor.ExtractAsync(state.AlbumArtStream, ct);
                    if (!ct.IsCancellationRequested) overlayViewModel.Palette = palette;
                }
                catch (OperationCanceledException) { }

                // Fetch lyrics
                try
                {
                    var lyrics = await lyricService.GetLyricsAsync(
                        state.Title, state.Artist, state.Album, state.EndTime, ct);

                    if (!ct.IsCancellationRequested) overlayViewModel.CurrentLyrics = lyrics;
                }
                catch (OperationCanceledException) { }
            });
        };

        // Start GSMTC monitoring
        await mediaWatcher.InitializeAsync();
    }

    private void ExitApplication()
    {
        _currentTrackCts?.Cancel();
        _serviceProvider?.GetService<ExecutionStateManager>()?.Dispose();
        _serviceProvider?.GetService<MediaSessionWatcher>()?.Dispose();
        _trayIconManager?.Dispose();
        _settingsWindow?.Close();
        _overlayWindow?.CloseWindow();

        if (_singleInstanceMutex != null)
        {
            try { _singleInstanceMutex.ReleaseMutex(); } catch { }
            _singleInstanceMutex.Dispose();
            _singleInstanceMutex = null;
        }

        Environment.Exit(0);
    }
}