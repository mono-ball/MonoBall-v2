# Convention-Based Definition Discovery Implementation Plan (Updated)

## Overview

Replace the `contentFolders` mapping system with convention-based definition discovery that infers types from file paths. Per project rules, this is a breaking change with no backward compatibility.

**Updated**: All critical and important issues from plan analysis have been addressed.

## Architecture

The implementation uses a Chain of Responsibility pattern with separate strategy classes for each inference tier:

```
ModLoader.LoadModDefinitions()
  └─> InferDefinitionType() [Chain of Responsibility]
      ├─> HardcodedPathInferenceStrategy (Tier 1 - Fastest)
      ├─> DirectoryNameInferenceStrategy (Tier 2 - Fast)
      ├─> JsonTypeOverrideStrategy (Tier 3 - Lazy JSON parsing)
      └─> ModManifestInferenceStrategy (Tier 4 - Validation)
```

## Implementation Steps

### Phase 1: Create Type Inference Infrastructure

#### 1.1 Create Type Inference Strategy Interface and Context

**File**: `MonoBall/MonoBall.Core/Mods/TypeInference/ITypeInferenceStrategy.cs` (new)

```csharp
namespace MonoBall.Core.Mods.TypeInference;

/// <summary>
/// Strategy interface for type inference using Chain of Responsibility pattern.
/// </summary>
public interface ITypeInferenceStrategy
{
    /// <summary>
    /// Attempts to infer the definition type. Returns null if inference fails.
    /// </summary>
    string? InferType(TypeInferenceContext context);
}
```

**File**: `MonoBall/MonoBall.Core/Mods/TypeInference/TypeInferenceContext.cs` (new)

```csharp
using System.Text.Json;
using Serilog;

namespace MonoBall.Core.Mods.TypeInference;

/// <summary>
/// Context object for type inference (reduces parameter count).
/// </summary>
public struct TypeInferenceContext
{
    public string FilePath { get; set; }
    public string NormalizedPath { get; set; }
    public JsonDocument? JsonDocument { get; set; }
    public ModManifest Mod { get; set; }
    public ILogger Logger { get; set; }
}
```

#### 1.2 Create Path Matching Utility

**File**: `MonoBall/MonoBall.Core/Mods/Utilities/PathMatcher.cs` (new)

```csharp
namespace MonoBall.Core.Mods.Utilities;

/// <summary>
/// Utility for matching file paths against patterns (DRY - used in multiple places).
/// </summary>
public static class PathMatcher
{
    public static bool MatchesPath(string filePath, string pattern, StringComparison comparison = StringComparison.Ordinal)
    {
        var normalized = ModPathNormalizer.Normalize(filePath);
        return normalized.StartsWith(pattern + "/", comparison) || normalized == pattern;
    }
}
```

#### 1.3 Create Hardcoded Path Mappings

**File**: `MonoBall/MonoBall.Core/Mods/TypeInference/KnownPathMappings.cs` (new)

