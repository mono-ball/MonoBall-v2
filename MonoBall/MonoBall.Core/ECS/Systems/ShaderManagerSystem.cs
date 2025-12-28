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

namespace MonoBall.Core.ECS.Systems
{
    /// <summary>
    /// System that manages shader effects and updates their parameters.
    /// Shader state is updated in Render phase to avoid timing mismatches.
    /// Note: This is not an Arch ECS BaseSystem (no Update() method) - it's a Render-phase helper
    /// that is called explicitly from SceneRendererSystem.
    /// </summary>
    public class ShaderManagerSystem
    {
        private readonly World _world;
        private readonly IShaderService _shaderService;
        private readonly IShaderParameterValidator _parameterValidator;
        private readonly IModManager? _modManager;
        private readonly QueryDescription _renderingShaderQuery;
        private readonly GraphicsDevice _graphicsDevice;
        private readonly ILogger _logger;

        // Cached active shader stacks per layer
        private readonly List<(
            Effect effect,
            ShaderBlendMode blendMode,
            Entity entity
        )> _activeTileLayerShaders = new();
        private readonly List<(
            Effect effect,
            ShaderBlendMode blendMode,
            Entity entity
        )> _activeSpriteLayerShaders = new();
        private readonly List<(
            Effect effect,
            ShaderBlendMode blendMode,
            Entity entity
        )> _activeCombinedLayerShaders = new();

        // Track previous shader IDs for change detection (for backward compatibility)
        private string? _previousTileShaderId;
        private string? _previousSpriteShaderId;
        private string? _previousCombinedShaderId;

        // Track shader entities for event firing (for backward compatibility)
        private Entity? _tileShaderEntity;
        private Entity? _spriteShaderEntity;
        private Entity? _combinedShaderEntity;

        // Dirty flag to avoid unnecessary updates
        private bool _shadersDirty = true;

        // Track previous parameter values per entity for dirty tracking
        private readonly Dictionary<Entity, Dictionary<string, object>> _previousParameterValues =
            new();

        // Reusable collections to avoid allocations
        private readonly List<(Entity entity, RenderingShaderComponent shader)> _tileShaders =
            new();
        private readonly List<(Entity entity, RenderingShaderComponent shader)> _spriteShaders =
            new();
        private readonly List<(Entity entity, RenderingShaderComponent shader)> _combinedShaders =
            new();

        /// <summary>
        /// Parameters that are automatically set by MonoGame/SpriteBatch and should not be required in shader definitions.
        /// These are set automatically when SpriteBatch.Begin() is called with an Effect.
        /// </summary>
        private static readonly HashSet<string> MonoGameManagedParameters = new HashSet<string>
        {
            "SpriteTexture", // Set automatically by SpriteBatch
            "Texture", // Alternative name for SpriteTexture
            "WorldViewProjection", // Transformation matrix set by SpriteBatch
            "MatrixTransform", // Alternative name for WorldViewProjection
        };

        /// <summary>
        /// Initializes a new instance of the ShaderManagerSystem.
        /// </summary>
        /// <param name="world">The ECS world.</param>
        /// <param name="shaderService">The shader service for loading shaders.</param>
        /// <param name="parameterValidator">The parameter validator for validating shader parameters.</param>
        /// <param name="graphicsDevice">The graphics device for getting viewport dimensions.</param>
        /// <param name="logger">The logger for logging operations.</param>
        /// <param name="modManager">Optional mod manager for compatibility checking.</param>
        public ShaderManagerSystem(
            World world,
            IShaderService shaderService,
            IShaderParameterValidator parameterValidator,
            GraphicsDevice graphicsDevice,
            ILogger logger,
            IModManager? modManager = null
        )
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _shaderService =
                shaderService ?? throw new ArgumentNullException(nameof(shaderService));
            _parameterValidator =
                parameterValidator ?? throw new ArgumentNullException(nameof(parameterValidator));
            _graphicsDevice =
                graphicsDevice ?? throw new ArgumentNullException(nameof(graphicsDevice));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _modManager = modManager;
            _renderingShaderQuery = new QueryDescription().WithAll<RenderingShaderComponent>();
        }

