# Scene Architecture Proposal: Data-Driven & Moddable

## Problem Statement

Current issues:
1. **SceneSystem would need methods for every scene type** → Violates Open/Closed Principle, impossible to mod
2. **Too many systems** → MapPopupSystem, SceneSystem, Scene classes create disjointed architecture
3. **Can't mod scenes** → No way for mods to define new scene types
4. **Tight coupling** → SceneSystem must know about all scene types

## Solution: Component-Based Scene Systems

### Core Principle
**Scenes are entities with components. Systems query for scene types they handle. SceneSystem only manages lifecycle.**

---

## Architecture Overview

```
┌─────────────────────────────────────────────────────────────┐
│                    SceneSystem                              │
│  - Manages scene stack (priority, lifecycle)              │
│  - Handles scene creation/destruction                      │
│  - Does NOT know about scene types                         │
│  - Does NOT handle update/render                           │
└─────────────────────────────────────────────────────────────┘
                          │
                          │ Scene entities
                          ▼
        ┌─────────────────────────────────────┐
        │   SceneEntity (Entity)              │
        │  ┌───────────────────────────────┐  │
        │  │ SceneComponent               │  │ (data: SceneId, Priority, etc.)
        │  └───────────────────────────────┘  │
        │  ┌───────────────────────────────┐  │
        │  │ GameSceneComponent            │  │ (marker)
        │  └───────────────────────────────┘  │
        └─────────────────────────────────────┘
                          │
        ┌─────────────────┼─────────────────┐
        │                 │                 │
        ▼                 ▼                 ▼
┌──────────────┐  ┌──────────────┐  ┌──────────────┐
│GameScene     │  │LoadingScene  │  │MapPopupScene │
│System        │  │System        │  │System        │
│              │  │              │  │              │
│Queries for: │  │Queries for:  │  │Queries for:  │
│GameScene     │  │LoadingScene  │  │MapPopupScene │
│Component     │  │Component     │  │Component     │
│              │  │              │  │              │
│Handles:      │  │Handles:      │  │Handles:      │
│- Update()    │  │- Update()    │  │- Update()    │
│- Render()    │  │- Render()    │  │- Render()    │
└──────────────┘  └──────────────┘  └──────────────┘
```

---

## Key Design Decisions

### 1. SceneSystem = Lifecycle Manager Only
```csharp
public class SceneSystem : BaseSystem<World, float> {
    // ONLY manages:
    // - Scene stack (priority ordering)
    // - Scene creation/destruction
    // - Scene state (active/paused)
    // - Scene blocking (BlocksUpdate/Draw/Input)
    
    // Does NOT:
    // - Know about scene types
    // - Handle update/render
    // - Query for scene content
}
```

### 2. Scene-Specific Systems Handle Behavior
```csharp
// Each scene type has its own system(s)
public class GameSceneSystem : BaseSystem<World, float> {
    private readonly QueryDescription _gameScenesQuery = new QueryDescription()
        .WithAll<SceneComponent, GameSceneComponent>();
    
    public override void Update(in float deltaTime) {
        // Query for active game scenes
        World.Query(in _gameScenesQuery, (Entity e, ref SceneComponent scene) => {
            if (scene.IsActive && !scene.IsPaused) {
                UpdateGameScene(e, ref scene, deltaTime);
            }
        });
    }
    
    public void Render(GameTime gameTime) {
        // Query for active game scenes (reverse order for rendering)
        World.Query(in _gameScenesQuery, (Entity e, ref SceneComponent scene) => {
            if (scene.IsActive) {
                RenderGameScene(e, ref scene, gameTime);
            }
        });
    }
}
```

### 3. Scene Definitions (Data-Driven)
```json
// Definitions/Scenes/GameScene.json
{
  "id": "base:scene:game",
  "type": "GameScene",
  "defaultPriority": 50,
  "defaultCameraMode": "GameCamera",
  "renderSystems": [
    "MapRendererSystem",
    "SpriteRendererSystem",
    "MapBorderRendererSystem"
  ]
}
```

### 4. Mods Can Add Scene Types
```json
// Mods/my-mod/Definitions/Scenes/CustomScene.json
{
  "id": "mymod:scene:custom",
  "type": "CustomScene",
  "defaultPriority": 60,
  "defaultCameraMode": "ScreenCamera"
}
```

```csharp
// Mods/my-mod/Systems/CustomSceneSystem.cs
public class CustomSceneSystem : BaseSystem<World, float> {
    private readonly QueryDescription _customScenesQuery = new QueryDescription()
        .WithAll<SceneComponent, CustomSceneComponent>();
    
    // Mod provides its own system to handle the scene type
}
```

---

## Implementation Structure

