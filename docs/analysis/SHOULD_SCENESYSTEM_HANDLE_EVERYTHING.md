# Should SceneSystem Handle Updates/Lifecycle/Rendering?

## Current Architecture (Overly Complex?)

### Current Structure
```
SceneSystem
  └── Lifecycle only (create/destroy/activate/pause)
      └── Update() - only does cleanup

GameSceneSystem
  ├── Update() - queries game scenes (currently empty)
  └── RenderScene() - renders game scenes

LoadingSceneSystem
  ├── Update() - processes loading progress
  └── RenderScene() - renders loading scenes

DebugBarSceneSystem
  ├── Update() - queries debug bar scenes (empty)
  └── RenderScene() - renders debug bar scenes

MapPopupSceneSystem (doesn't exist yet)
  ├── Update() - would query popup scenes (empty?)
  └── RenderScene() - would render popup scenes

SceneRendererSystem (coordinator)
  └── Render() - iterates scenes, calls appropriate scene system
```

### Problems with Current Architecture

1. **Too Many Systems**
   - 5+ systems for scene management
   - Each scene type needs its own system
   - SceneRendererSystem acts as coordinator

2. **SceneRendererSystem is Already a God Class**
   ```csharp
   // SceneRendererSystem already knows about all scene types!
   if (World.Has<GameSceneComponent>(sceneEntity)) {
       _gameSceneSystem.RenderScene(sceneEntity, gameTime);
   } else if (World.Has<LoadingSceneComponent>(sceneEntity)) {
       _loadingSceneSystem.RenderScene(sceneEntity, gameTime);
   } else if (World.Has<DebugBarSceneComponent>(sceneEntity)) {
       _debugBarSceneSystem.RenderScene(sceneEntity, gameTime);
   } else if (World.Has<MapPopupSceneComponent>(sceneEntity)) {
       _mapPopupSystem.RenderScene(sceneEntity, gameTime);
   }
   ```
   So we're not avoiding the "god class" problem anyway!

3. **Empty Update Methods**
   ```csharp
   // GameSceneSystem.Update() - does nothing!
   public override void Update(in float deltaTime) {
       World.Query(in _gameScenesQuery, (Entity e, ref SceneComponent scene) => {
           if (scene.IsActive && !scene.IsPaused && !scene.BlocksUpdate) {
               // Empty! No logic here
           }
       });
   }
   ```

4. **Unnecessary Indirection**
   - SceneRendererSystem calls scene-specific systems
   - Scene-specific systems just delegate to render systems
   - Why not have SceneSystem do it directly?

## Alternative: SceneSystem Handles Everything

### Proposed Structure
```
SceneSystem
  ├── Lifecycle (create/destroy/activate/pause) ✅
  ├── Update() - updates all scene types ✅
  └── Render() - renders all scene types ✅
```

### Implementation
```csharp
public class SceneSystem : BaseSystem<World, float>, IPrioritizedSystem, IDisposable
{
    private readonly GraphicsDevice _graphicsDevice;
    private readonly SpriteBatch _spriteBatch;
    private readonly MapRendererSystem _mapRendererSystem;
    private readonly SpriteRendererSystem _spriteRendererSystem;
    private readonly MapBorderRendererSystem _mapBorderRendererSystem;
    private readonly MapPopupRendererSystem _mapPopupRendererSystem;
    private readonly DebugBarRendererSystem _debugBarRendererSystem;
    private readonly LoadingSceneRendererSystem _loadingSceneRendererSystem;
    private readonly ShaderManagerSystem? _shaderManagerSystem;
    private readonly ShaderRendererSystem? _shaderRendererSystem;
    private readonly RenderTargetManager? _renderTargetManager;
    
    // Cached queries
    private readonly QueryDescription _allScenesQuery = new QueryDescription()
        .WithAll<SceneComponent>();
    
    private readonly QueryDescription _gameScenesQuery = new QueryDescription()
        .WithAll<SceneComponent, GameSceneComponent>();
    
    private readonly QueryDescription _loadingScenesQuery = new QueryDescription()
        .WithAll<SceneComponent, LoadingSceneComponent>();
    
    // ... other scene type queries
    
    public override void Update(in float deltaTime)
    {
        // Clean up dead entities
        CleanupDeadEntities();
        
        // Update loading scenes (process progress queue)
        UpdateLoadingScenes(deltaTime);
        
        // Update other scene types as needed
        // (Most scenes don't need per-frame updates)
    }
    
    public void Render(GameTime gameTime)
    {
        // Iterate scenes in reverse priority order
        IterateScenesReverse((sceneEntity, sceneComponent) => {
            if (!sceneComponent.IsActive) {
                return true; // Continue
            }
            
            // Update shader state
            _shaderManagerSystem?.UpdateShaderState(sceneEntity);
            
            // Render based on scene type
            if (World.Has<GameSceneComponent>(sceneEntity)) {
                RenderGameScene(sceneEntity, ref sceneComponent, gameTime);
            } else if (World.Has<LoadingSceneComponent>(sceneEntity)) {
                RenderLoadingScene(sceneEntity, ref sceneComponent, gameTime);
            } else if (World.Has<DebugBarSceneComponent>(sceneEntity)) {
                RenderDebugBarScene(sceneEntity, ref sceneComponent, gameTime);
            } else if (World.Has<MapPopupSceneComponent>(sceneEntity)) {
                RenderMapPopupScene(sceneEntity, ref sceneComponent, gameTime);
            }
            
            // Check BlocksDraw
            if (sceneComponent.BlocksDraw) {
                return false; // Stop iterating
            }
            
            return true; // Continue
        });
    }
    
    private void RenderGameScene(Entity sceneEntity, ref SceneComponent scene, GameTime gameTime)
    {
        // Resolve camera
        var camera = ResolveCamera(scene);
        if (!camera.HasValue) return;
        
        // Render game scene (same logic as GameSceneSystem.RenderScene)
        // ... delegate to MapRendererSystem, SpriteRendererSystem, etc.
    }
    
    private void RenderLoadingScene(Entity sceneEntity, ref SceneComponent scene, GameTime gameTime)
    {
        // Render loading scene (same logic as LoadingSceneSystem.RenderScene)
        // ... delegate to LoadingSceneRendererSystem
    }
    
    // ... other Render methods
}
```

