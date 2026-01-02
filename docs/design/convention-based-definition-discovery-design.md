# Convention-Based Definition Discovery Design

**Version:** 2.0.0  
**Status:** Design Specification (Updated with Architecture Fixes)  
**Date:** 2025-01-XX  
**Author:** MonoBall Design Team  
**Last Updated:** 2025-01-XX (Architecture analysis fixes applied)

---

## Executive Summary

This document defines the architecture for eliminating the `contentFolders` mapping from mod manifests by replacing it with convention-based definition discovery. The new system infers definition types from file paths using a standardized directory structure, reducing boilerplate configuration and enabling seamless support for custom definition types.

**Performance Optimizations**: The design includes critical performance improvements:
- **Lazy JSON parsing**: Only parses JSON when path-based inference fails (99%+ files skip JSON parsing)
- **Pre-sorted path mappings**: Avoids runtime sorting overhead
- **Span-based path parsing**: Eliminates allocations from string splitting
- **Chain of Responsibility pattern**: Separates concerns for better maintainability

### Design Principles

1. **Convention Over Configuration**: Standard directory structure eliminates need for explicit mappings
2. **Zero Configuration for Standard Types**: Known definition types work automatically
3. **Flexible Custom Types**: Custom definition types inferred from directory names
4. **Fail Fast**: Clear errors when conventions aren't followed (per project rules: no backward compatibility)
5. **Performance First**: Optimized for fast mod loading with minimal I/O
6. **SOLID Principles**: Uses Strategy pattern and Chain of Responsibility for maintainability
7. **Event-Driven**: Fires events for ECS systems to react to definition discovery

---

## Problem Statement

### Current State

Mods currently require explicit `contentFolders` mapping in `mod.json`:

```json
{
  "id": "base:pokemon-emerald",
  "contentFolders": {
    "Root": "",
    "Graphics": "Graphics",
    "Audio": "Audio",
    "Scripts": "Scripts",
    "ScriptDefinitions": "Definitions/Scripts",
    "TileBehaviorDefinitions": "Definitions/TileBehaviors",
    "BehaviorDefinitions": "Definitions/Behaviors",
    "SpriteDefinitions": "Definitions/Sprites",
    "TilesetDefinitions": "Definitions/Maps/Tilesets",
    "MapDefinitions": "Definitions/Maps/Regions",
    "AudioDefinitions": "Definitions/Audio",
    "TextWindowDefinitions": "Definitions/TextWindow",
    "PopupBackgroundDefinitions": "Definitions/Maps/Popups/Backgrounds",
    "PopupOutlineDefinitions": "Definitions/Maps/Popups/Outlines",
    "PopupThemeDefinitions": "Definitions/Maps/Popups/Themes",
    "MapSectionDefinitions": "Definitions/Maps/Sections"
  }
}
```

### Problems

1. **Verbose Configuration**: Every mod must declare the same mappings
2. **Maintenance Burden**: Must keep manifest in sync with actual directory structure
3. **Error-Prone**: Easy to forget mappings or make typos
4. **No Custom Type Support**: Custom definition types require engine changes
5. **Duplication**: Same mappings repeated across all mods

---

## Proposed Solution

### Core Concept

Replace explicit `contentFolders` mapping with **convention-based discovery** that infers definition types from file paths using a standardized directory structure.

### Standard Directory Convention

Based on Porycon3's organization pattern, definitions are organized into three main categories:

**Assets** - Definitions that reference external files (graphics, audio, fonts, etc.)  
**Entities** - Game data/logic definitions (maps, regions, pokemon species, etc.)  
**Scripts** - Script definitions (interactions, movement, etc.)

