# Camera and Rendering Coordination Design

**Date:** 2025-01-27  
**Status:** Design (Updated with fixes)  
**Related:** `arch-relationships-opportunities.md`, `elevation-based-rendering-design.md`, `camera-rendering-coordination-design-analysis.md`

**Note:** This design has been updated to address architecture, Arch ECS, and .cursorrules compliance issues identified in the analysis document.

---

## Overview

This design addresses two major architectural issues identified in the camera and rendering systems:

1. **Weak Camera-Scene Coupling**: Scenes store camera entity IDs (`int?`) instead of using ECS relationships, leading to inefficient lookups and duplicated code.

2. **Fragmented Rendering Pipeline**: Multiple systems independently manage rendering state (viewport, SpriteBatch, render targets), causing coordination issues and duplicated logic.

This refactor will:
- Replace `SceneComponent.CameraEntityId` with ECS relationships
- Centralize camera queries in `CameraService`
- Create a `SceneRenderingCoordinator` for unified rendering state management
- Unify the rendering pipeline with shared batch management

---

## Goals

### Primary Goals
1. **Use ECS relationships** for camera-scene associations (replacing entity ID storage)
2. **Centralize rendering coordination** to eliminate fragmented state management
3. **Reduce code duplication** in camera lookups and rendering setup
4. **Improve performance** by eliminating redundant queries and state changes

### Secondary Goals
5. **Maintain clear separation** between scene coordination and rendering coordination
6. **Preserve existing functionality** while improving architecture
7. **Follow ECS best practices** (data in components, relationships for associations, systems for behavior)

---

## Current Architecture Issues

### Issue 1: Weak Camera-Scene Coupling

**Current Pattern:**
```csharp
// SceneComponent stores camera entity ID
public struct SceneComponent
{
    public int? CameraEntityId { get; set; } // Weak reference
    public SceneCameraMode CameraMode { get; set; }
}

// Systems manually query cameras by ID
World.Query(in _cameraQuery, (Entity entity, ref CameraComponent cam) => {
    if (entity.Id == cameraEntityId) {
        camera = cam; // Found it!
    }
});
```

**Problems:**
- No ECS relationship - just a stored ID
- Inefficient: full world query filtered by ID
- Duplicated lookup logic in 4+ systems
- No compile-time safety
- No automatic cleanup when camera entity is destroyed

**Affected Systems:**
- `GameSceneSystem.RenderScene()`
- `MapPopupSceneSystem.RenderScene()`
- `MessageBoxSceneSystem.RenderScene()`
- `UIRenderSystem.RenderScene()`
- `SceneCameraHelper.GetCameraForScene()`

### Issue 2: Fragmented Rendering Pipeline

**Current Pattern:**
Multiple systems independently manage rendering state:

1. **`SceneSystem.Render()`** - Coordinates scene rendering (dispatcher only)
2. **`GameSceneSystem.RenderScene()`** - Manages render targets, viewports, calls `ElevationRendererSystem`
3. **`ElevationRendererSystem.Render()`** - Manages its own `SpriteBatch.Begin/End` cycles
4. **`MapPopupSceneSystem.RenderScene()`** - Manages viewport and `SpriteBatch`
5. **`UIRenderSystem.RenderScene()`** - Manages viewport and `SpriteBatch`
6. **`ShaderRendererSystem.ApplyShaderStack()`** - Manages `SpriteBatch` for shader passes

**Problems:**
- Multiple `SpriteBatch.Begin/End` cycles per scene
- Viewport management scattered across systems
- Transform matrix calculation duplicated
- Render target management fragmented
- No centralized rendering coordinator

---

## Proposed Architecture

### Phase 1: Camera-Scene Relationships

#### 1.1 Create Camera Relationship Types

**Location:** `MonoBall.Core/Scenes/Relationships/`

```csharp
namespace MonoBall.Core.Scenes.Relationships;

/// <summary>
///     Relationship type for scene → camera association.
///     Used when SceneComponent.CameraMode == SceneCameraMode.SceneCamera.
///     Automatically cleaned up when scene or camera entity is destroyed.
/// </summary>
/// <remarks>
///     This relationship follows the same pattern as <see cref="MonoBall.Core.UI.Relationships.OwnsUIElement"/>.
///     Scenes should only have one camera relationship (one-to-one).
/// </remarks>
public struct UsesCamera
{
    // Marker relationship - no data needed
    // Can be extended with metadata if needed (e.g., priority, viewport override)
}
```

**Pattern:** Follows existing `OwnsUIElement` relationship pattern.

#### 1.2 Update SceneComponent

**Remove:**
```csharp
public int? CameraEntityId { get; set; } // REMOVE
```

**Keep:**
```csharp
public SceneCameraMode CameraMode { get; set; } // KEEP
```

**Rationale:**
- `CameraMode` determines lookup strategy (GameCamera, SceneCamera, ScreenCamera)
- Relationship replaces `CameraEntityId` for `SceneCamera` mode
- `GameCamera` mode uses `CameraService.GetActiveCamera()` (no relationship needed)
- `ScreenCamera` mode doesn't use a camera (no relationship needed)

#### 1.3 Update CameraService

