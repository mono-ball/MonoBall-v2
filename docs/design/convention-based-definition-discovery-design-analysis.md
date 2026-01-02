# Convention-Based Definition Discovery - Architecture Analysis

**Date:** 2025-01-XX  
**Status:** Critical Issues Identified  
**Related:** [Convention-Based Definition Discovery Design](./convention-based-definition-discovery-design.md)

---

## Executive Summary

This document analyzes the convention-based definition discovery design for architecture issues, ECS/event system concerns, SOLID/DRY violations, mod system problems, and code smells. Several critical issues have been identified that should be addressed before implementation.

---

## Critical Architecture Issues

### 1. ❌ **Performance: JSON Parsing on Every File**

**Issue**: The design parses JSON for every file to check for `$type` field, even when path-based inference would work.

**Location**: `InferDefinitionType()` method, lines 312-326

**Problem**:
```csharp
// Current design parses JSON FIRST, before checking path mappings
if (jsonDoc != null && 
    jsonDoc.RootElement.TryGetProperty("$type", out var typeElement))
{
    return explicitType; // Early return
}
// Then checks path mappings...
```

**Impact**:
- **Unnecessary I/O**: Reading and parsing JSON for every file
- **Performance**: JSON parsing is expensive (~50-200μs per file)
- **Memory**: Allocates `JsonDocument` objects that may not be needed
- **Scalability**: With 1000+ definition files, this adds 50-200ms to mod loading

**Solution**: **Reverse the order** - check path mappings first, only parse JSON if needed:
```csharp
// Tier 1: Check hardcoded mappings FIRST (no I/O)
foreach (var (pathPattern, type) in KnownPathMappings.OrderByDescending(...))
{
    if (normalizedPath.StartsWith(pathPattern + "/", ...))
        return type; // Fast path - no JSON parsing needed
}

// Tier 2: Directory inference (no I/O)
if (normalizedPath.StartsWith("Definitions/Assets/", ...))
    return InferFromDirectory(...);

// Tier 3: Only NOW parse JSON if path didn't match (rare case)
if (jsonDoc == null)
    jsonDoc = ParseJsonFile(filePath); // Lazy parsing
if (jsonDoc?.RootElement.TryGetProperty("$type", ...))
    return explicitType;
```

**Severity**: 🔴 **HIGH** - Performance critical path

---

### 2. ❌ **Inefficient Path Matching Algorithm**

**Issue**: Linear search through dictionary with `OrderByDescending()` on every call.

**Location**: `InferDefinitionType()`, line 329-342

**Problem**:
```csharp
// This runs on EVERY file - O(n*m) where n=files, m=mappings
foreach (var (pathPattern, type) in KnownPathMappings
    .OrderByDescending(kvp => kvp.Key.Length)) // ⚠️ Sorts on every call!
{
    if (normalizedPath.StartsWith(pathPattern + "/", ...))
        return type;
}
```

**Impact**:
- **O(n log n) sorting** on every file (50+ mappings × 1000+ files = expensive)
- **No caching** of sorted order
- **String allocations** for path concatenation (`pathPattern + "/"`)

**Solution**: **Use Trie or pre-sorted array**:
```csharp
// Cache sorted mappings as static readonly
private static readonly (string Path, string Type)[] SortedPathMappings = 
    KnownPathMappings
        .OrderByDescending(kvp => kvp.Key.Length)
        .Select(kvp => (kvp.Key, kvp.Value))
        .ToArray();

// Or use Trie for O(path_length) lookup
private static readonly PathTrie<string> PathMappingsTrie = BuildPathTrie(...);
```

**Severity**: 🟡 **MEDIUM** - Performance optimization opportunity

---

### 3. ❌ **String Splitting Allocations**

**Issue**: Multiple `Split('/')` calls create temporary arrays.

**Location**: Lines 348, 365, 399

**Problem**:
```csharp
var parts = normalizedPath.Split('/'); // ⚠️ Allocates array
if (parts.Length >= 3)
{
    var typeName = parts[2]; // Uses array
}
```

**Impact**:
- **Allocations**: Creates string arrays on heap
- **GC pressure**: With 1000+ files, creates 3000+ temporary arrays
- **Performance**: String splitting is relatively slow

