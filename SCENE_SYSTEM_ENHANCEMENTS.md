# Scene System Enhancements (Post-Migration)

This document outlines realistic enhancements to the scene system architecture that can be implemented without scripting support.

## 🎯 Enhancement Overview

### 1. **Scene Definitions (JSON-Based)** ⭐⭐⭐
**Priority:** High  
**Complexity:** Medium  
**Time Estimate:** 1-2 weeks

**Current State:**
- Scenes are created programmatically via `SceneSystem.CreateScene()`
- Scene configuration is hardcoded in C# (e.g., `GameSceneHelper.CreateGameScene()`)
- No data-driven scene definitions

**Proposed Enhancement:**
Add JSON-based scene definitions that can be loaded from mods, similar to other definition types.

**Benefits:**
- Mods can define custom scenes without code changes
- Scenes can be configured via JSON
- Easier to create scene templates
- Better separation of data and code

**Implementation:**

**1.1 Create SceneDefinition class:**
```csharp
namespace MonoBall.Core.Mods
{
    /// <summary>
    /// Definition for a scene that can be loaded from JSON.
    /// </summary>
    public class SceneDefinition
    {
        /// <summary>
        /// Unique identifier for the scene definition.
        /// </summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// Scene ID to use when creating the scene entity.
        /// </summary>
        public string SceneId { get; set; } = string.Empty;

        /// <summary>
        /// Scene type marker component to add.
        /// </summary>
        public string SceneType { get; set; } = string.Empty; // "GameScene", "LoadingScene", etc.

        /// <summary>
        /// Scene priority.
        /// </summary>
        public int Priority { get; set; }

        /// <summary>
        /// Camera mode for the scene.
        /// </summary>
        public string CameraMode { get; set; } = "GameCamera"; // "GameCamera", "ScreenCamera", "SceneCamera"

        /// <summary>
        /// Optional camera entity ID (required if CameraMode == "SceneCamera").
        /// </summary>
        public int? CameraEntityId { get; set; }

        /// <summary>
        /// Whether this scene blocks lower scenes from updating.
        /// </summary>
        public bool BlocksUpdate { get; set; }

        /// <summary>
        /// Whether this scene blocks lower scenes from drawing.
        /// </summary>
        public bool BlocksDraw { get; set; }

        /// <summary>
        /// Whether this scene blocks lower scenes from receiving input.
        /// </summary>
        public bool BlocksInput { get; set; }

        /// <summary>
        /// Background color for the scene (RGBA).
        /// </summary>
        public SceneBackgroundColor? BackgroundColor { get; set; }

        /// <summary>
        /// Whether the scene should be active when created.
        /// </summary>
        public bool StartActive { get; set; } = true;

        /// <summary>
        /// Whether the scene should be paused when created.
        /// </summary>
        public bool StartPaused { get; set; } = false;
    }

    /// <summary>
    /// Background color definition for a scene.
    /// </summary>
    public class SceneBackgroundColor
    {
        public byte R { get; set; }
        public byte G { get; set; }
        public byte B { get; set; }
        public byte A { get; set; } = 255;
    }
}
```

**1.2 Add SceneDefinitionLoader:**
```csharp
namespace MonoBall.Core.Mods
{
    /// <summary>
    /// Loads scene definitions from mod directories.
    /// </summary>
    public class SceneDefinitionLoader
    {
        private readonly ILogger _logger;

        public SceneDefinitionLoader(ILogger logger)
        {
            _logger = logger;
        }

        public SceneDefinition? LoadFromFile(string filePath)
        {
            // Load JSON and deserialize to SceneDefinition
            // Validate required fields
            // Return SceneDefinition or null if invalid
        }
    }
}
```

**1.3 Register SceneDefinitions in ModManager:**
- Add `SceneDefinitions` to `contentFolders` in `mod.json`
- Load scene definitions during mod loading
- Store in `DefinitionRegistry`

**1.4 Add SceneDefinitionHelper:**
```csharp
namespace MonoBall.Core.Scenes
{
    /// <summary>
    /// Helper for creating scenes from SceneDefinitions.
    /// </summary>
    public static class SceneDefinitionHelper
    {
        public static Entity CreateSceneFromDefinition(
            World world,
            SceneSystem sceneSystem,
            SceneDefinition definition
        )
        {
            // Convert SceneDefinition to SceneComponent
            // Determine marker component based on SceneType
            // Call SceneSystem.CreateScene()
        }
    }
}
```