**Add Methods:**
```csharp
public interface ICameraService
{
    // Existing
    CameraComponent? GetActiveCamera();
    
    // NEW: Get camera for a scene entity
    CameraComponent? GetCameraForScene(Entity sceneEntity);
    
    // NEW: Get camera entity for a scene (for relationship queries)
    Entity? GetCameraEntityForScene(Entity sceneEntity);
}
```

**Implementation:**
```csharp
using System.Linq;
using Arch.Core;
using MonoBall.Core.ECS.Components;
using MonoBall.Core.Scenes.Relationships;
using Serilog;

namespace MonoBall.Core.ECS.Services;

public class CameraService : ICameraService
{
    private static readonly QueryDescription CameraQueryDescription =
        new QueryDescription().WithAll<CameraComponent>();
    
    private readonly World _world;
    private readonly ILogger _logger;
    
    /// <summary>
    ///     Initializes a new instance of the CameraService.
    /// </summary>
    /// <param name="world">The ECS world.</param>
    /// <param name="logger">The logger for logging operations.</param>
    public CameraService(World world, ILogger logger)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }
    
    /// <summary>
    ///     Gets the camera component for a scene entity based on its CameraMode.
    /// </summary>
    /// <param name="sceneEntity">The scene entity.</param>
    /// <returns>The camera component, or null if not found or scene doesn't have SceneComponent.</returns>
    public CameraComponent? GetCameraForScene(Entity sceneEntity)
    {
        if (!_world.IsAlive(sceneEntity))
            return null;
        
        if (!_world.Has<SceneComponent>(sceneEntity))
            return null;
        
        ref var scene = ref _world.Get<SceneComponent>(sceneEntity);
        
        switch (scene.CameraMode)
        {
            case SceneCameraMode.GameCamera:
                return GetActiveCamera();
            
            case SceneCameraMode.SceneCamera:
                // Query via relationship
                var cameraEntity = GetCameraEntityForScene(sceneEntity);
                if (!cameraEntity.HasValue || !_world.IsAlive(cameraEntity.Value))
                    return null;
                
                if (!_world.Has<CameraComponent>(cameraEntity.Value))
                    return null;
                
                return _world.Get<CameraComponent>(cameraEntity.Value);
            
            case SceneCameraMode.ScreenCamera:
                return null; // ScreenCamera doesn't use a camera component
            
            default:
                return null;
        }
    }
    
    /// <summary>
    ///     Gets the camera entity for a scene via relationship query.
    /// </summary>
    /// <param name="sceneEntity">The scene entity.</param>
    /// <returns>The camera entity, or null if not found or relationship doesn't exist.</returns>
    public Entity? GetCameraEntityForScene(Entity sceneEntity)
    {
        if (!_world.IsAlive(sceneEntity))
            return null;
        
        try
        {
            var relationships = _world.GetRelationships<UsesCamera>(sceneEntity);
            if (relationships == null || relationships.Count == 0)
                return null;
            
            // Return first camera entity (scenes should only have one camera)
            var cameraEntity = relationships.Keys.First();
            return _world.IsAlive(cameraEntity) ? cameraEntity : null;
        }
        catch (InvalidOperationException)
        {
            // Relationship query failed - skip this scene
            // InvalidOperationException is thrown by Arch.Extended when relationship queries fail
            return null;
        }
        catch (ArgumentException)
        {
            // Invalid entity or relationship type
            return null;
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to query camera relationship for scene {SceneId}", sceneEntity.Id);
            return null;
        }
    }
}
```

#### 1.4 Update SceneSystem.CreateScene()

**Before:**
```csharp
// Validate CameraEntityId if SceneCamera mode is specified
if (sceneComponent.CameraMode == SceneCameraMode.SceneCamera)
{
    if (!sceneComponent.CameraEntityId.HasValue)
        throw new ArgumentException(...);
    
    // Verify camera entity exists
    var cameraEntityId = sceneComponent.CameraEntityId.Value;
    var cameraFound = false;
    World.Query(in _cameraQueryDescription, (Entity entity, ref CameraComponent _) => {
        if (entity.Id == cameraEntityId)
            cameraFound = true;
    });
    
    if (!cameraFound)
        throw new ArgumentException(...);
}
```

**After:**
```csharp
// Validate and create relationship if SceneCamera mode is specified
if (sceneComponent.CameraMode == SceneCameraMode.SceneCamera)
{
    // CameraEntityId parameter is now required (not stored in component)
    if (!cameraEntity.HasValue)
        throw new ArgumentException(
            "CameraEntity is required when CameraMode is SceneCamera.",
            nameof(cameraEntity)
        );
    
    // Verify camera entity exists and has CameraComponent
    if (!World.IsAlive(cameraEntity.Value))
        throw new ArgumentException(
            $"Camera entity {cameraEntity.Value.Id} does not exist.",
            nameof(cameraEntity)
        );
    
    if (!World.Has<CameraComponent>(cameraEntity.Value))
        throw new ArgumentException(
            $"Camera entity {cameraEntity.Value.Id} does not have CameraComponent.",
            nameof(cameraEntity)
        );
    
    // Validate entities before relationship operation
    if (!World.IsAlive(sceneEntity))
        throw new ArgumentException($"Scene entity {sceneEntity.Id} is not alive.", nameof(sceneEntity));
    
    if (!World.IsAlive(cameraEntity.Value))
        throw new ArgumentException($"Camera entity {cameraEntity.Value.Id} is not alive.", nameof(cameraEntity));
    
    // Create relationship (automatically cleaned up when scene or camera is destroyed)
    try
    {
        World.AddRelationship(sceneEntity, cameraEntity.Value, new UsesCamera());
    }
    catch (InvalidOperationException ex)
    {
        // Relationship addition failed - log and cleanup
        // InvalidOperationException is thrown by Arch.Extended when relationship operations fail
        _logger.Error(ex, "Failed to create camera relationship for scene {SceneId}", sceneEntity.Id);
        throw;
    }
    catch (ArgumentException ex)
    {
        // Invalid entity or relationship type
        _logger.Error(ex, "Invalid arguments for camera relationship: {Message}", ex.Message);
        throw;
    }
}
```

