# Scripting System Architecture Review

**Date**: 2025-01-27  
**Scope**: Scripting implementation, definition system, and behavior integration

---

## Executive Summary

This analysis identifies architecture issues, DRY violations, SOLID principle violations, bugs, and code quality issues in the scripting system implementation. The system is generally well-designed but has several areas for improvement.

---

## 🔴 Critical Issues

### 1. **Logic Bug in `copy_player_behavior.csx`**

**Location**: `Mods/pokemon-emerald/Scripts/Movement/copy_player_behavior.csx:65-88`

**Issue**: The `UpdateFacingDirection()` method checks if `playerDirection == _lastPlayerDirection` on line 83, but `_lastPlayerDirection` is already updated on line 88 before this check. This means the check will always be false after the first update.

**Current Code**:
```csharp
private void UpdateFacingDirection()
{
    // ... get playerDirection ...
    
    // Only update if player direction changed
    if (playerDirection == _lastPlayerDirection)  // Line 83
    {
        return; // No change
    }
    
    _lastPlayerDirection = playerDirection;  // Line 88 - updates BEFORE check
    
    // ... rest of method ...
}
```

**Problem**: The check on line 83 compares against the OLD value, but `_lastPlayerDirection` was already updated in `OnMovementCompleted` on line 60. This creates a logic error where:
1. `OnMovementCompleted` updates `_lastPlayerDirection` to `evt.Direction` (line 60)
2. Then calls `UpdateFacingDirection()` (line 61)
3. `UpdateFacingDirection()` queries for player direction (line 71-75)
4. Compares `playerDirection` (from query) with `_lastPlayerDirection` (from event) on line 83
5. These might differ if the player's facing direction changed between the event and the query

**Fix**: Remove the redundant check on line 83, or store the queried direction before comparing:

```csharp
private void UpdateFacingDirection()
{
    Direction playerDirection = Direction.South;
    bool foundPlayer = false;
    
    Context.Query<PlayerComponent, GridMovement>((Entity playerEntity, ref PlayerComponent player, ref GridMovement movement) =>
    {
        playerDirection = movement.FacingDirection;
        foundPlayer = true;
    });
    
    if (!foundPlayer)
    {
        return;
    }
    
    // Only update if player direction changed (compare BEFORE updating)
    if (playerDirection == _lastPlayerDirection)
    {
        return; // No change
    }
    
    _lastPlayerDirection = playerDirection;  // Update AFTER check
    
    // ... rest of method ...
}
```

**Impact**: Medium - May cause unnecessary updates or miss direction changes.

---

### 2. **QueryDescription Created in Hot Path**

**Location**: `MonoBall.Core/Scripting/Runtime/ScriptContext.cs:316-354`

**Issue**: The `Query<T1>`, `Query<T1, T2>`, and `Query<T1, T2, T3>` methods create new `QueryDescription` instances on every call, violating ECS best practices.

**Current Code**:
```csharp
public void Query<T1>(QueryAction<T1> action)
    where T1 : struct
{
    var query = new QueryDescription().WithAll<T1>();  // Created every call
    _world.Query(in query, (Entity e, ref T1 c1) => action(e, ref c1));
}
```

**Problem**: According to project rules, `QueryDescription` should be cached, not created in hot paths. This causes unnecessary allocations in frequently-called script methods.

**Fix**: Cache `QueryDescription` instances or use a static cache:

```csharp
private static readonly Dictionary<Type, QueryDescription> _queryCache = new();

public void Query<T1>(QueryAction<T1> action)
    where T1 : struct
{
    var query = GetOrCreateQuery<T1>();
    _world.Query(in query, (Entity e, ref T1 c1) => action(e, ref c1));
}

private static QueryDescription GetOrCreateQuery<T1>()
    where T1 : struct
{
    var type = typeof(T1);
    if (!_queryCache.TryGetValue(type, out var query))
    {
        query = new QueryDescription().WithAll<T1>();
        _queryCache[type] = query;
    }
    return query;
}
```

**Impact**: High - Performance issue affecting all scripts that query entities.

---

## 🟡 Architecture Issues

### 3. **Duplicate Parameter Resolution Logic**

**Location**: 
- `MapLoaderSystem.MergeScriptParameters()` (lines 1153-1209)
- `ScriptLifecycleSystem.BuildScriptParameters()` (lines 221-356)

**Issue**: Parameter resolution logic is duplicated between two systems. Both:
1. Start with ScriptDefinition defaults
2. Apply overrides from EntityVariablesComponent
3. Validate parameters

**Problem**: 
- Violates DRY principle
- Changes to parameter resolution must be made in two places
- Risk of inconsistencies between systems
- Harder to maintain

**Fix**: Extract parameter resolution to a shared service:

```csharp
public class ScriptParameterResolver
{
    public Dictionary<string, object> ResolveParameters(
        ScriptDefinition scriptDef,
        EntityVariablesComponent? variables = null
    )
    {
        // Single source of truth for parameter resolution
    }
}
```

**Impact**: Medium - Maintenance burden and risk of bugs.

---

### 4. **Duplicate Vector2 Parsing**