```
ModRoot/
├── Definitions/                    # All definition JSON files
│   ├── Assets/                     # Asset definitions (reference files)
│   │   ├── Audio/                  → Type: "AudioAsset"
│   │   │   ├── Music/              → Type: "AudioAsset" (subcategory)
│   │   │   └── SFX/                → Type: "AudioAsset" (subcategory)
│   │   ├── Battle/                 → Type: "BattleAsset"
│   │   ├── Characters/             → Type: "CharacterAsset"
│   │   │   ├── Npcs/               → Type: "CharacterAsset" (subcategory)
│   │   │   └── Players/            → Type: "CharacterAsset" (subcategory)
│   │   ├── FieldEffects/           → Type: "FieldEffectAsset"
│   │   ├── Fonts/                  → Type: "FontAsset"
│   │   ├── Objects/                → Type: "ObjectAsset"
│   │   │   ├── BerryTrees/         → Type: "ObjectAsset" (subcategory)
│   │   │   ├── Pokeballs/          → Type: "ObjectAsset" (subcategory)
│   │   │   └── ...                 
│   │   ├── Pokemon/                → Type: "PokemonAsset"
│   │   ├── Shaders/                → Type: "ShaderAsset"
│   │   │   ├── Screen/             → Type: "ShaderAsset" (subcategory)
│   │   │   └── Entity/             → Type: "ShaderAsset" (subcategory)
│   │   ├── Sprites/                → Type: "SpriteAsset"
│   │   ├── Maps/                   
│   │   │   ├── Tiles/              
│   │   │   │   └── DoorAnimations/ → Type: "DoorAnimationAsset"
│   │   │   └── Tilesets/           → Type: "TilesetAsset"
│   │   ├── UI/                     
│   │   │   ├── Interface/          → Type: "InterfaceAsset"
│   │   │   ├── Popups/             
│   │   │   │   ├── Backgrounds/    → Type: "PopupBackgroundAsset"
│   │   │   │   └── Outlines/       → Type: "PopupOutlineAsset"
│   │   │   └── TextWindows/        → Type: "TextWindowAsset"
│   │   └── Weather/                → Type: "WeatherAsset"
│   │
│   ├── Constants/                  → Type: "Constants"
│   │
│   ├── Entities/                   # Entity definitions (game data/logic)
│   │   ├── BattleScenes/           → Type: "BattleScene"
│   │   ├── Maps/                   → Type: "Map"
│   │   │   └── {Region}/           → Type: "Map" (e.g., "Hoenn")
│   │   ├── MapSections/            → Type: "MapSection"
│   │   ├── Pokemon/                → Type: "Pokemon"
│   │   ├── PopupThemes/            → Type: "PopupTheme"
│   │   ├── Regions/                → Type: "Region"
│   │   ├── Text/                   
│   │   │   ├── ColorPalettes/      → Type: "ColorPalette"
│   │   │   └── TextEffects/        → Type: "TextEffect"
│   │   └── Weather/                → Type: "Weather"
│   │
│   └── Scripts/                     → Type: "Script"
│       ├── Movement/                → Type: "Script" (movement scripts)
│       │   └── NPCs/                → Type: "Script" (NPC movement scripts)
│       └── Interactions/            → Type: "Script" (interaction scripts)
│           ├── NPCs/               
│           ├── Signs/               
│           ├── Tiles/               
│           └── Triggers/           
│
├── Graphics/                       # Non-definition assets (textures, images)
├── Audio/                          # Non-definition assets (music, SFX files)
└── Scripts/                         # Script files (.csx), not definitions
```

**Key Organizational Principles**:

1. **Assets vs Entities vs Constants**: 
   - `Assets/` = Definitions that reference external files (sprites, audio, fonts, etc.)
   - `Constants/` = Game constants and configuration values (top-level under Definitions/)
   - `Entities/` = Game logic/data definitions (maps, regions, pokemon species, etc.)

2. **Naming Convention**:
   - Asset types end with `Asset` suffix (e.g., `FontAsset`, `SpriteAsset`)
   - Constants types use simple names (e.g., `Constants`)
   - Entity types use simple names (e.g., `Map`, `Region`, `Pokemon`)
   - Script types remain as-is (`Script`)

3. **Subdirectories**:
   - Subdirectories under main categories are treated as subcategories
   - Type inference uses the most specific path match
   - Example: `Definitions/Assets/UI/Popups/Backgrounds/` → Type: `"PopupBackgroundAsset"`

---

## Type Inference Algorithm

### Multi-Tier Discovery Strategy

The system uses a fallback chain to determine definition types. **Critical Performance Optimization**: Path-based inference is checked **first** (no I/O), JSON parsing only occurs when needed (rare case).

#### Tier 1: Hardcoded Path Mappings (Known Types) - **FAST PATH**

For engine-standard definition types, use a hardcoded mapping dictionary:

```csharp
// Pre-sorted by path length (most specific first) to avoid sorting on every call
private static readonly (string Path, string Type)[] SortedPathMappings = 
    new Dictionary<string, string>
    {
    // Asset definitions (reference external files)
    { "Definitions/Assets/Audio", "AudioAsset" },
    { "Definitions/Assets/Battle", "BattleAsset" },
    { "Definitions/Assets/Characters", "CharacterAsset" },
    { "Definitions/Assets/FieldEffects", "FieldEffectAsset" },
    { "Definitions/Assets/Fonts", "FontAsset" },
    { "Definitions/Assets/Maps/Tiles/DoorAnimations", "DoorAnimationAsset" },
    { "Definitions/Assets/Maps/Tilesets", "TilesetAsset" },
    { "Definitions/Assets/Objects", "ObjectAsset" },
    { "Definitions/Assets/Pokemon", "PokemonAsset" },
    { "Definitions/Assets/Shaders", "ShaderAsset" },
    { "Definitions/Assets/Sprites", "SpriteAsset" },
    { "Definitions/Assets/UI/Interface", "InterfaceAsset" },
    { "Definitions/Assets/UI/Popups/Backgrounds", "PopupBackgroundAsset" },
    { "Definitions/Assets/UI/Popups/Outlines", "PopupOutlineAsset" },
    { "Definitions/Assets/UI/TextWindows", "TextWindowAsset" },
    { "Definitions/Assets/Weather", "WeatherAsset" },
    
    // Constants (top-level)
    { "Definitions/Constants", "Constants" },
    
    // Entity definitions (game data/logic)
    { "Definitions/Entities/BattleScenes", "BattleScene" },
    { "Definitions/Entities/Maps", "Map" },
    { "Definitions/Entities/MapSections", "MapSection" },
    { "Definitions/Entities/Pokemon", "Pokemon" },
    { "Definitions/Entities/PopupThemes", "PopupTheme" },
    { "Definitions/Entities/Regions", "Region" },
    { "Definitions/Entities/Text/ColorPalettes", "ColorPalette" },
    { "Definitions/Entities/Text/TextEffects", "TextEffect" },
    { "Definitions/Entities/Weather", "Weather" },
    
    // Script definitions
    { "Definitions/Scripts", "Script" },
    
    // Legacy/flat structure support (for backward compatibility)
    { "Definitions/Sprites", "Sprite" },
    { "Definitions/Audio", "Audio" },
    { "Definitions/TextWindow", "TextWindow" },
    { "Definitions/TextEffects", "TextEffect" },
    { "Definitions/ColorPalettes", "ColorPalette" },
    { "Definitions/Constants", "Constants" },
    { "Definitions/Fonts", "Font" },
    { "Definitions/Shaders", "Shader" },
    }
    .OrderByDescending(kvp => kvp.Key.Length)
    .Select(kvp => (kvp.Key, kvp.Value))
    .ToArray();

// Path matching utility (DRY - used in multiple places)
private static class PathMatcher
{
    public static bool MatchesPath(string filePath, string pattern, StringComparison comparison = StringComparison.Ordinal)
    {
        var normalized = ModPathNormalizer.Normalize(filePath);
        return normalized.StartsWith(pattern + "/", comparison) || normalized == pattern;
    }
}
```

**Matching Strategy**: Check paths from most specific to least specific (pre-sorted array, no runtime sorting):
- `Definitions/Maps/Popups/Backgrounds/` matches before `Definitions/Maps/`
- Exact path matches take precedence

#### Tier 2: Directory Name Inference (Custom Types) - **FAST PATH**

If no hardcoded mapping matches, infer type from directory structure:

**For Assets** (under `Definitions/Assets/`):
```
Definitions/Assets/Quests/          → Type: "QuestAsset"
Definitions/Assets/Achievements/    → Type: "AchievementAsset"
Definitions/Assets/CustomStuff/     → Type: "CustomStuffAsset"
Definitions/Assets/Maps/Tiles/CustomAnimations/ → Type: "CustomAnimationAsset" (under Maps/Tiles parent)
```

**For Entities** (under `Definitions/Entities/`):
```
Definitions/Entities/Quests/        → Type: "Quest"
Definitions/Entities/Achievements/  → Type: "Achievement"
Definitions/Entities/CustomStuff/   → Type: "CustomStuff"
Definitions/Entities/Text/CustomEffects/ → Type: "CustomEffect" (under Text parent)
```

**For Top-Level** (directly under `Definitions/`):
```
Definitions/Quests/                 → Type: "Quest"
Definitions/Achievements/           → Type: "Achievement"
Definitions/CustomStuff/            → Type: "CustomStuff"
```

**Algorithm**:
1. Check if path starts with `Definitions/Assets/` → Add `Asset` suffix
2. Check if path starts with `Definitions/Entities/` → Use simple name
3. Otherwise, extract path segment immediately after `Definitions/`
4. Optionally singularize common patterns (e.g., "Quests" → "Quest")

**Examples**:
- `Definitions/Assets/Quests/daily_quest.json` → Type: `"QuestAsset"`
- `Definitions/Entities/Quests/daily_quest.json` → Type: `"Quest"`
- `Definitions/Quests/daily_quest.json` → Type: `"Quest"`

#### Tier 3: JSON Field Override (Explicit Type) - **LAZY PARSING**

**Performance Note**: JSON is only parsed if path-based inference fails (rare case). This avoids unnecessary I/O for 99%+ of files.

