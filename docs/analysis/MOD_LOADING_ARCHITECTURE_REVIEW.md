# Mod Loading Architecture Review

**Date:** 2025-01-XX  
**Reviewer:** AI Code Review  
**Scope:** Comprehensive review of mod loading changes for architecture issues, SOLID/DRY principles, .cursorrules compliance, code smells, and potential bugs

---

## Executive Summary

This document provides a comprehensive analysis of the mod loading system, identifying architecture issues, SOLID/DRY violations, .cursorrules compliance problems, code smells, and potential bugs. The review covers:

- `ModLoader.cs` - Core mod loading logic
- `ModManager.cs` - Mod management facade
- `ModValidator.cs` - Mod validation logic
- Type inference strategies - Convention-based definition discovery
- `DefinitionRegistry.cs` - Definition storage and retrieval

**Overall Assessment:** The mod loading system is well-architected with good separation of concerns, but several issues need attention:

1. **Critical Issues:** 3
2. **Architecture Issues:** 5
3. **SOLID/DRY Violations:** 4
4. **Code Smells:** 6
5. **Potential Bugs:** 4

---

## 1. Critical Issues

### 1.1 ModLoader.cs: Duplicate mod.manifest Reading

**Location:** `ModLoader.cs:166-199` and `ModLoader.cs:328-383`

**Issue:** The `mod.manifest` file is read and parsed twice:
- Once in `DetermineCoreModId()` (line 168-199)
- Again in `ResolveLoadOrder()` (line 328-383)

**Impact:**
- Unnecessary I/O operations
- Code duplication (DRY violation)
- Potential inconsistency if file changes between reads
- Performance overhead

**Recommendation:**
```csharp
// Cache the root manifest after first read
private RootModManifest? _cachedRootManifest;

private RootModManifest? LoadRootManifest(List<string> errors)
{
    if (_cachedRootManifest != null)
        return _cachedRootManifest;
    
    var rootManifestPath = Path.Combine(_modsDirectory, "mod.manifest");
    if (!File.Exists(rootManifestPath))
    {
        errors.Add("mod.manifest not found or empty. Cannot determine core mod (slot 0).");
        return null;
    }
    
    try
    {
        var rootManifestContent = File.ReadAllText(rootManifestPath);
        _cachedRootManifest = JsonSerializer.Deserialize<RootModManifest>(
            rootManifestContent,
            JsonSerializerOptionsFactory.ForManifests
        );
        return _cachedRootManifest;
    }
    catch (Exception ex)
    {
        errors.Add($"Error reading root mod.manifest: {ex.Message}");
        return null;
    }
}
```

**Priority:** HIGH

---

### 1.2 ModLoader.cs: JsonDocument Disposal Logic Complexity

**Location:** `ModLoader.cs:481-529`

**Issue:** Complex JsonDocument disposal logic with `createdJsonDoc` flag is error-prone and violates single responsibility.

**Problems:**
- Disposal logic mixed with loading logic
- Easy to forget disposal in error paths
- Complex state tracking (`createdJsonDoc` flag)
- Potential memory leaks if exception occurs before finally block

**Current Code:**
```csharp
JsonDocument? jsonDoc = null;
bool createdJsonDoc = false; // Track if we created the document
string definitionType;

try
{
    definitionType = InferDefinitionType(jsonFile, null, mod);
}
catch (InvalidOperationException)
{
    var jsonContent = mod.ModSource.ReadTextFile(jsonFile);
    jsonDoc = JsonDocument.Parse(jsonContent);
    createdJsonDoc = true; // Mark as created by us
    definitionType = InferDefinitionType(jsonFile, jsonDoc, mod);
}
finally
{
    if (createdJsonDoc)
        jsonDoc?.Dispose();
}
```

**Recommendation:** Use `using` statements for automatic disposal:
```csharp
string definitionType;
JsonDocument? jsonDoc = null;

try
{
    // Attempt inference without parsing JSON (fast path)
    definitionType = InferDefinitionType(jsonFile, null, mod);
}
catch (InvalidOperationException)
{
    // Path-based inference failed - parse JSON for $type field (lazy parsing)
    var jsonContent = mod.ModSource.ReadTextFile(jsonFile);
    using (var tempDoc = JsonDocument.Parse(jsonContent))
    {
        definitionType = InferDefinitionType(jsonFile, tempDoc, mod);
        // Clone root element if needed for later use
        jsonDoc = JsonDocument.Parse(jsonContent); // Parse again for reuse
    }
}

// Use jsonDoc if available, otherwise parse fresh
try
{
    loadResult = LoadDefinitionFromFile(
        mod.ModSource,
        jsonFile,
        definitionType,
        mod,
        jsonDoc
    );
}
finally
{
    jsonDoc?.Dispose();
}
```

