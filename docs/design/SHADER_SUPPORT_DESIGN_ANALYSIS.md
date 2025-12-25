# Shader Support Design - Architecture Analysis

**Date:** 2025-01-27  
**Status:** Critical Issues & Enhancements Identified  
**Reviewer:** Architecture Analysis

---

## Executive Summary

This document analyzes the shader support design for architecture issues, Arch ECS/event pattern violations, and forward-thinking enhancements common in the game industry. **Multiple critical issues and enhancement opportunities have been identified.**

---

## 🔴 CRITICAL ARCHITECTURE ISSUES

### 1. Update vs Render Timing Mismatch

**Issue:** `ShaderManagerSystem.Update()` is called in Update phase, but rendering systems call `GetTileLayerShader()` in Render phase. This creates a timing gap where shader state may be stale.

**Location:** 
- `ShaderManagerSystem.Update()` - called in Update phase
- `MapRendererSystem.Render()` / `SpriteRendererSystem.Render()` - called in Render phase

**Problem:**
```csharp
// Update phase
ShaderManagerSystem.Update(deltaTime); // Updates shader cache

// ... other systems run ...

// Render phase (potentially many frames later)
Effect? shader = _shaderManagerSystem.GetTileLayerShader(); // May be stale
```

**Impact:** **HIGH** - Shader changes may not apply immediately, causing visual inconsistencies

**Fix Required:**
```csharp
// Option 1: Move shader updates to Render phase (before rendering)
public void Render(GameTime gameTime)
{
    _shaderManagerSystem.UpdateShaderState(); // Update just before rendering
    _mapRendererSystem.Render(gameTime);
    // ...
}

// Option 2: Make shader queries happen in Update, cache results for Render
// Option 3: Use events to notify rendering systems of shader changes
```

**Recommendation:** Option 1 - Update shader state in Render phase, just before rendering systems need it.

---

### 2. Missing Event System Integration

**Issue:** Shader changes are not communicated via events, violating the event-driven architecture pattern used throughout the codebase.

**Location:** `ShaderManagerSystem`, `LayerShaderComponent`

**Problem:**
- No `ShaderChangedEvent` when shaders are enabled/disabled
- No `ShaderParameterChangedEvent` when parameters change
- Rendering systems must poll `ShaderManagerSystem` every frame
- Mods cannot react to shader changes

**Impact:** **MEDIUM** - Violates event-driven architecture, reduces modding capabilities

**Fix Required:**
```csharp
namespace MonoBall.Core.ECS.Events
{
    /// <summary>
    /// Event fired when a layer shader is enabled, disabled, or changed.
    /// </summary>
    public struct LayerShaderChangedEvent
    {
        public ShaderLayer Layer { get; set; }
        public string? PreviousShaderId { get; set; }
        public string? NewShaderId { get; set; }
        public Entity ShaderEntity { get; set; }
    }

    /// <summary>
    /// Event fired when shader parameters are updated.
    /// </summary>
    public struct ShaderParameterChangedEvent
    {
        public ShaderLayer Layer { get; set; }
        public string ShaderId { get; set; }
        public string ParameterName { get; set; }
        public object? OldValue { get; set; }
        public object? NewValue { get; set; }
        public Entity ShaderEntity { get; set; }
    }
}
```

**Usage:**
```csharp
// In ShaderManagerSystem.UpdateActiveShaders()
if (_previousTileShaderId != currentShaderId)
{
    var evt = new LayerShaderChangedEvent
    {
        Layer = ShaderLayer.TileLayer,
        PreviousShaderId = _previousTileShaderId,
        NewShaderId = currentShaderId,
        ShaderEntity = shaderEntity
    };
    EventBus.Send(ref evt);
}
```

---

### 3. Type-Unsafe Shader Parameters

**Issue:** `Dictionary<string, object>` for shader parameters is not type-safe and requires runtime type checking.

**Location:** `LayerShaderComponent.Parameters`

**Problem:**
```csharp
Parameters = new Dictionary<string, object>
{
    ["Brightness"] = 0.1f,  // What if shader expects int?
    ["ColorTint"] = new Vector3(...),  // What if shader expects Color?
    ["InvalidParam"] = "wrong type"  // No compile-time checking
}
```

**Impact:** **MEDIUM** - Runtime errors, no IntelliSense, difficult to validate

