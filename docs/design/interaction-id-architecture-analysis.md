# InteractionId Design - Architecture Analysis

## Executive Summary

This document analyzes the `interactionId` design for architecture issues, Arch ECS problems, event system concerns, scripting system issues, and general architectural concerns.

---

## 🔴 Critical Issues

### 1. **Missing Input Check in InteractionSystem**

**Problem:**
The `InteractionSystem.Update()` method fires `InteractionTriggeredEvent` every frame when the player is near an interaction, without checking if the player actually pressed the interact button.

**Current Code (Line 456-467):**
```csharp
// TODO: Check if interact button was pressed (requires input system integration)
// For now, this is a placeholder - actual implementation will check input state

// Trigger interaction event
var interactionEvent = new InteractionTriggeredEvent { ... };
EventBus.Send(ref interactionEvent);
```

**Impact:**
- Event fires continuously while player is near interaction
- Scripts receive event every frame, causing spam
- Message boxes could open repeatedly
- Performance issues from excessive event firing

**Solution:**
```csharp
// Check if interact button was just pressed (not held)
bool interactJustPressed = _inputBindingService.IsActionJustPressed(InputAction.Interact);
if (!interactJustPressed)
{
    return; // Player didn't press interact button
}

// Also check if player is not already in an interaction
if (IsPlayerInInteraction(playerEntity.Value))
{
    return; // Player is already interacting with something
}

// Trigger interaction event
var interactionEvent = new InteractionTriggeredEvent { ... };
EventBus.Send(ref interactionEvent);
```

**Required Changes:**
- Inject `IInputBindingService` into `InteractionSystem` constructor
- Add state tracking to prevent multiple simultaneous interactions
- Use `IsActionJustPressed()` not `IsActionPressed()` (edge-triggered, not level-triggered)

---

### 2. **Inefficient Script Pausing/Resuming**

**Problem:**
The `PauseScript()` and `ResumeScript()` helper methods query ALL entities with `ScriptAttachmentComponent` to find the specific script on one entity. This is extremely inefficient.

**Current Code (Lines 585-632):**
```csharp
private bool PauseScript(Entity entity, string scriptDefinitionId)
{
    // Query all ScriptAttachmentComponents on the entity
    bool found = false;
    World.Query(
        new QueryDescription().WithAll<ScriptAttachmentComponent>(),
        (Entity e, ref ScriptAttachmentComponent attachment) =>
        {
            if (e.Id == entity.Id && attachment.ScriptDefinitionId == scriptDefinitionId)
            {
                attachment.IsActive = false;
                World.Set(e, attachment);
                found = true;
            }
        }
    );
    return found;
}
```

**Impact:**
- Queries ALL entities in the world, not just the target entity
- O(n) complexity where n = total entities with ScriptAttachmentComponent
- Called every time an interaction starts/ends
- Performance bottleneck

**Solution:**
Use Arch ECS's `World.Get()` or `World.TryGet()` with component filtering, or use a more efficient approach:

```csharp
private bool PauseScript(Entity entity, string scriptDefinitionId)
{
    // Arch ECS doesn't support querying multiple components of same type directly
    // We need to iterate through all ScriptAttachmentComponents on the entity
    // But we can optimize by checking entity ID first
    
    if (!World.Has<ScriptAttachmentComponent>(entity))
    {
        return false;
    }

    // Get all ScriptAttachmentComponents on this entity
    // Note: Arch ECS allows multiple components of same type
    // We need to query and filter by entity ID
    bool found = false;
    World.Query(
        new QueryDescription().WithAll<ScriptAttachmentComponent>(),
        (Entity e, ref ScriptAttachmentComponent attachment) =>
        {
            // Early exit if not our entity
            if (e.Id != entity.Id)
            {
                return;
            }

            if (attachment.ScriptDefinitionId == scriptDefinitionId)
            {
                attachment.IsActive = false;
                World.Set(e, attachment);
                found = true;
            }
        }
    );
    return found;
}
```

**Better Solution:**
Store a mapping of entity → script attachments in `InteractionSystem`:

```csharp
private readonly Dictionary<Entity, List<ScriptAttachmentComponent>> _entityScripts = new();

// Update mapping when scripts are added/removed (subscribe to component change events)
// Then PauseScript becomes:
private bool PauseScript(Entity entity, string scriptDefinitionId)
{
    if (!_entityScripts.TryGetValue(entity, out var scripts))
    {
        return false;
    }

    foreach (var script in scripts)
    {
        if (script.ScriptDefinitionId == scriptDefinitionId)
        {
            script.IsActive = false;
            World.Set(entity, script); // Update component
            return true;
        }
    }
    return false;
}
```

**Note:** Arch ECS doesn't have built-in support for querying multiple components of the same type on a single entity efficiently. The dictionary approach requires manual tracking.

---

### 3. **InteractionSystem Query Includes Unnecessary Component**

**Problem:**
The `InteractionSystem` query includes `ScriptAttachmentComponent` but doesn't use it in the callback:

```csharp
_interactionQuery = new QueryDescription()
    .WithAll<InteractionComponent, PositionComponent, ScriptAttachmentComponent>();
```

**Impact:**
- Unnecessary component requirement (interactions might not have scripts attached yet)
- Query is more restrictive than needed
- Script attachment is handled by `ScriptLifecycleSystem`, not `InteractionSystem`

**Solution:**
Remove `ScriptAttachmentComponent` from the query:

```csharp
_interactionQuery = new QueryDescription()
    .WithAll<InteractionComponent, PositionComponent>();
```

The interaction script will be attached separately and will subscribe to `InteractionTriggeredEvent` via the event system.

---

### 4. **Multiple ScriptAttachmentComponent Query Issue**

**Problem:**
When an NPC has both a behavior script and an interaction script, both are `ScriptAttachmentComponent` instances on the same entity. The `PauseScript()` method needs to find the specific behavior script, but Arch ECS doesn't provide an efficient way to query multiple components of the same type on one entity.

**Current Approach:**
The design queries all entities and filters by entity ID, which is inefficient.

**Better Approach:**
1. **Store script metadata in component**: Add a `ScriptType` enum or string to identify script purpose
2. **Use separate components**: Create `BehaviorScriptComponent` and `InteractionScriptComponent` (violates DRY)
3. **Track in system**: Maintain a dictionary mapping entity → list of script attachments (requires manual synchronization)

**Recommended Solution:**
Add a `ScriptType` field to `ScriptAttachmentComponent`:

```csharp
public struct ScriptAttachmentComponent
{
    public string ScriptDefinitionId { get; set; }
    public ScriptType Type { get; set; } // NEW: Behavior, Interaction, etc.
    // ... other fields
}

public enum ScriptType
{
    Behavior,
    Interaction,
    Other
}
```

Then `PauseScript()` becomes:
```csharp
private bool PauseScript(Entity entity, ScriptType scriptType)
{
    bool found = false;
    World.Query(
        new QueryDescription().WithAll<ScriptAttachmentComponent>(),
        (Entity e, ref ScriptAttachmentComponent attachment) =>
        {
            if (e.Id == entity.Id && attachment.Type == scriptType)
            {
                attachment.IsActive = false;
                World.Set(e, attachment);
                found = true;
            }
        }
    );
    return found;
}
```

**Alternative:** Use the script definition ID directly (current approach), but optimize the query.

---

## 🟡 Medium Priority Issues

### 5. **Interaction State Tracking Not Fully Designed**

**Problem:**
The design mentions tracking interaction state to resume behavior scripts when message boxes close, but the implementation is incomplete.

**Current Design (Lines 728-745):**
- Mentions `InteractionStateComponent` or dictionary-based tracking
- Recommends component-based tracking
- But doesn't show how `MessageBoxClosedEvent` connects to resuming scripts

**Missing Pieces:**
1. How does `InteractionSystem` know which NPC to resume when message box closes?
2. How to handle multiple message boxes (nested interactions)?
3. How to handle message box cancellation (player moves away)?

**Solution:**
Add `InteractionStateComponent`:

```csharp
public struct InteractionStateComponent
{
    /// <summary>
    /// The behavior script ID that was paused for this interaction.
    /// </summary>
    public string BehaviorScriptId { get; set; }
    
    /// <summary>
    /// The interaction entity (NPC or map interaction).
    /// </summary>
    public Entity InteractionEntity { get; set; }
    
    /// <summary>
    /// The player entity that triggered this interaction.
    /// </summary>
    public Entity PlayerEntity { get; set; }
}
```

