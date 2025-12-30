namespace MonoBall.Core.Diagnostics;

/// <summary>
/// Static hook for profiling ECS system execution times.
/// Used by SystemManager to report timing and by SystemProfilerPanel to display it.
/// </summary>
public static class SystemProfiler
{
    /// <summary>
    /// Records a system timing measurement.
    /// Notifies all subscribed hooks via SystemTimingHook.
    /// </summary>
    /// <param name="systemName">The name of the system.</param>
    /// <param name="elapsedMs">The elapsed time in milliseconds.</param>
    public static void RecordTiming(string systemName, double elapsedMs)
    {
        SystemTimingHook.Notify(systemName, elapsedMs);
    }
}
