# VerciWin — Walkthrough & Implementation Summary

**VerciWin** has been built from scratch as a Windows-native, system-tray-resident, always-on-top, click-through kinetic typography overlay synced to system audio using .NET 8, WinUI 3 (Windows App SDK 2.4.0), Win2D, and GSMTC.

---

## 🏗️ Architecture & Created Components

### 1. Solution & Scaffold (`VerciWin.sln`)
- **[VerciWin.sln](file:///d:/Downloads/myCodes/projects/verci/VerciWin.sln)**: Solution wiring all four projects.
- **[VerciWin.App.csproj](file:///d:/Downloads/myCodes/projects/verci/src/VerciWin.App/VerciWin.App.csproj)**: WinUI 3 unpackaged host application (`win-x64`, self-contained).
- **[app.manifest](file:///d:/Downloads/myCodes/projects/verci/src/VerciWin.App/app.manifest)**: Declares `PerMonitorV2` DPI awareness and Windows 10/11 compatibility GUIDs.
- **[Program.cs](file:///d:/Downloads/myCodes/projects/verci/src/VerciWin.App/Program.cs)**: Custom entry point that manually calls `Bootstrap.Initialize(0x00020004)` before WinUI types instantiate, providing friendly Win32 fallback error dialogs if the runtime is absent.

### 2. Core Domain & Media Sync (`VerciWin.Core`)
- **[PlaybackState.cs](file:///d:/Downloads/myCodes/projects/verci/src/VerciWin.Core/Media/PlaybackState.cs)**: Immutable model representing track title, artist, album, duration, seekable `AlbumArtStream`, and timeline state.
- **[MediaSessionWatcher.cs](file:///d:/Downloads/myCodes/projects/verci/src/VerciWin.Core/Media/MediaSessionWatcher.cs)**: GSMTC wrapper managing active sessions, scrub detection (>2s position jump), 5 Hz drift-correction polling, and sub-frame Stopwatch extrapolation for smooth 60 FPS position queries.
- **[ExecutionStateManager.cs](file:///d:/Downloads/myCodes/projects/verci/src/VerciWin.Core/Power/ExecutionStateManager.cs)**: Windows power management via `SetThreadExecutionState` (`ES_CONTINUOUS | ES_SYSTEM_REQUIRED | ES_DISPLAY_REQUIRED`) engaged during playback and safely released on pause/exit.

### 3. Lyric Processing & Interpolation
- **[LyricModels](file:///d:/Downloads/myCodes/projects/verci/src/VerciWin.Core/Lyrics/Models/)**: `LyricWord`, `LyricLine`, and `LyricDocument` with cached sorted word list for $O(\log n)$ binary search.
- **[LrcParser.cs](file:///d:/Downloads/myCodes/projects/verci/src/VerciWin.Core/Lyrics/LrcParser.cs)**: Parses standard line-level `[mm:ss.xx]` and A2 word-level `<mm:ss.xx>` timestamps with instrumental break preservation.
- **[WordTimingInterpolator.cs](file:///d:/Downloads/myCodes/projects/verci/src/VerciWin.Core/Lyrics/WordTimingInterpolator.cs)**: Character-length-weighted proportional duration distribution with configurable 50 ms inter-word gaps.
- **[LrcLibProvider.cs](file:///d:/Downloads/myCodes/projects/verci/src/VerciWin.Core/Lyrics/LrcLibProvider.cs)**: LRCLIB API client (`/api/get` with `/api/search` fallback) with `User-Agent` identification and `Retry-After` rate-limit handling.
- **[LyricCacheStore.cs](file:///d:/Downloads/myCodes/projects/verci/src/VerciWin.Core/Caching/LyricCacheStore.cs)**: Atomic JSON persistence to `%AppData%/VerciWin/lyrics/`.
- **[LyricService.cs](file:///d:/Downloads/myCodes/projects/verci/src/VerciWin.Core/Lyrics/LyricService.cs)**: Cache-first orchestrator with per-track cancellation tokens.

### 4. Color Palette Extraction
- **[TypographyPalette.cs](file:///d:/Downloads/myCodes/projects/verci/src/VerciWin.Core/Color/TypographyPalette.cs)**: Palette model with `NeutralPalette` dark-glass fallback.
- **[PaletteExtractor.cs](file:///d:/Downloads/myCodes/projects/verci/src/VerciWin.Core/Color/PaletteExtractor.cs)**: Median-cut color quantizer with saturation and mid-lightness preference scoring.

### 5. Hardware-Accelerated Rendering & Transparency
- **[ExtendedWindowStyles.cs](file:///d:/Downloads/myCodes/projects/verci/src/VerciWin.App/Interop/ExtendedWindowStyles.cs)** & **[Win32Interop.cs](file:///d:/Downloads/myCodes/projects/verci/src/VerciWin.App/Interop/Win32Interop.cs)**: P/Invoke signatures for `WS_EX_NOREDIRECTIONBITMAP`, `WS_EX_TRANSPARENT`, `WS_EX_TOOLWINDOW`, DWM frame extensions, and monitor DPI querying.
- **[OverlayWindow.xaml/.cs](file:///d:/Downloads/myCodes/projects/verci/src/VerciWin.App/OverlayWindow.xaml.cs)**: Transparent, click-through overlay with `WM_NCHITTEST` subclassing, dynamic monitor work-area positioning (bottom 30%), and interactive/overlay mode toggling.
- **[LyricCanvasRenderer.cs](file:///d:/Downloads/myCodes/projects/verci/src/VerciWin.App/Rendering/LyricCanvasRenderer.cs)**: Win2D kinetic typography engine with cubic ease-out word scaling ($1.0\times \to 1.15\times$), multi-pass feathered accent glow, 3-line focal zone vertical sliding, and floating ambient background glow.
- **[RenderLoop.cs](file:///d:/Downloads/myCodes/projects/verci/src/VerciWin.App/Rendering/RenderLoop.cs)**: 60 Hz `DispatcherQueueTimer` loop that halts when playback is idle.

### 6. System Tray, Settings & DI Host
- **[SettingsStore.cs](file:///d:/Downloads/myCodes/projects/verci/src/VerciWin.Core/Settings/SettingsStore.cs)**: `%AppData%/VerciWin/settings.json` persistence.
- **[TrayIconManager.cs](file:///d:/Downloads/myCodes/projects/verci/src/VerciWin.App/Tray/TrayIconManager.cs)**: `H.NotifyIcon.WinUI` tray integration with context menus for opacity presets, visual styles, and mode switching.
- **[SettingsWindow.xaml/.cs](file:///d:/Downloads/myCodes/projects/verci/src/VerciWin.App/SettingsWindow.xaml.cs)**: WinUI 3 settings dialog.
- **[App.xaml.cs](file:///d:/Downloads/myCodes/projects/verci/src/VerciWin.App/App.xaml.cs)**: Single-instance mutex, DI container, unhandled exception logging to `%AppData%/VerciWin/logs/error.log`, and event routing.

---

## 🧪 Unit Tests

The test suite in **[VerciWin.Core.Tests](file:///d:/Downloads/myCodes/projects/verci/tests/VerciWin.Core.Tests/)** covers:
- **`WordTimingInterpolatorTests`**: Proportional character weighting, inter-word gap enforcement, single-word lines, and edge cases.
- **`LrcParserTests`**: Standard line timestamps, A2 word-level tags, instrumental breaks, decimal variations, and tag filtering.
- **`LyricCacheStoreTests`**: Atomic round-trip persistence, key sanitization, and corrupt file resilience.

---

## 🚀 Build & Verification Commands

```powershell
# Build entire solution
dotnet build VerciWin.sln

# Run unit tests
dotnet test tests/VerciWin.Core.Tests/VerciWin.Core.Tests.csproj

# Publish unpackaged self-contained executable
dotnet publish src/VerciWin.App/VerciWin.App.csproj -c Release -r win-x64 --self-contained
```