**Method Signature Change:**
```csharp
// BEFORE
public Entity CreateScene(SceneComponent sceneComponent, params object[] additionalComponents)

// AFTER
public Entity CreateScene(
    SceneComponent sceneComponent,
    Entity? cameraEntity = null, // Required if CameraMode == SceneCamera
    params object[] additionalComponents
)
```

#### 1.5 Update SceneCameraHelper

**Simplify:**
```csharp
public static class SceneCameraHelper
{
    /// <summary>
    ///     Gets the camera component for a scene entity based on its CameraMode.
    ///     Uses CameraService for centralized camera queries.
    /// </summary>
    public static CameraComponent? GetCameraForScene(
        World world,
        Entity sceneEntity,
        ICameraService cameraService
    )
    {
        // Delegate to CameraService (no need for cameraQuery parameter anymore)
        return cameraService.GetCameraForScene(sceneEntity);
    }
    
    // Overload with ref SceneComponent (for systems that already have component)
    public static CameraComponent? GetCameraForScene(
        World world,
        ref SceneComponent scene,
        ICameraService cameraService
    )
    {
        // For GameCamera mode, use GetActiveCamera()
        if (scene.CameraMode == SceneCameraMode.GameCamera)
            return cameraService.GetActiveCamera();
        
        // For SceneCamera mode, need scene entity to query relationship
        // Caller should use the entity-based overload
        if (scene.CameraMode == SceneCameraMode.SceneCamera)
            return null; // Cannot resolve without entity
        
        // ScreenCamera doesn't use a camera
        return null;
    }
}
```

**Rationale:**
- Removes need for `QueryDescription` parameter (camera queries centralized in `CameraService`)
- Simplifies API - systems just call `cameraService.GetCameraForScene(sceneEntity)`

#### 1.6 Update All Scene Systems

**Remove Manual Camera Queries:**

**Before (GameSceneSystem):**
```csharp
case SceneCameraMode.SceneCamera:
    if (scene.CameraEntityId.HasValue)
    {
        var cameraEntityId = scene.CameraEntityId.Value;
        var foundCamera = false;
        World.Query(in _cameraQuery, (Entity entity, ref CameraComponent cam) => {
            if (entity.Id == cameraEntityId)
            {
                camera = cam;
                foundCamera = true;
            }
        });
        // ... error handling
    }
```

**After (GameSceneSystem):**
```csharp
// Use CameraService (no manual queries needed)
var camera = _cameraService.GetCameraForScene(sceneEntity);
if (!camera.HasValue)
{
    _logger.Warning("GameScene '{SceneId}' requires camera but none was found.", scene.SceneId);
    return;
}
```

**Remove `_cameraQuery` field:**
- No longer needed - `CameraService` handles all queries

**Update Constructor:**
```csharp
// Remove cameraQuery parameter, add ICameraService
public GameSceneSystem(
    World world,
    GraphicsDevice graphicsDevice,
    SpriteBatch spriteBatch,
    ElevationRendererSystem elevationRendererSystem,
    ICameraService cameraService, // NEW
    // ... other params
)
{
    _cameraService = cameraService ?? throw new ArgumentNullException(nameof(cameraService));
    // Remove: _cameraQuery = new QueryDescription()...
}
```

**Affected Systems:**
- `GameSceneSystem`
- `MapPopupSceneSystem`
- `MessageBoxSceneSystem`
- `UIRenderSystem`

---

### Phase 2: Rendering Coordination

#### 2.1 Create SceneRenderingCoordinator

**Location:** `MonoBall.Core/Scenes/Systems/SceneRenderingCoordinator.cs`

**Note:** While this is a coordinator/service rather than a system that processes entities, it's placed in `Systems/` because it's scene-specific coordination logic. Alternatively, it could be placed in `Scenes/Services/` if preferred.

**Purpose:**
- Manages rendering state (viewport, render targets, SpriteBatch lifecycle) per scene
- Provides unified rendering pipeline
- Eliminates fragmented state management