**Example JSON Definition:**
```json
{
  "id": "base:scene:game",
  "sceneId": "game:main",
  "sceneType": "GameScene",
  "priority": 50,
  "cameraMode": "GameCamera",
  "blocksUpdate": false,
  "blocksDraw": false,
  "blocksInput": false,
  "backgroundColor": {
    "r": 0,
    "g": 0,
    "b": 0,
    "a": 255
  },
  "startActive": true,
  "startPaused": false
}
```

---

### 2. **BackgroundColor Component** ⭐⭐⭐
**Priority:** High  
**Complexity:** Low  
**Time Estimate:** 2-3 days

**Current State:**
- Background color is hardcoded in `SceneRendererSystem.GetBackgroundColor()`
- Each scene type has a hardcoded color (GameScene = Black, LoadingScene = from LoadingSceneRendererSystem, etc.)
- Adding new scene types requires modifying `SceneRendererSystem`

**Proposed Enhancement:**
Add `BackgroundColor` property to `SceneComponent` for data-driven background colors.

**Benefits:**
- No code changes needed for new scene types
- Mods can customize scene background colors
- Simpler `GetBackgroundColor()` implementation
- More flexible and extensible

**Implementation:**

**2.1 Update SceneComponent:**
```csharp
namespace MonoBall.Core.Scenes.Components
{
    public struct SceneComponent
    {
        // ... existing properties ...

        /// <summary>
        /// Background color for the scene. If null, uses default based on scene type.
        /// </summary>
        public Color? BackgroundColor { get; set; }
    }
}
```

**2.2 Update SceneRendererSystem.GetBackgroundColor():**
```csharp
public Color GetBackgroundColor()
{
    Color? backgroundColor = null;

    _sceneSystem.IterateScenesReverse(
        (sceneEntity, sceneComponent) =>
        {
            if (!sceneComponent.IsActive)
            {
                return true;
            }

            // Use BackgroundColor from SceneComponent if set
            if (sceneComponent.BackgroundColor.HasValue)
            {
                backgroundColor = sceneComponent.BackgroundColor.Value;
            }
            else
            {
                // Fallback to scene type defaults (for backward compatibility)
                if (World.Has<GameSceneComponent>(sceneEntity))
                {
                    backgroundColor = Color.Black;
                }
                else if (World.Has<LoadingSceneComponent>(sceneEntity))
                {
                    backgroundColor = LoadingSceneRendererSystem.GetBackgroundColor();
                }
                // ... other scene types ...
            }

            if (sceneComponent.BlocksDraw)
            {
                return false;
            }

            return true;
        }
    );

    return backgroundColor ?? Color.Black;
}
```

**2.3 Update Scene Creation Sites:**
- Update `GameSceneHelper.CreateGameScene()` to optionally accept background color
- Update `SceneDefinitionHelper` to set background color from definition
- Update all scene creation calls to set background color if needed

---

### 3. **Scene Update Systems (Separation)** ⭐⭐
**Priority:** Medium  
**Complexity:** Low-Medium  
**Time Estimate:** 3-5 days

**Current State:**
- Scene-specific systems (`GameSceneSystem`, `LoadingSceneSystem`, etc.) have `Update()` methods
- Update logic is minimal (mostly empty)
- All scene systems are registered in `SystemManager` update group

**Proposed Enhancement:**
Clarify and potentially separate update logic if scenes need more complex update behavior.

**Benefits:**
- Clear separation of update vs render concerns
- Easier to add scene-specific update logic later
- Better organization

**Implementation Options:**

**Option A: Keep Current Structure (Recommended)**
- Current structure is fine for now
- Update methods are already separated by scene type
- No changes needed unless specific update requirements arise

**Option B: Add SceneUpdateCoordinator (If Needed)**
```csharp
/// <summary>
/// Coordinates scene updates, similar to SceneRendererSystem for rendering.
/// Currently not needed, but can be added if scene updates become more complex.
/// </summary>
public class SceneUpdateCoordinator : BaseSystem<World, float>
{
    // Similar pattern to SceneRendererSystem but for updates
    // Only needed if we need to coordinate updates across scenes
}
```