        /// <summary>
        /// Updates shader state. Called in Render phase, just before rendering systems need shaders.
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
        /// Gets the active shader stack for tile layer rendering.
        /// Returns all enabled shaders sorted by RenderOrder.
        /// </summary>
        /// <param name="sceneEntity">Optional scene entity to filter shaders. If null, returns global shaders only.</param>
        public IReadOnlyList<(
            Effect effect,
            ShaderBlendMode blendMode,
            Entity entity
        )> GetTileLayerShaderStack(Entity? sceneEntity = null)
        {
            if (sceneEntity == null)
            {
                return _activeTileLayerShaders;
            }

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
        /// Gets the active shader stack for sprite layer rendering.
        /// Returns all enabled shaders sorted by RenderOrder.
        /// </summary>
        /// <param name="sceneEntity">Optional scene entity to filter shaders. If null, returns global shaders only.</param>
        public IReadOnlyList<(
            Effect effect,
            ShaderBlendMode blendMode,
            Entity entity
        )> GetSpriteLayerShaderStack(Entity? sceneEntity = null)
        {
            if (sceneEntity == null)
            {
                return _activeSpriteLayerShaders;
            }

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
        /// Gets the active shader stack for combined layer rendering (post-processing).
        /// Returns all enabled shaders sorted by RenderOrder.
        /// </summary>
        /// <param name="sceneEntity">Optional scene entity to filter shaders. If null, returns global shaders only.</param>
        public IReadOnlyList<(
            Effect effect,
            ShaderBlendMode blendMode,
            Entity entity
        )> GetCombinedLayerShaderStack(Entity? sceneEntity = null)
        {
            if (sceneEntity == null)
            {
                return _activeCombinedLayerShaders;
            }

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
        /// Gets the active shader for tile layer rendering (backward compatibility).
        /// Returns the first shader from the stack, or null if no shaders.
        /// </summary>
        public Effect? GetTileLayerShader() =>
            _activeTileLayerShaders.Count > 0 ? _activeTileLayerShaders[0].effect : null;

        /// <summary>
        /// Gets the active shader for sprite layer rendering (backward compatibility).
        /// Returns the first shader from the stack, or null if no shaders.
        /// </summary>
        public Effect? GetSpriteLayerShader() =>
            _activeSpriteLayerShaders.Count > 0 ? _activeSpriteLayerShaders[0].effect : null;

        /// <summary>
        /// Gets the active shader for combined layer rendering (backward compatibility).
        /// Returns the first shader from the stack, or null if no shaders.
        /// </summary>
        public Effect? GetCombinedLayerShader() =>
            _activeCombinedLayerShaders.Count > 0 ? _activeCombinedLayerShaders[0].effect : null;

        /// <summary>
        /// Forces update of all parameters for all active combined layer shaders.
        /// Called right before SpriteBatch.Begin() in Immediate mode to ensure parameters are set.
        /// </summary>
        public void ForceUpdateCombinedLayerParameters()
        {
            foreach (var (effect, _, entity) in _activeCombinedLayerShaders)
            {
                UpdateShaderParametersForEntity(entity, effect);
            }
        }

        /// <summary>
        /// Updates dynamic parameters for all active combined layer shaders.
        /// Called before applying post-processing to ensure correct parameter values.
        /// </summary>
        /// <param name="parameters">Dictionary of parameter names to values.</param>
        public void UpdateCombinedLayerDynamicParameters(Dictionary<string, object> parameters)
        {
            if (
                _activeCombinedLayerShaders.Count == 0
                || parameters == null
                || parameters.Count == 0
            )
                return;

            foreach (var (effect, _, entity) in _activeCombinedLayerShaders)
            {
                foreach (var (paramName, value) in parameters)
                {
                    // Skip MonoGame-managed parameters - these are set automatically
                    if (MonoGameManagedParameters.Contains(paramName))
                        continue;

                    // ScreenSize is handled specially - check if it exists first
                    // Not all shaders have ScreenSize, so we check before setting
                    if (paramName == "ScreenSize")
                    {
                        if (value is not Vector2 screenSize)
                        {
                            _logger.Warning(
                                "ScreenSize parameter value is not Vector2 (type: {Type}) for combined layer shader, skipping.",
                                value?.GetType().Name ?? "null"
                            );
                            continue;
                        }

                        // Get or create previous values dictionary for dirty tracking
                        if (!_previousParameterValues.TryGetValue(entity, out var previousValues))
                        {
                            previousValues = new Dictionary<string, object>();
                            _previousParameterValues[entity] = previousValues;
                        }

                        // Get shader component for event firing
                        if (_world.Has<RenderingShaderComponent>(entity))
                        {
                            ref var shader = ref _world.Get<RenderingShaderComponent>(entity);
                            TrySetScreenSizeParameter(effect, previousValues, shader, entity);
                        }
                        continue;
                    }

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
        }

        /// <summary>
        /// Updates the ScreenSize parameter for all active shaders (tile, sprite, and combined layers).
        /// Called before applying parameters to ensure correct screen dimensions.
        /// </summary>
        /// <param name="width">The screen/viewport width.</param>
        /// <param name="height">The screen/viewport height.</param>
        public void UpdateAllLayersScreenSize(int width, int height)
        {
            var screenSize = new Vector2(width, height);

            UpdateScreenSizeForShaders(_activeTileLayerShaders, screenSize);
            UpdateScreenSizeForShaders(_activeSpriteLayerShaders, screenSize);
            UpdateScreenSizeForShaders(_activeCombinedLayerShaders, screenSize);
        }

        /// <summary>
        /// Updates the ScreenSize parameter for a list of shaders.
        /// Uses the helper method to avoid code duplication and ensure consistent behavior.
        /// </summary>
        /// <param name="shaders">The list of shaders to update.</param>
        /// <param name="screenSize">The screen size vector (unused - viewport is used instead).</param>
        private void UpdateScreenSizeForShaders(
            List<(Effect effect, ShaderBlendMode blendMode, Entity entity)> shaders,
            Vector2 screenSize
        )
        {
            foreach (var (effect, _, entity) in shaders)
            {
                // Skip destroyed entities - CRITICAL: must check before ANY entity access
                if (!_world.IsAlive(entity))
                    continue;

                // Get or create previous values dictionary for dirty tracking
                if (!_previousParameterValues.TryGetValue(entity, out var previousValues))
                {
                    previousValues = new Dictionary<string, object>();
                    _previousParameterValues[entity] = previousValues;
                }

                // Get shader component for event firing
                if (_world.Has<RenderingShaderComponent>(entity))
                {
                    ref var shader = ref _world.Get<RenderingShaderComponent>(entity);
                    TrySetScreenSizeParameter(effect, previousValues, shader, entity);
                }
            }
        }

        /// <summary>
        /// Updates the ScreenSize parameter for the active combined layer shader.
        /// Called before applying post-processing to ensure correct screen dimensions.
        /// </summary>
        /// <param name="width">The screen/viewport width.</param>
        /// <param name="height">The screen/viewport height.</param>
        public void UpdateCombinedLayerScreenSize(int width, int height)
        {
            if (_activeCombinedLayerShaders.Count == 0)
                return;

            // Temporarily override viewport for ScreenSize calculation
            var originalViewport = _graphicsDevice.Viewport;
            try
            {
                _graphicsDevice.Viewport = new Viewport(0, 0, width, height);

                foreach (var (effect, _, entity) in _activeCombinedLayerShaders)
                {
                    // Get or create previous values dictionary for dirty tracking
                    if (!_previousParameterValues.TryGetValue(entity, out var previousValues))
                    {
                        previousValues = new Dictionary<string, object>();
                        _previousParameterValues[entity] = previousValues;
                    }

                    // Get shader component for event firing
                    if (_world.Has<RenderingShaderComponent>(entity))
                    {
                        ref var shader = ref _world.Get<RenderingShaderComponent>(entity);
                        TrySetScreenSizeParameter(effect, previousValues, shader, entity);
                    }
                }
            }
            finally
            {
                // Restore original viewport
                _graphicsDevice.Viewport = originalViewport;
            }
        }

        /// <summary>
        /// Marks shaders as dirty, forcing an update on next UpdateShaderState() call.
        /// Called when components are added/removed/modified.
        /// </summary>
        public void MarkShadersDirty()
        {
            _shadersDirty = true;
        }

        /// <summary>
        /// Updates the active shader lists based on current RenderingShaderComponent entities.
        /// </summary>
        /// <param name="sceneEntity">Optional scene entity to filter shaders. If null, includes all shaders (global and per-scene).</param>
        private void UpdateActiveShaders(Entity? sceneEntity = null)
        {
            // Clear reusable collections
            _tileShaders.Clear();
            _spriteShaders.Clear();
            _combinedShaders.Clear();

            int totalFound = 0;
            _world.Query(
                in _renderingShaderQuery,
                (Entity entity, ref RenderingShaderComponent shader) =>
                {
                    totalFound++;

                    // Filter by scene entity if provided
                    // Include shaders with null SceneEntity (global) and shaders matching the scene
                    if (sceneEntity != null)
                    {
                        if (shader.SceneEntity != null && shader.SceneEntity != sceneEntity)
                        {
                            // Shader is scoped to a different scene, skip it
                            return;
                        }
                    }

                    if (!shader.IsEnabled)
                    {
                        _logger.Debug(
                            "ShaderManagerSystem: Found disabled shader entity {EntityId}, ShaderId: {ShaderId}, Layer: {Layer}, SceneEntity: {SceneEntity}",
                            entity.Id,
                            shader.ShaderId,
                            shader.Layer,
                            shader.SceneEntity?.Id.ToString() ?? "null (global)"
                        );
                        return;
                    }

                    _logger.Debug(
                        "ShaderManagerSystem: Found enabled shader entity {EntityId}, ShaderId: {ShaderId}, Layer: {Layer}, SceneEntity: {SceneEntity}",
                        entity.Id,
                        shader.ShaderId,
                        shader.Layer,
                        shader.SceneEntity?.Id.ToString() ?? "null (global)"
                    );

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

            _logger.Debug(
                "ShaderManagerSystem: UpdateActiveShaders found {Total} shader components. Tile: {TileCount}, Sprite: {SpriteCount}, Combined: {CombinedCount}",
                totalFound,
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
                    int orderComparison = a.shader.RenderOrder.CompareTo(b.shader.RenderOrder);
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

                _logger.Debug(
                    "ShaderManagerSystem: Successfully loaded shader {ShaderId}, Techniques: {TechniqueCount}",
                    shaderComp.ShaderId,
                    effect.Techniques.Count
                );

                // Set CurrentTechnique (use first technique if not explicitly set)
                ShaderParameterApplier.EnsureCurrentTechnique(effect, _logger);

                // Add to stack
                activeShaderStack.Add((effect, shaderComp.BlendMode, entity));

                // Clear previous parameter values when shader is added
                if (_previousParameterValues.ContainsKey(entity))
                {
                    _previousParameterValues[entity].Clear();
                }
            }

            // Check if first shader changed (for backward compatibility and events)
            if (activeShaderStack.Count > 0)
            {
                var firstShader = activeShaderStack[0];
                ref var firstShaderComp = ref _world.Get<RenderingShaderComponent>(
                    firstShader.entity
                );

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

            _logger.Debug(
                "ShaderManagerSystem: Updated shader stack for layer {Layer}, Count: {Count}",
                layer,
                activeShaderStack.Count
            );
        }

        private void UpdateShaderParameters()
        {
            // Update shader parameters for all active shaders in stacks
            // Validate parameters before applying
            foreach (var (effect, _, entity) in _activeTileLayerShaders)
            {
                UpdateShaderParametersForEntity(entity, effect);
            }

            foreach (var (effect, _, entity) in _activeSpriteLayerShaders)
            {
                UpdateShaderParametersForEntity(entity, effect);
            }

            foreach (var (effect, _, entity) in _activeCombinedLayerShaders)
            {
                UpdateShaderParametersForEntity(entity, effect);
            }
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
            {
                shaderDef = _modManager.GetDefinition<ShaderDefinition>(shader.ShaderId);
            }

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
            foreach (EffectParameter param in effect.Parameters)
            {
                if (param != null)
                {
                    allShaderParameters[param.Name] = param;
                }
            }

            // First, automatically set ScreenSize if the shader has it (before definition processing)
            // ScreenSize is a special case - it's always set from viewport, never from definition/default
            if (TrySetScreenSizeParameter(effect, previousValues, shader, entity))
            {
                // ScreenSize was set (and event fired if value changed)
            }

            // Second, process all parameters from the shader definition (if available)
            // This ensures all defined parameters are set, using defaults if not specified
            if (shaderDef?.Parameters != null)
            {
                foreach (var paramDef in shaderDef.Parameters)
                {
                    // Skip ScreenSize - it's handled separately above
                    if (paramDef.Name == "ScreenSize")
                        continue;

                    // Skip MonoGame-managed parameters - these are set automatically by SpriteBatch
                    if (MonoGameManagedParameters.Contains(paramDef.Name))
                        continue;

                    // Parameter MUST exist in the shader effect if it's in the definition
                    if (!allShaderParameters.TryGetValue(paramDef.Name, out var effectParam))
                    {
                        throw new InvalidOperationException(
                            $"Shader '{shader.ShaderId}' definition lists parameter '{paramDef.Name}', "
                                + $"but shader effect doesn't have it. Shader definitions must match shader code."
                        );
                    }

                    // Determine the value to use: component value or default value
                    object? valueToUse = null;
                    bool isFromComponent = false;

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
                    object? oldValue = previousValues.TryGetValue(
                        paramDef.Name,
                        out var existingValue
                    )
                        ? existingValue
                        : null;

                    // Check if parameter value has changed (dirty tracking)
                    if (oldValue != null && AreParameterValuesEqual(valueToUse, oldValue))
                    {
                        // Parameter hasn't changed, skip setting it
                        continue;
                    }

                    // Validate parameter
                    if (
                        !_parameterValidator.ValidateParameter(
                            shader.ShaderId,
                            paramDef.Name,
                            valueToUse,
                            out var error
                        )
                    )
                    {
                        throw new InvalidOperationException(
                            $"Invalid parameter '{paramDef.Name}' for shader '{shader.ShaderId}': {error}"
                        );
                    }

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
            }

            // Second, ensure ALL parameters in the shader effect are set (no optional parameters)
            // Check for any parameters that exist in the shader but weren't set above
            // Note: If _modManager is null, we can't validate against definitions, so we skip this check
            if (_modManager != null)
            {
                foreach (var (paramName, effectParam) in allShaderParameters)
                {
                    // Skip ScreenSize - already handled
                    if (paramName == "ScreenSize")
                        continue;

                    // Skip MonoGame-managed parameters - these are set automatically by SpriteBatch
                    if (MonoGameManagedParameters.Contains(paramName))
                        continue;

                    // Skip if already processed from definition
                    bool alreadyProcessed =
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
            }
            else
            {
                // Without mod manager, we can't validate parameters against definitions
                // Log warning but continue - parameters from component will still be processed
                _logger.Debug(
                    "ModManager is null for shader {ShaderId}, skipping parameter validation against definitions. "
                        + "Only component parameters will be set.",
                    shader.ShaderId
                );
            }

            // Third, process any additional parameters from component that aren't in the definition
            // This allows runtime parameters that aren't in the definition (for flexibility)
            foreach (var (paramName, value) in componentParameters)
            {
                // Skip ScreenSize - already handled (set automatically from viewport)
                if (paramName == "ScreenSize")
                {
                    _logger.Debug(
                        "Component specifies ScreenSize parameter for shader {ShaderId}, "
                            + "but ScreenSize is set automatically from viewport. Ignoring component value.",
                        shader.ShaderId
                    );
                    continue;
                }

                // Skip MonoGame-managed parameters - these are set automatically by SpriteBatch
                if (MonoGameManagedParameters.Contains(paramName))
                {
                    _logger.Debug(
                        "Component specifies MonoGame-managed parameter {ParamName} for shader {ShaderId}, "
                            + "but it's set automatically by SpriteBatch. Ignoring component value.",
                        paramName,
                        shader.ShaderId
                    );
                    continue;
                }

                // Skip if we already processed this parameter from the definition
                bool alreadyProcessed =
                    shaderDef?.Parameters?.Any(p => p.Name == paramName) ?? false;
                if (alreadyProcessed)
                    continue;

                // Parameter MUST exist in the shader effect
                if (!allShaderParameters.TryGetValue(paramName, out var param))
                {
                    throw new InvalidOperationException(
                        $"Shader '{shader.ShaderId}' component specifies parameter '{paramName}', "
                            + $"but shader effect doesn't have it. Cannot set parameters that don't exist in the shader."
                    );
                }

                // Get old value for event (before checking if changed)
                object? oldValue = previousValues.TryGetValue(paramName, out var existingValue)
                    ? existingValue
                    : null;

                // Check if parameter value has changed (dirty tracking)
                if (oldValue != null && AreParameterValuesEqual(value, oldValue))
                {
                    // Parameter hasn't changed, skip setting it
                    continue;
                }

                // Validate parameter
                if (
                    !_parameterValidator.ValidateParameter(
                        shader.ShaderId,
                        paramName,
                        value,
                        out var error
                    )
                )
                {
                    throw new InvalidOperationException(
                        $"Invalid parameter '{paramName}' for shader '{shader.ShaderId}': {error}"
                    );
                }

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
        /// Compares two parameter values for equality.
        /// Handles value types (Vector2, Vector3, Vector4, Color, float, etc.) and reference types.
        /// </summary>
        private static bool AreParameterValuesEqual(object value1, object value2)
        {
            if (value1 == null && value2 == null)
                return true;
            if (value1 == null || value2 == null)
                return false;

            // For value types, use Equals() which works for Vector2, Vector3, Vector4, Color, float, etc.
            if (value1.GetType().IsValueType)
            {
                return value1.Equals(value2);
            }

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

        /// <summary>
        /// Attempts to set the ScreenSize parameter on an effect from the current viewport.
        /// Fires an event if the value changed.
        /// </summary>
        /// <param name="effect">The shader effect.</param>
        /// <param name="previousValues">Dictionary tracking previous parameter values for dirty tracking.</param>
        /// <param name="shader">The layer shader component.</param>
        /// <param name="entity">The entity owning the shader component.</param>
        /// <returns>True if ScreenSize parameter was set, false if it doesn't exist in the shader.</returns>
        private bool TrySetScreenSizeParameter(
            Effect effect,
            Dictionary<string, object> previousValues,
            RenderingShaderComponent shader,
            Entity entity
        )
        {
            try
            {
                var screenSizeParam = effect.Parameters["ScreenSize"];
                if (
                    screenSizeParam != null
                    && screenSizeParam.ParameterClass == EffectParameterClass.Vector
                    && screenSizeParam.ColumnCount == 2
                )
                {
                    var viewport = _graphicsDevice.Viewport;
                    var screenSize = new Vector2(viewport.Width, viewport.Height);

                    // Check if value changed (dirty tracking)
                    var oldScreenSize = previousValues.TryGetValue("ScreenSize", out var oldValue)
                        ? oldValue
                        : null;

                    if (
                        oldScreenSize == null
                        || !AreParameterValuesEqual(screenSize, oldScreenSize)
                    )
                    {
                        // Value changed - set it and fire event
                        screenSizeParam.SetValue(screenSize);
                        previousValues["ScreenSize"] = screenSize;

                        // Fire event for ScreenSize update
                        var evt = new ShaderParameterChangedEvent
                        {
                            Layer = shader.Layer,
                            ShaderId = shader.ShaderId,
                            ParameterName = "ScreenSize",
                            OldValue = oldScreenSize,
                            NewValue = screenSize,
                            ShaderEntity = entity,
                        };
                        EventBus.Send(ref evt);
                    }

                    return true;
                }
            }
            catch (KeyNotFoundException)
            {
                // ScreenSize doesn't exist - that's fine, not all shaders need it
            }

            return false;
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
        /// Checks if two shaders are compatible with each other.
        /// </summary>
        /// <param name="shaderId1">First shader ID.</param>
        /// <param name="shaderId2">Second shader ID.</param>
        /// <returns>True if shaders are compatible, false otherwise.</returns>
        public bool AreCompatible(string shaderId1, string shaderId2)
        {
            if (_modManager == null)
            {
                // No mod manager - assume compatible (can't check)
                return true;
            }

            if (string.IsNullOrEmpty(shaderId1) || string.IsNullOrEmpty(shaderId2))
            {
                return true; // Empty shaders are compatible
            }

            if (shaderId1 == shaderId2)
            {
                return true; // Same shader is compatible with itself
            }

            // Get shader definitions
            var shader1 = _modManager.GetDefinition<ShaderDefinition>(shaderId1);
            var shader2 = _modManager.GetDefinition<ShaderDefinition>(shaderId2);

            if (shader1 == null || shader2 == null)
            {
                // Can't check compatibility if definitions don't exist
                return true; // Assume compatible
            }

            // Check if shader1 lists shader2 as compatible
            if (shader1.CompatibleWith != null && shader1.CompatibleWith.Contains(shaderId2))
            {
                return true;
            }

            // Check if shader2 lists shader1 as compatible
            if (shader2.CompatibleWith != null && shader2.CompatibleWith.Contains(shaderId1))
            {
                return true;
            }

            // Not explicitly listed as compatible
            return false;
        }

        /// <summary>
        /// Validates an entire shader stack for compatibility.
        /// Logs warnings for incompatible combinations.
        /// </summary>
        /// <param name="shaderIds">List of shader IDs in the stack.</param>
        /// <returns>True if all shaders are compatible, false otherwise.</returns>
        public bool ValidateShaderStack(IReadOnlyList<string> shaderIds)
        {
            if (shaderIds == null || shaderIds.Count <= 1)
            {
                return true; // Empty or single shader stacks are always valid
            }

            bool allCompatible = true;

            // Check all pairs of shaders
            for (int i = 0; i < shaderIds.Count; i++)
            {
                for (int j = i + 1; j < shaderIds.Count; j++)
                {
                    if (!AreCompatible(shaderIds[i], shaderIds[j]))
                    {
                        _logger.Warning(
                            "Incompatible shaders detected in stack: {ShaderId1} and {ShaderId2} are not compatible",
                            shaderIds[i],
                            shaderIds[j]
                        );
                        allCompatible = false;
                    }
                }
            }

            return allCompatible;
        }

        /// <summary>
        /// Validates shader stack for a specific layer.
        /// </summary>
        private void ValidateShaderStackForLayer(
            List<(Entity entity, RenderingShaderComponent shader)> shaders,
            ShaderLayer layer
        )
        {
            if (shaders.Count <= 1)
            {
                return; // No need to validate single or empty stacks
            }

            var shaderIds = shaders.Select(s => s.shader.ShaderId).ToList();
            ValidateShaderStack(shaderIds);
        }
    }
}