**Interface:**
```csharp
namespace MonoBall.Core.Scenes.Systems;

/// <summary>
///     Coordinates rendering state for scene rendering.
///     Manages viewport, render targets, and SpriteBatch lifecycle.
/// </summary>
public interface ISceneRenderingCoordinator
{
    /// <summary>
    ///     Prepares rendering state for a scene.
    ///     Sets viewport, render target, and begins SpriteBatch with correct transform.
    /// </summary>
    /// <param name="sceneEntity">The scene entity to prepare.</param>
    /// <param name="camera">The camera component for this scene (null for ScreenCamera).</param>
    /// <returns>Render context with prepared state.</returns>
    /// <exception cref="InvalidOperationException">
    ///     Thrown when scene entity does not have SceneComponent.
    /// </exception>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when sceneEntity is invalid or required dependencies are null.
    /// </exception>
    IRenderContext PrepareScene(Entity sceneEntity, CameraComponent? camera);
    
    /// <summary>
    ///     Finishes rendering for a scene.
    ///     Ends SpriteBatch, restores viewport/render target, applies post-processing.
    /// </summary>
    /// <param name="context">The render context from PrepareScene.</param>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when context is null.
    /// </exception>
    void FinishScene(IRenderContext context);
}
```

**Implementation:**
```csharp
using System;
using System.Collections.Generic;
using Arch.Core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoBall.Core.ECS.Components;
using MonoBall.Core.ECS.Services;
using MonoBall.Core.Rendering;
using MonoBall.Core.Scenes.Components;
using Serilog;

namespace MonoBall.Core.Scenes.Systems;

/// <summary>
///     Coordinates rendering state for scene rendering.
///     Manages viewport, render targets, and SpriteBatch lifecycle per scene.
/// </summary>
public class SceneRenderingCoordinator : ISceneRenderingCoordinator
{
    private readonly World _world;
    private readonly GraphicsDevice _graphicsDevice;
    private readonly SpriteBatch _spriteBatch;
    private readonly ICameraService _cameraService;
    private readonly IShaderManager? _shaderManager;
    private readonly IShaderRenderer? _shaderRenderer;
    private readonly IRenderTargetManager? _renderTargetManager;
    private readonly ILogger _logger;
    
    // Cached collections for shader stacks (reusable to avoid allocations)
    private readonly List<(Effect effect, ShaderBlendMode blendMode, Entity entity)> _shaderStackCache = new();
    private IReadOnlyList<(Effect effect, ShaderBlendMode blendMode, Entity entity)>? _currentShaderStack;
    
    /// <summary>
    ///     Initializes a new instance of the SceneRenderingCoordinator.
    /// </summary>
    /// <param name="world">The ECS world.</param>
    /// <param name="graphicsDevice">The graphics device for rendering.</param>
    /// <param name="spriteBatch">The sprite batch for rendering.</param>
    /// <param name="cameraService">The camera service for camera queries.</param>
    /// <param name="shaderManager">The shader manager system (optional).</param>
    /// <param name="shaderRenderer">The shader renderer system (optional).</param>
    /// <param name="renderTargetManager">The render target manager (optional).</param>
    /// <param name="logger">The logger for logging operations.</param>
    public SceneRenderingCoordinator(
        World world,
        GraphicsDevice graphicsDevice,
        SpriteBatch spriteBatch,
        ICameraService cameraService,
        IShaderManager? shaderManager = null,
        IShaderRenderer? shaderRenderer = null,
        IRenderTargetManager? renderTargetManager = null,
        ILogger? logger = null
    )
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _graphicsDevice = graphicsDevice ?? throw new ArgumentNullException(nameof(graphicsDevice));
        _spriteBatch = spriteBatch ?? throw new ArgumentNullException(nameof(spriteBatch));
        _cameraService = cameraService ?? throw new ArgumentNullException(nameof(cameraService));
        _shaderManager = shaderManager;
        _shaderRenderer = shaderRenderer;
        _renderTargetManager = renderTargetManager;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }
    
    /// <summary>
    ///     Prepares rendering state for a scene.
    ///     Sets viewport, render target, and begins SpriteBatch with correct transform.
    /// </summary>
    /// <param name="sceneEntity">The scene entity to prepare.</param>
    /// <param name="camera">The camera component for this scene (null for ScreenCamera).</param>
    /// <returns>Render context with prepared state.</returns>
    /// <exception cref="InvalidOperationException">
    ///     Thrown when scene entity does not have SceneComponent.
    /// </exception>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when sceneEntity is invalid or required dependencies are null.
    /// </exception>
    public IRenderContext PrepareScene(Entity sceneEntity, CameraComponent? camera)
    {
        if (!_world.IsAlive(sceneEntity))
            throw new ArgumentException($"Scene entity {sceneEntity.Id} is not alive.", nameof(sceneEntity));
        
        if (!_world.Has<SceneComponent>(sceneEntity))
            throw new InvalidOperationException($"Scene entity {sceneEntity.Id} missing SceneComponent.");
        
        ref var scene = ref _world.Get<SceneComponent>(sceneEntity);
        
        // Save original state
        var savedViewport = _graphicsDevice.Viewport;
        var savedRenderTargets = _graphicsDevice.GetRenderTargets();
        var savedRenderTarget = savedRenderTargets.Length > 0 
            ? savedRenderTargets[0].RenderTarget as RenderTarget2D 
            : null;
        
        // Determine render target (for post-processing)
        RenderTarget2D? renderTarget = null;
        var hasPostProcessing = false;
        
        if (_shaderManager != null)
        {
            _shaderStackCache.Clear();
            _currentShaderStack = null;
            var shaderStack = _shaderManager.GetCombinedLayerShaderStack(sceneEntity);
            if (shaderStack != null && shaderStack.Count > 0)
            {
                _shaderStackCache.AddRange(shaderStack);
                _currentShaderStack = _shaderStackCache; // Direct reference, no allocation
                hasPostProcessing = true;
            }
        }
        
        if (hasPostProcessing && _renderTargetManager != null)
        {
            renderTarget = _renderTargetManager.GetOrCreateRenderTarget();
            if (renderTarget == null)
            {
                _logger.Warning("Failed to create render target for post-processing. Rendering directly to back buffer.");
            }
        }
        
        // Set render target
        if (renderTarget != null)
        {
            _graphicsDevice.SetRenderTarget(renderTarget);
            _graphicsDevice.Clear(Color.Transparent);
        }
        
        // Set viewport based on camera
        if (camera.HasValue && camera.Value.VirtualViewport != Rectangle.Empty)
        {
            _graphicsDevice.Viewport = new Viewport(camera.Value.VirtualViewport);
        }
        
        // Calculate transform matrix
        Matrix transform = Matrix.Identity;
        if (camera.HasValue)
        {
            transform = camera.Value.GetTransformMatrix();
        }
        
        // Begin SpriteBatch
        _spriteBatch.Begin(
            SpriteSortMode.Deferred,
            BlendState.AlphaBlend,
            SamplerState.PointClamp,
            DepthStencilState.None,
            RasterizerState.CullCounterClockwise,
            null,
            transform
        );
        
        // Create render context
        return new RenderContext
        {
            SceneEntity = sceneEntity,
            Camera = camera,
            SpriteBatch = _spriteBatch,
            SavedViewport = savedViewport,
            SavedRenderTarget = savedRenderTarget,
            RenderTarget = renderTarget,
            HasPostProcessing = hasPostProcessing,
            ShaderStack = _currentShaderStack // Direct reference, no allocation
        };
    }
    
    /// <summary>
    ///     Finishes rendering for a scene.
    ///     Ends SpriteBatch, restores viewport/render target, applies post-processing.
    /// </summary>
    /// <param name="context">The render context from PrepareScene.</param>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when context is null.
    /// </exception>
    public void FinishScene(IRenderContext context)
    {
        if (context == null)
            throw new ArgumentNullException(nameof(context));
        
        var renderContext = (RenderContext)context;
        
        // End SpriteBatch
        _spriteBatch.End();
        
        // Apply post-processing if needed
        if (renderContext.HasPostProcessing 
            && renderContext.RenderTarget != null 
            && renderContext.ShaderStack != null 
            && _shaderRenderer != null)
        {
            // Restore render target before applying shaders
            _graphicsDevice.SetRenderTarget(renderContext.SavedRenderTarget);
            if (renderContext.Camera.HasValue 
                && renderContext.Camera.Value.VirtualViewport != Rectangle.Empty)
            {
                _graphicsDevice.Viewport = new Viewport(renderContext.Camera.Value.VirtualViewport);
            }
            
            // Update shader parameters
            _shaderManager?.ForceUpdateCombinedLayerParameters();
            
            // Apply shader stack
            _shaderRenderer.ApplyShaderStack(
                renderContext.RenderTarget,
                null, // Render to back buffer
                renderContext.ShaderStack,
                _spriteBatch,
                _graphicsDevice,
                _renderTargetManager
            );
        }
        
        // Restore viewport
        _graphicsDevice.Viewport = renderContext.SavedViewport;
        
        // Restore render target (if not already restored)
        if (!renderContext.HasPostProcessing)
        {
            _graphicsDevice.SetRenderTarget(renderContext.SavedRenderTarget);
        }
    }
}
```

