using System;
using System.Collections.Generic;
using System.Linq;
using Arch.Core;
using MonoBall.Core.ECS.Components;
using MonoBall.Core.ECS.Utilities;

namespace MonoBall.Core.Scripting;

/// <summary>
///     Fluent builder for creating shader animation chains.
///     Allows sequencing multiple animation phases with delays and looping.
/// </summary>
public class ShaderAnimationBuilder
{
    private readonly List<AnimationPhase> _phases = new();
    private readonly World _world;
    private AnimationPhase _currentPhase;
    private Action<ShaderAnimationBuilder>? _onStart;

    /// <summary>
    ///     Creates a new animation builder for an entity.
    /// </summary>
    /// <param name="entity">The target entity.</param>
    /// <param name="world">The ECS world.</param>
    /// <exception cref="ArgumentException">Thrown if entity is not alive.</exception>
    public ShaderAnimationBuilder(Entity entity, World world)
    {
        if (!world.IsAlive(entity))
            throw new ArgumentException($"Entity {entity.Id} is not alive.", nameof(entity));

        Entity = entity;
        _world = world;
        _currentPhase = new AnimationPhase();
        _phases.Add(_currentPhase);
    }

    /// <summary>
    ///     Gets the target entity.
    /// </summary>
    public Entity Entity { get; }

    /// <summary>
    ///     Gets the animation phases.
    /// </summary>
    internal IReadOnlyList<AnimationPhase> Phases => _phases;

    /// <summary>
    ///     Gets whether the chain should loop.
    /// </summary>
    public bool IsLooping { get; private set; }

    /// <summary>
    ///     Gets the total number of phases.
    /// </summary>
    public int PhaseCount => _phases.Count;

    /// <summary>
    ///     Gets the total duration of all phases including delays.
    /// </summary>
    public float TotalDuration
    {
        get
        {
            var total = 0f;
            foreach (var phase in _phases)
                total += phase.Delay + phase.Duration;
            return total;
        }
    }

    /// <summary>
    ///     Adds a parameter animation to the current phase.
    /// </summary>
    /// <param name="parameterName">The shader parameter name.</param>
    /// <param name="from">Starting value.</param>
    /// <param name="to">Ending value.</param>
    /// <param name="duration">Animation duration in seconds.</param>
    /// <param name="easing">Easing function. Default is Linear.</param>
    /// <returns>This builder for chaining.</returns>
    public ShaderAnimationBuilder Animate(
        string parameterName,
        object from,
        object to,
        float duration,
        EasingFunction easing = EasingFunction.Linear
    )
    {
        if (string.IsNullOrEmpty(parameterName))
            throw new ArgumentNullException(nameof(parameterName));

        _currentPhase.Animations.Add(
            ShaderAnimationData.Create(parameterName, from, to, duration, easing)
        );

        return this;
    }

    /// <summary>
    ///     Starts a new animation phase.
    ///     Animations added after this will run in sequence after the previous phase completes.
    /// </summary>
    /// <returns>This builder for chaining.</returns>
    public ShaderAnimationBuilder Then()
    {
        _currentPhase = new AnimationPhase();
        _phases.Add(_currentPhase);
        return this;
    }

    /// <summary>
    ///     Adds a delay before the current phase starts.
    /// </summary>
    /// <param name="seconds">Delay duration in seconds.</param>
    /// <returns>This builder for chaining.</returns>
    public ShaderAnimationBuilder Wait(float seconds)
    {
        _currentPhase.Delay += seconds;
        return this;
    }

    /// <summary>
    ///     Makes the animation chain loop from the beginning when complete.
    /// </summary>
    /// <returns>This builder for chaining.</returns>
    public ShaderAnimationBuilder Loop()
    {
        IsLooping = true;
        return this;
    }

    /// <summary>
    ///     Sets a callback to execute when the chain starts.
    /// </summary>
    /// <param name="onStart">The callback action.</param>
    /// <returns>This builder for chaining.</returns>
    public ShaderAnimationBuilder OnStart(Action<ShaderAnimationBuilder> onStart)
    {
        _onStart = onStart;
        return this;
    }

    /// <summary>
    ///     Builds and starts the animation chain on the entity.
    ///     Adds ShaderAnimationChainComponent and registers phases with the system.
    /// </summary>
    /// <returns>This builder for reference.</returns>
    public ShaderAnimationBuilder Start()
    {
        // Invoke start callback
        _onStart?.Invoke(this);

        // Create the chain component
        var chainComponent = new ShaderAnimationChainComponent
        {
            CurrentPhaseIndex = 0,
            PhaseElapsedTime = 0f,
            State = ShaderAnimationChainState.Playing,
            IsLooping = IsLooping,
            IsEnabled = true,
        };

        // Add or set the component
        if (_world.Has<ShaderAnimationChainComponent>(Entity))
            _world.Set(Entity, chainComponent);
        else
            _world.Add(Entity, chainComponent);

        // Note: The ShaderAnimationChainSystem will need to register this builder's phases
        // via its SetChain method. This is handled by the API implementation.

        return this;
    }

    /// <summary>
    ///     Represents a single phase in the animation chain.
    /// </summary>
    internal class AnimationPhase
    {
        public float Delay { get; set; }
        public List<ShaderAnimationData> Animations { get; } = new();

        public float Duration => Animations.Count > 0 ? Animations.Max(a => a.Duration) : 0f;
    }
}
