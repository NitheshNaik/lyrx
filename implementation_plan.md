# VerciWin — Implementation Plan (Revised)

A Windows-native, system-tray-resident, always-on-top, click-through kinetic typography overlay that syncs word-by-word animated lyrics to currently playing audio using GSMTC, WinUI 3, and Win2D.

---

## Verified API Research & Key Technical Findings

### LRCLIB API (verified via live call)

**Base URL:** `https://lrclib.net`

**`GET /api/get`** — Best-match lookup
- Required params: `track_name`, `artist_name`
- Recommended params: `album_name`, `duration` (seconds, float)
- Response fields (confirmed from live call — `GET /api/get?track_name=Bohemian+Rhapsody&artist_name=Queen`):
  ```json
  {
    "id": 19079,
    "name": "...",
    "trackName": "...",
    "artistName": "...",
    "albumName": "...",
    "duration": 355.0,
    "instrumental": false,
    "plainLyrics": "line1\nline2\n...",
    "syncedLyrics": "[mm:ss.xx] line text\n...",
    "lyricsfile": "..."
  }
  ```
- `syncedLyrics`: standard LRC format (`[mm:ss.xx]` per-line timestamps) — **LRCLIB provides line-level timing only**; no word-level inline tags were observed in this or other test calls. `WordTimingInterpolator` will always run on LRCLIB data.
- **`GET /api/search`** — fuzzy search fallback: params `track_name` + `artist_name`, or free-text `q`.
- **Auth:** None.
- **Required Header:** `User-Agent: VerciWin/1.0 (https://github.com/verciwin)` — LRCLIB requires this per their API documentation.
- **Rate limiting:** VerciWin will self-impose a minimum 300ms gap between outbound LRCLIB requests as a conservative default (not a published LRCLIB constraint). The `Retry-After` header on HTTP 429 will be honoured — that _is_ a confirmed API behaviour.

### Win2D in WinUI 3 (unpackaged) — Documented Deviations

> [!IMPORTANT]
> **Deviation 1 — `CanvasAnimatedControl` not supported in WinUI 3.**
> It was UWP-only and has not been ported to the Windows App SDK. Using `CanvasControl` + `DispatcherQueueTimer` at 60Hz instead. Timer is stopped entirely when playback is paused or no session is active.
>
> **Deviation 2 — `CompositionTarget.Rendering` not recommended in WinUI 3.**
> WinUI 3 uses `Microsoft.UI.Composition`; `CompositionTarget.Rendering` is effectively deprecated. `DispatcherQueueTimer` is the correct replacement.
>
> If `CanvasControl`+timer proves CPU-intensive under profiling, the escalation path is `CanvasSwapChain` on a dedicated background render thread — this is documented but not pre-built.

### Transparency & Click-Through — Revised Approach

> [!IMPORTANT]
> **The original plan (`WS_EX_LAYERED + SetLayeredWindowAttributes`) has been replaced.**
>
> WinUI 3 renders through DirectComposition/XAML islands. Stacking legacy GDI-level `WS_EX_LAYERED` on the same HWND creates two compositing systems fighting over the surface — known failure modes include black window content and broken hit-testing. This must not be assumed to work without explicit verification.
>
> **New approach (Step 7a — Spike):**
> 1. `SystemBackdrop = null`, root XAML `Background = "Transparent"`
> 2. Apply `WS_EX_NOREDIRECTIONBITMAP` — tells DWM not to allocate a redirection bitmap, allowing the WinUI DirectComposition surface to own its own per-pixel alpha
> 3. Apply `WS_EX_TRANSPARENT` (without `WS_EX_LAYERED`) for click-passthrough via DWM's normal hit-test routing
> 4. Subclass the HWND via `SetWindowSubclass` to intercept `WM_NCHITTEST` and return `HTTRANSPARENT` — belt-and-suspenders on top of the style flag
>
> **Two outcomes from the spike, each with a documented path:**
> - ✅ **Expected outcome:** Win2D's `CanvasControl` draws with per-pixel alpha natively (it is a DirectComposition surface), glow effects are achieved via `CanvasBlurEffect` inside the draw session, window is visually transparent and click-through. No `UpdateLayeredWindow` needed.
> - ❌ **Fallback if Win2D alpha doesn't composite through correctly:** The glow background must live entirely inside Win2D's own alpha channel (no window-level `UpdateLayeredWindow` — that path is a raw DIB incompatible with hosted XAML). This means the background gradient/glow is drawn as Win2D geometry with explicit alpha values rather than relying on window-level blending. Flag this explicitly in code comments and in this plan before Step 8 is started.

### GSMTC Position Polling Strategy

