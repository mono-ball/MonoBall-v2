# Mod Loading System

This mod loading system provides a comprehensive solution for loading, managing, and querying mod definitions in
MonoBall.

## Overview

The mod system supports:

- **Load Order Resolution**: Determines mod load order from root `mod.manifest` or priority/dependencies
- **Definition Management**: Loads definitions with support for modify/extend/replace operations
- **Collision Resolution**: Handles duplicate definition IDs across mods
- **Validation**: Finds inconsistencies in mod manifests and definitions
- **Queryable Storage**: Efficient registry for looking up definitions by ID or type

## Architecture

### Core Components

1. **ModManager**: Main entry point for the mod system
2. **ModLoader**: Handles discovery, load order resolution, and definition loading
3. **DefinitionRegistry**: Read-only storage for loaded definitions
4. **ModValidator**: Validates mods and finds inconsistencies

### Data Models

- **ModManifest**: Represents a mod's `mod.json` file
- **DefinitionMetadata**: Metadata about a loaded definition
- **DefinitionOperation**: Enum for modify/extend/replace operations

## Usage

### Basic Usage

```csharp
// Initialize the mod manager
var modManager = new ModManager(); // Uses "Mods" directory by default

// Load all mods
var errors = new List<string>();
bool success = modManager.Load(errors);

if (!success)
{
    foreach (var error in errors)
    {
        Console.WriteLine(error);
    }
}

// Query definitions
var fontDef = modManager.GetDefinition<FontDefinition>("base:font:game/pokemon");
var allFonts = modManager.GetDefinitionsByType("FontDefinitions");
```

### Validation

```csharp
var validator = new ModValidator("Mods");
var issues = validator.ValidateAll();

foreach (var issue in issues)
{
    Console.WriteLine($"[{issue.Severity}] {issue.Message}");
}
```

## Mod Structure

### Root mod.manifest

Create a `mod.manifest` file in the `Mods` directory to specify explicit load order:

```json
{
  "modOrder": [
    "base:monoball-core",
    "pokemon:emerald"
  ]
}
```

If this file doesn't exist, mods are loaded by priority (lower first) and dependencies.

### mod.json

Each mod directory must contain a `mod.json` file:

```json
{
  "id": "base:monoball-core",
  "name": "MonBall Core Content",
  "author": "MonBall Team",
  "version": "1.0.0",
  "description": "Base game content",
  "priority": 0,
  "contentFolders": {
    "FontDefinitions": "Definitions/Fonts",
    "TileBehaviorDefinitions": "Definitions/TileBehaviors"
  },
  "dependencies": []
}
```

### Definition Files

Definition files are JSON files with an `id` field. They can specify an operation type:

```json
{
  "id": "base:font:game/pokemon",
  "$operation": "modify",
  "defaultSize": 18
}
```

**Operations:**

- **Create** (default): Creates a new definition
- **Modify**: Updates existing properties, keeps unspecified properties
- **Extend**: Adds new properties, merges nested objects
- **Replace**: Completely replaces the existing definition

## Definition Resolution

When multiple mods define the same definition ID:

1. Definitions are loaded in mod load order
2. If a definition with the same ID already exists:
    - **Modify**: Merges properties, new values override old ones
    - **Extend**: Adds new properties, merges nested objects
    - **Replace**: Completely replaces the old definition
3. The registry tracks which mod originally created and last modified each definition

## Storage Design

The `DefinitionRegistry` provides:

- **Read-only after load**: Locked after initial load to prevent modifications
- **Efficient lookups**: Dictionary-based storage for O(1) ID lookups
- **Type indexing**: Secondary index by definition type for type-based queries
- **Metadata tracking**: Tracks source mod, operation type, and file path

## Validation

The validator checks for:

- Missing or invalid `mod.json` files
- Duplicate mod IDs
- Duplicate definition IDs (warnings)
- Missing dependencies
- Circular dependencies
- Missing required fields (id, name, version)

## Inconsistencies Found

The current mod structure has these inconsistencies:

1. **Duplicate Definition IDs**:
    - `base:font:debug/mono` is defined in both `core` and `pokemon-emerald` mods
    - `base:font:game/pokemon` is defined in both mods

These should be resolved by:

- Removing duplicates from one mod
- Using modify/extend operations if intentional
- Or ensuring only one mod defines base definitions

## Integration

To integrate with MonoBallGame:

```csharp
public class MonoBallGame : Game
{
    private ModManager _modManager;

    protected override void Initialize()
    {
        base.Initialize();
        
        _modManager = new ModManager();
        var errors = new List<string>();
        _modManager.Load(errors);
        
        // Log errors if any
        foreach (var error in errors)
        {
            System.Diagnostics.Debug.WriteLine(error);
        }
    }
}
```