**Location**:
- `ScriptContext.ParseVector2()` (lines 125-137)
- `ScriptLifecycleSystem.ParseVector2()` (lines 361-373)
- `MapLoaderSystem.ParseVector2()` (lines 1343-1365)

**Issue**: Vector2 parsing logic is duplicated in three places with slight variations:
- `ScriptContext`: Returns `Vector2.Zero` on parse failure (silent failure)
- `ScriptLifecycleSystem`: Returns `Vector2.Zero` on parse failure (silent failure)
- `MapLoaderSystem`: Throws `FormatException` on parse failure (fail fast - correct)

**Problem**: 
- Violates DRY principle
- Inconsistent error handling (some fail silently, some throw)
- Harder to maintain

**Fix**: Extract to a shared utility class:

```csharp
public static class Vector2Parser
{
    public static Vector2 Parse(string value)
    {
        // Single implementation with consistent error handling
    }
}
```

**Impact**: Low - Code duplication, but functionality works.

---

### 5. **Duplicate Parameter Type Conversion**

**Location**:
- `ScriptContext.GetParameter<T>()` (lines 91-120)
- `MapLoaderSystem.ConvertParameterValue()` (lines 1215-1338)

**Issue**: Parameter type conversion logic is duplicated with different implementations:
- `ScriptContext`: Uses `Convert.ChangeType()` with special Vector2 handling
- `MapLoaderSystem`: Uses switch expressions with JsonElement handling

**Problem**:
- Violates DRY principle
- Different conversion logic may produce different results
- Harder to maintain

**Fix**: Extract to a shared parameter converter service:

```csharp
public class ScriptParameterConverter
{
    public T Convert<T>(object value, ScriptParameterDefinition? paramDef = null)
    {
        // Single implementation
    }
}
```

**Impact**: Medium - Risk of inconsistent behavior.

---

### 6. **State Key Format Hardcoded**

**Location**:
- `ScriptBase.GetStateKey()` (line 325): `$"script:{_scriptDefinitionId}:{key}"`
- `MapLoaderSystem` (line 1123): `$"script:{scriptDefForVariables.Id}:param:{kvp.Key}"`
- `ScriptLifecycleSystem` (line 255): `$"script:{scriptDef.Id}:param:{paramDef.Name}"`

**Issue**: State key format is hardcoded in multiple places with slight variations:
- Script state: `script:{scriptDefId}:{key}`
- Parameter overrides: `script:{scriptDefId}:param:{paramName}`

**Problem**:
- Violates DRY principle
- Risk of typos or inconsistencies
- Harder to refactor

**Fix**: Extract to constants or utility methods:

```csharp
public static class ScriptStateKeys
{
    public static string GetStateKey(string scriptDefId, string key)
        => $"script:{scriptDefId}:{key}";
    
    public static string GetParameterKey(string scriptDefId, string paramName)
        => $"script:{scriptDefId}:param:{paramName}";
}
```

**Impact**: Low - Code duplication, but functionality works.

---

## 🟠 SOLID Principle Violations

### 7. **ScriptContext Has Too Many Responsibilities** ✅ FIXED

**Location**: `MonoBall.Core/Scripting/Runtime/ScriptContext.cs`

**Issue**: `ScriptContext` violated Single Responsibility Principle by handling:
1. Component access (`GetComponent`, `SetComponent`, `HasComponent`)
2. Entity creation/destruction (`CreateEntity`, `DestroyEntity`)
3. Entity querying (`Query<T1>`, `Query<T1, T2>`, `Query<T1, T2, T3>`)
4. Parameter access (`GetParameter`, `GetParameters`)
5. World encapsulation

**Problem**: 
- Large class with multiple concerns
- Harder to test
- Harder to maintain
- Violates SRP

**Fix Applied**: Split responsibilities into focused interfaces:

```csharp
public interface IComponentAccessor
{
    T GetComponent<T>() where T : struct;
    void SetComponent<T>(T component) where T : struct;
    bool HasComponent<T>() where T : struct;
}

public interface IEntityQuery
{
    void Query<T1>(QueryAction<T1> action) where T1 : struct;
    // ...
}

public interface IEntityFactory
{
    Entity CreateEntity(params object[] components);
    void DestroyEntity(Entity entity);
}

public interface IScriptParameters
{
    T GetParameter<T>(string name, T? defaultValue = default);
    IReadOnlyDictionary<string, object> GetParameters();
}
```

`ScriptContext` now implements all four interfaces, making responsibilities explicit and improving maintainability. Backward compatibility is maintained - all existing scripts continue to work without changes.

**Impact**: Low - Architectural improvement for maintainability.

---

### 8. **ScriptLifecycleSystem Does Too Much**

**Location**: `MonoBall.Core/ECS/Systems/ScriptLifecycleSystem.cs`

**Issue**: `ScriptLifecycleSystem` handles:
1. Script initialization
2. Script cleanup
3. Parameter building
4. Parameter validation
5. Event subscription (EntityDestroyedEvent)

**Problem**: 
- Violates Single Responsibility Principle
- Harder to test individual concerns
- Harder to maintain

