# Why Are Scene Systems Registered in SystemManager?

## Current Architecture

### Systems Registered in SystemManager:
```
SceneSystem (lifecycle manager)
  └── Registered in SystemManager ✅

GameSceneSystem (game scene behavior)
  └── Registered in SystemManager ✅

LoadingSceneSystem (loading scene behavior)
  └── Registered in SystemManager ✅

DebugBarSceneSystem (debug bar scene behavior)
  └── Registered in SystemManager ✅

MapPopupSceneSystem (popup scene behavior)
  └── Should be registered in SystemManager ✅
```

## Why Are They Registered Separately?

### ECS Architecture Pattern

**In ECS, systems are registered with the World/Group, not with other systems:**

```csharp
// Systems are registered with the update Group
_updateSystems = new Group<float>("UpdateSystems", systems);
_updateSystems.Update(deltaTime); // All systems update in priority order
```

**SceneSystem doesn't "manage" other systems** - it manages **scene entities**:
- `SceneSystem` - manages scene entity lifecycle (create/destroy/activate/pause)
- `GameSceneSystem` - operates on game scene entities (update/render)
- `LoadingSceneSystem` - operates on loading scene entities (update/render)

### Why Both Are Needed

**SceneSystem (lifecycle):**
- Creates/destroys scene entities
- Manages scene stack (priority ordering)
- Handles scene state (active/paused)
- Does NOT know about scene types or rendering

**Scene-Specific Systems (behavior):**
- Query for scene entities of their type
- Handle update/render logic for those scenes
- Operate independently

### The Relationship

```
SceneSystem
  ├── Creates GameSceneComponent entity
  ├── Creates LoadingSceneComponent entity
  └── Creates MapPopupSceneComponent entity

GameSceneSystem
  └── Queries for GameSceneComponent entities → renders them

LoadingSceneSystem
  └── Queries for LoadingSceneComponent entities → updates/renders them

MapPopupSceneSystem
  └── Queries for MapPopupSceneComponent entities → updates/renders them
```

## Alternative: Should SceneSystem Own Everything?

### Option 1: Current (Separate Systems)

**Pros:**
- ✅ Separation of concerns
- ✅ Each system has single responsibility
- ✅ Easy to test independently
- ✅ Mods can add new scene systems without modifying SceneSystem

**Cons:**
- ❌ More systems registered in SystemManager
- ❌ SceneRendererSystem already acts as coordinator

### Option 2: SceneSystem Handles Everything

**If SceneSystem handled all scene logic:**

```csharp
public class SceneSystem : BaseSystem<World, float>
{
    // Lifecycle management
    public Entity CreateScene(...) { ... }
    
    // Update all scene types
    public override void Update(in float deltaTime)
    {
        // Update loading scenes (process progress queue)
        UpdateLoadingScenes(deltaTime);
        
        // Update other scene types as needed
    }
    
    // Render all scene types
    public void Render(GameTime gameTime)
    {
        IterateScenesReverse((sceneEntity, sceneComponent) => {
            if (World.Has<GameSceneComponent>(sceneEntity)) {
                RenderGameScene(sceneEntity, gameTime);
            } else if (World.Has<LoadingSceneComponent>(sceneEntity)) {
                RenderLoadingScene(sceneEntity, gameTime);
            } else if (World.Has<DebugBarSceneComponent>(sceneEntity)) {
                RenderDebugBarScene(sceneEntity, gameTime);
            } else if (World.Has<MapPopupSceneComponent>(sceneEntity)) {
                RenderMapPopupScene(sceneEntity, gameTime);
            }
        });
    }
}
```

**Pros:**
- ✅ Single system instead of multiple
- ✅ Simpler registration
- ✅ All scene logic in one place

**Cons:**
- ❌ SceneSystem becomes a god class
- ❌ Harder to test individual scene types
- ❌ Mods need to modify SceneSystem to add new types

## The Real Question

**You're asking: "Why do we have GameSceneSystem in SystemManager if SceneSystem should manage scenes?"**

**Answer:** SceneSystem manages **scene entities** (data), not **scene systems** (behavior).

**In ECS:**
- **Entities** = data (scenes are entities)
- **Systems** = behavior (scene systems operate on scene entities)
- **Both systems and entities** are registered/managed by the World/Group

**SceneSystem doesn't "own" GameSceneSystem** - they're both systems that operate on the same entities:
- `SceneSystem` - manages scene entity lifecycle
- `GameSceneSystem` - operates on game scene entities

## Current Reality

**SceneRendererSystem already acts as coordinator:**
```csharp
// SceneRendererSystem already knows about all scene types!
if (World.Has<GameSceneComponent>(sceneEntity)) {
    _gameSceneSystem.RenderScene(sceneEntity, gameTime);
} else if (World.Has<LoadingSceneComponent>(sceneEntity)) {
    _loadingSceneSystem.RenderScene(sceneEntity, gameTime);
}
```

**So we have:**
- SceneSystem - manages lifecycle
- Scene-specific systems - handle behavior
- SceneRendererSystem - coordinates rendering

**This is the current pattern, and it works, but it's complex.**

## Recommendation

**Keep current architecture BUT:**
- Scene systems are registered in SystemManager (this is correct for ECS)
- SceneSystem manages scene entities (lifecycle)
- Scene-specific systems operate on scene entities (behavior)
- SceneRendererSystem coordinates rendering

**OR consolidate:**
- Move all scene logic into SceneSystem
- Remove scene-specific systems
- Simpler but less modular

**The key insight:** SceneSystem doesn't "manage" other systems - it manages scene entities. Both SceneSystem and scene-specific systems are registered in SystemManager because they're both systems that need to update.

