# SceneSystem as Coordinator Architecture

## Current Architecture (Problematic)

### SystemManager Updates All Systems Independently:
```
SystemManager.Update()
  └── Updates all systems in priority order:
      ├── SceneSystem.Update()           (lifecycle cleanup)
      ├── GameSceneSystem.Update()       (queries game scenes)
      ├── LoadingSceneSystem.Update()   (processes loading progress)
      ├── DebugBarSceneSystem.Update()   (queries debug bar scenes)
      └── MapPopupSceneSystem.Update()   (updates popup animations)
```

**Problems:**
- ❌ All scene systems registered independently in SystemManager
- ❌ SystemManager needs to know about all scene system types
- ❌ Scene systems don't have a clear owner/coordinator
- ❌ Harder to manage scene-related systems as a group

## Proposed Architecture (Better)

### SystemManager Updates SceneSystem, Which Coordinates Scene-Specific Systems:
```
SystemManager.Update()
  └── SceneSystem.Update()
      └── Coordinates scene-specific systems:
          ├── GameSceneSystem.Update()
          ├── LoadingSceneSystem.Update()
          ├── DebugBarSceneSystem.Update()
          └── MapPopupSceneSystem.Update()
```

**Benefits:**
- ✅ Only SceneSystem registered in SystemManager
- ✅ SceneSystem owns/manages scene-specific systems
- ✅ SystemManager doesn't need to know about scene system types
- ✅ Clear hierarchy: SystemManager → SceneSystem → Scene-specific systems

## Implementation

### SceneSystem as Coordinator

```csharp
namespace MonoBall.Core.Scenes.Systems
{
    /// <summary>
    /// System responsible for managing scene lifecycle and coordinating scene-specific systems.
    /// Owns and coordinates GameSceneSystem, LoadingSceneSystem, DebugBarSceneSystem, etc.
    /// </summary>
    public class SceneSystem : BaseSystem<World, float>, IPrioritizedSystem, IDisposable
    {
        // Scene-specific systems (owned by SceneSystem)
        private readonly GameSceneSystem _gameSceneSystem;
        private readonly LoadingSceneSystem _loadingSceneSystem;
        private readonly DebugBarSceneSystem _debugBarSceneSystem;
        private readonly MapPopupSceneSystem _mapPopupSceneSystem;
        
        // Lifecycle management (existing)
        private readonly List<Entity> _sceneStack = new List<Entity>();
        private readonly Dictionary<string, Entity> _sceneIds = new Dictionary<string, Entity>();
        
        public SceneSystem(
            World world,
            ILogger logger,
            GameSceneSystem gameSceneSystem,
            LoadingSceneSystem loadingSceneSystem,
            DebugBarSceneSystem debugBarSceneSystem,
            MapPopupSceneSystem mapPopupSceneSystem
        ) : base(world)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _gameSceneSystem = gameSceneSystem ?? throw new ArgumentNullException(nameof(gameSceneSystem));
            _loadingSceneSystem = loadingSceneSystem ?? throw new ArgumentNullException(nameof(loadingSceneSystem));
            _debugBarSceneSystem = debugBarSceneSystem ?? throw new ArgumentNullException(nameof(debugBarSceneSystem));
            _mapPopupSceneSystem = mapPopupSceneSystem ?? throw new ArgumentNullException(nameof(mapPopupSceneSystem));
        }
        
        /// <summary>
        /// Updates scene system and coordinates scene-specific systems.
        /// </summary>
        public override void Update(in float deltaTime)
        {
            // Clean up dead entities
            CleanupDeadEntities();
            
            // Coordinate scene-specific systems
            _gameSceneSystem.Update(in deltaTime);
            _loadingSceneSystem.Update(in deltaTime);
            _debugBarSceneSystem.Update(in deltaTime);
            _mapPopupSceneSystem.Update(in deltaTime);
        }
        
        // ... existing lifecycle methods (CreateScene, DestroyScene, etc.)
    }
}
```

### SystemManager Changes

