# Flag/Variable System Architecture Analysis

**Date:** 2025-01-XX  
**Status:** Critical Issues Identified  
**Reviewer:** Architecture Analysis

---

## Executive Summary

This document identifies architecture issues, SOLID/DRY violations, Arch ECS integration problems, and bugs in the flags and variables system implementation. **All issues should be addressed to ensure maintainability, performance, and correctness.**

---

## 🔴 CRITICAL ISSUES

### 1. Event Missing Entity Context

**Issue**: `FlagChangedEvent` and `VariableChangedEvent` don't distinguish between global and entity-specific changes. This causes ambiguity for event subscribers.

**Location**: `FlagVariableService.SetFlag()`, `FlagVariableService.SetEntityFlag()`, `FlagVariableService.SetVariable()`, `FlagVariableService.SetEntityVariable()`

**Problem**:
- `SetEntityFlag()` fires `FlagChangedEvent` with only `FlagId`, but subscribers can't tell if it's a global flag or entity-specific flag
- Same issue with `VariableChangedEvent` - no way to know if it's global or entity-specific
- Systems subscribing to these events may react incorrectly

**Impact**: **HIGH** - Event subscribers cannot properly handle entity-specific vs global flags/variables

**Fix Required**:
```csharp
// Option 1: Add Entity field to events (nullable for global)
public struct FlagChangedEvent
{
    public string FlagId { get; set; }
    public Entity? Entity { get; set; } // null = global, non-null = entity-specific
    public bool OldValue { get; set; }
    public bool NewValue { get; set; }
}

// Option 2: Separate events for entity-specific changes
public struct EntityFlagChangedEvent { ... }
public struct EntityVariableChangedEvent { ... }
```

---

### 2. OldValue Calculation Bug for New Flags

**Issue**: In `SetFlag()` and `SetEntityFlag()`, `oldValue` is retrieved BEFORE checking if the flag index exists, causing incorrect oldValue for newly created flags.

**Location**: 
- `FlagVariableService.SetFlag()` line 142
- `FlagVariableService.SetEntityFlag()` line 441

**Problem**:
```csharp
bool oldValue = GetFlag(flagId); // Returns false if flag doesn't exist

// Get or allocate index for this flag
if (!flags.FlagIndices.TryGetValue(flagId, out int index))
{
    // Flag is being created for first time
    // oldValue is false, but this might be setting it to false
    // Event will fire with OldValue=false, NewValue=false (no-op event)
}
```

**Impact**: **MEDIUM** - Events fire with incorrect `OldValue` for new flags, or fire when value doesn't actually change (false→false)

**Fix Required**:
```csharp
// Check if flag exists BEFORE getting old value
bool flagExists = flags.FlagIndices != null && flags.FlagIndices.ContainsKey(flagId);
bool oldValue = flagExists ? GetFlag(flagId) : false;

// Or better: only fire event if flag existed before
if (!flagExists)
{
    // First time setting this flag - oldValue is implicitly false
    oldValue = false;
}
```

---

### 3. VariableChangedEvent Loses Type Information

**Issue**: `VariableChangedEvent` stores values as strings (`OldValue` and `NewValue` are `string`), losing type information and making it difficult for subscribers to deserialize.

**Location**: `FlagVariableService.SetVariable()`, `FlagVariableService.SetEntityVariable()`

**Problem**:
```csharp
var variableChangedEvent = new VariableChangedEvent
{
    Key = key,
    OldValue = oldValue?.ToString() ?? string.Empty, // Lost type info
    NewValue = value?.ToString() ?? string.Empty,    // Lost type info
};
```

**Impact**: **MEDIUM** - Subscribers cannot properly deserialize variable values without knowing the type

**Fix Required**:
```csharp
public struct VariableChangedEvent
{
    public string Key { get; set; }
    public string? OldValue { get; set; }      // Serialized value
    public string? NewValue { get; set; }      // Serialized value
    public string? OldType { get; set; }       // Type name for deserialization
    public string? NewType { get; set; }       // Type name for deserialization
    public Entity? Entity { get; set; }        // null = global, non-null = entity-specific
}
```

---

### 4. Missing Deletion Events

**Issue**: `DeleteVariable()` doesn't fire any event when a variable is deleted, so subscribers can't react to deletions.

**Location**: `FlagVariableService.DeleteVariable()` line 295

**Impact**: **MEDIUM** - Systems cannot react to variable deletions

**Fix Required**:
```csharp
public void DeleteVariable(string key)
{
    if (string.IsNullOrWhiteSpace(key))
        return;

    EnsureInitialized();
    ref VariablesComponent variables = ref _world.Get<VariablesComponent>(_gameStateEntity);

    if (variables.Variables != null && variables.Variables.ContainsKey(key))
    {
        string? oldValue = variables.Variables[key];
        variables.Variables.Remove(key);
        variables.VariableTypes?.Remove(key);

        // Fire deletion event
        var variableDeletedEvent = new VariableDeletedEvent
        {
            Key = key,
            OldValue = oldValue ?? string.Empty,
        };
        EventBus.Send(ref variableDeletedEvent);
    }
}
```

