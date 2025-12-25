# Shader Support Design Plan

**Status:** Design Phase (Updated with Architecture Fixes)  
**Created:** 2025-01-27  
**Last Updated:** 2025-01-27  
**Framework:** MonoGame 3.8+ with .NET 10.0  
**Architecture:** ECS-based rendering system

**Note:** This design has been updated based on architecture analysis. See [SHADER_SUPPORT_DESIGN_ANALYSIS.md](./SHADER_SUPPORT_DESIGN_ANALYSIS.md) for details on fixes applied.

---

## Executive Summary

This design plan outlines the architecture for adding shader support to MonoBall's rendering pipeline. Shaders will affect tile layers, sprite layers, and both layers independently, enabling visual effects like color manipulation, post-processing, lighting, and custom visual styles.

**Key Design Principles:**
- ✅ **No Backward Compatibility** - Refactor freely, update all call sites
- ✅ **Fail Fast** - Require all dependencies, throw clear exceptions
- ✅ **ECS Architecture** - Shader configuration via components
- ✅ **SOLID & DRY** - Single responsibility, dependency injection, no code duplication
- ✅ **Content Pipeline** - MonoGame .mgcb integration for shader compilation

---

## Table of Contents

1. [Architecture Overview](#architecture-overview)
2. [Shader Types & Scope](#shader-types--scope)
3. [Component Design](#component-design)
4. [System Design](#system-design)
5. [Content Pipeline Integration](#content-pipeline-integration)
6. [Shader Service Design](#shader-service-design)
7. [Rendering Integration](#rendering-integration)
8. [Example Shaders](#example-shaders)
9. [Implementation Phases](#implementation-phases)

---

## Architecture Overview

### Current Rendering Flow

```
SceneRendererSystem.Render()
  ├─ MapRendererSystem.Render()
  │   └─ SpriteBatch.Begin(effect: null) → Render tiles
  ├─ MapBorderRendererSystem.Render()
  ├─ SpriteRendererSystem.Render()
  │   └─ SpriteBatch.Begin(effect: null) → Render sprites
  └─ MapBorderRendererSystem.RenderTopLayer()
```

### Proposed Rendering Flow with Shaders

```
SceneRendererSystem.Render()
  ├─ ShaderManagerSystem.UpdateShaderState() → Update shader state (Render phase)
  ├─ MapRendererSystem.Render()
  │   └─ SpriteBatch.Begin(effect: GetTileLayerShader()) → Render tiles with shader
  ├─ MapBorderRendererSystem.Render()
  ├─ SpriteRendererSystem.Render()
  │   └─ SpriteBatch.Begin(effect: GetSpriteLayerShader()) → Render sprites with shader
  ├─ RenderTargetManager.GetOrCreateSceneRenderTarget() → Get render target for post-processing
  ├─ Apply combined layer shader (if active)
  └─ MapBorderRendererSystem.RenderTopLayer()
```

**Note:** Shader state updates happen in Render phase, just before rendering systems need them, to avoid timing mismatches.

### Key Components

1. **ShaderService** - Loads, caches, and manages shader effects
2. **ShaderComponent** - ECS component for per-entity shader configuration (future)
3. **LayerShaderComponent** - ECS component for layer-wide shader configuration
4. **ShaderManagerSystem** - Updates shader state in Render phase, manages active shaders
5. **RenderTargetManager** - Manages render targets for combined layer shaders (post-processing)
6. **Shader Events** - `LayerShaderChangedEvent`, `ShaderParameterChangedEvent` for event-driven architecture

---

## Shader Types & Scope

### 1. Tile Layer Shaders

**Scope:** Affects all tiles rendered by `MapRendererSystem`

**Use Cases:**
- Color grading (sepia, grayscale, color filters)
- Lighting effects (ambient occlusion, shadows)
- Weather effects (rain distortion, fog overlay)
- Time-of-day effects (day/night cycle, sunset tinting)
- Post-processing (blur, pixelation, scanlines)

**Example:**
```hlsl
// TileLayerColorGrading.fx
// Applies color grading to all tile layers
```

### 2. Sprite Layer Shaders

**Scope:** Affects all sprites rendered by `SpriteRendererSystem`

**Use Cases:**
- Character effects (outline, glow, shadow)
- Status effects (poison tint, frozen effect, invisibility)
- Animation effects (pixelation, distortion)
- Lighting (dynamic shadows, rim lighting)

**Example:**
```hlsl
// SpriteLayerOutline.fx
// Adds outline effect to all sprites
```

### 3. Combined Layer Shaders

**Scope:** Affects both tiles and sprites (applied after both layers render)

**Use Cases:**
- Screen-wide post-processing (bloom, vignette, chromatic aberration)
- Global effects (screen shake, flash, fade transitions)
- Color correction (brightness, contrast, saturation)
- Screen filters (CRT scanlines, film grain)

**Example:**
```hlsl
// CombinedLayerBloom.fx
// Applies bloom effect to entire screen
```

### 4. Per-Entity Shaders

**Scope:** Individual entities can have custom shaders

**Use Cases:**
- Special effects on specific NPCs
- Item glow effects
- Custom visual states
- Status effect visuals (poison glow, frozen effect)
- Character-specific effects

**Note:** Implemented as part of the shader system to enable rich visual effects per entity.

---

## Component Design

### ShaderComponent

**Purpose:** Configures shader for a specific entity

**Location:** `MonoBall.Core/ECS/Components/ShaderComponent.cs`

**Design:**
```csharp
namespace MonoBall.Core.ECS.Components
{
    /// <summary>
    /// Component that applies a shader effect to a specific entity.
    /// When present, this shader overrides layer shaders for this entity.
    /// </summary>
    public struct ShaderComponent
    {
        /// <summary>
        /// The shader effect ID to apply.
        /// </summary>
        public string ShaderId { get; set; }

        /// <summary>
        /// Shader parameters dictionary (parameter name -> value).
        /// Values can be: float, Vector2, Vector3, Vector4, Color, Texture2D, Matrix
        /// Note: Parameters are validated before being applied to shaders.
        /// </summary>
        public Dictionary<string, object> Parameters { get; set; }

        /// <summary>
        /// Whether the shader is enabled.
        /// </summary>
        public bool IsEnabled { get; set; }

        /// <summary>
        /// Render order for per-entity shaders (lower = rendered first).
        /// Per-entity shaders are applied after layer shaders.
        /// </summary>
        public int RenderOrder { get; set; }
    }
}
```

**Usage:**
- Attach to any entity (NPC, Player, Item, etc.)
- Shader is applied when entity is rendered
- Overrides layer shaders for this specific entity
- Can be combined with `ShaderParameterAnimationComponent` for animated effects

---

### LayerShaderComponent

**Purpose:** Configures shader for an entire layer (tiles or sprites)

**Location:** `MonoBall.Core/ECS/Components/LayerShaderComponent.cs`

**Design:**
```csharp
namespace MonoBall.Core.ECS.Components
{
    /// <summary>
    /// Defines which rendering layer a shader applies to.
    /// </summary>
    public enum ShaderLayer
    {
        /// <summary>
        /// Shader affects tile layers only.
        /// </summary>
        TileLayer,

        /// <summary>
        /// Shader affects sprite layers only.
        /// </summary>
        SpriteLayer,

        /// <summary>
        /// Shader affects both tile and sprite layers (post-processing).
        /// </summary>
        CombinedLayer
    }

    /// <summary>
    /// Component that applies a shader effect to an entire rendering layer.
    /// Attach to a scene entity or global entity to affect layer rendering.
    /// </summary>
    public struct LayerShaderComponent
    {
        /// <summary>
        /// The shader effect ID to apply.
        /// </summary>
        public string ShaderId { get; set; }

        /// <summary>
        /// Which layer(s) this shader affects.
        /// </summary>
        public ShaderLayer Layer { get; set; }

        /// <summary>
        /// Shader parameters dictionary (parameter name -> value).
        /// Values can be: float, Vector2, Vector3, Vector4, Color, Texture2D, Matrix

        /// Note: Parameters are validated before being applied to shaders.
        /// 
        /// Future Enhancement: Consider strongly-typed parameter classes per shader for compile-time type safety.
        /// Example: ColorGradingParameters class instead of Dictionary&lt;string, object&gt;.
        /// </summary>
        public Dictionary<string, object> Parameters { get; set; }

        /// <summary>
        /// Whether the shader is enabled.
        /// </summary>
        public bool IsEnabled { get; set; }

        /// <summary>
        /// Render order for multiple shaders on the same layer (lower = applied first).
        /// </summary>
        public int RenderOrder { get; set; }
    }
}
```

---

### ShaderParameterAnimationComponent

**Purpose:** Animates shader parameters over time (tweening, easing)

**Location:** `MonoBall.Core/ECS/Components/ShaderParameterAnimationComponent.cs`

**Design:**
```csharp
namespace MonoBall.Core.ECS.Components
{
    /// <summary>
    /// Defines easing functions for shader parameter animation.
    /// </summary>
    public enum EasingFunction
    {
        /// <summary>
        /// Linear interpolation (no easing).
        /// </summary>
        Linear,

        /// <summary>
        /// Ease in (slow start, fast end).
        /// </summary>
        EaseIn,

        /// <summary>
        /// Ease out (fast start, slow end).
        /// </summary>
        EaseOut,

        /// <summary>
        /// Ease in-out (slow start and end, fast middle).
        /// </summary>
        EaseInOut,

        /// <summary>
        /// Smooth step interpolation.
        /// </summary>
        SmoothStep
    }

    /// <summary>
    /// Component that animates shader parameters over time.
    /// Can be attached to entities with ShaderComponent or LayerShaderComponent.
    /// </summary>
    public struct ShaderParameterAnimationComponent
    {
        /// <summary>
        /// The name of the shader parameter to animate.
        /// </summary>
        public string ParameterName { get; set; }

        /// <summary>
        /// The starting value for the animation.
        /// </summary>
        public object StartValue { get; set; }

        /// <summary>
        /// The ending value for the animation.
        /// </summary>
        public object EndValue { get; set; }

        /// <summary>
        /// The duration of the animation in seconds.
        /// </summary>
        public float Duration { get; set; }

        /// <summary>
        /// The elapsed time since animation started (in seconds).
        /// </summary>
        public float ElapsedTime { get; set; }

        /// <summary>
        /// The easing function to use for interpolation.
        /// </summary>
        public EasingFunction Easing { get; set; }

        /// <summary>
        /// Whether the animation loops (restarts when complete).
        /// </summary>
        public bool IsLooping { get; set; }

        /// <summary>
        /// Whether the animation is enabled.
        /// </summary>
        public bool IsEnabled { get; set; }

        /// <summary>
        /// Whether to ping-pong the animation (reverse direction when looping).
        /// </summary>
        public bool PingPong { get; set; }
    }
}
```

**Usage:**
- Attach to entities with `ShaderComponent` or entities with `LayerShaderComponent`
- Animates a single parameter over time
- Supports multiple animations per entity (multiple components)
- Can loop or ping-pong for continuous effects

---

## Event System Integration

### Shader Events

**Purpose:** Events for shader changes, following event-driven architecture pattern

**Location:** `MonoBall.Core/ECS/Events/`

**Design:**
```csharp
namespace MonoBall.Core.ECS.Events
{
    /// <summary>
    /// Event fired when a layer shader is enabled, disabled, or changed.
    /// </summary>
    public struct LayerShaderChangedEvent
    {
        /// <summary>
        /// The layer affected by the shader change.
        /// </summary>
        public Components.ShaderLayer Layer { get; set; }

        /// <summary>
        /// The previous shader ID (null if no previous shader).
        /// </summary>
        public string? PreviousShaderId { get; set; }

        /// <summary>
        /// The new shader ID (null if shader was disabled).
        /// </summary>
        public string? NewShaderId { get; set; }

        /// <summary>
        /// The entity that owns the LayerShaderComponent.
        /// </summary>
        public Entity ShaderEntity { get; set; }
    }

    /// <summary>
    /// Event fired when shader parameters are updated.
    /// </summary>
    public struct ShaderParameterChangedEvent
    {
        /// <summary>
        /// The layer affected by the parameter change.
        /// </summary>
        public Components.ShaderLayer Layer { get; set; }

        /// <summary>
        /// The shader ID whose parameter changed.
        /// </summary>
        public string ShaderId { get; set; }

        /// <summary>
        /// The name of the parameter that changed.
        /// </summary>
        public string ParameterName { get; set; }

        /// <summary>
        /// The old parameter value (null if parameter was just added).
        /// </summary>
        public object? OldValue { get; set; }

        /// <summary>
        /// The new parameter value.
        /// </summary>
        public object? NewValue { get; set; }

        /// <summary>
        /// The entity that owns the LayerShaderComponent.
        /// </summary>
        public Entity ShaderEntity { get; set; }
    }
}
```

**Usage:**
- Mods can subscribe to shader change events
- Systems can react to shader parameter updates
- Enables event-driven shader management

---

## System Design

### ShaderService

**Purpose:** Loads, caches, and provides access to shader effects

**Location:** `MonoBall.Core/Rendering/ShaderService.cs`

**Interface:** `MonoBall.Core/Rendering/IShaderService.cs`

**Design:**
```csharp
namespace MonoBall.Core.Rendering
{
    /// <summary>
    /// Service for loading and managing shader effects.
    /// </summary>
    public interface IShaderService
    {
        /// <summary>
        /// Loads a shader effect from content.
        /// </summary>
        /// <param name="shaderId">The shader ID (e.g., "TileLayerColorGrading").</param>
        /// <returns>The loaded Effect, or null if not found.</returns>
        Effect? LoadShader(string shaderId);

        /// <summary>
        /// Gets a cached shader effect, or loads it if not cached.
        /// </summary>
        /// <param name="shaderId">The shader ID.</param>
        /// <returns>The Effect, or null if not found.</returns>
        Effect? GetShader(string shaderId);

        /// <summary>
        /// Checks if a shader exists and is loaded.
        /// </summary>
        /// <param name="shaderId">The shader ID.</param>
        /// <returns>True if the shader exists and is loaded.</returns>
        bool HasShader(string shaderId);

        /// <summary>
        /// Unloads a shader from cache.
        /// </summary>
        /// <param name="shaderId">The shader ID.</param>
        void UnloadShader(string shaderId);

        /// <summary>
        /// Unloads all shaders from cache.
        /// </summary>
        void UnloadAllShaders();
    }
}
```

**Implementation Notes:**
- Uses `Content.Load<Effect>()` for loading
- LRU cache for performance (similar to ContentProvider pattern)
- Thread-safe for async loading scenarios
- Validates shader existence before loading
- Throws `ArgumentNullException` for null shaderId

---

### ShaderManagerSystem

**Purpose:** Updates shader state in Render phase and manages active shaders based on ECS components

**Location:** `MonoBall.Core/ECS/Systems/ShaderManagerSystem.cs`

**Design:**
```csharp
namespace MonoBall.Core.ECS.Systems
{
    /// <summary>
    /// System that manages shader effects and updates their parameters.
    /// Shader state is updated in Render phase to avoid timing mismatches.
    /// </summary>
    public class ShaderManagerSystem : BaseSystem<World, float>
    {
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

        public ShaderManagerSystem(
            World world,
            IShaderService shaderService,
            IShaderParameterValidator parameterValidator,
            ILogger logger
        ) : base(world)
        {
            _shaderService = shaderService ?? throw new ArgumentNullException(nameof(shaderService));
            _parameterValidator = parameterValidator ?? throw new ArgumentNullException(nameof(parameterValidator));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _layerShaderQuery = new QueryDescription()
                .WithAll<LayerShaderComponent>();
        }

        /// <summary>
        /// Updates shader state. Called in Render phase, just before rendering systems need shaders.
        /// </summary>
        public void UpdateShaderState()
        {
            if (_shadersDirty)
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

        private void UpdateActiveShaders()
        {
            // Query for active layer shaders, sort by RenderOrder
            // Select enabled shader with lowest RenderOrder per layer
            // Fire events when shaders change
            // Cache results for rendering systems
            
            var tileShaders = new List<(Entity entity, LayerShaderComponent shader)>();
            var spriteShaders = new List<(Entity entity, LayerShaderComponent shader)>();
            var combinedShaders = new List<(Entity entity, LayerShaderComponent shader)>();

            World.Query(in _layerShaderQuery, (Entity entity, ref LayerShaderComponent shader) =>
            {
                if (!shader.IsEnabled) return;

                switch (shader.Layer)
                {
                    case ShaderLayer.TileLayer:
                        tileShaders.Add((entity, shader));
                        break;
                    case ShaderLayer.SpriteLayer:
                        spriteShaders.Add((entity, shader));
                        break;
                    case ShaderLayer.CombinedLayer:
                        combinedShaders.Add((entity, shader));
                        break;
                }
            });

            // Select shader with lowest RenderOrder for each layer
            UpdateLayerShader(
                tileShaders,
                ShaderLayer.TileLayer,
                ref _activeTileLayerShader,
                ref _previousTileShaderId,
                ref _tileShaderEntity
            );
            UpdateLayerShader(
                spriteShaders,
                ShaderLayer.SpriteLayer,
                ref _activeSpriteLayerShader,
                ref _previousSpriteShaderId,
                ref _spriteShaderEntity
            );
            UpdateLayerShader(
                combinedShaders,
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
            
            var newShader = _shaderService.GetShader(selected.shader.ShaderId);
            if (newShader == null)
            {
                _logger.Warning(
                    "Shader {ShaderId} not found for layer {Layer}",
                    selected.shader.ShaderId,
                    layer
                );
                return;
            }

            // Check if shader changed
            if (selected.shader.ShaderId != previousShaderId)
            {
                FireShaderChangedEvent(layer, previousShaderId, selected.shader.ShaderId, selected.entity);
                previousShaderId = selected.shader.ShaderId;
                shaderEntity = selected.entity;
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
                UpdateShaderParametersForEntity(_spriteShaderEntity.Value, _activeSpriteLayerShader);
            }
            if (_activeCombinedLayerShader != null && _combinedShaderEntity.HasValue)
            {
                UpdateShaderParametersForEntity(_combinedShaderEntity.Value, _activeCombinedLayerShader);
            }
        }

        private void UpdateShaderParametersForEntity(Entity entity, Effect effect)
        {
            if (!World.Has<LayerShaderComponent>(entity))
                return;

            ref var shader = ref World.Get<LayerShaderComponent>(entity);
            if (shader.Parameters == null)
                return;

            foreach (var (paramName, value) in shader.Parameters)
            {
                if (!effect.Parameters.Contains(paramName))
                {
                    _logger.Warning(
                        "Shader {ShaderId} does not have parameter {ParamName}",
                        shader.ShaderId,
                        paramName
                    );
                    continue;
                }

                // Validate parameter
                if (!_parameterValidator.ValidateParameter(shader.ShaderId, paramName, value, out var error))
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
            }
        }

        private void ApplyShaderParameter(Effect effect, string paramName, object value)
        {
            var param = effect.Parameters[paramName];
            try
            {
                switch (value)
                {
                    case float f:
                        if (param.ParameterType == EffectParameterType.Single)
                            param.SetValue(f);
                        break;
                    case Vector2 v2:
                        if (param.ParameterType == EffectParameterType.Vector2)
                            param.SetValue(v2);
                        break;
                    case Vector3 v3:
                        if (param.ParameterType == EffectParameterType.Vector3)
                            param.SetValue(v3);
                        break;
                    case Vector4 v4:
                        if (param.ParameterType == EffectParameterType.Vector4)
                            param.SetValue(v4);
                        break;
                    case Color color:
                        if (param.ParameterType == EffectParameterType.Vector4)
                            param.SetValue(color.ToVector4());
                        break;
                    case Texture2D texture:
                        if (param.ParameterType == EffectParameterType.Texture2D)
                            param.SetValue(texture);
                        break;
                    case Matrix matrix:
                        if (param.ParameterType == EffectParameterType.Matrix)
                            param.SetValue(matrix);
                        break;
                    default:
                        _logger.Warning(
                            "Unsupported parameter type {Type} for {ParamName}",
                            value.GetType(),
                            paramName
                        );
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to set shader parameter {ParamName}", paramName);
            }
        }

        private void FireShaderChangedEvent(ShaderLayer layer, string? previousShaderId, string? newShaderId, Entity shaderEntity)
        {
            var evt = new Events.LayerShaderChangedEvent
            {
                Layer = layer,
                PreviousShaderId = previousShaderId,
                NewShaderId = newShaderId,
                ShaderEntity = shaderEntity
            };
            EventBus.Send(ref evt);
        }

        /// <summary>
        /// Marks shaders as dirty, forcing an update on next UpdateShaderState() call.
        /// Called when components are added/removed/modified.
        /// </summary>
        public void MarkShadersDirty()
        {
            _shadersDirty = true;
        }
    }
}
```

**Key Features:**
- Updates shader state in Render phase (not Update phase) to avoid timing mismatches
- Caches active shaders per layer for performance
- Uses dirty flag to avoid unnecessary updates
- Validates shader parameters before applying
- Fires events when shaders change
- Supports multiple shaders per layer (lowest RenderOrder wins)
- Provides shader access to rendering systems

**Note:** This system does NOT override `Update()` - it's called explicitly from `SceneRendererSystem.Render()`.

---

## Content Pipeline Integration

### Directory Structure

```
MonoBall.Core/
├── Content/
│   ├── Shaders/
│   │   ├── TileLayer/
│   │   │   ├── ColorGrading.fx
│   │   │   ├── Sepia.fx
│   │   │   └── Fog.fx
│   │   ├── SpriteLayer/
│   │   │   ├── Outline.fx
│   │   │   ├── Glow.fx
│   │   │   └── Shadow.fx
│   │   └── CombinedLayer/
│   │       ├── Bloom.fx
│   │       ├── Vignette.fx
│   │       └── Scanlines.fx
│   └── MonoBall.mgcb
```

### MonoGame Content Pipeline (.mgcb)

**Shader Build Configuration:**
```
#begin Shaders/TileLayer/ColorGrading.fx
/importer:EffectImporter
/processor:EffectProcessor
/build:Shaders/TileLayer/ColorGrading.fx

#begin Shaders/SpriteLayer/Outline.fx
/importer:EffectImporter
/processor:EffectProcessor
/build:Shaders/SpriteLayer/Outline.fx

#begin Shaders/CombinedLayer/Bloom.fx
/importer:EffectImporter
/processor:EffectProcessor
/build:Shaders/CombinedLayer/Bloom.fx
```

**Shader Naming Convention:**
- Format: `{LayerType}{EffectName}.fx`
- Examples:
  - `TileLayerColorGrading.fx` → Shader ID: `"TileLayerColorGrading"`
  - `SpriteLayerOutline.fx` → Shader ID: `"SpriteLayerOutline"`
  - `CombinedLayerBloom.fx` → Shader ID: `"CombinedLayerBloom"`

**Content Loading:**
```csharp
// In ShaderService.LoadShader()
string contentPath = $"Shaders/{GetShaderSubdirectory(shaderId)}/{shaderId}.fx";
Effect effect = Content.Load<Effect>(contentPath);
```

---

## Shader Service Design

### ShaderService Implementation

**Location:** `MonoBall.Core/Rendering/ShaderService.cs`

**Key Features:**
- Dependency injection: `ContentManager`, `GraphicsDevice`, `ILogger`
- LRU cache for loaded shaders (max 20 shaders)
- Thread-safe loading and caching
- Validates shader existence before loading
- Throws clear exceptions for missing shaders

**Error Handling:**
- `ArgumentNullException` for null shaderId
- `InvalidOperationException` if ContentManager is null
- Logs warnings for missing shaders (doesn't crash)

**Caching Strategy:**
- Cache loaded `Effect` objects
- Unload least-recently-used shaders when cache is full
- Provide `UnloadShader()` and `UnloadAllShaders()` methods

---

### ShaderParameterValidator

**Purpose:** Validates shader parameters before applying them to effects

**Location:** `MonoBall.Core/Rendering/ShaderParameterValidator.cs`

**Interface:** `MonoBall.Core/Rendering/IShaderParameterValidator.cs`

**Design:**
```csharp
namespace MonoBall.Core.Rendering
{
    /// <summary>
    /// Service for validating shader parameters before application.
    /// Provides runtime validation for Dictionary&lt;string, object&gt; parameters.
    /// </summary>
    public interface IShaderParameterValidator
    {
        /// <summary>
        /// Validates a shader parameter value.
        /// </summary>
        /// <param name="shaderId">The shader ID.</param>
        /// <param name="paramName">The parameter name.</param>
        /// <param name="value">The parameter value to validate.</param>
        /// <param name="error">Output error message if validation fails.</param>
        /// <returns>True if the parameter is valid, false otherwise.</returns>
        bool ValidateParameter(string shaderId, string paramName, object value, out string? error);

        /// <summary>
        /// Gets the expected type for a shader parameter.
        /// </summary>
        /// <param name="shaderId">The shader ID.</param>
        /// <param name="paramName">The parameter name.</param>
        /// <returns>The expected parameter type, or null if unknown.</returns>
        Type? GetParameterType(string shaderId, string paramName);
    }
}
```

**Implementation Notes:**
- Validates parameter existence in shader
- Validates parameter type matches shader expectation
- Validates parameter value ranges (if applicable)
- Returns clear error messages for debugging

**Future Enhancement - Strongly-Typed Parameters:**
For better type safety and IntelliSense support, consider strongly-typed parameter classes per shader:

```csharp
/// <summary>
/// Base class for shader parameters with type-safe properties.
/// </summary>
public abstract class ShaderParameters
{
    /// <summary>
    /// Applies these parameters to the given effect.
    /// </summary>
    public abstract void ApplyToEffect(Effect effect);
}

/// <summary>
/// Strongly-typed parameters for ColorGrading shader.
/// </summary>
public class ColorGradingParameters : ShaderParameters
{
    public float Brightness { get; set; }
    public float Contrast { get; set; }
    public float Saturation { get; set; }
    public Vector3 ColorTint { get; set; }
    
    public override void ApplyToEffect(Effect effect)
    {
        effect.Parameters["Brightness"].SetValue(Brightness);
        effect.Parameters["Contrast"].SetValue(Contrast);
        effect.Parameters["Saturation"].SetValue(Saturation);
        effect.Parameters["ColorTint"].SetValue(ColorTint);
    }
}

// Usage in LayerShaderComponent (future):
// public ShaderParameters? TypedParameters { get; set; }  // Instead of Dictionary
```

**Benefits:**
- Compile-time type checking
- IntelliSense support
- No runtime type errors
- Better refactoring support

**Trade-offs:**
- Requires creating a class per shader
- Less flexible for dynamic shaders
- Dictionary approach remains for flexibility

---

### RenderTargetManager

**Purpose:** Manages render targets for combined layer shaders (post-processing)

**Location:** `MonoBall.Core/Rendering/RenderTargetManager.cs`

**Design:**
```csharp
namespace MonoBall.Core.Rendering
{
    /// <summary>
    /// Manages render targets for post-processing shaders.
    /// Handles creation, resizing, and disposal of render targets.
    /// </summary>
    public class RenderTargetManager : IDisposable
    {
        private RenderTarget2D? _sceneRenderTarget;
        private readonly GraphicsDevice _graphicsDevice;
        private readonly ILogger _logger;
        private bool _isDisposed;

        /// <summary>
        /// Initializes a new instance of RenderTargetManager.
        /// </summary>
        /// <param name="graphicsDevice">The graphics device.</param>
        /// <param name="logger">The logger for logging operations.</param>
        public RenderTargetManager(GraphicsDevice graphicsDevice, ILogger logger)
        {
            _graphicsDevice = graphicsDevice ?? throw new ArgumentNullException(nameof(graphicsDevice));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Gets or creates a render target for scene rendering.
        /// </summary>
        /// <param name="width">The render target width.</param>
        /// <param name="height">The render target height.</param>
        /// <returns>The render target, or null if creation failed.</returns>
        public RenderTarget2D? GetOrCreateSceneRenderTarget(int width, int height)
        {
            if (_isDisposed)
            {
                throw new ObjectDisposedException(nameof(RenderTargetManager));
            }

            // Recreate if size changed or doesn't exist
            if (_sceneRenderTarget == null ||
                _sceneRenderTarget.Width != width ||
                _sceneRenderTarget.Height != height)
            {
                DisposeRenderTarget();

                try
                {
                    _sceneRenderTarget = new RenderTarget2D(
                        _graphicsDevice,
                        width,
                        height,
                        false, // mipMap
                        SurfaceFormat.Color,
                        DepthFormat.None
                    );
                    _logger.Debug(
                        "Created scene render target: {Width}x{Height}",
                        width,
                        height
                    );
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Failed to create render target {Width}x{Height}", width, height);
                    return null; // Fallback to direct rendering
                }
            }

            return _sceneRenderTarget;
        }

        /// <summary>
        /// Disposes the current render target.
        /// </summary>
        public void DisposeRenderTarget()
        {
            if (_sceneRenderTarget != null)
            {
                _sceneRenderTarget.Dispose();
                _sceneRenderTarget = null;
            }
        }

        /// <summary>
        /// Disposes all resources.
        /// </summary>
        public void Dispose()
        {
            if (!_isDisposed)
            {
                DisposeRenderTarget();
                _isDisposed = true;
            }
        }
    }
}
```

**Key Features:**
- Creates render targets on-demand
- Automatically resizes when dimensions change
- Handles creation failures gracefully (returns null, falls back to direct rendering)
- Proper disposal to prevent memory leaks
- Thread-safe (if needed in future)

---

## Rendering Integration

### MapRendererSystem Changes

**Current:**
```csharp
_spriteBatch.Begin(
    SpriteSortMode.Deferred,
    BlendState.AlphaBlend,
    SamplerState.PointClamp,
    null,  // Effect = null
    null,
    null,
    transform
);
```

**Proposed:**
```csharp
Effect? tileShader = _shaderManagerSystem?.GetTileLayerShader();
_spriteBatch.Begin(
    SpriteSortMode.Deferred,
    BlendState.AlphaBlend,
    SamplerState.PointClamp,
    null,
    null,
    tileShader,  // Apply tile layer shader
    transform
);
```

**Dependencies:**
- Inject `ShaderManagerSystem` via constructor (optional/nullable)
- Nullable reference (shader is optional)
- Shader state updated in Render phase before this system renders

---

### SpriteRendererSystem Changes

**Current:**
```csharp
_spriteBatch.Begin(
    SpriteSortMode.Deferred,
    BlendState.AlphaBlend,
    SamplerState.PointClamp,
    null,
    null,
    null,  // Effect = null
    transform
);
```

**Proposed:**
```csharp
Effect? spriteShader = _shaderManagerSystem?.GetSpriteLayerShader();
Effect? currentShader = spriteShader;

_spriteBatch.Begin(
    SpriteSortMode.Deferred,
    BlendState.AlphaBlend,
    SamplerState.PointClamp,
    null,
    null,
    currentShader,  // Apply sprite layer shader (or per-entity shader)
    transform
);

// Render each sprite (with per-entity shader support)
foreach (var (entity, spriteId, anim, pos, render) in sprites)
{
    // Check for per-entity shader
    Effect? entityShader = null;
    if (World.Has<ShaderComponent>(entity))
    {
        ref var shaderComp = ref World.Get<ShaderComponent>(entity);
        if (shaderComp.IsEnabled)
        {
            entityShader = _shaderService?.GetShader(shaderComp.ShaderId);
            if (entityShader != null)
            {
                // Apply per-entity shader parameters
                ApplyShaderParameters(entityShader, shaderComp.Parameters);
            }
        }
    }

    // Use per-entity shader if available, otherwise use layer shader
    Effect? activeShader = entityShader ?? spriteShader;
    
    // If shader changed, need to restart SpriteBatch
    if (activeShader != currentShader)
    {
        _spriteBatch.End();
        currentShader = activeShader;
        _spriteBatch.Begin(
            SpriteSortMode.Deferred,
            BlendState.AlphaBlend,
            SamplerState.PointClamp,
            null,
            null,
            currentShader,
            transform
        );
    }

    RenderSingleSprite(spriteId, anim, pos, render);
}

_spriteBatch.End();
```

**Dependencies:**
- Inject `ShaderManagerSystem` via constructor (optional/nullable)
- Inject `IShaderService` via constructor (for per-entity shaders)
- Nullable reference (shader is optional)
- Shader state updated in Render phase before this system renders
- Per-entity shaders override layer shaders
- SpriteBatch restarted when shader changes (performance consideration)

---

### SceneRendererSystem Changes

**For Combined Layer Shaders (Post-Processing):**

**Updated Render Flow:**
```csharp
public void Render(GameTime gameTime)
{
    // Update shader state BEFORE rendering (critical timing fix)
    _shaderManagerSystem?.UpdateShaderState();

    // Check if combined shader is active
    Effect? combinedShader = _shaderManagerSystem?.GetCombinedLayerShader();
    bool needsRenderTarget = combinedShader != null;

    RenderTarget2D? sceneRenderTarget = null;
    Viewport? originalViewport = null;

    if (needsRenderTarget)
    {
        // Get render target from manager
        var viewport = _graphicsDevice.Viewport;
        sceneRenderTarget = _renderTargetManager.GetOrCreateSceneRenderTarget(
            viewport.Width,
            viewport.Height
        );

        if (sceneRenderTarget != null)
        {
            // Save original viewport
            originalViewport = _graphicsDevice.Viewport;

            // Set render target
            _graphicsDevice.SetRenderTarget(sceneRenderTarget);
            _graphicsDevice.Clear(Color.Transparent);
        }
        else
        {
            // Render target creation failed - fallback to direct rendering
            _logger.Warning("Failed to create render target, rendering directly");
            needsRenderTarget = false;
        }
    }

    try
    {
        // Render all layers to render target (if using post-processing) or directly
        _mapRendererSystem.Render(gameTime);
        
        if (_mapBorderRendererSystem != null)
        {
            _mapBorderRendererSystem.Render(gameTime);
        }

        _spriteRendererSystem?.Render(gameTime);

        if (_mapBorderRendererSystem != null)
        {
            _mapBorderRendererSystem.RenderTopLayer(gameTime);
        }

        // Apply combined layer shader if active
        if (needsRenderTarget && sceneRenderTarget != null && combinedShader != null)
        {
            // Restore original viewport
            if (originalViewport.HasValue)
            {
                _graphicsDevice.Viewport = originalViewport.Value;
            }

            // Set render target to null (render to back buffer)
            _graphicsDevice.SetRenderTarget(null);

            // Apply post-processing shader
            _spriteBatch.Begin(
                SpriteSortMode.Deferred,
                BlendState.Opaque,
                SamplerState.LinearClamp,
                null,
                null,
                combinedShader
            );
            _spriteBatch.Draw(sceneRenderTarget, Vector2.Zero, Color.White);
            _spriteBatch.End();
        }
    }
    finally
    {
        // Always restore render target and viewport
        if (needsRenderTarget)
        {
            _graphicsDevice.SetRenderTarget(null);
            if (originalViewport.HasValue)
            {
                _graphicsDevice.Viewport = originalViewport.Value;
            }
        }
    }
}
```

**Dependencies:**
- Inject `ShaderManagerSystem` via constructor (optional/nullable)
- Inject `RenderTargetManager` via constructor (required if combined shaders are used)
- Shader state updated at start of Render() method
- Render target managed by `RenderTargetManager` (prevents memory leaks)

---

## Example Shaders

### Example 1: Tile Layer - Color Grading

**File:** `Content/Shaders/TileLayer/ColorGrading.fx`

```hlsl
// ColorGrading.fx
// Applies color grading to tile layers

sampler TextureSampler : register(s0);

float Brightness : register(c0);
float Contrast : register(c1);
float Saturation : register(c2);
float3 ColorTint : register(c3);

struct VertexShaderOutput
{
    float4 Position : POSITION0;
    float4 Color : COLOR0;
    float2 TextureCoordinates : TEXCOORD0;
};

float4 MainPS(VertexShaderOutput input) : COLOR0
{
    float4 color = tex2D(TextureSampler, input.TextureCoordinates);
    color *= input.Color;
    
    // Apply brightness
    color.rgb += Brightness;
    
    // Apply contrast
    color.rgb = (color.rgb - 0.5) * Contrast + 0.5;
    
    // Apply saturation
    float gray = dot(color.rgb, float3(0.299, 0.587, 0.114));
    color.rgb = lerp(gray, color.rgb, Saturation);
    
    // Apply color tint
    color.rgb *= ColorTint;
    
    return color;
}

technique ColorGrading
{
    pass Pass1
    {
        VertexShader = compile vs_2_0 SpriteVertexShader();
        PixelShader = compile ps_2_0 MainPS();
    }
}
```

**Usage:**
```csharp
var shaderComponent = new LayerShaderComponent
{
    ShaderId = "TileLayerColorGrading",
    Layer = ShaderLayer.TileLayer,
    IsEnabled = true,
    Parameters = new Dictionary<string, object>
    {
        ["Brightness"] = 0.1f,
        ["Contrast"] = 1.2f,
        ["Saturation"] = 0.8f,
        ["ColorTint"] = new Vector3(1.0f, 0.95f, 0.9f) // Warm tint
    }
};
```

---

### Example 2: Sprite Layer - Outline

**File:** `Content/Shaders/SpriteLayer/Outline.fx`

```hlsl
// Outline.fx
// Adds outline effect to sprites

sampler TextureSampler : register(s0);

float4 OutlineColor : register(c0);
float OutlineWidth : register(c1);

struct VertexShaderOutput
{
    float4 Position : POSITION0;
    float4 Color : COLOR0;
    float2 TextureCoordinates : TEXCOORD0;
};

float4 MainPS(VertexShaderOutput input) : COLOR0
{
    float4 color = tex2D(TextureSampler, input.TextureCoordinates);
    color *= input.Color;
    
    // Sample neighboring pixels for outline detection
    float2 texelSize = 1.0 / 256.0; // Adjust based on texture size
    float alpha = color.a;
    
    // Check if pixel is on edge
    float edge = 0.0;
    for (int x = -1; x <= 1; x++)
    {
        for (int y = -1; y <= 1; y++)
        {
            if (x == 0 && y == 0) continue;
            float neighborAlpha = tex2D(TextureSampler, 
                input.TextureCoordinates + float2(x, y) * texelSize * OutlineWidth).a;
            if (neighborAlpha < alpha)
                edge = max(edge, neighborAlpha);
        }
    }
    
    // Blend outline color
    color.rgb = lerp(color.rgb, OutlineColor.rgb, edge);
    color.a = max(color.a, edge * OutlineColor.a);
    
    return color;
}

technique Outline
{
    pass Pass1
    {
        VertexShader = compile vs_2_0 SpriteVertexShader();
        PixelShader = compile ps_2_0 MainPS();
    }
}
```

**Usage:**
```csharp
var shaderComponent = new LayerShaderComponent
{
    ShaderId = "SpriteLayerOutline",
    Layer = ShaderLayer.SpriteLayer,
    IsEnabled = true,
    Parameters = new Dictionary<string, object>
    {
        ["OutlineColor"] = Color.Black,
        ["OutlineWidth"] = 2.0f
    }
};
```

---

### Example 3: Combined Layer - Vignette

**File:** `Content/Shaders/CombinedLayer/Vignette.fx`

```hlsl
// Vignette.fx
// Applies vignette effect to entire screen

sampler TextureSampler : register(s0);

float VignetteIntensity : register(c0);
float VignetteRadius : register(c1);
float2 ScreenSize : register(c2);

struct VertexShaderOutput
{
    float4 Position : POSITION0;
    float4 Color : COLOR0;
    float2 TextureCoordinates : TEXCOORD0;
};

float4 MainPS(VertexShaderOutput input) : COLOR0
{
    float4 color = tex2D(TextureSampler, input.TextureCoordinates);
    
    // Calculate distance from center
    float2 center = float2(0.5, 0.5);
    float2 uv = input.TextureCoordinates;
    float dist = distance(uv, center);
    
    // Apply vignette
    float vignette = 1.0 - smoothstep(VignetteRadius, 1.0, dist) * VignetteIntensity;
    color.rgb *= vignette;
    
    return color;
}

technique Vignette
{
    pass Pass1
    {
        VertexShader = compile vs_2_0 SpriteVertexShader();
        PixelShader = compile ps_2_0 MainPS();
    }
}
```

**Usage:**
```csharp
var shaderComponent = new LayerShaderComponent
{
    ShaderId = "CombinedLayerVignette",
    Layer = ShaderLayer.CombinedLayer,
    IsEnabled = true,
    Parameters = new Dictionary<string, object>
    {
        ["VignetteIntensity"] = 0.5f,
        ["VignetteRadius"] = 0.7f,
        ["ScreenSize"] = new Vector2(1280, 800)
    }
};
```

---

## Implementation Phases

### Phase 1: Foundation (Core Infrastructure)

**Tasks:**
1. ✅ Create `IShaderService` interface
2. ✅ Implement `ShaderService` with content loading and caching
3. ✅ Create `IShaderParameterValidator` interface and implementation
4. ✅ Create `LayerShaderComponent` component
5. ✅ Create `ShaderComponent` component (per-entity shaders)
6. ✅ Create `ShaderParameterAnimationComponent` component
7. ✅ Create shader events (`LayerShaderChangedEvent`, `ShaderParameterChangedEvent`)
8. ✅ Create `ShaderManagerSystem` for managing active shaders (Render phase updates)
9. ✅ Create `ShaderParameterAnimationSystem` for animating shader parameters
10. ✅ Create `RenderTargetManager` for post-processing render targets
11. ✅ Register `ShaderService` and `ShaderParameterValidator` in `GameServices`
12. ✅ Add shader loading to `GameInitializationService`

**Deliverables:**
- Shader service loads and caches effects
- Parameter validator validates shader parameters
- ECS components for shader configuration (layer, per-entity, animation)
- Event system integration for shader changes
- System for managing shader state (Render phase)
- System for animating shader parameters
- Render target manager for post-processing

---

### Phase 2: Tile Layer Shaders

**Tasks:**
1. ✅ Create example tile layer shader (ColorGrading.fx)
2. ✅ Integrate `ShaderManagerSystem` into `MapRendererSystem` (constructor injection)
3. ✅ Update `MapRendererSystem.Render()` to get shader from `ShaderManagerSystem`
4. ✅ Update `MapRendererSystem.Begin()` to use tile layer shader
5. ✅ Test shader parameter validation
6. ✅ Test shader change events
7. ✅ Test with example shader

**Deliverables:**
- Tile layers render with shaders
- Shader parameters validated before application
- Events fired on shader changes
- Example shader demonstrates functionality

---

### Phase 3: Sprite Layer Shaders

**Tasks:**
1. ✅ Create example sprite layer shader (Outline.fx)
2. ✅ Integrate `ShaderManagerSystem` into `SpriteRendererSystem` (constructor injection)
3. ✅ Update `SpriteRendererSystem.Render()` to get shader from `ShaderManagerSystem`
4. ✅ Update `SpriteRendererSystem.Begin()` to use sprite layer shader
5. ✅ Test shader parameter validation
6. ✅ Test shader change events
7. ✅ Test with example shader

**Deliverables:**
- Sprite layers render with shaders
- Shader parameters validated before application
- Events fired on shader changes
- Example shader demonstrates functionality

---

### Phase 4: Combined Layer Shaders (Post-Processing)

**Tasks:**
1. ✅ Integrate `RenderTargetManager` into `SceneRendererSystem` (constructor injection)
2. ✅ Integrate `ShaderManagerSystem` into `SceneRendererSystem` (constructor injection)
3. ✅ Update `SceneRendererSystem.Render()` to:
   - Call `ShaderManagerSystem.UpdateShaderState()` at start
   - Use `RenderTargetManager` for render target lifecycle
   - Apply combined shader as post-processing pass
4. ✅ Create example combined layer shader (Vignette.fx)
5. ✅ Handle render target cleanup and disposal (via `RenderTargetManager`)
6. ✅ Test render target creation failure fallback
7. ✅ Test with example shader

**Deliverables:**
- Combined layer shaders work via render targets
- Render target lifecycle properly managed (no memory leaks)
- Fallback to direct rendering if render target creation fails
- Post-processing effects functional

---

### Phase 5: Per-Entity Shaders

**Tasks:**
1. ✅ Create `ShaderComponent` component
2. ✅ Update `SpriteRendererSystem` to support per-entity shaders
3. ✅ Update `MapRendererSystem` to support per-entity shaders (for tile entities if needed)
4. ✅ Integrate `IShaderService` into rendering systems
5. ✅ Add per-entity shader parameter application logic
6. ✅ Create example per-entity shader (Glow.fx for items)
7. ✅ Test per-entity shaders override layer shaders correctly
8. ✅ Test multiple entities with different shaders

**Deliverables:**
- Per-entity shader support functional
- Entities can have custom shaders
- Per-entity shaders override layer shaders
- Example shader demonstrates functionality

---

### Phase 6: Shader Parameter Animation

**Tasks:**
1. ✅ Create `ShaderParameterAnimationComponent` component
2. ✅ Create `ShaderParameterAnimationSystem` system
3. ✅ Implement easing functions (Linear, EaseIn, EaseOut, EaseInOut, SmoothStep)
4. ✅ Implement parameter interpolation (float, Vector2, Vector3, Vector4, Color)
5. ✅ Support looping and ping-pong animations
6. ✅ Integrate with `ShaderManagerSystem` for parameter updates
7. ✅ Fire events when animated parameters change
8. ✅ Create example animations:
   - Pulsing glow effect
   - Fade in/out effect
   - Color transition
9. ✅ Test animation system with layer and per-entity shaders

**Deliverables:**
- Shader parameter animation system functional
- Multiple easing functions supported
- Looping and ping-pong animations work
- Example animations demonstrate functionality

---

### Phase 7: Content Pipeline & Examples

**Tasks:**
1. ✅ Add shader files to Content/Shaders/
2. ✅ Update MonoBall.mgcb with shader build entries
3. ✅ Create additional example shaders:
   - TileLayer: Sepia, Fog, Pixelation
   - SpriteLayer: Glow, Shadow, Distortion
   - CombinedLayer: Bloom, Scanlines, ChromaticAberration
   - PerEntity: ItemGlow, StatusEffect, Highlight
4. ✅ Document shader creation guidelines
5. ✅ Document shader parameter animation usage
6. ✅ Test all example shaders
7. ✅ Test shader animations

**Deliverables:**
- Complete shader library with examples
- Content pipeline configured
- Documentation for shader authors
- Animation examples and documentation

---

## Technical Considerations

### Performance

**Optimizations:**
- Cache active shaders per layer (avoid querying every frame)
- Use dirty flag to skip updates when nothing changed
- LRU cache for loaded shaders (max 20 shaders)
- Only update shader parameters when components change
- Render targets only created when combined shaders active
- Render targets reused and resized as needed (no unnecessary recreation)

**Performance Impact:**
- Tile layer shaders: Minimal (single shader per batch)
- Sprite layer shaders: Minimal (single shader per batch)
- Combined layer shaders: Moderate (requires render target, extra pass)

---

### Memory Management

**Shader Caching:**
- LRU eviction when cache is full
- Unload shaders when scenes unload
- Dispose render targets properly

**Disposal:**
- `ShaderService` implements `IDisposable`
- Unloads all shaders on disposal
- `RenderTargetManager` implements `IDisposable`
- Render targets disposed via `RenderTargetManager.Dispose()`
- `RenderTargetManager` disposed in `SceneRendererSystem` or `SystemManager`

---

### Error Handling

**Shader Loading:**
- Missing shaders: Log warning, return null (graceful degradation)
- Invalid shader files: Log error, throw exception
- Missing parameters: Log warning, use default values

**Rendering:**
- Null shader: Render normally (no shader applied)
- Invalid shader state: Log error, disable shader
- Missing shader parameters: Log warning, skip parameter
- Invalid parameter types: Log warning, skip parameter
- Render target creation failure: Fallback to direct rendering, log warning

---

## Future Enhancements

### Advanced Shader Features
- Shader parameter animation timelines (sequence multiple animations)
- Shader parameter keyframe system
- Shader blending modes (multiple shaders per entity)
- Shader inheritance/composition

### Shader Quality Levels / LOD
- Multiple quality levels (Low, Medium, High, Ultra)
- Automatic shader disabling based on quality settings
- Performance scaling for different hardware

### Shader Hot-Reloading (Development)
- File system watcher for shader files
- Runtime shader reloading without game restart
- Development-only feature (conditional compilation)

### Shader Stacking / Composition
- Multiple shaders per layer that stack together
- Shader blend modes (Replace, Multiply, Add, Overlay, Screen)
- Post-processing chain support

### Mod Support
- Mods can provide custom shaders
- Shader definitions in mod.json
- Mod shader priority system
- Shader override support

### Shader Debugging Tools
- Debug visualization for active shaders
- Parameter value display
- Performance profiling
- Shader complexity visualization

### Advanced Post-Processing
- Multiple render passes
- Depth buffer support
- Particle effects integration
- Screen-space effects

---

## Testing Strategy

### Unit Tests
- `ShaderService` loading and caching
- `ShaderManagerSystem` shader selection logic
- Component validation

### Integration Tests
- Shader application to rendering systems
- Render target management
- Parameter updates

### Visual Tests
- Example shaders render correctly
- Performance benchmarks
- Memory leak detection

---

## Documentation Requirements

### XML Documentation
- All public APIs documented
- Parameter descriptions
- Exception documentation
- Usage examples

### Shader Author Guide
- HLSL shader structure
- Parameter naming conventions
- Content pipeline setup
- Testing guidelines

---

## Conclusion

This design plan provides a comprehensive architecture for shader support in MonoBall, following SOLID principles, ECS patterns, and MonoGame best practices. The phased implementation approach allows incremental development and testing, ensuring each phase is complete before moving to the next.

**Key Benefits:**
- ✅ Flexible shader system for visual effects
- ✅ Clean ECS integration
- ✅ Performance-optimized caching
- ✅ Extensible architecture for future enhancements
- ✅ Content pipeline integration
- ✅ Clear separation of concerns

**Next Steps:**
1. Review and approve design plan
2. Begin Phase 1 implementation
3. Create initial shader examples
4. Integrate with rendering systems

