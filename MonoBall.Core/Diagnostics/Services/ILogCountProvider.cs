namespace MonoBall.Core.Diagnostics.Services;

/// <summary>
/// Provides log count statistics for error and warning tracking.
/// </summary>
public interface ILogCountProvider
{
    /// <summary>
    /// Gets the current error count.
    /// </summary>
    int ErrorCount { get; }

    /// <summary>
    /// Gets the current warning count.
    /// </summary>
    int WarningCount { get; }

    /// <summary>
    /// Gets the total log count.
    /// </summary>
    int TotalCount { get; }
}