**Fix Required:**
```csharp
// Option 1: Strongly-typed parameter classes per shader
public abstract class ShaderParameters
{
    public abstract void ApplyToEffect(Effect effect);
}

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

// Option 2: Parameter validation service
public interface IShaderParameterValidator
{
    bool ValidateParameter(string shaderId, string paramName, object value, out string? error);
    Type GetParameterType(string shaderId, string paramName);
}
```

**Recommendation:** Option 1 for type safety, Option 2 as fallback for dynamic shaders.

---

### 4. Render Target Management Not Well Defined

**Issue:** Combined layer shaders require render targets, but design doesn't specify:
- When render targets are created/destroyed
- How to handle multiple combined shaders
- Memory management for render targets
- What happens if render target creation fails

**Location:** `SceneRendererSystem` combined shader rendering

**Problem:**
- No lifecycle management for render targets
- No fallback if render target creation fails
- No support for multiple render passes
- Render target size not specified (viewport vs full screen)

**Impact:** **HIGH** - Memory leaks, crashes, poor performance

**Fix Required:**
```csharp
public class RenderTargetManager : IDisposable
{
    private RenderTarget2D? _sceneRenderTarget;
    private readonly GraphicsDevice _graphicsDevice;
    private readonly ILogger _logger;
    private bool _isDisposed;

    public RenderTarget2D GetOrCreateSceneRenderTarget(int width, int height)
    {
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
                    false,
                    SurfaceFormat.Color,
                    DepthFormat.None
                );
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to create render target");
                return null; // Fallback to direct rendering
            }
        }
        return _sceneRenderTarget;
    }

    public void DisposeRenderTarget()
    {
        _sceneRenderTarget?.Dispose();
        _sceneRenderTarget = null;
    }

    public void Dispose()
    {
        if (!_isDisposed)
        {
            DisposeRenderTarget();
            _isDisposed = true;
        }
    }
}
```

---

### 5. No Shader Parameter Validation

**Issue:** Shader parameters are applied without validation, causing runtime crashes if:
- Parameter name doesn't exist in shader
- Parameter type doesn't match shader expectation
- Parameter value is out of valid range

**Location:** `ShaderManagerSystem.UpdateShaderParameters()`

**Problem:**
```csharp
// No validation - crashes if parameter doesn't exist
effect.Parameters["InvalidParam"].SetValue(1.0f);
```

**Impact:** **MEDIUM** - Runtime crashes, poor error messages

**Fix Required:**
```csharp
private void ApplyShaderParameter(Effect effect, string paramName, object value)
{
    if (!effect.Parameters.Contains(paramName))
    {
        _logger.Warning(
            "Shader {ShaderId} does not have parameter {ParamName}",
            _currentShaderId,
            paramName
        );
        return;
    }

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
            // ... other types
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
```

---

## 🟡 ARCH ECS PATTERN ISSUES

### 6. ShaderManagerSystem Not Following ECS Patterns

**Issue:** `ShaderManagerSystem` queries for `LayerShaderComponent` but doesn't follow standard ECS patterns:
- No entity tracking for shader components
- No handling of component add/remove
- Query runs every frame even if nothing changed

**Location:** `ShaderManagerSystem.UpdateActiveShaders()`

**Problem:**
```csharp
// Current: Queries every frame
private void UpdateActiveShaders()
{
    World.Query(in _layerShaderQuery, (ref LayerShaderComponent shader) =>
    {
        // Process all shaders every frame
    });
}
```

**Impact:** **LOW** - Performance overhead, doesn't react to component changes

**Fix Required:**
```csharp
// Option 1: Subscribe to component add/remove events
// Option 2: Cache shader entities and only update when components change
// Option 3: Use dirty flag to skip updates when nothing changed

private readonly List<Entity> _shaderEntities = new();
private bool _shadersDirty = true;

public override void Update(in float deltaTime)
{
    if (_shadersDirty)
    {
        UpdateActiveShaders();
        _shadersDirty = false;
    }
    UpdateShaderParameters(deltaTime);
}

// Subscribe to component changes via events or system callbacks
```

---

### 7. Missing Component Lifecycle Handling

**Issue:** Design doesn't specify what happens when:
- `LayerShaderComponent` is added to an entity
- `LayerShaderComponent` is removed from an entity
- `LayerShaderComponent.IsEnabled` changes
- Entity with shader component is destroyed

**Location:** `ShaderManagerSystem`, `LayerShaderComponent`

**Impact:** **MEDIUM** - Shaders may not update correctly, memory leaks

