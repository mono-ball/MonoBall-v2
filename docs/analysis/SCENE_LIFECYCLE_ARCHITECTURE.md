# Why Scenes Don't Handle Their Own Lifecycle/Updates

## Current Architecture: Pure ECS Approach

### What Scenes Are (Current)
Scenes are **data-only entities** with components:
```csharp
Entity sceneEntity = World.Create(
    new SceneComponent { 
        SceneId = "game:main",
        Priority = 50,
        IsActive = true,
        // ... configuration data
    },
    new GameSceneComponent() // Marker component
);
```

### What Handles Behavior (Current)
**Systems** operate on scene entities:
- `SceneSystem` - manages lifecycle (create/destroy/activate/pause)
- `GameSceneSystem` - handles update/render for game scenes
- `MapPopupSceneSystem` - handles update/render for popup scenes
- `SceneRendererSystem` - coordinates rendering across all scenes

## Why This Design?

### 1. **Pure ECS Philosophy**
The architecture follows **pure ECS principles**:
- **Components = Data** (scenes are just data)
- **Systems = Behavior** (systems operate on components)
- **Entities = Containers** (scenes are entities with components)

**Benefits:**
- ✅ Consistent with rest of codebase (everything is ECS)
- ✅ Queryable - can query for "all active scenes", "all paused scenes", etc.
- ✅ Composable - scenes can have multiple components
- ✅ Testable - systems can be tested independently
- ✅ Performance - ECS is optimized for batch operations

### 2. **Separation of Concerns**

**Current Split:**
```
SceneSystem (lifecycle manager)
  ├── CreateScene() - creates entity + components
  ├── DestroyScene() - destroys entity
  ├── SetSceneActive() - updates IsActive flag
  └── SetScenePaused() - updates IsPaused flag

GameSceneSystem (behavior handler)
  ├── Update() - queries GameSceneComponent entities
  └── RenderScene() - renders game scenes

SceneRendererSystem (coordinator)
  └── Render() - iterates scenes, calls appropriate scene system
```

**Why Split?**
- **SceneSystem** doesn't need to know about scene types
- **Scene-specific systems** don't need to manage lifecycle
- **SceneRendererSystem** coordinates without knowing implementation details

### 3. **Mod Extensibility**

With pure ECS, mods can:
- Add new scene types by adding marker components
- Create systems that query for those components
- No need to modify core scene management code

**Example:**
```csharp
// Mod adds CustomSceneComponent
World.Create(
    new SceneComponent { SceneId = "custom:main" },
    new CustomSceneComponent()
);

// Mod creates CustomSceneSystem
public class CustomSceneSystem : BaseSystem<World, float> {
    private readonly QueryDescription _query = new QueryDescription()
        .WithAll<SceneComponent, CustomSceneComponent>();
    
    public override void Update(in float deltaTime) {
        World.Query(in _query, (Entity e, ref SceneComponent scene) => {
            // Handle custom scene updates
        });
    }
}
```

## Alternative: Scenes as Objects

### What It Would Look Like

**Scenes as Classes:**
```csharp
public abstract class Scene : IDisposable
{
    public string SceneId { get; }
    public int Priority { get; set; }
    public bool IsActive { get; set; }
    public bool IsPaused { get; set; }
    
    public abstract void Update(float deltaTime);
    public abstract void Render(GameTime gameTime);
    public virtual void OnCreated() { }
    public virtual void OnDestroyed() { }
    public virtual void OnActivated() { }
    public virtual void OnDeactivated() { }
}

public class GameScene : Scene
{
    private readonly MapRendererSystem _mapRenderer;
    private readonly SpriteRendererSystem _spriteRenderer;
    
    public override void Update(float deltaTime)
    {
        // Update game scene logic
    }
    
    public override void Render(GameTime gameTime)
    {
        _mapRenderer.Render(gameTime);
        _spriteRenderer.Render(gameTime);
    }
}
```

