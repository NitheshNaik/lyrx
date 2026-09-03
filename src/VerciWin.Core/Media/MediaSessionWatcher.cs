using System.Diagnostics;
using System.IO;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace VerciWin.Core.Media;

/// <summary>
/// Wraps the Global System Media Transport Controls (GSMTC) session manager.
/// <para>
/// <b>Position strategy:</b> GSMTC does not push per-frame position ticks.
/// Instead we:
/// <list type="bullet">
///   <item>Subscribe to <c>TimelinePropertiesChanged</c> for authoritative anchors.</item>
///   <item>Maintain a local <see cref="Stopwatch"/> extrapolation between anchors:
///         <c>effectivePos = anchor + stopwatch.Elapsed * rate</c>.</item>
///   <item>Poll GSMTC at 5 Hz via a <see cref="System.Threading.Timer"/> for drift
///         correction; each poll refreshes the anchor.</item>
/// </list>
/// The render loop calls <see cref="GetCurrentPosition"/> at 60 fps — it reads only
/// the extrapolated value, making zero GSMTC calls per frame.
/// </para>
/// <para>
/// <b>Scrub detection:</b> If the absolute difference between the reported position
/// and the extrapolated position exceeds 2 seconds on a <c>TimelinePropertiesChanged</c>
/// event, a scrub (seek) is detected and the anchor is reset without interpolating
/// across the gap.
/// </para>
/// </summary>
public sealed class MediaSessionWatcher : IDisposable
{
    // ── Events ───────────────────────────────────────────────────────────────
    public event EventHandler<PlaybackState>? StateChanged;

    // ── GSMTC objects ────────────────────────────────────────────────────────
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _session;

    // ── Position extrapolation ────────────────────────────────────────────────
    private readonly Stopwatch _extrapolationClock = new();
    private TimeSpan _anchorPosition = TimeSpan.Zero;
    private double _playbackRate = 1.0;
    private bool _isPaused = true;

    // ── 5 Hz drift-correction poll ───────────────────────────────────────────
    private System.Threading.Timer? _pollTimer;
    private const int PollIntervalMs = 200; // 5 Hz

    // ── Current synthesised state ─────────────────────────────────────────────
    private PlaybackState _currentState = PlaybackState.Empty;
    private readonly object _stateLock = new();

    // ── Scrub detection threshold ─────────────────────────────────────────────
    private const double ScrubThresholdSeconds = 2.0;

    // ─────────────────────────────────────────────────────────────────────────
    // Public API
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Initialises the session manager and starts monitoring.
    /// Call once from the UI thread at app startup.
    /// </summary>
    public async Task InitializeAsync()
    {
        try
        {
            _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            _manager.CurrentSessionChanged += OnCurrentSessionChanged;
            BindToSession(_manager.GetCurrentSession());
        }
        catch (Exception ex)
        {
            // Denied permission, or OS too old — degrade gracefully.
            Debug.WriteLine($"[MediaSessionWatcher] Failed to initialise GSMTC: {ex.Message}");
            RaiseStateChanged(PlaybackState.Empty);
        }

        // Start drift-correction poll regardless; it handles null session safely.
        _pollTimer = new System.Threading.Timer(
            PollPosition, null, PollIntervalMs, PollIntervalMs);
    }

    /// <summary>
    /// Returns the current playback position with sub-frame accuracy.
    /// Reads only from the local Stopwatch — safe to call at 60 fps.
    /// </summary>
    public TimeSpan GetCurrentPosition()
    {
        if (_isPaused)
            return _anchorPosition;

        var extrapolated = _anchorPosition +
            TimeSpan.FromSeconds(_extrapolationClock.Elapsed.TotalSeconds * _playbackRate);

        // Clamp to reported end time so we don't overshoot.
        var end = _currentState.EndTime;
        return end > TimeSpan.Zero && extrapolated > end ? end : extrapolated;
    }

