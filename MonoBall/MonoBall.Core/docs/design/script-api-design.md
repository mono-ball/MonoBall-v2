# Script API Design - Separation of Concerns

## Overview

This document defines the clear separation between **ScriptBase**, **APIs**, and **Utilities** to ensure consistent, maintainable script development.

## Design Principles

### ScriptBase
**Purpose**: Entity-specific convenience methods and script lifecycle management.

**Characteristics**:
- Operates on the script's own entity (`Context.Entity`)
- Provides convenience wrappers for common patterns
- Manages script-specific state and lifecycle
- Entity-attached scripts only (throws for plugin scripts when appropriate)
- Context-aware (has access to `Context`)

**When to add to ScriptBase**:
- ✅ Operation is specific to the script's own entity
- ✅ Reduces boilerplate for common patterns
- ✅ Requires script context (entity, state, timers)
- ✅ Entity-attached script convenience wrapper

**When NOT to add to ScriptBase**:
- ❌ Pure function (no context needed)
- ❌ Operates on other entities
- ❌ Game system operation
- ❌ Parsing/formatting utility

---

### APIs (IScriptApiProvider)
**Purpose**: Game system operations and cross-entity interactions.

**Characteristics**:
- Operates on any entity (passed as parameter)
- Accesses game systems and services
- Cross-entity operations (e.g., player, NPCs, maps)
- Service-like operations (movement, camera, flags)
- Stateless (no script-specific context)

**When to add to API**:
- ✅ Operates on multiple entities or game systems
- ✅ Accesses game services (movement, camera, etc.)
- ✅ Cross-entity operations (e.g., face player, query maps)
- ✅ Game state queries (flags, variables, definitions)

**When NOT to add to API**:
- ❌ Script-specific convenience wrapper
- ❌ Pure function or parsing utility
- ❌ Operates only on script's own entity (use ScriptBase)

---

### Utilities
**Purpose**: Pure functions, parsing, formatting, and stateless helpers.

**Characteristics**:
- Pure functions (no side effects, no state)
- Stateless (no context needed)
- Parsing/formatting operations
- Mathematical/algorithmic helpers
- Can be used outside scripts

**When to add to Utilities**:
- ✅ Pure function (input → output, no side effects)
- ✅ Parsing/formatting (strings, enums, etc.)
- ✅ Mathematical operations
- ✅ Algorithm helpers (e.g., direction calculations)
- ✅ No context or entity needed

**When NOT to add to Utilities**:
- ❌ Requires script context
- ❌ Operates on entities or components
- ❌ Accesses game systems

---

## Current State Analysis

### ScriptBase (Current)
✅ **Correctly placed**:
- `On<TEvent>()` - Event subscription (script-specific)
- `Get<T>()` / `Set<T>()` - State management (script-specific)
- `TryGetComponent<T>()` - Component access (own entity)
- `StartTimer()` / `UpdateTimer()` / `CancelTimer()` / `HasTimer()` - Timer management (own entity)
- `Publish<TEvent>()` - Event publishing (script-specific)

### APIs (Current)
✅ **Correctly placed**:
- `IPlayerApi` - Player operations (cross-entity)
- `IMapApi` - Map operations (game system)
- `IMovementApi` - Movement operations (game system)
- `ICameraApi` - Camera operations (game system)
- `IFlagVariableService` - Flags/variables (game state)

⚠️ **Missing**:
- `INpcApi` - NPC-specific operations (facing direction, movement state) - **To be added**

### Utilities (Current)
✅ **Correctly placed**:
- `DirectionParser` - Parsing direction strings (pure function)
- `Vector2Parser` - Parsing Vector2 strings (pure function)
- `ScriptStateKeys` - Key generation (pure function)

---

## Proposed Enhancements

### ScriptBase Enhancements

#### Event Filtering Helpers
**Rationale**: Reduces boilerplate for event filtering (appears in every event handler).

```csharp
// Event filtering for events with Entity property
protected bool IsEventForThisEntity<TEvent>(TEvent evt) 
    where TEvent : struct
{
    if (!Context.Entity.HasValue) return false;
    // Use reflection or interface constraint to get Entity property
    // Implementation checks evt.Entity.Id == Context.Entity.Value.Id
}

// Ref event version
protected bool IsEventForThisEntity<TEvent>(ref TEvent evt) 
    where TEvent : struct

// Timer event filtering
protected bool IsTimerEvent(string timerId, TimerElapsedEvent evt)
{
    return IsEventForThisEntity(evt) && evt.TimerId == timerId;
}

// Entity validation (fail-fast)
protected void RequireEntity()
{
    if (!Context.Entity.HasValue)
        throw new InvalidOperationException("This operation requires an entity-attached script.");
}
```