## Comparison

### Current Architecture

**Pros:**
- ✅ Separation of concerns (each scene type has its own system)
- ✅ Easy to test individual scene types
- ✅ Mods can add new scene systems without modifying SceneSystem

**Cons:**
- ❌ Too many systems (5+ systems)
- ❌ SceneRendererSystem is already a god class
- ❌ Empty Update() methods
- ❌ Unnecessary indirection
- ❌ More complex initialization

### Proposed Architecture (SceneSystem Handles Everything)

**Pros:**
- ✅ Simpler - one system instead of 5+
- ✅ Less indirection
- ✅ No empty Update() methods
- ✅ Easier to understand flow
- ✅ SceneRendererSystem already does this anyway!

**Cons:**
- ❌ SceneSystem becomes larger (but SceneRendererSystem is already large)
- ❌ Mods need to modify SceneSystem to add new scene types (but they already need to modify SceneRendererSystem)
- ❌ Harder to test individual scene types (but scene rendering is already tested via SceneRendererSystem)

## The Key Insight

**SceneRendererSystem already acts as a god class** that knows about all scene types and coordinates rendering. So we're not avoiding the problem - we're just moving it around!

**Current Reality:**
- SceneRendererSystem knows about all scene types ✅ (already a god class)
- SceneRendererSystem coordinates rendering ✅ (already doing this)
- Scene-specific systems just delegate ✅ (unnecessary indirection)

**Why not consolidate?**
- Move rendering logic from scene-specific systems into SceneSystem
- Remove SceneRendererSystem (or make it a thin wrapper)
- Remove scene-specific systems (or keep them only if they have real update logic)

## Recommendation

### Option 1: Consolidate into SceneSystem (Recommended)

**Benefits:**
- Simpler architecture
- Less systems to manage
- No unnecessary indirection
- SceneRendererSystem already does this anyway

**Implementation:**
1. Move rendering logic from scene-specific systems into SceneSystem
2. Remove SceneRendererSystem (or make it call SceneSystem.Render())
3. Keep scene-specific systems ONLY if they have real update logic
4. Remove empty Update() methods

### Option 2: Keep Current Architecture BUT Fix It

**If we keep the current architecture:**
1. Remove empty Update() methods
2. Consolidate SceneRendererSystem logic into SceneSystem
3. Keep scene-specific systems only for rendering coordination
4. But this still has unnecessary indirection

## Conclusion

**You're right to question this!** The current architecture is overly complex:

- SceneRendererSystem already knows about all scene types (god class)
- Scene-specific systems mostly just delegate (unnecessary indirection)
- Empty Update() methods (no real logic)

**Better approach:**
- Have SceneSystem handle lifecycle + update + render
- Remove SceneRendererSystem (or make it a thin wrapper)
- Keep scene-specific systems ONLY if they have real update logic (like LoadingSceneSystem processing progress queue)

This would be **simpler, clearer, and easier to maintain** without losing functionality.