```csharp
namespace MonoBall.Core.Mods.TypeInference;

/// <summary>
/// Pre-sorted path mappings for known definition types (sorted by path length, most specific first).
/// </summary>
public static class KnownPathMappings
{
    public static readonly (string Path, string Type)[] SortedMappings = 
        new Dictionary<string, string>
        {
            // Asset definitions (most specific first)
            { "Definitions/Assets/UI/Popups/Backgrounds", "PopupBackgroundAsset" },
            { "Definitions/Assets/UI/Popups/Outlines", "PopupOutlineAsset" },
            { "Definitions/Assets/Maps/Tiles/DoorAnimations", "DoorAnimationAsset" },
            { "Definitions/Assets/Maps/Tilesets", "TilesetAsset" },
            { "Definitions/Assets/UI/Interface", "InterfaceAsset" },
            { "Definitions/Assets/UI/TextWindows", "TextWindowAsset" },
            { "Definitions/Assets/Characters", "CharacterAsset" },
            { "Definitions/Assets/FieldEffects", "FieldEffectAsset" },
            { "Definitions/Assets/Shaders", "ShaderAsset" },
            { "Definitions/Assets/Sprites", "SpriteAsset" },
            { "Definitions/Assets/Weather", "WeatherAsset" },
            { "Definitions/Assets/Audio", "AudioAsset" },
            { "Definitions/Assets/Battle", "BattleAsset" },
            { "Definitions/Assets/Fonts", "FontAsset" },
            { "Definitions/Assets/Objects", "ObjectAsset" },
            { "Definitions/Assets/Pokemon", "PokemonAsset" },
            
            // Constants (top-level)
            { "Definitions/Constants", "Constants" },
            
            // Entity definitions (most specific first)
            { "Definitions/Entities/Text/ColorPalettes", "ColorPalette" },
            { "Definitions/Entities/Text/TextEffects", "TextEffect" },
            { "Definitions/Entities/BattleScenes", "BattleScene" },
            { "Definitions/Entities/MapSections", "MapSection" },
            { "Definitions/Entities/PopupThemes", "PopupTheme" },
            { "Definitions/Entities/Maps", "Map" },
            { "Definitions/Entities/Pokemon", "Pokemon" },
            { "Definitions/Entities/Regions", "Region" },
            { "Definitions/Entities/Weather", "Weather" },
            
            // Scripts
            { "Definitions/Scripts", "Script" },
            
            // Legacy/flat structure support
            { "Definitions/Sprites", "Sprite" },
            { "Definitions/Audio", "Audio" },
            { "Definitions/TextWindow", "TextWindow" },
            { "Definitions/TextEffects", "TextEffect" },
            { "Definitions/ColorPalettes", "ColorPalette" },
            { "Definitions/Fonts", "Font" },
            { "Definitions/Shaders", "Shader" },
        }
        .OrderByDescending(kvp => kvp.Key.Length)
        .Select(kvp => (kvp.Key, kvp.Value))
        .ToArray();
}
```

#### 1.4 Create Inference Strategy Classes

**File**: `MonoBall/MonoBall.Core/Mods/TypeInference/HardcodedPathInferenceStrategy.cs` (new)

```csharp
using MonoBall.Core.Mods.Utilities;

namespace MonoBall.Core.Mods.TypeInference;

/// <summary>
/// Strategy 1: Hardcoded path mappings (fastest - no I/O).
/// </summary>
public class HardcodedPathInferenceStrategy : ITypeInferenceStrategy
{
    public string? InferType(TypeInferenceContext context)
    {
        foreach (var (pathPattern, type) in KnownPathMappings.SortedMappings)
        {
            if (PathMatcher.MatchesPath(context.NormalizedPath, pathPattern))
            {
                context.Logger.Debug(
                    "Definition type inferred from hardcoded mapping: {Type} for {Path}",
                    type,
                    context.FilePath
                );
                return type;
            }
        }
        return null;
    }
}
```

**File**: `MonoBall/MonoBall.Core/Mods/TypeInference/DirectoryNameInferenceStrategy.cs` (new)

