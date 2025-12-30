namespace MonoBall.Core.Diagnostics.Console.Services;

using System;

/// <summary>
/// Simple adapter that provides performance statistics for console commands.
/// Calculates stats on-demand from system sources.
/// </summary>
public sealed class PerformanceStatsAdapter : IPerformanceStats
{
    private float _lastFrameTimeMs;
    private float _fps;
    private int _entityCount;
    private int _drawCalls;

    /// <inheritdoc />
    public float Fps => _fps;

    /// <inheritdoc />
    public float FrameTimeMs => _lastFrameTimeMs;

    /// <inheritdoc />
    public int EntityCount => _entityCount;

    /// <inheritdoc />
    public long MemoryBytes => GC.GetTotalMemory(false);

    /// <inheritdoc />
    public int DrawCalls => _drawCalls;

    /// <inheritdoc />
    public int GcGen0 => GC.CollectionCount(0);

    /// <inheritdoc />
    public int GcGen1 => GC.CollectionCount(1);

    /// <inheritdoc />
    public int GcGen2 => GC.CollectionCount(2);

    /// <summary>
    /// Updates the stats with the current frame data.
    /// Call this once per frame to keep stats current.
    /// </summary>
    /// <param name="deltaTime">The frame delta time in seconds.</param>
    /// <param name="entityCount">The current entity count.</param>
    /// <param name="drawCalls">The current draw call count.</param>
    public void Update(float deltaTime, int entityCount = 0, int drawCalls = 0)
    {
        _lastFrameTimeMs = deltaTime * 1000f;
        _fps = deltaTime > 0 ? 1f / deltaTime : 0f;
        _entityCount = entityCount;
        _drawCalls = drawCalls;
    }
}
