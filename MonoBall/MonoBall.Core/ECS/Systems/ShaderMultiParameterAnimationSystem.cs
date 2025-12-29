using System;
using System.Collections.Generic;
using Arch.Core;
using Arch.System;
using MonoBall.Core.ECS.Components;
using MonoBall.Core.ECS.Events;
using MonoBall.Core.ECS.Utilities;
using Serilog;

namespace MonoBall.Core.ECS.Systems;

/// <summary>
///     System that animates multiple shader parameters simultaneously on an entity.
///     Animations are stored externally to avoid List&lt;T&gt; allocations in ECS components.
///     Uses ShaderAnimationUtilities for DRY easing/interpolation.
/// </summary>
public class ShaderMultiParameterAnimationSystem
    : BaseSystem<World, float>,
        IPrioritizedSystem,
        IDisposable
{
    // External storage for animations (avoids List<T> in component struct)
    private readonly Dictionary<Entity, List<ShaderAnimationData>> _animations = new();
    private readonly List<ShaderAnimationCompletedEvent> _completedEvents = new();
    private readonly List<Entity> _deadEntities = new();
    private readonly QueryDescription _entityShaderQuery;
    private readonly QueryDescription _layerShaderQuery;
    private readonly ILogger _logger;
    private readonly ShaderManagerSystem? _shaderManagerSystem;

    /// <summary>
    ///     Initializes a new instance of the ShaderMultiParameterAnimationSystem.
    /// </summary>
    public ShaderMultiParameterAnimationSystem(
        World world,
        ShaderManagerSystem? shaderManagerSystem,
        ILogger logger
    )
        : base(world)
    {
        _shaderManagerSystem = shaderManagerSystem;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _entityShaderQuery = new QueryDescription().WithAll<
            ShaderComponent,
            ShaderMultiParameterAnimationComponent
        >();
        _layerShaderQuery = new QueryDescription().WithAll<
            RenderingShaderComponent,
            ShaderMultiParameterAnimationComponent
        >();
    }

    /// <summary>
    ///     Disposes of system resources.
    /// </summary>
    public new void Dispose()
    {
        _animations.Clear();
        _deadEntities.Clear();
        _completedEvents.Clear();
    }

    /// <summary>
    ///     Gets the execution priority for this system.
    /// </summary>
    public int Priority => SystemPriority.ShaderMultiParameterAnimation;

    /// <summary>
    ///     Sets animations for an entity.
    /// </summary>
    /// <param name="entity">The entity to animate.</param>
    /// <param name="animations">The animations to apply.</param>
    public void SetAnimations(Entity entity, List<ShaderAnimationData> animations)
    {
        _animations[entity] = animations;
    }

    /// <summary>
    ///     Clears animations for an entity.
    /// </summary>
    /// <param name="entity">The entity to clear animations for.</param>
    public void ClearAnimations(Entity entity)
    {
        _animations.Remove(entity);
    }

    /// <inheritdoc />
    public override void Update(in float deltaTime)
    {
        var dt = deltaTime;
        _completedEvents.Clear();
        _deadEntities.Clear();

        // Clean up dead entities
        foreach (var kvp in _animations)
            if (!World.IsAlive(kvp.Key))
                _deadEntities.Add(kvp.Key);

        foreach (var entity in _deadEntities)
            _animations.Remove(entity);

        // Animate per-entity shader parameters
        World.Query(
            in _entityShaderQuery,
            (
                Entity entity,
                ref ShaderComponent shader,
                ref ShaderMultiParameterAnimationComponent multi
            ) =>
            {
                if (!shader.IsEnabled || !multi.IsEnabled)
                    return;

                if (!_animations.TryGetValue(entity, out var animations))
                    return;

                // Ensure dictionary exists (can't pass property by ref)
                shader.Parameters ??= new Dictionary<string, object>();

                UpdateAnimations(
                    animations,
                    shader.Parameters,
                    dt,
                    entity,
                    ShaderLayer.SpriteLayer,
                    shader.ShaderId
                );
            }
        );

        // Animate layer shader parameters
        World.Query(
            in _layerShaderQuery,
            (
                Entity entity,
                ref RenderingShaderComponent shader,
                ref ShaderMultiParameterAnimationComponent multi
            ) =>
            {
                if (!shader.IsEnabled || !multi.IsEnabled)
                    return;

                if (!_animations.TryGetValue(entity, out var animations))
                    return;

                // Ensure dictionary exists (can't pass property by ref)
                shader.Parameters ??= new Dictionary<string, object>();

                UpdateAnimations(
                    animations,
                    shader.Parameters,
                    dt,
                    entity,
                    shader.Layer,
                    shader.ShaderId
                );
            }
        );

        // Fire completion events AFTER query (Arch ECS constraint)
        foreach (var evt in _completedEvents)
        {
            var e = evt;
            EventBus.Send(ref e);
        }

        if (_completedEvents.Count > 0)
            _shaderManagerSystem?.MarkShadersDirty();
    }

    private void UpdateAnimations(
        List<ShaderAnimationData> animations,
        Dictionary<string, object> parameters,
        float deltaTime,
        Entity entity,
        ShaderLayer layer,
        string shaderId
    )
    {
        if (animations.Count == 0)
            return;

        var anyChanged = false;
        var completedIndices = new List<int>();

        for (var i = 0; i < animations.Count; i++)
        {
            var animation = animations[i];

            if (
                !ShaderAnimationUtilities.UpdateAnimation(
                    ref animation,
                    deltaTime,
                    out var interpolatedValue,
                    out var isComplete
                )
            )
            {
                _logger.Warning(
                    "Failed to interpolate animation for parameter {ParamName}",
                    animation.ParameterName
                );
                continue;
            }

            if (interpolatedValue != null)
            {
                parameters[animation.ParameterName] = interpolatedValue;
                anyChanged = true;
            }

            // Store back the updated animation data
            animations[i] = animation;

            // Collect completed (non-looping) animations
            if (isComplete && !animation.IsLooping)
            {
                completedIndices.Add(i);
                _completedEvents.Add(
                    new ShaderAnimationCompletedEvent
                    {
                        Entity = entity,
                        ParameterName = animation.ParameterName,
                        ShaderId = shaderId,
                        Layer = layer,
                        FinalValue = interpolatedValue,
                    }
                );
            }
        }

        // Remove completed animations (iterate backwards to preserve indices)
        for (var i = completedIndices.Count - 1; i >= 0; i--)
            animations.RemoveAt(completedIndices[i]);

        if (anyChanged)
            _shaderManagerSystem?.MarkShadersDirty();
    }
}