---

## 🟡 ARCHITECTURE ISSUES

### 5. Inefficient Singleton Entity Lookup

**Issue**: `EnsureInitialized()` uses a query to find the singleton entity every time until initialized, but doesn't break after finding the first match.

**Location**: `FlagVariableService.EnsureInitialized()` line 47-54

**Problem**:
```csharp
var found = false;
_world.Query(
    in GameStateQuery,
    (Entity entity) =>
    {
        _gameStateEntity = entity;
        found = true;
        // No break - continues iterating unnecessarily
    }
);
```

**Impact**: **LOW** - Performance issue, but only runs once per service lifetime

**Fix Required**:
- Query is cached correctly (good!)
- Consider using `World.QueryFirst()` if available, or break after first match
- Or cache the entity reference after first lookup

---

### 6. No Validation of Entity Still Exists

**Issue**: `_gameStateEntity` is cached but never validated that the entity still exists. If the entity is destroyed, subsequent operations will fail.

**Location**: `FlagVariableService` - all methods using `_gameStateEntity`

**Impact**: **LOW** - Unlikely scenario, but could cause crashes if entity is destroyed

**Fix Required**:
```csharp
private void EnsureInitialized()
{
    if (_initialized)
    {
        // Validate entity still exists
        if (!_world.IsAlive(_gameStateEntity))
        {
            _initialized = false;
            _gameStateEntity = Entity.Null;
        }
        else
        {
            return;
        }
    }
    // ... rest of initialization
}
```

---

## 🟠 SOLID/DRY VIOLATIONS

### 7. Massive Code Duplication Between Global and Entity Operations

**Issue**: Significant duplication between global flag/variable operations and entity-specific operations.

**Location**: 
- `GetFlag()` vs `GetEntityFlag()` - nearly identical bit manipulation
- `SetFlag()` vs `SetEntityFlag()` - nearly identical logic
- `GetVariable()` vs `GetEntityVariable()` - identical deserialization
- `SetVariable()` vs `SetEntityVariable()` - identical serialization

**Impact**: **HIGH** - Violates DRY principle, makes maintenance difficult, increases bug risk

**Fix Required**: Extract common logic into private helper methods:
```csharp
// Extract bit manipulation logic
private static bool GetFlagValue(byte[] flags, Dictionary<string, int> flagIndices, string flagId)
{
    if (flagIndices == null || !flagIndices.TryGetValue(flagId, out int index))
        return false;
    
    if (flags == null)
        return false;
    
    int byteIndex = index / 8;
    int bitIndex = index % 8;
    
    if (byteIndex >= flags.Length)
        return false;
    
    return (flags[byteIndex] & (1 << bitIndex)) != 0;
}

private static void SetFlagValue(ref byte[] flags, ref Dictionary<string, int> flagIndices, 
    ref Dictionary<int, string> indexToFlagId, ref int nextIndex, string flagId, bool value)
{
    // Common flag setting logic
}

// Then use in both global and entity methods
public bool GetFlag(string flagId)
{
    EnsureInitialized();
    ref FlagsComponent flags = ref _world.Get<FlagsComponent>(_gameStateEntity);
    return GetFlagValue(flags.Flags, flags.FlagIndices, flagId);
}

public bool GetEntityFlag(Entity entity, string flagId)
{
    if (!_world.Has<EntityFlagsComponent>(entity))
        return false;
    
    ref EntityFlagsComponent flags = ref _world.Get<EntityFlagsComponent>(entity);
    return GetFlagValue(flags.Flags, flags.FlagIndices, flagId);
}
```

---

### 8. Duplicated Null-Coalescing Initialization

**Issue**: Null-coalescing assignments (`??=`) are repeated in multiple methods.

**Location**: 
- `SetFlag()` lines 138-140
- `SetVariable()` lines 258-259
- `SetEntityFlag()` lines 437-439
- `SetEntityVariable()` lines 519-520

**Impact**: **MEDIUM** - Code duplication, but low risk

**Fix Required**: Extract to helper methods:
```csharp
private static void EnsureFlagsComponentInitialized(ref FlagsComponent flags)
{
    flags.FlagIndices ??= new Dictionary<string, int>();
    flags.IndexToFlagId ??= new Dictionary<int, string>();
    flags.Flags ??= new byte[313];
}

private static void EnsureVariablesComponentInitialized(ref VariablesComponent variables)
{
    variables.Variables ??= new Dictionary<string, string>();
    variables.VariableTypes ??= new Dictionary<string, string>();
}
```

---

### 9. Duplicated Bitfield Expansion Logic

**Issue**: Bitfield array expansion logic is duplicated in `SetFlag()` and `SetEntityFlag()`.

**Location**: 
- `SetFlag()` lines 151-159
- `SetEntityFlag()` lines 449-456

**Impact**: **MEDIUM** - Code duplication