```csharp
namespace MonoBall.Core.Mods.TypeInference;

/// <summary>
/// Strategy 2: Directory name inference (fast - no I/O).
/// </summary>
public class DirectoryNameInferenceStrategy : ITypeInferenceStrategy
{
    public string? InferType(TypeInferenceContext context)
    {
        var normalizedPath = context.NormalizedPath;
        
        // Use Span-based parsing to avoid allocations
        if (normalizedPath.StartsWith("Definitions/Assets/", StringComparison.Ordinal))
        {
            var typeName = ExtractTypeNameFromPath(normalizedPath, startIndex: 19);
            if (typeName != null)
            {
                var inferredType = SingularizeTypeName(typeName) + "Asset";
                context.Logger.Debug(
                    "Definition type inferred from Assets directory: {Type} for {Path}",
                    inferredType,
                    context.FilePath
                );
                return inferredType;
            }
        }
        else if (normalizedPath.StartsWith("Definitions/Entities/", StringComparison.Ordinal))
        {
            var typeName = ExtractTypeNameFromPath(normalizedPath, startIndex: 20);
            if (typeName != null)
            {
                // Check for nested structure (e.g., Text/TextEffects)
                var nestedTypeName = ExtractNestedTypeName(normalizedPath, startIndex: 20);
                var inferredType = SingularizeTypeName(nestedTypeName ?? typeName);
                
                context.Logger.Debug(
                    "Definition type inferred from Entities directory: {Type} for {Path}",
                    inferredType,
                    context.FilePath
                );
                return inferredType;
            }
        }
        else if (normalizedPath.StartsWith("Definitions/", StringComparison.Ordinal))
        {
            var typeName = ExtractTypeNameFromPath(normalizedPath, startIndex: 12);
            if (typeName != null)
            {
                var inferredType = SingularizeTypeName(typeName);
                context.Logger.Debug(
                    "Definition type inferred from directory: {Type} for {Path}",
                    inferredType,
                    context.FilePath
                );
                return inferredType;
            }
        }
        
        return null;
    }
    
    /// <summary>
    /// Extracts type name from path using Span-based parsing (no allocations).
    /// </summary>
    private static string? ExtractTypeNameFromPath(string path, int startIndex)
    {
        var span = path.AsSpan(startIndex);
        var slashIndex = span.IndexOf('/');
        if (slashIndex < 0)
            return null;
        
        return span.Slice(0, slashIndex).ToString();
    }
    
    /// <summary>
    /// Extracts nested type name (e.g., "TextEffects" from "Definitions/Entities/Text/TextEffects/...").
    /// </summary>
    private static string? ExtractNestedTypeName(string path, int startIndex)
    {
        var span = path.AsSpan(startIndex);
        var firstSlash = span.IndexOf('/');
        if (firstSlash < 0)
            return null;
        
        var secondSlash = span.Slice(firstSlash + 1).IndexOf('/');
        if (secondSlash < 0)
            return null;
        
        return span.Slice(firstSlash + 1, secondSlash).ToString();
    }
    
    /// <summary>
    /// Singularizes common plural type names for consistency.
    /// Kept as private static method per design document.
    /// </summary>
    private static string SingularizeTypeName(string typeName)
    {
        return typeName switch
        {
            "Quests" => "Quest",
            "Achievements" => "Achievement",
            "TextEffects" => "TextEffect",
            "ColorPalettes" => "ColorPalette",
            "WeatherEffects" => "WeatherEffect",
            _ => typeName
        };
    }
}
```

**File**: `MonoBall/MonoBall.Core/Mods/TypeInference/JsonTypeOverrideStrategy.cs` (new)

```csharp
namespace MonoBall.Core.Mods.TypeInference;

/// <summary>
/// Strategy 3: JSON $type field override (lazy parsing - only when needed).
/// </summary>
public class JsonTypeOverrideStrategy : ITypeInferenceStrategy
{
    public string? InferType(TypeInferenceContext context)
    {
        var jsonDoc = context.JsonDocument;
        if (jsonDoc == null)
            return null; // JSON not available - skip this strategy
        
        if (jsonDoc.RootElement.TryGetProperty("$type", out var typeElement))
        {
            var explicitType = typeElement.GetString();
            if (!string.IsNullOrEmpty(explicitType))
            {
                context.Logger.Debug(
                    "Definition type inferred from JSON $type field: {Type} for {Path}",
                    explicitType,
                    context.FilePath
                );
                return explicitType;
            }
        }
        
        return null;
    }
}
```

**File**: `MonoBall/MonoBall.Core/Mods/TypeInference/ModManifestInferenceStrategy.cs` (new)