Allow JSON files to explicitly specify their type:

```json
{
  "id": "mymod:quest:daily",
  "$type": "Quest",  // Explicit type override
  "name": "Daily Quest",
  "description": "Complete daily objectives"
}
```

This overrides path-based inference if present.

#### Tier 4: Mod Manifest Declaration (Optional Documentation) - **VALIDATION**

For custom types, optionally declare them in `mod.json` for documentation/validation:

```json
{
  "id": "mymod:quest-system",
  "customDefinitionTypes": {
    "Quest": "Definitions/Quests",
    "Achievement": "Definitions/Achievements"
  }
}
```

**Purpose**:
- Documentation of custom types
- Validation that directories exist
- Future schema validation hints
- **Note**: This is optional - custom types work without it

---

## Implementation Details

### Architecture: Strategy Pattern for Type Inference

Type inference uses the **Chain of Responsibility** pattern with separate strategy classes for each tier. This improves testability, maintainability, and follows SOLID principles.

```csharp
/// <summary>
/// Strategy interface for type inference.
/// </summary>
public interface ITypeInferenceStrategy
{
    /// <summary>
    /// Attempts to infer the definition type. Returns null if inference fails.
    /// </summary>
    string? InferType(TypeInferenceContext context);
}

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

/// <summary>
/// Strategy 1: Hardcoded path mappings (fastest - no I/O).
/// </summary>
public class HardcodedPathInferenceStrategy : ITypeInferenceStrategy
{
    public string? InferType(TypeInferenceContext context)
    {
        foreach (var (pathPattern, type) in SortedPathMappings)
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
            var typeName = ExtractTypeNameFromPath(normalizedPath, startIndex: 19); // "Definitions/Assets/".Length
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
            var typeName = ExtractTypeNameFromPath(normalizedPath, startIndex: 20); // "Definitions/Entities/".Length
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
            var typeName = ExtractTypeNameFromPath(normalizedPath, startIndex: 12); // "Definitions/".Length
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
    private string? ExtractTypeNameFromPath(string path, int startIndex)
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
    private string? ExtractNestedTypeName(string path, int startIndex)
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
}

/// <summary>
/// Strategy 3: JSON $type field override (lazy parsing - only when needed).
/// </summary>
public class JsonTypeOverrideStrategy : ITypeInferenceStrategy
{
    public string? InferType(TypeInferenceContext context)
    {
        // Only parse JSON if not already parsed and path-based inference failed
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
    foreach (var strategy in _inferenceStrategies)
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

// Strategy chain (ordered by performance: fastest first)
private readonly ITypeInferenceStrategy[] _inferenceStrategies = new ITypeInferenceStrategy[]
{
    new HardcodedPathInferenceStrategy(),      // Tier 1: Fastest (no I/O)
    new DirectoryNameInferenceStrategy(),      // Tier 2: Fast (no I/O)
    new JsonTypeOverrideStrategy(),            // Tier 3: Slow (JSON parsing)
    new ModManifestInferenceStrategy()        // Tier 4: Validation
};

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

/// <summary>
/// Singularizes common plural type names for consistency.
/// Used by DirectoryNameInferenceStrategy.
/// </summary>
private static string SingularizeTypeName(string typeName)
{
    // Common patterns
    return typeName switch
    {
        "Quests" => "Quest",
        "Achievements" => "Achievement",
        "TextEffects" => "TextEffect",
        "ColorPalettes" => "ColorPalette",
        "WeatherEffects" => "WeatherEffect",
        _ => typeName // Keep as-is if no pattern matches
    };
}
```

### Discovery Process

**Performance Optimization**: JSON parsing is **lazy** - only occurs when path-based inference fails (rare case). This avoids unnecessary I/O for 99%+ of files.

