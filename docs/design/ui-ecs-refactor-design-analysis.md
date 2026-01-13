# UI ECS Refactor Design - Architecture Analysis

## Overview

This document analyzes the UI ECS Refactor Design for architecture issues, Arch ECS/event system violations, and .cursorrules compliance problems.

---

## 🔴 CRITICAL ARCHITECTURE ISSUES

### 1. UIRenderSystem Integration with Scene System

**Issue**: `UIRenderSystem` doesn't implement `ISceneSystem`, so it cannot be called by `SceneSystem.Render()`.

**Current Design**:
```csharp
public class UIRenderSystem : BaseSystem<World, float>
{
    public override void Render(GameTime gameTime, Entity sceneEntity)  // ❌ BaseSystem doesn't have Render()
    {
        // ...
    }
}
```

**Problem**: 
- `BaseSystem<World, float>` doesn't have a `Render()` method
- `SceneSystem.Render()` calls `ISceneSystem.RenderScene(sceneEntity, gameTime)` - different signature
- `UIRenderSystem` won't be called by the scene rendering pipeline

**Solution Options**:

**Option A: UIRenderSystem implements ISceneSystem**
```csharp
public class UIRenderSystem : BaseSystem<World, float>, ISceneSystem
{
    public void RenderScene(Entity sceneEntity, GameTime gameTime)
    {
        // Query UI entities for this scene and render
    }
    
    public void Update(Entity sceneEntity, float deltaTime)
    {
        // No update needed for pure render system
    }
    
    public void ProcessInternal(float deltaTime)
    {
        // No internal processing needed
    }
}
```

**Option B: Scene systems delegate to UIRenderSystem**
```csharp
// In MessageBoxSceneSystem.RenderScene()
public void RenderScene(Entity sceneEntity, GameTime gameTime)
{
    // Delegate to UIRenderSystem
    _uiRenderSystem.RenderScene(sceneEntity, gameTime);
}
```

**Recommendation**: Option A - `UIRenderSystem` should implement `ISceneSystem` and be registered with `SceneSystem` for UI scenes (MessageBoxScene, MapPopupScene, etc.).

---

### 2. Component Namespace Violation

**Issue**: Design puts UI components in `MonoBall.Core.UI.Components`, but `.cursorrules` states components should be in `MonoBall.Core.ECS.Components`.

**Current Design**:
```csharp
namespace MonoBall.Core.UI.Components;  // ❌ Wrong namespace
```

**`.cursorrules` Requirement**:
> **Location**: `ECS/Components/` directory, namespace `MonoBall.Core.ECS.Components`

**However**: Scene components are in `MonoBall.Core.Scenes.Components`, which suggests feature-specific components can have their own namespace.

**Solution**: 
- **Option A**: Put in `MonoBall.Core.UI.Components` (follows scene component pattern)
- **Option B**: Put in `MonoBall.Core.ECS.Components` (follows .cursorrules strictly)

**Recommendation**: Option A - UI components in `MonoBall.Core.UI.Components` to match the pattern of `MonoBall.Core.Scenes.Components`. Update `.cursorrules` to clarify that feature-specific components can have their own namespace.

---

### 3. MessageBoxComponent Role Unclear

**Issue**: Design mentions updating `MessageBoxComponent` but doesn't explain its role in the new architecture.

**Questions**:
- Is `MessageBoxComponent` still needed?
- Does it store text state machine data?
- How does it relate to UI entities (window, text, etc.)?
- Should it be on the scene entity or window entity?

**Solution**: Clarify in design:
- `MessageBoxComponent` stores text processing state (current token index, delay counter, state machine state)
- It's attached to the **scene entity** (not window entity) because it's scene-level state
- UI entities (window, text, sprite) are separate entities with their own components
- `MessageBoxSceneSystem` updates `MessageBoxComponent` state, which affects UI entity visibility/position (e.g., down arrow visibility based on `IsWaitingForInput`)

---

### 4. Missing Integration with SceneSystem

**Issue**: Design doesn't explain how `UIRenderSystem` gets registered and called by `SceneSystem`.