`TimelinePropertiesChanged` fires on seek/play/pause, not every tick. Strategy:
- Subscribe to `TimelinePropertiesChanged` for authoritative `Position` anchors + seek detection (delta > 2s between reported and extrapolated = scrub).
- Maintain a local `Stopwatch` extrapolation: `effectivePosition = lastAnchorPosition + stopwatch.Elapsed * playbackRate`.
- Poll raw GSMTC `GetTimelineProperties().Position` at 5Hz (200ms) as drift-correction; update anchor on each poll.
- Render loop queries `GetCurrentPosition()` at 60fps using extrapolation — no GSMTC call per frame.

### Windows App SDK Bootstrapper — Explicit Requirement

> [!IMPORTANT]
> Self-contained (`WindowsAppSDKSelfContained: true`) does NOT eliminate the bootstrapper requirement. The WinAppSDK runtime libraries are native DLLs that are not bundled into the managed exe — they are extracted to a side-by-side directory at runtime. Without `Bootstrap.Initialize()`, the app will fail on machines that don't have the matching WinAppSDK runtime installed.
>
> **Implementation (Step 1):**
> - Set `<WindowsAppSdkBootstrapInitialize>false</WindowsAppSdkBootstrapInitialize>` in `.csproj` to disable the auto-initializer (which runs too late for unhandled exception wiring).
> - Add a `Program.cs` with an explicit `[STAThread] static int Main(string[] args)` entry point.
> - Call `Bootstrap.Initialize(majorMinorVersion: 0x00020004)` (matching SDK 2.4.x) **before** `Application.Start`.
> - On `Bootstrap.Initialize` failure: show a `MessageBox` (Win32 P/Invoke — no XAML available yet) explaining the missing runtime, then exit cleanly.
> - Re-verify bootstrapper call sequence in Step 11 after DI wiring is in place.

### Tray Icon — H.NotifyIcon.WinUI

Chosen over raw `Shell_NotifyIcon` P/Invoke. See Tradeoffs section.

### Palette Extraction — Median Cut with Null Art Handling

> [!NOTE]
> **Null album art:** GSMTC's `Thumbnail` property is `null` for some sources (some browser tabs, apps that don't report artwork). `PaletteExtractor` must handle this without throwing.
>
> A static `NeutralPalette` constant is defined:
> ```
> NeutralPalette = {
>   Primary:        #E8E8F0   (cool near-white)
>   Accent:         #9090C8   (muted periwinkle)
>   GlowBackground: #0D0D1A   (deep dark navy)
> }
> ```
> `PaletteExtractor.ExtractAsync(Stream? artStream)` returns `NeutralPalette` immediately when `artStream` is `null` and logs a debug trace. Callers never need to null-check the return value.

---

## NuGet Package List

| Package | Version | Project(s) | Purpose |
|---|---|---|---|
| `Microsoft.WindowsAppSDK` | `2.4.0` | App | WinUI 3 host |
| `Microsoft.Graphics.Win2D` | `1.3.0` | App | Canvas rendering |
| `CommunityToolkit.Mvvm` | `8.4.0` | App, ViewModels | MVVM infrastructure |
| `H.NotifyIcon.WinUI` | `2.2.0` | App | System tray icon |
| `Microsoft.Extensions.DependencyInjection` | `8.0.1` | App | DI container |
| `Microsoft.Extensions.Http` | `8.0.1` | Core | `HttpClient` factory |
| `System.Text.Json` | `8.0.5` | Core | JSON serialization |
| `xunit` | `2.9.0` | Tests | Unit testing |
| `xunit.runner.visualstudio` | `2.8.2` | Tests | VS test runner |
| `Microsoft.NET.Test.Sdk` | `17.11.1` | Tests | Test SDK |

> [!NOTE]
> `Windows.Media.Control` (GSMTC) is part of the Windows Runtime — no separate NuGet package. All projects target `net8.0-windows10.0.22621.0` to enable WinRT API access.

---

## Open Questions

None blocking. All items from the prior review have been resolved above.

> [!NOTE]
> **Display positioning default:** The overlay defaults to spanning the full screen width at the bottom 30% of the primary monitor. This is configurable per-monitor in `SettingsWindow`. DPI-correct positioning is addressed in Step 7b.

---

## Proposed Changes (Revised Build Order — 12 Commits)

---

### Step 1 — Solution Scaffold + Bootstrapper

**Files created:**

#### [NEW] `VerciWin.sln`
Solution wiring all projects.

#### [NEW] `src/VerciWin.App/VerciWin.App.csproj`
```xml
<OutputType>WinExe</OutputType>
<TargetFramework>net8.0-windows10.0.22621.0</TargetFramework>
<WindowsPackageType>None</WindowsPackageType>
<WindowsAppSDKSelfContained>true</WindowsAppSDKSelfContained>
<WindowsAppSdkBootstrapInitialize>false</WindowsAppSdkBootstrapInitialize>
<!-- Disabled: we call Bootstrap.Initialize manually in Program.cs for early error handling -->
<SelfContained>true</SelfContained>
<RuntimeIdentifier>win-x64</RuntimeIdentifier>
```