**Better Approach:** Refactor to use a helper method that handles disposal:
```csharp
private string InferDefinitionTypeWithJsonFallback(
    string filePath,
    ModManifest mod,
    out JsonDocument? jsonDoc)
{
    jsonDoc = null;
    
    try
    {
        return InferDefinitionType(filePath, null, mod);
    }
    catch (InvalidOperationException)
    {
        var jsonContent = mod.ModSource.ReadTextFile(filePath);
        jsonDoc = JsonDocument.Parse(jsonContent);
        return InferDefinitionType(filePath, jsonDoc, mod);
    }
}
```

**Priority:** HIGH

---

### 1.3 ModLoader.cs: ValidateBehaviorDefinitions Throws Exceptions After Adding to Errors List

**Location:** `ModLoader.cs:751-996`

**Issue:** `ValidateBehaviorDefinitions()` adds errors to the list AND throws exceptions. This violates the fail-fast principle inconsistently - some errors are collected, others throw immediately.

**Problems:**
- Inconsistent error handling (some errors collected, others thrown)
- Errors added to list but then exception thrown (error list becomes useless)
- Violates single responsibility (validation vs error collection)
- Makes error recovery difficult

**Current Code Pattern:**
```csharp
errors.Add(errorMessage);
_logger.Error(errorMessage);
throw new InvalidOperationException(errorMessage);
```

**Recommendation:** Choose one approach:
- **Option 1:** Fail fast - throw immediately, don't collect errors
- **Option 2:** Collect all errors, throw at end if any critical errors

**Recommended Fix (Fail Fast):**
```csharp
private void ValidateBehaviorDefinitions(List<string> errors)
{
    var behaviorDefinitionIds = _registry.GetByType("Behavior").ToList();
    if (behaviorDefinitionIds.Count == 0)
        return;

    foreach (var behaviorId in behaviorDefinitionIds)
    {
        var behaviorDef = _registry.GetById<BehaviorDefinition>(behaviorId);
        if (behaviorDef == null)
            continue;

        // Fail fast - throw immediately with clear error
        if (string.IsNullOrWhiteSpace(behaviorDef.ScriptId))
        {
            throw new InvalidOperationException(
                $"BehaviorDefinition '{behaviorId}' has empty scriptId. " +
                "BehaviorDefinition must reference a valid ScriptDefinition."
            );
        }

        var scriptDef = _registry.GetById<ScriptDefinition>(behaviorDef.ScriptId);
        if (scriptDef == null)
        {
            throw new InvalidOperationException(
                $"ScriptDefinition '{behaviorDef.ScriptId}' not found for " +
                $"BehaviorDefinition '{behaviorId}'. Ensure the script definition exists and is loaded."
            );
        }

        // ... rest of validation
    }
}
```

**Priority:** HIGH

---

## 2. Architecture Issues

### 2.1 ModManager.cs: Optional Logger Parameter Violates Fail-Fast Principle

**Location:** `ModManager.cs:27-35`

**Issue:** Logger parameter is optional (`ILogger? logger = null`) but then throws `ArgumentNullException` if null. This violates the "no fallback code" rule from .cursorrules.

**Current Code:**
```csharp
public ModManager(string? modsDirectory = null, ILogger? logger = null)
{
    modsDirectory ??= ModsPathResolver.FindModsDirectory();
    // ... path resolution ...
    
    _logger = logger ?? throw new ArgumentNullException(nameof(logger));
}
```

**Problem:** Optional parameter suggests logger is optional, but it's actually required. This is confusing and violates fail-fast principle.

**Recommendation:** Make logger required:
```csharp
public ModManager(string? modsDirectory = null, ILogger logger)
{
    _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    // ... rest of constructor
}
```

**Priority:** MEDIUM

---

### 2.2 ModLoader.cs: LoadModDefinitions Has Too Many Responsibilities

**Location:** `ModLoader.cs:459-555`

**Issue:** `LoadModDefinitions()` does too much:
1. Enumerates JSON files
2. Infers definition types
3. Parses JSON (with error handling)
4. Loads definitions
5. Fires events
6. Handles JsonDocument disposal

**Violation:** Single Responsibility Principle (SRP)