**Solution**: **Use `Span<char>` or `ReadOnlySpan<char>` with manual parsing**:
```csharp
// Use Span-based parsing (no allocations)
var span = normalizedPath.AsSpan();
var startIndex = FindNthSlash(span, 2); // Manual index finding
var endIndex = FindNextSlash(span, startIndex);
var typeName = normalizedPath.Substring(startIndex, endIndex - startIndex);
```

**Severity**: 🟡 **MEDIUM** - GC pressure concern

---

### 4. ❌ **No Caching of Inferred Types**

**Issue**: Type inference runs on every file load, even for same paths.

**Location**: `LoadModDefinitions()`, line 500

**Problem**:
- Same directory structure = same inference logic repeated
- No memoization of path → type mappings
- Could cache per-directory instead of per-file

**Solution**: **Cache inferred types per directory**:
```csharp
private readonly Dictionary<string, string> _directoryTypeCache = new();

private string InferDefinitionType(string filePath, ...)
{
    var directory = Path.GetDirectoryName(filePath);
    if (_directoryTypeCache.TryGetValue(directory, out var cachedType))
        return cachedType;
    
    var inferredType = InferTypeFromPath(filePath, ...);
    _directoryTypeCache[directory] = inferredType;
    return inferredType;
}
```

**Severity**: 🟢 **LOW** - Optimization opportunity

---

## Arch ECS / Event System Issues

### 5. ❌ **No Event for Definition Discovery**

**Issue**: Definition discovery happens silently - no events fired for systems to react.

**Location**: `LoadModDefinitions()`, entire method

**Problem**:
- Systems can't react to new definitions being loaded
- No `DefinitionDiscoveredEvent` or `DefinitionLoadedEvent`
- Hot-reload systems can't detect changes
- Script systems can't subscribe to definition loading

**Solution**: **Fire events during discovery**:
```csharp
private void LoadModDefinitions(ModManifest mod, List<string> errors)
{
    foreach (var jsonFile in jsonFiles)
    {
        var definitionType = InferDefinitionType(...);
        var metadata = LoadDefinitionFromFile(...);
        
        // Fire event for systems to react
        var discoveredEvent = new DefinitionDiscoveredEvent
        {
            ModId = mod.Id,
            DefinitionType = definitionType,
            DefinitionId = metadata.Id,
            FilePath = jsonFile
        };
        EventBus.Send(ref discoveredEvent);
    }
}
```

**Severity**: 🟡 **MEDIUM** - Missing integration point

---

### 6. ❌ **No ECS Component for Definition Metadata**

**Issue**: Definitions are stored in `DefinitionRegistry` but not accessible via ECS queries.

**Location**: Entire design - no ECS integration

**Problem**:
- Can't query definitions via ECS `World.Query()`
- Can't attach definition metadata to entities
- No component-based access pattern

**Solution**: **Create ECS component for definition references**:
```csharp
public struct DefinitionReferenceComponent
{
    public string DefinitionId { get; set; }
    public string DefinitionType { get; set; }
}

// Systems can query entities with definitions
World.Query(new QueryDescription().WithAll<DefinitionReferenceComponent>(), 
    (ref DefinitionReferenceComponent def) => { ... });
```

**Severity**: 🟢 **LOW** - Architectural enhancement (may not be needed)

---

## SOLID / DRY Violations

### 7. ❌ **Single Responsibility Violation**

**Issue**: `InferDefinitionType()` does too much - path matching, JSON parsing, directory inference, mod.json checking.

**Location**: `InferDefinitionType()`, entire method (140+ lines)

**Problem**:
- **Too many responsibilities**: Path matching, JSON parsing, inference logic, mod.json lookup
- **Hard to test**: Requires mocking multiple dependencies
- **Hard to maintain**: Changes to one tier affect entire method

**Solution**: **Extract separate classes**:
```csharp
public interface ITypeInferenceStrategy
{
    string? InferType(string filePath, JsonDocument? jsonDoc, ModManifest mod);
}

public class HardcodedPathInferenceStrategy : ITypeInferenceStrategy { ... }
public class DirectoryNameInferenceStrategy : ITypeInferenceStrategy { ... }
public class JsonTypeOverrideStrategy : ITypeInferenceStrategy { ... }
public class ModManifestInferenceStrategy : ITypeInferenceStrategy { ... }

// Chain of responsibility pattern
private readonly ITypeInferenceStrategy[] _inferenceStrategies = new[]
{
    new HardcodedPathInferenceStrategy(),
    new DirectoryNameInferenceStrategy(),
    new JsonTypeOverrideStrategy(),
    new ModManifestInferenceStrategy()
};

private string InferDefinitionType(...)
{
    foreach (var strategy in _inferenceStrategies)
    {
        var type = strategy.InferType(filePath, jsonDoc, mod);
        if (type != null)
            return type;
    }
    return "Unknown";
}
```

