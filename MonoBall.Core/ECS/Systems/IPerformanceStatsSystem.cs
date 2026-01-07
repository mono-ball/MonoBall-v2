namespace MonoBall.Core.ECS.Systems;

/// <summary>
///     Interface for performance statistics system.
///     Tracks FPS, frame time, entity count, memory usage, draw calls, and GC information.
/// </summary>
public interface IPerformanceStatsSystem
{
    /// <summary>
    ///     Gets the current FPS (frames per second).
    /// </summary>
    float Fps { get; }

    /// <summary>
    ///     Gets the current frame time in milliseconds.
    /// </summary>
    float FrameTimeMs { get; }

    /// <summary>
    ///     Gets the current entity count in the ECS world.
    /// </summary>
    int EntityCount { get; }

    /// <summary>
    ///     Gets the current memory usage in bytes.
    /// </summary>
    long MemoryBytes { get; }

    /// <summary>
    ///     Gets the current draw call count.
    /// </summary>
    int DrawCalls { get; }

    /// <summary>
    ///     Gets the GC Generation 0 collection count.
    /// </summary>
    int GcGen0 { get; }

    /// <summary>
    ///     Gets the GC Generation 1 collection count.
    /// </summary>
    int GcGen1 { get; }

    /// <summary>
    ///     Gets the GC Generation 2 collection count.
    /// </summary>
    int GcGen2 { get; }

    /// <summary>
    ///     Increments the draw call counter. Called by render systems.
    /// </summary>
    void IncrementDrawCalls();
}
