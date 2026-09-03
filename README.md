# VerciWin

**VerciWin** is a Windows-native, system-tray-resident, always-on-top, click-through kinetic typography overlay that syncs animated lyrics word-by-word to whatever audio is currently playing on Windows (Spotify, YouTube in Chrome/Edge, Apple Music, Tidal, etc.).

Visually inspired by the macOS app **Verci**, built from scratch using Windows-idiomatic APIs: **GSMTC** (Global System Media Transport Controls), **WinUI 3** (Windows App SDK), and **Win2D** (`Microsoft.Graphics.Win2D`).

---

## Key Features

- **Kinetic Typography:** Word-by-word animated highlights, cubic ease-out scaling, and focal-zone vertical line sliding.
- **Glass & Glow Aesthetics:** Per-word outer glow, dynamic ambient backdrop gradients tinted by the active track's album art using median-cut palette extraction with saturation weighting.
- **Click-Through Transparency:** Uses Win32 extended window styles (`WS_EX_NOREDIRECTIONBITMAP | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW`) and DWM frame extension for true hardware-accelerated click-through overlay without black window artifacts.
- **System-Wide Audio Sync:** Hooks into Windows GSMTC session manager to automatically capture playback state, title, artist, album art, and timeline properties across any media player.
- **Sub-Frame Smoothness (60 FPS):** Extrapolates playback position using a local stopwatch clock between 5 Hz GSMTC drift-correction polls; renders with Win2D at 60 Hz and pauses when music stops.
- **Cache-First Lyric Pipeline:** Integrated with the LRCLIB public API, cached locally to `%AppData%/VerciWin/lyrics/` as JSON, with character-weighted proportional word-timing interpolation for standard line-level LRC files.
- **Power Management:** Automatically engages `SetThreadExecutionState` during active playback to prevent unwanted display sleep, and safely releases it on pause or exit.
- **System Tray Resident:** Context menu for instant opacity changes (25/50/75/100%), visual style switching (Glow vs Minimal), mode toggling (Overlay vs Normal Draggable Window), and dedicated settings dialog.

---

## Architecture

```
VerciWin/
├── VerciWin.sln
├── src/
│   ├── VerciWin.App/                     # WinUI 3 unpackaged host application (win-x64)
│   │   ├── Program.cs                    # Explicit Bootstrapper (Bootstrap.Initialize)
│   │   ├── App.xaml / App.xaml.cs        # Single-instance mutex, DI container bootstrap, event wiring
│   │   ├── OverlayWindow.xaml/.cs        # Transparent click-through Win2D canvas window
│   │   ├── SettingsWindow.xaml/.cs       # Settings configuration UI
│   │   ├── Interop/
│   │   │   ├── Win32Interop.cs           # P/Invoke signatures (SetWindowLongPtr, DwmExtendFrame, etc.)
│   │   │   └── ExtendedWindowStyles.cs   # Win32 style constants
│   │   ├── Tray/
│   │   │   └── TrayIconManager.cs        # H.NotifyIcon system tray manager
│   │   └── Rendering/
│   │       ├── LyricCanvasRenderer.cs    # Win2D kinetic typography & glow draw loop
│   │       └── RenderLoop.cs             # 60 Hz DispatcherQueueTimer render loop
│   ├── VerciWin.Core/                    # Pure platform-agnostic business logic (no XAML/WinUI)
│   │   ├── Media/
│   │   │   ├── MediaSessionWatcher.cs    # GSMTC wrapper with position extrapolation & scrub detection
│   │   │   └── PlaybackState.cs          # Immutable playback state model
│   │   ├── Lyrics/
│   │   │   ├── ILyricProvider.cs         # Lyric source abstraction
│   │   │   ├── LrcLibProvider.cs         # LRCLIB API client with User-Agent & Retry-After handling
│   │   │   ├── LyricService.cs           # Cache-first orchestrator
│   │   │   ├── LrcParser.cs              # Standard & A2 word-level LRC parser
│   │   │   ├── WordTimingInterpolator.cs # Proportional word-length duration distributor
│   │   │   └── Models/                   # LyricLine, LyricWord, LyricDocument
│   │   ├── Color/
│   │   │   ├── PaletteExtractor.cs       # Median-cut color quantizer with saturation scoring
│   │   │   └── TypographyPalette.cs      # Palette model & NeutralPalette fallback
│   │   ├── Power/
│   │   │   └── ExecutionStateManager.cs  # SetThreadExecutionState power management
│   │   ├── Settings/
│   │   │   ├── AppSettings.cs            # Settings model
│   │   │   └── SettingsStore.cs          # %AppData%/VerciWin/settings.json store
│   │   └── Caching/
│   │       └── LyricCacheStore.cs        # %AppData%/VerciWin/lyrics/ cache store
│   ├── VerciWin.ViewModels/              # MVVM ViewModels
│   │   ├── OverlayViewModel.cs
│   │   ├── SettingsViewModel.cs
│   │   └── TrayMenuViewModel.cs
└── tests/
    └── VerciWin.Core.Tests/              # xUnit tests for Parser, Interpolator, and Cache
```