**Fix**: Extract parameter building/validation to a separate service:

```csharp
public class ScriptParameterService
{
    public Dictionary<string, object> BuildParameters(Entity entity, ScriptDefinition scriptDef)
    {
        // Extracted from ScriptLifecycleSystem
    }
}
```

**Impact**: Low - Current design works but could be improved.

---

## 🟢 Code Quality Issues

### 9. **Inconsistent Error Handling**

**Location**: Multiple files

**Issue**: Error handling is inconsistent:
- `ScriptContext.ParseVector2()`: Returns `Vector2.Zero` on failure (silent)
- `MapLoaderSystem.ParseVector2()`: Throws `FormatException` on failure (fail fast)
- `ScriptContext.GetParameter<T>()`: Returns default on conversion failure (silent)
- `MapLoaderSystem.ConvertParameterValue()`: Throws exception on failure (fail fast)

**Problem**: 
- Inconsistent behavior makes debugging harder
- Silent failures can hide bugs
- Project rules specify "fail fast" but some code doesn't follow this

**Fix**: Standardize on fail-fast approach per project rules:

```csharp
// All parsing/conversion should throw exceptions, not return defaults
public static Vector2 Parse(string value)
{
    if (string.IsNullOrWhiteSpace(value))
        throw new ArgumentException("Value cannot be null or empty", nameof(value));
    // ... parse and throw on failure
}
```

**Impact**: Medium - Inconsistent behavior may hide bugs.

---

### 10. **Missing Null Checks**

**Location**: `ScriptContext.Query<T1>()` and related methods

**Issue**: `Query` methods don't validate that `action` parameter is not null before use.

**Current Code**:
```csharp
public void Query<T1>(QueryAction<T1> action)
    where T1 : struct
{
    var query = new QueryDescription().WithAll<T1>();
    _world.Query(in query, (Entity e, ref T1 c1) => action(e, ref c1));  // No null check
}
```

**Problem**: 
- Null reference exception if `action` is null
- Violates fail-fast principle

**Fix**: Add null checks:

```csharp
public void Query<T1>(QueryAction<T1> action)
    where T1 : struct
{
    if (action == null)
        throw new ArgumentNullException(nameof(action));
    
    var query = new QueryDescription().WithAll<T1>();
    _world.Query(in query, (Entity e, ref T1 c1) => action(e, ref c1));
}
```

**Impact**: Low - Unlikely to occur in practice, but should be fixed for robustness.

---

### 11. **Type Conversion Robustness**

**Location**: `ScriptContext.GetParameter<T>()` (lines 109-117)

**Issue**: Uses `Convert.ChangeType()` which may fail for complex types or nullable types.

**Current Code**:
```csharp
try
{
    return (T)Convert.ChangeType(value, typeof(T));
}
catch
{
    return defaultValue ?? default(T)!;
}
```

**Problem**: 
- Silent failure on conversion errors
- Doesn't handle nullable types well
- Generic catch block hides specific errors

**Fix**: More robust conversion with better error messages:

```csharp
try
{
    if (value is T directValue)
        return directValue;
    
    return (T)Convert.ChangeType(value, typeof(T));
}
catch (InvalidCastException ex)
{
    throw new InvalidOperationException(
        $"Cannot convert parameter '{name}' from {value.GetType()} to {typeof(T)}",
        ex
    );
}
```

**Impact**: Low - Current implementation works for common cases.

---

## 📋 Recommendations Summary

### High Priority
1. ✅ **Fix QueryDescription creation in hot path** (Issue #2)
2. ✅ **Fix logic bug in copy_player_behavior.csx** (Issue #1)

### Medium Priority
3. ✅ **Extract duplicate parameter resolution logic** (Issue #3)
4. ✅ **Standardize error handling** (Issue #9)
5. ✅ **Extract duplicate parameter conversion** (Issue #5)

### Low Priority
6. ✅ **Extract duplicate Vector2 parsing** (Issue #4)
7. ✅ **Extract state key generation** (Issue #6)
8. ✅ **Add null checks to Query methods** (Issue #10)
9. ✅ **Improve type conversion robustness** (Issue #11)
10. ✅ **Refactor ScriptContext for SRP** (Issue #7) - Created IComponentAccessor, IEntityQuery, IEntityFactory, IScriptParameters interfaces
11. ✅ **Refactor ScriptLifecycleSystem for SRP** (Issue #8) - Extracted parameter logic to ScriptParameterResolver

---

## ✅ Positive Aspects

1. **Good Event Subscription Cleanup**: `ScriptBase` properly tracks and disposes event subscriptions
2. **Fail-Fast Parameter Validation**: `MapLoaderSystem` and `ScriptLifecycleSystem` validate parameters and throw exceptions
3. **Clear Separation**: Behavior → Script chain is well-defined
4. **Proper Disposal**: Systems implement `IDisposable` correctly
5. **Good Documentation**: XML comments are comprehensive

---

## 📝 Notes

- The scripting system is generally well-architected
- Most issues are code quality improvements rather than critical bugs
- The duplicate code issues are the most impactful for maintainability
- The QueryDescription hot-path issue is the most critical performance concern

