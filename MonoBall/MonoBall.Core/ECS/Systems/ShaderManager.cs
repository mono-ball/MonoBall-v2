using System;
using System.Collections.Generic;
using System.Linq;
using Arch.Core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoBall.Core.ECS.Components;
using MonoBall.Core.ECS.Events;
using MonoBall.Core.Mods;
using MonoBall.Core.Mods.Definitions;
using MonoBall.Core.Rendering;
using Serilog;

namespace MonoBall.Core.ECS.Systems;

/// <summary>
///     Manages shader effects and updates their parameters.
///     Shader state is updated in Render phase to avoid timing mismatches.
///     Note: This is NOT an ECS System (no BaseSystem inheritance) - it's a Render-phase
///     helper that is called explicitly during rendering.
/// </summary>
public class ShaderManager
{
    /// <summary>
    ///     Parameters that are automatically set by MonoGame/SpriteBatch and should not be required in shader definitions.
    ///     These are set automatically when SpriteBatch.Begin() is called with an Effect.
    /// </summary>
    private static readonly HashSet<string> MonoGameManagedParameters = new()
    {
        "SpriteTexture", // Set automatically by SpriteBatch
        "Texture", // Alternative name for SpriteTexture
        "WorldViewProjection", // Transformation matrix set by SpriteBatch
        "MatrixTransform", // Alternative name for WorldViewProjection
    };

    private readonly List<(
        Effect effect,
        ShaderBlendMode blendMode,
        Entity entity
    )> _activeCombinedLayerShaders = new();

    private readonly List<(
        Effect effect,
        ShaderBlendMode blendMode,
        Entity entity
    )> _activeSpriteLayerShaders = new();

    // Cached active shader stacks per layer
    private readonly List<(
        Effect effect,
        ShaderBlendMode blendMode,
        Entity entity
    )> _activeTileLayerShaders = new();

    private readonly List<(Entity entity, RenderingShaderComponent shader)> _combinedShaders =
        new();

    private readonly GraphicsDevice _graphicsDevice;
    private readonly ILogger _logger;
    private readonly IModManager? _modManager;
    private readonly IShaderParameterValidator _parameterValidator;

    // Track previous parameter values per entity for dirty tracking
    private readonly Dictionary<Entity, Dictionary<string, object>> _previousParameterValues =
        new();

    private readonly QueryDescription _renderingShaderQuery;
    private readonly IShaderService _shaderService;

    private readonly List<(Entity entity, RenderingShaderComponent shader)> _spriteShaders = new();

    // Reusable collections to avoid allocations
    private readonly List<(Entity entity, RenderingShaderComponent shader)> _tileShaders = new();

    private readonly World _world;
    private Entity? _combinedShaderEntity;
    private string? _previousCombinedShaderId;
    private string? _previousSpriteShaderId;

    // Track previous shader IDs for change detection (for backward compatibility)
    private string? _previousTileShaderId;

    // Dirty flag to avoid unnecessary updates
    private bool _shadersDirty = true;
    private Entity? _spriteShaderEntity;

    // Track shader entities for event firing (for backward compatibility)
    private Entity? _tileShaderEntity;

