# SystemManager → SceneSystem → Update/Render Flow

## Proposed Architecture Flow

### Update Flow

```
Game.Update()
  └── SystemManager.Update(gameTime)
      └── SceneSystem.Update(deltaTime)
          └── Coordinates scene-specific systems:
              ├── GameSceneSystem.Update(deltaTime)
              │   └── Queries for GameSceneComponent entities
              │       └── Updates game scene logic (if any)
              │
              ├── LoadingSceneSystem.Update(deltaTime)
              │   └── Queries for LoadingSceneComponent entities
              │       └── Processes loading progress queue
              │
              ├── DebugBarSceneSystem.Update(deltaTime)
              │   └── Queries for DebugBarSceneComponent entities
              │       └── Updates debug bar logic (if any)
              │
              └── MapPopupSceneSystem.Update(deltaTime)
                  └── Queries for MapPopupComponent + PopupAnimationComponent
                      └── Updates popup animation states
```

### Render Flow

**Option 1: SceneRendererSystem Still Exists (Current Pattern)**
```
Game.Draw()
  └── SystemManager.Render(gameTime)
      └── SceneRendererSystem.Render(gameTime)
          └── Iterates scenes in reverse priority order:
              └── For each active scene:
                  ├── If GameSceneComponent → GameSceneSystem.RenderScene()
                  ├── If LoadingSceneComponent → LoadingSceneSystem.RenderScene()
                  ├── If DebugBarSceneComponent → DebugBarSceneSystem.RenderScene()
                  └── If MapPopupSceneComponent → MapPopupSceneSystem.RenderScene()
```

**Option 2: SceneSystem Coordinates Rendering (More Consistent)**
```
Game.Draw()
  └── SystemManager.Render(gameTime)
      └── SceneSystem.Render(gameTime)
          └── Iterates scenes in reverse priority order:
              └── For each active scene:
                  ├── If GameSceneComponent → GameSceneSystem.RenderScene()
                  ├── If LoadingSceneComponent → LoadingSceneSystem.RenderScene()
                  ├── If DebugBarSceneComponent → DebugBarSceneSystem.RenderScene()
                  └── If MapPopupSceneComponent → MapPopupSceneSystem.RenderScene()
```

## Detailed Flow Diagrams

### Update Flow (Detailed)

```
┌─────────────────┐
│   Game.Update() │
└────────┬─────────┘
         │
         ▼
┌──────────────────────┐
│ SystemManager.Update │
│                      │
│ 1. Calculate deltaTime│
│ 2. Check if updates   │
│    are blocked        │
│ 3. Call SceneSystem   │
│    .Update()          │
└──────────┬────────────┘
           │
           ▼
┌──────────────────────┐
│  SceneSystem.Update  │
│                      │
│ 1. CleanupDeadEntities│
│ 2. Check BlocksUpdate │
│ 3. Coordinate systems:│
│    - GameSceneSystem  │
│    - LoadingSceneSystem│
│    - DebugBarSceneSystem│
│    - MapPopupSceneSystem│
└──────────┬────────────┘
           │
           ├─────────────────┬─────────────────┬─────────────────┐
           ▼                 ▼                 ▼                 ▼
┌──────────────────┐ ┌──────────────────┐ ┌──────────────────┐ ┌──────────────────┐
│GameSceneSystem   │ │LoadingSceneSystem│ │DebugBarSceneSystem│ │MapPopupSceneSystem│
│                  │ │                  │ │                  │ │                  │
│Query GameScene   │ │Query LoadingScene│ │Query DebugBar    │ │Query Popup       │
│Component entities│ │Component entities│ │Component entities│ │Component entities│
│                  │ │                  │ │                  │ │                  │
│Update game scene │ │Process progress  │ │Update debug bar  │ │Update animation  │
│logic (if any)    │ │queue             │ │logic (if any)    │ │states            │
└──────────────────┘ └──────────────────┘ └──────────────────┘ └──────────────────┘
```

### Render Flow (Detailed)

```
┌─────────────────┐
│   Game.Draw()   │
└────────┬────────┘
         │
         ▼
┌──────────────────────┐
│ SystemManager.Render │
│                      │
│ Calls SceneSystem    │
│ .Render()            │
└──────────┬───────────┘
           │
           ▼
┌──────────────────────┐
│  SceneSystem.Render   │
│                      │
│ 1. Iterate scenes    │
│    (reverse priority)│
│ 2. For each active   │
│    scene:            │
│    - Update shader   │
│      state           │
│    - Determine type  │
│    - Call appropriate│
│      RenderScene()   │
│    - Check BlocksDraw│
└──────────┬───────────┘
           │
           ├─────────────────┬─────────────────┬─────────────────┐
           ▼                 ▼                 ▼                 ▼
┌──────────────────┐ ┌──────────────────┐ ┌──────────────────┐ ┌──────────────────┐
│GameSceneSystem   │ │LoadingSceneSystem│ │DebugBarSceneSystem│ │MapPopupSceneSystem│
│.RenderScene()    │ │.RenderScene()    │ │.RenderScene()    │ │.RenderScene()    │
│                  │ │                  │ │                  │ │                  │
│1. Resolve camera │ │1. Set viewport   │ │1. Set viewport   │ │1. Resolve camera │
│2. Render maps    │ │2. Begin SpriteBatch│ │2. Begin SpriteBatch│ │2. Set viewport   │
│3. Render sprites │ │3. Render loading  │ │3. Render debug bar│ │3. Begin SpriteBatch│
│4. Render borders │ │   screen          │ │4. End SpriteBatch│ │4. Render popups   │
│5. Apply shaders  │ │4. End SpriteBatch│ │                  │ │5. End SpriteBatch│
│                  │ │                  │ │                  │ │                  │
│Delegates to:     │ │Delegates to:     │ │Delegates to:     │ │Renders directly: │
│- MapRenderer     │ │- LoadingScene    │ │- DebugBarRenderer│ │- Popup textures  │
│- SpriteRenderer  │ │  Renderer        │ │                  │ │- Popup text      │
│- MapBorderRender │ │                  │ │                  │ │                  │
└──────────────────┘ └──────────────────┘ └──────────────────┘ └──────────────────┘
```

