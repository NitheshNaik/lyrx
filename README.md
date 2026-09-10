<div align="center">

<h1>lyrx</h1>

<p><strong>Transparent, GPU-accelerated kinetic typography lyrics, always on top, never in the way.</strong><br/>
System-wide lyric sync for Spotify, YouTube, and Apple Music on Windows.</p>

<br/>

[![License: MIT](https://img.shields.io/badge/License-MIT-blueviolet?style=flat-square)](./LICENSE)
[![Platform](https://img.shields.io/badge/Platform-Windows%2010%2F11-0078D4?style=flat-square&logo=windows&logoColor=white)](https://www.microsoft.com/windows)

</div>

---

<div align="center">

## Built With

[![.NET 8](https://img.shields.io/badge/.NET%208-512BD4?style=flat-square&logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/download/dotnet/8.0)
[![C#](https://img.shields.io/badge/C%23-239120?style=flat-square&logo=csharp&logoColor=white)](https://learn.microsoft.com/dotnet/csharp/)
[![Windows App SDK](https://img.shields.io/badge/Windows%20App%20SDK-0078D4?style=flat-square&logo=windows&logoColor=white)](https://learn.microsoft.com/windows/apps/windows-app-sdk/)
[![WinUI 3](https://img.shields.io/badge/WinUI%203-0063B1?style=flat-square&logo=microsoft&logoColor=white)](https://learn.microsoft.com/windows/apps/winui/winui3/)
[![Win2D](https://img.shields.io/badge/Win2D-00BCF2?style=flat-square&logo=microsoft&logoColor=white)](https://github.com/microsoft/Win2D)
[![CommunityToolkit.Mvvm](https://img.shields.io/badge/CommunityToolkit.Mvvm-9B59B6?style=flat-square&logo=nuget&logoColor=white)](https://learn.microsoft.com/dotnet/communitytoolkit/mvvm/)
[![LRCLIB](https://img.shields.io/badge/LRCLIB%20API-FF6B6B?style=flat-square&logo=musicbrainz&logoColor=white)](https://lrclib.net/)

</div>

---

## Features

- **Kinetic Typography** — Word-by-word animated highlights with cubic ease-out scaling and a focal-zone vertical sliding line.
- **Glass & Glow Aesthetics** — Per-word outer glow and dynamic ambient backdrop gradients, tinted in real-time by album art via **median-cut palette extraction** with saturation weighting.
- **Click-Through Transparency** — Uses Win32 extended window styles (`WS_EX_NOREDIRECTIONBITMAP | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW`) and DWM frame extension for true hardware-accelerated click-through with zero black-window artifacts.
- **System-Wide Audio Sync** — Hooks into the **Windows GSMTC** session manager to automatically capture playback state, title, artist, album art, and timeline properties across any media player.
- **Sub-Frame Smoothness (60 FPS)** — Extrapolates playback position using a local stopwatch clock between 5 Hz GSMTC drift-correction polls; renders via Win2D at 60 Hz and pauses gracefully when music stops.
- **Cache-First Lyric Pipeline** — Integrated with the **LRCLIB** public API; lyrics are cached locally to `%AppData%/VerciWin/lyrics/` as JSON, with character-weighted proportional word-timing interpolation for standard line-level LRC files.
- **Power Management** — Automatically engages `SetThreadExecutionState` during active playback to prevent unwanted display sleep, and safely releases it on pause or exit.
- **System Tray Resident** — Context menu for instant **opacity changes** (25/50/75/100%), **visual style switching** (Glow vs Minimal), **mode toggling** (Overlay vs Normal Draggable Window), and a dedicated settings dialog.

---

## Architecture

```
lyrx/
├── VerciWin.sln
├── src/
│   ├── VerciWin.App/                     # WinUI 3 unpackaged host application (win-x64)
│   │   ├── Program.cs                    # Explicit bootstrapper (Bootstrap.Initialize)
│   │   ├── App.xaml / App.xaml.cs        # Single-instance mutex, DI container, event wiring
│   │   ├── OverlayWindow.xaml/.cs        # Transparent click-through Win2D canvas window
│   │   ├── SettingsWindow.xaml/.cs       # Settings configuration UI
│   │   ├── Interop/
│   │   │   ├── Win32Interop.cs           # P/Invoke signatures (SetWindowLongPtr, DwmExtendFrame…)
│   │   │   └── ExtendedWindowStyles.cs   # Win32 style constants
│   │   ├── Tray/
│   │   │   └── TrayIconManager.cs        # H.NotifyIcon system tray manager
│   │   └── Rendering/
│   │       ├── LyricCanvasRenderer.cs    # Win2D kinetic typography & glow draw loop
│   │       └── RenderLoop.cs             # 60 Hz DispatcherQueueTimer render loop
│   ├── VerciWin.Core/                    # Pure, platform-agnostic business logic (no XAML/WinUI)
│   │   ├── Media/
│   │   │   ├── MediaSessionWatcher.cs    # GSMTC wrapper with position extrapolation & scrub detection
│   │   │   └── PlaybackState.cs          # Immutable playback state model
│   │   ├── Lyrics/
│   │   │   ├── ILyricProvider.cs         # Lyric source abstraction
│   │   │   ├── LrcLibProvider.cs         # LRCLIB API client with User-Agent & Retry-After handling
│   │   │   ├── LyricService.cs           # Cache-first orchestrator
│   │   │   ├── LrcParser.cs              # Standard & A2 word-level LRC parser
│   │   │   └── WordTimingInterpolator.cs # Proportional word-length duration distributor
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
│   └── VerciWin.ViewModels/              # MVVM ViewModels (CommunityToolkit.Mvvm)
│       ├── OverlayViewModel.cs
│       ├── SettingsViewModel.cs
│       └── TrayMenuViewModel.cs
└── tests/
    └── VerciWin.Core.Tests/              # xUnit tests for Parser, Interpolator, and Cache
```

---

## Prerequisites

| Requirement | Detail |
|---|---|
| **OS** | Windows 10 (build 17763+) or Windows 11 (build 22000+) |
| **SDK** | [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) or higher |
| **Runtime** | [Windows App SDK 1.6+](https://learn.microsoft.com/windows/apps/windows-app-sdk/downloads) *(bundled in self-contained publish)* |

---

## Build & Run

### 1. Clone & Build

```bash
git clone https://github.com/NitheshNaik/lyrx.git
cd lyrx
```

```powershell
dotnet build VerciWin.sln
```

### 2. Run Unit Tests

```powershell
dotnet test tests/VerciWin.Core.Tests/VerciWin.Core.Tests.csproj
```

### 3. Publish — Unpackaged Self-Contained Single Exe

```powershell
dotnet publish src/VerciWin.App/VerciWin.App.csproj `
    -c Release -r win-x64 --self-contained
```

> The published binary and assets will be output to:
> `src/VerciWin.App/bin/Release/net8.0-windows10.0.22621.0/win-x64/publish/`

### 4. Launch

```powershell
.\src\VerciWin.App\bin\Release\net8.0-windows10.0.22621.0\win-x64\publish\VerciWin.App.exe
```

---

<div align="center">

Made by **[Nithesh](https://github.com/NitheshNaik)**

</div>