#### [NEW] `src/VerciWin.App/Program.cs`
Custom entry point — **must be set as `<StartupObject>` in `.csproj`**.
```csharp
[STAThread]
static int Main(string[] args)
{
    // Initialize Windows App SDK runtime BEFORE any WinUI types are used.
    // majorMinorVersion 0x00020004 = SDK 2.4.x
    try { Bootstrap.Initialize(0x00020004); }
    catch (Exception ex)
    {
        // No XAML available yet — use Win32 MessageBox
        MessageBox(IntPtr.Zero,
            $"VerciWin requires the Windows App SDK 2.4 runtime.\n\n{ex.Message}",
            "VerciWin — Missing Runtime", MB_ICONERROR);
        return 1;
    }
    Application.Start(_ => new App());
    Bootstrap.Shutdown();
    return 0;
}
```

#### [NEW] `src/VerciWin.Core/VerciWin.Core.csproj`
`net8.0-windows10.0.22621.0`, no XAML/WinUI refs.

#### [NEW] `src/VerciWin.ViewModels/VerciWin.ViewModels.csproj`
`net8.0-windows10.0.22621.0`, refs Core + CommunityToolkit.Mvvm.

#### [NEW] `tests/VerciWin.Core.Tests/VerciWin.Core.Tests.csproj`
`net8.0-windows10.0.22621.0`, refs Core + xunit.

#### [NEW] `src/VerciWin.App/app.manifest`
Per-monitor DPI awareness declaration (required here, used by Step 7b):
```xml
<dpiAwareness xmlns="http://schemas.microsoft.com/SMI/2016/WindowsSettings">PerMonitorV2</dpiAwareness>
<dpiAware xmlns="http://schemas.microsoft.com/SMI/2005/WindowsSettings">true</dpiAware>
```
Also includes Windows 10/11 compatibility GUIDs.

#### [NEW] `src/VerciWin.App/App.xaml` / `App.xaml.cs`
Minimal — single-instance mutex, DI container setup deferred to Step 11. At Step 1, just confirms blank WinUI window opens.

**Commit 1 goal:** `dotnet run` opens a blank WinUI 3 window. Bootstrapper confirmed working.

---

### Step 2 — MediaSessionWatcher

#### [NEW] `src/VerciWin.Core/Media/PlaybackState.cs`
```
PlaybackState {
  string Title, Artist, Album, SourceAppId
  Stream? AlbumArtStream           // null when GSMTC provides no thumbnail
  TimeSpan Position, EndTime
  double PlaybackRate
  bool IsPaused, HasTimeline
}
```

#### [NEW] `src/VerciWin.Core/Media/MediaSessionWatcher.cs`
- `RequestAsync()` → binds to `GetCurrentSession()`
- `SessionsChanged` → rebinds session
- `TimelinePropertiesChanged` → anchor update + seek detection
- `PlaybackInfoChanged` → rate/pause state
- `MediaPropertiesChanged` → metadata + art
- Stopwatch extrapolation; `GetCurrentPosition()` callable at 60fps
- 5Hz drift-correction `DispatcherQueueTimer`
- Exposes `event EventHandler<PlaybackState> StateChanged`
- Degrades gracefully: no session, no timeline, denied permission → `HasTimeline = false`

---

### Step 3 — Lyric Models + LRC Parser

#### [NEW] `src/VerciWin.Core/Lyrics/Models/LyricWord.cs`
`{ string Text, TimeSpan Start, TimeSpan End }`

#### [NEW] `src/VerciWin.Core/Lyrics/Models/LyricLine.cs`
`{ List<LyricWord> Words, TimeSpan Start, TimeSpan End }`

#### [NEW] `src/VerciWin.Core/Lyrics/Models/LyricDocument.cs`
`{ List<LyricLine> Lines, bool IsWordLevel }` + helper `IReadOnlyList<LyricWord> AllWordsSorted`

#### [NEW] `src/VerciWin.Core/Lyrics/LrcParser.cs`
- Line-level: regex `^\[(\d{2}):(\d{2}\.\d{2,3})\](.*)$`
- Word-level A2: inline `<mm:ss.xx>` tags — parse interleaved text/tag pairs within a line
- Empty lines (instrumental breaks) → stored as `LyricLine` with empty `Words` list, used to clear the display
- Returns `LyricDocument` with `IsWordLevel` reflecting whether any word tags were found

---

### Step 4 — WordTimingInterpolator + Unit Tests

#### [NEW] `src/VerciWin.Core/Lyrics/WordTimingInterpolator.cs`
Algorithm:
1. Tokenize line text (split on whitespace)
2. Sum total character lengths across all words
3. Per word: `proportionalDuration = lineDuration * (wordCharCount / totalCharCount)`
4. Subtract configurable inter-word gap (default 50ms) from each word slot
5. Accumulate start times; clamp to line bounds