    /// <summary>Returns the most recent synthesised <see cref="PlaybackState"/>.</summary>
    public PlaybackState CurrentState
    {
        get { lock (_stateLock) return _currentState; }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Session binding
    // ─────────────────────────────────────────────────────────────────────────

    private void OnCurrentSessionChanged(
        GlobalSystemMediaTransportControlsSessionManager sender,
        CurrentSessionChangedEventArgs args)
    {
        BindToSession(sender.GetCurrentSession());
    }

    private void BindToSession(GlobalSystemMediaTransportControlsSession? session)
    {
        // Unsubscribe from old session.
        if (_session is not null)
        {
            _session.MediaPropertiesChanged -= OnMediaPropertiesChanged;
            _session.PlaybackInfoChanged -= OnPlaybackInfoChanged;
            _session.TimelinePropertiesChanged -= OnTimelinePropertiesChanged;
        }

        _session = session;

        if (_session is null)
        {
            _isPaused = true;
            _extrapolationClock.Stop();
            RaiseStateChanged(PlaybackState.Empty);
            return;
        }

        _session.MediaPropertiesChanged += OnMediaPropertiesChanged;
        _session.PlaybackInfoChanged += OnPlaybackInfoChanged;
        _session.TimelinePropertiesChanged += OnTimelinePropertiesChanged;

        // Fetch initial state synchronously (fire-and-forget async update below).
        _ = RefreshFullStateAsync();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GSMTC event handlers
    // ─────────────────────────────────────────────────────────────────────────

    private void OnMediaPropertiesChanged(
        GlobalSystemMediaTransportControlsSession sender,
        MediaPropertiesChangedEventArgs args)
        => _ = RefreshFullStateAsync();

    private void OnPlaybackInfoChanged(
        GlobalSystemMediaTransportControlsSession sender,
        PlaybackInfoChangedEventArgs args)
        => _ = RefreshPlaybackInfoAsync();

    private void OnTimelinePropertiesChanged(
        GlobalSystemMediaTransportControlsSession sender,
        TimelinePropertiesChangedEventArgs args)
        => RefreshTimelineAnchor(sender.GetTimelineProperties(), isScrubDetectionEnabled: true);

    // ─────────────────────────────────────────────────────────────────────────
    // 5 Hz poll (drift correction)
    // ─────────────────────────────────────────────────────────────────────────

    private void PollPosition(object? _)
    {
        if (_session is null) return;
        try
        {
            var timeline = _session.GetTimelineProperties();
            if (timeline is null) return;
            // Poll correction — treat as anchor refresh without scrub detection.
            RefreshTimelineAnchor(timeline, isScrubDetectionEnabled: false);
        }
        catch
        {
            // Session may have gone away between the null check and the call.
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // State refresh helpers
    // ─────────────────────────────────────────────────────────────────────────

    private async Task RefreshFullStateAsync()
    {
        if (_session is null) return;
        try
        {
            var props = await _session.TryGetMediaPropertiesAsync();
            if (props is null) return;

            // Read playback info.
            var info = _session.GetPlaybackInfo();
            var timeline = _session.GetTimelineProperties();

            _isPaused = info?.PlaybackStatus is not
                GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            _playbackRate = info?.PlaybackRate ?? 1.0;

            bool hasTimeline = timeline is not null;
            if (hasTimeline)
                RefreshTimelineAnchor(timeline!, isScrubDetectionEnabled: false);

            // Read album art asynchronously.
            Stream? artStream = null;
            if (props.Thumbnail is not null)
            {
                try
                {
                    var winrtStream = await props.Thumbnail.OpenReadAsync();
                    var ms = new MemoryStream();
                    await winrtStream.AsStreamForRead().CopyToAsync(ms);
                    ms.Position = 0;
                    artStream = ms;
                }
                catch
                {
                    // Art unavailable — PaletteExtractor handles null gracefully.
                }
            }

            var state = new PlaybackState
            {
                Title = props.Title ?? string.Empty,
                Artist = props.Artist ?? string.Empty,
                Album = props.AlbumTitle ?? string.Empty,
                SourceAppId = _session.SourceAppUserModelId ?? string.Empty,
                AlbumArtStream = artStream,
                Position = GetCurrentPosition(),
                EndTime = timeline?.EndTime ?? TimeSpan.Zero,
                PlaybackRate = _playbackRate,
                IsPaused = _isPaused,
                HasTimeline = hasTimeline,
            };

            RaiseStateChanged(state);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MediaSessionWatcher] RefreshFullStateAsync: {ex.Message}");
        }
    }

    private async Task RefreshPlaybackInfoAsync()
    {
        if (_session is null) return;
        try
        {
            var info = _session.GetPlaybackInfo();
            bool wasPaused = _isPaused;

            _isPaused = info?.PlaybackStatus is not
                GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            _playbackRate = info?.PlaybackRate ?? 1.0;

            if (wasPaused && !_isPaused)
            {
                // Resuming — restart the extrapolation clock from the current anchor.
                _extrapolationClock.Restart();
            }
            else if (!wasPaused && _isPaused)
            {
                // Pausing — freeze the anchor at the current extrapolated position.
                _anchorPosition = GetCurrentPosition();
                _extrapolationClock.Stop();
            }

            // Raise a state update so OverlayViewModel can start/stop the render loop.
            lock (_stateLock)
            {
                _currentState = _currentState with
                {
                    IsPaused = _isPaused,
                    PlaybackRate = _playbackRate,
                    Position = GetCurrentPosition(),
                };
            }
            StateChanged?.Invoke(this, _currentState);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MediaSessionWatcher] RefreshPlaybackInfoAsync: {ex.Message}");
        }
    }

    private void RefreshTimelineAnchor(
        GlobalSystemMediaTransportControlsSessionTimelineProperties timeline,
        bool isScrubDetectionEnabled)
    {
        var reported = timeline.Position;

        if (isScrubDetectionEnabled)
        {
            // Scrub detection: compare reported position to extrapolated value.
            var extrapolated = GetCurrentPosition();
            var delta = Math.Abs((reported - extrapolated).TotalSeconds);
            if (delta > ScrubThresholdSeconds)
            {
                Debug.WriteLine(
                    $"[MediaSessionWatcher] Scrub detected — " +
                    $"expected {extrapolated:g}, got {reported:g}, Δ={delta:F1}s");
            }
        }

        _anchorPosition = reported;
        if (!_isPaused)
            _extrapolationClock.Restart();
    }

    private void RaiseStateChanged(PlaybackState state)
    {
        lock (_stateLock) { _currentState = state; }
        StateChanged?.Invoke(this, state);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // IDisposable
    // ─────────────────────────────────────────────────────────────────────────

    public void Dispose()
    {
        _pollTimer?.Dispose();
        _pollTimer = null;

        if (_session is not null)
        {
            _session.MediaPropertiesChanged -= OnMediaPropertiesChanged;
            _session.PlaybackInfoChanged -= OnPlaybackInfoChanged;
            _session.TimelinePropertiesChanged -= OnTimelinePropertiesChanged;
        }

        if (_manager is not null)
            _manager.CurrentSessionChanged -= OnCurrentSessionChanged;
    }
}