**Fix Required:**
```csharp
// Add event handlers or system callbacks
public override void Update(in float deltaTime)
{
    // Check for new shader components
    World.Query(in _newShaderQuery, (Entity entity, ref LayerShaderComponent shader) =>
    {
        if (!_shaderEntities.Contains(entity))
        {
            _shaderEntities.Add(entity);
            _shadersDirty = true;
            
            // Fire event
            var evt = new LayerShaderChangedEvent
            {
                Layer = shader.Layer,
                NewShaderId = shader.ShaderId,
                ShaderEntity = entity
            };
            EventBus.Send(ref evt);
        }
    });

    // Check for removed shader components
    _shaderEntities.RemoveAll(entity => !World.Has<LayerShaderComponent>(entity));
}
```

---

### 8. System Dependency Injection Pattern Inconsistency

**Issue:** Design uses `SetShaderManagerSystem()` pattern, but other systems use constructor injection.

**Location:** `MapRendererSystem`, `SpriteRendererSystem`

**Problem:**
```csharp
// Current pattern in codebase: Constructor injection
public MapRendererSystem(
    World world,
    GraphicsDevice graphicsDevice,
    ITilesetLoaderService tilesetLoader,
    ICameraService cameraService,
    ILogger logger
)

// Proposed pattern: Setter injection
public void SetShaderManagerSystem(ShaderManagerSystem shaderManager)
```

**Impact:** **LOW** - Inconsistent patterns, harder to test

**Fix Required:**
```csharp
// Option 1: Constructor injection (preferred)
public MapRendererSystem(
    World world,
    GraphicsDevice graphicsDevice,
    ITilesetLoaderService tilesetLoader,
    ICameraService cameraService,
    ShaderManagerSystem? shaderManagerSystem,  // Optional
    ILogger logger
)

// Option 2: Keep setter but make it required (fail fast)
public void SetShaderManagerSystem(ShaderManagerSystem shaderManager)
{
    _shaderManagerSystem = shaderManager ?? throw new ArgumentNullException(nameof(shaderManager));
}
```

**Recommendation:** Use constructor injection for consistency, make shader manager optional (nullable).

---

## 🟢 FORWARD-THINKING ENHANCEMENTS

### 9. Shader Parameter Animation System

**Enhancement:** Add support for animating shader parameters over time (tweening, easing).

**Industry Standard:** Unity's Shader Graph, Unreal's Material Editor, Godot's ShaderParam animation

**Design:**
```csharp
public struct ShaderParameterAnimationComponent
{
    public string ShaderId { get; set; }
    public string ParameterName { get; set; }
    public object StartValue { get; set; }
    public object EndValue { get; set; }
    public float Duration { get; set; }
    public float ElapsedTime { get; set; }
    public EasingFunction Easing { get; set; }
    public bool IsLooping { get; set; }
    public bool IsEnabled { get; set; }
}

public class ShaderParameterAnimationSystem : BaseSystem<World, float>
{
    public override void Update(in float deltaTime)
    {
        World.Query(in _animationQuery, (ref ShaderParameterAnimationComponent anim) =>
        {
            if (!anim.IsEnabled) return;

            anim.ElapsedTime += deltaTime;
            float t = Math.Clamp(anim.ElapsedTime / anim.Duration, 0f, 1f);
            float easedT = ApplyEasing(t, anim.Easing);

            // Interpolate parameter value
            object currentValue = Interpolate(anim.StartValue, anim.EndValue, easedT);
            
            // Update shader parameter
            // ...

            if (anim.ElapsedTime >= anim.Duration)
            {
                if (anim.IsLooping)
                {
                    anim.ElapsedTime = 0f;
                    // Swap start/end for ping-pong
                }
                else
                {
                    anim.IsEnabled = false;
                }
            }
        });
    }
}
```

**Use Cases:**
- Fade in/out effects
- Pulsing glow effects
- Color transitions (day/night cycle)
- Screen flash effects

---

### 10. Shader Quality Levels / LOD System

**Enhancement:** Support multiple shader quality levels for performance scaling.

**Industry Standard:** Unity's Quality Settings, Unreal's Scalability System