#### 2.2 Create IRenderContext Interface

**Location:** `MonoBall.Core/Scenes/Systems/IRenderContext.cs`

```csharp
namespace MonoBall.Core.Scenes.Systems;

/// <summary>
///     Render context provided by SceneRenderingCoordinator.
///     Contains prepared rendering state for a scene.
/// </summary>
public interface IRenderContext
{
    /// <summary>
    ///     The scene entity being rendered.
    /// </summary>
    Entity SceneEntity { get; }
    
    /// <summary>
    ///     The camera component for this scene (null for ScreenCamera).
    /// </summary>
    CameraComponent? Camera { get; }
    
    /// <summary>
    ///     The SpriteBatch (already begun, ready for drawing).
    /// </summary>
    SpriteBatch SpriteBatch { get; }
}
```

**Internal Implementation:**
```csharp
/// <summary>
///     Internal render context implementation.
///     Immutable struct to prevent accidental state modification.
/// </summary>
internal readonly struct RenderContext : IRenderContext
{
    /// <summary>
    ///     The scene entity being rendered.
    /// </summary>
    public Entity SceneEntity { get; init; }
    
    /// <summary>
    ///     The camera component for this scene (null for ScreenCamera).
    /// </summary>
    public CameraComponent? Camera { get; init; }
    
    /// <summary>
    ///     The SpriteBatch (already begun, ready for drawing).
    /// </summary>
    public SpriteBatch SpriteBatch { get; init; }
    
    // Internal state for coordinator
    internal Viewport SavedViewport { get; init; }
    internal RenderTarget2D? SavedRenderTarget { get; init; }
    internal RenderTarget2D? RenderTarget { get; init; }
    internal bool HasPostProcessing { get; init; }
    internal IReadOnlyList<(Effect effect, ShaderBlendMode blendMode, Entity entity)>? ShaderStack { get; init; }
}
```