### SceneSystem (Lifecycle Only)
```csharp
namespace MonoBall.Core.Scenes.Systems
{
    /// <summary>
    /// Manages scene lifecycle: creation, destruction, priority stack, state.
    /// Does NOT handle update/render - that's done by scene-specific systems.
    /// </summary>
    public class SceneSystem : BaseSystem<World, float>
    {
        private readonly List<Entity> _sceneStack = new List<Entity>();
        private readonly Dictionary<string, Entity> _sceneIds = new Dictionary<string, Entity>();
        
        // Lifecycle methods only
        public Entity CreateScene(SceneComponent sceneComponent, params object[] components) { }
        public void DestroyScene(Entity sceneEntity) { }
        public void SetSceneActive(string sceneId, bool active) { }
        
        // Update() - NO scene update logic here!
        // Scene-specific systems handle updates
        public override void Update(in float deltaTime)
        {
            // Just cleanup dead entities
            CleanupDeadEntities();
            // Scene-specific systems will query and update scenes
        }
    }
}
```

### Scene-Specific Systems
```csharp
namespace MonoBall.Core.Scenes.Systems
{
    /// <summary>
    /// Handles update and rendering for GameScene entities.
    /// Queries for GameSceneComponent entities and processes them.
    /// </summary>
    public class GameSceneSystem : BaseSystem<World, float>
    {
        private readonly GraphicsDevice _graphicsDevice;
        private readonly SpriteBatch _spriteBatch;
        private readonly MapRendererSystem _mapRendererSystem;
        private readonly SpriteRendererSystem? _spriteRendererSystem;
        private readonly MapBorderRendererSystem? _mapBorderRendererSystem;
        private readonly ShaderManagerSystem? _shaderManagerSystem;
        
        private readonly QueryDescription _gameScenesQuery = new QueryDescription()
            .WithAll<SceneComponent, GameSceneComponent>();
        
        public override void Update(in float deltaTime)
        {
            // Query for active, unpaused game scenes
            World.Query(in _gameScenesQuery, (Entity e, ref SceneComponent scene) =>
            {
                if (scene.IsActive && !scene.IsPaused && !scene.BlocksUpdate)
                {
                    // Game scenes typically don't need per-frame updates
                    // But if they do, add logic here
                }
            });
        }
        
        public void Render(GameTime gameTime)
        {
            // Query for active game scenes
            // Note: SceneSystem manages priority, but we query in reverse for rendering
            var scenes = GetActiveGameScenes();
            foreach (var sceneEntity in scenes.Reverse())
            {
                ref var scene = ref World.Get<SceneComponent>(sceneEntity);
                RenderGameScene(sceneEntity, ref scene, gameTime);
                
                if (scene.BlocksDraw) break;
            }
        }
        
        private void RenderGameScene(Entity sceneEntity, ref SceneComponent scene, GameTime gameTime)
        {
            var camera = GetCameraForScene(ref scene);
            if (!camera.HasValue) return;
            
            // Render maps, sprites, borders, post-processing
            _mapRendererSystem.Render(gameTime, sceneEntity);
            // ... rest of rendering logic
        }
    }
}
```

### MapPopupSystem Integration
```csharp
namespace MonoBall.Core.ECS.Systems
{
    /// <summary>
    /// Handles map popup lifecycle AND rendering.
    /// Creates MapPopupSceneComponent entities and renders them.
    /// </summary>
    public class MapPopupSystem : BaseSystem<World, float>
    {
        private readonly SceneSystem _sceneSystem;
        
        // Creates popup scene entity
        private void OnMapPopupShow(ref MapPopupShowEvent evt)
        {
            var sceneEntity = _sceneSystem.CreateScene(
                new SceneComponent { SceneId = "map:popup", ... },
                new MapPopupSceneComponent()
            );
            
            // Create popup entity
            var popupEntity = World.Create(...);
        }
        
        // Renders popup scenes
        public void Render(GameTime gameTime)
        {
            var query = new QueryDescription()
                .WithAll<SceneComponent, MapPopupSceneComponent>();
            
            World.Query(in query, (Entity e, ref SceneComponent scene) =>
            {
                if (scene.IsActive)
                {
                    RenderPopupScene(e, ref scene, gameTime);
                }
            });
        }
    }
}
```

---

## Benefits

### 1. **Extensible Without Modification**
- Add new scene type: Create component + system
- No changes to SceneSystem needed
- Follows Open/Closed Principle

### 2. **Moddable**
- Mods can define scene types in JSON
- Mods can provide systems to handle their scene types
- SceneSystem doesn't need to know about mod scenes

### 3. **Clear Responsibilities**
- **SceneSystem**: Lifecycle only
- **GameSceneSystem**: Game scene update/render
- **MapPopupSystem**: Popup lifecycle + render
- **LoadingSceneSystem**: Loading screen update/render

### 4. **Reduced Coupling**
- Systems don't depend on SceneSystem for behavior
- Systems query for their scene types independently
- SceneSystem doesn't know about scene types

### 5. **Consistent with Codebase**
- Matches pattern: MapSystem queries for MapComponent
- Matches pattern: SpriteSystem queries for SpriteComponent
- Matches pattern: Definition-driven content

