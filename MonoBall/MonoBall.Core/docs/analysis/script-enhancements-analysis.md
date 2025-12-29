# Script Enhancements - Code Analysis

**Date:** Analysis of recent script API enhancements  
**Status:** Issues identified, recommendations provided

---

## Summary

Analysis of the script enhancements including `ScriptBase` helpers, `INpcApi`, `DirectionHelper`, and `RandomHelper`
utilities. Identified several architectural issues, DRY violations, and potential bugs.

---

## 🐛 Critical Issues

### 1. DRY Violation: Duplicate Functionality Between ScriptBase and INpcApi

**Severity:** Medium  
**Category:** SOLID/DRY Violation

**Issue:**

- `ScriptBase.SetFacingDirection()` and `INpcApi.FaceDirection()` both modify facing direction
- `ScriptBase.GetPosition()` and `INpcApi.GetPosition()` both retrieve position
- `ScriptBase.GetFacingDirection()` / `TryGetFacingDirection()` and `INpcApi.GetFacingDirection()` both retrieve facing
  direction

**Location:**

- `MonoBall.Core/Scripting/Runtime/ScriptBase.cs` (lines 293-343)
- `MonoBall.Core/Scripting/INpcApi.cs` (lines 12-39)
- `MonoBall.Core/Scripting/ScriptApiProvider.cs` (lines 412-466)

**Impact:**

- Code duplication violates DRY principle
- Developers confused about which API to use
- Maintenance burden: changes need to be made in multiple places

**Recommendation:**
According to cursor rules: "we don't need movement helpers if we have movementapi". However, there's nuance:

