using System;
using System.Collections.Generic;
using System.Linq;
using Arch.Core;
using Arch.System;
using MonoBall.Core.Audio;
using MonoBall.Core.ECS.Components.Audio;
using MonoBall.Core.Mods;
using Serilog;

namespace MonoBall.Core.ECS.Systems.Audio;

/// <summary>
///     System that manages looping ambient sounds attached to entities.
/// </summary>
public class AmbientSoundSystem : BaseSystem<World, float>, IPrioritizedSystem, IDisposable
{
    private readonly Dictionary<Entity, ISoundEffectInstance> _ambientInstances = new();
    private readonly QueryDescription _ambientSoundQuery;
    private readonly IAudioEngine _audioEngine;
    private readonly ILogger _logger;
    private readonly DefinitionRegistry _registry;
    private bool _disposed;

    /// <summary>
    ///     Initializes a new instance of the AmbientSoundSystem.
    /// </summary>
    /// <param name="world">The ECS world.</param>
    /// <param name="registry">The definition registry.</param>
    /// <param name="audioEngine">The audio engine for playing sounds.</param>
    /// <param name="logger">The logger for logging operations.</param>
    public AmbientSoundSystem(
        World world,
        DefinitionRegistry registry,
        IAudioEngine audioEngine,
        ILogger logger
    )
        : base(world)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _audioEngine = audioEngine ?? throw new ArgumentNullException(nameof(audioEngine));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Cache QueryDescription in constructor (required by .cursorrules)
        _ambientSoundQuery = new QueryDescription().WithAll<AmbientSoundComponent>();
    }

    /// <summary>
    ///     Disposes the system and stops all ambient sounds.
    /// </summary>
    /// <remarks>
    ///     Implements IDisposable to properly clean up event subscriptions and stop all ambient sounds.
    ///     Uses standard dispose pattern without finalizer since only managed resources are disposed.
    ///     Uses 'new' keyword because BaseSystem may have a Dispose() method with different signature.
    /// </remarks>
    public new void Dispose()
    {
        Dispose(true);
    }

    /// <summary>
    ///     Gets the execution priority for this system.
    /// </summary>
    public int Priority => SystemPriority.Audio + 20;

    public override void Update(in float deltaTime)
    {
        // Query for entities with ambient sound component
        World.Query(
            in _ambientSoundQuery,
            (Entity entity, ref AmbientSoundComponent ambient) =>
            {
                // Check if instance exists
                if (!_ambientInstances.ContainsKey(entity))
                {
                    // Start new ambient sound
                    var definition = _registry.GetById<AudioDefinition>(ambient.AudioId);
                    if (definition == null)
                    {
                        _logger.Warning("Audio definition not found: {AudioId}", ambient.AudioId);
                        return;
                    }

                    var volume = ambient.Volume >= 0 ? ambient.Volume : definition.Volume;
                    try
                    {
                        var instance = _audioEngine.PlayLoopingSound(ambient.AudioId, volume);
                        _ambientInstances[entity] = instance;
                    }
                    catch (Exception ex)
                    {
                        _logger.Warning(
                            ex,
                            "Failed to play looping sound effect {AudioId} for entity {EntityId}, skipping",
                            ambient.AudioId,
                            entity.Id
                        );
                    }
                }
                else
                {
                    // Update existing instance volume if changed
                    var instance = _ambientInstances[entity];
                    if (instance != null && !instance.IsPlaying)
                        // Instance stopped, remove it
                        _ambientInstances.Remove(entity);
                }
            }
        );

        // Clean up instances for entities that no longer have component
        var entitiesToRemove = _ambientInstances
            .Keys.Where(e => !World.Has<AmbientSoundComponent>(e))
            .ToList();

        foreach (var entity in entitiesToRemove)
        {
            var instance = _ambientInstances[entity];
            if (instance != null)
                _audioEngine.StopSound(instance);
            _ambientInstances.Remove(entity);
        }
    }

    /// <summary>
    ///     Disposes the system and stops all ambient sounds.
    /// </summary>
    /// <param name="disposing">Whether managed resources should be disposed.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                foreach (var instance in _ambientInstances.Values)
                    if (instance != null)
                        _audioEngine.StopSound(instance);

                _ambientInstances.Clear();
            }

            _disposed = true;
        }
    }
}