#### Component Access Helpers
**Rationale**: Common pattern for getting/setting GridMovement.FacingDirection and PositionComponent.

```csharp
// Facing direction helpers
protected Direction GetFacingDirection()
{
    RequireEntity();
    if (!TryGetComponent<GridMovement>(out var movement))
        throw new InvalidOperationException("Entity does not have GridMovement component.");
    return movement.FacingDirection;
}

protected Direction? TryGetFacingDirection()
{
    if (!Context.Entity.HasValue || !TryGetComponent<GridMovement>(out var movement))
        return null;
    return movement.FacingDirection;
}

protected void SetFacingDirection(Direction direction)
{
    RequireEntity();
    if (!TryGetComponent<GridMovement>(out var movement))
        throw new InvalidOperationException("Entity does not have GridMovement component.");
    movement.FacingDirection = direction;
    Context.SetComponent(movement);
}

// Position helpers
protected (int X, int Y) GetPosition()
{
    RequireEntity();
    var position = Context.GetComponent<PositionComponent>();
    return (position.X, position.Y);
}
```

#### Timer Helpers
**Rationale**: Common patterns for random timers and cleanup.

```csharp
// Random timer helpers
protected void StartRandomTimer(string timerId, float minDuration, float maxDuration, bool isRepeating = false)
{
    var duration = RandomHelper.RandomFloat(minDuration, maxDuration);
    StartTimer(timerId, duration, isRepeating);
}

protected void UpdateRandomTimer(string timerId, float minDuration, float maxDuration)
{
    var duration = RandomHelper.RandomFloat(minDuration, maxDuration);
    UpdateTimer(timerId, duration);
}

// Cleanup helper
protected void CancelTimerIfExists(string timerId)
{
    if (HasTimer(timerId))
        CancelTimer(timerId);
}
```

#### Parameter Parsing Helpers
**Rationale**: Reduces boilerplate for common parameter types.

```csharp
// Type-specific parameter helpers
protected Direction GetParameterAsDirection(string name, Direction defaultValue = Direction.South)
{
    var str = Context.GetParameter<string>(name, null);
    return string.IsNullOrEmpty(str) ? defaultValue : DirectionParser.Parse(str, defaultValue);
}

protected Direction[] GetParameterAsDirections(string name, Direction[]? defaultValue = null)
{
    var str = Context.GetParameter<string>(name, null);
    return string.IsNullOrEmpty(str) 
        ? (defaultValue ?? Array.Empty<Direction>())
        : DirectionParser.ParseList(str);
}

protected float GetParameterAsFloat(string name, float defaultValue = 0f)
{
    return Context.GetParameter<float>(name, defaultValue);
}

protected int GetParameterAsInt(string name, int defaultValue = 0)
{
    return Context.GetParameter<int>(name, defaultValue);
}

protected bool GetParameterAsBool(string name, bool defaultValue = false)
{
    return Context.GetParameter<bool>(name, defaultValue);
}
```

#### State Persistence Helpers
**Rationale**: Common patterns for enum serialization.

```csharp
// Enum state helpers
protected TEnum GetEnum<TEnum>(string key, TEnum defaultValue) 
    where TEnum : struct, Enum
{
    var str = Get<string>(key, null);
    return Enum.TryParse<TEnum>(str, out var value) ? value : defaultValue;
}

protected void SetEnum<TEnum>(string key, TEnum value) 
    where TEnum : struct, Enum
{
    Set(key, value.ToString());
}

// Direction-specific (most common)
protected Direction GetDirection(string key, Direction defaultValue = Direction.South)
{
    return GetEnum(key, defaultValue);
}

protected void SetDirection(string key, Direction value)
{
    SetEnum(key, value);
}

// Position state helpers
protected (int X, int Y) GetPositionState(string keyX, string keyY, int defaultX = 0, int defaultY = 0)
{
    return (Get<int>(keyX, defaultX), Get<int>(keyY, defaultY));
}

protected void SetPositionState(string keyX, string keyY, int x, int y)
{
    Set(keyX, x);
    Set(keyY, y);
}
```

**Note**: Movement operations should use `Context.Apis.Movement.*` directly. For NPC-specific operations like facing direction and movement state, use `Context.Apis.Npc.*`.

---

### API Enhancements

