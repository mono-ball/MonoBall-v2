using System;
using Arch.Core;
using Arch.System;
using MonoBall.Core.ECS;
using MonoBall.Core.ECS.Components;
using Serilog;

namespace MonoBall.Core.ECS.Systems
{
    /// <summary>
    /// System that tracks performance statistics including FPS, frame time, entity count, memory usage, draw calls, and GC information.
    /// </summary>
    public class PerformanceStatsSystem : BaseSystem<World, float>, IPrioritizedSystem
    {
        private readonly ILogger _logger;

        // Cached query description to avoid allocations in hot paths
        private static readonly QueryDescription _allEntitiesQuery = new QueryDescription();

        // Stats tracking
        private float _fps;
        private float _frameTimeMs;
        private int _entityCount;
        private long _memoryBytes;
        private int _drawCalls;
        private int _gcGen0;
        private int _gcGen1;
        private int _gcGen2;

        // FPS calculation
        private float _fpsAccumulator;
        private int _fpsFrames;
        private float _fpsTimer;

        /// <summary>
        /// Gets the execution priority for this system.
        /// </summary>
        public int Priority => SystemPriority.PerformanceStats;

        /// <summary>
        /// Initializes a new instance of the PerformanceStatsSystem.
        /// </summary>
        /// <param name="world">The ECS world.</param>
        /// <param name="logger">The logger for logging operations.</param>
        public PerformanceStatsSystem(World world, ILogger logger)
            : base(world)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Gets the current FPS (frames per second).
        /// </summary>
        public float Fps => _fps;

        /// <summary>
        /// Gets the current frame time in milliseconds.
        /// </summary>
        public float FrameTimeMs => _frameTimeMs;

        /// <summary>
        /// Gets the current entity count in the ECS world.
        /// </summary>
        public int EntityCount => _entityCount;

        /// <summary>
        /// Gets the current memory usage in bytes.
        /// </summary>
        public long MemoryBytes => _memoryBytes;

        /// <summary>
        /// Gets the current draw call count.
        /// </summary>
        public int DrawCalls => _drawCalls;

        /// <summary>
        /// Gets the GC Generation 0 collection count.
        /// </summary>
        public int GcGen0 => _gcGen0;

        /// <summary>
        /// Gets the GC Generation 1 collection count.
        /// </summary>
        public int GcGen1 => _gcGen1;

        /// <summary>
        /// Gets the GC Generation 2 collection count.
        /// </summary>
        public int GcGen2 => _gcGen2;

        /// <summary>
        /// Increments the draw call counter. Called by render systems.
        /// </summary>
        public void IncrementDrawCalls()
        {
            _drawCalls++;
        }

        /// <summary>
        /// Updates performance statistics each frame.
        /// </summary>
        /// <param name="deltaTime">The elapsed time since last update in seconds.</param>
        /// <remarks>
        /// Note: Draw calls are reset at the start of Update, then incremented during Render() calls.
        /// The DrawCalls property shows the count from the previous frame's rendering.
        /// </remarks>
        public override void Update(in float deltaTime)
        {
            // Reset draw calls counter each frame
            // Note: This counter is incremented during Render() calls, so the value shown
            // represents the draw calls from the previous frame's rendering.
            _drawCalls = 0;

            // Update frame time (convert seconds to milliseconds)
            _frameTimeMs = deltaTime * 1000.0f;

            // Update entity count
            _entityCount = World.CountEntities(_allEntitiesQuery);

            // Update memory usage
            _memoryBytes = GC.GetTotalMemory(false);

            // Update GC collection counts
            _gcGen0 = GC.CollectionCount(0);
            _gcGen1 = GC.CollectionCount(1);
            _gcGen2 = GC.CollectionCount(2);

            // Calculate FPS (average over 1 second)
            _fpsAccumulator += deltaTime;
            _fpsFrames++;
            _fpsTimer += deltaTime;

            if (_fpsTimer >= 1.0f)
            {
                _fps = _fpsFrames / _fpsTimer;
                _fpsAccumulator = 0.0f;
                _fpsFrames = 0;
                _fpsTimer = 0.0f;
            }
        }
    }
}