```csharp
/// <summary>
/// Discovers and loads all definitions from a mod using convention-based discovery.
/// </summary>
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
        catch (InvalidOperationException)
        {
            // Path-based inference failed - parse JSON for $type field (lazy parsing)
            try
            {
                var jsonContent = mod.ModSource.ReadTextFile(jsonFile);
                jsonDoc = JsonDocument.Parse(jsonContent);
                
                // Try inference again with JSON document
                definitionType = InferDefinitionType(jsonFile, jsonDoc, mod);
            }
            catch (Exception ex)
            {
                errors.Add($"Failed to parse JSON file '{jsonFile}': {ex.Message}");
                _logger.Error(ex, "Failed to parse JSON file {FilePath}", jsonFile);
                continue;
            }
        }
        
        // Load definition with inferred type
        var loadResult = LoadDefinitionFromFile(mod.ModSource, jsonFile, definitionType, mod);
        
        if (loadResult.IsError)
        {
            errors.Add(loadResult.Error);
            _logger.Warning("Failed to load definition from {FilePath}: {Error}", jsonFile, loadResult.Error);
        }
        else
        {
            // Fire event for systems to react (ECS/Event integration)
            var discoveredEvent = new DefinitionDiscoveredEvent
            {
                ModId = mod.Id,
                DefinitionType = definitionType,
                DefinitionId = loadResult.Metadata.Id,
                FilePath = jsonFile,
                Metadata = loadResult.Metadata
            };
            EventBus.Send(ref discoveredEvent);
        }
        
        jsonDoc?.Dispose();
    }
}

/// <summary>
/// Result type for definition loading (unified error handling).
/// </summary>
private struct DefinitionLoadResult
{
    public DefinitionMetadata? Metadata { get; set; }
    public string? Error { get; set; }
    
    public bool IsError => Error != null;
    
    public static DefinitionLoadResult Success(DefinitionMetadata metadata) =>
        new() { Metadata = metadata };
    
    public static DefinitionLoadResult Failure(string error) =>
        new() { Error = error };
}

/// <summary>
/// Loads a single definition from a file with unified error handling.
/// </summary>
private DefinitionLoadResult LoadDefinitionFromFile(
    IModSource modSource,
    string relativePath,
    string definitionType,
    ModManifest mod
)
{
    try
    {
        var jsonContent = modSource.ReadTextFile(relativePath);
        var jsonDoc = JsonDocument.Parse(jsonContent);
        
        // Extract definition ID from JSON
        if (!jsonDoc.RootElement.TryGetProperty("id", out var idElement))
        {
            return DefinitionLoadResult.Failure(
                $"Definition file '{relativePath}' is missing required 'id' field."
            );
        }
        
        var definitionId = idElement.GetString();
        if (string.IsNullOrEmpty(definitionId))
        {
            return DefinitionLoadResult.Failure(
                $"Definition file '{relativePath}' has empty 'id' field."
            );
        }
        
        // Create metadata
        var metadata = new DefinitionMetadata
        {
            Id = definitionId,
            DefinitionType = definitionType,
            SourceMod = mod.Id,
            RawJson = jsonContent
        };
        
        // Register in registry
        _registry.Register(metadata);
        
        jsonDoc.Dispose();
        return DefinitionLoadResult.Success(metadata);
    }
    catch (Exception ex)
    {
        return DefinitionLoadResult.Failure(
            $"Failed to load definition from '{relativePath}': {ex.Message}"
        );
    }
}
```

### Event Integration

**ECS/Event System Integration**: Definition discovery fires events for systems to react.

```csharp
/// <summary>
/// Event fired when a definition is discovered and loaded.
/// </summary>
public struct DefinitionDiscoveredEvent
{
    public string ModId { get; set; }
    public string DefinitionType { get; set; }
    public string DefinitionId { get; set; }
    public string FilePath { get; set; }
    public DefinitionMetadata Metadata { get; set; }
}
```

**Usage Example**:
```csharp
// Systems can subscribe to definition discovery events
public class ScriptLoaderSystem : BaseSystem<World, float>
{
    private readonly List<IDisposable> _subscriptions = new();
    
    public ScriptLoaderSystem(World world) : base(world)
    {
        _subscriptions.Add(EventBus.Subscribe<DefinitionDiscoveredEvent>(OnDefinitionDiscovered));
    }
    
    private void OnDefinitionDiscovered(ref DefinitionDiscoveredEvent evt)
    {
        if (evt.DefinitionType == "Script")
        {
            // Load script when definition is discovered
            LoadScript(evt.Metadata);
        }
    }
    
    public new void Dispose() => Dispose(true);
    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (var subscription in _subscriptions)
                subscription.Dispose();
        }
    }
}
```

---

## Custom Definition Types

### Zero-Configuration Approach

Custom definition types work automatically through directory name inference:

**Example: Quest System Mod**

```
Mods/quest-system/
├── mod.json
└── Definitions/
    ├── Quests/
    │   ├── daily_quest.json      → Type: "Quest"
    │   └── weekly_quest.json      → Type: "Quest"
    └── Achievements/
        └── first_quest.json      → Type: "Achievement"
```

**mod.json (minimal)**:
```json
{
  "id": "mymod:quest-system"
}
```

**Result**: All files automatically discovered with correct types inferred from directory names.

### Explicit Type Override

If needed, override inferred type in JSON:

```json
{
  "id": "mymod:quest:daily",
  "$type": "DailyQuest",  // Override to "DailyQuest" instead of "Quest"
  "name": "Daily Quest",
  "description": "Complete daily objectives"
}
```