```csharp
using MonoBall.Core.Mods.Utilities;

namespace MonoBall.Core.Mods.TypeInference;

/// <summary>
/// Strategy 4: Mod manifest custom types (validation/documentation).
/// </summary>
public class ModManifestInferenceStrategy : ITypeInferenceStrategy
{
    public string? InferType(TypeInferenceContext context)
    {
        if (context.Mod.CustomDefinitionTypes == null)
            return null;
        
        foreach (var (type, declaredPath) in context.Mod.CustomDefinitionTypes)
        {
            var normalizedDeclaredPath = ModPathNormalizer.Normalize(declaredPath);
            if (PathMatcher.MatchesPath(context.NormalizedPath, normalizedDeclaredPath))
            {
                context.Logger.Debug(
                    "Definition type inferred from mod.json customDefinitionTypes: {Type} for {Path}",
                    type,
                    context.FilePath
                );
                return type;
            }
        }
        
        return null;
    }
}
```

### Phase 2: Update ModLoader

#### 2.1 Replace LoadModDefinitions Method

**File**: `MonoBall/MonoBall.Core/Mods/ModLoader.cs`

- Remove `contentFolders` iteration logic (lines 462-476)
- Remove hardcoded script/behavior path checks (lines 478-498)
- Implement convention-based discovery with error recovery:
  - Enumerate all JSON files
  - Skip mod.json
  - Try path-based inference first (no JSON parsing)
  - Parse JSON only if path inference fails (lazy parsing)
  - Reuse JsonDocument between inference and loading
  - Continue loading other files when one fails (error recovery)
  - Fire `DefinitionDiscoveredEvent` after successful load
  - Ensure JsonDocument disposal

**Implementation**:

```csharp
private void LoadModDefinitions(ModManifest mod, List<string> errors)
{
    if (mod.ModSource == null)
        throw new InvalidOperationException(
            $"Mod '{mod.Id}' has no ModSource. Mods must have a valid ModSource to load definitions."
        );

    _logger.Debug(
        "Loading definitions for mod {ModId} using convention-based discovery",
        mod.Id
    );

    // Enumerate all JSON files in the mod
    var jsonFiles = mod.ModSource.EnumerateFiles("*.json", SearchOption.AllDirectories);
    
    foreach (var jsonFile in jsonFiles)
    {
        // Skip mod.json itself
        if (jsonFile.Equals("mod.json", StringComparison.OrdinalIgnoreCase))
            continue;
        
        // Try path-based inference first (no I/O)
        JsonDocument? jsonDoc = null;
        string definitionType;
        
        try
        {
            // Attempt inference without parsing JSON (fast path)
            definitionType = InferDefinitionType(jsonFile, null, mod);
        }
        catch (InvalidOperationException ex)
        {
            // Path-based inference failed - parse JSON for $type field (lazy parsing)
            try
            {
                var jsonContent = mod.ModSource.ReadTextFile(jsonFile);
                jsonDoc = JsonDocument.Parse(jsonContent);
                
                // Try inference again with JSON document
                definitionType = InferDefinitionType(jsonFile, jsonDoc, mod);
            }
            catch (Exception parseEx)
            {
                var errorMsg = $"Failed to parse JSON file '{jsonFile}': {parseEx.Message}";
                errors.Add(errorMsg);
                _logger.Error(parseEx, "Failed to parse JSON file {FilePath}", jsonFile);
                continue; // Skip this file, continue with others (error recovery)
            }
        }
        
        // Load definition with inferred type (reuse jsonDoc if available)
        var loadResult = LoadDefinitionFromFile(mod.ModSource, jsonFile, definitionType, mod, jsonDoc);
        
        // Dispose JsonDocument if we created it for inference
        jsonDoc?.Dispose();
        
        if (loadResult.IsError)
        {
            errors.Add(loadResult.Error!);
            _logger.Warning("Failed to load definition from {FilePath}: {Error}", jsonFile, loadResult.Error);
            continue; // Skip this file, continue with others (error recovery)
        }
        
        // Fire event for systems to react (ECS/Event integration)
        var discoveredEvent = new DefinitionDiscoveredEvent
        {
            ModId = mod.Id,
            DefinitionType = definitionType,
            DefinitionId = loadResult.Metadata!.Id,
            FilePath = jsonFile,
            SourceModId = loadResult.Metadata.OriginalModId,
            Operation = loadResult.Metadata.Operation
        };
        EventBus.Send(ref discoveredEvent);
    }
}

// Static readonly strategy array (stateless strategies, no per-instance allocation)
private static readonly ITypeInferenceStrategy[] DefaultStrategies = new ITypeInferenceStrategy[]
{
    new HardcodedPathInferenceStrategy(),      // Tier 1: Fastest (no I/O)
    new DirectoryNameInferenceStrategy(),      // Tier 2: Fast (no I/O)
    new JsonTypeOverrideStrategy(),            // Tier 3: Slow (JSON parsing)
    new ModManifestInferenceStrategy()        // Tier 4: Validation
};

/// <summary>
/// Main type inference method using Chain of Responsibility pattern.
/// </summary>
private string InferDefinitionType(
    string filePath,
    JsonDocument? jsonDoc,
    ModManifest mod
)
{
    var normalizedPath = ModPathNormalizer.Normalize(filePath);
    
    var context = new TypeInferenceContext
    {
        FilePath = filePath,
        NormalizedPath = normalizedPath,
        JsonDocument = jsonDoc,
        Mod = mod,
        Logger = _logger
    };
    
    // Chain of Responsibility: Try each strategy in order
    foreach (var strategy in DefaultStrategies)
    {
        var inferredType = strategy.InferType(context);
        if (inferredType != null)
        {
            // Validate inferred type if mod.json declares custom types
            ValidateInferredType(inferredType, context);
            return inferredType;
        }
    }
    
    // All strategies failed - throw exception (fail fast, per project rules)
    throw new InvalidOperationException(
        $"Could not infer definition type for '{filePath}'. " +
        "Ensure file follows convention-based directory structure or specify $type field in JSON."
    );
}

/// <summary>
/// Validates inferred type against mod.json customDefinitionTypes if present.
/// </summary>
private void ValidateInferredType(string inferredType, TypeInferenceContext context)
{
    if (context.Mod.CustomDefinitionTypes != null &&
        !context.Mod.CustomDefinitionTypes.ContainsValue(inferredType))
    {
        context.Logger.Warning(
            "Inferred type '{Type}' not declared in mod.json customDefinitionTypes for {Path}. " +
            "Consider adding it to customDefinitionTypes for documentation.",
            inferredType,
            context.FilePath
        );
    }
}
```

#### 2.2 Update LoadDefinitionFromFile Method

**File**: `MonoBall/MonoBall.Core/Mods/ModLoader.cs`

- Change return type from `void` to `DefinitionLoadResult`
- Remove `errors` parameter (errors returned in result)
- Add optional `jsonDoc` parameter to reuse parsed JSON
- **Preserve $operation support** (Modify/Extend/Replace)
- **Preserve merging logic** using `JsonElementMerger.Merge()`
- **Preserve operation tracking** (OriginalModId, LastModifiedByModId)
- Ensure JsonDocument disposal
- Use `Data` property (JsonElement), not `RawJson`

**Implementation**:

```csharp
/// <summary>
/// Loads a single definition from a file with unified error handling.
/// Preserves $operation support (Create/Modify/Extend/Replace).
/// </summary>
private DefinitionLoadResult LoadDefinitionFromFile(
    IModSource modSource,
    string relativePath,
    string definitionType,
    ModManifest mod,
    JsonDocument? existingJsonDoc = null  // Reuse parsed JSON if available
)
{
    JsonDocument? jsonDoc = null;
    try
    {
        // Reuse existing JsonDocument if provided, otherwise parse
        if (existingJsonDoc != null)
        {
            jsonDoc = existingJsonDoc;
        }
        else
        {
            var jsonContent = modSource.ReadTextFile(relativePath);
            jsonDoc = JsonDocument.Parse(jsonContent);
        }
        
        // Extract definition ID from JSON
        if (!jsonDoc.RootElement.TryGetProperty("id", out var idElement))
        {
            return DefinitionLoadResult.Failure(
                $"Definition file '{relativePath}' is missing required 'id' field."
            );
        }
        
        var id = idElement.GetString();
        if (string.IsNullOrEmpty(id))
        {
            return DefinitionLoadResult.Failure(
                $"Definition file '{relativePath}' has empty 'id' field."
            );
        }
        
        // Determine operation type (defaults to Create, but can be specified)
        var operation = DefinitionOperation.Create;
        if (jsonDoc.RootElement.TryGetProperty("$operation", out var opElement))
        {
            var opString = opElement.GetString()?.ToLowerInvariant();
            operation = opString switch
            {
                "modify" => DefinitionOperation.Modify,
                "extend" => DefinitionOperation.Extend,
                "replace" => DefinitionOperation.Replace,
                _ => DefinitionOperation.Create,
            };
        }
        
        // Check if definition already exists
        var existing = _registry.GetById(id);
        DefinitionMetadata metadata;
        
        if (existing != null)
        {
            // Apply operation (Modify/Extend/Replace)
            var finalData = jsonDoc.RootElement;
            if (operation == DefinitionOperation.Modify || operation == DefinitionOperation.Extend)
            {
                finalData = JsonElementMerger.Merge(
                    existing.Data,
                    jsonDoc.RootElement,
                    operation == DefinitionOperation.Extend
                );
            }
            // For Replace, use the new data as-is
            
            metadata = new DefinitionMetadata
            {
                Id = id,
                OriginalModId = existing.OriginalModId,
                LastModifiedByModId = mod.Id,
                Operation = operation,
                DefinitionType = definitionType,
                Data = finalData,  // Use Data (JsonElement), not RawJson
                SourcePath = relativePath,
            };
        }
        else
        {
            // New definition
            metadata = new DefinitionMetadata
            {
                Id = id,
                OriginalModId = mod.Id,
                LastModifiedByModId = mod.Id,
                Operation = DefinitionOperation.Create,
                DefinitionType = definitionType,
                Data = jsonDoc.RootElement,  // Use Data (JsonElement), not RawJson
                SourcePath = relativePath,
            };
        }
        
        // Register in registry
        _registry.Register(metadata);
        
        // Only dispose if we created the JsonDocument (not if it was passed in)
        if (existingJsonDoc == null)
        {
            jsonDoc.Dispose();
        }
        
        return DefinitionLoadResult.Success(metadata);
    }
    catch (JsonException ex)
    {
        return DefinitionLoadResult.Failure(
            $"JSON error in definition file '{relativePath}': {ex.Message}"
        );
    }
    catch (Exception ex)
    {
        return DefinitionLoadResult.Failure(
            $"Error loading definition from '{relativePath}': {ex.Message}"
        );
    }
    finally
    {
        // Ensure disposal if we created the document and an exception occurred
        if (existingJsonDoc == null && jsonDoc != null)
        {
            jsonDoc.Dispose();
        }
    }
}
```

#### 2.3 Add DefinitionLoadResult Type

**File**: `MonoBall/MonoBall.Core/Mods/DefinitionLoadResult.cs` (new)

```csharp
namespace MonoBall.Core.Mods;

/// <summary>
/// Result type for definition loading (unified error handling).
/// </summary>
internal struct DefinitionLoadResult
{
    public DefinitionMetadata? Metadata { get; set; }
    public string? Error { get; set; }
    
    public bool IsError => Error != null;
    
    public static DefinitionLoadResult Success(DefinitionMetadata metadata) =>
        new() { Metadata = metadata };
    
    public static DefinitionLoadResult Failure(string error) =>
        new() { Error = error };
}
```