**Recommendation:** Extract methods:
```csharp
private void LoadModDefinitions(ModManifest mod, List<string> errors)
{
    if (mod.ModSource == null)
        throw new InvalidOperationException(
            $"Mod '{mod.Id}' has no ModSource. Mods must have a valid ModSource to load definitions."
        );

    _logger.Debug("Loading definitions for mod {ModId} using convention-based discovery", mod.Id);

    var jsonFiles = mod.ModSource.EnumerateFiles("*.json", SearchOption.AllDirectories);
    
    foreach (var jsonFile in jsonFiles)
    {
        if (jsonFile.Equals("mod.json", StringComparison.OrdinalIgnoreCase))
            continue;

        ProcessDefinitionFile(jsonFile, mod, errors);
    }
}

private void ProcessDefinitionFile(string jsonFile, ModManifest mod, List<string> errors)
{
    var (definitionType, jsonDoc) = InferDefinitionTypeWithJsonFallback(jsonFile, mod);
    
    try
    {
        var loadResult = LoadDefinitionFromFile(
            mod.ModSource!,
            jsonFile,
            definitionType,
            mod,
            jsonDoc
        );

        if (loadResult.IsError)
        {
            errors.Add(loadResult.Error!);
            _logger.Warning(
                "Failed to load definition from mod {ModId}, file {FilePath}: {Error}",
                mod.Id,
                jsonFile,
                loadResult.Error
            );
            return;
        }

        FireDefinitionDiscoveredEvent(mod, definitionType, jsonFile, loadResult.Metadata!);
    }
    finally
    {
        jsonDoc?.Dispose();
    }
}
```

**Priority:** MEDIUM

---

### 2.3 ModLoader.cs: ResolveLoadOrder Has Duplicate Logic

**Location:** `ModLoader.cs:322-406`

**Issue:** `ResolveLoadOrder()` has duplicate logic for reading `mod.manifest` and adding mods to `_loadedMods`.

**Problems:**
- Duplicate `mod.manifest` reading (see Critical Issue 1.1)
- Duplicate logic for adding mods to `_loadedMods` (lines 373-375 and 401-403)
- Complex conditional logic mixing two different ordering strategies

**Recommendation:** Extract methods:
```csharp
private List<ModManifest> ResolveLoadOrder(
    List<ModManifest> mods,
    string coreModId,
    List<string> errors)
{
    var rootManifest = LoadRootManifest(errors);
    
    if (rootManifest?.ModOrder != null && rootManifest.ModOrder.Count > 0)
    {
        return ResolveLoadOrderFromManifest(mods, coreModId, rootManifest, errors);
    }
    
    return ResolveLoadOrderByPriority(mods, coreModId, errors);
}

private List<ModManifest> ResolveLoadOrderFromManifest(
    List<ModManifest> mods,
    string coreModId,
    RootModManifest rootManifest,
    List<string> errors)
{
    var orderedMods = new List<ModManifest>();
    var modsById = mods.ToDictionary(m => m.Id);

    foreach (var modId in rootManifest.ModOrder)
    {
        if (modId == coreModId)
            continue;

        if (modsById.TryGetValue(modId, out var mod))
            orderedMods.Add(mod);
        else
            errors.Add($"Mod '{modId}' specified in root mod.manifest not found");
    }

    // Add mods not in manifest
    foreach (var mod in mods)
    {
        if (!orderedMods.Contains(mod) && mod.Id != coreModId)
        {
            orderedMods.Add(mod);
            errors.Add($"Mod '{mod.Id}' not specified in root mod.manifest, added at end");
        }
    }

    AddToLoadedMods(orderedMods);
    return orderedMods;
}

private List<ModManifest> ResolveLoadOrderByPriority(
    List<ModManifest> mods,
    string coreModId,
    List<string> errors)
{
    var sortedMods = new List<ModManifest>();
    var processed = new HashSet<string>();
    var processing = new HashSet<string>();

    var modsByPriority = mods
        .Where(m => m.Id != coreModId)
        .OrderBy(m => m.Priority)
        .ThenBy(m => m.Id)
        .ToList();

    foreach (var mod in modsByPriority)
    {
        if (!processed.Contains(mod.Id))
            ResolveDependencies(mod, mods, sortedMods, processed, processing, errors);
    }

    AddToLoadedMods(sortedMods);
    return sortedMods;
}

private void AddToLoadedMods(IEnumerable<ModManifest> mods)
{
    foreach (var mod in mods)
    {
        if (!_loadedMods.Contains(mod))
            _loadedMods.Add(mod);
    }
}
```

**Priority:** MEDIUM

---

### 2.4 ModValidator.cs: CollectDefinitionIds Does Too Much

**Location:** `ModValidator.cs:216-297`

**Issue:** `CollectDefinitionIds()` does multiple things:
1. Validates mod source exists
2. Enumerates JSON files
3. Parses JSON files
4. Collects definition IDs
5. Validates shader definitions

