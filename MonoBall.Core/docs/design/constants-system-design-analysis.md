# Constants System Design - Architecture Analysis

## Overview

This document analyzes the constants system design for architecture issues, SOLID/DRY violations, ECS/event system
integration, and alignment with project patterns.

## Critical Issues

### 1. ❌ Service Initialization Timing

**Issue**: `ConstantsService` loads constants in its constructor, but mods may not be loaded yet.

**Problem**:

```csharp
public ConstantsService(IModManager modManager, ILogger logger)
{
    LoadConstantsFromMods(modManager); // Mods may not be loaded!
}
```

**Impact**: Service creation fails if mods aren't loaded, violating dependency order.

**Solution**: Follow the pattern used by `FontServiceFactory` - create service after mods load:

- Create service in `MonoBallGame.LoadModsSynchronously()` after mods are loaded
- Or use lazy initialization pattern with validation

**Recommended Pattern**:

```csharp
// In MonoBallGame.LoadModsSynchronously(), after mods load:
var constantsService = new ConstantsService(modManager, logger);
Services.AddService(typeof(IConstantsService), constantsService);
```

### 2. ❌ Performance: Deserialization on Every Access

**Issue**: `DeserializeConstant<T>()` deserializes JSON on every `Get<T>()` call.

**Problem**:

```csharp
private T DeserializeConstant<T>(string key, JsonElement element) where T : struct
{
    return JsonSerializer.Deserialize<T>(
        element.GetRawText(), // Allocates string!
        JsonSerializerOptionsFactory.Default
    );
}
```

**Impact**:

- Allocates strings on every access (`GetRawText()`)
- Deserializes JSON repeatedly for same constant
- Violates "no allocations in hot paths" requirement

**Solution**: Cache deserialized values:

```csharp
private readonly Dictionary<string, object> _cachedValues = new();
private readonly Dictionary<string, JsonElement> _constants = new();

public T Get<T>(string key) where T : struct
{
    if (_cachedValues.TryGetValue(key, out var cached) && cached is T typed)
        return typed;
    
    // Deserialize once, cache result
    var value = DeserializeConstant<T>(key, _constants[key]);
    _cachedValues[key] = value;
    return value;
}
```

### 3. ❌ Exception Handling Violates Fail-Fast Principle

**Issue**: `TryGet<T>()` swallows all exceptions, hiding errors.

**Problem**:

```csharp
try
{
    value = DeserializeConstant<T>(key, element);
    return true;
}
catch  // Swallows ALL exceptions!
{
    return false;
}
```

**Impact**: Type mismatches, JSON errors, and other issues are silently ignored.

**Solution**: Only catch expected exceptions:

```csharp
catch (InvalidCastException)
{
    return false; // Type mismatch is expected
}
catch (JsonException)
{
    _logger.Warning("Failed to deserialize constant '{Key}': {Error}", key, ex.Message);
    return false;
}
// Let other exceptions propagate (fail-fast)
```

### 4. ❌ Missing Null Validation for modManager Parameter

**Issue**: Constructor doesn't validate `modManager` parameter.

**Problem**:

```csharp
public ConstantsService(IModManager modManager, ILogger logger)
{
    _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    // modManager not validated!
    LoadConstantsFromMods(modManager);
}
```

**Impact**: Violates project's fail-fast principle.

**Solution**: Add null check:

```csharp
public ConstantsService(IModManager modManager, ILogger logger)
{
    _modManager = modManager ?? throw new ArgumentNullException(nameof(modManager));
    _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    LoadConstantsFromMods(_modManager);
}
```

### 5. ❌ Type Safety: Generic Constraint Inconsistency

**Issue**: `Get<T>()` has `where T : struct` constraint, but `GetString()` exists separately.

**Problem**:

- Inconsistent API design
- Can't use `Get<string>()` even though strings are valid constants
- Forces separate method for strings

**Impact**: API confusion, harder to use.

**Solution Options**:

1. **Keep separate methods** (current approach) - clearer intent
2. **Remove constraint, handle strings in generic method** - more consistent but less type-safe

**Recommendation**: Keep separate methods but document why clearly.

### 6. ❌ DRY Violation: Duplicate Validation Logic

**Issue**: `Get<T>()` and `GetString()` have duplicate key validation.

**Problem**:

```csharp
public T Get<T>(string key) where T : struct
{
    if (string.IsNullOrEmpty(key))
        throw new ArgumentException("Key cannot be null or empty.", nameof(key));
    // ...
}

public string GetString(string key)
{
    if (string.IsNullOrEmpty(key))
        throw new ArgumentException("Key cannot be null or empty.", nameof(key));
    // ...
}
```

