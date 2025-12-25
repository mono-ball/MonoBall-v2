using System;
using System.Collections.Generic;
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
        private readonly ILogger _logger;

        // Cached active shaders per layer
        private Effect? _activeTileLayerShader;
        private Effect? _activeSpriteLayerShader;
        private Effect? _activeCombinedLayerShader;

        // Track previous shader IDs for change detection
        private string? _previousTileShaderId;
        private string? _previousSpriteShaderId;
        private string? _previousCombinedShaderId;

        // Track shader entities for event firing
        private Entity? _tileShaderEntity;
        private Entity? _spriteShaderEntity;
        private Entity? _combinedShaderEntity;

        // Dirty flag to avoid unnecessary updates
        private bool _shadersDirty = true;

        // Track previous parameter values per entity for dirty tracking
        private readonly Dictionary<Entity, Dictionary<string, object>> _previousParameterValues = new();

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
        /// <param name="logger">The logger for logging operations.</param>
        public ShaderManagerSystem(
            World world,
            IShaderService shaderService,
            IShaderParameterValidator parameterValidator,
            ILogger logger
        )
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _shaderService =
                shaderService ?? throw new ArgumentNullException(nameof(shaderService));
            _parameterValidator =
                parameterValidator ?? throw new ArgumentNullException(nameof(parameterValidator));
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
                    _activeTileLayerShader == null
                    && _activeSpriteLayerShader == null
                    && _activeCombinedLayerShader == null
                )
            )
            {
                UpdateActiveShaders();
                _shadersDirty = false;
            }

            UpdateShaderParameters();
        }

        /// <summary>
        /// Gets the active shader for tile layer rendering.
        /// </summary>
        public Effect? GetTileLayerShader() => _activeTileLayerShader;

        /// <summary>
        /// Gets the active shader for sprite layer rendering.
        /// </summary>
        public Effect? GetSpriteLayerShader() => _activeSpriteLayerShader;

        /// <summary>
        /// Gets the active shader for combined layer rendering (post-processing).
        /// </summary>
        public Effect? GetCombinedLayerShader() => _activeCombinedLayerShader;

        /// <summary>
        /// Forces update of all parameters for the active combined layer shader.
        /// Called right before SpriteBatch.Begin() in Immediate mode to ensure parameters are set.
        /// </summary>
        public void ForceUpdateCombinedLayerParameters()
        {
            if (_activeCombinedLayerShader != null && _combinedShaderEntity.HasValue)
            {
                UpdateShaderParametersForEntity(
                    _combinedShaderEntity.Value,
                    _activeCombinedLayerShader
                );
            }
        }

        /// <summary>
        /// Updates dynamic parameters for the active combined layer shader.
        /// Called before applying post-processing to ensure correct parameter values.
        /// </summary>
        /// <param name="parameters">Dictionary of parameter names to values.</param>
        public void UpdateCombinedLayerDynamicParameters(Dictionary<string, object> parameters)
        {
            if (_activeCombinedLayerShader == null || parameters == null || parameters.Count == 0)
                return;

            foreach (var (paramName, value) in parameters)
            {
                try
                {
                    ShaderParameterApplier.ApplyParameter(_activeCombinedLayerShader, paramName, value, _logger);
                }
                catch (InvalidOperationException ex)
                {
                    // Log and continue - one parameter failure shouldn't stop others
                    _logger.Warning(ex, "Failed to set dynamic parameter {ParamName} on combined layer shader", paramName);
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
            var parameters = new Dictionary<string, object> { { "ScreenSize", new Vector2(width, height) } };
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

            // Select shader with lowest RenderOrder for each layer
            UpdateLayerShader(
                _tileShaders,
                ShaderLayer.TileLayer,
                ref _activeTileLayerShader,
                ref _previousTileShaderId,
                ref _tileShaderEntity
            );
            UpdateLayerShader(
                _spriteShaders,
                ShaderLayer.SpriteLayer,
                ref _activeSpriteLayerShader,
                ref _previousSpriteShaderId,
                ref _spriteShaderEntity
            );
            UpdateLayerShader(
                _combinedShaders,
                ShaderLayer.CombinedLayer,
                ref _activeCombinedLayerShader,
                ref _previousCombinedShaderId,
                ref _combinedShaderEntity
            );
        }

        private void UpdateLayerShader(
            List<(Entity entity, LayerShaderComponent shader)> shaders,
            ShaderLayer layer,
            ref Effect? activeShader,
            ref string? previousShaderId,
            ref Entity? shaderEntity
        )
        {
            if (shaders.Count == 0)
            {
                if (activeShader != null)
                {
                    // Shader was disabled
                    FireShaderChangedEvent(layer, previousShaderId, null, shaderEntity ?? default);
                    activeShader = null;
                    previousShaderId = null;
                    shaderEntity = null;
                }

                return;
            }

            // Sort by RenderOrder (lowest first)
            shaders.Sort((a, b) => a.shader.RenderOrder.CompareTo(b.shader.RenderOrder));
            var selected = shaders[0];

            _logger.Debug(
                "ShaderManagerSystem: Loading shader {ShaderId} for layer {Layer}",
                selected.shader.ShaderId,
                layer
            );

            Effect newShader;
            try
            {
                newShader = _shaderService.GetShader(selected.shader.ShaderId);
                _logger.Debug(
                    "ShaderManagerSystem: Successfully loaded shader {ShaderId}, Techniques: {TechniqueCount}",
                    selected.shader.ShaderId,
                    newShader.Techniques.Count
                );
            }
            catch (InvalidOperationException ex)
            {
                _logger.Error(
                    ex,
                    "Shader {ShaderId} failed to load for layer {Layer}",
                    selected.shader.ShaderId,
                    layer
                );
                return;
            }

            // Set CurrentTechnique (use first technique if not explicitly set)
            ShaderParameterApplier.EnsureCurrentTechnique(newShader, _logger);

            // Check if shader changed
            if (selected.shader.ShaderId != previousShaderId)
            {
                FireShaderChangedEvent(
                    layer,
                    previousShaderId,
                    selected.shader.ShaderId,
                    selected.entity
                );
                previousShaderId = selected.shader.ShaderId;
                shaderEntity = selected.entity;

                // Clear previous parameter values when shader changes
                if (_previousParameterValues.ContainsKey(selected.entity))
                {
                    _previousParameterValues[selected.entity].Clear();
                }
            }

            activeShader = newShader;
        }

        private void UpdateShaderParameters()
        {
            // Update shader parameters for active shaders
            // Validate parameters before applying
            if (_activeTileLayerShader != null && _tileShaderEntity.HasValue)
            {
                UpdateShaderParametersForEntity(_tileShaderEntity.Value, _activeTileLayerShader);
            }

            if (_activeSpriteLayerShader != null && _spriteShaderEntity.HasValue)
            {
                UpdateShaderParametersForEntity(
                    _spriteShaderEntity.Value,
                    _activeSpriteLayerShader
                );
            }

            if (_activeCombinedLayerShader != null && _combinedShaderEntity.HasValue)
            {
                UpdateShaderParametersForEntity(
                    _combinedShaderEntity.Value,
                    _activeCombinedLayerShader
                );
            }
        }

        private void UpdateShaderParametersForEntity(Entity entity, Effect effect)
        {
            if (!_world.Has<LayerShaderComponent>(entity))
                return;

            // Ensure CurrentTechnique is set before setting parameters
            ShaderParameterApplier.EnsureCurrentTechnique(effect, _logger);

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