#### 2.3 Update SceneSystem.Render()

**Before:**
```csharp
public void Render(GameTime gameTime)
{
    IterateScenesReverse((sceneEntity, sceneComponent) => {
        if (!sceneComponent.IsActive)
            return true;
        
        _shaderManagerSystem?.UpdateShaderState(sceneEntity);
        FindSceneSystem(sceneEntity)?.RenderScene(sceneEntity, gameTime);
        
        if (sceneComponent.BlocksDraw)
            return false;
        
        return true;
    });
}
```

**After:**
```csharp
public void Render(GameTime gameTime)
{
    IterateScenesReverse((sceneEntity, sceneComponent) => {
        if (!sceneComponent.IsActive)
            return true;
        
        // Get camera for scene
        var camera = _cameraService.GetCameraForScene(sceneEntity);
        
        // Prepare rendering state
        var renderContext = _renderingCoordinator.PrepareScene(sceneEntity, camera);
        
        try
        {
            // Update shader state before rendering
            _shaderManagerSystem?.UpdateShaderState(sceneEntity);
            
            // Render scene content (systems render into shared SpriteBatch)
            FindSceneSystem(sceneEntity)?.RenderScene(sceneEntity, gameTime, renderContext);
            
            // Finish rendering (applies post-processing, restores state)
            _renderingCoordinator.FinishScene(renderContext);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error rendering scene {SceneId}", sceneComponent.SceneId);
            // Ensure state is restored even on error
            _renderingCoordinator.FinishScene(renderContext);
            throw;
        }
        
        if (sceneComponent.BlocksDraw)
            return false;
        
        return true;
    });
}
```

#### 2.4 Update ISceneSystem Interface

**Before:**
```csharp
public interface ISceneSystem
{
    void Update(Entity sceneEntity, float deltaTime);
    void RenderScene(Entity sceneEntity, GameTime gameTime);
    void ProcessInternal(float deltaTime);
}
```

**After:**
```csharp
public interface ISceneSystem
{
    void Update(Entity sceneEntity, float deltaTime);
    void RenderScene(Entity sceneEntity, GameTime gameTime, IRenderContext renderContext);
    void ProcessInternal(float deltaTime);
}
```

**Rationale:**
- `RenderScene` receives `IRenderContext` with prepared `SpriteBatch`
- Systems render content only - no state management

#### 2.5 Update Scene Systems

**GameSceneSystem.RenderScene() - Before:**
```csharp
public void RenderScene(Entity sceneEntity, GameTime gameTime)
{
    // Get camera (manual query)
    var camera = _cameraService.GetCameraForScene(sceneEntity);
    if (!camera.HasValue)
        return;
    
    // Save viewport
    var savedViewport = _graphicsDevice.Viewport;
    
    // Set viewport
    if (camera.Value.VirtualViewport != Rectangle.Empty)
        _graphicsDevice.Viewport = new Viewport(camera.Value.VirtualViewport);
    
    // Manage render target
    RenderTarget2D? renderTarget = null;
    // ... complex render target logic ...
    
    try
    {
        // Begin SpriteBatch
        _spriteBatch.Begin(...);
        
        // Render
        _elevationRendererSystem.Render(gameTime, sceneEntity);
        
        // Apply post-processing
        // ... complex shader logic ...
    }
    finally
    {
        _spriteBatch.End();
        _graphicsDevice.Viewport = savedViewport;
        // ... restore render target ...
    }
}
```

**GameSceneSystem.RenderScene() - After:**
```csharp
public void RenderScene(Entity sceneEntity, GameTime gameTime, IRenderContext renderContext)
{
    // Verify this is a game scene
    if (!World.Has<GameSceneComponent>(sceneEntity))
        return;
    
    ref var scene = ref World.Get<SceneComponent>(sceneEntity);
    if (!scene.IsActive)
        return;
    
    // Render content (SpriteBatch already begun, viewport already set)
    // ElevationRendererSystem receives renderContext, doesn't manage its own batch
    _elevationRendererSystem.Render(gameTime, sceneEntity, renderContext);
    
    // No state management needed - coordinator handles it
}
```

**UIRenderSystem.RenderScene() - Before:**
```csharp
public void RenderScene(Entity sceneEntity, GameTime gameTime)
{
    // Get camera
    var camera = SceneCameraHelper.GetCameraForScene(World, ref scene, _cameraService, _cameraQuery);
    
    // Save viewport
    var savedViewport = _graphicsDevice.Viewport;
    
    try
    {
        // Set viewport
        if (camera.Value.VirtualViewport != Rectangle.Empty)
            _graphicsDevice.Viewport = new Viewport(camera.Value.VirtualViewport);
        
        // Begin SpriteBatch
        _spriteBatch.Begin(..., Matrix.Identity);
        
        // Render UI elements
        // ...
        
        _spriteBatch.End();
    }
    finally
    {
        _graphicsDevice.Viewport = savedViewport;
    }
}
```