**Impact**: Code duplication, harder to maintain.

**Solution**: Extract validation:

```csharp
private static void ValidateKey(string key)
{
    if (string.IsNullOrEmpty(key))
        throw new ArgumentException("Key cannot be null or empty.", nameof(key));
}
```

### 7. ❌ Missing Validation: Required Constants Not Checked

**Issue**: No validation that critical constants exist at startup.

**Problem**: Game may fail at runtime when accessing missing constants.

**Impact**: Poor error messages, hard to debug.

**Solution**: Add validation method:

```csharp
public void ValidateRequiredConstants(IEnumerable<string> requiredKeys)
{
    var missing = requiredKeys.Where(k => !Contains(k)).ToList();
    if (missing.Any())
    {
        throw new InvalidOperationException(
            $"Required constants are missing: {string.Join(", ", missing)}. " +
            "Ensure they are defined in the core mod."
        );
    }
}
```

## Architecture Concerns

### 8. ⚠️ Single Responsibility: Service Does Too Much

**Issue**: `ConstantsService` handles loading, caching, deserialization, and access.

**Current**: One class does everything.

**Better**: Separate concerns:

- `ConstantsLoader`: Loads constants from mods
- `ConstantsCache`: Caches deserialized values
- `ConstantsService`: Provides access API

**Recommendation**: Keep as-is for now (YAGNI), but be aware of this if it grows.

### 9. ⚠️ Missing Integration with ECS Systems

**Issue**: Design doesn't specify how systems access constants.

**Current**: Systems inject `IConstantsService` in constructor.

**Concerns**:

- Systems need to be updated to inject service
- No guidance on when to cache vs. access each frame
- No consideration for computed constants in systems

**Recommendation**:

- Document that systems should cache frequently-used constants
- Consider helper methods for common computed constants

### 10. ⚠️ No Event System Integration

**Issue**: Constants can't be changed at runtime, no events for changes.

**Current**: Constants loaded once, never change.

**Future Consideration**: If hot-reload is needed, consider:

- `ConstantsChangedEvent` when mods reload
- Systems subscribe to update cached values

**Recommendation**: Not needed now, but document as future enhancement.

## Mod System Integration Issues

### 11. ⚠️ Definition Type Naming

**Issue**: Uses `"Constants"` as definition type, but other types use plural (e.g., `"FontDefinitions"`).

**Current**: `"definitionType": "Constants"`

**Consistency Check**: Other types:

- `"FontDefinitions"`
- `"TileBehaviorDefinitions"`
- `"SpriteDefinitions"`

**Recommendation**: Use `"ConstantsDefinitions"` for consistency, OR document why `"Constants"` is different.

### 12. ⚠️ Missing Error Handling in LoadConstantsFromMods

**Issue**: Method silently continues if definition is null.

**Problem**:

```csharp
var definition = modManager.Registry.GetById<ConstantDefinition>(defId);
if (definition == null) continue; // Silent skip
```

**Impact**: Missing constants aren't logged, hard to debug.

**Solution**: Log warnings:

```csharp
if (definition == null)
{
    _logger.Warning("Failed to load constants definition '{DefId}'", defId);
    continue;
}
```

### 13. ⚠️ JSON Number Precision Loss

**Issue**: JSON numbers are `double`, conversion to `int`/`float` may lose precision.

**Problem**:

- `16.0` in JSON → `double` → `int` (OK)
- `16.7` in JSON → `double` → `int` (loses precision, but should fail validation)

**Impact**: Type mismatches may be silently accepted.

**Solution**: Add validation in `DeserializeConstant`:

```csharp
if (typeof(T) == typeof(int) && element.ValueKind == JsonValueKind.Number)
{
    var dbl = element.GetDouble();
    if (dbl != Math.Floor(dbl))
        throw new InvalidCastException($"Constant '{key}' is not an integer.");
}
```

## SOLID Principles Analysis

### Single Responsibility Principle ✅

- **Current**: Service handles loading, caching, and access
- **Status**: Acceptable for now, but could be split if it grows

### Open/Closed Principle ✅

- **Current**: Interface allows extension
- **Status**: Good - mods can override constants without code changes

### Liskov Substitution Principle ✅

- **Current**: Interface-based design
- **Status**: Good - implementations can be swapped

### Interface Segregation Principle ✅

- **Current**: Single focused interface
- **Status**: Good - interface is cohesive