**Current Architecture**:
- `SceneSystem` maintains a registry of `ISceneSystem` implementations
- `SceneSystem.Render()` calls `FindSceneSystem(sceneEntity)?.RenderScene(sceneEntity, gameTime)`
- Scene systems are registered by `SystemManager` and passed to `SceneSystem`

**Solution**: Add to design:
1. `UIRenderSystem` implements `ISceneSystem`
2. `SystemManager` creates `UIRenderSystem` and passes it to `SceneSystem`
3. `SceneSystem` registers `UIRenderSystem` for UI scene types (MessageBoxScene, MapPopupScene)
4. When rendering UI scenes, `SceneSystem` calls `UIRenderSystem.RenderScene()`

---

## 🟡 ARCH ECS ISSUES

### 5. Query Structure Not Shown

**Issue**: Design shows cached queries but doesn't show the actual `QueryDescription` structure.

**Current Design**:
```csharp
private readonly QueryDescription _uiWindowQuery;
private readonly QueryDescription _uiSpriteQuery;
private readonly QueryDescription _uiTextQuery;
```

**Problem**: Missing actual query definitions - what components are queried?

**Solution**: Show complete query definitions:
```csharp
// In constructor
_uiWindowQuery = new QueryDescription()
    .WithAll<WindowComponent, UIElementComponent, PositionComponent, RenderableComponent>();

_uiSpriteQuery = new QueryDescription()
    .WithAll<SpriteComponent, UIElementComponent, PositionComponent, RenderableComponent>();

_uiTextQuery = new QueryDescription()
    .WithAll<UITextComponent, UIElementComponent, PositionComponent, RenderableComponent>();
```

---

### 6. Relationship Query Pattern Unclear

**Issue**: Design shows relationship iteration but doesn't show how to filter by scene entity.

**Current Design**:
```csharp
ref var uiElements = ref sceneEntity.GetRelationships<OwnsUIElement>();
foreach (var uiElement in uiElements)
{
    // Render UI element
}
```

**Problem**: This iterates all relationships, but we need to verify the entity is still alive and belongs to the correct scene.

**Solution**: Show proper pattern with validation:
```csharp
ref var uiElements = ref sceneEntity.GetRelationships<OwnsUIElement>();
foreach (var uiElement in uiElements)
{
    if (!World.IsAlive(uiElement))
        continue;
    
    if (!World.Has<UIElementComponent>(uiElement))
        continue;  // Not a valid UI element
    
    // Render UI element
    RenderUIElement(uiElement);
}
```

---

### 7. Missing Reusable Collections Pattern

**Issue**: Design doesn't mention caching collections for hot paths (Update/Render methods).

**`.cursorrules` Requirement**:
> **Reusable collections**: Cache collections as instance fields (e.g., `List<T>`) to avoid allocations in hot paths - clear and reuse them in Update/Render methods

**Solution**: Add to `UIRenderSystem`:
```csharp
public class UIRenderSystem : BaseSystem<World, float>, ISceneSystem
{
    // Reusable collection for collecting UI elements to render
    private readonly List<(Entity entity, UIElementComponent ui, int zOrder)> _renderList = new();
    
    public void RenderScene(Entity sceneEntity, GameTime gameTime)
    {
        // Clear reusable collection
        _renderList.Clear();
        
        // Collect UI elements
        ref var uiElements = ref sceneEntity.GetRelationships<OwnsUIElement>();
        foreach (var uiElement in uiElements)
        {
            if (!World.IsAlive(uiElement) || !World.Has<UIElementComponent>(uiElement))
                continue;
            
            ref var ui = ref World.Get<UIElementComponent>(uiElement);
            _renderList.Add((uiElement, ui, ui.ZOrder));
        }
        
        // Sort by z-order
        _renderList.Sort((a, b) => a.zOrder.CompareTo(b.zOrder));
        
        // Render in order
        foreach (var (entity, ui, _) in _renderList)
        {
            RenderUIElement(entity);
        }
    }
}
```

---

### 8. Component Access Patterns Not Shown

**Issue**: Design doesn't show how to safely access components in queries.

**Solution**: Show proper component access with validation:
```csharp
World.Query(
    in _uiWindowQuery,
    (Entity entity, ref WindowComponent window, ref UIElementComponent ui, ref PositionComponent pos, ref RenderableComponent render) =>
    {
        if (!render.IsVisible)
            return;
        
        // Render window
        RenderWindow(entity, ref window, ref pos);
    }
);
```