---

## Prerequisites

- **OS:** Windows 10 (version 1809+, build 17763) or Windows 11 (build 22000+)
- **SDK:** [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) or higher
- **Runtime:** [Windows App SDK 2.4+ Runtime](https://learn.microsoft.com/windows/apps/windows-app-sdk/downloads) (packaged automatically in self-contained publish)

---

## Build & Run

### 1. Build the Solution
```powershell
dotnet build VerciWin.sln
```

### 2. Run Unit Tests
```powershell
dotnet test tests/VerciWin.Core.Tests/VerciWin.Core.Tests.csproj
```

### 3. Publish Unpackaged Self-Contained Single Exe
```powershell
dotnet publish src/VerciWin.App/VerciWin.App.csproj -c Release -r win-x64 --self-contained
```
The published binary and assets will be located in:
`src/VerciWin.App/bin/Release/net8.0-windows10.0.22621.0/win-x64/publish/`

### 4. Run VerciWin
```powershell
.\src\VerciWin.App\bin\Release\net8.0-windows10.0.22621.0/win-x64/publish/VerciWin.App.exe
```

---

## Manual Verification Checklist

1. **Click-Through Transparency:** Open Notepad or browser behind the overlay and click anywhere in the lyric area — clicks pass through directly to the underlying window.
2. **No Taskbar Presence:** The app runs silently in the system tray; no window icon appears in the Windows Taskbar or Alt+Tab switcher (`WS_EX_TOOLWINDOW`).
3. **Tray Menu Controls:** Right-click the VerciWin icon in the system tray to adjust opacity, toggle between "Glow" and "Minimal" styles, switch window modes, or open Settings.
4. **Always-on-Top:** The overlay remains pinned on top of full-screen and maximized windows while in Overlay Mode (`HWND_TOPMOST`).
5. **Normal Window Mode:** Selecting "Switch to Normal Window" makes the window interactable and repositionable.
6. **Per-Monitor DPI Awareness:** When moving across monitors with different scaling factors (e.g., 100% vs 150%), the overlay dynamically recalculates its physical pixel rectangle.
7. **GSMTC Audio Detection:** Start audio in Spotify, YouTube, or Apple Music — track metadata and album art appear within seconds.
8. **Kinetic Typography Sync:** Lyrics highlight word-by-word with cubic ease-out scaling and vibrant accents matching the album artwork.
9. **Session Handoff:** Switching from Spotify to a YouTube video in Chrome cleanly transitions to the new media session without crashes.
10. **Single-Instance Guard:** Attempting to launch a second instance of `VerciWin.App.exe` exits cleanly without spawning multiple overlays.

---

## License
MIT License.