    /// <summary>
    ///     Initializes a new instance of the ShaderManager.
    /// </summary>
    /// <param name="world">The ECS world.</param>
    /// <param name="shaderService">The shader service for loading shaders.</param>
    /// <param name="parameterValidator">The parameter validator for validating shader parameters.</param>
    /// <param name="graphicsDevice">The graphics device for getting viewport dimensions.</param>
    /// <param name="logger">The logger for logging operations.</param>
    /// <param name="modManager">Optional mod manager for compatibility checking.</param>
    public ShaderManager(
        World world,
        IShaderService shaderService,
        IShaderParameterValidator parameterValidator,
        GraphicsDevice graphicsDevice,
        ILogger logger,
        IModManager? modManager = null
    )
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _shaderService = shaderService ?? throw new ArgumentNullException(nameof(shaderService));
        _parameterValidator =
            parameterValidator ?? throw new ArgumentNullException(nameof(parameterValidator));
        _graphicsDevice = graphicsDevice ?? throw new ArgumentNullException(nameof(graphicsDevice));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _modManager = modManager;
        _renderingShaderQuery = new QueryDescription().WithAll<RenderingShaderComponent>();
    }

    /// <summary>
    ///     Updates shader state. Called in Render phase, just before rendering systems need shaders.
    /// </summary>
    /// <param name="sceneEntity">Optional scene entity to filter shaders. If null, includes global shaders only.</param>
    public void UpdateShaderState(Entity? sceneEntity = null)
    {
        // Always update active shaders if dirty, or if we don't have active shaders but might have components
        // This ensures shaders are found even if MarkShadersDirty() wasn't called
        if (
            _shadersDirty
            || (
                _activeTileLayerShaders.Count == 0
                && _activeSpriteLayerShaders.Count == 0
                && _activeCombinedLayerShaders.Count == 0
            )
        )
        {
            UpdateActiveShaders(sceneEntity);
            _shadersDirty = false;
        }

        UpdateShaderParameters();
    }

    /// <summary>
    ///     Gets the active shader stack for tile layer rendering.
    ///     Returns all enabled shaders sorted by RenderOrder.
    /// </summary>
    /// <param name="sceneEntity">Optional scene entity to filter shaders. If null, returns global shaders only.</param>
    public IReadOnlyList<(
        Effect effect,
        ShaderBlendMode blendMode,
        Entity entity
    )> GetTileLayerShaderStack(Entity? sceneEntity = null)
    {
        if (sceneEntity == null)
            return _activeTileLayerShaders;

        // Filter by scene entity (include global shaders with null SceneEntity and shaders matching this scene)
        return _activeTileLayerShaders
            .Where(s =>
            {
                if (!_world.Has<RenderingShaderComponent>(s.entity))
                    return false;
                ref var shader = ref _world.Get<RenderingShaderComponent>(s.entity);
                return shader.SceneEntity == null || shader.SceneEntity == sceneEntity;
            })
            .ToList();
    }

    /// <summary>
    ///     Gets the active shader stack for sprite layer rendering.
    ///     Returns all enabled shaders sorted by RenderOrder.
    /// </summary>
    /// <param name="sceneEntity">Optional scene entity to filter shaders. If null, returns global shaders only.</param>
    public IReadOnlyList<(
        Effect effect,
        ShaderBlendMode blendMode,
        Entity entity
    )> GetSpriteLayerShaderStack(Entity? sceneEntity = null)
    {
        if (sceneEntity == null)
            return _activeSpriteLayerShaders;

        // Filter by scene entity (include global shaders with null SceneEntity and shaders matching this scene)
        return _activeSpriteLayerShaders
            .Where(s =>
            {
                if (!_world.Has<RenderingShaderComponent>(s.entity))
                    return false;
                ref var shader = ref _world.Get<RenderingShaderComponent>(s.entity);
                return shader.SceneEntity == null || shader.SceneEntity == sceneEntity;
            })
            .ToList();
    }

    /// <summary>
    ///     Gets the active shader stack for combined layer rendering (post-processing).
    ///     Returns all enabled shaders sorted by RenderOrder.
    /// </summary>
    /// <param name="sceneEntity">Optional scene entity to filter shaders. If null, returns global shaders only.</param>
    public IReadOnlyList<(
        Effect effect,
        ShaderBlendMode blendMode,
        Entity entity
    )> GetCombinedLayerShaderStack(Entity? sceneEntity = null)
    {
        if (sceneEntity == null)
            return _activeCombinedLayerShaders;

        // Filter by scene entity (include global shaders with null SceneEntity and shaders matching this scene)
        return _activeCombinedLayerShaders
            .Where(s =>
            {
                if (!_world.Has<RenderingShaderComponent>(s.entity))
                    return false;
                ref var shader = ref _world.Get<RenderingShaderComponent>(s.entity);
                return shader.SceneEntity == null || shader.SceneEntity == sceneEntity;
            })
            .ToList();
    }

    /// <summary>
    ///     Gets the active shader for tile layer rendering (backward compatibility).
    ///     Returns the first shader from the stack, or null if no shaders.
    /// </summary>
    public Effect? GetTileLayerShader()
    {
        return _activeTileLayerShaders.Count > 0 ? _activeTileLayerShaders[0].effect : null;
    }

    /// <summary>
    ///     Gets the active shader for sprite layer rendering (backward compatibility).
    ///     Returns the first shader from the stack, or null if no shaders.
    /// </summary>
    public Effect? GetSpriteLayerShader()
    {
        return _activeSpriteLayerShaders.Count > 0 ? _activeSpriteLayerShaders[0].effect : null;
    }

    /// <summary>
    ///     Gets the active shader for combined layer rendering (backward compatibility).
    ///     Returns the first shader from the stack, or null if no shaders.
    /// </summary>
    public Effect? GetCombinedLayerShader()
    {
        return _activeCombinedLayerShaders.Count > 0 ? _activeCombinedLayerShaders[0].effect : null;
    }

    /// <summary>
    ///     Forces update of all parameters for all active combined layer shaders.
    ///     Called right before SpriteBatch.Begin() in Immediate mode to ensure parameters are set.
    /// </summary>
    public void ForceUpdateCombinedLayerParameters()
    {
        foreach (var (effect, _, entity) in _activeCombinedLayerShaders)
            UpdateShaderParametersForEntity(entity, effect);
    }

    /// <summary>
    ///     Updates dynamic parameters for all active combined layer shaders.
    ///     Called before applying post-processing to ensure correct parameter values.
    /// </summary>
    /// <param name="parameters">Dictionary of parameter names to values.</param>
    public void UpdateCombinedLayerDynamicParameters(Dictionary<string, object> parameters)
    {
        if (_activeCombinedLayerShaders.Count == 0 || parameters == null || parameters.Count == 0)
            return;

        foreach (var (effect, _, entity) in _activeCombinedLayerShaders)
        foreach (var (paramName, value) in parameters)
        {
            // Skip MonoGame-managed parameters - these are set automatically
            if (MonoGameManagedParameters.Contains(paramName))
                continue;

            // Check if parameter exists before trying to set it
            EffectParameter? param = null;
            try
            {
                param = effect.Parameters[paramName];
            }
            catch (KeyNotFoundException)
            {
                // Parameter doesn't exist - skip it (may not be in all shaders)
                _logger.Debug(
                    "Parameter {ParamName} does not exist in combined layer shader, skipping.",
                    paramName
                );
                continue;
            }

            if (param == null)
            {
                // Parameter is null - skip it
                _logger.Debug(
                    "Parameter {ParamName} is null in combined layer shader, skipping.",
                    paramName
                );
                continue;
            }

            try
            {
                ShaderParameterApplier.ApplyParameter(effect, paramName, value, _logger);
            }
            catch (InvalidOperationException ex)
            {
                // Log and continue - one parameter failure shouldn't stop others
                _logger.Warning(
                    ex,
                    "Failed to set dynamic parameter {ParamName} on combined layer shader",
                    paramName
                );
            }
        }
    }

    /// <summary>
    ///     Marks shaders as dirty, forcing an update on next UpdateShaderState() call.
    ///     Called when components are added/removed/modified.
    /// </summary>
    public void MarkShadersDirty()
    {
        _shadersDirty = true;
    }

    /// <summary>
    ///     Updates the active shader lists based on current RenderingShaderComponent entities.
    /// </summary>
    /// <param name="sceneEntity">
    ///     Optional scene entity to filter shaders. If null, includes all shaders (global and
    ///     per-scene).
    /// </param>
    private void UpdateActiveShaders(Entity? sceneEntity = null)
    {
        // Clear reusable collections
        _tileShaders.Clear();
        _spriteShaders.Clear();
        _combinedShaders.Clear();

        var totalFound = 0;
        _world.Query(
            in _renderingShaderQuery,
            (Entity entity, ref RenderingShaderComponent shader) =>
            {
                totalFound++;

                // Filter by scene entity if provided
                // Include shaders with null SceneEntity (global) and shaders matching the scene
                if (sceneEntity != null)
                    if (shader.SceneEntity != null && shader.SceneEntity != sceneEntity)
                        // Shader is scoped to a different scene, skip it
                        return;

                if (!shader.IsEnabled)
                    return;

                switch (shader.Layer)
                {
                    case ShaderLayer.TileLayer:
                        _tileShaders.Add((entity, shader));
                        break;
                    case ShaderLayer.SpriteLayer:
                        _spriteShaders.Add((entity, shader));
                        break;
                    case ShaderLayer.CombinedLayer:
                        _combinedShaders.Add((entity, shader));
                        break;
                }
            }
        );

        // Only log when shaders are actually active (avoid log spam every frame)
        if (totalFound > 0)
            _logger.Debug(
                "ShaderManager: Active shaders - Tile: {TileCount}, Sprite: {SpriteCount}, Combined: {CombinedCount}",
                _tileShaders.Count,
                _spriteShaders.Count,
                _combinedShaders.Count
            );

        // Select all enabled shaders for each layer (sorted by RenderOrder)
        UpdateLayerShaderStack(
            _tileShaders,
            ShaderLayer.TileLayer,
            _activeTileLayerShaders,
            ref _previousTileShaderId,
            ref _tileShaderEntity
        );
        UpdateLayerShaderStack(
            _spriteShaders,
            ShaderLayer.SpriteLayer,
            _activeSpriteLayerShaders,
            ref _previousSpriteShaderId,
            ref _spriteShaderEntity
        );
        UpdateLayerShaderStack(
            _combinedShaders,
            ShaderLayer.CombinedLayer,
            _activeCombinedLayerShaders,
            ref _previousCombinedShaderId,
            ref _combinedShaderEntity
        );
    }

    private void UpdateLayerShaderStack(
        List<(Entity entity, RenderingShaderComponent shader)> shaders,
        ShaderLayer layer,
        List<(Effect effect, ShaderBlendMode blendMode, Entity entity)> activeShaderStack,
        ref string? previousShaderId,
        ref Entity? shaderEntity
    )
    {
        // Clear current stack
        activeShaderStack.Clear();

        if (shaders.Count == 0)
        {
            if (previousShaderId != null)
            {
                // Shader was disabled
                FireShaderChangedEvent(layer, previousShaderId, null, shaderEntity ?? default);
                previousShaderId = null;
                shaderEntity = null;
            }

            return;
        }

        // Sort by RenderOrder (lowest first), then by entity ID for stable ordering
        shaders.Sort(
            (a, b) =>
            {
                var orderComparison = a.shader.RenderOrder.CompareTo(b.shader.RenderOrder);
                if (orderComparison != 0)
                    return orderComparison;
                return a.entity.Id.CompareTo(b.entity.Id);
            }
        );

        // Load all shaders
        foreach (var (entity, shaderComp) in shaders)
        {
            // Check if shader exists first (optional shader support)
            if (!_shaderService.HasShader(shaderComp.ShaderId))
            {
                _logger.Warning(
                    "Shader {ShaderId} not found for layer {Layer}, skipping",
                    shaderComp.ShaderId,
                    layer
                );
                continue; // Skip missing shader, continue with others
            }

            // Load shader (fail fast if loading fails)
            Effect effect;
            try
            {
                effect = _shaderService.GetShader(shaderComp.ShaderId);
            }
            catch (Exception ex)
            {
                _logger.Warning(
                    ex,
                    "Shader {ShaderId} failed to load for layer {Layer}, skipping",
                    shaderComp.ShaderId,
                    layer
                );
                continue; // Skip failed shader, continue with others
            }

            // Set CurrentTechnique (use first technique if not explicitly set)
            ShaderParameterApplier.EnsureCurrentTechnique(effect, _logger);

            // Add to stack
            activeShaderStack.Add((effect, shaderComp.BlendMode, entity));

            // Clear previous parameter values when shader is added
            if (_previousParameterValues.ContainsKey(entity))
                _previousParameterValues[entity].Clear();
        }

        // Check if first shader changed (for backward compatibility and events)
        if (activeShaderStack.Count > 0)
        {
            var firstShader = activeShaderStack[0];
            ref var firstShaderComp = ref _world.Get<RenderingShaderComponent>(firstShader.entity);

            if (firstShaderComp.ShaderId != previousShaderId)
            {
                FireShaderChangedEvent(
                    layer,
                    previousShaderId,
                    firstShaderComp.ShaderId,
                    firstShader.entity
                );
                previousShaderId = firstShaderComp.ShaderId;
                shaderEntity = firstShader.entity;
            }
        }
    }

    private void UpdateShaderParameters()
    {
        // Update shader parameters for all active shaders in stacks
        // Validate parameters before applying
        foreach (var (effect, _, entity) in _activeTileLayerShaders)
            UpdateShaderParametersForEntity(entity, effect);

        foreach (var (effect, _, entity) in _activeSpriteLayerShaders)
            UpdateShaderParametersForEntity(entity, effect);

        foreach (var (effect, _, entity) in _activeCombinedLayerShaders)
            UpdateShaderParametersForEntity(entity, effect);
    }

    private void UpdateShaderParametersForEntity(Entity entity, Effect effect)
    {
        // CRITICAL: Check entity is alive before accessing components
        if (!_world.IsAlive(entity))
            return;

        if (!_world.Has<RenderingShaderComponent>(entity))
            return;

        // Ensure CurrentTechnique is set before setting parameters
        ShaderParameterApplier.EnsureCurrentTechnique(effect, _logger);

        ref var shader = ref _world.Get<RenderingShaderComponent>(entity);

        // Get shader definition to access parameter defaults
        ShaderDefinition? shaderDef = null;
        if (_modManager != null)
            shaderDef = _modManager.GetDefinition<ShaderDefinition>(shader.ShaderId);

        // Get or create previous parameter values dictionary for this entity
        if (!_previousParameterValues.TryGetValue(entity, out var previousValues))
        {
            previousValues = new Dictionary<string, object>();
            _previousParameterValues[entity] = previousValues;
        }

        // Get component parameters (may be null)
        var componentParameters = shader.Parameters ?? new Dictionary<string, object>();

        // Build a set of all parameters that exist in the shader effect
        // We MUST set all of these - no optional parameters allowed
        var allShaderParameters = new Dictionary<string, EffectParameter>();
        foreach (var param in effect.Parameters)
            if (param != null)
                allShaderParameters[param.Name] = param;

        // Process all parameters from the shader definition (if available)
        // This ensures all defined parameters are set, using defaults if not specified
        if (shaderDef?.Parameters != null)
            foreach (var paramDef in shaderDef.Parameters)
            {
                // Skip MonoGame-managed parameters - these are set automatically by SpriteBatch
                if (MonoGameManagedParameters.Contains(paramDef.Name))
                    continue;

                // Parameter MUST exist in the shader effect if it's in the definition
                if (!allShaderParameters.TryGetValue(paramDef.Name, out var effectParam))
                    throw new InvalidOperationException(
                        $"Shader '{shader.ShaderId}' definition lists parameter '{paramDef.Name}', "
                            + $"but shader effect doesn't have it. Shader definitions must match shader code."
                    );

                // Determine the value to use: component value or default value
                object? valueToUse = null;
                var isFromComponent = false;

                if (componentParameters.TryGetValue(paramDef.Name, out var componentValue))
                {
                    // Use value from component
                    valueToUse = componentValue;
                    isFromComponent = true;
                }
                else if (paramDef.DefaultValue != null)
                {
                    // Use default value from definition
                    valueToUse = paramDef.DefaultValue;
                    isFromComponent = false;
                }
                else
                {
                    // Parameter is required but has no default and wasn't provided
                    throw new InvalidOperationException(
                        $"Shader '{shader.ShaderId}' requires parameter '{paramDef.Name}' "
                            + $"but no value was provided and no default value is defined in the shader definition."
                    );
                }

                // Get old value for event (before checking if changed)
                var oldValue = previousValues.TryGetValue(paramDef.Name, out var existingValue)
                    ? existingValue
                    : null;

                // Check if parameter value has changed (dirty tracking)
                if (oldValue != null && AreParameterValuesEqual(valueToUse, oldValue))
                    // Parameter hasn't changed, skip setting it
                    continue;

                // Validate parameter
                if (
                    !_parameterValidator.ValidateParameter(
                        shader.ShaderId,
                        paramDef.Name,
                        valueToUse,
                        out var error
                    )
                )
                    throw new InvalidOperationException(
                        $"Invalid parameter '{paramDef.Name}' for shader '{shader.ShaderId}': {error}"
                    );

                // Apply parameter
                ApplyShaderParameter(effect, paramDef.Name, valueToUse);

                // Update previous value for dirty tracking
                previousValues[paramDef.Name] = valueToUse;

                // Fire event for parameter change (only if value actually changed or was set from component)
                if (
                    isFromComponent
                    || oldValue == null
                    || !AreParameterValuesEqual(valueToUse, oldValue)
                )
                {
                    var evt = new ShaderParameterChangedEvent
                    {
                        Layer = shader.Layer,
                        ShaderId = shader.ShaderId,
                        ParameterName = paramDef.Name,
                        OldValue = oldValue,
                        NewValue = valueToUse,
                        ShaderEntity = entity,
                    };
                    EventBus.Send(ref evt);
                }
            }

        // Second, ensure ALL parameters in the shader effect are set (no optional parameters)
        // Check for any parameters that exist in the shader but weren't set above
        // Note: If _modManager is null, we can't validate against definitions, so we skip this check
        if (_modManager != null)
            foreach (var (paramName, effectParam) in allShaderParameters)
            {
                // Skip MonoGame-managed parameters - these are set automatically by SpriteBatch
                if (MonoGameManagedParameters.Contains(paramName))
                    continue;

                // Skip if already processed from definition
                var alreadyProcessed =
                    shaderDef?.Parameters?.Any(p => p.Name == paramName) ?? false;
                if (alreadyProcessed)
                    continue;

                // Skip if set from component (will be processed in next loop)
                if (componentParameters.ContainsKey(paramName))
                    continue;

                // Parameter exists in shader but has no definition and no component value
                // This is an error - all parameters must be defined or have defaults
                throw new InvalidOperationException(
                    $"Shader '{shader.ShaderId}' has parameter '{paramName}' in the effect, "
                        + $"but it's not defined in the shader definition and no value was provided. "
                        + $"All shader parameters must be defined in the shader definition with a default value. "
                        + $"Note: Parameters like 'SpriteTexture' and 'WorldViewProjection' are set automatically by MonoGame."
                );
            }
        // else: Without mod manager, we skip definition-based validation
        // Parameters from component will still be processed below

        // Process any additional parameters from component that aren't in the definition
        // This allows runtime parameters that aren't in the definition (for flexibility)
        foreach (var (paramName, value) in componentParameters)
        {
            // Skip MonoGame-managed parameters - these are set automatically by SpriteBatch
            if (MonoGameManagedParameters.Contains(paramName))
                continue;

            // Skip if we already processed this parameter from the definition
            var alreadyProcessed = shaderDef?.Parameters?.Any(p => p.Name == paramName) ?? false;
            if (alreadyProcessed)
                continue;

            // Parameter should exist in the shader effect - if not, skip with warning
            // (shader compiler may optimize away unused variables)
            if (!allShaderParameters.TryGetValue(paramName, out var param))
            {
                _logger.Debug(
                    "Shader '{ShaderId}' component specifies parameter '{ParamName}', "
                        + "but shader effect doesn't have it (may have been optimized away). Skipping.",
                    shader.ShaderId,
                    paramName
                );
                continue;
            }

            // Get old value for event (before checking if changed)
            var oldValue = previousValues.TryGetValue(paramName, out var existingValue)
                ? existingValue
                : null;

            // Check if parameter value has changed (dirty tracking)
            if (oldValue != null && AreParameterValuesEqual(value, oldValue))
                // Parameter hasn't changed, skip setting it
                continue;

            // Validate parameter
            if (
                !_parameterValidator.ValidateParameter(
                    shader.ShaderId,
                    paramName,
                    value,
                    out var error
                )
            )
                throw new InvalidOperationException(
                    $"Invalid parameter '{paramName}' for shader '{shader.ShaderId}': {error}"
                );

            // Apply parameter
            ApplyShaderParameter(effect, paramName, value);

            // Update previous value for dirty tracking
            previousValues[paramName] = value;

            // Fire event for parameter change
            var evt = new ShaderParameterChangedEvent
            {
                Layer = shader.Layer,
                ShaderId = shader.ShaderId,
                ParameterName = paramName,
                OldValue = oldValue,
                NewValue = value,
                ShaderEntity = entity,
            };
            EventBus.Send(ref evt);
        }
    }

    /// <summary>
    ///     Compares two parameter values for equality.
    ///     Handles value types (Vector2, Vector3, Vector4, Color, float, etc.) and reference types.
    /// </summary>
    private static bool AreParameterValuesEqual(object value1, object value2)
    {
        if (value1 == null && value2 == null)
            return true;
        if (value1 == null || value2 == null)
            return false;

        // For value types, use Equals() which works for Vector2, Vector3, Vector4, Color, float, etc.
        if (value1.GetType().IsValueType)
            return value1.Equals(value2);

        // For reference types (Texture2D, Matrix), use reference equality
        return ReferenceEquals(value1, value2);
    }

    private void ApplyShaderParameter(Effect effect, string paramName, object value)
    {
        // Use shared utility to avoid code duplication (DRY)
        try
        {
            ShaderParameterApplier.ApplyParameter(effect, paramName, value, _logger);
        }
        catch (InvalidOperationException ex)
        {
            // Log and continue - one parameter failure shouldn't stop others
            _logger.Warning(ex, "Failed to apply shader parameter {ParamName}", paramName);
        }
    }

    private void FireShaderChangedEvent(
        ShaderLayer layer,
        string? previousShaderId,
        string? newShaderId,
        Entity shaderEntity
    )
    {
        var evt = new RenderingShaderChangedEvent
        {
            Layer = layer,
            PreviousShaderId = previousShaderId,
            NewShaderId = newShaderId,
            ShaderEntity = shaderEntity,
        };
        EventBus.Send(ref evt);
    }

    /// <summary>
    ///     Checks if two shaders are compatible with each other.
    /// </summary>
    /// <param name="shaderId1">First shader ID.</param>
    /// <param name="shaderId2">Second shader ID.</param>
    /// <returns>True if shaders are compatible, false otherwise.</returns>
    public bool AreCompatible(string shaderId1, string shaderId2)
    {
        if (_modManager == null)
            // No mod manager - assume compatible (can't check)
            return true;

        if (string.IsNullOrEmpty(shaderId1) || string.IsNullOrEmpty(shaderId2))
            return true; // Empty shaders are compatible

        if (shaderId1 == shaderId2)
            return true; // Same shader is compatible with itself

        // Get shader definitions
        var shader1 = _modManager.GetDefinition<ShaderDefinition>(shaderId1);
        var shader2 = _modManager.GetDefinition<ShaderDefinition>(shaderId2);

        if (shader1 == null || shader2 == null)
            // Can't check compatibility if definitions don't exist
            return true; // Assume compatible

        // Check if shader1 lists shader2 as compatible
        if (shader1.CompatibleWith != null && shader1.CompatibleWith.Contains(shaderId2))
            return true;

        // Check if shader2 lists shader1 as compatible
        if (shader2.CompatibleWith != null && shader2.CompatibleWith.Contains(shaderId1))
            return true;

        // Not explicitly listed as compatible
        return false;
    }

    /// <summary>
    ///     Validates an entire shader stack for compatibility.
    ///     Logs warnings for incompatible combinations.
    /// </summary>
    /// <param name="shaderIds">List of shader IDs in the stack.</param>
    /// <returns>True if all shaders are compatible, false otherwise.</returns>
    public bool ValidateShaderStack(IReadOnlyList<string> shaderIds)
    {
        if (shaderIds == null || shaderIds.Count <= 1)
            return true; // Empty or single shader stacks are always valid

        var allCompatible = true;

        // Check all pairs of shaders
        for (var i = 0; i < shaderIds.Count; i++)
        for (var j = i + 1; j < shaderIds.Count; j++)
            if (!AreCompatible(shaderIds[i], shaderIds[j]))
            {
                _logger.Warning(
                    "Incompatible shaders detected in stack: {ShaderId1} and {ShaderId2} are not compatible",
                    shaderIds[i],
                    shaderIds[j]
                );
                allCompatible = false;
            }

        return allCompatible;
    }

    /// <summary>
    ///     Validates shader stack for a specific layer.
    /// </summary>
    private void ValidateShaderStackForLayer(
        List<(Entity entity, RenderingShaderComponent shader)> shaders,
        ShaderLayer layer
    )
    {
        if (shaders.Count <= 1)
            return; // No need to validate single or empty stacks

        var shaderIds = shaders.Select(s => s.shader.ShaderId).ToList();
        ValidateShaderStack(shaderIds);
    }
}
