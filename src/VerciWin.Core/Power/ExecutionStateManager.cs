using System.Diagnostics;
using System.Runtime.InteropServices;

namespace VerciWin.Core.Power;

/// <summary>
/// Manages Windows execution state via <c>SetThreadExecutionState</c> to prevent
/// the screen/system from sleeping while music is actively playing.
/// <para>
/// <b>CRITICAL:</b> When playback is paused or stopped, <see cref="Release"/> must be called
/// (passing <c>ES_CONTINUOUS</c> alone) to clear the requirement. Leaving
/// <c>ES_SYSTEM_REQUIRED</c> or <c>ES_DISPLAY_REQUIRED</c> engaged permanently is a critical
/// bug that prevents Windows from entering sleep mode or powering off displays.
/// </para>
/// </summary>
public sealed class ExecutionStateManager : IDisposable
{
    [Flags]
    private enum ExecutionState : uint
    {
        ES_SYSTEM_REQUIRED = 0x00000001,
        ES_DISPLAY_REQUIRED = 0x00000002,
        ES_USER_PRESENT = 0x00000004,
        ES_AWAYMODE_REQUIRED = 0x00000040,
        ES_CONTINUOUS = 0x80000000,
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern ExecutionState SetThreadExecutionState(ExecutionState esFlags);

    private bool _isEngaged;
    private bool _isDisposed;

    /// <summary>
    /// Prevents the display and system from sleeping while playback is active.
    /// Safe to call repeatedly.
    /// </summary>
    public void Engage()
    {
        if (_isDisposed) return;
        if (_isEngaged) return;

        try
        {
            var result = SetThreadExecutionState(
                ExecutionState.ES_CONTINUOUS |
                ExecutionState.ES_SYSTEM_REQUIRED |
                ExecutionState.ES_DISPLAY_REQUIRED);

            if (result != 0)
            {
                _isEngaged = true;
                Debug.WriteLine("[ExecutionStateManager] Execution state engaged (Continuous | System | Display)");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ExecutionStateManager] Failed to engage execution state: {ex.Message}");
        }
    }

    /// <summary>
    /// Clears the sleep prevention requirement by passing <c>ES_CONTINUOUS</c> alone.
    /// Safe to call repeatedly.
    /// </summary>
    public void Release()
    {
        if (!_isEngaged) return;

        try
        {
            SetThreadExecutionState(ExecutionState.ES_CONTINUOUS);
            _isEngaged = false;
            Debug.WriteLine("[ExecutionStateManager] Execution state released (Continuous alone)");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ExecutionStateManager] Failed to release execution state: {ex.Message}");
        }
    }

    /// <summary>
    /// Ensures execution state hold is unconditionally released upon disposal.
    /// </summary>
    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        Release();
    }
}