**Severity**: 🟡 **MEDIUM** - Code maintainability

---

### 8. ❌ **DRY Violation: Duplicate Path Matching Logic**

**Issue**: Path matching logic duplicated in multiple places.

**Location**: 
- `InferDefinitionType()` - hardcoded mappings (line 329)
- `LoadModDefinitions()` - script/behavior path checks (lines 482-484, 493-495)
- Legacy `contentFolders` support (if implemented)

**Problem**:
- Same path matching logic in 3+ places
- Changes require updates in multiple locations
- Inconsistent behavior possible

**Solution**: **Extract to shared utility**:
```csharp
public static class PathMatcher
{
    public static bool MatchesPath(string filePath, string pattern, StringComparison comparison = StringComparison.Ordinal)
    {
        var normalized = ModPathNormalizer.Normalize(filePath);
        return normalized.StartsWith(pattern + "/", comparison) || normalized == pattern;
    }
    
    public static bool IsInDirectory(string filePath, string directory)
    {
        return MatchesPath(filePath, directory);
    }
}
```

**Severity**: 🟡 **MEDIUM** - Code duplication

---

### 9. ❌ **Open/Closed Violation: Hardcoded Mappings**

**Issue**: Adding new definition types requires modifying `KnownPathMappings` dictionary.

**Location**: `KnownPathMappings` dictionary, lines 169-213

**Problem**:
- **Not extensible**: Can't add mappings without code changes
- **Violates Open/Closed Principle**: Should be open for extension, closed for modification
- **Tight coupling**: Engine code knows about all definition types

**Solution**: **Use plugin/registration pattern**:
```csharp
public interface IPathMappingProvider
{
    Dictionary<string, string> GetPathMappings();
}

public class DefaultPathMappingProvider : IPathMappingProvider
{
    public Dictionary<string, string> GetPathMappings() => KnownPathMappings;
}

// Mods can register custom mappings
public class ModPathMappingProvider : IPathMappingProvider
{
    private readonly ModManifest _mod;
    public Dictionary<string, string> GetPathMappings() => _mod.CustomDefinitionTypes;
}

// Composite provider merges all mappings
public class CompositePathMappingProvider : IPathMappingProvider
{
    private readonly IPathMappingProvider[] _providers;
    public Dictionary<string, string> GetPathMappings()
    {
        var result = new Dictionary<string, string>();
        foreach (var provider in _providers)
            foreach (var (path, type) in provider.GetPathMappings())
                result[path] = type;
        return result;
    }
}
```

**Severity**: 🟢 **LOW** - Design improvement (current approach may be acceptable)

---

## Mod System Issues

### 10. ❌ **No Validation of Inferred Types**

**Issue**: System infers types but doesn't validate they're correct or expected.

**Location**: `InferDefinitionType()`, returns "Unknown" on failure

**Problem**:
- **Silent failures**: Returns "Unknown" without validation
- **No schema validation**: Doesn't check if inferred type matches JSON schema
- **No mod.json validation**: Doesn't validate against `customDefinitionTypes` if present

**Solution**: **Add validation layer**:
```csharp
private string InferDefinitionType(...)
{
    var inferredType = InferTypeFromPath(...);
    
    // Validate against mod.json customDefinitionTypes if present
    if (mod.CustomDefinitionTypes != null && 
        !mod.CustomDefinitionTypes.ContainsValue(inferredType))
    {
        _logger.Warning(
            "Inferred type '{Type}' not declared in mod.json customDefinitionTypes for {Path}",
            inferredType, filePath
        );
    }
    
    // Validate JSON schema matches inferred type (if schemas available)
    if (HasSchemaForType(inferredType))
    {
        ValidateJsonAgainstSchema(jsonDoc, inferredType);
    }
    
    return inferredType;
}
```

**Severity**: 🟡 **MEDIUM** - Data integrity concern

---

### 11. ❌ **Backward Compatibility Complexity**