#### [NEW] `tests/VerciWin.Core.Tests/WordTimingInterpolatorTests.cs`
- Proportional allocation (12-char word vs 2-char word — durations must differ by ~6×)
- Inter-word gaps leave no overlap between word end and next word start
- Single-word line: word gets full duration
- Empty line: returns empty list, no throw
- Total duration consumed ≤ line duration

---

### Step 5 — Lyric Provider + Service + Cache

#### [NEW] `src/VerciWin.Core/Lyrics/ILyricProvider.cs`
```csharp
Task<LyricDocument?> GetLyricsAsync(
    string title, string artist, string album,
    TimeSpan duration, CancellationToken ct);
```

#### [NEW] `src/VerciWin.Core/Lyrics/LrcLibProvider.cs`
- `GET /api/get?track_name=&artist_name=&album_name=&duration=` (seconds, 1 decimal)
- On 404 or empty/null `syncedLyrics`: retry with `GET /api/search?track_name=&artist_name=`
- Parse `syncedLyrics` via `LrcParser`; fall back to `plainLyrics` (no timing → `LyricDocument` with line-level at 0:00 as a degraded state)
- Sets `User-Agent: VerciWin/1.0 (https://github.com/verciwin)`
- Self-imposed 300ms minimum between calls (conservative default, not a published LRCLIB constraint)
- Honours `Retry-After` on HTTP 429

#### [NEW] `src/VerciWin.Core/Lyrics/LyricService.cs`
1. Check `LyricCacheStore` → hit: return cached `LyricDocument`
2. Miss: call `LrcLibProvider.GetLyricsAsync`
3. On `LyricDocument` with `IsWordLevel = false`: run `WordTimingInterpolator`
4. Write to cache; return in-memory document
5. Cancellation-aware throughout (cancel in-flight request when `StateChanged` fires a new track)

#### [NEW] `src/VerciWin.Core/Caching/LyricCacheStore.cs`
- Path: `%AppData%/VerciWin/lyrics/{normalizedKey}.json`
- Key: `artist|title` lowercased, trimmed, invalid-path-chars replaced with `_`
- `ReadAsync(key)` → `LyricDocument?` (null on miss/corrupt)
- `WriteAsync(key, document)` → JSON serialize

#### [NEW] `tests/VerciWin.Core.Tests/LrcParserTests.cs`
- Line-level: timestamp extraction, text trimming, empty-line handling
- Word-level A2: inline tag parsing, correct word boundaries

#### [NEW] `tests/VerciWin.Core.Tests/LyricCacheStoreTests.cs`
- Write + read round-trip preserves all fields
- Key normalization: `"The Beatles" | "Hey Jude"` → valid filename
- Missing file returns `null` without throw
- Corrupt JSON returns `null` without throw

---

### Step 6 — PaletteExtractor (with Null Art Handling)

#### [NEW] `src/VerciWin.Core/Color/PaletteExtractor.cs`

```
TypographyPalette { Windows.UI.Color Primary, Accent, GlowBackground }
```

**Static `NeutralPalette`** (returned when `artStream` is `null`):
```
Primary:        #E8E8F0   (cool near-white)
Accent:         #9090C8   (muted periwinkle)
GlowBackground: #0D0D1A   (deep dark navy)
```

**Algorithm when stream is non-null:**
1. Decode image stream → `SoftwareBitmap` (via `BitmapDecoder`)
2. Downsample to 64×64 via `BitmapTransform`
3. Copy pixels to `byte[]`, read RGB triples
4. Median-cut → 8 buckets → 8 candidate `(R,G,B)` centroids
5. Score each centroid: `score = S × V × (1 - |L - 0.5| × 2)` (HSL; prefer saturated, mid-lightness)
6. Sort by score descending: `Primary` = top scorer with L > 0.4, `Accent` = second distinct scorer, `GlowBackground` = darkened version of `Primary` (L reduced to 0.08)
7. Cache result keyed by `XXHash32` of first 512 bytes of stream — recompute only on track change

**`ExtractAsync(Stream? artStream)`:** Returns `NeutralPalette` immediately when `artStream` is `null`. Never throws to callers.

---

### Step 7a — BLOCKING SPIKE: Click-Through Transparency Verification

> [!CAUTION]
> This step must complete and pass before Step 7b is started. It is the gate on which all renderer work (Step 8) depends. A failed spike changes the rendering approach in Step 8.

#### Purpose
Verify that a WinUI 3 window can be made visually transparent and click-through using `WS_EX_NOREDIRECTIONBITMAP + WS_EX_TRANSPARENT` without `WS_EX_LAYERED`, before the full overlay infrastructure is built on top.

#### [NEW] `src/VerciWin.App/Interop/Win32Interop.cs` (initial, extended in 7b)
P/Invoke signatures for this spike:
- `GetWindowLong` / `SetWindowLong` (both 32/64-bit variants)
- `SetWindowSubclass` / `DefSubclassProc` — for `WM_NCHITTEST` interception
- Constants: `WS_EX_NOREDIRECTIONBITMAP = 0x00200000`, `WS_EX_TRANSPARENT = 0x00000020`, `HTTRANSPARENT = -1`, `WM_NCHITTEST = 0x0084`