### Phase 3: Add Event System Integration

#### 3.1 Create DefinitionDiscoveredEvent

**File**: `MonoBall/MonoBall.Core/ECS/Events/DefinitionDiscoveredEvent.cs` (new)

```csharp
using MonoBall.Core.Mods;

namespace MonoBall.Core.ECS.Events;

/// <summary>
/// Event fired when a definition is discovered and loaded.
/// Contains essential fields only (struct per project rules).
/// </summary>
public struct DefinitionDiscoveredEvent
{
    public string ModId { get; set; }
    public string DefinitionType { get; set; }
    public string DefinitionId { get; set; }
    public string FilePath { get; set; }
    public string SourceModId { get; set; }
    public DefinitionOperation Operation { get; set; }
    
    // Note: Full DefinitionMetadata not included - systems can query registry if needed
}
```

#### 3.2 Fire Events in ModLoader

**File**: `MonoBall/MonoBall.Core/Mods/ModLoader.cs`

- After successful definition load, fire `DefinitionDiscoveredEvent`
- Use `EventBus.Send(ref discoveredEvent)` pattern
- Pass essential fields only, not full `DefinitionMetadata` class

### Phase 4: Remove ContentFolders Support

#### 4.1 Remove ContentFolders from ModManifest

**File**: `MonoBall/MonoBall.Core/Mods/ModManifest.cs`

- Remove `ContentFolders` property (line 50-51)
- Add optional `CustomDefinitionTypes` property for documentation:
```csharp
/// <summary>
/// Optional mapping of custom definition types to paths for documentation/validation.
/// </summary>
[JsonPropertyName("customDefinitionTypes")]
public Dictionary<string, string>? CustomDefinitionTypes { get; set; }
```


#### 4.2 Remove ContentFolders from All Mod Manifests

**Files to Update**:

- `Mods/pokemon-emerald/mod.json` - Remove contentFolders section
- `Mods/core/mod.json` - Remove contentFolders section (lines 8-30)
- `Mods/test-shaders/mod.json` - Remove contentFolders section (lines 8-10)
- `Mods/plugin-scripts-demo/mod.json` - No contentFolders, already clean

#### 4.3 Remove ModPathFilter Usage

**File**: `MonoBall/MonoBall.Core/Mods/ModLoader.cs`

- Remove `ModPathFilter.FilterByContentFolder` usage
- Direct file enumeration is sufficient

### Phase 5: Update Utilities

#### 5.1 Update ModPathNormalizer (if needed)

**File**: `MonoBall/MonoBall.Core/Mods/Utilities/ModPathNormalizer.cs`

- Ensure it handles path normalization correctly
- Verify case-insensitive handling on Windows

### Phase 6: Testing and Validation

#### 6.1 Unit Tests

**File**: `MonoBall/MonoBall.Core.Tests/Mods/TypeInferenceTests.cs` (new)

- Test each inference strategy independently
- Test path matching (specificity, case sensitivity)
- Test custom type inference
- Test JSON $type override
- Test error handling
- Test $operation support (Modify/Extend/Replace)

#### 6.2 Integration Tests

**File**: `MonoBall/MonoBall.Core.Tests/Mods/ModLoaderConventionBasedTests.cs` (new)

