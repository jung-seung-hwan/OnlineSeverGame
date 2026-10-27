using System.Diagnostics;
using System.Runtime.InteropServices;

namespace OnlineSeverGame.Server;

/// <summary>
/// Fixed-rate schedule on absolute time: tick n is due at start + n * period, so a late tick does not
/// push the following ones back. On Windows the system timer normally ticks every ~15.6 ms, which makes
/// Thread.Sleep overshoot by up to that much; timeBeginPeriod(1) asks for 1 ms resolution while this
/// timer is alive. The last ~1.5 ms before the deadline is spun instead of slept.
/// </summary>
public sealed class TickTimer : IDisposable
{
    private const double SpinThresholdMs = 1.5;

    private readonly double _periodMs;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly bool _raisedResolution;
    private double _next;
    private double _lateSum;
    private double _lateMax;
    private int _lateCount;

    public TickTimer(double periodMs)
    {
        _periodMs = periodMs;
        _next = _clock.Elapsed.TotalMilliseconds;
        if (OperatingSystem.IsWindows()) _raisedResolution = TimeBeginPeriod(1) == 0;
    }

    public bool HighResolution => _raisedResolution;

    /// <summary>Ticks given up because the loop fell more than 5 periods behind.</summary>
    public int SkippedTicks { get; private set; }

    /// <summary>Blocks until the next tick is due. Returns how late (ms) it actually woke up.</summary>
    public double WaitNext()
    {
        _next += _periodMs;
        double now = _clock.Elapsed.TotalMilliseconds;
        if (now - _next > 5 * _periodMs)
        {
            SkippedTicks += (int)((now - _next) / _periodMs);
            _next = now;
        }

        double wait;
        while ((wait = _next - _clock.Elapsed.TotalMilliseconds) > 0)
        {
            if (wait > SpinThresholdMs) Thread.Sleep((int)(wait - 1));
            else Thread.SpinWait(50);
        }

        double late = -wait;
        _lateSum += late;
        _lateMax = Math.Max(_lateMax, late);
        _lateCount++;
        return late;
    }

    /// <summary>Average and worst lateness since the previous call.</summary>
    public (double Avg, double Max) TakeStats()
    {
        var result = (_lateCount == 0 ? 0 : _lateSum / _lateCount, _lateMax);
        _lateSum = _lateMax = 0;
        _lateCount = 0;
        return result;
    }

    public void Dispose()
    {
        if (_raisedResolution) TimeEndPeriod(1);
    }

    [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
    private static extern uint TimeBeginPeriod(uint milliseconds);

    [DllImport("winmm.dll", EntryPoint = "timeEndPeriod")]
    private static extern uint TimeEndPeriod(uint milliseconds);
}
