using System;
using System.Collections.Generic;
using System.Linq;
using Arch.Core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoBall.Core.ECS.Components;
using MonoBall.Core.ECS.Events;
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
        private readonly QueryDescription _layerShaderQuery;
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
        private readonly List<(Entity entity, LayerShaderComponent shader)> _tileShaders = new();
        private readonly List<(Entity entity, LayerShaderComponent shader)> _spriteShaders = new();
        private readonly List<(Entity entity, LayerShaderComponent shader)> _combinedShaders =
            new();

        /// <summary>
        /// Initializes a new instance of the ShaderManagerSystem.
        /// </summary>
        /// <param name="world">The ECS world.</param>
        /// <param name="shaderService">The shader service for loading shaders.</param>
        /// <param name="parameterValidator">The parameter validator for validating shader parameters.</param>
        /// <param name="graphicsDevice">The graphics device for getting viewport dimensions.</param>
        /// <param name="logger">The logger for logging operations.</param>
        public ShaderManagerSystem(
            World world,
            IShaderService shaderService,
            IShaderParameterValidator parameterValidator,
            GraphicsDevice graphicsDevice,
            ILogger logger
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
            _layerShaderQuery = new QueryDescription().WithAll<LayerShaderComponent>();
        }

        /// <summary>
        /// Updates shader state. Called in Render phase, just before rendering systems need shaders.
        /// </summary>
        public void UpdateShaderState()
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
                UpdateActiveShaders();
                _shadersDirty = false;
            }

            UpdateShaderParameters();
        }

        /// <summary>
        /// Gets the active shader stack for tile layer rendering.
        /// Returns all enabled shaders sorted by RenderOrder.
        /// </summary>
        public IReadOnlyList<(
            Effect effect,
            ShaderBlendMode blendMode,
            Entity entity
        )> GetTileLayerShaderStack()
        {
            return _activeTileLayerShaders;
        }

        /// <summary>
        /// Gets the active shader stack for sprite layer rendering.
        /// Returns all enabled shaders sorted by RenderOrder.
        /// </summary>
        public IReadOnlyList<(
            Effect effect,
            ShaderBlendMode blendMode,
            Entity entity
        )> GetSpriteLayerShaderStack()
        {
            return _activeSpriteLayerShaders;
        }

        /// <summary>
        /// Gets the active shader stack for combined layer rendering (post-processing).
        /// Returns all enabled shaders sorted by RenderOrder.
        /// </summary>
        public IReadOnlyList<(
            Effect effect,
            ShaderBlendMode blendMode,
            Entity entity
        )> GetCombinedLayerShaderStack()
        {
            return _activeCombinedLayerShaders;
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

            foreach (var (effect, _, _) in _activeCombinedLayerShaders)
            {
                foreach (var (paramName, value) in parameters)
                {
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

            // Update ScreenSize for tile layer shaders
            foreach (var (effect, _, _) in _activeTileLayerShaders)
            {
                try
                {
                    ShaderParameterApplier.ApplyParameter(
                        effect,
                        "ScreenSize",
                        screenSize,
                        _logger
                    );
                }
                catch (InvalidOperationException ex)
                {
                    // Log and continue - ScreenSize is optional for shaders
                    _logger.Debug(
                        ex,
                        "ScreenSize parameter not available or invalid for tile layer shader"
                    );
                }
            }

            // Update ScreenSize for sprite layer shaders
            foreach (var (effect, _, _) in _activeSpriteLayerShaders)
            {
                try
                {
                    ShaderParameterApplier.ApplyParameter(
                        effect,
                        "ScreenSize",
                        screenSize,
                        _logger
                    );
                }
                catch (InvalidOperationException ex)
                {
                    // Log and continue - ScreenSize is optional for shaders
                    _logger.Debug(
                        ex,
                        "ScreenSize parameter not available or invalid for sprite layer shader"
                    );
                }
            }

            // Update ScreenSize for combined layer shaders
            foreach (var (effect, _, _) in _activeCombinedLayerShaders)
            {
                try
                {
                    ShaderParameterApplier.ApplyParameter(
                        effect,
                        "ScreenSize",
                        screenSize,
                        _logger
                    );
                }
                catch (InvalidOperationException ex)
                {
                    // Log and continue - ScreenSize is optional for shaders
                    _logger.Debug(
                        ex,
                        "ScreenSize parameter not available or invalid for combined layer shader"
                    );
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
            var parameters = new Dictionary<string, object>
            {
                { "ScreenSize", new Vector2(width, height) },
            };
            UpdateCombinedLayerDynamicParameters(parameters);
        }

        /// <summary>
        /// Marks shaders as dirty, forcing an update on next UpdateShaderState() call.
        /// Called when components are added/removed/modified.
        /// </summary>
        public void MarkShadersDirty()
        {
            _shadersDirty = true;
        }

        private void UpdateActiveShaders()
        {
            // Clear reusable collections
            _tileShaders.Clear();
            _spriteShaders.Clear();
            _combinedShaders.Clear();

            int totalFound = 0;
            _world.Query(
                in _layerShaderQuery,
                (Entity entity, ref LayerShaderComponent shader) =>
                {
                    totalFound++;
                    if (!shader.IsEnabled)
                    {
                        _logger.Debug(
                            "ShaderManagerSystem: Found disabled shader entity {EntityId}, ShaderId: {ShaderId}, Layer: {Layer}",
                            entity.Id,
                            shader.ShaderId,
                            shader.Layer
                        );
                        return;
                    }

                    _logger.Debug(
                        "ShaderManagerSystem: Found enabled shader entity {EntityId}, ShaderId: {ShaderId}, Layer: {Layer}",
                        entity.Id,
                        shader.ShaderId,
                        shader.Layer
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
            List<(Entity entity, LayerShaderComponent shader)> shaders,
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
                _logger.Debug(
                    "ShaderManagerSystem: Loading shader {ShaderId} for layer {Layer}",
                    shaderComp.ShaderId,
                    layer
                );

                Effect? effect = _shaderService.GetShader(shaderComp.ShaderId);
                if (effect == null)
                {
                    _logger.Warning(
                        "Shader {ShaderId} failed to load for layer {Layer}",
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
                ref var firstShaderComp = ref _world.Get<LayerShaderComponent>(firstShader.entity);

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
            if (!_world.Has<LayerShaderComponent>(entity))
                return;

            // Ensure CurrentTechnique is set before setting parameters
            ShaderParameterApplier.EnsureCurrentTechnique(effect, _logger);

            // Automatically set ScreenSize parameter if the shader has it
            // This prevents warnings about null ScreenSize parameters
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
                    screenSizeParam.SetValue(screenSize);
                }
            }
            catch (KeyNotFoundException)
            {
                // ScreenSize parameter doesn't exist - that's fine, not all shaders need it
            }
            catch (Exception ex)
            {
                // Log unexpected errors but don't fail - ScreenSize is optional
                _logger.Debug(ex, "Failed to set ScreenSize parameter automatically");
            }

            ref var shader = ref _world.Get<LayerShaderComponent>(entity);
            if (shader.Parameters == null)
                return;

            // Get or create previous parameter values dictionary for this entity
            if (!_previousParameterValues.TryGetValue(entity, out var previousValues))
            {
                previousValues = new Dictionary<string, object>();
                _previousParameterValues[entity] = previousValues;
            }

            foreach (var (paramName, value) in shader.Parameters)
            {
                // Check if parameter value has changed (dirty tracking)
                if (previousValues.TryGetValue(paramName, out var previousValue))
                {
                    if (AreParameterValuesEqual(value, previousValue))
                    {
                        // Parameter hasn't changed, skip setting it
                        continue;
                    }
                }

                EffectParameter? param = null;
                try
                {
                    param = effect.Parameters[paramName];
                }
                catch (System.Collections.Generic.KeyNotFoundException)
                {
                    // Parameter doesn't exist - log and continue (parameter is optional)
                    _logger.Warning(
                        "Shader {ShaderId} does not have parameter {ParamName}",
                        shader.ShaderId,
                        paramName
                    );
                    continue;
                }
                catch (Exception ex)
                {
                    // Unexpected error - fail fast per .cursorrules
                    throw new InvalidOperationException(
                        $"Unexpected error accessing parameter '{paramName}' in shader '{shader.ShaderId}': {ex.Message}",
                        ex
                    );
                }

                if (param == null)
                {
                    _logger.Warning(
                        "Shader {ShaderId} does not have parameter {ParamName}",
                        shader.ShaderId,
                        paramName
                    );
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
                    _logger.Warning(
                        "Invalid parameter {ParamName} for shader {ShaderId}: {Error}",
                        paramName,
                        shader.ShaderId,
                        error
                    );
                    continue;
                }

                // Apply parameter
                ApplyShaderParameter(effect, paramName, value);

                // Update previous value for dirty tracking
                previousValues[paramName] = value;
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

        private void FireShaderChangedEvent(
            ShaderLayer layer,
            string? previousShaderId,
            string? newShaderId,
            Entity shaderEntity
        )
        {
            var evt = new LayerShaderChangedEvent
            {
                Layer = layer,
                PreviousShaderId = previousShaderId,
                NewShaderId = newShaderId,
                ShaderEntity = shaderEntity,
            };
            EventBus.Send(ref evt);
        }
    }
}