- Test mod loading with standard structure
- Test mod loading with custom types
- Test mod loading with $type overrides
- Test mod loading with $operation (Modify/Extend/Replace)
- Test event firing
- Test error recovery (one bad file doesn't stop others)
- Test JsonDocument disposal

#### 6.3 Manual Testing

- Test with existing pokemon-emerald mod
- Test with core mod
- Test with test-shaders mod
- Verify all definition types are discovered correctly
- Verify events are fired correctly
- Verify $operation support works
- Verify error recovery works

## File Changes Summary

### New Files (11)

1. `MonoBall/MonoBall.Core/Mods/TypeInference/ITypeInferenceStrategy.cs`
2. `MonoBall/MonoBall.Core/Mods/TypeInference/TypeInferenceContext.cs`
3. `MonoBall/MonoBall.Core/Mods/TypeInference/HardcodedPathInferenceStrategy.cs`
4. `MonoBall/MonoBall.Core/Mods/TypeInference/DirectoryNameInferenceStrategy.cs`
5. `MonoBall/MonoBall.Core/Mods/TypeInference/JsonTypeOverrideStrategy.cs`
6. `MonoBall/MonoBall.Core/Mods/TypeInference/ModManifestInferenceStrategy.cs`
7. `MonoBall/MonoBall.Core/Mods/TypeInference/KnownPathMappings.cs`
8. `MonoBall/MonoBall.Core/Mods/Utilities/PathMatcher.cs`
9. `MonoBall/MonoBall.Core/Mods/DefinitionLoadResult.cs`
10. `MonoBall/MonoBall.Core/ECS/Events/DefinitionDiscoveredEvent.cs`

### Modified Files (5)

1. `MonoBall/MonoBall.Core/Mods/ModLoader.cs` - Replace LoadModDefinitions, update LoadDefinitionFromFile (preserve $operation)
2. `MonoBall/MonoBall.Core/Mods/ModManifest.cs` - Remove ContentFolders, add CustomDefinitionTypes
3. `Mods/pokemon-emerald/mod.json` - Remove contentFolders
4. `Mods/core/mod.json` - Remove contentFolders (lines 8-30)
5. `Mods/test-shaders/mod.json` - Remove contentFolders (lines 8-10)

## Performance Considerations

- **Lazy JSON parsing**: Only parse JSON when path-based inference fails (99%+ files skip JSON)
- **Pre-sorted mappings**: Path mappings sorted once at initialization
- **Span-based parsing**: Use Span<char> to avoid allocations from Split('/')
- **Early returns**: Each strategy returns immediately on success
- **JsonDocument reuse**: Reuse parsed JsonDocument between inference and loading
- **Static strategy array**: Strategies are stateless, use static readonly array

## Migration Notes

- **Breaking Change**: All mods must be updated to remove `contentFolders`
- **No Backward Compatibility**: Per project rules, old system is removed immediately
- **Clear Errors**: Fail fast with helpful error messages when conventions aren't followed
- **Error Recovery**: One bad file doesn't prevent other files from loading
- **$operation Preserved**: Modify/Extend/Replace operations continue to work

## Dependencies

- `MonoBall.Core.ECS.EventBus` - For event firing
- `MonoBall.Core.Mods.Utilities` - Path normalization utilities, JsonElementMerger
- `System.Text.Json` - JSON parsing (lazy)
- `Serilog` - Logging

## Success Criteria

1. All definition types are correctly inferred from paths
2. JSON parsing only occurs when needed (performance optimization)
3. Events are fired for all discovered definitions
4. All mod.json files updated (no contentFolders)
5. $operation support preserved (Modify/Extend/Replace)
6. Error recovery works (one bad file doesn't stop others)
7. JsonDocument properly disposed (no memory leaks)
8. Unit tests pass
9. Integration tests pass
10. Manual testing confirms mods load correctly

## Fixes Applied

All critical and important issues from plan analysis have been addressed:

✅ **Critical Fixes**:

- Preserved $operation support (Modify/Extend/Replace)
- Fixed DefinitionMetadata usage (Data property, not RawJson)
- Added JsonDocument disposal
- Optimized JSON parsing (reuse JsonDocument)
- Fixed DefinitionDiscoveredEvent (struct with essential fields only)
- Added error recovery (continue loading other files)

✅ **Code Quality**:

- TypeNameSingularizer kept as private static method (matches design)
- Strategy array made static readonly (stateless strategies)
- Improved error handling with DefinitionLoadResult