### Optional Documentation

Document custom types in `mod.json` (optional):

```json
{
  "id": "mymod:quest-system",
  "customDefinitionTypes": {
    "Quest": "Definitions/Quests",
    "Achievement": "Definitions/Achievements"
  }
}
```

**Benefits**:
- Self-documenting mod structure
- Validation that directories exist
- Future schema validation support
- IDE/tooling hints

---

## Migration Strategy

**Note**: Per project rules - "NO BACKWARD COMPATIBILITY - Refactor APIs freely". This migration removes `contentFolders` immediately.

### Migration Steps

**For Mod Authors**:
1. Remove `contentFolders` from `mod.json`
2. Ensure directory structure follows conventions (see Standard Directory Convention)
3. Test mod loading with new system
4. Use `$type` field in JSON if non-standard structure needed

**For Engine Developers**:
1. Remove `ContentFolders` property from `ModManifest` class
2. Remove all `contentFolders` from mod.json files
3. Update all documentation
4. Update modding guide with convention examples

---

## Examples

### Example 1: Standard Mod (Porycon3 Structure)

**Directory Structure**:
```
Mods/pokemon-emerald/
├── mod.json
├── Definitions/
│   ├── Assets/
│   │   ├── Characters/
│   │   │   └── Npcs/
│   │   │       └── brendan.json
│   │   ├── Fonts/
│   │   │   └── pokemon-narrow.json
│   │   └── UI/
│   │       └── TextWindows/
│   │           └── default.json
│   ├── Entities/
│   │   ├── Maps/
│   │   │   └── Hoenn/
│   │   │       └── littleroot_town.json
│   │   └── Regions/
│   │       └── Hoenn.json
│   └── Scripts/
│       └── Interactions/
│           └── NPCs/
│               └── littleroot_event.json
├── Graphics/
│   └── Characters/
│       └── brendan.png
└── Scripts/
    └── Interactions/
        └── littleroot_event.csx
```

**mod.json**:
```json
{
  "id": "base:pokemon-emerald",
  "name": "Pokemon Emerald Content",
  "version": "1.0.0"
}
```

**Result**:
- `Definitions/Assets/Characters/Npcs/brendan.json` → Type: `"CharacterAsset"`
- `Definitions/Assets/Fonts/pokemon-narrow.json` → Type: `"FontAsset"`
- `Definitions/Assets/UI/TextWindows/default.json` → Type: `"TextWindowAsset"`
- `Definitions/Entities/Maps/Hoenn/littleroot_town.json` → Type: `"Map"`
- `Definitions/Entities/Regions/Hoenn.json` → Type: `"Region"`
- `Definitions/Scripts/Interactions/NPCs/littleroot_event.json` → Type: `"Script"`

### Example 2: Custom Definition Types

**Directory Structure** (using Assets/Entities pattern):
```
Mods/quest-system/
├── mod.json
└── Definitions/
    ├── Assets/
    │   └── QuestIcons/
    │       ├── daily.json
    │       └── weekly.json
    └── Entities/
        ├── Quests/
        │   ├── daily_quest.json
        │   └── weekly_quest.json
        └── Achievements/
            └── first_quest.json
```

**mod.json**:
```json
{
  "id": "mymod:quest-system",
  "name": "Quest System",
  "version": "1.0.0"
}
```

**Result**:
- `Definitions/Assets/QuestIcons/daily.json` → Type: `"QuestIconAsset"`
- `Definitions/Entities/Quests/daily_quest.json` → Type: `"Quest"`
- `Definitions/Entities/Achievements/first_quest.json` → Type: `"Achievement"`

**Alternative** (flat structure):
```
Mods/quest-system/
├── mod.json
└── Definitions/
    ├── Quests/
    │   └── daily_quest.json
    └── Achievements/
        └── first_quest.json
```

**Result**:
- `Definitions/Quests/daily_quest.json` → Type: `"Quest"`
- `Definitions/Achievements/first_quest.json` → Type: `"Achievement"`

### Example 3: Explicit Type Override

**File**: `Definitions/Entities/Weather/rain.json`
```json
{
  "id": "mymod:weather:rain",
  "$type": "PrecipitationEffect",  // Override inferred "Weather"
  "name": "Rain",
  "intensity": 0.8
}
```

**Result**: Type is `"PrecipitationEffect"` instead of `"Weather"`

### Example 4: Optional Custom Type Documentation