### Dependency Inversion Principle ✅

- **Current**: Depends on `IModManager` abstraction
- **Status**: Good - follows DI pattern

## DRY Analysis

### Code Duplication Issues:

1. ❌ Key validation duplicated in `Get<T>()` and `GetString()`
2. ❌ Key lookup duplicated in multiple methods
3. ⚠️ Error message formatting similar across methods

### Recommendations:

- Extract `ValidateKey()` helper
- Extract `GetConstantElement()` helper
- Consider extracting error message formatting

## Performance Considerations

### Current Issues:

1. ❌ **Deserialization on every access** - should cache
2. ❌ **String allocation** (`GetRawText()`) - should cache deserialized values
3. ⚠️ **Dictionary lookup** - O(1), acceptable
4. ⚠️ **Type checking** - minimal overhead

### Recommendations:

- Cache deserialized values (critical)
- Consider type-specific caches if profiling shows issues
- Document that constants should be accessed once and cached in systems

## ECS Integration Considerations

### System Access Pattern:

```csharp
public class PlayerSystem : BaseSystem<World, float>
{
    private readonly IConstantsService _constants;
    private float _cachedMovementSpeed; // Cache in system
    
    public PlayerSystem(World world, IConstantsService constants, ...) : base(world)
    {
        _constants = constants ?? throw new ArgumentNullException(nameof(constants));
        // Cache frequently-used constants
        _cachedMovementSpeed = _constants.Get<float>("DefaultPlayerMovementSpeed");
    }
}
```

### Recommendations:

- Document that systems should cache constants used in Update/Render
- Consider helper methods for common constant groups
- Don't access constants in hot loops without caching

## Missing Features

### 1. Validation at Startup

- No check that required constants exist
- No validation of constant value ranges
- No check for type mismatches at load time

### 2. Documentation

- No XML docs on when to cache vs. access
- No guidance on computed constants
- No examples for system integration

### 3. Error Messages

- Error messages could be more helpful
- No suggestion of similar constant names if typo
- No indication of which mod should define missing constant

## Recommendations Summary

### Critical (Must Fix):

1. ✅ Fix service initialization timing (create after mods load)
2. ✅ Cache deserialized values (performance)
3. ✅ Add null validation for modManager
4. ✅ Fix exception handling in TryGet (don't swallow all exceptions)
5. ✅ Extract duplicate validation logic

### Important (Should Fix):

6. ⚠️ Add validation for required constants
7. ⚠️ Log warnings for missing definitions
8. ⚠️ Add precision validation for number conversions
9. ⚠️ Consider definition type naming consistency

### Nice to Have:

10. Consider splitting into loader/cache/service
11. Add event system integration for hot-reload
12. Add helper methods for computed constants
13. Improve error messages with suggestions

## Updated Design Recommendations

### Service Creation Pattern:

```csharp
// In MonoBallGame.LoadModsSynchronously(), after mods load:
var constantsService = new ConstantsService(modManager, logger);
constantsService.ValidateRequiredConstants(new[] { 
    "TileChunkSize", 
    "DefaultPlayerMovementSpeed",
    // ... other critical constants
});
Services.AddService(typeof(IConstantsService), constantsService);
```

### Caching Pattern:

```csharp
private readonly Dictionary<string, object> _valueCache = new();
private readonly Dictionary<string, JsonElement> _rawConstants = new();

public T Get<T>(string key) where T : struct
{
    ValidateKey(key);
    
    if (_valueCache.TryGetValue(key, out var cached) && cached is T typed)
        return typed;
    
    if (!_rawConstants.TryGetValue(key, out var element))
        throw new KeyNotFoundException(...);
    
    var value = DeserializeAndValidate<T>(key, element);
    _valueCache[key] = value;
    return value;
}
```

### Validation Pattern:

```csharp
private T DeserializeAndValidate<T>(string key, JsonElement element) where T : struct
{
    try
    {
        // Validate number precision for int
        if (typeof(T) == typeof(int) && element.ValueKind == JsonValueKind.Number)
        {
            var dbl = element.GetDouble();
            if (dbl != Math.Floor(dbl))
                throw new InvalidCastException($"Constant '{key}' must be an integer.");
        }
        
        return JsonSerializer.Deserialize<T>(
            element.GetRawText(),
            JsonSerializerOptionsFactory.Default
        );
    }
    catch (JsonException ex)
    {
        throw new InvalidCastException(
            $"Failed to deserialize constant '{key}' to {typeof(T).Name}: {ex.Message}",
            ex
        );
    }
}
```

