using System;
using Arch.Core;
using Arch.System;
using MonoBall.Core.Audio;
using MonoBall.Core.ECS.Components.Audio;
using MonoBall.Core.Mods;
using Serilog;

namespace MonoBall.Core.ECS.Systems.Audio;

/// <summary>
///     System that processes sound effect requests and plays them.
/// </summary>
public class SoundEffectSystem : BaseSystem<World, float>, IPrioritizedSystem, IDisposable
{
    private readonly IAudioEngine _audioEngine;
    private readonly ILogger _logger;
    private readonly DefinitionRegistry _registry;
    private readonly QueryDescription _soundEffectQuery;

    /// <summary>
    ///     Initializes a new instance of the SoundEffectSystem.
    /// </summary>
    /// <param name="world">The ECS world.</param>
    /// <param name="registry">The definition registry.</param>
    /// <param name="audioEngine">The audio engine for playing sounds.</param>
    /// <param name="logger">The logger for logging operations.</param>
    public SoundEffectSystem(
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
        _soundEffectQuery = new QueryDescription().WithAll<SoundEffectRequestComponent>();
    }

    /// <summary>
    ///     Disposes the system.
    /// </summary>
    /// <remarks>
    ///     Currently a no-op as this system has no managed resources to dispose.
    ///     Implements IDisposable for consistency with other systems.
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
    public int Priority => SystemPriority.Audio + 10;

    public override void Update(in float deltaTime)
    {
        World.Query(
            in _soundEffectQuery,
            (Entity entity, ref SoundEffectRequestComponent request) =>
            {
                // Get definition from registry
                var definition = _registry.GetById<AudioDefinition>(request.AudioId);
                if (definition == null)
                {
                    _logger.Warning("Audio definition not found: {AudioId}", request.AudioId);
                    World.Remove<SoundEffectRequestComponent>(entity);
                    return;
                }

                // Determine volume (use request override or definition default)
                var volume = request.Volume >= 0 ? request.Volume : definition.Volume;

                // Play sound effect
                _audioEngine.PlaySound(request.AudioId, volume, request.Pitch, request.Pan);

                // Remove component after processing
                World.Remove<SoundEffectRequestComponent>(entity);
            }
        );
    }

    /// <summary>
    ///     Disposes the system.
    /// </summary>
    /// <param name="disposing">Whether managed resources should be disposed.</param>
    protected virtual void Dispose(bool disposing)
    {
        // No resources to dispose - this system doesn't subscribe to events
        // Implemented for consistency with other systems
    }
}