#### INpcApi (New)
**Rationale**: Currently missing - scripts manipulate components directly. Provides NPC-specific operations that complement IMovementApi.

```csharp
public interface INpcApi
{
    /// <summary>
    /// Sets an NPC's facing direction without moving.
    /// </summary>
    /// <param name="npc">The NPC entity.</param>
    /// <param name="direction">Direction to face.</param>
    void FaceDirection(Entity npc, Direction direction);
    
    /// <summary>
    /// Gets an NPC's current facing direction.
    /// </summary>
    /// <param name="npc">The NPC entity.</param>
    /// <returns>The facing direction, or null if NPC doesn't have GridMovement component.</returns>
    Direction? GetFacingDirection(Entity npc);
    
    /// <summary>
    /// Makes an NPC face toward another entity (e.g., face the player).
    /// Calculates direction based on positions.
    /// </summary>
    /// <param name="npc">The NPC entity.</param>
    /// <param name="target">The entity to face toward.</param>
    void FaceEntity(Entity npc, Entity target);
    
    /// <summary>
    /// Gets an NPC's current grid position.
    /// </summary>
    /// <param name="npc">The NPC entity.</param>
    /// <returns>The position component, or null if not found.</returns>
    PositionComponent? GetPosition(Entity npc);
    
    /// <summary>
    /// Sets an NPC's movement state (running state).
    /// </summary>
    /// <param name="npc">The NPC entity.</param>
    /// <param name="state">The running state to set.</param>
    void SetMovementState(Entity npc, RunningState state);
}
```

**Note**: Movement operations (RequestMovement, IsMoving, LockMovement, UnlockMovement) are handled by `IMovementApi` and should be used directly. `INpcApi` focuses on NPC-specific operations like facing direction and movement state.

---

### Utility Enhancements

#### DirectionHelper (New)
**Rationale**: Pure functions for direction manipulation - currently duplicated across scripts.

```csharp
public static class DirectionHelper
{
    /// <summary>
    /// Rotates a direction clockwise or counter-clockwise.
    /// </summary>
    public static Direction Rotate(Direction dir, bool clockwise)
    {
        return clockwise ? RotateClockwise(dir) : RotateCounterClockwise(dir);
    }
    
    /// <summary>
    /// Rotates a direction 90 degrees clockwise.
    /// </summary>
    public static Direction RotateClockwise(Direction dir)
    {
        return dir switch
        {
            Direction.North => Direction.East,
            Direction.East => Direction.South,
            Direction.South => Direction.West,
            Direction.West => Direction.North,
            _ => dir,
        };
    }
    
    /// <summary>
    /// Rotates a direction 90 degrees counter-clockwise.
    /// </summary>
    public static Direction RotateCounterClockwise(Direction dir)
    {
        return dir switch
        {
            Direction.North => Direction.West,
            Direction.West => Direction.South,
            Direction.South => Direction.East,
            Direction.East => Direction.North,
            _ => dir,
        };
    }
    
    /// <summary>
    /// Gets the opposite direction.
    /// </summary>
    public static Direction GetOpposite(Direction dir)
    {
        return dir switch
        {
            Direction.North => Direction.South,
            Direction.South => Direction.North,
            Direction.East => Direction.West,
            Direction.West => Direction.East,
            _ => dir,
        };
    }
    
    /// <summary>
    /// Calculates the primary direction from one point to another.
    /// Uses the axis with the larger delta.
    /// </summary>
    public static Direction GetDirectionTo(int fromX, int fromY, int toX, int toY)
    {
        int dx = toX - fromX;
        int dy = toY - fromY;
        
        if (Math.Abs(dx) > Math.Abs(dy))
            return dx > 0 ? Direction.East : Direction.West;
        
        return dy > 0 ? Direction.South : Direction.North;
    }
    
    /// <summary>
    /// Gets a random direction from the four cardinal directions.
    /// </summary>
    public static Direction GetRandomDirection()
    {
        var directions = new[] { Direction.North, Direction.South, Direction.East, Direction.West };
        return directions[Random.Shared.Next(directions.Length)];
    }
    
    /// <summary>
    /// Gets a random direction from the allowed directions.
    /// </summary>
    public static Direction GetRandomDirection(Direction[] allowed)
    {
        if (allowed == null || allowed.Length == 0)
            throw new ArgumentException("Allowed directions cannot be null or empty.", nameof(allowed));
        return allowed[Random.Shared.Next(allowed.Length)];
    }
}
```

#### RandomHelper (New)
**Rationale**: Common random number generation patterns.

