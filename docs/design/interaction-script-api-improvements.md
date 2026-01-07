# Interaction Script API Improvements Analysis

## Executive Summary

This document analyzes the example interaction scripts in the design document to identify API improvements that would simplify script writing and reduce boilerplate code.

---

## Current Example Scripts Analysis

### Map Interaction Script (Sign)

**Current Code:**
```csharp
public class LittlerootTownSignScript : ScriptBase
{
    public override void RegisterEventHandlers(ScriptContext context)
    {
        On<InteractionTriggeredEvent>(OnInteractionTriggered);
    }

    private void OnInteractionTriggered(InteractionTriggeredEvent evt)
    {
        // Only handle if this is our interaction entity
        if (!IsEventForThisEntity(evt))
        {
            return;
        }

        // Show message box with town sign text
        Context.Apis.MessageBox.ShowMessage(
            "LITTLEROOT TOWN\n\"The town where the\nadventure begins.\""
        );
    }
}
```

**Issues:**
- ✅ Good: Uses `IsEventForThisEntity()` check
- ✅ Good: Simple and clean
- ⚠️ Minor: Could use a helper method for interaction event handling

---

### NPC Interaction Script (Twin)

**Current Code:**
```csharp
public class LittlerootTownTwinScript : ScriptBase
{
    private const string InteractionCountKey = "interactionCount";

    private void OnInteractionStarted(InteractionStartedEvent evt)
    {
        // Only handle if this is our NPC entity
        if (!IsEventForThisEntity(evt))
        {
            return;
        }

        // Track interaction count
        var interactionCount = Get<int>(InteractionCountKey, 0);
        interactionCount++;
        Set(InteractionCountKey, interactionCount);

        // Face the player when interacting
        var playerPos = GetPlayerPosition(evt.PlayerEntity);  // ❌ Method doesn't exist
        if (playerPos.HasValue)
        {
            var npcPos = GetPosition();  // ❌ Method doesn't exist
            var direction = CalculateDirectionToPlayer(npcPos, playerPos.Value);  // ❌ Method doesn't exist
            SetFacingDirection(direction);  // ❌ Method doesn't exist
        }

        // Show different dialogue based on interaction count
        string message;
        if (interactionCount == 1)
        {
            message = "Hi! We're twins!\nWe just moved here!";
        }
        else if (interactionCount == 2)
        {
            message = "This is LITTLEROOT TOWN.\nIt's a nice place!";
        }
        else
        {
            message = "Have fun exploring!";
        }

        Context.Apis.MessageBox.ShowMessage(message);
    }

    private (int X, int Y)? GetPlayerPosition(Arch.Core.Entity playerEntity)  // ❌ Manual implementation
    {
        (int X, int Y)? position = null;
        Context.Query<Components.PositionComponent>((Entity e, ref Components.PositionComponent pos) =>
        {
            if (e.Id == playerEntity.Id)
            {
                position = ((int)pos.Position.X, (int)pos.Position.Y);  // ❌ Uses pixel position, not tile
            }
        });
        return position;
    }

    private Direction CalculateDirectionToPlayer((int X, int Y) npcPos, (int X, int Y) playerPos)  // ❌ Manual implementation
    {
        int deltaX = playerPos.X - npcPos.X;
        int deltaY = playerPos.Y - npcPos.Y;

        if (Math.Abs(deltaY) > Math.Abs(deltaX))
        {
            return deltaY < 0 ? Direction.North : Direction.South;
        }
        else
        {
            return deltaX < 0 ? Direction.West : Direction.East;
        }
    }
}
```

**Issues Identified:**

1. ❌ **`GetPlayerPosition()` doesn't exist** - Script manually queries for player position
2. ❌ **`GetPosition()` doesn't exist** - Should use `Context.Apis.Npc.GetPosition()` or component access
3. ❌ **`SetFacingDirection()` doesn't exist** - Should use `Context.Apis.Npc.FaceDirection()`
4. ❌ **`CalculateDirectionToPlayer()` is manual** - Common operation that should be a helper
5. ❌ **Uses pixel position instead of tile position** - Should use tile coordinates for direction calculation
6. ⚠️ **Boilerplate code** - Event filtering, position getting, direction calculation are repetitive

---

## Proposed API Improvements

### 1. **Add `FacePlayer()` Helper Method**