**Recommendation:** Keep current structure unless specific update coordination needs arise.

---

### 4. **Enhanced Scene Events** ⭐⭐
**Priority:** Medium  
**Complexity:** Low-Medium  
**Time Estimate:** 1 week

**Current State:**
- Basic scene events exist: `SceneCreatedEvent`, `SceneDestroyedEvent`, `SceneActivatedEvent`, etc.
- Events are simple structs with minimal data
- No event filtering or priority system

**Proposed Enhancement:**
Enhance scene events with more context and add missing lifecycle events.

**Benefits:**
- Better event-driven architecture
- More information in events for systems that need it
- Easier to debug scene lifecycle issues

**Implementation:**

**4.1 Add Missing Events:**
```csharp
namespace MonoBall.Core.Scenes.Events
{
    /// <summary>
    /// Event fired when a scene is about to be created (before entity creation).
    /// Allows systems to prepare or cancel scene creation.
    /// </summary>
    public struct SceneCreatingEvent
    {
        public string SceneId { get; set; }
        public bool Cancel { get; set; }
    }

    /// <summary>
    /// Event fired when a scene's priority changes.
    /// </summary>
    public struct ScenePriorityChangedEvent
    {
        public string SceneId { get; set; }
        public int OldPriority { get; set; }
        public int NewPriority { get; set; }
    }

    /// <summary>
    /// Event fired when a scene's camera mode changes.
    /// </summary>
    public struct SceneCameraModeChangedEvent
    {
        public string SceneId { get; set; }
        public SceneCameraMode OldMode { get; set; }
        public SceneCameraMode NewMode { get; set; }
    }
}
```

**4.2 Enhance Existing Events:**
```csharp
/// <summary>
/// Enhanced SceneCreatedEvent with more context.
/// </summary>
public struct SceneCreatedEvent
{
    public string SceneId { get; set; }
    public Entity SceneEntity { get; set; }
    public int Priority { get; set; }
    public SceneCameraMode CameraMode { get; set; }
    public string? SceneType { get; set; } // "GameScene", "LoadingScene", etc.
    public DateTime CreatedAt { get; set; }
}
```

**4.3 Add Event Helpers:**
```csharp
namespace MonoBall.Core.Scenes
{
    /// <summary>
    /// Helper methods for scene event handling.
    /// </summary>
    public static class SceneEventHelper
    {
        public static void FireSceneCreating(World world, string sceneId, out bool cancelled)
        {
            var evt = new SceneCreatingEvent { SceneId = sceneId, Cancel = false };
            EventBus.Send(ref evt);
            cancelled = evt.Cancel;
        }

        public static void FireSceneCreated(World world, Entity sceneEntity, ref SceneComponent scene)
        {
            var evt = new SceneCreatedEvent
            {
                SceneId = scene.SceneId,
                SceneEntity = sceneEntity,
                Priority = scene.Priority,
                CameraMode = scene.CameraMode,
                CreatedAt = DateTime.UtcNow
            };
            EventBus.Send(ref evt);
        }
    }
}
```

---

### 5. **Mod Scene Support (Framework for Mod Scene Systems)** ⭐⭐⭐
**Priority:** High  
**Complexity:** Medium-High  
**Time Estimate:** 2-3 weeks

**Current State:**
- Scene systems are hardcoded in `SceneRendererSystem` (GameSceneSystem, LoadingSceneSystem, etc.)
- Adding new scene types requires modifying core code
- Mods cannot register custom scene types

**Proposed Enhancement:**
Create a framework that allows mods to register custom scene systems without modifying core code.

**Benefits:**
- Mods can add new scene types
- No core code changes needed for new scene types
- Better extensibility
- Follows modding architecture patterns

**Implementation:**