**SceneManager:**
```csharp
public class SceneManager
{
    private readonly List<Scene> _scenes = new();
    
    public void AddScene(Scene scene)
    {
        _scenes.Add(scene);
        scene.OnCreated();
    }
    
    public void Update(float deltaTime)
    {
        foreach (var scene in _scenes.Where(s => s.IsActive && !s.IsPaused))
        {
            scene.Update(deltaTime);
        }
    }
    
    public void Render(GameTime gameTime)
    {
        foreach (var scene in _scenes.OrderByDescending(s => s.Priority))
        {
            if (scene.IsActive)
            {
                scene.Render(gameTime);
            }
        }
    }
}
```

### Trade-offs: Scenes as Objects

**Pros:**
- ✅ Encapsulation - scene owns its behavior
- ✅ Polymorphism - can use Scene base class
- ✅ Familiar OOP pattern
- ✅ Scene can manage its own resources

**Cons:**
- ❌ Breaks ECS consistency - rest of codebase is ECS
- ❌ Harder to query - can't query "all active scenes" easily
- ❌ Less composable - harder to add components to scenes
- ❌ Mods need to create classes, not just components
- ❌ Dual representation - scenes as objects AND entities?

## Hybrid Approach: Scene Objects That Wrap Entities

### What It Could Look Like

```csharp
public class SceneEntity
{
    public Entity Entity { get; }
    public SceneComponent Component { get; }
    
    public SceneEntity(Entity entity, SceneComponent component)
    {
        Entity = entity;
        Component = component;
    }
    
    public virtual void Update(float deltaTime) { }
    public virtual void Render(GameTime gameTime) { }
}

public class GameSceneEntity : SceneEntity
{
    private readonly MapRendererSystem _mapRenderer;
    
    public override void Render(GameTime gameTime)
    {
        _mapRenderer.Render(gameTime);
    }
}
```

**But this creates problems:**
- How do you store SceneEntity objects? (not queryable)
- How do mods extend? (need classes, not just components)
- Dual representation (entity + object)

## Why Current Design Makes Sense

### 1. **Consistency**
Everything in the codebase is ECS:
- Maps are entities with components
- Sprites are entities with components  
- Cameras are entities with components
- **Scenes should be entities with components too**

### 2. **Queryability**
```csharp
// Query for all active, unpaused game scenes
World.Query(in _gameScenesQuery, (Entity e, ref SceneComponent scene) => {
    if (scene.IsActive && !scene.IsPaused) {
        // Process scene
    }
});
```

With objects, you'd need:
```csharp
var activeScenes = _sceneManager.Scenes
    .Where(s => s.IsActive && !s.IsPaused)
    .ToList();
```

### 3. **Mod Extensibility**
Mods can add scene types without modifying core code:
- Add marker component
- Create system that queries for it
- Done!

With objects, mods would need to:
- Create class inheriting from Scene
- Modify SceneManager to handle new type
- Less flexible

### 4. **Performance**
ECS is optimized for:
- Batch operations (update all scenes in one query)
- Cache-friendly memory layout
- Parallel processing

Objects require:
- Virtual method calls (less cache-friendly)
- Individual updates (less batching)

## Current Architecture's Answer

**Why scenes don't handle lifecycle:**
- Scenes are **data** (components), not objects
- `SceneSystem` manages lifecycle (create/destroy/activate/pause)
- Scene-specific systems handle behavior (update/render)

**Why this works:**
- ✅ Consistent with ECS architecture
- ✅ Queryable and composable
- ✅ Mod-extensible
- ✅ Performance-optimized

**The "scene" is the entity + components. The "scene system" is what operates on it.**

## Conclusion

The current architecture follows **pure ECS principles** where:
- **Scenes = Data** (entities with components)
- **Systems = Behavior** (operate on components)
- **Lifecycle = Managed by SceneSystem** (not by scene itself)

This is a **design choice**, not a limitation. The alternative (scenes as objects) would work but would:
- Break ECS consistency
- Reduce queryability
- Make modding harder
- Reduce performance benefits

The current design prioritizes **consistency, extensibility, and performance** over **encapsulation**.

