# Architecture Analysis: Player Entity Implementation Plan

## Critical Architecture Problems

### 1. **Query Complexity and Performance Issue**

**Problem:**
The plan requires querying for `WithAny<NpcComponent, PlayerComponent>()` and then checking for optional `SpriteSheetComponent` inside the lambda. In Arch ECS, you cannot query for optional components efficiently.

**Current Plan:**
```csharp
// This doesn't work well - SpriteSheetComponent is optional
World.Query(in query, (ref NpcComponent npc, ref PlayerComponent player, ...) => {
    string spriteId;
    if (World.Has<SpriteSheetComponent>(entity)) {
        // Player path
    } else {
        // NPC path
    }
});
```

**Issues:**
- `World.Has<T>()` check inside hot path (rendering/animation) is inefficient
- Can't use `WithAny` for optional components in a clean way
- Forces branching logic in every iteration

**Solutions:**
1. **Separate Queries** (Recommended): Query NPCs and Players separately
   ```csharp
   // Query 1: NPCs (no SpriteSheetComponent)
   _npcQuery = new QueryDescription()
       .WithAll<NpcComponent, SpriteAnimationComponent>()
       .WithNone<SpriteSheetComponent>();
   
   // Query 2: Players (with SpriteSheetComponent)
   _playerQuery = new QueryDescription()
       .WithAll<PlayerComponent, SpriteSheetComponent, SpriteAnimationComponent>();
   ```

2. **Unified SpriteComponent**: Create a generic `SpriteComponent` that both NPCs and Players use
   ```csharp
   public struct SpriteComponent {
       public string SpriteId { get; set; }
   }
   // NPCs: Set once, never change
   // Players: Update when sprite sheet changes
   ```

3. **Make SpriteSheetComponent Required for Players**: Always add it, never optional

---

### 2. **Component Design Inconsistency**

**Problem:**
NPCs use `NpcComponent.SpriteId` (single sprite), Players use `SpriteSheetComponent.CurrentSpriteSheetId` (multiple sprites). This creates:
- Different code paths for similar functionality
- Future refactoring needed if NPCs need multiple sprites
- Complexity in systems that need sprite information

**Better Approach:**
Use a unified `SpriteComponent` that both entity types use:
```csharp
public struct SpriteComponent {
    public string SpriteId { get; set; } // Current active sprite
}
```

- NPCs: Set once during creation, never changes
- Players: Updated when sprite sheet changes
- Systems: Always query `SpriteComponent.SpriteId` (consistent)

**Alternative:** Keep current design but make it explicit that `SpriteSheetComponent` is the "multi-sprite" version, and NPCs could use it too if needed.

---

### 3. **System Coupling and Responsibility**