**5.1 Create ISceneSystem Interface:**
```csharp
namespace MonoBall.Core.Scenes.Systems
{
    /// <summary>
    /// Interface for scene-specific systems that handle update and render for a scene type.
    /// </summary>
    public interface ISceneSystem
    {
        /// <summary>
        /// Gets the marker component type this system handles.
        /// </summary>
        Type MarkerComponentType { get; }

        /// <summary>
        /// Updates a scene entity.
        /// </summary>
        void UpdateScene(Entity sceneEntity, float deltaTime);

        /// <summary>
        /// Renders a scene entity.
        /// </summary>
        void RenderScene(Entity sceneEntity, GameTime gameTime);

        /// <summary>
        /// Gets the background color for a scene (optional).
        /// </summary>
        Color? GetBackgroundColor(Entity sceneEntity);
    }
}
```

**5.2 Create SceneSystemRegistry:**
```csharp
namespace MonoBall.Core.Scenes.Systems
{
    /// <summary>
    /// Registry for scene systems that can be registered by mods.
    /// </summary>
    public class SceneSystemRegistry
    {
        private readonly Dictionary<Type, ISceneSystem> _systemsByMarker = new();
        private readonly Dictionary<string, ISceneSystem> _systemsByTypeName = new();

        /// <summary>
        /// Registers a scene system for a marker component type.
        /// </summary>
        public void RegisterSceneSystem(ISceneSystem sceneSystem)
        {
            var markerType = sceneSystem.MarkerComponentType;
            _systemsByMarker[markerType] = sceneSystem;
            _systemsByTypeName[markerType.Name] = sceneSystem;
        }

        /// <summary>
        /// Gets a scene system by marker component type.
        /// </summary>
        public ISceneSystem? GetSceneSystem(Type markerComponentType)
        {
            return _systemsByMarker.TryGetValue(markerComponentType, out var system) ? system : null;
        }

        /// <summary>
        /// Gets a scene system by marker component type name (for JSON definitions).
        /// </summary>
        public ISceneSystem? GetSceneSystem(string markerComponentTypeName)
        {
            return _systemsByTypeName.TryGetValue(markerComponentTypeName, out var system) ? system : null;
        }
    }
}
```

**5.3 Update SceneRendererSystem to Use Registry:**
```csharp
public class SceneRendererSystem : BaseSystem<World, float>
{
    private readonly SceneSystemRegistry _sceneSystemRegistry;
    // ... existing fields ...

    public SceneRendererSystem(
        World world,
        GraphicsDevice graphicsDevice,
        SceneSystem sceneSystem,
        SceneSystemRegistry sceneSystemRegistry, // Add registry
        GameSceneSystem gameSceneSystem, // Keep for backward compatibility
        LoadingSceneSystem loadingSceneSystem,
        DebugBarSceneSystem debugBarSceneSystem,
        ECS.Systems.MapPopupSystem mapPopupSystem,
        ILogger logger,
        ShaderManagerSystem? shaderManagerSystem = null
    )
    {
        _sceneSystemRegistry = sceneSystemRegistry;
        // ... existing initialization ...

        // Register built-in scene systems
        _sceneSystemRegistry.RegisterSceneSystem(new SceneSystemAdapter(gameSceneSystem, typeof(GameSceneComponent)));
        _sceneSystemRegistry.RegisterSceneSystem(new SceneSystemAdapter(loadingSceneSystem, typeof(LoadingSceneComponent)));
        _sceneSystemRegistry.RegisterSceneSystem(new SceneSystemAdapter(debugBarSceneSystem, typeof(DebugBarSceneComponent)));
        _sceneSystemRegistry.RegisterSceneSystem(new SceneSystemAdapter(mapPopupSystem, typeof(MapPopupSceneComponent)));
    }

    public void Render(GameTime gameTime)
    {
        // ... existing code ...

        _sceneSystem.IterateScenesReverse(
            (sceneEntity, sceneComponent) =>
            {
                if (!sceneComponent.IsActive)
                {
                    return true;
                }

                // Try to find scene system by checking marker components
                ISceneSystem? sceneSystem = null;
                foreach (var markerType in _sceneSystemRegistry.GetRegisteredMarkerTypes())
                {
                    if (World.Has(sceneEntity, markerType))
                    {
                        sceneSystem = _sceneSystemRegistry.GetSceneSystem(markerType);
                        break;
                    }
                }

                if (sceneSystem != null)
                {
                    sceneSystem.RenderScene(sceneEntity, gameTime);
                }
                else
                {
                    _logger.Warning(
                        "Scene '{SceneId}' has no registered scene system. Scene will not render.",
                        sceneComponent.SceneId
                    );
                }

                if (sceneComponent.BlocksDraw)
                {
                    return false;
                }

                return true;
            }
        );
    }
}
```