#### [NEW] `src/VerciWin.App/Interop/ExtendedWindowStyles.cs`
All window style constants, documented with their purpose and the compositing model they target.

#### Spike Implementation (in `OverlayWindow.xaml.cs` — temporary test state)

```xml
<!-- OverlayWindow.xaml during spike -->
<Window SystemBackdrop="{x:Null}">
  <Grid Background="Transparent">
    <Ellipse Width="200" Height="200" Fill="#800000FF" />
    <!-- Semi-transparent blue circle — must appear floating with no black surround -->
  </Grid>
</Window>
```

```csharp
// After window creation:
var hwnd = WindowNative.GetWindowHandle(this);
int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
// Apply NOREDIRECTIONBITMAP + TRANSPARENT, NOT LAYERED
SetWindowLong(hwnd, GWL_EXSTYLE,
    ex | WS_EX_NOREDIRECTIONBITMAP | WS_EX_TRANSPARENT);
// Belt-and-suspenders: subclass for WM_NCHITTEST
SetWindowSubclass(hwnd, SubclassProc, 0, 0);
```

#### Pass/Fail Criteria

| Test | Pass | Fail |
|---|---|---|
| Visual | Semi-transparent blue ellipse visible, surrounding area invisible (shows desktop beneath) | Black rectangle or black window background |
| Click-through | Click on desktop wallpaper area through the window → wallpaper context menu appears | Click absorbed by VerciWin window |
| Win2D alpha | Place `CanvasControl` with `DrawingSession` alpha fill → correct per-pixel alpha composited | Rendered Win2D content appears opaque or missing |

#### Outcomes & Actions

**✅ Spike passes (expected):**
- Proceed to Step 7b without changes to rendering approach.
- Win2D glow effects are achieved entirely within `CanvasDrawingSession` alpha blending.
- No `UpdateLayeredWindow` path needed. Document this in Step 8 code comments.

**❌ Spike fails — black window or no alpha:**
- Add `WS_EX_LAYERED` and test `SetLayeredWindowAttributes(hwnd, 0, 255, LWA_ALPHA)` as a fallback.
- If that causes hit-testing failure, use the `WM_NCHITTEST → HTTRANSPARENT` subclass path exclusively.
- **If `UpdateLayeredWindow` turns out to be required:** Flag explicitly — this is a raw DIB path incompatible with hosted XAML/Win2D. The glow/background must be achieved entirely as Win2D geometry with explicit alpha, not via window-level layering. Update this plan and Step 8 accordingly before proceeding.

---

### Step 7b — OverlayWindow (Full) + Win32 Interop + DPI + OverlayViewModel

#### [MODIFY] `src/VerciWin.App/Interop/Win32Interop.cs`
Add remaining P/Invoke signatures:
- `SetWindowPos` (topmost/non-topmost toggle)
- `GetDpiForMonitor` (for per-monitor DPI rect calculation)
- `MonitorFromWindow` + `GetMonitorInfo` (to find which monitor the overlay lives on)
- `DwmSetWindowAttribute` (corner preference for borderless window)

#### [NEW] `src/VerciWin.App/OverlayWindow.xaml` / `.xaml.cs`

**Transparency setup** (using approach confirmed by spike):
```csharp
var hwnd = WindowNative.GetWindowHandle(this);
SetWindowLong(hwnd, GWL_EXSTYLE,
    GetWindowLong(hwnd, GWL_EXSTYLE)
    | WS_EX_NOREDIRECTIONBITMAP | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW);
SetWindowSubclass(hwnd, _subclassProc, subclassId: 0, refData: 0);
```

**DPI-correct positioning:**
```csharp
// Get monitor that contains current window
var hMonitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
GetDpiForMonitor(hMonitor, MDT_EFFECTIVE_DPI, out uint dpiX, out _);
float scale = dpiX / 96.0f;

// Get monitor work area in physical pixels
GetMonitorInfo(hMonitor, ref monitorInfo);
var workArea = monitorInfo.rcWork; // RECT in physical pixels — no scaling needed for Win32 APIs

// Overlay: full width, bottom 30%, all in physical pixels
int overlayHeight = (int)((workArea.bottom - workArea.top) * 0.30f);
int overlayTop    = workArea.bottom - overlayHeight;
MoveWindow(hwnd, workArea.left, overlayTop,
    workArea.right - workArea.left, overlayHeight, repaint: true);
```

**DPI change handling:**
- Subscribe to `AppWindow.Changed` + check `DisplayChanged` reason
- Re-run positioning logic above on each display change event