**UIRenderSystem.RenderScene() - After:**
```csharp
public void RenderScene(Entity sceneEntity, GameTime gameTime, IRenderContext renderContext)
{
    // Camera and viewport already set by coordinator
    // SpriteBatch already begun
    
    // Render UI elements (use renderContext.SpriteBatch)
    _renderList.Clear();
    
    // Collect UI elements via relationships
    var relationships = World.GetRelationships<OwnsUIElement>(sceneEntity);
    foreach (var kvp in relationships)
    {
        // ... collect UI elements ...
    }
    
    // Render (SpriteBatch already active)
    foreach (var (uiElement, ui, zOrder) in _renderList)
    {
        // Render UI element
    }
    
    // No End() needed - coordinator handles it
}
```

#### 2.6 Update ElevationRendererSystem

**Current:**
```csharp
public void Render(GameTime gameTime, Entity? sceneEntity = null)
{
    // Gets camera itself
    var activeCamera = _cameraService.GetActiveCamera();
    
    // Manages its own SpriteBatch.Begin/End cycles
    _spriteBatch.Begin(...);
    // ... render items ...
    _spriteBatch.End();
}
```

**After:**
```csharp
public void Render(GameTime gameTime, Entity sceneEntity, IRenderContext renderContext)
{
    // Use camera from renderContext (already validated)
    var camera = renderContext.Camera;
    if (!camera.HasValue)
        return;
    
    // Use SpriteBatch from renderContext (already begun)
    // No Begin/End - render into shared batch
    
    // Collect renderables
    _renderableItems.Clear();
    CollectTileChunks(visiblePixelBounds);
    CollectSprites(visiblePixelBounds);
    
    // Sort
    _renderableItems.Sort(ElevationComparer);
    
    // Render items (use renderContext.SpriteBatch, no Begin/End)
    foreach (var item in _renderableItems)
    {
        item.Render(World, renderContext.SpriteBatch, _tileChunkRenderer, _spriteRenderer);
    }
    
    // No End() - coordinator handles it
}
```

**Rationale:**
- `ElevationRendererSystem` no longer manages `SpriteBatch` lifecycle
- Renders into shared batch provided by coordinator
- Camera comes from `renderContext` (already validated)

---

## Migration Plan

### Phase 1: Camera Relationships (Breaking Changes)

1. **Create relationship type**
   - `MonoBall.Core/Scenes/Relationships/UsesCamera.cs`

2. **Update CameraService**
   - Add `GetCameraForScene()` method
   - Add `GetCameraEntityForScene()` method
   - Update interface

3. **Update SceneComponent**
   - Remove `CameraEntityId` property
   - Update XML docs

4. **Update SceneSystem.CreateScene()**
   - Change signature to accept `Entity? cameraEntity` parameter
   - Create relationship instead of storing ID
   - Update validation logic

5. **Update SceneCameraHelper**
   - Simplify to use `CameraService`
   - Remove `QueryDescription` parameter

6. **Update all scene systems**
   - Remove `_cameraQuery` fields
   - Remove manual camera queries
   - Use `CameraService.GetCameraForScene()`
   - Update constructors to require `ICameraService`

7. **Update all call sites**
   - `SceneSystem.CreateScene()` calls need `cameraEntity` parameter
   - `GameSceneHelper.CreateGameScene()` needs update
   - All scene creation code needs update

**Breaking Changes:**
- `SceneComponent.CameraEntityId` removed
- `SceneSystem.CreateScene()` signature changed
- Scene systems require `ICameraService` in constructor

### Phase 2: Rendering Coordination (Breaking Changes)

1. **Create rendering coordinator**
   - `SceneRenderingCoordinator` class
   - `IRenderContext` interface
   - `RenderContext` struct

2. **Update SceneSystem**
   - Add `ISceneRenderingCoordinator` dependency
   - Update `Render()` to use coordinator
   - Pass `IRenderContext` to scene systems

3. **Update ISceneSystem interface**
   - Change `RenderScene()` signature to accept `IRenderContext`

4. **Update all scene systems**
   - Remove viewport management
   - Remove `SpriteBatch.Begin/End` calls
   - Remove render target management
   - Use `renderContext.SpriteBatch` for rendering
   - Simplify `RenderScene()` methods

5. **Update ElevationRendererSystem**
   - Change `Render()` signature to accept `IRenderContext`
   - Remove `SpriteBatch` management
   - Remove camera query (use `renderContext.Camera`)

6. **Update SystemManager**
   - Create `SceneRenderingCoordinator` instance
   - Pass to `SceneSystem` constructor

**Breaking Changes:**
- `ISceneSystem.RenderScene()` signature changed
- `ElevationRendererSystem.Render()` signature changed
- Scene systems no longer manage rendering state

---

## Benefits

### Architecture Improvements

1. **ECS Relationships**
   - Proper ECS pattern for entity associations
   - Automatic cleanup when entities destroyed
   - Type-safe relationships
   - Efficient relationship queries

2. **Centralized Camera Queries**
   - Single source of truth (`CameraService`)
   - No duplicated lookup logic
   - Consistent camera resolution across systems

3. **Unified Rendering Pipeline**
   - Single `SpriteBatch` lifecycle per scene
   - Centralized viewport management
   - Centralized render target management
   - Consistent rendering state

4. **Separation of Concerns**
   - Scene systems render content only
   - Coordinator manages state
   - Clear responsibilities

### Performance Improvements

1. **Efficient Camera Queries**
   - Relationship queries instead of full world scans
   - Cached queries in `CameraService`

