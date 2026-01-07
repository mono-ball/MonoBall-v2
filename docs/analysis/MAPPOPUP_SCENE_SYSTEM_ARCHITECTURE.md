# MapPopupSceneSystem Architecture

## Current Architecture (Problematic)

### Systems Registered in SystemManager:
```
MapPopupOrchestratorSystem
  └── Listens to MapTransitionEvent, fires MapPopupShowEvent ✅

MapPopupSystem (registered in SystemManager)
  ├── Listens to MapPopupShowEvent/HideEvent (lifecycle)
  ├── Creates popup entity + scene entity
  ├── Update() - updates PopupAnimationComponent
  └── RenderScene() - coordinates rendering

MapPopupRendererSystem (registered in SystemManager)
  └── Render() - renders popups (called by MapPopupSystem.RenderScene())
```

### Problems:
- ❌ Two systems registered in SystemManager for popup functionality
- ❌ MapPopupSystem handles lifecycle + updates + rendering coordination
- ❌ MapPopupRendererSystem only renders (unnecessary separation)
- ❌ Inconsistent with other scene systems pattern

## Proposed Architecture (Better)

### Systems Registered in SystemManager:
```
MapPopupOrchestratorSystem
  └── Listens to MapTransitionEvent, fires MapPopupShowEvent ✅

MapPopupSceneSystem (registered in SystemManager)
  ├── Listens to MapPopupShowEvent/HideEvent (lifecycle)
  ├── Creates popup entity + scene entity
  ├── Update() - updates PopupAnimationComponent (queries popup entities)
  └── RenderScene() - renders popups (queries popup scene entities)
```

### Benefits:
- ✅ Single system registered in SystemManager
- ✅ Follows same pattern as GameSceneSystem, LoadingSceneSystem, DebugBarSceneSystem
- ✅ Scene system owns all popup-related logic
- ✅ No unnecessary separation between systems

## Implementation

### MapPopupSceneSystem Structure

```csharp
namespace MonoBall.Core.Scenes.Systems
{
    /// <summary>
    /// System that handles lifecycle, updates, and rendering for MapPopupScene entities.
    /// Queries for MapPopupSceneComponent entities and processes them.
    /// </summary>
    public class MapPopupSceneSystem : BaseSystem<World, float>, IPrioritizedSystem, IDisposable
    {
        private readonly GraphicsDevice _graphicsDevice;
        private readonly SpriteBatch _spriteBatch;
        private readonly SceneSystem _sceneSystem;
        private readonly FontService _fontService;
        private readonly IModManager _modManager;
        private readonly ILogger _logger;
        
        private Entity? _currentPopupEntity;
        private Entity? _currentPopupSceneEntity;
        private bool _disposed = false;
        
        // Texture cache for popup rendering
        private readonly Dictionary<string, Texture2D> _textureCache = new();
        
        // Cached queries
        private readonly QueryDescription _popupScenesQuery = new QueryDescription().WithAll<
            SceneComponent,
            MapPopupSceneComponent
        >();
        
        private readonly QueryDescription _popupQuery = new QueryDescription().WithAll<
            MapPopupComponent,
            PopupAnimationComponent
        >();
        
        private readonly QueryDescription _cameraQuery = new QueryDescription().WithAll<
            CameraComponent
        >();
        
        public int Priority => SystemPriority.MapPopupScene;
        
        public MapPopupSceneSystem(
            World world,
            GraphicsDevice graphicsDevice,
            SpriteBatch spriteBatch,
            SceneSystem sceneSystem,
            FontService fontService,
            IModManager modManager,
            ILogger logger
        ) : base(world)
        {
            _graphicsDevice = graphicsDevice ?? throw new ArgumentNullException(nameof(graphicsDevice));
            _spriteBatch = spriteBatch ?? throw new ArgumentNullException(nameof(spriteBatch));
            _sceneSystem = sceneSystem ?? throw new ArgumentNullException(nameof(sceneSystem));
            _fontService = fontService ?? throw new ArgumentNullException(nameof(fontService));
            _modManager = modManager ?? throw new ArgumentNullException(nameof(modManager));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            
            // Subscribe to events for lifecycle management
            EventBus.Subscribe<MapPopupShowEvent>(OnMapPopupShow);
            EventBus.Subscribe<MapPopupHideEvent>(OnMapPopupHide);
        }
        
        /// <summary>
        /// Updates popup animation states.
        /// Queries for popup entities (not scene entities) and updates their animation components.
        /// </summary>
        public override void Update(in float deltaTime)
        {
            float dt = deltaTime;
            World.Query(in _popupQuery, (Entity entity, ref PopupAnimationComponent anim) => {
                // Update animation state machine
                anim.ElapsedTime += dt;
                
                switch (anim.State)
                {
                    case PopupAnimationState.SlidingDown:
                        // ... animation logic
                        break;
                    case PopupAnimationState.Paused:
                        // ... animation logic
                        break;
                    case PopupAnimationState.SlidingUp:
                        // ... animation logic
                        if (progress >= 1.0f) {
                            // Fire MapPopupHideEvent when animation completes
                            var hideEvent = new MapPopupHideEvent { PopupEntity = entity };
                            EventBus.Send(ref hideEvent);
                        }
                        break;
                }
            });
        }
        
        /// <summary>
        /// Handles MapPopupShowEvent by creating popup entity and scene.
        /// </summary>
        private void OnMapPopupShow(ref MapPopupShowEvent evt)
        {
            // ... create popup scene entity
            // ... create popup entity with MapPopupComponent + PopupAnimationComponent
            // Store references in _currentPopupEntity and _currentPopupSceneEntity
        }
        
        /// <summary>
        /// Handles MapPopupHideEvent by destroying popup entity and scene.
        /// </summary>
        private void OnMapPopupHide(ref MapPopupHideEvent evt)
        {
            // ... destroy popup entity and scene entity
        }
        
        /// <summary>
        /// Renders a single map popup scene. Called by SceneRendererSystem (coordinator) for a single scene.
        /// </summary>
        public void RenderScene(Entity sceneEntity, GameTime gameTime)
        {
            // Verify this is actually a map popup scene
            if (!World.Has<MapPopupSceneComponent>(sceneEntity))
            {
                return;
            }
            
            ref var scene = ref World.Get<SceneComponent>(sceneEntity);
            if (!scene.IsActive)
            {
                return;
            }
            
            // Resolve camera
            CameraComponent? camera = ResolveCamera(scene);
            if (!camera.HasValue)
            {
                return;
            }
            
            // Render popups (query for popup entities, render them)
            RenderMapPopupScene(sceneEntity, ref scene, gameTime, camera.Value);
        }
        
        /// <summary>
        /// Renders popups for the scene.
        /// </summary>
        private void RenderMapPopupScene(
            Entity sceneEntity,
            ref SceneComponent scene,
            GameTime gameTime,
            CameraComponent camera
        )
        {
            // Set up viewport and SpriteBatch
            // Query for popup entities and render them
            // (Move rendering logic from MapPopupRendererSystem here)
        }
        
        // ... helper methods for rendering (texture loading, border drawing, etc.)
    }
}
```