When interaction starts:
```csharp
// Pause behavior script
PauseScript(npcEntity, behaviorScriptId);

// Add InteractionStateComponent to player entity
World.Add(playerEntity, new InteractionStateComponent
{
    BehaviorScriptId = behaviorScriptId,
    InteractionEntity = npcEntity,
    PlayerEntity = playerEntity
});
```

When message box closes:
```csharp
private void OnMessageBoxClosed(MessageBoxClosedEvent evt)
{
    // Find player entity with InteractionStateComponent
    Entity? playerEntity = null;
    World.Query(
        new QueryDescription().WithAll<PlayerComponent, InteractionStateComponent>(),
        (Entity e, ref PlayerComponent player, ref InteractionStateComponent state) =>
        {
            playerEntity = e;
            // Resume behavior script
            ResumeScript(state.InteractionEntity, state.BehaviorScriptId);
            // Remove state component
            World.Remove<InteractionStateComponent>(e);
        }
    );
}
```

---

### 6. **PositionComponent Tile Coordinate Sync**

**Problem:**
The `InteractionSystem` uses `pos.X` and `pos.Y` (tile coordinates) directly, but these might not be synced if the entity was created with pixel coordinates.

**Current Code:**
```csharp
int interactionTileX = pos.X;
int interactionTileY = pos.Y;
```

**Impact:**
- If `PositionComponent` was created with `Position = new Vector2(pixelX, pixelY)`, the tile coordinates might be incorrect
- `PositionComponent.SyncPixelsToGrid()` must be called after setting pixel position

**Solution:**
Ensure `MapLoaderSystem` syncs tile coordinates after setting pixel position:

```csharp
var positionComponent = new PositionComponent
{
    Position = interactionPixelPosition, // Sets PixelX, PixelY and calls SyncPixelsToGrid()
};
// Position property setter automatically calls SyncPixelsToGrid() with default 16x16 tiles
```

Or explicitly sync:
```csharp
var positionComponent = new PositionComponent
{
    Position = interactionPixelPosition,
};
positionComponent.SyncPixelsToGrid(mapDefinition.TileWidth, mapDefinition.TileHeight);
```

**Verification:**
Check that `PositionComponent.Position` setter calls `SyncPixelsToGrid()` (it does, line 57).

---

### 7. **MapLoaderSystem Needs Tile Size**

**Problem:**
The `CreateInteractions()` method calculates pixel position using `mapDefinition.TileWidth` and `mapDefinition.TileHeight`, but these might not exist or might be hardcoded.

**Current Code (Line 253-256):**
```csharp
Vector2 interactionPixelPosition = new Vector2(
    mapTilePosition.X * mapDefinition.TileWidth + interactionDef.X,
    mapTilePosition.Y * mapDefinition.TileHeight + interactionDef.Y
);
```

**Solution:**
Verify `MapDefinition` has `TileWidth` and `TileHeight` properties, or use constants (16x16 for Pokemon games).

---

### 8. **Event Firing in Update Loop**

**Problem:**
`InteractionTriggeredEvent` is fired in the `Update()` loop, which means it could fire multiple times if the player stays near an interaction for multiple frames (even with input check).

**Impact:**
- Scripts need to handle idempotency (same event fired multiple times)
- Event handlers should check if interaction already started

**Solution:**
Add state tracking to prevent duplicate events:

```csharp
private readonly HashSet<Entity> _activeInteractions = new();

public override void Update(in float deltaTime)
{
    // ... existing code ...
    
    // Check if this interaction is already active
    if (_activeInteractions.Contains(interactionEntity))
    {
        return; // Already interacting
    }
    
    // Trigger interaction event
    var interactionEvent = new InteractionTriggeredEvent { ... };
    EventBus.Send(ref interactionEvent);
    
    // Mark as active
    _activeInteractions.Add(interactionEntity);
}

private void OnInteractionEnded(InteractionEndedEvent evt)
{
    _activeInteractions.Remove(evt.InteractionEntity);
}
```

---

## 🟢 Low Priority Issues

### 9. **NPC Interaction Detection**

**Problem:**
The design shows `InteractionSystem` checking for NPCs with `NpcComponent`, but NPCs don't have `InteractionComponent`. The system needs to handle both map interactions (with `InteractionComponent`) and NPC interactions (with `NpcComponent`).

