using Microsoft.UI.Dispatching;

namespace VerciWin.App.Rendering;

/// <summary>
/// 60 Hz rendering timer driven by <see cref="DispatcherQueueTimer"/>.
/// Automatically starts when music is actively playing and pauses when playback stops,
/// preventing unnecessary CPU/GPU usage when idle.
/// </summary>
public sealed class RenderLoop : IDisposable
{
    private readonly DispatcherQueueTimer _timer;
    private readonly Action _renderCallback;
    private bool _isRunning;

    public RenderLoop(DispatcherQueue dispatcherQueue, Action renderCallback)
    {
        _renderCallback = renderCallback ?? throw new ArgumentNullException(nameof(renderCallback));
        _timer = dispatcherQueue.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(16.67); // ~60 fps
        _timer.Tick += (s, e) => _renderCallback();
    }

    public bool IsRunning => _isRunning;

    public void Start()
    {
        if (_isRunning) return;
        _isRunning = true;
        _timer.Start();
    }

    public void Stop()
    {
        if (!_isRunning) return;
        _isRunning = false;
        _timer.Stop();
    }

    public void Dispose()
    {
        Stop();
    }
}