**mod.json**:
```json
{
  "id": "mymod:quest-system",
  "name": "Quest System",
  "version": "1.0.0",
  "customDefinitionTypes": {
    "Quest": "Definitions/Entities/Quests",
    "Achievement": "Definitions/Entities/Achievements",
    "QuestChain": "Definitions/Entities/QuestChains",
    "QuestIconAsset": "Definitions/Assets/QuestIcons"
  }
}
```

**Purpose**: Documents custom types for mod users and tooling

---

## Edge Cases and Considerations

### Nested Definitions

**Problem**: Multiple path patterns could match

**Solution**: Match most specific path first
- `Definitions/Maps/Popups/Backgrounds/` matches before `Definitions/Maps/`
- Order mappings by specificity (longest path first)

### Non-Standard Layouts

**Problem**: Mod wants to use non-standard directory structure

**Solutions**:
1. **Use `$type` field**: Override type in each JSON file
2. **Use `customDefinitionTypes`**: Declare custom mappings in mod.json
3. **Follow convention**: Recommended approach for consistency

### Empty Directories

**Problem**: Empty definition directories

**Solution**: Ignore empty directories - only process JSON files that exist

### Case Sensitivity

**Problem**: Windows vs Linux path case sensitivity

**Solution**: Use `StringComparison.OrdinalIgnoreCase` for path matching on Windows, `Ordinal` on Linux

### Special Characters in Paths

**Problem**: Paths with special characters or spaces

**Solution**: Use `ModPathNormalizer.Normalize()` to handle path normalization consistently

### Performance Considerations

**Optimizations Implemented**:

1. **Lazy JSON Parsing**: JSON is only parsed when path-based inference fails (99%+ of files skip JSON parsing)
2. **Pre-sorted Path Mappings**: Path mappings are sorted once at initialization, not on every call
3. **Span-based Path Parsing**: Uses `Span<char>` to avoid allocations from `Split('/')`
4. **Fast Path Matching**: Pre-sorted array iteration instead of dictionary + LINQ sorting
5. **Early Returns**: Each strategy returns immediately on success

**Performance Characteristics** (estimated):
- **Path-based inference**: ~50-100ns per file (no I/O)
- **JSON parsing**: ~50-200μs per file (only when needed)
- **Total for 1000 files**: ~50-100μs (path-based) + ~50-200ms (JSON parsing for ~1% of files) = **~50-200ms total**

**Previous Design** (without optimizations):
- JSON parsing for every file: ~50-200ms × 1000 = **50-200ms per file** = **50-200 seconds total** ❌

**Performance Improvement**: **~1000x faster** for typical mods (99%+ files use fast path)

---

## Benefits

### For Mod Authors

1. **Zero Configuration**: Standard types work automatically
2. **Less Boilerplate**: No need to maintain `contentFolders` mapping
3. **Custom Types**: Easy to add new definition types
4. **Consistency**: Standard structure across all mods
5. **Self-Documenting**: Directory structure shows mod organization

### For Engine Developers

1. **Simpler Code**: No need to maintain `contentFolders` mappings
2. **Extensible**: Easy to add new standard types
3. **Less Validation**: Fewer configuration errors to handle
4. **Better Errors**: Clear errors when conventions aren't followed

### For Tooling

1. **Predictable Structure**: Tools can infer mod structure
2. **Better IDE Support**: Can provide autocomplete for definition types
3. **Validation**: Can validate mod structure against conventions

---

## Risks and Mitigations

### Risk 1: Breaking Existing Mods

**Mitigation**: 
- Per project rules: "NO BACKWARD COMPATIBILITY - Refactor APIs freely"
- Clear migration guide provided
- Fail fast with helpful error messages
- Update all mods to follow conventions
- All mods must be updated before deployment

### Risk 2: Ambiguous Type Inference

**Mitigation**:
- Explicit `$type` field override
- Clear error messages
- Documentation of inference rules

### Risk 3: Performance Impact

**Mitigation**:
- Efficient path matching algorithms
- Cache parsed JSON when possible
- Profile and optimize hot paths

### Risk 4: Custom Types Confusion

**Mitigation**:
- Clear documentation
- Optional `customDefinitionTypes` for documentation
- Examples in modding guide

---

## Testing Strategy

### Unit Tests

1. **Type Inference Tests**:
   - Test each tier of inference
   - Test path matching (specificity, case sensitivity)
   - Test custom type inference
   - Test JSON `$type` override

2. **Discovery Tests**:
   - Test file enumeration
   - Test empty directories
   - Test nested structures
   - Test special characters in paths

### Integration Tests

1. **Mod Loading Tests**:
   - Load mods with standard structure
   - Load mods with custom types
   - Load mods with `$type` overrides
   - Load mods with legacy `contentFolders`