**Current Design:**
- Map interactions: Have `InteractionComponent`, query with `_interactionQuery`
- NPC interactions: Have `NpcComponent`, need separate query

**Solution:**
Add a separate query for NPCs:

```csharp
private readonly QueryDescription _npcInteractionQuery;

public InteractionSystem(World world, ILogger logger) : base(world)
{
    // ... existing code ...
    _npcInteractionQuery = new QueryDescription()
        .WithAll<NpcComponent, PositionComponent, ScriptAttachmentComponent>();
}

public override void Update(in float deltaTime)
{
    // Check map interactions
    World.Query(in _interactionQuery, ...);
    
    // Check NPC interactions
    World.Query(in _npcInteractionQuery, (ref NpcComponent npc, ref PositionComponent pos, ref ScriptAttachmentComponent script) =>
    {
        // Check if this NPC has an interaction script
        if (script.ScriptDefinitionId != npc.InteractionId)
        {
            return; // Not the interaction script
        }
        
        // Check distance, facing, input, etc.
        // ...
    });
}
```

**Better Solution:**
Unify the approach - add `InteractionComponent` to NPCs as well, or create a base `InteractableComponent`.

---

### 10. **Event Subscription Cleanup** ✅ VERIFIED

**Status:** ✅ **Not an Issue**

**Verification:**
- `ScriptBase.On<T>()` stores subscriptions in `_subscriptions` list (line 21)
- `OnUnload()` automatically disposes all subscriptions (lines 59-63)
- `ScriptLifecycleSystem` calls `OnUnload()` when scripts are cleaned up
- No memory leaks - subscriptions are properly tracked and disposed

**Conclusion:**
Event subscription cleanup is handled correctly. Scripts using `On<T>()` will automatically unsubscribe when the script is unloaded or the entity is destroyed.

---

### 11. **QueryDescription Caching**

**Problem:**
The design shows `QueryDescription` being cached in constructor, which is correct. But the `PauseScript()` method creates a new `QueryDescription` in the method, which is inefficient.

**Current Code:**
```csharp
World.Query(
    new QueryDescription().WithAll<ScriptAttachmentComponent>(), // Created every call
    ...
);
```

**Solution:**
Cache the query description:

```csharp
private readonly QueryDescription _scriptAttachmentQuery;

public InteractionSystem(World world, ILogger logger) : base(world)
{
    // ... existing code ...
    _scriptAttachmentQuery = new QueryDescription().WithAll<ScriptAttachmentComponent>();
}

private bool PauseScript(Entity entity, string scriptDefinitionId)
{
    World.Query(in _scriptAttachmentQuery, ...);
}
```

---

## 📋 Summary of Required Changes

### Critical (Must Fix):
1. ✅ Add input check using `IsActionJustPressed()` in `InteractionSystem`
2. ✅ Optimize `PauseScript()`/`ResumeScript()` to avoid querying all entities
3. ✅ Remove `ScriptAttachmentComponent` from `InteractionSystem` query
4. ✅ Add interaction state tracking to prevent duplicate events

### Medium Priority:
5. ✅ Implement `InteractionStateComponent` for tracking active interactions
6. ✅ Verify `PositionComponent` tile coordinate sync
7. ✅ Verify `MapDefinition` has tile size properties
8. ✅ Add state tracking to prevent duplicate event firing

### Low Priority:
9. ✅ Add separate query for NPC interactions or unify approach
10. ✅ Verify event subscription cleanup
11. ✅ Cache `QueryDescription` in `PauseScript()` method

---

## 🎯 Recommended Implementation Order

1. **Fix input check** (Critical) - Prevents event spam
2. **Optimize script pausing** (Critical) - Performance issue
3. **Add interaction state tracking** (Medium) - Prevents duplicate interactions
4. **Implement InteractionStateComponent** (Medium) - Enables proper resume logic
5. **Fix query issues** (Low) - Code quality improvements

---

## 📚 References

- Arch ECS Documentation: Multiple components of same type
- `ScriptLifecycleSystem.cs`: How scripts are managed
- `InputBindingService.cs`: Input checking API
- `MessageBoxSceneSystem.cs`: Message box lifecycle
- `PositionComponent.cs`: Tile coordinate sync