**Problem:** Scripts need to manually get positions and calculate direction to face the player.

**Current (Manual):**
```csharp
var playerPos = GetPlayerPosition(evt.PlayerEntity);
if (playerPos.HasValue)
{
    var npcPos = GetPosition();
    var direction = CalculateDirectionToPlayer(npcPos, playerPos.Value);
    SetFacingDirection(direction);
}
```

**Proposed (Simplified):**
```csharp
// Option 1: Direct helper method
FacePlayer(evt.PlayerEntity);

// Option 2: Using existing API (if we add convenience method)
Context.Apis.Npc.FaceEntity(Context.Entity.Value, evt.PlayerEntity);
```

**Implementation:**
```csharp
// In ScriptBase.cs
/// <summary>
/// Makes this entity face toward the player entity.
/// Calculates direction based on tile positions.
/// </summary>
/// <param name="playerEntity">The player entity to face toward.</param>
protected void FacePlayer(Entity playerEntity)
{
    RequireEntity();
    Context.Apis.Npc.FaceEntity(Context.Entity.Value, playerEntity);
}
```

**Benefits:**
- ✅ Eliminates 5+ lines of boilerplate
- ✅ Uses tile coordinates automatically
- ✅ Handles edge cases (null checks, component validation)

---

### 2. **Add `GetPlayerEntity()` Helper Method**

**Problem:** Scripts need to query for player entity manually.

**Current (Manual):**
```csharp
Entity? playerEntity = null;
Context.Query<PlayerComponent>((Entity e, ref PlayerComponent player) =>
{
    playerEntity = e;
});
```

**Proposed (Simplified):**
```csharp
var playerEntity = GetPlayerEntity();
if (playerEntity.HasValue)
{
    // Use player entity
}
```

**Implementation:**
```csharp
// In ScriptBase.cs
/// <summary>
/// Gets the player entity from the world.
/// </summary>
/// <returns>The player entity, or null if not found.</returns>
protected Entity? GetPlayerEntity()
{
    Entity? playerEntity = null;
    Context.Query<PlayerComponent>((Entity e, ref PlayerComponent player) =>
    {
        playerEntity = e;
    });
    return playerEntity;
}
```

**Benefits:**
- ✅ Common operation simplified
- ✅ Consistent player entity access

---

### 3. **Add `GetTilePosition()` Helper Method**

**Problem:** Scripts need to manually get position component and extract tile coordinates.

**Current (Manual):**
```csharp
var npcPos = Context.Apis.Npc.GetPosition(Context.Entity.Value);
if (npcPos.HasValue)
{
    int tileX = npcPos.Value.X;
    int tileY = npcPos.Value.Y;
}
```

**Proposed (Simplified):**
```csharp
var (tileX, tileY) = GetTilePosition();
// Or for another entity:
var (tileX, tileY) = GetTilePosition(someEntity);
```

**Implementation:**
```csharp
// In ScriptBase.cs
/// <summary>
/// Gets the tile position of this script's entity.
/// </summary>
/// <returns>The tile position as (X, Y), or null if not found.</returns>
protected (int X, int Y)? GetTilePosition()
{
    RequireEntity();
    return GetTilePosition(Context.Entity.Value);
}

/// <summary>
/// Gets the tile position of an entity.
/// </summary>
/// <param name="entity">The entity to get position for.</param>
/// <returns>The tile position as (X, Y), or null if not found.</returns>
protected (int X, int Y)? GetTilePosition(Entity entity)
{
    var pos = Context.Apis.Npc.GetPosition(entity);
    if (pos == null)
    {
        return null;
    }
    return (pos.Value.X, pos.Value.Y);
}
```

**Benefits:**
- ✅ Direct access to tile coordinates (not pixel)
- ✅ Consistent position access pattern

---

### 4. **Add `OnInteraction()` Convenience Method**

**Problem:** Every interaction script needs to check `IsEventForThisEntity()`.

**Current (Repetitive):**
```csharp
private void OnInteractionTriggered(InteractionTriggeredEvent evt)
{
    if (!IsEventForThisEntity(evt))
    {
        return;
    }
    // Handle interaction
}
```