**Issue**: Dual support for `contentFolders` and convention-based discovery adds complexity.

**Location**: Migration strategy, Phase 1 (lines 577-609)

**Problem**:
- **Two code paths**: Legacy and new system must be maintained
- **Testing burden**: Must test both paths
- **Migration risk**: Mods might break during transition
- **Code duplication**: Similar logic in two places

**Solution**: **Consider breaking change** (per project rules - no backward compatibility):
```csharp
// Per project rules: "NO BACKWARD COMPATIBILITY - Refactor APIs freely"
// Remove contentFolders support immediately, update all mods
private void LoadModDefinitions(ModManifest mod, List<string> errors)
{
    // Only convention-based discovery - no legacy support
    LoadModDefinitionsConventionBased(mod, errors);
}
```

**Severity**: 🟡 **MEDIUM** - Conflicts with project philosophy

---

### 12. ❌ **No Mod Dependency Resolution for Types**

**Issue**: Custom types from one mod aren't available to dependent mods.

**Location**: Entire design - no dependency resolution

**Problem**:
- Mod A defines `Quest` type
- Mod B depends on Mod A and uses `Quest` definitions
- Mod B's definitions might not be discovered correctly
- No way to ensure Mod A's types are loaded first

**Solution**: **Resolve type dependencies**:
```csharp
private void LoadModDefinitions(ModManifest mod, List<string> errors)
{
    // Load dependencies first to ensure their types are available
    foreach (var dependency in mod.Dependencies)
    {
        var depMod = GetModById(dependency);
        if (depMod != null && !depMod.DefinitionsLoaded)
            LoadModDefinitions(depMod, errors);
    }
    
    // Now load this mod's definitions (can reference dependency types)
    LoadModDefinitionsConventionBased(mod, errors);
}
```

**Severity**: 🟢 **LOW** - Edge case (may already be handled by load order)

---

## Code Smells

### 13. ❌ **Magic Strings: "Unknown" Return Value**

**Issue**: Returns magic string "Unknown" instead of nullable or Result type.

**Location**: `InferDefinitionType()`, line 438

**Problem**:
```csharp
return "Unknown"; // ⚠️ Magic string
```

**Impact**:
- **No type safety**: String can be typo'd elsewhere
- **No validation**: Can't distinguish "Unknown" from actual type name
- **Error handling**: Hard to detect failures

**Solution**: **Use Result type or throw exception**:
```csharp
// Option 1: Throw exception (fail fast)
throw new InvalidOperationException(
    $"Could not infer definition type for {filePath}. " +
    "Ensure file follows convention-based directory structure or specify $type field."
);

// Option 2: Return nullable
private string? InferDefinitionType(...)
{
    // ... inference logic ...
    return null; // Indicates failure
}

// Option 3: Result type
private Result<string, string> InferDefinitionType(...)
{
    // ... inference logic ...
    return Result.Error("Could not infer type");
}
```

**Severity**: 🟡 **MEDIUM** - Error handling improvement

---

### 14. ❌ **Long Parameter List**

**Issue**: `InferDefinitionType()` has 3 parameters, some optional.

**Location**: `InferDefinitionType()` signature, line 304

**Problem**:
```csharp
private string InferDefinitionType(
    string filePath,      // Required
    JsonDocument? jsonDoc, // Optional - might be null
    ModManifest mod        // Required
)
```

**Solution**: **Use parameter object**:
```csharp
private struct TypeInferenceContext
{
    public string FilePath { get; set; }
    public JsonDocument? JsonDocument { get; set; }
    public ModManifest Mod { get; set; }
}

private string InferDefinitionType(TypeInferenceContext context)
{
    // Use context.FilePath, context.JsonDocument, context.Mod
}
```

**Severity**: 🟢 **LOW** - Minor refactoring

---

### 15. ❌ **Inconsistent Error Handling**

**Issue**: Some errors are logged, some are added to errors list, some throw exceptions.

**Location**: Throughout design

**Problem**:
- `LoadModDefinitions()` adds to `errors` list
- `InferDefinitionType()` logs warnings
- `LoadDefinitionFromFile()` might throw exceptions
- Inconsistent error handling strategy

