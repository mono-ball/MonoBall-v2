using System;
using Arch.Core;
using Arch.System;
using Serilog;

namespace MonoBall.Core.ECS.Systems;

/// <summary>
///     System that tracks performance statistics including FPS, frame time, entity count, memory usage, draw calls, and GC
///     information.
/// </summary>
public class PerformanceStatsSystem : BaseSystem<World, float>, IPrioritizedSystem
{
    // Cached query description to avoid allocations in hot paths
    private static readonly QueryDescription _allEntitiesQuery = new();
    private readonly ILogger _logger;

    // Stats tracking

    // FPS calculation
    private float _fpsAccumulator;
    private int _fpsFrames;
    private float _fpsTimer;

    /// <summary>
    ///     Initializes a new instance of the PerformanceStatsSystem.
    /// </summary>
    /// <param name="world">The ECS world.</param>
    /// <param name="logger">The logger for logging operations.</param>
    public PerformanceStatsSystem(World world, ILogger logger)
        : base(world)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    ///     Gets the current FPS (frames per second).
    /// </summary>
    public float Fps { get; private set; }

    /// <summary>
    ///     Gets the current frame time in milliseconds.
    /// </summary>
    public float FrameTimeMs { get; private set; }

    /// <summary>
    ///     Gets the current entity count in the ECS world.
    /// </summary>
    public int EntityCount { get; private set; }

    /// <summary>
    ///     Gets the current memory usage in bytes.
    /// </summary>
    public long MemoryBytes { get; private set; }

    /// <summary>
    ///     Gets the current draw call count.
    /// </summary>
    public int DrawCalls { get; private set; }

    /// <summary>
    ///     Gets the GC Generation 0 collection count.
    /// </summary>
    public int GcGen0 { get; private set; }

    /// <summary>
    ///     Gets the GC Generation 1 collection count.
    /// </summary>
    public int GcGen1 { get; private set; }

    /// <summary>
    ///     Gets the GC Generation 2 collection count.
    /// </summary>
    public int GcGen2 { get; private set; }

    /// <summary>
    ///     Gets the execution priority for this system.
    /// </summary>
    public int Priority => SystemPriority.PerformanceStats;

    /// <summary>
    ///     Increments the draw call counter. Called by render systems.
    /// </summary>
    public void IncrementDrawCalls()
    {
        DrawCalls++;
    }

    /// <summary>
    ///     Updates performance statistics each frame.
    /// </summary>
    /// <param name="deltaTime">The elapsed time since last update in seconds.</param>
    /// <remarks>
    ///     Note: Draw calls are reset at the start of Update, then incremented during Render() calls.
    ///     The DrawCalls property shows the count from the previous frame's rendering.
    /// </remarks>
    public override void Update(in float deltaTime)
    {
        // Reset draw calls counter each frame
        // Note: This counter is incremented during Render() calls, so the value shown
        // represents the draw calls from the previous frame's rendering.
        DrawCalls = 0;

        // Update frame time (convert seconds to milliseconds)
        FrameTimeMs = deltaTime * 1000.0f;

        // Update entity count
        EntityCount = World.CountEntities(_allEntitiesQuery);

        // Update memory usage
        MemoryBytes = GC.GetTotalMemory(false);

        // Update GC collection counts
        GcGen0 = GC.CollectionCount(0);
        GcGen1 = GC.CollectionCount(1);
        GcGen2 = GC.CollectionCount(2);

        // Calculate FPS (average over 1 second)
        _fpsAccumulator += deltaTime;
        _fpsFrames++;
        _fpsTimer += deltaTime;

        if (_fpsTimer >= 1.0f)
        {
            Fps = _fpsFrames / _fpsTimer;
            _fpsAccumulator = 0.0f;
            _fpsFrames = 0;
            _fpsTimer = 0.0f;
        }
    }
}