**Fix Required**: Extract to helper method:
```csharp
private static void ExpandBitfieldIfNeeded(ref byte[] flags, int index)
{
    int requiredBytes = (index / 8) + 1;
    if (requiredBytes > flags.Length)
    {
        int newSize = Math.Max(flags.Length * 2, requiredBytes);
        var newFlags = new byte[newSize];
        Array.Copy(flags, newFlags, flags.Length);
        flags = newFlags;
    }
}
```

---

## 🟢 ARCH ECS BEST PRACTICES

### 10. QueryDescription Caching ✅

**Status**: **GOOD** - `GameStateQuery` is correctly cached as `static readonly` field.

**Location**: `FlagVariableService` line 17

**Note**: This follows .cursorrules requirement to cache queries.

---

### 11. Components Are Pure Data ✅

**Status**: **GOOD** - All components (`FlagsComponent`, `VariablesComponent`, `EntityFlagsComponent`, `EntityVariablesComponent`) are pure data structures with no methods.

**Note**: This follows .cursorrules requirement for ECS components.

---

## 🟡 EVENT SYSTEM ISSUES

### 12. Event Subscription Disposal ✅

**Status**: **GOOD** - `VisibilityFlagSystem` correctly implements `IDisposable` and unsubscribes in `Dispose()`.

**Location**: `VisibilityFlagSystem.cs` lines 78-87

**Note**: This follows .cursorrules requirement for event subscriptions.

---

### 13. Event Fires Even When Value Doesn't Change

**Issue**: Events fire when setting a flag/variable to the same value it already has (though there's a check, it's not perfect for new flags).

**Location**: `FlagVariableService.SetFlag()` line 172, `SetVariable()` line 267

**Impact**: **LOW** - Events fire unnecessarily, but doesn't break functionality

**Note**: This is partially addressed by the `oldValue != value` check, but see Issue #2 for the bug.

---

## 🐛 BUGS

### 14. Floating Point Comparison in SetVariable

**Issue**: `SetVariable()` uses `Equals()` to compare old and new values, which may not work correctly for floating-point types due to precision issues.

**Location**: `FlagVariableService.SetVariable()` line 267

**Problem**:
```csharp
if (!Equals(oldValue, value)) // Problematic for float/double
{
    // Fire event
}
```

**Impact**: **LOW** - May fire events when values are "equal" but not exactly equal (e.g., 1.0f vs 0.9999999f)

**Fix Required**: Use type-specific comparison:
```csharp
bool valuesChanged = oldValue switch
{
    null when value == null => false,
    null => true,
    float f when value is float f2 => Math.Abs(f - f2) > float.Epsilon,
    double d when value is double d2 => Math.Abs(d - d2) > double.Epsilon,
    _ => !Equals(oldValue, value)
};
```

---

### 15. Missing Entity Validation in Entity Methods

**Issue**: `GetEntityFlag()`, `SetEntityFlag()`, `GetEntityVariable()`, `SetEntityVariable()` don't validate that the entity is alive.

**Location**: All entity-specific methods

**Impact**: **LOW** - Could cause exceptions if entity is destroyed

**Fix Required**: Add validation:
```csharp
public bool GetEntityFlag(Entity entity, string flagId)
{
    if (string.IsNullOrWhiteSpace(flagId))
        return false;
    
    if (!_world.IsAlive(entity))
        return false;
    
    // ... rest of method
}
```

---

## 📊 SUMMARY

### Critical Issues (Must Fix)
1. ✅ Event missing entity context (#1)
2. ✅ OldValue calculation bug (#2)
3. ✅ VariableChangedEvent loses type info (#3)
4. ✅ Missing deletion events (#4)

### Architecture Issues (Should Fix)
5. ⚠️ Inefficient singleton lookup (#5)
6. ⚠️ No entity validation (#6)

### SOLID/DRY Violations (Should Fix)
7. ✅ Massive code duplication (#7)
8. ⚠️ Duplicated initialization (#8)
9. ⚠️ Duplicated bitfield expansion (#9)

### Bugs (Should Fix)
14. ⚠️ Floating point comparison (#14)
15. ⚠️ Missing entity validation (#15)

### Good Practices (Keep)
- ✅ QueryDescription caching
- ✅ Pure data components
- ✅ Event subscription disposal

---

## RECOMMENDATIONS

### Priority 1 (Critical)
1. Add entity context to events (Issue #1)
2. Fix oldValue calculation bug (Issue #2)
3. Add type information to VariableChangedEvent (Issue #3)
4. Add deletion events (Issue #4)

### Priority 2 (Important)
5. Extract common logic to reduce duplication (Issue #7)
6. Add entity validation (Issue #15)

### Priority 3 (Nice to Have)
7. Optimize singleton lookup (Issue #5)
8. Extract initialization helpers (Issue #8)
9. Extract bitfield expansion (Issue #9)
10. Fix floating point comparison (Issue #14)

---

## NEXT STEPS

1. Create separate event types for entity-specific changes OR add Entity field to existing events
2. Fix oldValue calculation to check flag existence before getting value
3. Add type information to VariableChangedEvent
4. Create VariableDeletedEvent and fire it in DeleteVariable()
5. Refactor to extract common logic between global and entity operations
6. Add entity validation to all entity-specific methods