**Mode toggle (runtime):**
- `EnableOverlayMode()`: re-applies `WS_EX_TRANSPARENT`, calls `SetWindowPos(HWND_TOPMOST)`
- `EnableInteractiveMode()`: strips `WS_EX_TRANSPARENT`, calls `SetWindowPos(HWND_NOTOPMOST)`, window becomes draggable/resizable

**XAML structure:**
```xml
<Window>
  <Grid Background="Transparent">
    <canvas:CanvasControl x:Name="LyricCanvas"
                          Draw="LyricCanvas_Draw"
                          ClearColor="Transparent" />
  </Grid>
</Window>
```

#### [NEW] `src/VerciWin.ViewModels/OverlayViewModel.cs`
```
ObservableObject
  PlaybackState CurrentState    ← bound from MediaSessionWatcher.StateChanged
  LyricDocument? CurrentLyrics  ← bound from LyricService result
  TypographyPalette Palette
  bool IsOverlayMode            ← drives WS_EX_TRANSPARENT toggle
  double Opacity                ← 0.25/0.5/0.75/1.0
  string VisualStyle            ← "Glow" | "Minimal"
```
No XAML references — only `CommunityToolkit.Mvvm` primitives.

---

### Step 8 — LyricCanvasRenderer + RenderLoop

#### [NEW] `src/VerciWin.App/Rendering/RenderLoop.cs`
- `DispatcherQueueTimer` at 16ms (~60Hz)
- Calls `CanvasControl.Invalidate()` each tick
- `Start()` / `Stop()` — stop when `IsPaused || !HasSession`, start on resume
- Timer reference held as field; no lambda capture to avoid GC churn

#### [NEW] `src/VerciWin.App/Rendering/LyricCanvasRenderer.cs`

**Draws in `CanvasControl.Draw` event handler (`CanvasDrawingSession`):**