**Proposed (Simplified):**
```csharp
public override void RegisterEventHandlers(ScriptContext context)
{
    // Automatically filters by entity
    OnInteraction(OnInteractionTriggered);
}

private void OnInteractionTriggered(InteractionTriggeredEvent evt)
{
    // No need to check IsEventForThisEntity - already filtered
    // Handle interaction
}
```

**Implementation:**
```csharp
// In ScriptBase.cs
/// <summary>
/// Subscribes to InteractionTriggeredEvent with automatic entity filtering.
/// Only fires if the event is for this script's entity.
/// </summary>
/// <param name="handler">The handler to invoke when interaction is triggered.</param>
protected void OnInteraction(Action<InteractionTriggeredEvent> handler)
{
    On<InteractionTriggeredEvent>(evt =>
    {
        if (IsEventForThisEntity(evt))
        {
            handler(evt);
        }
    });
}
```

**Benefits:**
- ✅ Eliminates repetitive `IsEventForThisEntity()` checks
- ✅ Cleaner, more readable code
- ✅ Reduces chance of forgetting the check

---

### 5. **Add `GetInteractionCount()` / `IncrementInteractionCount()` Helpers**

**Problem:** Interaction count tracking is a common pattern but requires manual state management.

**Current (Manual):**
```csharp
private const string InteractionCountKey = "interactionCount";

var interactionCount = Get<int>(InteractionCountKey, 0);
interactionCount++;
Set(InteractionCountKey, interactionCount);
```

**Proposed (Simplified):**
```csharp
var interactionCount = IncrementInteractionCount();
// Or get without incrementing:
var count = GetInteractionCount();
```

**Implementation:**
```csharp
// In ScriptBase.cs
private const string InteractionCountKey = "interaction_count";

/// <summary>
/// Gets the number of times this entity has been interacted with.
/// </summary>
/// <returns>The interaction count, or 0 if never interacted with.</returns>
protected int GetInteractionCount()
{
    return Get<int>(InteractionCountKey, 0);
}

/// <summary>
/// Increments and returns the interaction count for this entity.
/// </summary>
/// <returns>The new interaction count after incrementing.</returns>
protected int IncrementInteractionCount()
{
    var count = GetInteractionCount();
    count++;
    Set(InteractionCountKey, count);
    return count;
}
```

**Benefits:**
- ✅ Common pattern simplified
- ✅ Consistent key naming
- ✅ One-line operation instead of three

---

### 6. **Add `ShowDialogue()` Helper with Count-Based Messages**

**Problem:** Showing different messages based on interaction count is verbose.

**Current (Verbose):**
```csharp
var interactionCount = Get<int>(InteractionCountKey, 0);
interactionCount++;
Set(InteractionCountKey, interactionCount);

string message;
if (interactionCount == 1)
{
    message = "Hi! We're twins!\nWe just moved here!";
}
else if (interactionCount == 2)
{
    message = "This is LITTLEROOT TOWN.\nIt's a nice place!";
}
else
{
    message = "Have fun exploring!";
}

Context.Apis.MessageBox.ShowMessage(message);
```

**Proposed (Simplified):**
```csharp
var count = IncrementInteractionCount();
ShowDialogue(
    count == 1 ? "Hi! We're twins!\nWe just moved here!" :
    count == 2 ? "This is LITTLEROOT TOWN.\nIt's a nice place!" :
    "Have fun exploring!"
);
```

**Or even simpler with a helper:**
```csharp
ShowDialogueByCount(
    "Hi! We're twins!\nWe just moved here!",  // First interaction
    "This is LITTLEROOT TOWN.\nIt's a nice place!",  // Second interaction
    "Have fun exploring!"  // Default for 3+
);
```

**Implementation:**
```csharp
// In ScriptBase.cs
/// <summary>
/// Shows a message box with the given text.
/// Convenience wrapper around MessageBox.ShowMessage.
/// </summary>
/// <param name="message">The message to display.</param>
protected void ShowDialogue(string message)
{
    Context.Apis.MessageBox.ShowMessage(message);
}

/// <summary>
/// Shows dialogue based on interaction count.
/// Increments interaction count automatically.
/// </summary>
/// <param name="firstMessage">Message for first interaction.</param>
/// <param name="secondMessage">Message for second interaction.</param>
/// <param name="defaultMessage">Message for third and subsequent interactions.</param>
protected void ShowDialogueByCount(string firstMessage, string secondMessage, string defaultMessage)
{
    var count = IncrementInteractionCount();
    string message = count == 1 ? firstMessage :
                     count == 2 ? secondMessage :
                     defaultMessage;
    ShowDialogue(message);
}
```