**Violation:** Single Responsibility Principle (SRP)

**Recommendation:** Extract methods:
```csharp
private void CollectDefinitionIds(
    ModManifest manifest,
    IModSource modSource,
    Dictionary<string, List<DefinitionLocation>> definitionIds,
    List<ValidationIssue> issues)
{
    if (modSource == null)
    {
        issues.Add(new ValidationIssue
        {
            Severity = ValidationSeverity.Error,
            Message = $"Mod '{manifest.Id}' has no ModSource",
            ModId = manifest.Id,
            FilePath = string.Empty,
        });
        return;
    }

    var jsonFiles = modSource.EnumerateFiles("*.json", SearchOption.AllDirectories);

    foreach (var jsonFile in jsonFiles)
    {
        if (jsonFile.Equals("mod.json", StringComparison.OrdinalIgnoreCase))
            continue;

        ProcessDefinitionFileForValidation(jsonFile, manifest, modSource, definitionIds, issues);
    }
}

private void ProcessDefinitionFileForValidation(
    string jsonFile,
    ModManifest manifest,
    IModSource modSource,
    Dictionary<string, List<DefinitionLocation>> definitionIds,
    List<ValidationIssue> issues)
{
    try
    {
        var jsonContent = modSource.ReadTextFile(jsonFile);
        using var jsonDoc = JsonDocument.Parse(jsonContent);

        CollectDefinitionId(jsonFile, manifest, jsonDoc, definitionIds);
        ValidateShaderDefinitionIfApplicable(jsonFile, jsonDoc, modSource, issues);
    }
    catch (JsonException ex)
    {
        issues.Add(new ValidationIssue
        {
            Severity = ValidationSeverity.Error,
            Message = $"Invalid JSON in definition file: {ex.Message}",
            ModId = manifest.Id,
            FilePath = jsonFile,
        });
    }
    catch
    {
        // Skip other errors during validation
    }
}
```

**Priority:** MEDIUM

---

### 2.5 ModLoader.cs: ValidateBehaviorDefinitions Should Be Extracted

**Location:** `ModLoader.cs:751-996`

**Issue:** `ValidateBehaviorDefinitions()` is a 245-line method that validates behavior definitions. This logic belongs in `ModValidator` or a separate validation service.