1. **Position:** `now = MediaSessionWatcher.GetCurrentPosition()`
2. **Active word:** binary-search over `LyricDocument.AllWordsSorted` (by `Start`) — O(log n)
3. **Layout — "focal zone" model** (mirrors Verci's aesthetic):
   - **Past line:** rendered above center, `Opacity = 0.35`, `Scale = 0.85`, color = `Palette.Primary` dimmed
   - **Current line:** rendered at focal point, `Opacity = 1.0`, active word `Scale = lerp(1.0, 1.15, easeOutCubic(wordProgress))`
   - **Next line:** rendered below center, `Opacity = 0.35`, `Scale = 0.85`
   - Lines slide vertically via eased offset on line change (200ms cubic-bezier transition)
4. **Active word glow ("Glow" style):** `CanvasBlurEffect` on the active word's `CanvasTextLayout`, sigma = `lerp(4, 12, wordProgress)`, tint = `Palette.Accent`
5. **Background:** radial gradient centered on screen, inner color = `Palette.GlowBackground @ 0.30 alpha`, outer = transparent. Center point drifts: `sin(totalTime * 0.3) * driftRadius`
6. **Text layout cache:** `Dictionary<(string text, float fontSize), CanvasTextLayout>` — populated lazily, invalidated on lyric/style change. Zero heap allocations in the hot draw path.
7. **"Minimal" style:** no glow effect, no background gradient, white text, scale animation only.
8. **Error states (drawn inline):**
   - No session: centered "♫" glyph @ `Palette.Accent`, `Opacity = 0.4`
   - No lyrics: song title + "No lyrics available" in subdued text
   - No timeline: title + artist, no word-level animation

> [!NOTE]
> **Glow implementation note from spike:** All glow and background effects are produced via Win2D's `CanvasBlurEffect` and `CanvasGeometry` with explicit alpha values inside the `CanvasDrawingSession`. Window-level blending (`UpdateLayeredWindow`) is NOT used — it is incompatible with hosted Win2D/XAML content.

---

### Step 9 — TrayIconManager + Settings Window + VMs + SettingsStore

#### [NEW] `src/VerciWin.App/Tray/TrayIconManager.cs`
Uses `H.NotifyIcon.WinUI` `TaskbarIcon`, XAML-defined:
```xml
<tb:TaskbarIcon IconSource="Assets/TrayIcon.ico"
                ToolTipText="VerciWin">
  <tb:TaskbarIcon.ContextMenu>
    <!-- Toggle Overlay/Interactive Mode -->
    <!-- Opacity: 25% / 50% / 75% / 100% -->
    <!-- Style: Glow / Minimal -->
    <!-- Open Settings -->
    <!-- Exit -->
  </tb:TaskbarIcon.ContextMenu>
</tb:TaskbarIcon>
```
- Tooltip updated on `StateChanged`: `"VerciWin — {Title} by {Artist}"`
- Icon asset: `Assets/TrayIcon.ico` (32×32 placeholder — **replace with final art before release**)
- Each menu action → writes `SettingsStore` immediately

#### [NEW] `src/VerciWin.App/SettingsWindow.xaml` / `.xaml.cs`
Normal (non-click-through) WinUI 3 window, opened from tray "Open Settings":
- Opacity slider
- Visual style selector (Glow / Minimal)
- Window mode toggle
- Overlay position (preset: Lower Third / Center / Full Screen)
- "About" row with version info
- All controls bound to `SettingsViewModel`

#### [NEW] `src/VerciWin.ViewModels/SettingsViewModel.cs`
`ObservableObject` — properties mirror `SettingsStore` fields; `SaveCommand` commits to disk.

#### [NEW] `src/VerciWin.ViewModels/TrayMenuViewModel.cs`
`ObservableObject` — `RelayCommand`s for each tray menu action; reads/writes `OverlayViewModel` + `SettingsStore`.

#### [NEW] `src/VerciWin.Core/Settings/SettingsStore.cs`
- Path: `%AppData%/VerciWin/settings.json`
- Schema:
  ```json
  {
    "opacity": 1.0,
    "visualStyle": "Glow",
    "isOverlayMode": true,
    "overlayPosition": "LowerThird"
  }
  ```
- `LoadAsync()` → deserialize or return `AppSettings.Defaults` on missing/corrupt file
- `SaveAsync(AppSettings)` → atomic write (write temp file, rename)
- Loaded at app startup (Step 11), written on every tray menu state change

---

### Step 10 — ExecutionStateManager

#### [NEW] `src/VerciWin.Core/Power/ExecutionStateManager.cs`

```csharp
// IMPORTANT: SetThreadExecutionState must be called from the thread that should be kept awake.
// Call Engage() when MediaSessionWatcher reports active (non-paused) playback.
// Call Release() when paused, stopped, or no session.
// IMPORTANT: Always call Release() in Dispose() — leaving ES_SYSTEM_REQUIRED engaged
// permanently is a bug that prevents the machine from sleeping. Don't do it.
public void Engage()  => SetThreadExecutionState(ES_CONTINUOUS | ES_SYSTEM_REQUIRED | ES_DISPLAY_REQUIRED);
public void Release() => SetThreadExecutionState(ES_CONTINUOUS);  // ES_CONTINUOUS alone clears the hold
```

Implements `IDisposable` — `Dispose()` calls `Release()` unconditionally.

---

### Step 11 — DI Wiring, Single-Instance Guard, Error States, Bootstrapper Re-verify

#### [MODIFY] `src/VerciWin.App/App.xaml.cs`

**Single-instance guard:**
```csharp
private static Mutex? _mutex;
// In OnLaunched:
_mutex = new Mutex(initiallyOwned: true, "VerciWin_SingleInstance_Mutex", out bool created);
if (!created) { Environment.Exit(0); return; }
```

**DI container:**
```
Singleton: MediaSessionWatcher, LyricService, LyricCacheStore,
           LrcLibProvider, PaletteExtractor, ExecutionStateManager,
           SettingsStore, OverlayViewModel, TrayMenuViewModel
Transient:  SettingsViewModel
HttpClient: named "lrclib", BaseAddress=https://lrclib.net,
            User-Agent header set
```

**Wire-up sequence:**
1. Load `SettingsStore` → populate `OverlayViewModel` initial state
2. Start `MediaSessionWatcher`
3. `MediaSessionWatcher.StateChanged` → `LyricService.GetLyricsAsync` (new `CancellationToken` per track) → `OverlayViewModel.CurrentLyrics`
4. `MediaSessionWatcher.StateChanged` → `PaletteExtractor.ExtractAsync(artStream)` → `OverlayViewModel.Palette`
5. `MediaSessionWatcher.StateChanged` → `ExecutionStateManager.Engage()` / `.Release()` based on `IsPaused`
6. Construct `OverlayWindow`, `TrayIconManager`; start `RenderLoop`

**Global exception logging:**
```csharp
Application.UnhandledException += (_, e) =>
{
    var logDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "VerciWin", "logs");
    Directory.CreateDirectory(logDir);
    File.AppendAllText(
        Path.Combine(logDir, "error.log"),
        $"[{DateTime.Now:o}] {e.Exception}\n");
    e.Handled = true;   // attempt to keep running; re-throw only on fatal
};
```

**Bootstrapper re-verify:** Confirm `Bootstrap.Initialize` in `Program.cs` fires before `new App()`. Integration test: remove WinAppSDK runtime from a clean VM and verify the `MessageBox` error path fires rather than a crash.

---

## Tradeoffs

### Tray: H.NotifyIcon.WinUI vs Shell_NotifyIcon P/Invoke
**Chosen:** `H.NotifyIcon.WinUI`
- Pros: XAML context menus, MVVM-friendly data binding, handles WndProc boilerplate
- Cons: NuGet dependency, SDK version coupling
- Raw P/Invoke alternative requires ~300 LOC for WndProc + context menu owner-draw; unjustified for a non-library project.

### Rendering: CanvasControl + DispatcherQueueTimer vs CanvasSwapChain
**Chosen:** `CanvasControl` + `DispatcherQueueTimer` at 60Hz
- `CanvasAnimatedControl` (UWP) is NOT available in WinUI 3 (confirmed via research)
- `CompositionTarget.Rendering` is deprecated in WinUI 3
- `CanvasControl.Invalidate()` driven by timer is the established WinUI 3 pattern for Win2D
- Escalation path: `CanvasSwapChain` on a background render thread if UI-thread contention is measured under profiling

### Transparency: WS_EX_NOREDIRECTIONBITMAP + WS_EX_TRANSPARENT vs WS_EX_LAYERED
**Chosen primary approach:** `WS_EX_NOREDIRECTIONBITMAP + WS_EX_TRANSPARENT`
- `WS_EX_LAYERED` on a WinUI 3 HWND conflicts with DirectComposition — known failure modes include black window content and broken hit-testing
- `WS_EX_NOREDIRECTIONBITMAP` lets the WinUI DirectComposition surface own per-pixel alpha directly
- Fallback only if spike fails: test `WS_EX_LAYERED`, document if that changes the glow approach in Step 8

### Palette: Median Cut vs K-Means
**Chosen:** Median cut
- Deterministic (no random seed variance between runs on the same image)
- O(n log n) vs O(k·n·iterations) — faster for per-track recalculation
- Saturation scoring post-extraction handles the "picks near-black as dominant" failure mode
- Null art handled via `NeutralPalette` constant — no exception path

### Settings Persistence: SettingsStore (JSON) vs Registry
**Chosen:** JSON file at `%AppData%/VerciWin/settings.json`
- Portable, inspectable, easy to reset (delete the file)
- No elevated permissions needed
- Atomic write (temp+rename) prevents partial-write corruption

---

## Verification Plan

### Automated Tests
```bash
dotnet test tests/VerciWin.Core.Tests/VerciWin.Core.Tests.csproj
```
Covers:
- `WordTimingInterpolatorTests` — proportional distribution, gaps, edge cases
- `LrcParserTests` — line-level and word-level A2 LRC, empty lines
- `LyricCacheStoreTests` — round-trip, key normalization, missing/corrupt file

### Build & Publish
```bash
dotnet build VerciWin.sln --configuration Debug
dotnet publish src/VerciWin.App/VerciWin.App.csproj -r win-x64 --self-contained -c Release
```

### Manual Verification Checklist (10 items)
1. **Click-through:** Overlay visible → click on desktop area → wallpaper context menu appears (not VerciWin absorbing click)
2. **No taskbar icon:** Overlay window absent from taskbar and Alt+Tab switcher (`WS_EX_TOOLWINDOW`)
3. **Tray icon:** VerciWin icon in system tray; right-click shows full context menu
4. **Overlay stays on top:** Open any window — overlay stays above it while in overlay mode
5. **Toggle mode:** "Normal Window" mode → window becomes draggable, click-through disabled
6. **DPI correctness:** On a secondary monitor with a different scale factor — overlay fills correct bottom-30% area (not oversized/undersized)
7. **GSMTC integration:** Play audio in Spotify or Chrome → title + artist appear in overlay within ~2s
8. **Lyrics sync:** Words highlight at approximately correct timestamps relative to playback position
9. **Session handoff:** Switch from Spotify to Chrome playing audio → overlay updates to new track without crash
10. **Single instance:** Launch app twice → second instance exits immediately without showing a window

---

## Revised Build Order (12 Commits)

```
Commit  1: Solution scaffold, all .csproj files, Program.cs bootstrapper, app.manifest, blank WinUI window
Commit  2: MediaSessionWatcher + PlaybackState (GSMTC wrapper, stopwatch extrapolation, 5Hz poll)
Commit  3: Lyric models (LyricWord, LyricLine, LyricDocument) + LrcParser (line-level + A2 word-level)
Commit  4: WordTimingInterpolator + unit tests (proportional, gaps, edge cases)
Commit  5: ILyricProvider + LrcLibProvider + LyricService + LyricCacheStore + cache tests
Commit  6: PaletteExtractor (median cut, saturation scoring, NeutralPalette null-art default)
Commit  7a: [BLOCKING SPIKE] Click-through transparency spike — verify WS_EX_NOREDIRECTIONBITMAP + WS_EX_TRANSPARENT
Commit  7b: OverlayWindow (full), Win32Interop, ExtendedWindowStyles, DPI-correct positioning, OverlayViewModel
Commit  8: LyricCanvasRenderer + RenderLoop (Win2D draw, focal-zone layout, word animation, glow, error states)
Commit  9: TrayIconManager + SettingsWindow + SettingsViewModel + TrayMenuViewModel + SettingsStore
Commit 10: ExecutionStateManager (SetThreadExecutionState, Dispose release guard)
Commit 11: DI wiring, single-instance mutex, MediaSessionWatcher→LyricService→OverlayViewModel end-to-end, README
```