---

## Migration Path

### Phase 1: Split SceneSystem
1. Rename `SceneManagerSystem` → `SceneSystem`
2. Remove update/render logic from SceneSystem
3. Keep only lifecycle management

### Phase 2: Create Scene-Specific Systems
1. Create `GameSceneSystem` - move rendering logic from Scene classes
2. Create `LoadingSceneSystem` - move rendering logic
3. Create `DebugBarSceneSystem` - move rendering logic
4. Update `MapPopupSystem` to handle its own rendering

### Phase 3: Remove Scene Classes
1. Delete `Scene`, `GameScene`, `LoadingScene`, etc.
2. Delete `SceneInstanceComponent`
3. Update scene creation to not require instances

### Phase 4: Scene Definitions (Future)
1. Add `SceneDefinition` class
2. Load scene definitions from mods
3. Use definitions to configure default scene properties

---

## Example: Adding a New Scene Type

### Step 1: Create Component
```csharp
public struct CustomSceneComponent { }
```

### Step 2: Create System
```csharp
public class CustomSceneSystem : BaseSystem<World, float>
{
    private readonly QueryDescription _query = new QueryDescription()
        .WithAll<SceneComponent, CustomSceneComponent>();
    
    public override void Update(in float deltaTime) { }
    public void Render(GameTime gameTime) { }
}
```

### Step 3: Register System
```csharp
systemManager.RegisterRenderSystem(customSceneSystem);
```

### Step 4: Create Scene
```csharp
var sceneEntity = sceneSystem.CreateScene(
    new SceneComponent { SceneId = "custom:main", ... },
    new CustomSceneComponent()
);
```

**No changes to SceneSystem needed!**

---

## Comparison: Before vs After

### Before (Current)
```
SceneSystem
├── Update() - calls scene.Update() for all types
├── Render() - calls scene.Render() for all types
└── Must know about: GameScene, LoadingScene, MapPopupScene, DebugBarScene, ...

Scene Classes
├── GameScene.Update()
├── GameScene.Render()
└── Dependencies: MapRendererSystem, SpriteRendererSystem, ...

MapPopupSystem
└── Creates scenes but doesn't render them
```

### After (Proposed)
```
SceneSystem
└── Lifecycle only (create, destroy, manage stack)

GameSceneSystem
├── Update() - queries GameSceneComponent
└── Render() - queries GameSceneComponent

LoadingSceneSystem
├── Update() - queries LoadingSceneComponent
└── Render() - queries LoadingSceneComponent

MapPopupSystem
├── Creates MapPopupSceneComponent entities
└── Render() - queries MapPopupSceneComponent

[Mod] CustomSceneSystem
├── Update() - queries CustomSceneComponent
└── Render() - queries CustomSceneComponent
```

---

## Key Principles

1. **SceneSystem = Lifecycle Manager**
   - Manages stack, priority, state
   - Does NOT handle update/render

2. **Scene-Specific Systems = Behavior Handlers**
   - Each scene type has its own system(s)
   - Systems query for their scene type
   - Systems handle update/render independently

3. **Component-Based Scene Types**
   - Marker components identify scene types
   - Systems query by component composition
   - Easy to extend with new types

4. **Data-Driven Scene Configuration**
   - Scene definitions in JSON (future)
   - Mods can define scene types
   - Default properties from definitions

5. **No Centralized Scene Logic**
   - No single system knows about all scene types
   - Each system handles its own scene type
   - Follows Single Responsibility Principle

---

## Questions & Answers

**Q: How does SceneSystem know when to stop iterating for BlocksUpdate/BlocksDraw?**
A: SceneSystem doesn't iterate for update/render. Scene-specific systems query independently and check BlocksUpdate/BlocksDraw flags themselves.

**Q: How do we ensure rendering order (priority)?**
A: SceneSystem maintains priority-ordered stack. Scene-specific systems can query this order, or SceneSystem can provide helper methods to get ordered scene lists.

**Q: What about scene transitions?**
A: SceneSystem handles transitions (activate/deactivate). Scene-specific systems react to state changes via events or queries.

**Q: How do mods register their scene systems?**
A: Mods provide systems, SystemManager registers them. SceneSystem doesn't need to know about them.

**Q: What if a scene needs multiple systems?**
A: That's fine! A scene can have multiple systems querying for it. For example, GameSceneSystem handles rendering, GameSceneUpdateSystem handles updates (if needed).

---

## Next Steps

1. **Review this proposal** - Does this address the concerns?
2. **Refactor SceneSystem** - Remove update/render logic
3. **Create scene-specific systems** - GameSceneSystem, LoadingSceneSystem, etc.
4. **Update MapPopupSystem** - Handle its own rendering
5. **Remove Scene classes** - Delete Scene, GameScene, etc.
6. **Add Scene Definitions** - Data-driven scene configuration (future)