**5.4 Create SceneSystemAdapter:**
```csharp
/// <summary>
/// Adapter to convert existing scene systems to ISceneSystem interface.
/// </summary>
public class SceneSystemAdapter : ISceneSystem
{
    private readonly object _sceneSystem;
    private readonly Type _markerComponentType;

    public SceneSystemAdapter(object sceneSystem, Type markerComponentType)
    {
        _sceneSystem = sceneSystem;
        _markerComponentType = markerComponentType;
    }

    public Type MarkerComponentType => _markerComponentType;

    public void UpdateScene(Entity sceneEntity, float deltaTime)
    {
        // Use reflection or dynamic dispatch to call UpdateScene
        // Or require scene systems to implement ISceneSystem directly
    }

    public void RenderScene(Entity sceneEntity, GameTime gameTime)
    {
        // Use reflection or dynamic dispatch
    }

    public Color? GetBackgroundColor(Entity sceneEntity)
    {
        return null; // Default implementation
    }
}
```

**5.5 Add Mod API for Scene System Registration:**
```csharp
namespace MonoBall.Core.Mods
{
    /// <summary>
    /// API for mods to register custom scene systems.
    /// </summary>
    public interface ISceneSystemRegistrationApi
    {
        /// <summary>
        /// Registers a custom scene system.
        /// </summary>
        void RegisterSceneSystem(ISceneSystem sceneSystem, string markerComponentTypeName);
    }
}
```

**5.6 Example Mod Scene System:**
```csharp
namespace MyMod.Scenes
{
    /// <summary>
    /// Custom scene system for a mod.
    /// </summary>
    public class CustomMenuSceneSystem : BaseSystem<World, float>, ISceneSystem
    {
        public Type MarkerComponentType => typeof(CustomMenuSceneComponent);

        public void UpdateScene(Entity sceneEntity, float deltaTime)
        {
            // Update logic
        }

        public void RenderScene(Entity sceneEntity, GameTime gameTime)
        {
            // Render logic
        }

        public Color? GetBackgroundColor(Entity sceneEntity)
        {
            return Color.DarkBlue;
        }
    }
}
```

---

## 📊 Implementation Priority

| Enhancement | Priority | Complexity | Time | Dependencies |
|-------------|----------|------------|------|--------------|
| BackgroundColor Component | ⭐⭐⭐ | Low | 2-3 days | None |
| Scene Definitions | ⭐⭐⭐ | Medium | 1-2 weeks | BackgroundColor |
| Mod Scene Support | ⭐⭐⭐ | Medium-High | 2-3 weeks | Scene Definitions |
| Enhanced Scene Events | ⭐⭐ | Low-Medium | 1 week | None |
| Scene Update Systems | ⭐⭐ | Low | 3-5 days | None |

---

## 🎯 Recommended Implementation Order

### Phase 1: Quick Wins (1 week)
1. **BackgroundColor Component** - Simplest, highest impact
2. **Enhanced Scene Events** - Improves architecture

### Phase 2: Data-Driven (2-3 weeks)
3. **Scene Definitions** - Foundation for modding
4. **Mod Scene Support** - Enables mod scene types

### Phase 3: Polish (Optional)
5. **Scene Update Systems** - Only if needed

---

## 💡 Design Principles

1. **Backward Compatibility:** All enhancements maintain compatibility with existing code
2. **Data-Driven:** Scene configuration moves to JSON definitions
3. **Extensibility:** Mods can add new scene types without core changes
4. **ECS Patterns:** All enhancements follow ECS architecture
5. **Event-Driven:** Enhanced events for better system communication

---

## 🔍 Notes

- All enhancements work without scripting support
- JSON definitions follow existing mod system patterns
- Scene system registry enables mod extensibility
- BackgroundColor component simplifies rendering logic
- Enhanced events improve debugging and system communication