**Problem:**
`PlayerSystem.SwitchSpriteSheet()` is a public method that other systems must call directly. This creates:
- Tight coupling (systems need PlayerSystem reference)
- Hard to test (must mock PlayerSystem)
- Violates ECS principle (systems shouldn't directly call each other)

**Better Approach:**
Use events or component-based operations:

**Option A: Events (Recommended)**
```csharp
// Input/Movement system publishes event
EventBus.Publish(new SpriteSheetChangeRequestEvent {
    Entity = playerEntity,
    NewSpriteSheetId = "base:sprite:players/may/machbike",
    AnimationName = "face_south"
});

// PlayerSystem subscribes and handles
private void OnSpriteSheetChangeRequest(SpriteSheetChangeRequestEvent evt) {
    // Validate and switch
}
```

**Option B: Component-Based**
```csharp
// Add component to request change
public struct SpriteSheetChangeRequestComponent {
    public string NewSpriteSheetId { get; set; }
    public string AnimationName { get; set; }
}

// PlayerSystem processes requests each frame
World.Query(in changeRequestQuery, (ref SpriteSheetChangeRequestComponent req) => {
    // Process and remove component
});
```

---

### 4. **Future System Integration Problems**

#### **Movement System**
**Needs:**
- Query: `PlayerComponent + PositionComponent + VelocityComponent` (future)
- When player moves, change animation: `"face_south"` → `"go_south"`
- **Problem**: Movement system needs to know current sprite sheet to pick correct animation

**Solution:**
- Movement system queries for `SpriteSheetComponent` (or `SpriteComponent`) to get current sprite
- Uses sprite + direction to determine animation name (e.g., `"go_south"` in current sprite sheet)
- Publishes `SpriteAnimationChangeRequestEvent` or directly updates `SpriteAnimationComponent`

#### **Input System**
**Needs:**
- Query: `PlayerComponent` (to identify player entity)
- Handle key presses (e.g., mount bike button)
- **Problem**: How to trigger sprite sheet change?

**Solution:**
- Input system publishes `SpriteSheetChangeRequestEvent` with new sprite sheet ID
- PlayerSystem handles validation and switching
- Or: Input system adds `SpriteSheetChangeRequestComponent` to player entity

#### **Collision System**
**Needs:**
- Query: `PlayerComponent + PositionComponent`
- Calculate collision bounds from sprite dimensions
- **Problem**: Needs sprite definition to get frame width/height

**Solution:**
- Query for `SpriteComponent` (or `SpriteSheetComponent`) to get sprite ID
- Use `ISpriteLoaderService.GetSpriteDefinition()` to get dimensions
- Or: Cache sprite dimensions in a component (redundant but faster)

---

### 5. **Event System Gaps**

**Missing Events:**
1. **SpriteSheetChangedEvent** - Fired when sprite sheet changes
   - Subscribers: Animation system (reset state), rendering system (cache invalidation)
   
2. **SpriteAnimationChangedEvent** (Generic) - Replace `NpcAnimationChangedEvent`
   - Works for both NPCs and Players
   - Or keep separate: `NpcAnimationChangedEvent` + `PlayerAnimationChangedEvent`

3. **SpriteSheetChangeRequestEvent** - Request to change sprite sheet
   - Published by: Input system, movement system, game logic
   - Handled by: PlayerSystem

**Current Plan Issue:**
Plan mentions events but doesn't specify which events are needed or how systems communicate.

---

### 6. **Query Performance Concerns**

**Current Approach:**
```csharp
// Inefficient - checking World.Has<> in hot path
World.Query(in query, (ref NpcComponent npc, ref PlayerComponent player, ...) => {
    if (World.Has<SpriteSheetComponent>(entity)) {
        // Player
    } else {
        // NPC
    }
});
```

**Performance Impact:**
- `World.Has<T>()` is a dictionary lookup per entity per frame
- In rendering system, this runs for every visible entity every frame
- Can cause frame drops with many entities

**Better:**
- Separate queries (no conditional checks)
- Or: Make component required (always present for players)

---

### 7. **Component Initialization and Lifecycle**

**Problem:**
Player entity needs 5 components created together:
- `PlayerComponent`
- `SpriteSheetComponent`
- `SpriteAnimationComponent`
- `PositionComponent`
- `RenderableComponent`

**Issues:**
- What if one component is missing? (e.g., forgot to add `SpriteSheetComponent`)
- Systems will fail silently or log warnings
- No compile-time safety

**Solution:**
- Create helper method in PlayerSystem: `CreatePlayerEntity(...)`
- Or: Use a "PlayerArchetype" pattern (if Arch ECS supports it)
- Document required components clearly

---

### 8. **Sprite ID Source Confusion**

**Problem:**
Systems need to extract sprite ID from different sources:
- NPCs: `NpcComponent.SpriteId`
- Players: `SpriteSheetComponent.CurrentSpriteSheetId`

**Future Problems:**
- What if we add other entity types? (Enemies, Items, etc.)
- Each new type needs new conditional logic
- Code becomes harder to maintain

**Better:**
Unified `SpriteComponent`:
```csharp
// All entities with sprites have this
public struct SpriteComponent {
    public string SpriteId { get; set; }
}

// NPCs: Set once
// Players: Updated when sprite sheet changes
// Future entities: Same pattern
```

---

## Recommended Architecture Changes

### 1. **Unified Sprite Component**
```csharp
public struct SpriteComponent {
    public string SpriteId { get; set; }
}
```
- Both NPCs and Players use this
- NPCs: Set once, immutable
- Players: Updated when sprite sheet changes
- Future entities: Same pattern

### 2. **Separate Queries for Performance**
```csharp
// NPC query (no SpriteSheetComponent)
_npcQuery = new QueryDescription()
    .WithAll<NpcComponent, SpriteComponent, SpriteAnimationComponent>();

// Player query (with SpriteSheetComponent for tracking)
_playerQuery = new QueryDescription()
    .WithAll<PlayerComponent, SpriteComponent, SpriteAnimationComponent, SpriteSheetComponent>();
```

### 3. **Event-Based Communication**
```csharp
// Request sprite sheet change
EventBus.Publish(new SpriteSheetChangeRequestEvent {
    Entity = playerEntity,
    NewSpriteSheetId = "...",
    AnimationName = "..."
});

// PlayerSystem handles and fires confirmation
EventBus.Publish(new SpriteSheetChangedEvent {
    Entity = playerEntity,
    OldSpriteSheetId = "...",
    NewSpriteSheetId = "..."
});
```

### 4. **Component-Based Requests (Alternative)**
```csharp
// Add request component
public struct SpriteSheetChangeRequestComponent {
    public string NewSpriteSheetId { get; set; }
    public string AnimationName { get; set; }
}

// PlayerSystem processes and removes
```

### 5. **Clear System Responsibilities**
- **PlayerSystem**: Initializes player, handles sprite sheet switching (via events/components)
- **MovementSystem**: Updates position, changes animations based on movement
- **InputSystem**: Publishes events for sprite sheet changes (mount bike, etc.)
- **SpriteRendererSystem**: Renders all entities with `SpriteComponent`
- **SpriteAnimationSystem**: Updates animations for all entities with `SpriteComponent`

---

## Migration Path

If keeping current design (NpcComponent.SpriteId vs SpriteSheetComponent):

1. **Make SpriteSheetComponent Required for Players**
   - Always add it during creation
   - Never make it optional

2. **Use Separate Queries**
   - Query NPCs separately from Players
   - Avoid `World.Has<>` checks in hot paths

3. **Add Events for Communication**
   - `SpriteSheetChangeRequestEvent`
   - `SpriteSheetChangedEvent`
   - Generic `SpriteAnimationChangedEvent` (or keep separate)

4. **Document Component Requirements**
   - Clearly document which components are required for each entity type
   - Add validation in PlayerSystem.CreatePlayerEntity()

---

## Questions to Resolve

1. **Should NPCs ever need multiple sprite sheets?**
   - If yes: Use unified `SpriteComponent` from start
   - If no: Current design is acceptable but use separate queries

2. **How should movement trigger animation changes?**
   - Direct component update? (MovementSystem updates SpriteAnimationComponent)
   - Event-based? (MovementSystem publishes animation change event)

3. **Should sprite sheet switching be synchronous or async?**
   - Synchronous: Direct component update
   - Async: Event-based with validation

4. **Performance vs. Flexibility trade-off?**
   - Separate queries: Better performance, more code
   - Unified query: Simpler code, worse performance