## Code Structure

### SystemManager

```csharp
public class SystemManager
{
    private SceneSystem _sceneSystem = null!;
    private SceneRendererSystem? _sceneRendererSystem; // Optional if SceneSystem handles rendering
    
    public void Update(GameTime gameTime)
    {
        float deltaTime = (float)gameTime.ElapsedGameTime.TotalSeconds;
        
        // Check if updates are blocked
        bool isUpdateBlocked = IsUpdateBlocked();
        
        if (isUpdateBlocked)
        {
            // Only update SceneSystem (which will coordinate LoadingSceneSystem)
            _sceneSystem.Update(in deltaTime);
        }
        else
        {
            // Update all systems (including SceneSystem)
            _updateSystems.BeforeUpdate(in deltaTime);
            _updateSystems.Update(in deltaTime);
            _updateSystems.AfterUpdate(in deltaTime);
        }
    }
    
    public void Render(GameTime gameTime)
    {
        // Option 1: SceneSystem handles rendering
        _sceneSystem.Render(gameTime);
        
        // Option 2: SceneRendererSystem handles rendering (current)
        // _sceneRendererSystem?.Render(gameTime);
    }
}
```

### SceneSystem

```csharp
public class SceneSystem : BaseSystem<World, float>, IPrioritizedSystem, IDisposable
{
    // Owned scene-specific systems
    private readonly GameSceneSystem _gameSceneSystem;
    private readonly LoadingSceneSystem _loadingSceneSystem;
    private readonly DebugBarSceneSystem _debugBarSceneSystem;
    private readonly MapPopupSceneSystem _mapPopupSceneSystem;
    
    // Lifecycle management
    private readonly List<Entity> _sceneStack = new();
    private readonly Dictionary<string, Entity> _sceneIds = new();
    
    public override void Update(in float deltaTime)
    {
        // 1. Cleanup dead entities
        CleanupDeadEntities();
        
        // 2. Check if updates are blocked
        bool isBlocked = IsUpdateBlocked();
        
        if (isBlocked)
        {
            // Only update loading scene (needs to process progress queue)
            _loadingSceneSystem.Update(in deltaTime);
        }
        else
        {
            // 3. Coordinate scene-specific systems
            _gameSceneSystem.Update(in deltaTime);
            _loadingSceneSystem.Update(in deltaTime);
            _debugBarSceneSystem.Update(in deltaTime);
            _mapPopupSceneSystem.Update(in deltaTime);
        }
    }
    
    public void Render(GameTime gameTime)
    {
        // Iterate scenes in reverse priority order (lowest first, highest last)
        IterateScenesReverse((sceneEntity, sceneComponent) => {
            if (!sceneComponent.IsActive) {
                return true; // Continue
            }
            
            // Update shader state for this scene
            _shaderManagerSystem?.UpdateShaderState(sceneEntity);
            
            // Determine scene type and render
            if (World.Has<GameSceneComponent>(sceneEntity)) {
                _gameSceneSystem.RenderScene(sceneEntity, gameTime);
            } else if (World.Has<LoadingSceneComponent>(sceneEntity)) {
                _loadingSceneSystem.RenderScene(sceneEntity, gameTime);
            } else if (World.Has<DebugBarSceneComponent>(sceneEntity)) {
                _debugBarSceneSystem.RenderScene(sceneEntity, gameTime);
            } else if (World.Has<MapPopupSceneComponent>(sceneEntity)) {
                _mapPopupSceneSystem.RenderScene(sceneEntity, gameTime);
            }
            
            // Check BlocksDraw
            if (sceneComponent.BlocksDraw) {
                return false; // Stop iterating
            }
            
            return true; // Continue
        });
    }
    
    private bool IsUpdateBlocked()
    {
        // Check if any active scene has BlocksUpdate=true
        foreach (var sceneEntity in _sceneStack) {
            if (!World.IsAlive(sceneEntity)) continue;
            
            ref var scene = ref World.Get<SceneComponent>(sceneEntity);
            if (scene.IsActive && !scene.IsPaused && scene.BlocksUpdate) {
                return true;
            }
        }
        return false;
    }
}
```

## Key Points

1. **SystemManager** only knows about SceneSystem
2. **SceneSystem** owns and coordinates scene-specific systems
3. **Update flow**: SystemManager → SceneSystem → Scene-specific systems
4. **Render flow**: SystemManager → SceneSystem → Scene-specific systems (or SceneRendererSystem)
5. **SceneSystem** handles BlocksUpdate logic internally
6. **SceneSystem** handles scene iteration and priority ordering

## Benefits

- ✅ Clear hierarchy: SystemManager → SceneSystem → Scene-specific systems
- ✅ SystemManager doesn't need to know about scene system types
- ✅ SceneSystem owns all scene-related logic
- ✅ Easier to add new scene types (modify SceneSystem only)
- ✅ Better encapsulation

