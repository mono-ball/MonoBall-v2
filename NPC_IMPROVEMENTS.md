# NPC Improvements Based on Player Entity Architecture

## Overview
This document outlines improvements that can be applied to NPCs based on the architecture patterns established for the player entity implementation.

---

## 1. Event-Based Animation Changes

### Current State
- `NpcAnimationChangedEvent` exists and `NpcAnimationSystem` subscribes to it
- **Problem**: The event is never published anywhere
- NPC animations change internally in `NpcAnimationSystem.UpdateAnimation()` but no event is fired

### Improvement
**Publish `NpcAnimationChangedEvent` when animations change:**

```csharp
// In NpcAnimationSystem.UpdateAnimation()
// When animation name changes (not just frame index)
if (oldAnimationName != anim.CurrentAnimationName)
{
    var evt = new NpcAnimationChangedEvent
    {
        NpcEntity = entity,
        NpcId = npc.NpcId,
        OldAnimationName = oldAnimationName,
        NewAnimationName = anim.CurrentAnimationName
    };
    EventBus.Publish(evt);
}
```

**Benefits:**
- Other systems can react to NPC animation changes (e.g., sound effects, visual effects)
- Consistent with player entity event patterns
- Enables decoupled communication

**Alternative:** Use generic `SpriteAnimationChangedEvent` for both NPCs and Players (see #3)

---

## 2. Helper Method for Safe Entity Creation

### Current State
- NPCs are created inline in `MapLoaderSystem.CreateNpcs()` (lines 721-745)
- Validation is done inline but scattered
- No centralized method for creating NPC entities

### Improvement
**Create helper method `CreateNpcEntity()` in MapLoaderSystem:**

```csharp
/// <summary>
/// Creates an NPC entity with all required components.
/// Validates inputs and throws exceptions if invalid.
/// </summary>
/// <param name="npcDef">The NPC definition.</param>
/// <param name="mapDefinition">The map definition.</param>
/// <param name="mapTilePosition">The map position in tile coordinates.</param>
/// <returns>The created NPC entity.</returns>
/// <exception cref="ArgumentNullException">Thrown if npcDef or mapDefinition is null.</exception>
/// <exception cref="ArgumentException">Thrown if sprite definition or animation is invalid.</exception>
private Entity CreateNpcEntity(
    NpcDefinition npcDef,
    MapDefinition mapDefinition,
    Vector2 mapTilePosition
)
{
    if (npcDef == null)
        throw new ArgumentNullException(nameof(npcDef));
    if (mapDefinition == null)
        throw new ArgumentNullException(nameof(mapDefinition));
    if (_spriteLoader == null)
        throw new InvalidOperationException("SpriteLoader is required to create NPCs");

    // Validate sprite definition
    if (!_spriteLoader.ValidateSpriteDefinition(npcDef.SpriteId))
    {
        throw new ArgumentException(
            $"Sprite definition not found: {npcDef.SpriteId}",
            nameof(npcDef)
        );
    }

    // Map direction to animation
    string animationName = MapDirectionToAnimation(npcDef.Direction);

    // Validate animation
    if (!_spriteLoader.ValidateAnimation(npcDef.SpriteId, animationName))
    {
        Log.Warning(
            "Animation '{AnimationName}' not found for sprite {SpriteId}, defaulting to 'face_south'",
            animationName,
            npcDef.SpriteId
        );
        animationName = "face_south";
    }

    // Get sprite definition and animation for flip state
    var spriteDefinition = _spriteLoader.GetSpriteDefinition(npcDef.SpriteId);
    var animation = spriteDefinition?.Animations?.FirstOrDefault(a => a.Name == animationName);
    bool flipHorizontal = animation?.FlipHorizontal ?? false;

    // Calculate position
    Vector2 mapPixelPosition = new Vector2(
        mapTilePosition.X * mapDefinition.TileWidth,
        mapTilePosition.Y * mapDefinition.TileHeight
    );
    Vector2 npcPixelPosition = new Vector2(
        mapPixelPosition.X + npcDef.X,
        mapPixelPosition.Y + npcDef.Y
    );

    // Create entity with all required components
    var npcEntity = World.Create(
        new NpcComponent
        {
            NpcId = npcDef.NpcId,
            Name = npcDef.Name,
            SpriteId = npcDef.SpriteId,
            MapId = mapDefinition.Id,
            Elevation = npcDef.Elevation,
            VisibilityFlag = npcDef.VisibilityFlag,
        },
        new SpriteAnimationComponent
        {
            CurrentAnimationName = animationName,
            CurrentFrameIndex = 0,
            ElapsedTime = 0.0f,
            FlipHorizontal = flipHorizontal,
        },
        new PositionComponent { Position = npcPixelPosition },
        new RenderableComponent
        {
            IsVisible = true,
            RenderOrder = npcDef.Elevation,
            Opacity = 1.0f,
        }
    );

    // Preload sprite texture
    _spriteLoader.GetSpriteTexture(npcDef.SpriteId);

    // Fire NpcLoadedEvent
    var loadedEvent = new NpcLoadedEvent
    {
        NpcEntity = npcEntity,
        NpcId = npcDef.NpcId,
        MapId = mapDefinition.Id,
    };
    EventBus.Publish(loadedEvent);

    return npcEntity;
}
```

**Benefits:**
- Centralized validation and error handling
- Reusable if NPCs need to be created elsewhere
- Consistent with `PlayerSystem.CreatePlayerEntity()` pattern
- Easier to test and maintain

---

## 3. Generic Animation Change Event

### Current State
- `NpcAnimationChangedEvent` is NPC-specific
- Player plan includes generic `SpriteAnimationChangedEvent`
- Two separate events for similar functionality

### Improvement Options

**Option A: Use Generic Event (Recommended)**
- Replace `NpcAnimationChangedEvent` with generic `SpriteAnimationChangedEvent`
- Works for both NPCs and Players
- Simpler event system

```csharp
public struct SpriteAnimationChangedEvent
{
    public Entity Entity { get; set; }
    public string OldAnimationName { get; set; }
    public string NewAnimationName { get; set; }
    // Optional: EntityType enum or string to distinguish NPC vs Player if needed
}
```

**Option B: Keep Both (More Explicit)**
- Keep `NpcAnimationChangedEvent` for NPC-specific logic
- Add `PlayerAnimationChangedEvent` for player-specific logic
- More explicit but more events to maintain

**Recommendation:** Use Option A (generic event) unless there's a strong need for NPC-specific vs Player-specific handling.

---

## 4. SpriteSheetComponent for NPCs (Optional)

### Current State
- NPCs use `NpcComponent.SpriteId` directly (single sprite per NPC)
- Players use `SpriteSheetComponent` for multiple sprite sheets

### Question: Do NPCs Need Multiple Sprite Sheets?

**Potential Use Cases:**
- NPCs that change outfits (e.g., seasonal clothing)
- NPCs with different forms (e.g., day/night variants)
- NPCs that transform (e.g., battle-ready vs casual)

**If Yes:**
- Add `SpriteSheetComponent` to NPCs that need it
- Update `NpcComponent` to remove `SpriteId` (or keep for backward compatibility)
- Update `SpriteRendererSystem` and `SpriteAnimationSystem` to handle both patterns

**If No (Most Likely):**
- Keep current design: NPCs use `NpcComponent.SpriteId`
- Players use `SpriteSheetComponent`
- Systems handle both patterns (already in plan)

**Recommendation:** Keep current design unless there's a specific need for NPC sprite sheet switching. The architecture already supports both patterns.

---

## 5. Component Initialization Validation

### Current State
- NPC creation validates sprite and animation inline
- No compile-time safety for required components
- Missing components cause runtime failures

### Improvement
**Add validation helper method:**

```csharp
/// <summary>
/// Validates that an NPC entity has all required components.
/// </summary>
/// <param name="npcEntity">The NPC entity to validate.</param>
/// <returns>True if valid, false otherwise.</returns>
private bool ValidateNpcEntity(Entity npcEntity)
{
    if (!World.Has<NpcComponent>(npcEntity))
    {
        Log.Error("NPC entity {EntityId} missing NpcComponent", npcEntity.Id);
        return false;
    }
    if (!World.Has<SpriteAnimationComponent>(npcEntity))
    {
        Log.Error("NPC entity {EntityId} missing SpriteAnimationComponent", npcEntity.Id);
        return false;
    }
    if (!World.Has<PositionComponent>(npcEntity))
    {
        Log.Error("NPC entity {EntityId} missing PositionComponent", npcEntity.Id);
        return false;
    }
    if (!World.Has<RenderableComponent>(npcEntity))
    {
        Log.Error("NPC entity {EntityId} missing RenderableComponent", npcEntity.Id);
        return false;
    }
    return true;
}
```

**Use in CreateNpcEntity():**
```csharp
var npcEntity = CreateNpcEntity(...);
if (!ValidateNpcEntity(npcEntity))
{
    World.Destroy(npcEntity);
    throw new InvalidOperationException("Failed to create valid NPC entity");
}
```

**Benefits:**
- Early detection of component issues
- Better error messages
- Consistent with player entity validation pattern

---

## 6. Event Publishing for Animation Changes

### Current State
- `NpcAnimationSystem` updates animations internally
- No events published when animations change
- Other systems can't react to animation changes

### Improvement
**Publish events when animations change:**

```csharp
// In NpcAnimationSystem.UpdateAnimation()
// Track previous animation name
string previousAnimationName = anim.CurrentAnimationName;

// ... update animation logic ...

// If animation name changed (not just frame index), publish event
if (previousAnimationName != anim.CurrentAnimationName)
{
    var evt = new SpriteAnimationChangedEvent // or NpcAnimationChangedEvent
    {
        Entity = entity,
        OldAnimationName = previousAnimationName,
        NewAnimationName = anim.CurrentAnimationName
    };
    EventBus.Publish(evt);
}
```

**Note:** This requires tracking previous animation name, which might need a component or system state.

**Alternative:** Only publish events when external systems request animation changes (not for automatic frame advancement).

---

## 7. Consistent Query Patterns

### Current State
- NPCs use single query: `WithAll<NpcComponent, SpriteAnimationComponent>()`
- Player plan uses separate queries for performance

### Improvement
**Already Addressed in Player Plan:**
- `SpriteRendererSystem` will have separate queries for NPCs and Players
- `SpriteAnimationSystem` will have separate queries for NPCs and Players
- NPCs benefit from the performance improvements

**No Additional Changes Needed** - NPCs already use efficient query patterns.

---

## 8. Documentation and Code Organization

### Current State
- NPC creation logic is in `MapLoaderSystem` (mixed with map loading)
- No clear separation of concerns

### Improvement
**Consider extracting to `NpcSystem` (optional):**
- Move `CreateNpcs()` and `CreateNpcEntity()` to a dedicated `NpcSystem`
- `MapLoaderSystem` calls `NpcSystem.CreateNpcsForMap()`
- Better separation of concerns

**Or:** Keep in `MapLoaderSystem` but improve documentation:
- Add XML comments explaining component requirements
- Document event publishing patterns
- Add examples of NPC creation

---

## Summary of Recommended Improvements

### High Priority (Should Do)
1. ✅ **Publish animation change events** - Enables other systems to react
2. ✅ **Create helper method `CreateNpcEntity()`** - Centralized validation and reuse
3. ✅ **Use generic `SpriteAnimationChangedEvent`** - Simpler event system

### Medium Priority (Consider)
4. ⚠️ **Add component validation** - Better error handling
5. ⚠️ **Extract to NpcSystem** - Better separation of concerns (optional)

### Low Priority (Only If Needed)
6. ❌ **SpriteSheetComponent for NPCs** - Only if NPCs need multiple sprite sheets
7. ❌ **Track previous animation for events** - Only if needed for event publishing

---

## Implementation Order

1. **First**: Create helper method `CreateNpcEntity()` and use it in `CreateNpcs()`
2. **Second**: Publish `SpriteAnimationChangedEvent` (or keep `NpcAnimationChangedEvent` if using separate events)
3. **Third**: Add component validation in helper method
4. **Fourth**: Consider extracting to `NpcSystem` if codebase grows

---

## Notes

- Most improvements are independent and can be implemented separately
- The player entity plan already addresses query performance for NPCs
- NPCs don't need `SpriteSheetComponent` unless they have multiple sprite sheets
- Event-based communication improves decoupling and testability