- `ScriptBase` helpers work on `Context.Entity` (script's own entity) - convenient for common case
- `INpcApi` methods take Entity parameter - more flexible, can work on any entity

**Options:**

1. **Remove ScriptBase helpers**, migrate scripts to use
   `Context.Apis.Npc.FaceDirection(Context.Entity.Value, direction)`
2. **Keep ScriptBase helpers** for convenience, but document that they delegate to `INpcApi` internally
3. **Make ScriptBase helpers delegate to INpcApi** - reduces duplication while keeping convenience API

**Recommended:** Option 3 - Keep convenience API but eliminate duplication by having ScriptBase methods delegate to
INpcApi.

---

### 2. NpcApiImpl Silently Fails (Potential Bug)

**Severity:** Medium  
**Category:** Error Handling / Cursor Rules Violation

**Issue:**
`NpcApiImpl` methods silently return/do nothing when:

- Entity is not alive
- Entity doesn't have required component

**Location:**

- `MonoBall.Core/Scripting/ScriptApiProvider.cs` (lines 412-477)

**Example:**

```csharp
public void FaceDirection(Entity npc, Direction direction)
{
    if (!_world.IsAlive(npc) || !_world.Has<GridMovement>(npc))
    {
        return; // ❌ Silently fails
    }
    // ...
}
```

**Cursor Rules Violation:**
> **NO FALLBACK CODE** - Fail fast with clear exceptions, never silently degrade or use default values for required
> dependencies

**Impact:**

- Bugs hidden - scripts think they set direction but entity was dead
- Hard to debug - no indication that operation failed
- Inconsistent with fail-fast philosophy

**Recommendation:**
Throw exceptions instead of silently failing:

```csharp
public void FaceDirection(Entity npc, Direction direction)
{
    if (!_world.IsAlive(npc))
    {
        throw new ArgumentException($"Entity {npc.Id} is not alive.", nameof(npc));
    }
    
    if (!_world.Has<GridMovement>(npc))
    {
        throw new InvalidOperationException(
            $"Entity {npc.Id} does not have GridMovement component. " +
            "Cannot set facing direction without GridMovement component."
        );
    }
    
    ref var movement = ref _world.Get<GridMovement>(npc);
    movement.FacingDirection = direction;
}
```

---

## ⚠️ Architectural Issues

### 3. Inconsistent Component Modification Patterns

**Severity:** Low  
**Category:** Arch ECS Pattern Inconsistency

**Issue:**
Two different patterns for modifying components:

1. `NpcApiImpl`: Uses `ref var movement = ref _world.Get<GridMovement>(npc);` - modifies via ref ✅
2. `ScriptBase.SetFacingDirection`: Uses `TryGetComponent` (copy) then `Context.SetComponent` (write-back) ✅

Both are correct Arch ECS patterns, but inconsistent.

**Location:**

- `MonoBall.Core/Scripting/ScriptApiProvider.cs` (line 419)
- `MonoBall.Core/Scripting/Runtime/ScriptBase.cs` (line 329-330)

**Analysis:**

- `NpcApiImpl` pattern is more efficient (direct ref modification)
- `ScriptBase` pattern goes through `Context.SetComponent` which uses `World.Set/Add`
- Both are valid, but `NpcApiImpl` is more efficient

**Recommendation:**
Keep both patterns (they serve different contexts), but document why:

- `NpcApiImpl` uses direct ref (more efficient, direct world access)
- `ScriptBase` uses `Context.SetComponent` (abstraction layer, consistent with other ScriptBase methods)

**Status:** Acceptable - both patterns are correct.

---

### 4. Null-Forgiving Operator Overuse

**Severity:** Low  
**Category:** Code Style

**Issue:**
Some scripts use `Context.Entity!.Value` after already checking or calling `RequireEntity()`, which is redundant.

**Location:**

- `Mods/pokemon-emerald/Scripts/Movement/walk_in_place_behavior.csx` (lines 28, 58)
- `Mods/pokemon-emerald/Scripts/Movement/guard_behavior.csx` (line 71)

**Example:**

```csharp
SetFacingDirection(_walkDirection); // Calls RequireEntity() internally
Context.Apis.Npc.SetMovementState(Context.Entity!.Value, RunningState.Moving); // Redundant !
```

**Impact:**

- Not a bug, but reduces code clarity
- Null-forgiving operator should be used sparingly

**Recommendation:**
Remove redundant `!` operators when entity is already guaranteed to exist:

```csharp
RequireEntity(); // or SetFacingDirection which calls RequireEntity
Context.Apis.Npc.SetMovementState(Context.Entity.Value, RunningState.Moving); // No ! needed
```

---

## 🔍 Potential Bugs

### 5. Reflection on Ref Struct Events

**Severity:** Low (Likely Safe)  
**Category:** Potential Bug

**Issue:**
`IsEventForThisEntity<TEvent>(ref TEvent evt)` copies struct then boxes for reflection. Need to verify this works
correctly for all event types.

**Location:**

- `MonoBall.Core/Scripting/Runtime/ScriptBase.cs` (lines 255-275)

**Code:**

```csharp
protected bool IsEventForThisEntity<TEvent>(ref TEvent evt)
    where TEvent : struct
{
    // ...
    TEvent evtCopy = evt; // Copy struct
    object boxedEvt = evtCopy; // Box for reflection
    var eventEntity = (Entity)entityProp.GetValue(boxedEvt)!;
    return eventEntity.Id == Context.Entity.Value.Id;
}
```

**Analysis:**

- Copying struct is safe (value type)
- Boxing is necessary for reflection on ref parameters
- Should work correctly, but worth testing with various event types

**Recommendation:**
Test with all event types to ensure reflection works correctly. Consider adding unit tests.

**Status:** Likely safe, but should be verified.

---

### 6. Missing Null Check in IsEventForThisEntity

**Severity:** Low  
**Category:** Defensive Programming

**Issue:**
`entityProp.GetValue(boxedEvt)!` uses null-forgiving operator, but if property exists but returns null, this could
throw.

**Location:**

- `MonoBall.Core/Scripting/Runtime/ScriptBase.cs` (lines 244, 273)

**Analysis:**

- Events should always have non-null Entity property
- But defensive programming would check for null
- Current code uses `!` assuming Entity is never null

**Recommendation:**
Add null check for safety:

```csharp
var eventEntityValue = entityProp.GetValue(boxedEvt);
if (eventEntityValue == null)
    return false;
var eventEntity = (Entity)eventEntityValue;
return eventEntity.Id == Context.Entity.Value.Id;
```

**Status:** Low risk, but would improve robustness.

---

## ✅ Positive Findings

### Correct Arch ECS Patterns

- `NpcApiImpl` correctly uses `ref var movement = ref _world.Get<GridMovement>(npc)` for component modification ✅
- `ScriptBase.SetFacingDirection` correctly uses `Context.SetComponent` for abstraction ✅
- Component access patterns follow Arch ECS best practices ✅

### Good Separation of Concerns

- Utility classes (`DirectionHelper`, `RandomHelper`) are pure functions ✅
- `ScriptBase` helpers reduce boilerplate ✅
- `INpcApi` provides script-safe abstraction ✅

### Proper Error Handling (in ScriptBase)

- `ScriptBase.SetFacingDirection` throws exceptions for missing components ✅
- `RequireEntity()` throws clear exceptions ✅

---

## 📋 Recommendations Summary

1. **HIGH PRIORITY:**
    - [ ] Make `NpcApiImpl` fail fast with exceptions instead of silently returning
    - [ ] Consider removing duplicate functionality between ScriptBase and INpcApi (or make ScriptBase delegate to
      INpcApi)

2. **MEDIUM PRIORITY:**
    - [ ] Remove redundant null-forgiving operators in scripts
    - [ ] Add null check in `IsEventForThisEntity` for robustness

3. **LOW PRIORITY:**
    - [ ] Test reflection on ref struct events with all event types
    - [ ] Document why different component modification patterns are used

---

## Notes

- Overall architecture is sound
- Most issues are minor code quality improvements
- The biggest issue is the duplicate functionality and silent failures in NpcApiImpl
- Script migrations look good - scripts are using the new helpers correctly


