namespace MonoBall.Core.Diagnostics.Console.Services;

/// <summary>
/// Interface for accessing performance statistics from console commands.
/// </summary>
public interface IPerformanceStats
{
    /// <summary>
    /// Gets the current frames per second.
    /// </summary>
    float Fps { get; }

    /// <summary>
    /// Gets the current frame time in milliseconds.
    /// </summary>
    float FrameTimeMs { get; }

    /// <summary>
    /// Gets the total entity count in the world.
    /// </summary>
    int EntityCount { get; }

    /// <summary>
    /// Gets the current managed memory usage in bytes.
    /// </summary>
    long MemoryBytes { get; }

    /// <summary>
    /// Gets the number of draw calls this frame.
    /// </summary>
    int DrawCalls { get; }

    /// <summary>
    /// Gets the number of Gen0 garbage collections.
    /// </summary>
    int GcGen0 { get; }

    /// <summary>
    /// Gets the number of Gen1 garbage collections.
    /// </summary>
    int GcGen1 { get; }

    /// <summary>
    /// Gets the number of Gen2 garbage collections.
    /// </summary>
    int GcGen2 { get; }
}