## Migration Steps

### Step 1: Create MapPopupSceneSystem
- Move lifecycle logic from `MapPopupSystem.OnMapPopupShow()` → `MapPopupSceneSystem.OnMapPopupShow()`
- Move lifecycle logic from `MapPopupSystem.OnMapPopupHide()` → `MapPopupSceneSystem.OnMapPopupHide()`
- Move update logic from `MapPopupSystem.Update()` → `MapPopupSceneSystem.Update()`
- Move rendering logic from `MapPopupRendererSystem.Render()` → `MapPopupSceneSystem.RenderScene()`

### Step 2: Update SystemManager
- Remove `MapPopupSystem` creation and registration
- Remove `MapPopupRendererSystem` creation
- Create `MapPopupSceneSystem` instead
- Register `MapPopupSceneSystem` as update system
- Pass `MapPopupSceneSystem` to `SceneRendererSystem` constructor

### Step 3: Update SceneRendererSystem
- Replace `_mapPopupSystem.RenderScene()` with `_mapPopupSceneSystem.RenderScene()`
- Update constructor to take `MapPopupSceneSystem` instead of `MapPopupSystem`

### Step 4: Delete Old Systems
- Delete `MapPopupSystem.cs`
- Delete `MapPopupRendererSystem.cs`

## Comparison

### Before:
```
SystemManager creates:
  ├── MapPopupSystem (lifecycle + updates + render coordination)
  └── MapPopupRendererSystem (rendering)

SceneRendererSystem uses:
  └── MapPopupSystem.RenderScene()
```

### After:
```
SystemManager creates:
  └── MapPopupSceneSystem (lifecycle + updates + rendering)

SceneRendererSystem uses:
  └── MapPopupSceneSystem.RenderScene()
```

## Benefits

1. **Consistency** - Follows same pattern as other scene systems
2. **Simplicity** - One system instead of two
3. **Cohesion** - All popup logic in one place
4. **Clarity** - Scene system owns scene-related functionality

## Key Insight

**Scene systems should own their functionality:**
- `GameSceneSystem` - owns game scene rendering
- `LoadingSceneSystem` - owns loading scene updates + rendering
- `DebugBarSceneSystem` - owns debug bar rendering
- `MapPopupSceneSystem` - should own popup lifecycle + updates + rendering

This is the pattern you're establishing, and it makes sense!