```csharp
// SystemManager only registers SceneSystem
private void CreateSceneSpecificSystems()
{
    // Create scene-specific systems FIRST
    _gameSceneSystem = new GameSceneSystem(...);
    _loadingSceneSystem = new LoadingSceneSystem(...);
    _debugBarSceneSystem = new DebugBarSceneSystem(...);
    _mapPopupSceneSystem = new MapPopupSceneSystem(...);
    
    // Create SceneSystem with references to scene-specific systems
    _sceneSystem = new SceneSystem(
        _world,
        LoggerFactory.CreateLogger<SceneSystem>(),
        _gameSceneSystem,
        _loadingSceneSystem,
        _debugBarSceneSystem,
        _mapPopupSceneSystem
    );
    
    // Only register SceneSystem (not scene-specific systems)
    RegisterUpdateSystem(_sceneSystem);
    
    // SceneRendererSystem still needs references for rendering
    _sceneRendererSystem = new SceneRendererSystem(
        _world,
        _graphicsDevice,
        _sceneSystem,
        _gameSceneSystem,  // Still needed for RenderScene()
        _loadingSceneSystem,
        _debugBarSceneSystem,
        _mapPopupSceneSystem,
        LoggerFactory.CreateLogger<SceneRendererSystem>(),
        _shaderManagerSystem
    );
}
```

## Comparison

### Before:
```
SystemManager
  ├── Registers SceneSystem
  ├── Registers GameSceneSystem
  ├── Registers LoadingSceneSystem
  ├── Registers DebugBarSceneSystem
  └── Registers MapPopupSceneSystem

Update Flow:
  SystemManager.Update()
    └── Updates all systems independently
```

### After:
```
SystemManager
  └── Registers SceneSystem (only)

SceneSystem
  ├── Owns GameSceneSystem
  ├── Owns LoadingSceneSystem
  ├── Owns DebugBarSceneSystem
  └── Owns MapPopupSceneSystem

Update Flow:
  SystemManager.Update()
    └── SceneSystem.Update()
        └── Coordinates scene-specific systems
```

## Benefits

1. **Clear Ownership** - SceneSystem owns scene-specific systems
2. **Simpler SystemManager** - Only one scene system registered
3. **Better Encapsulation** - Scene-related systems grouped together
4. **Easier Management** - Add new scene systems by modifying SceneSystem only
5. **Clear Hierarchy** - SystemManager → SceneSystem → Scene-specific systems

## Considerations

### SceneRendererSystem Still Needs References

**SceneRendererSystem** still needs references to scene-specific systems for rendering:
```csharp
// SceneRendererSystem needs to call RenderScene() on each system
if (World.Has<GameSceneComponent>(sceneEntity)) {
    _gameSceneSystem.RenderScene(sceneEntity, gameTime);
}
```

**Options:**
1. **Keep references** - SceneRendererSystem still gets references (current approach)
2. **SceneSystem coordinates rendering** - Move rendering coordination into SceneSystem
3. **SceneSystem exposes systems** - SceneSystem provides access to scene-specific systems

### Update Ordering

**SceneSystem.Update() coordinates systems:**
- Can control update order
- Can skip systems based on scene state
- Can handle BlocksUpdate logic internally

**Example:**
```csharp
public override void Update(in float deltaTime)
{
    CleanupDeadEntities();
    
    // Check if updates are blocked
    bool isBlocked = IsUpdateBlocked();
    
    if (isBlocked)
    {
        // Only update loading scene (needs to process progress queue)
        _loadingSceneSystem.Update(in deltaTime);
    }
    else
    {
        // Update all scene systems
        _gameSceneSystem.Update(in deltaTime);
        _loadingSceneSystem.Update(in deltaTime);
        _debugBarSceneSystem.Update(in deltaTime);
        _mapPopupSceneSystem.Update(in deltaTime);
    }
}
```

## Conclusion

**Your proposed architecture makes sense:**
- SystemManager → SceneSystem → Scene-specific systems
- Clear ownership and hierarchy
- Simpler SystemManager
- Better encapsulation

**This is a better architecture than the current approach!**

