using System;
using System.Collections.Generic;
using Arch.Core;
using MonoBall.Core.ECS.Components;
using MonoBall.Core.ECS.Services;
using MonoBall.Core.ECS.Systems;
using MonoBall.Core.ECS.Utilities;
using MonoBall.Core.Mods;
using MonoBall.Core.Mods.Definitions;

namespace MonoBall.Core.Scripting.Api;

/// <summary>
///     Implementation of IShaderApi providing scripting access to shader functionality.
///     Implements all three sub-interfaces: IShaderEntityApi, IShaderLayerApi, IShaderAnimationApi.
/// </summary>
public class ShaderApiImpl : IShaderApi
{
    private readonly ShaderAnimationChainSystem? _chainSystem;
    private readonly DefinitionRegistry _definitionRegistry;
    private readonly ShaderMultiParameterAnimationSystem? _multiAnimSystem;
    private readonly IShaderPresetService? _presetService;
    private readonly ShaderManagerSystem? _shaderManagerSystem;
    private readonly ShaderTransitionSystem? _transitionSystem;
    private readonly World _world;

    /// <summary>
    ///     Creates a new shader API implementation.
    /// </summary>
    public ShaderApiImpl(
        World world,
        DefinitionRegistry definitionRegistry,
        ShaderManagerSystem? shaderManagerSystem = null,
        ShaderTransitionSystem? transitionSystem = null,
        ShaderMultiParameterAnimationSystem? multiAnimSystem = null,
        ShaderAnimationChainSystem? chainSystem = null,
        IShaderPresetService? presetService = null
    )
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _definitionRegistry =
            definitionRegistry ?? throw new ArgumentNullException(nameof(definitionRegistry));
        _shaderManagerSystem = shaderManagerSystem;
        _transitionSystem = transitionSystem;
        _multiAnimSystem = multiAnimSystem;
        _chainSystem = chainSystem;
        _presetService = presetService;
    }

    #region IShaderEntityApi

    /// <inheritdoc />
    public void EnableShader(Entity entity)
    {
        ValidateEntityAlive(entity);
        ValidateHasShaderComponent(entity);

        ref var shader = ref _world.Get<ShaderComponent>(entity);
        shader.IsEnabled = true;
        _shaderManagerSystem?.MarkShadersDirty();
    }

    /// <inheritdoc />
    public void DisableShader(Entity entity)
    {
        ValidateEntityAlive(entity);
        ValidateHasShaderComponent(entity);

        ref var shader = ref _world.Get<ShaderComponent>(entity);
        shader.IsEnabled = false;
        _shaderManagerSystem?.MarkShadersDirty();
    }

    /// <inheritdoc />
    public bool IsShaderEnabled(Entity entity)
    {
        if (!_world.IsAlive(entity) || !_world.Has<ShaderComponent>(entity))
            return false;

        return _world.Get<ShaderComponent>(entity).IsEnabled;
    }

    /// <inheritdoc />
    public void SetParameter(Entity entity, string paramName, object value)
    {
        if (string.IsNullOrEmpty(paramName))
            throw new ArgumentNullException(nameof(paramName));

        ValidateEntityAlive(entity);
        ValidateHasShaderComponent(entity);

        ref var shader = ref _world.Get<ShaderComponent>(entity);
        shader.Parameters ??= new Dictionary<string, object>();
        shader.Parameters[paramName] = value;
        _shaderManagerSystem?.MarkShadersDirty();
    }

    /// <inheritdoc />
    public object? GetParameter(Entity entity, string paramName)
    {
        if (!_world.IsAlive(entity) || !_world.Has<ShaderComponent>(entity))
            return null;

        var shader = _world.Get<ShaderComponent>(entity);
        if (shader.Parameters == null)
            return null;

        return shader.Parameters.TryGetValue(paramName, out var value) ? value : null;
    }

    /// <inheritdoc />
    public string? GetShaderId(Entity entity)
    {
        if (!_world.IsAlive(entity) || !_world.Has<ShaderComponent>(entity))
            return null;

        return _world.Get<ShaderComponent>(entity).ShaderId;
    }

    #endregion

    #region IShaderLayerApi

    /// <inheritdoc />
    public Entity? AddLayerShader(ShaderLayer layer, string shaderId, int renderOrder = 0)
    {
        if (string.IsNullOrEmpty(shaderId))
            throw new ArgumentNullException(nameof(shaderId));

        // Verify shader definition exists
        var shaderDef = _definitionRegistry.GetById<ShaderDefinition>(shaderId);
        if (shaderDef == null)
            return null;

        var entity = _world.Create(
            new RenderingShaderComponent
            {
                Layer = layer,
                ShaderId = shaderId,
                IsEnabled = true,
                RenderOrder = renderOrder,
                BlendMode = ShaderBlendMode.Replace,
                Parameters = null,
                SceneEntity = null,
            }
        );

        _shaderManagerSystem?.MarkShadersDirty();
        return entity;
    }

    /// <inheritdoc />
    public void RemoveLayerShader(Entity shaderEntity)
    {
        ValidateEntityAlive(shaderEntity);

        _world.Destroy(shaderEntity);
        _shaderManagerSystem?.MarkShadersDirty();
    }

    /// <inheritdoc />
    public bool EnableLayerShader(ShaderLayer layer, string shaderId)
    {
        var entity = FindLayerShader(layer, shaderId);
        if (!entity.HasValue)
            return false;

        ref var shader = ref _world.Get<RenderingShaderComponent>(entity.Value);
        shader.IsEnabled = true;
        _shaderManagerSystem?.MarkShadersDirty();
        return true;
    }

    /// <inheritdoc />
    public bool DisableLayerShader(ShaderLayer layer, string shaderId)
    {
        var entity = FindLayerShader(layer, shaderId);
        if (!entity.HasValue)
            return false;

        ref var shader = ref _world.Get<RenderingShaderComponent>(entity.Value);
        shader.IsEnabled = false;
        _shaderManagerSystem?.MarkShadersDirty();
        return true;
    }

    /// <inheritdoc />
    public bool SetLayerParameter(
        ShaderLayer layer,
        string shaderId,
        string paramName,
        object value
    )
    {
        var entity = FindLayerShader(layer, shaderId);
        if (!entity.HasValue)
            return false;

        ref var shader = ref _world.Get<RenderingShaderComponent>(entity.Value);
        shader.Parameters ??= new Dictionary<string, object>();
        shader.Parameters[paramName] = value;
        _shaderManagerSystem?.MarkShadersDirty();
        return true;
    }

    /// <inheritdoc />
    public object? GetLayerParameter(ShaderLayer layer, string shaderId, string paramName)
    {
        var entity = FindLayerShader(layer, shaderId);
        if (!entity.HasValue)
            return null;

        var shader = _world.Get<RenderingShaderComponent>(entity.Value);
        if (shader.Parameters == null)
            return null;

        return shader.Parameters.TryGetValue(paramName, out var value) ? value : null;
    }

    /// <inheritdoc />
    public Entity? FindLayerShader(ShaderLayer layer, string shaderId)
    {
        var query = new QueryDescription().WithAll<RenderingShaderComponent>();
        Entity? found = null;

        _world.Query(
            in query,
            (Entity entity, ref RenderingShaderComponent shader) =>
            {
                if (shader.Layer == layer && shader.ShaderId == shaderId)
                    found = entity;
            }
        );

        return found;
    }

    /// <inheritdoc />
    public IEnumerable<Entity> GetLayerShaders(ShaderLayer layer)
    {
        var result = new List<Entity>();
        var query = new QueryDescription().WithAll<RenderingShaderComponent>();

        _world.Query(
            in query,
            (Entity entity, ref RenderingShaderComponent shader) =>
            {
                if (shader.Layer == layer)
                    result.Add(entity);
            }
        );

        return result;
    }

    #endregion

    #region IShaderAnimationApi

    /// <inheritdoc />
    public void AnimateParameter(
        Entity entity,
        string paramName,
        object from,
        object to,
        float duration,
        EasingFunction easing = EasingFunction.Linear,
        bool isLooping = false,
        bool pingPong = false
    )
    {
        if (string.IsNullOrEmpty(paramName))
            throw new ArgumentNullException(nameof(paramName));

        ValidateEntityAlive(entity);

        // Create animation component
        var animation = new ShaderParameterAnimationComponent
        {
            ParameterName = paramName,
            StartValue = from,
            EndValue = to,
            Duration = duration,
            ElapsedTime = 0f,
            Easing = easing,
            IsLooping = isLooping,
            PingPong = pingPong,
            IsEnabled = true,
        };

        if (_world.Has<ShaderParameterAnimationComponent>(entity))
            _world.Set(entity, animation);
        else
            _world.Add(entity, animation);
    }

    /// <inheritdoc />
    public bool StopAnimation(Entity entity, string paramName)
    {
        if (!_world.IsAlive(entity) || !_world.Has<ShaderParameterAnimationComponent>(entity))
            return false;

        var animation = _world.Get<ShaderParameterAnimationComponent>(entity);
        if (animation.ParameterName == paramName)
        {
            _world.Remove<ShaderParameterAnimationComponent>(entity);
            return true;
        }

        return false;
    }

    /// <inheritdoc />
    public Entity? TransitionToShader(
        ShaderLayer layer,
        string? fromShaderId,
        string toShaderId,
        float duration,
        EasingFunction easing = EasingFunction.Linear
    )
    {
        if (string.IsNullOrEmpty(toShaderId))
            throw new ArgumentNullException(nameof(toShaderId));

        if (_transitionSystem == null)
            return null;

        // Find existing shader entity or create new one
        Entity? shaderEntity = null;
        if (!string.IsNullOrEmpty(fromShaderId))
            shaderEntity = FindLayerShader(layer, fromShaderId);

        if (!shaderEntity.HasValue)
        {
            // Create new shader entity for transition
            shaderEntity = AddLayerShader(layer, toShaderId);
            if (!shaderEntity.HasValue)
                return null;
        }

        _transitionSystem.StartTransition(
            shaderEntity.Value,
            fromShaderId,
            toShaderId,
            duration,
            easing
        );
        return shaderEntity;
    }

    /// <inheritdoc />
    public void ApplyPreset(Entity entity, string presetId, float transitionDuration = 0)
    {
        if (string.IsNullOrEmpty(presetId))
            throw new ArgumentNullException(nameof(presetId));

        ValidateEntityAlive(entity);

        if (_presetService == null)
            return;

        var parameters = _presetService.ResolveParameters(presetId);
        if (parameters.Count == 0)
            return;

        if (transitionDuration <= 0)
        {
            // Apply immediately
            foreach (var kvp in parameters)
                if (_world.Has<ShaderComponent>(entity))
                {
                    SetParameter(entity, kvp.Key, kvp.Value);
                }
                else if (_world.Has<RenderingShaderComponent>(entity))
                {
                    ref var shader = ref _world.Get<RenderingShaderComponent>(entity);
                    shader.Parameters ??= new Dictionary<string, object>();
                    shader.Parameters[kvp.Key] = kvp.Value;
                }

            _shaderManagerSystem?.MarkShadersDirty();
        }
        else
        {
            // Animate to new values
            var animations = new List<ShaderAnimationData>();

            foreach (var kvp in parameters)
            {
                var currentValue = GetCurrentValue(entity, kvp.Key);
                if (currentValue != null)
                    animations.Add(
                        ShaderAnimationData.Create(
                            kvp.Key,
                            currentValue,
                            kvp.Value,
                            transitionDuration,
                            EasingFunction.EaseInOut
                        )
                    );
            }

            if (animations.Count > 0 && _multiAnimSystem != null)
            {
                _multiAnimSystem.SetAnimations(entity, animations);

                var multiComp = ShaderMultiParameterAnimationComponent.Create(presetId);
                if (_world.Has<ShaderMultiParameterAnimationComponent>(entity))
                    _world.Set(entity, multiComp);
                else
                    _world.Add(entity, multiComp);
            }
        }
    }

    /// <inheritdoc />
    public ShaderAnimationBuilder CreateAnimationChain(Entity entity)
    {
        ValidateEntityAlive(entity);
        return new ShaderAnimationBuilder(entity, _world);
    }

    #endregion

    #region Private Helpers

    private void ValidateEntityAlive(Entity entity)
    {
        if (!_world.IsAlive(entity))
            throw new ArgumentException($"Entity {entity.Id} is not alive.", nameof(entity));
    }

    private void ValidateHasShaderComponent(Entity entity)
    {
        if (!_world.Has<ShaderComponent>(entity))
            throw new InvalidOperationException(
                $"Entity {entity.Id} does not have ShaderComponent."
            );
    }

    private object? GetCurrentValue(Entity entity, string paramName)
    {
        if (_world.Has<ShaderComponent>(entity))
            return GetParameter(entity, paramName);

        if (_world.Has<RenderingShaderComponent>(entity))
        {
            var shader = _world.Get<RenderingShaderComponent>(entity);
            if (shader.Parameters?.TryGetValue(paramName, out var value) == true)
                return value;
        }

        return null;
    }

    #endregion
}
