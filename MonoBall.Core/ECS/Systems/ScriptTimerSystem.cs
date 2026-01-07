using System;
using System.Collections.Generic;
using Arch.Core;
using Arch.System;
using MonoBall.Core.ECS.Components;
using MonoBall.Core.ECS.Events;
using Serilog;

namespace MonoBall.Core.ECS.Systems;

/// <summary>
///     System that processes script timers and fires TimerElapsedEvent when they expire.
///     Runs every frame to update timer elapsed time.
/// </summary>
public class ScriptTimerSystem : BaseSystem<World, float>, IPrioritizedSystem
{
    private readonly ILogger _logger;
    private readonly QueryDescription _timerQuery;

    /// <summary>
    ///     Initializes a new instance of the ScriptTimerSystem class.
    /// </summary>
    /// <param name="world">The ECS world.</param>
    /// <param name="logger">The logger instance.</param>
    public ScriptTimerSystem(World world, ILogger logger)
        : base(world)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        // Only process timers for entities in active maps
        _timerQuery = new QueryDescription().WithAll<ScriptTimersComponent, ActiveMapEntity>();
    }

    /// <summary>
    ///     Gets the execution priority for this system.
    /// </summary>
    public int Priority => SystemPriority.ScriptTimer;

    public override void Update(in float deltaTime)
    {
        // Copy deltaTime to local variable to use in lambda
        var dt = deltaTime;

        World.Query(
            in _timerQuery,
            (Entity entity, ref ScriptTimersComponent timers) =>
            {
                // Check if entity is still alive
                if (!World.IsAlive(entity))
                    return;

                // Process each timer
                // Collect keys first to avoid modifying dictionary during enumeration
                var timerIds = new List<string>(timers.Timers.Keys);
                var timersToRemove = new List<string>();

                foreach (var timerId in timerIds)
                {
                    // Skip if timer was already removed
                    if (!timers.Timers.TryGetValue(timerId, out var timer))
                        continue;

                    // Skip inactive timers
                    if (!timer.IsActive)
                    {
                        timersToRemove.Add(timerId);
                        continue;
                    }

                    // Update elapsed time
                    timer.ElapsedTime += dt;

                    // Check if timer has expired
                    if (timer.ElapsedTime >= timer.Duration)
                    {
                        // Fire timer elapsed event
                        var timerEvent = new TimerElapsedEvent
                        {
                            Entity = entity,
                            TimerId = timerId,
                            IsRepeating = timer.IsRepeating,
                        };
                        EventBus.Send(ref timerEvent);

                        // Handle repeating timers
                        if (timer.IsRepeating)
                        {
                            // Reset elapsed time for next cycle
                            timer.ElapsedTime = 0f;
                            timers.Timers[timerId] = timer;
                        }
                        else
                        {
                            // Mark timer as inactive (will be removed)
                            timer.IsActive = false;
                            timers.Timers[timerId] = timer;
                            timersToRemove.Add(timerId);
                        }
                    }
                    else
                    {
                        // Update timer with new elapsed time
                        timers.Timers[timerId] = timer;
                    }
                }

                // Remove inactive timers
                foreach (var timerId in timersToRemove)
                    timers.Timers.Remove(timerId);

                // Update the component
                World.Set(entity, timers);
            }
        );
    }
}