**Benefits:**
- ✅ Reduces boilerplate for common dialogue patterns
- ✅ Automatic interaction count tracking
- ✅ More readable code

---

## Summary of Proposed Improvements

### High Priority (Most Impact)

1. ✅ **`FacePlayer(Entity playerEntity)`** - Eliminates 5+ lines of boilerplate
2. ✅ **`OnInteraction(Action<InteractionTriggeredEvent>)`** - Eliminates repetitive entity checks
3. ✅ **`GetTilePosition()` / `GetTilePosition(Entity)`** - Direct tile coordinate access

### Medium Priority (Nice to Have)

4. ✅ **`GetPlayerEntity()`** - Common operation simplified
5. ✅ **`IncrementInteractionCount()` / `GetInteractionCount()`** - Common pattern simplified

### Low Priority (Convenience)

6. ✅ **`ShowDialogue(string)`** - Thin wrapper, but consistent API
7. ✅ **`ShowDialogueByCount(...)`** - Very specific use case, but common pattern

---

## Updated Example Scripts

### Map Interaction Script (Simplified)

```csharp
public class LittlerootTownSignScript : ScriptBase
{
    public override void RegisterEventHandlers(ScriptContext context)
    {
        // Automatically filters by entity
        OnInteraction(OnInteractionTriggered);
    }

    private void OnInteractionTriggered(InteractionTriggeredEvent evt)
    {
        // No entity check needed - already filtered
        ShowDialogue("LITTLEROOT TOWN\n\"The town where the\nadventure begins.\"");
    }
}
```

**Improvements:**
- ✅ Removed `IsEventForThisEntity()` check (handled by `OnInteraction()`)
- ✅ Added `ShowDialogue()` convenience method
- ✅ Cleaner, more readable code

---

### NPC Interaction Script (Simplified)

```csharp
public class LittlerootTownTwinScript : ScriptBase
{
    public override void RegisterEventHandlers(ScriptContext context)
    {
        OnInteraction(OnInteractionTriggered);
        On<InteractionStartedEvent>(OnInteractionStarted);
    }

    private void OnInteractionTriggered(InteractionTriggeredEvent evt)
    {
        // Behavior script is automatically paused by InteractionSystem
    }

    private void OnInteractionStarted(InteractionStartedEvent evt)
    {
        if (!IsEventForThisEntity(evt))
        {
            return;
        }

        // Face the player (one line instead of 5+)
        FacePlayer(evt.PlayerEntity);

        // Show dialogue based on interaction count (one line instead of 10+)
        ShowDialogueByCount(
            "Hi! We're twins!\nWe just moved here!",
            "This is LITTLEROOT TOWN.\nIt's a nice place!",
            "Have fun exploring!"
        );
    }
}
```

**Improvements:**
- ✅ Removed `GetPlayerPosition()` manual implementation
- ✅ Removed `GetPosition()` manual implementation
- ✅ Removed `CalculateDirectionToPlayer()` manual implementation
- ✅ Removed `SetFacingDirection()` - replaced with `FacePlayer()`
- ✅ Removed interaction count tracking boilerplate
- ✅ Removed dialogue selection logic
- ✅ Reduced from ~80 lines to ~25 lines
- ✅ Much more readable and maintainable

---

## Implementation Notes

### Backward Compatibility

All proposed methods are **additions** to `ScriptBase`, not replacements. Existing scripts will continue to work.

### Performance Considerations

- Helper methods are thin wrappers - minimal overhead
- `OnInteraction()` adds one extra check, but eliminates duplicate checks in scripts
- Caching player entity could be added if needed (low priority)

### Testing

Each helper method should have:
- Unit tests for happy path
- Unit tests for edge cases (null entities, missing components)
- Integration tests with example scripts

---

## Conclusion

These API improvements would significantly simplify interaction script writing:

- **Reduced boilerplate**: From ~80 lines to ~25 lines for complex NPC interactions
- **Better readability**: Intent is clearer with helper methods
- **Fewer bugs**: Common operations are centralized and tested
- **Faster development**: Script writers can focus on game logic, not ECS details

The improvements follow the existing API patterns and maintain backward compatibility.