**Design:**
```csharp
public enum ShaderQualityLevel
{
    Low,      // No shaders or simple shaders
    Medium,   // Basic shaders, reduced effects
    High,     // Full shader effects
    Ultra    // All effects + advanced features
}

public struct ShaderQualityComponent
{
    public ShaderQualityLevel QualityLevel { get; set; }
}

public class ShaderQualitySystem : BaseSystem<World, float>
{
    public ShaderQualityLevel CurrentQuality { get; private set; }

    public void SetQualityLevel(ShaderQualityLevel level)
    {
        CurrentQuality = level;
        
        // Disable shaders below quality threshold
        World.Query(in _shaderQuery, (Entity entity, ref LayerShaderComponent shader) =>
        {
            var qualityReq = GetShaderQualityRequirement(shader.ShaderId);
            shader.IsEnabled = qualityReq <= level;
        });
    }
}
```

**Use Cases:**
- Mobile devices (Low quality)
- Low-end PCs (Medium quality)
- High-end PCs (High/Ultra quality)
- Performance mode toggle

---

### 11. Shader Hot-Reloading (Development)

**Enhancement:** Support reloading shaders at runtime during development without restarting the game.

**Industry Standard:** Unity's Shader Hot Reload, Unreal's Live Shader Compilation

**Design:**
```csharp
public interface IShaderService
{
    /// <summary>
    /// Reloads a shader from disk (development only).
    /// </summary>
    bool ReloadShader(string shaderId);

    /// <summary>
    /// Reloads all shaders from disk (development only).
    /// </summary>
    void ReloadAllShaders();

    /// <summary>
    /// Event fired when a shader is reloaded.
    /// </summary>
    event Action<string>? ShaderReloaded;
}

#if DEBUG
public class ShaderHotReloadSystem : BaseSystem<World, float>
{
    private readonly IShaderService _shaderService;
    private FileSystemWatcher? _shaderWatcher;

    public ShaderHotReloadSystem(World world, IShaderService shaderService) : base(world)
    {
        _shaderService = shaderService;
        
        // Watch shader files for changes
        _shaderWatcher = new FileSystemWatcher("Content/Shaders");
        _shaderWatcher.Changed += OnShaderFileChanged;
        _shaderWatcher.EnableRaisingEvents = true;
    }

    private void OnShaderFileChanged(object sender, FileSystemEventArgs e)
    {
        // Extract shader ID from filename
        string shaderId = ExtractShaderId(e.FullPath);
        _shaderService.ReloadShader(shaderId);
    }
}
#endif
```

---

### 12. Shader Stacking / Composition

**Enhancement:** Support multiple shaders per layer that stack/compose together.

**Industry Standard:** Unity's Post-Processing Stack, Unreal's Post-Process Volume

**Design:**
```csharp
public struct LayerShaderComponent
{
    // ... existing fields ...
    
    /// <summary>
    /// Render order for shader stacking (lower = applied first).
    /// Multiple shaders with same RenderOrder are applied in sequence.
    /// </summary>
    public int RenderOrder { get; set; }
    
    /// <summary>
    /// Blend mode for shader composition.
    /// </summary>
    public ShaderBlendMode BlendMode { get; set; }
}

public enum ShaderBlendMode
{
    Replace,    // Replace previous shader output
    Multiply,   // Multiply with previous shader output
    Add,        // Add to previous shader output
    Overlay,    // Overlay blend
    Screen      // Screen blend
}

// In ShaderManagerSystem
private List<Effect> GetShaderStack(ShaderLayer layer)
{
    var shaders = _shaderEntities
        .Where(e => World.Has<LayerShaderComponent>(e))
        .Select(e => World.Get<LayerShaderComponent>(e))
        .Where(s => s.Layer == layer && s.IsEnabled)
        .OrderBy(s => s.RenderOrder)
        .Select(s => _shaderService.GetShader(s.ShaderId))
        .Where(e => e != null)
        .Cast<Effect>()
        .ToList();
    
    return shaders;
}
```

**Use Cases:**
- Multiple post-processing effects (bloom + vignette + color grading)
- Layered visual effects
- Mod-provided shader combinations

---

### 13. Mod Support for Shaders

**Enhancement:** Allow mods to provide custom shaders and shader configurations.

**Industry Standard:** Modding frameworks (Skyrim, Minecraft shader mods)