2. **Reduced State Changes**
   - Single viewport change per scene
   - Single `SpriteBatch.Begin/End` per scene
   - Fewer render target switches

3. **Better Batch Management**
   - Shared `SpriteBatch` across systems
   - Fewer batch breaks

### Code Quality Improvements

1. **DRY Principle**
   - No duplicated camera lookup code
   - No duplicated viewport management
   - No duplicated transform calculation

2. **Maintainability**
   - Clear responsibilities
   - Easier to add new scene types
   - Easier to modify rendering pipeline

3. **Testability**
   - `CameraService` can be mocked
   - `ISceneRenderingCoordinator` can be mocked
   - Scene systems easier to test (no state management)

---

## Risks and Mitigations

### Risk 1: Breaking Changes
**Impact:** High - affects all scene systems and scene creation code

**Mitigation:**
- Update all call sites in single refactor
- No backward compatibility needed (per project rules)
- Comprehensive testing of scene creation and rendering

### Risk 2: Performance Regression
**Impact:** Low - should improve performance

**Mitigation:**
- Profile before/after
- Relationship queries are efficient
- Reduced state changes should improve performance

### Risk 3: Complex Rendering Scenarios
**Impact:** Medium - some systems may need special handling

**Mitigation:**
- `IRenderContext` can be extended
- Coordinator can handle edge cases
- Systems can still access `GraphicsDevice` if needed

---

## Testing Strategy

### Unit Tests

1. **CameraService**
   - Test `GetCameraForScene()` for all `CameraMode` values
   - Test relationship queries
   - Test error handling

2. **SceneRenderingCoordinator**
   - Test `PrepareScene()` sets correct state
   - Test `FinishScene()` restores state
   - Test post-processing application

3. **Scene Systems**
   - Test `RenderScene()` with `IRenderContext`
   - Verify no state management in systems

### Integration Tests

1. **Scene Creation**
   - Test scene creation with `SceneCamera` mode
   - Verify relationship created correctly
   - Test camera entity validation

2. **Scene Rendering**
   - Test full rendering pipeline
   - Verify viewport/transform correct
   - Verify post-processing applied

### Manual Testing

1. **Game Scenes**
   - Verify rendering correct
   - Verify camera following works
   - Verify shaders applied

2. **UI Scenes**
   - Verify UI renders correctly
   - Verify screen-space rendering
   - Verify viewport scaling

3. **Popup Scenes**
   - Verify popups render correctly
   - Verify camera viewport used

---

## Implementation Checklist

### Phase 1: Camera Relationships

- [ ] Create `UsesCamera` relationship type
- [ ] Update `CameraService` interface and implementation
- [ ] Remove `CameraEntityId` from `SceneComponent`
- [ ] Update `SceneSystem.CreateScene()` signature and implementation
- [ ] Update `SceneCameraHelper` to use `CameraService`
- [ ] Update `GameSceneSystem` (remove queries, use `CameraService`)
- [ ] Update `MapPopupSceneSystem` (remove queries, use `CameraService`)
- [ ] Update `MessageBoxSceneSystem` (remove queries, use `CameraService`)
- [ ] Update `UIRenderSystem` (remove queries, use `CameraService`)
- [ ] Update all scene creation call sites
- [ ] Update `GameSceneHelper.CreateGameScene()`
- [ ] Test scene creation with relationships
- [ ] Test camera queries via relationships

### Phase 2: Rendering Coordination

- [ ] Create `IRenderContext` interface
- [ ] Create `RenderContext` struct
- [ ] Create `SceneRenderingCoordinator` class
- [ ] Update `ISceneSystem` interface
- [ ] Update `SceneSystem.Render()` to use coordinator
- [ ] Update `GameSceneSystem.RenderScene()` (remove state management)
- [ ] Update `MapPopupSceneSystem.RenderScene()` (remove state management)
- [ ] Update `MessageBoxSceneSystem.RenderScene()` (remove state management)
- [ ] Update `UIRenderSystem.RenderScene()` (remove state management)
- [ ] Update `ElevationRendererSystem.Render()` (remove batch management)
- [ ] Update `SystemManager` to create coordinator
- [ ] Test full rendering pipeline
- [ ] Verify viewport/transform correct
- [ ] Verify post-processing works
- [ ] Performance profiling

---

## Future Enhancements

### Potential Improvements

1. **Render Context Extensions**
   - Add viewport override support
   - Add custom transform support
   - Add render target override support

2. **Batch Optimization**
   - Automatic batch breaking for shader changes
   - Texture atlas optimization
   - Draw call batching

3. **Multi-Camera Support**
   - Support multiple cameras per scene
   - Camera layers/priorities
   - Camera transitions

4. **Rendering Statistics**
   - Track draw calls per scene
   - Track batch breaks
   - Performance metrics

---

## Conclusion

This design addresses the identified architectural issues:

1. **Camera-Scene Relationships**: Replaces weak entity ID storage with proper ECS relationships
2. **Rendering Coordination**: Centralizes rendering state management in a dedicated coordinator

The refactor follows ECS best practices:
- Data in components (`SceneComponent`, `CameraComponent`)
- Relationships for associations (`UsesCamera`)
- Systems for behavior (`SceneRenderingCoordinator`, scene systems)
- Clear separation of concerns

Implementation should proceed in two phases to manage complexity and testing.