---

## 🟠 .CURSORRULES COMPLIANCE ISSUES

### 9. Backward Compatibility Section Violates Rule

**Issue**: Design has a "Backward Compatibility" section, which violates the "NO BACKWARD COMPATIBILITY" rule.

**`.cursorrules` Rule**:
> **NO BACKWARD COMPATIBILITY** - Refactor APIs freely, break existing code if needed, update all call sites

**Current Design**:
```
### Backward Compatibility

**Migration Period:**
- Support both `SceneOwnershipComponent` and relationships during migration
- `UIRenderSystem` can query both patterns
- Gradually migrate systems one at a time
```

**Solution**: Remove backward compatibility section. Instead:
- Migrate all systems immediately
- Remove `SceneOwnershipComponent` usage
- Update all call sites to use relationships
- Fail fast if old patterns are detected

---

### 10. Missing Fail-Fast Validation

**Issue**: Design doesn't show validation and exception handling for missing components/relationships.

**`.cursorrules` Requirement**:
> **NO FALLBACK CODE** - Fail fast with clear exceptions, never silently degrade

**Solution**: Add validation examples:
```csharp
public void RenderScene(Entity sceneEntity, GameTime gameTime)
{
    if (!World.IsAlive(sceneEntity))
        throw new InvalidOperationException($"Scene entity {sceneEntity.Id} is not alive.");
    
    if (!World.Has<SceneComponent>(sceneEntity))
        throw new InvalidOperationException($"Scene entity {sceneEntity.Id} does not have SceneComponent.");
    
    // Validate UI elements exist
    ref var uiElements = ref sceneEntity.GetRelationships<OwnsUIElement>();
    if (uiElements.Count == 0)
    {
        _logger.Warning("Scene {SceneId} has no UI elements to render", sceneEntity.Id);
        return;  // Not an error - scene might not have UI yet
    }
    
    // Render...
}
```

---

### 11. Missing IDisposable for Event Subscriptions

**Issue**: Design doesn't mention if `UIRenderSystem` needs to subscribe to events and implement `IDisposable`.

**`.cursorrules` Requirement**:
> **Event Subscriptions**: MUST implement `IDisposable` and unsubscribe in `Dispose()` to prevent leaks

**Solution**: If `UIRenderSystem` subscribes to events (e.g., `UIElementCreatedEvent`, `UIElementDestroyedEvent`), add:
```csharp
public class UIRenderSystem : BaseSystem<World, float>, ISceneSystem, IDisposable
{
    private readonly List<IDisposable> _subscriptions = new();
    private bool _disposed;
    
    public UIRenderSystem(World world, ...) : base(world)
    {
        // Subscribe to events
        _subscriptions.Add(EventBus.Subscribe<UIElementCreatedEvent>(OnUIElementCreated));
    }
    
    public new void Dispose() => Dispose(true);
    
    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed && disposing)
        {
            foreach (var subscription in _subscriptions)
                subscription.Dispose();
        }
        _disposed = true;
    }
}
```

---

### 12. Missing Exception Documentation

**Issue**: Design doesn't document exceptions that methods can throw.

**`.cursorrules` Requirement**:
> **XML Documentation**: Document all public APIs with XML comments (`<summary>`, `<param>`, `<returns>`, `<exception>`)

**Solution**: Add exception documentation:
```csharp
/// <summary>
///     Renders UI elements for a specific scene.
/// </summary>
/// <param name="sceneEntity">The scene entity to render UI for.</param>
/// <param name="gameTime">The game time.</param>
/// <exception cref="InvalidOperationException">Thrown if scene entity is not alive or missing SceneComponent.</exception>
public void RenderScene(Entity sceneEntity, GameTime gameTime)
{
    // ...
}
```

---

### 13. Missing System Priority

**Issue**: Design doesn't mention if `UIRenderSystem` needs `IPrioritizedSystem` or system priority.

**Current Pattern**: Scene systems like `GameSceneSystem` implement `IPrioritizedSystem`.