**Design:**
```csharp
// In mod.json
{
    "id": "custom-shaders-mod",
    "shaders": {
        "CustomTileShader": {
            "path": "Shaders/TileLayer/CustomEffect.fx",
            "layer": "TileLayer",
            "parameters": {
                "Intensity": {
                    "type": "float",
                    "default": 1.0,
                    "min": 0.0,
                    "max": 2.0
                }
            }
        }
    }
}

// Shader loading from mods
public class ShaderService : IShaderService
{
    private readonly IModManager _modManager;

    public Effect? LoadShader(string shaderId)
    {
        // Check mods first (by priority)
        var modShader = _modManager.Registry.GetShaderDefinition(shaderId);
        if (modShader != null)
        {
            return LoadShaderFromMod(modShader);
        }

        // Fallback to base game shaders
        return LoadShaderFromContent(shaderId);
    }
}
```

---

### 14. Shader Debugging / Visualization Tools

**Enhancement:** Add debugging tools for shader development and troubleshooting.

**Industry Standard:** Unity's Frame Debugger, RenderDoc, Unreal's Shader Complexity View

**Design:**
```csharp
public class ShaderDebugSystem : BaseSystem<World, float>
{
    public bool ShowShaderInfo { get; set; }
    public bool ShowParameterValues { get; set; }
    public string? HighlightShaderId { get; set; }

    public void RenderDebugInfo(SpriteBatch spriteBatch, SpriteFont font)
    {
        if (!ShowShaderInfo) return;

        int y = 10;
        foreach (var layer in Enum.GetValues<ShaderLayer>())
        {
            var shader = GetActiveShader(layer);
            if (shader != null)
            {
                var color = HighlightShaderId == shader.Name ? Color.Yellow : Color.White;
                spriteBatch.DrawString(font, $"{layer}: {shader.Name}", new Vector2(10, y), color);
                y += 20;

                if (ShowParameterValues)
                {
                    foreach (var param in shader.Parameters)
                    {
                        spriteBatch.DrawString(font, $"  {param.Name}: {GetParameterValue(param)}", 
                            new Vector2(30, y), Color.Gray);
                        y += 15;
                    }
                }
            }
        }
    }
}
```

---

### 15. Shader Performance Profiling

**Enhancement:** Add performance profiling for shader execution.

**Design:**
```csharp
public class ShaderProfiler
{
    private readonly Dictionary<string, ShaderProfile> _profiles = new();

    public void BeginShader(string shaderId)
    {
        // Start timing
    }

    public void EndShader(string shaderId)
    {
        // End timing, record
    }

    public ShaderProfile GetProfile(string shaderId)
    {
        return _profiles.GetValueOrDefault(shaderId);
    }
}

public struct ShaderProfile
{
    public int CallCount { get; set; }
    public TimeSpan TotalTime { get; set; }
    public TimeSpan AverageTime => TotalTime / CallCount;
    public TimeSpan MinTime { get; set; }
    public TimeSpan MaxTime { get; set; }
}
```

---

## 📋 SUMMARY OF RECOMMENDATIONS

### Critical Fixes (Must Have)
1. ✅ Fix Update vs Render timing mismatch
2. ✅ Add event system integration (`LayerShaderChangedEvent`, `ShaderParameterChangedEvent`)
3. ✅ Add render target lifecycle management
4. ✅ Add shader parameter validation

### Important Improvements (Should Have)
5. ✅ Type-safe shader parameters (strongly-typed classes)
6. ✅ Component lifecycle handling (add/remove events)
7. ✅ Consistent dependency injection pattern

### Nice-to-Have Enhancements (Future)
8. ✅ Shader parameter animation system
9. ✅ Shader quality levels / LOD
10. ✅ Shader hot-reloading (development)
11. ✅ Shader stacking / composition
12. ✅ Mod support for shaders
13. ✅ Shader debugging tools
14. ✅ Shader performance profiling

---

## 🎯 PRIORITY RANKING

**P0 (Critical - Block Release):**
- Update vs Render timing fix
- Render target management
- Parameter validation

**P1 (Important - Should Fix Soon):**
- Event system integration
- Type-safe parameters
- Component lifecycle handling

**P2 (Enhancement - Future):**
- Shader animation system
- Quality levels
- Hot-reloading
- Shader stacking
- Mod support
- Debugging tools

---

## 📝 NEXT STEPS

1. **Update Design Document** with fixes for critical issues
2. **Create Implementation Plan** for P0 and P1 items
3. **Design Event System Integration** - define event structures
4. **Design Render Target Manager** - lifecycle and memory management
5. **Create Shader Parameter Validator** - type checking and validation
6. **Plan Enhancement Phases** - prioritize P2 items for future releases