**Solution**: **Unify error handling**:
```csharp
// Consistent error handling pattern
private Result<DefinitionMetadata, string> LoadDefinitionFromFile(...)
{
    try
    {
        // ... loading logic ...
        return Result.Success(metadata);
    }
    catch (Exception ex)
    {
        return Result.Error($"Failed to load definition: {ex.Message}");
    }
}

// Caller handles errors consistently
var result = LoadDefinitionFromFile(...);
if (result.IsError)
{
    errors.Add(result.Error);
    _logger.Warning("Failed to load definition: {Error}", result.Error);
    continue;
}
```

**Severity**: 🟡 **MEDIUM** - Error handling consistency

---

### 16. ❌ **God Method: `InferDefinitionType()`**

**Issue**: Method is 140+ lines and handles multiple concerns.

**Location**: `InferDefinitionType()`, lines 304-439

**Problem**:
- **Too long**: Should be < 20-30 lines per method
- **Too complex**: Cyclomatic complexity too high
- **Hard to test**: Requires many test cases
- **Hard to understand**: Multiple nested conditionals

**Solution**: **Break into smaller methods** (already suggested in #7):
```csharp
private string InferDefinitionType(...)
{
    return TryInferFromJsonType(jsonDoc) ??
           TryInferFromHardcodedMapping(filePath) ??
           TryInferFromDirectory(filePath) ??
           TryInferFromModManifest(filePath, mod) ??
           "Unknown";
}

private string? TryInferFromJsonType(JsonDocument? jsonDoc) { ... }
private string? TryInferFromHardcodedMapping(string filePath) { ... }
private string? TryInferFromDirectory(string filePath) { ... }
private string? TryInferFromModManifest(string filePath, ModManifest mod) { ... }
```

**Severity**: 🟡 **MEDIUM** - Code readability

---

## Summary of Issues

### Critical (Must Fix)
1. ❌ **Performance: JSON parsing on every file** (🔴 HIGH)
2. ❌ **Inefficient path matching algorithm** (🟡 MEDIUM)

### Important (Should Fix)
3. ❌ **String splitting allocations** (🟡 MEDIUM)
4. ❌ **No event for definition discovery** (🟡 MEDIUM)
5. ❌ **Single Responsibility violation** (🟡 MEDIUM)
6. ❌ **DRY violation: duplicate path matching** (🟡 MEDIUM)
7. ❌ **No validation of inferred types** (🟡 MEDIUM)
8. ❌ **Backward compatibility complexity** (🟡 MEDIUM)
9. ❌ **Magic strings: "Unknown" return value** (🟡 MEDIUM)
10. ❌ **Inconsistent error handling** (🟡 MEDIUM)
11. ❌ **God method: `InferDefinitionType()`** (🟡 MEDIUM)

### Nice to Have (Optimizations)
12. ✅ **No caching of inferred types** (🟢 LOW)
13. ✅ **No ECS component for definition metadata** (🟢 LOW)
14. ✅ **Open/Closed violation: hardcoded mappings** (🟢 LOW)
15. ✅ **No mod dependency resolution for types** (🟢 LOW)
16. ✅ **Long parameter list** (🟢 LOW)

---

## Recommended Fixes Priority

### Phase 1: Critical Performance Fixes
1. **Reverse JSON parsing order** - Check path mappings first, parse JSON only if needed
2. **Cache sorted path mappings** - Pre-sort mappings, avoid `OrderByDescending()` on every call
3. **Use Span-based path parsing** - Reduce allocations from `Split('/')`

### Phase 2: Architecture Improvements
4. **Extract inference strategies** - Use Chain of Responsibility pattern
5. **Add definition discovery events** - Fire events for systems to react
6. **Unify error handling** - Consistent error handling pattern

### Phase 3: Code Quality
7. **Break down god method** - Split `InferDefinitionType()` into smaller methods
8. **Extract path matching utility** - DRY violation fix
9. **Add type validation** - Validate inferred types against mod.json

### Phase 4: Optional Enhancements
10. **Add caching** - Cache inferred types per directory
11. **Consider breaking change** - Remove backward compatibility per project rules
12. **ECS integration** - Add definition reference components (if needed)

---

## Conclusion

The convention-based definition discovery design is **sound overall** but has several **performance and architecture issues** that should be addressed before implementation. The most critical issues are:

1. **Performance**: JSON parsing should be lazy (only when needed)
2. **Architecture**: Extract inference strategies for better separation of concerns
3. **Error handling**: Unify error handling approach

With these fixes, the design will be production-ready and align with MonoBall's architecture principles.