```csharp
public static class RandomHelper
{
    /// <summary>
    /// Generates a random float in the specified range [min, max).
    /// </summary>
    public static float RandomFloat(float min, float max)
    {
        if (min >= max)
            throw new ArgumentException("Min must be less than max.", nameof(min));
        return (float)(Random.Shared.NextDouble() * (max - min) + min);
    }
    
    /// <summary>
    /// Generates a random integer in the specified range [min, max).
    /// </summary>
    public static int RandomInt(int min, int max)
    {
        return Random.Shared.Next(min, max);
    }
    
    /// <summary>
    /// Generates a random integer in the specified range [min, max] (inclusive).
    /// </summary>
    public static int RandomIntInclusive(int min, int max)
    {
        return Random.Shared.Next(min, max + 1);
    }
}
```

---

## Summary Table

| Category | Purpose | Examples | Access Pattern |
|----------|---------|----------|---------------|
| **ScriptBase** | Entity-specific convenience | `GetFacingDirection()`, `SetFacingDirection()`, `IsEventForThisEntity()` | `protected` methods in script class |
| **APIs** | Game system operations | `Context.Apis.Player.GetPlayerEntity()`, `Context.Apis.Movement.RequestMovement()`, `Context.Apis.Npc.FaceDirection()` | `Context.Apis.*` |
| **Utilities** | Pure functions | `DirectionHelper.Rotate()`, `DirectionParser.Parse()` | Static class methods |

---

## Migration Guide

### Moving Code to ScriptBase
If code appears in multiple scripts and:
- Operates on `Context.Entity` (script's own entity)
- Reduces boilerplate
- Requires script context

→ Move to ScriptBase as protected helper method

### Moving Code to API
If code:
- Operates on multiple entities (passed as parameter)
- Accesses game systems
- Cross-entity operations

→ Create or extend API interface

### Moving Code to Utilities
If code:
- Pure function (no side effects)
- No context needed
- Parsing/formatting
- Mathematical operation

→ Move to Utility static class

---

## Examples

### Before (Current Script Pattern)
```csharp
private void OnTimerElapsed(TimerElapsedEvent evt)
{
    // Boilerplate event filtering
    if (!Context.Entity.HasValue || evt.Entity.Id != Context.Entity.Value.Id)
        return;
    
    if (evt.TimerId != LookTimerId)
        return;
    
    // Manual component access
    if (TryGetComponent<GridMovement>(out var movement))
    {
        movement.FacingDirection = newDirection;
        Context.SetComponent(movement);
    }
    
    // Manual random calculation
    var nextInterval = (float)(Random.Shared.NextDouble() * (_maxInterval - _minInterval) + _minInterval);
    UpdateTimer(LookTimerId, nextInterval);
}

// Setting facing direction for another NPC (current approach)
if (TryGetComponent<GridMovement>(out var otherMovement))
{
    otherMovement.FacingDirection = Direction.North;
    Context.SetComponent(otherMovement);
}
```

### After (With Enhancements)
```csharp
private void OnTimerElapsed(TimerElapsedEvent evt)
{
    // Clean event filtering
    if (!IsTimerEvent(LookTimerId, evt))
        return;
    
    // Simple component access (for own entity)
    SetFacingDirection(newDirection);
    
    // Simple random timer
    UpdateRandomTimer(LookTimerId, _minInterval, _maxInterval);
}

// Setting facing direction for another NPC (using API)
Context.Apis.Npc.FaceDirection(otherNpcEntity, Direction.North);
```

---

## Decision Matrix

When adding a new helper, ask:

1. **Does it operate on the script's own entity?**
   - Yes → ScriptBase
   - No → Continue to #2

2. **Does it operate on game systems or multiple entities (including other entities)?**
   - Yes → API
   - No → Continue to #3

3. **Is it a pure function with no side effects?**
   - Yes → Utility
   - No → Re-evaluate (may need API or ScriptBase)

---

## Implementation Priority

### Phase 1: High Impact (Implement First)
1. ✅ Event filtering helpers (ScriptBase)
2. ✅ Component access helpers (ScriptBase)
3. ✅ DirectionHelper utility (Utilities)
4. ✅ RandomHelper utility (Utilities)

### Phase 2: Medium Impact
5. ✅ Timer helpers (ScriptBase)
6. ✅ Parameter parsing helpers (ScriptBase)
7. ✅ State persistence helpers (ScriptBase)

### Phase 3: Lower Priority
8. ✅ INpcApi - NPC-specific operations (facing direction, movement state)