**Solution**: Clarify if `UIRenderSystem` needs priority:
- If it's called via `ISceneSystem.RenderScene()`, it doesn't need priority (scene priority handles ordering)
- If it's a standalone system in the update loop, it needs `IPrioritizedSystem`

**Recommendation**: Since `UIRenderSystem` implements `ISceneSystem` and is called by `SceneSystem`, it doesn't need `IPrioritizedSystem`.

---

### 14. Missing Dependency Injection Validation

**Issue**: Design doesn't show constructor parameter validation.

**`.cursorrules` Requirement**:
> **Dependency Injection**: Required dependencies in constructor, throw `ArgumentNullException` for null

**Solution**: Add validation:
```csharp
public UIRenderSystem(
    World world,
    GraphicsDevice graphicsDevice,
    SpriteBatch spriteBatch,
    IResourceManager resourceManager,
    ILogger logger
) : base(world)
{
    _graphicsDevice = graphicsDevice ?? throw new ArgumentNullException(nameof(graphicsDevice));
    _spriteBatch = spriteBatch ?? throw new ArgumentNullException(nameof(spriteBatch));
    _resourceManager = resourceManager ?? throw new ArgumentNullException(nameof(resourceManager));
    _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    
    // Cache queries in constructor
    _uiWindowQuery = new QueryDescription()...
}
```

---

## 🟢 MINOR ISSUES / CLARIFICATIONS

### 15. UITextComponent vs MessageBoxComponent Text

**Issue**: Design shows `UITextComponent` with `Text` property, but message box text is processed character-by-character. How does this work?

**Clarification Needed**:
- Does `UITextComponent.Text` store the full text or current visible text?
- How does character-by-character printing update `UITextComponent`?
- Should `MessageBoxComponent` store processing state and `UITextComponent` store display state?

**Recommendation**: 
- `MessageBoxComponent`: Stores text processing state (tokens, current index, state machine)
- `UITextComponent`: Stores display text (current visible characters, updated by `MessageBoxSceneSystem`)

---

### 16. WindowComponent Position Duplication

**Issue**: `WindowComponent` has `InteriorX` and `InteriorY`, but entities also have `PositionComponent`. This is duplication.

**Solution**: 
- Remove `InteriorX`/`InteriorY` from `WindowComponent`
- Use `PositionComponent.Position` for window position
- `WindowComponent` only stores dimensions and style IDs

---

### 17. Relationship Data Storage

**Issue**: Design mentions relationships can store data but doesn't show when this is needed.

**Clarification**: 
- Marker relationships (`OwnsUIElement`, `ContainsUIElement`) are sufficient for most cases
- Only add data to relationships if needed (e.g., `ZOrder`, `LayoutConstraints`)
- Prefer storing data in components rather than relationships

---

## 📋 SUMMARY OF REQUIRED FIXES

### Critical (Must Fix)
1. ✅ Make `UIRenderSystem` implement `ISceneSystem`
2. ✅ Clarify component namespace (UI.Components vs ECS.Components)
3. ✅ Clarify `MessageBoxComponent` role in new architecture
4. ✅ Show integration with `SceneSystem` registration

### Important (Should Fix)
5. ✅ Show complete query definitions
6. ✅ Show proper relationship query patterns with validation
7. ✅ Add reusable collections pattern
8. ✅ Remove backward compatibility section
9. ✅ Add fail-fast validation examples
10. ✅ Add IDisposable if events are subscribed

### Minor (Nice to Have)
11. ✅ Add exception documentation
12. ✅ Clarify system priority needs
13. ✅ Add dependency injection validation
14. ✅ Clarify UITextComponent vs MessageBoxComponent text handling
15. ✅ Remove position duplication from WindowComponent

---

## 🔧 RECOMMENDED DESIGN UPDATES

1. **Update UIRenderSystem signature** to implement `ISceneSystem`
2. **Add complete query definitions** with component lists
3. **Add reusable collections** for hot paths
4. **Remove backward compatibility** section
5. **Add validation and exception handling** examples
6. **Clarify component responsibilities** (MessageBoxComponent vs UITextComponent)
7. **Show SystemManager integration** (how UIRenderSystem is created and registered)
8. **Add IDisposable pattern** if events are used
9. **Document exceptions** in XML comments
10. **Show proper relationship iteration** with validation