**Problems:**
- Violates Single Responsibility Principle
- Makes `ModLoader` responsible for validation logic
- Hard to test in isolation
- Duplicates validation concerns (ModValidator exists but doesn't validate behavior definitions)

**Recommendation:** Move to `ModValidator`:
```csharp
// In ModValidator.cs
public void ValidateBehaviorDefinitions(DefinitionRegistry registry, List<ValidationIssue> issues)
{
    var behaviorDefinitionIds = registry.GetByType("Behavior").ToList();
    if (behaviorDefinitionIds.Count == 0)
        return;

    foreach (var behaviorId in behaviorDefinitionIds)
    {
        ValidateBehaviorDefinition(behaviorId, registry, issues);
    }
}

private void ValidateBehaviorDefinition(
    string behaviorId,
    DefinitionRegistry registry,
    List<ValidationIssue> issues)
{
    var behaviorDef = registry.GetById<BehaviorDefinition>(behaviorId);
    if (behaviorDef == null)
        return;

    // Validate scriptId
    if (string.IsNullOrWhiteSpace(behaviorDef.ScriptId))
    {
        issues.Add(new ValidationIssue
        {
            Severity = ValidationSeverity.Error,
            Message = $"BehaviorDefinition '{behaviorId}' has empty scriptId.",
            ModId = string.Empty,
            FilePath = string.Empty,
        });
        return;
    }

    // Validate script exists
    var scriptDef = registry.GetById<ScriptDefinition>(behaviorDef.ScriptId);
    if (scriptDef == null)
    {
        issues.Add(new ValidationIssue
        {
            Severity = ValidationSeverity.Error,
            Message = $"ScriptDefinition '{behaviorDef.ScriptId}' not found for BehaviorDefinition '{behaviorId}'.",
            ModId = string.Empty,
            FilePath = string.Empty,
        });
        return;
    }

    // Validate parameter overrides
    ValidateParameterOverrides(behaviorId, behaviorDef, scriptDef, issues);
}
```

**Priority:** MEDIUM

---

## 3. SOLID/DRY Violations

### 3.1 ModLoader.cs: Duplicate mod.manifest Reading (DRY)

**Location:** See Critical Issue 1.1

**Violation:** Don't Repeat Yourself (DRY)

**Priority:** HIGH (already covered in Critical Issues)

---

### 3.2 ModLoader.cs: Duplicate Path Normalization Logic

**Location:** `ModLoader.cs:562` and throughout codebase

**Issue:** `ModPathNormalizer.Normalize()` is called in multiple places. While this is acceptable, the normalization logic could be centralized in the type inference context creation.

**Current Code:**
```csharp
private string InferDefinitionType(string filePath, JsonDocument? jsonDoc, ModManifest mod)
{
    var normalizedPath = ModPathNormalizer.Normalize(filePath);
    
    var context = new TypeInferenceContext
    {
        FilePath = filePath,
        NormalizedPath = normalizedPath,
        // ...
    };
}
```

**Recommendation:** Consider normalizing in the context creation helper or making it automatic:
```csharp
private TypeInferenceContext CreateInferenceContext(
    string filePath,
    JsonDocument? jsonDoc,
    ModManifest mod)
{
    return new TypeInferenceContext
    {
        FilePath = filePath,
        NormalizedPath = ModPathNormalizer.Normalize(filePath),
        JsonDocument = jsonDoc,
        Mod = mod,
        Logger = _logger,
    };
}
```

**Priority:** LOW

---

### 3.3 ModValidator.cs: Duplicate Circular Dependency Detection

**Location:** `ModValidator.cs:299-332` and `ModLoader.cs:411-444`

**Issue:** Circular dependency detection logic exists in both `ModValidator` and `ModLoader` with slight variations.

**ModValidator:**
```csharp
private void CheckCircularDependencies(
    string modId,
    ModManifest manifest,
    Dictionary<string, ModManifest> allMods,
    HashSet<string> visited,
    List<ValidationIssue> issues)
{
    if (visited.Contains(modId))
    {
        issues.Add(new ValidationIssue { /* ... */ });
        return;
    }
    visited.Add(modId);
    // ...
}
```

**ModLoader:**
```csharp
private void ResolveDependencies(
    ModManifest mod,
    List<ModManifest> allMods,
    List<ModManifest> sortedMods,
    HashSet<string> processed,
    HashSet<string> processing,
    List<string> errors)
{
    if (processing.Contains(mod.Id))
    {
        errors.Add($"Circular dependency detected involving mod '{mod.Id}'");
        return;
    }
    processing.Add(mod.Id);
    // ...
}
```

**Recommendation:** Extract to shared utility:
```csharp
// In Mods/Utilities/DependencyResolver.cs
public static class DependencyResolver
{
    public static List<string> DetectCircularDependencies(
        Dictionary<string, ModManifest> mods)
    {
        var circularDeps = new List<string>();
        var visited = new HashSet<string>();
        
        foreach (var (modId, manifest) in mods)
        {
            if (!visited.Contains(modId))
            {
                var cycle = DetectCycle(modId, manifest, mods, new HashSet<string>(), visited);
                if (cycle != null)
                    circularDeps.AddRange(cycle);
            }
        }
        
        return circularDeps;
    }
    
    private static List<string>? DetectCycle(
        string modId,
        ModManifest manifest,
        Dictionary<string, ModManifest> allMods,
        HashSet<string> currentPath,
        HashSet<string> visited)
    {
        if (currentPath.Contains(modId))
            return new List<string>(currentPath) { modId };
        
        currentPath.Add(modId);
        visited.Add(modId);
        
        foreach (var depId in manifest.Dependencies)
        {
            if (allMods.TryGetValue(depId, out var depManifest))
            {
                var cycle = DetectCycle(depId, depManifest, allMods, currentPath, visited);
                if (cycle != null)
                    return cycle;
            }
        }
        
        currentPath.Remove(modId);
        return null;
    }
}
```

**Priority:** MEDIUM

---

### 3.4 ModLoader.cs: ValidateInferredType Logic Could Be in Strategy

**Location:** `ModLoader.cs:595-609`

**Issue:** `ValidateInferredType()` is called after inference, but the validation logic could be part of `ModManifestInferenceStrategy` since it validates against `mod.json` custom types.

**Current Code:**
```csharp
foreach (var strategy in DefaultStrategies)
{
    var inferredType = strategy.InferType(context);
    if (inferredType != null)
    {
        ValidateInferredType(inferredType, context);
        return inferredType;
    }
}
```

**Recommendation:** Move validation into `ModManifestInferenceStrategy` or create a validation decorator:
```csharp
// Option 1: Validate in ModManifestInferenceStrategy
public class ModManifestInferenceStrategy : ITypeInferenceStrategy
{
    public string? InferType(TypeInferenceContext context)
    {
        // ... existing inference logic ...
        
        if (inferredType != null)
        {
            // Validate against customDefinitionTypes
            ValidateInferredType(inferredType, context);
            return inferredType;
        }
        
        return null;
    }
}

// Option 2: Validation decorator
public class ValidatingInferenceStrategy : ITypeInferenceStrategy
{
    private readonly ITypeInferenceStrategy _inner;
    
    public string? InferType(TypeInferenceContext context)
    {
        var inferredType = _inner.InferType(context);
        if (inferredType != null)
        {
            ValidateInferredType(inferredType, context);
        }
        return inferredType;
    }
}
```

**Priority:** LOW

---

## 4. Code Smells

### 4.1 ModLoader.cs: Magic String "Behavior"

**Location:** `ModLoader.cs:754`

**Issue:** Hardcoded string `"Behavior"` used for type lookup.

**Current Code:**
```csharp
var behaviorDefinitionIds = _registry.GetByType("Behavior").ToList();
```

**Recommendation:** Use constant:
```csharp
private const string BehaviorDefinitionType = "Behavior";

// Usage:
var behaviorDefinitionIds = _registry.GetByType(BehaviorDefinitionType).ToList();
```

**Priority:** LOW

---

### 4.2 ModLoader.cs: Complex Parameter Validation Logic

**Location:** `ModLoader.cs:816-936`

**Issue:** 120+ lines of nested switch statements for parameter type validation. This is hard to maintain and test.

**Problems:**
- Deeply nested conditionals
- Duplicate logic for JsonElement vs deserialized types
- Hard to extend with new types
- Difficult to test individual validation cases

**Recommendation:** Extract to separate validator class:
```csharp
// In Mods/Validation/ParameterTypeValidator.cs
public static class ParameterTypeValidator
{
    public static void ValidateParameter(
        string paramName,
        object paramValue,
        ScriptParameterDefinition paramDef,
        string behaviorId,
        string scriptId)
    {
        ValidateParameterType(paramName, paramValue, paramDef, behaviorId, scriptId);
        ValidateParameterBounds(paramName, paramValue, paramDef, behaviorId, scriptId);
    }
    
    private static void ValidateParameterType(
        string paramName,
        object paramValue,
        ScriptParameterDefinition paramDef,
        string behaviorId,
        string scriptId)
    {
        var paramType = paramDef.Type.ToLowerInvariant();
        var converter = GetTypeConverter(paramType);
        
        if (!converter.CanConvert(paramValue))
        {
            throw new InvalidOperationException(
                $"BehaviorDefinition '{behaviorId}' has parameterOverride '{paramName}' " +
                $"with invalid type. Expected '{paramDef.Type}', got '{paramValue.GetType()}'. " +
                $"Error: {converter.GetConversionError(paramValue)}"
            );
        }
    }
    
    private static ITypeConverter GetTypeConverter(string paramType)
    {
        return paramType switch
        {
            "int" => new IntTypeConverter(),
            "float" => new FloatTypeConverter(),
            "bool" => new BoolTypeConverter(),
            "string" => new StringTypeConverter(),
            "vector2" => new Vector2TypeConverter(),
            _ => throw new NotSupportedException($"Unsupported parameter type: {paramType}")
        };
    }
}
```

**Priority:** MEDIUM

---

### 4.3 ModLoader.cs: Long Method (ValidateBehaviorDefinitions)

**Location:** `ModLoader.cs:751-996`

**Issue:** 245-line method violates "methods should be short" principle.

**Priority:** MEDIUM (covered in Architecture Issue 2.5)

---

### 4.4 ModValidator.cs: Silent Exception Swallowing

**Location:** `ModValidator.cs:292-295`

**Issue:** Generic catch block swallows all exceptions without logging.

**Current Code:**
```csharp
catch
{
    // Skip other errors during validation
}
```

**Recommendation:** Log exceptions for debugging:
```csharp
catch (Exception ex)
{
    _logger.Warning(ex, "Unexpected error validating definition file {FilePath} in mod {ModId}", 
        jsonFile, manifest.Id);
    // Continue validation for other files
}
```

**Priority:** LOW

---

### 4.5 ModLoader.cs: Inconsistent Error Handling

**Location:** Throughout `ModLoader.cs`

**Issue:** Some methods return error lists, others throw exceptions. Inconsistent error handling strategy.

**Examples:**
- `LoadAllMods()` returns `List<string>` errors
- `ValidateBehaviorDefinitions()` throws exceptions
- `LoadModDefinitions()` adds to errors list
- `InferDefinitionType()` throws exceptions

**Recommendation:** Standardize error handling strategy:
- **Option 1:** Always collect errors, throw at end if critical
- **Option 2:** Always fail fast with exceptions

**Priority:** MEDIUM

---

### 4.6 ModManager.cs: Fallback Code in GetTileWidth/GetTileHeight

**Location:** `ModManager.cs:172-198` and `ModManager.cs:207-233`

**Issue:** Methods have fallback logic that violates "no fallback code" rule from .cursorrules.

**Current Code:**
```csharp
public int GetTileWidth(IConstantsService? constantsService = null)
{
    // Prioritize constants service if available
    if (constantsService != null && constantsService.Contains("TileWidth"))
        return constantsService.Get<int>("TileWidth");

    // Fall back to mod configuration (for backward compatibility)
    if (!_isLoaded || LoadedMods.Count == 0)
        throw new InvalidOperationException(/* ... */);

    // Prioritize core mod
    if (CoreMod != null && CoreMod.TileWidth > 0)
        return CoreMod.TileWidth;

    // Fall back to first loaded mod
    var firstMod = LoadedMods.OrderBy(m => m.Priority).First();
    if (firstMod.TileWidth > 0)
        return firstMod.TileWidth;

    throw new InvalidOperationException(/* ... */);
}
```

**Problem:** Multiple fallback levels violate "no fallback code" rule.

**Recommendation:** Fail fast if ConstantsService not available:
```csharp
public int GetTileWidth(IConstantsService constantsService)
{
    _logger = constantsService ?? throw new ArgumentNullException(nameof(constantsService));
    
    if (!constantsService.Contains("TileWidth"))
    {
        throw new InvalidOperationException(
            "ConstantsService does not contain 'TileWidth' constant. " +
            "Ensure ConstantsService is properly initialized with tile width configuration."
        );
    }
    
    return constantsService.Get<int>("TileWidth");
}
```

**Priority:** MEDIUM

---

## 5. Potential Bugs

### 5.1 ModLoader.cs: Race Condition in _loadedMods

**Location:** `ModLoader.cs:132, 373-375, 401-403`

**Issue:** `_loadedMods` is modified in multiple places without thread safety considerations. While mod loading is typically single-threaded, this could cause issues if loading becomes async.

**Current Code:**
```csharp
_loadedMods.Add(coreMod);
// ... later ...
foreach (var mod in orderedMods)
    if (!_loadedMods.Contains(mod))
        _loadedMods.Add(mod);
```

**Recommendation:** Use thread-safe collection or ensure single-threaded access:
```csharp
// Option 1: Use ConcurrentBag if async loading is planned
private readonly ConcurrentBag<ModManifest> _loadedMods = new();

// Option 2: Document that mod loading is single-threaded
// Add XML comment: "Mod loading is single-threaded. This list is not thread-safe."
```

**Priority:** LOW (unless async loading is planned)

---

### 5.2 ModLoader.cs: JsonDocument Disposal in Error Path

**Location:** `ModLoader.cs:523-529`

**Issue:** If `LoadDefinitionFromFile()` throws an exception, `jsonDoc` might not be disposed if exception occurs before `finally` block executes.

**Current Code:**
```csharp
try
{
    loadResult = LoadDefinitionFromFile(/* ... */);
}
finally
{
    if (createdJsonDoc)
        jsonDoc?.Dispose();
}
```

**Problem:** If exception occurs in `LoadDefinitionFromFile()` before it returns, disposal happens. But if `LoadDefinitionFromFile()` internally creates a new JsonDocument and throws, the original `jsonDoc` might leak.

**Recommendation:** Use `using` statement:
```csharp
using (var jsonDocWrapper = jsonDoc != null ? new JsonDocumentWrapper(jsonDoc) : null)
{
    loadResult = LoadDefinitionFromFile(
        mod.ModSource,
        jsonFile,
        definitionType,
        mod,
        jsonDocWrapper?.Document
    );
}

// Or simpler: always dispose in finally
finally
{
    jsonDoc?.Dispose();
}
```

**Priority:** LOW (disposal should work, but could be safer)

---

### 5.3 ModLoader.cs: modManifests.Remove() Modifies Collection During Iteration

**Location:** `ModLoader.cs:134`

**Issue:** `modManifests.Remove(coreMod)` modifies the list that was passed in. This could cause issues if the caller expects the list to remain unchanged.

**Current Code:**
```csharp
modManifests.Remove(coreMod); // Remove from list so it's not loaded again
```

**Problem:** Modifies input parameter, which is unexpected behavior.

**Recommendation:** Create a new list or use a different approach:
```csharp
// Option 1: Create new list
var remainingMods = modManifests.Where(m => m.Id != coreModId).ToList();

// Option 2: Use HashSet for O(1) lookup
var modsToLoad = new HashSet<ModManifest>(modManifests);
modsToLoad.Remove(coreMod);
```

**Priority:** LOW

---

### 5.4 ModValidator.cs: JsonDocument Not Disposed

**Location:** `ModValidator.cs:249`

**Issue:** `JsonDocument.Parse()` creates a document that is never disposed.

**Current Code:**
```csharp
var jsonContent = modSource.ReadTextFile(jsonFile);
var jsonDoc = JsonDocument.Parse(jsonContent);
// ... use jsonDoc ...
// No disposal!
```

**Recommendation:** Use `using` statement:
```csharp
var jsonContent = modSource.ReadTextFile(jsonFile);
using var jsonDoc = JsonDocument.Parse(jsonContent);
// ... use jsonDoc ...
```

**Priority:** MEDIUM (memory leak potential)

---

## 6. .cursorrules Compliance Issues

### 6.1 ModManager.cs: Optional Logger Parameter

**Location:** See Architecture Issue 2.1

**Violation:** "NO FALLBACK CODE" - Optional parameter with null check violates fail-fast principle.

**Priority:** MEDIUM

---

### 6.2 ModManager.cs: Fallback Code in GetTileWidth/GetTileHeight

**Location:** See Code Smell 4.6

**Violation:** "NO FALLBACK CODE" - Multiple fallback levels.

**Priority:** MEDIUM

---

### 6.3 ModLoader.cs: ValidateBehaviorDefinitions Error Collection + Exceptions

**Location:** See Critical Issue 1.3

**Violation:** "NO FALLBACK CODE" - Inconsistent error handling (collects errors but also throws).

**Priority:** HIGH

---

### 6.4 Missing XML Documentation

**Location:** Various private methods

**Issue:** Some private methods lack XML documentation. While not required for private methods, it's good practice.

**Examples:**
- `ModLoader.ResolveDependencies()` - No XML docs
- `ModLoader.ValidateInferredType()` - No XML docs
- `ModValidator.CheckCircularDependencies()` - No XML docs

**Priority:** LOW

---

## 7. Recommendations Summary

### High Priority (Fix Immediately)

1. **Fix duplicate mod.manifest reading** (Critical Issue 1.1)
2. **Fix JsonDocument disposal logic** (Critical Issue 1.2)
3. **Fix ValidateBehaviorDefinitions error handling** (Critical Issue 1.3)
4. **Fix JsonDocument disposal in ModValidator** (Bug 5.4)

### Medium Priority (Fix Soon)

1. **Extract ValidateBehaviorDefinitions to ModValidator** (Architecture Issue 2.5)
2. **Refactor LoadModDefinitions for SRP** (Architecture Issue 2.2)
3. **Refactor ResolveLoadOrder** (Architecture Issue 2.3)
4. **Extract parameter validation logic** (Code Smell 4.2)
5. **Standardize error handling** (Code Smell 4.5)
6. **Remove fallback code from GetTileWidth/GetTileHeight** (Code Smell 4.6)
7. **Make logger required in ModManager** (Architecture Issue 2.1)

### Low Priority (Nice to Have)

1. **Extract circular dependency detection** (SOLID/DRY 3.3)
2. **Add constants for magic strings** (Code Smell 4.1)
3. **Add XML documentation** (.cursorrules 6.4)
4. **Consider thread safety** (Bug 5.1)

---

## 8. Testing Recommendations

### Unit Tests Needed

1. **ModLoader Tests:**
   - Test duplicate mod.manifest reading doesn't occur
   - Test JsonDocument disposal in all code paths
   - Test error handling consistency
   - Test mod load order resolution

2. **ModValidator Tests:**
   - Test JsonDocument disposal
   - Test circular dependency detection
   - Test behavior definition validation

3. **Type Inference Tests:**
   - Test all inference strategies
   - Test type validation logic

### Integration Tests Needed

1. **End-to-end mod loading:**
   - Test mod loading with various error scenarios
   - Test mod loading with compressed mods
   - Test mod loading with custom definition types

---

## Conclusion

The mod loading system is generally well-architected with good separation of concerns. However, several issues need attention:

1. **Critical:** Fix duplicate file reading, JsonDocument disposal, and error handling inconsistencies
2. **Architecture:** Extract large methods, improve SRP compliance
3. **SOLID/DRY:** Remove duplication, improve single responsibility
4. **Code Smells:** Refactor complex methods, remove magic strings
5. **Bugs:** Fix JsonDocument disposal, ensure proper resource cleanup

Most issues are fixable with refactoring and don't require architectural changes. The system is maintainable but would benefit from the recommended improvements.