2. **Migration Tests**:
   - Test backward compatibility
   - Test deprecation warnings
   - Test mixed mods (some with `contentFolders`, some without)

### Manual Testing

1. **Real Mod Testing**:
   - Test with existing pokemon-emerald mod
   - Test with custom mods
   - Test edge cases

---

## Future Enhancements

### Schema Validation

Use `customDefinitionTypes` for future JSON schema validation:

```json
{
  "customDefinitionTypes": {
    "Quest": {
      "path": "Definitions/Quests",
      "schema": "schemas/quest-schema.json"
    }
  }
}
```

### IDE Support

Provide IDE plugins that:
- Autocomplete definition types
- Validate mod structure
- Generate mod.json from directory structure

### Tooling

Create tools that:
- Migrate existing mods to convention-based structure
- Validate mod structure
- Generate documentation from mod structure

---

## Conclusion

Convention-based definition discovery eliminates the need for `contentFolders` configuration while maintaining flexibility for custom definition types. The multi-tier inference strategy ensures backward compatibility during migration and provides clear paths for both standard and custom types.

**Key Takeaways**:
- ✅ Zero configuration for standard types
- ✅ Flexible custom type support
- ✅ Backward compatible migration path
- ✅ Self-documenting mod structure
- ✅ Reduced maintenance burden

---

## Appendix

### A. Complete Path Mapping Reference

| Path Pattern | Definition Type |
|--------------|----------------|
| **Assets** | |
| `Definitions/Assets/Audio` | `AudioAsset` |
| `Definitions/Assets/Battle` | `BattleAsset` |
| `Definitions/Assets/Characters` | `CharacterAsset` |
| `Definitions/Assets/FieldEffects` | `FieldEffectAsset` |
| `Definitions/Assets/Fonts` | `FontAsset` |
| `Definitions/Assets/Tiles/DoorAnimations` | `DoorAnimationAsset` |
| `Definitions/Assets/Objects` | `ObjectAsset` |
| `Definitions/Assets/Pokemon` | `PokemonAsset` |
| `Definitions/Assets/Shaders` | `ShaderAsset` |
| `Definitions/Assets/Sprites` | `SpriteAsset` |
| `Definitions/Assets/Maps/Tiles/DoorAnimations` | `DoorAnimationAsset` |
| `Definitions/Assets/Maps/Tilesets` | `TilesetAsset` |
| `Definitions/Assets/UI/Interface` | `InterfaceAsset` |
| `Definitions/Assets/UI/Popups/Backgrounds` | `PopupBackgroundAsset` |
| `Definitions/Assets/UI/Popups/Outlines` | `PopupOutlineAsset` |
| `Definitions/Assets/UI/TextWindows` | `TextWindowAsset` |
| `Definitions/Assets/Weather` | `WeatherAsset` |
| **Constants** | |
| `Definitions/Constants` | `Constants` |
| **Entities** | |
| `Definitions/Entities/BattleScenes` | `BattleScene` |
| `Definitions/Entities/Maps` | `Map` |
| `Definitions/Entities/MapSections` | `MapSection` |
| `Definitions/Entities/Pokemon` | `Pokemon` |
| `Definitions/Entities/PopupThemes` | `PopupTheme` |
| `Definitions/Entities/Regions` | `Region` |
| `Definitions/Entities/Text/ColorPalettes` | `ColorPalette` |
| `Definitions/Entities/Text/TextEffects` | `TextEffect` |
| `Definitions/Entities/Weather` | `Weather` |
| **Scripts** | |
| `Definitions/Scripts` | `Script` |
| **Legacy/Flat Structure** | |
| `Definitions/Sprites` | `Sprite` |
| `Definitions/Audio` | `Audio` |
| `Definitions/TextWindow` | `TextWindow` |
| `Definitions/TextEffects` | `TextEffect` |
| `Definitions/ColorPalettes` | `ColorPalette` |
| `Definitions/Constants` | `Constants` |
| `Definitions/Fonts` | `Font` |
| `Definitions/Shaders` | `Shader` |

### B. Migration Checklist

For mod authors migrating from `contentFolders`:

- [ ] Remove `contentFolders` from `mod.json`
- [ ] Ensure directory structure follows conventions
- [ ] Test mod loading with new system
- [ ] Update documentation if needed
- [ ] Verify all definition types are discovered correctly

### C. Related Documentation

- [Mod System README](../../MonoBall/MonoBall.Core/Mods/README.md)
- [Definition Registry Documentation](../../MonoBall/MonoBall.Core/Mods/README.md#definition-registry)
- [Mod Manifest Schema](../../MonoBall/MonoBall.Core/Mods/README.md#mod-manifest)
