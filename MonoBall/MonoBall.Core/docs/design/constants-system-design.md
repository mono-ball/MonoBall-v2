# Constants System Design

## Executive Summary

This design moves hardcoded constants from C# static classes into mod JSON definitions, enabling mods to customize game
behavior without code changes. Constants are loaded through the existing mod system and accessed via a type-safe service
interface.

### Key Decisions

- **Location**: Constants defined in **core mod** (`base:monoball-core`) at `Mods/core/Definitions/Constants/`
- **Storage**: Constants stored as JSON definitions with type `"ConstantsDefinitions"` in mod files
- **Access**: Type-safe service interface (`IConstantsService`) with generic methods
- **Override**: Other mods can modify/extend/replace constants using existing definition operations (core mod loads
  first)
- **Naming**: Flat, descriptive names (e.g., `DefaultPlayerMovementSpeed`) rather than grouped
- **Pre-calculated Values**: All constants are pre-calculated and stored directly in JSON (no runtime computation)
- **Validation**: Fail-fast with clear exceptions if constants are missing
- **Performance**: Dictionary-based O(1) lookup, no allocations in hot paths

### Migration Path

1. Create constants service and interface
2. Add default constants JSON files to `Mods/core/Definitions/Constants/`
3. Update `Mods/core/mod.json` to include Constants folder
4. Update code to use service instead of static classes
5. Remove old constant classes

## Overview

This design proposes a system to move hardcoded constants from C# files into mod definitions, allowing mods to customize
game behavior without code changes. Constants will be loaded from mod JSON files and accessed through a centralized
service.

## Goals

1. **Moddability**: Allow mods to define and override constants
2. **Type Safety**: Maintain compile-time type checking where possible
3. **Performance**: Fast access without allocations in hot paths
4. **Compatibility**: Integrate seamlessly with existing mod system
5. **Validation**: Ensure required constants exist and have valid values

## Architecture

### Components

1. **ConstantDefinition**: JSON definition structure for constants
2. **ConstantsRegistry**: Service that loads and caches constants from mods
3. **IConstantsService**: Interface for accessing constants
4. **ConstantsService**: Implementation that provides type-safe access
5. **Default Constants**: Fallback values defined in code (fail-fast if missing)

### Definition Structure

Constants will be defined in JSON files within the **core mod** (`base:monoball-core`), following the existing
definition pattern. The core mod will contain default constants that can be overridden by other mods.

**Core Mod Structure:**

```
Mods/core/
├── mod.json
└── Definitions/
    └── Constants/
        ├── game.json
        └── messagebox.json
```

**Example: `Mods/core/Definitions/Constants/game.json`**

```json
{
  "id": "base:constants:game",
  "definitionType": "ConstantsDefinitions",
  "constants": {
    "TileChunkSize": 16,
    "GbaReferenceWidth": 240,
    "GbaReferenceHeight": 160,
    "DefaultCameraSmoothingSpeed": 0.1,
    "DefaultCameraZoom": 1.0,
    "DefaultCameraRotation": 0.0,
    "DefaultPlayerSpriteSheetId": "base:sprite:players/may/normal",
    "DefaultPlayerInitialAnimation": "face_south",
    "DefaultPlayerSpawnX": 10,
    "DefaultPlayerSpawnY": 8,
    "DefaultPlayerMovementSpeed": 4.0,
    "InputBufferTimeoutSeconds": 0.2,
    "InputBufferMaxSize": 5,
    "PopupBackgroundWidth": 80,
    "PopupBackgroundHeight": 24,
    "PopupBaseFontSize": 12,
    "PopupTextOffsetY": 3,
    "PopupTextPadding": 4,
    "PopupShadowOffsetX": 1,
    "PopupShadowOffsetY": 1,
    "PopupInteriorTilesX": 10,
    "PopupInteriorTilesY": 3,
    "PopupScreenPadding": 0
  },
  "validationRules": {
    "TileChunkSize": {
      "min": 1,
      "description": "Tile chunk size must be positive"
    },
    "DefaultPlayerMovementSpeed": {
      "min": 0.1,
      "max": 20.0,
      "description": "Movement speed must be between 0.1 and 20.0 tiles per second"
    },
    "InputBufferTimeoutSeconds": {
      "min": 0.0,
      "max": 5.0,
      "description": "Input buffer timeout must be between 0 and 5 seconds"
    },
    "InputBufferMaxSize": {
      "min": 1,
      "max": 50,
      "description": "Input buffer size must be between 1 and 50"
    }
  }
}
```

**Example: `Mods/core/Definitions/Constants/messagebox.json`**

```json
{
  "id": "base:constants:messagebox",
  "definitionType": "ConstantsDefinitions",
  "constants": {
    "ScenePriorityOffset": 20,
    "TextSpeedSlowSeconds": 0.133333,
    "TextSpeedMediumSeconds": 0.066667,
    "TextSpeedFastSeconds": 0.016667,
    "TextSpeedInstantSeconds": 0.0,
    "TextSpeedVariableName": "player:textSpeed",
    "DefaultTextSpeed": "medium",
    "DefaultFontId": "base:font:game/pokemon",
    "MessageBoxTilesheetId": "base:textwindow:tilesheet/message_box",
    "MessageBoxInteriorWidth": 216,
    "MessageBoxInteriorHeight": 32,
    "MessageBoxInteriorTileX": 2,
    "MessageBoxInteriorTileY": 15,
    "DefaultFontSize": 16,
    "TextPaddingTop": 1,
    "TextPaddingX": 0,
    "DefaultLineSpacing": 0,
    "ArrowBlinkFrames": 30,
    "DefaultTilesheetColumns": 7,
    "MaxVisibleLines": 2,
    "ScrollSpeedSlowPixelsPerSecond": 60.0,
    "ScrollSpeedMediumPixelsPerSecond": 120.0,
    "ScrollSpeedFastPixelsPerSecond": 240.0,
    "ScrollSpeedInstantPixelsPerSecond": 360.0,
    "DefaultScrollDistance": 16
  }
}
```

### Mod Integration

Constants will be loaded as definitions with type `"ConstantsDefinitions"` (consistent with other definition types like
`"FontDefinitions"`). The core mod (`base:monoball-core`) defines the default constants. Other mods can:

- **Create**: Define new constant groups
- **Modify**: Override specific constants in existing groups (loaded after core mod) - **nested objects merge
  recursively**
- **Replace**: Completely replace a constant group

**Core Mod `mod.json` Update:**

The core mod's `mod.json` must include the Constants folder:

```json
{
  "id": "base:monoball-core",
  "name": "MonBall Core Content",
  "contentFolders": {
    "ConstantsDefinitions": "Definitions/Constants",
    "FontDefinitions": "Definitions/Fonts",
    // ... other folders
  }
}
```

**Example Mod Override:**

Other mods can override individual constants by creating a definition file with the same ID. The `modify` operation
merges nested objects recursively, so you only need to specify the constants you want to change:

```json
{
  "id": "base:constants:game",
  "$operation": "modify",
  "constants": {
    "DefaultPlayerMovementSpeed": 6.0,
    "InputBufferTimeoutSeconds": 0.3
  }
}
```

**How It Works:**

- The `modify` operation merges the `constants` dictionary recursively
- Only the specified constants are overridden; all other constants remain unchanged
- This is a system-wide improvement to `JsonElementMerger` - nested objects merge recursively for `modify` operations
- Since the core mod loads first (priority 0), other mods loaded later will override these values

### Service Interface

```csharp
namespace MonoBall.Core.Constants
{
    /// <summary>
    /// Service for accessing game constants loaded from mods.
    /// </summary>
    public interface IConstantsService
    {
        /// <summary>
        /// Gets a constant value by key, throwing if not found.
        /// </summary>
        /// <typeparam name="T">The expected type of the constant.</typeparam>
        /// <param name="key">The constant key (e.g., "TileChunkSize").</param>
        /// <returns>The constant value.</returns>
        /// <exception cref="KeyNotFoundException">Thrown if the constant is not found.</exception>
        /// <exception cref="InvalidCastException">Thrown if the constant cannot be converted to T.</exception>
        T Get<T>(string key) where T : struct;

        /// <summary>
        /// Gets a string constant value, throwing if not found.
        /// </summary>
        /// <param name="key">The constant key.</param>
        /// <returns>The constant value.</returns>
        /// <exception cref="KeyNotFoundException">Thrown if the constant is not found.</exception>
        string GetString(string key);

        /// <summary>
        /// Tries to get a constant value, returning false if not found.
        /// </summary>
        /// <typeparam name="T">The expected type of the constant.</typeparam>
        /// <param name="key">The constant key.</param>
        /// <param name="value">The constant value if found.</param>
        /// <returns>True if the constant was found, false otherwise.</returns>
        bool TryGet<T>(string key, out T value) where T : struct;

        /// <summary>
        /// Tries to get a string constant value, returning false if not found.
        /// </summary>
        /// <param name="key">The constant key.</param>
        /// <param name="value">The constant value if found.</param>
        /// <returns>True if the constant was found, false otherwise.</returns>
        bool TryGetString(string key, out string? value);

        /// <summary>
        /// Checks if a constant exists.
        /// </summary>
        /// <param name="key">The constant key.</param>
        /// <returns>True if the constant exists, false otherwise.</returns>
        bool Contains(string key);
    }
}
```

### Implementation

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using MonoBall.Core.Mods;
using MonoBall.Core.Mods.Utilities;
using Serilog;

namespace MonoBall.Core.Constants
{
    /// <summary>
    /// Service for accessing game constants loaded from mods.
    /// Wraps the mod definition registry (like FontService and SpriteLoaderService) and provides
    /// fast, type-safe access to constants. Caches deserialized values to avoid allocations in hot paths.
    /// 
    /// This service follows the same pattern as other definition services:
    /// - Uses IModManager.GetDefinition&lt;T&gt;() to access definitions
    /// - Uses IModManager.GetDefinitionMetadata() for metadata
    /// - Caches frequently accessed values
    /// - Provides domain-specific access methods
    /// </summary>
    public class ConstantsService : IConstantsService
    {
        private readonly Dictionary<string, JsonElement> _rawConstants;
        private readonly Dictionary<string, object> _valueCache;
        private readonly ILogger _logger;

        private readonly IModManager _modManager;

        /// <summary>
        /// Initializes a new instance of ConstantsService.
        /// </summary>
        /// <param name="modManager">The mod manager to load constants from. Must not be null.</param>
        /// <param name="logger">The logger instance. Must not be null.</param>
        /// <exception cref="ArgumentNullException">Thrown if modManager or logger is null.</exception>
        public ConstantsService(IModManager modManager, ILogger logger)
        {
            _modManager = modManager ?? throw new ArgumentNullException(nameof(modManager));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _rawConstants = new Dictionary<string, JsonElement>();
            _valueCache = new Dictionary<string, object>();

            LoadConstantsFromMods(_modManager);
        }

        private void LoadConstantsFromMods(IModManager modManager)
        {
            var constantDefinitions = modManager.Registry.GetByType("ConstantsDefinitions");
            
            foreach (var defId in constantDefinitions)
            {
                // Use GetDefinitionMetadata() for consistency with other services
                var metadata = modManager.GetDefinitionMetadata(defId);
                if (metadata == null)
                {
                    _logger.Warning("Constants definition metadata not found for '{DefId}'", defId);
                    continue;
                }

                // Use GetDefinition<T>() for consistency with other services
                var definition = modManager.GetDefinition<ConstantDefinition>(defId);
                if (definition == null)
                {
                    _logger.Warning("Failed to load constants definition '{DefId}'", defId);
                    continue;
                }

                // Merge constants into flat dictionary (later mods override earlier ones)
                // This flattens the constants dictionary from all definition files
                foreach (var kvp in definition.Constants)
                {
                    _rawConstants[kvp.Key] = kvp.Value;
                }
            }

            _logger.Information("Loaded {Count} constants from {DefCount} definition(s)", _rawConstants.Count, constantDefinitions.Count());
        }

        /// <summary>
        /// Validates that required constants exist. Call after service creation to fail-fast.
        /// </summary>
        /// <param name="requiredKeys">The keys that must exist.</param>
        /// <exception cref="InvalidOperationException">Thrown if any required constants are missing.</exception>
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

        /// <summary>
        /// Validates all constants against their validation rules (if defined).
        /// Call after service creation to fail-fast on invalid values.
        /// Validates the final merged constants (after all mod overrides), not individual definitions.
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown if any constants fail validation.</exception>
        public void ValidateConstants()
        {
            var constantDefinitions = _modManager.Registry.GetByType("ConstantsDefinitions");
            var validationRules = new Dictionary<string, ConstantValidationRule>();

            // Collect all validation rules from all definitions (later mods override earlier ones)
            foreach (var defId in constantDefinitions)
            {
                var definition = _modManager.GetDefinition<ConstantDefinition>(defId);
                if (definition?.ValidationRules == null)
                {
                    continue;
                }

                // Merge validation rules (later mods override earlier ones, same as constants)
                foreach (var ruleKvp in definition.ValidationRules)
                {
                    validationRules[ruleKvp.Key] = ruleKvp.Value;
                }
            }

            // Validate final merged constants against collected validation rules
            var validationErrors = new List<string>();
            foreach (var ruleKvp in validationRules)
            {
                var constantKey = ruleKvp.Key;
                var rule = ruleKvp.Value;

                if (!_rawConstants.TryGetValue(constantKey, out var element))
                {
                    continue; // Constant not found, skip (existence is validated separately)
                }

                // Validate numeric constants
                if (element.ValueKind == JsonValueKind.Number)
                {
                    var numericValue = element.GetDouble();

                    if (rule.Min.HasValue && numericValue < rule.Min.Value)
                    {
                        validationErrors.Add(
                            $"Constant '{constantKey}' value {numericValue} is below minimum {rule.Min.Value}. " +
                            $"Value must be >= {rule.Min.Value}. " +
                            (string.IsNullOrEmpty(rule.Description) ? "" : $"({rule.Description})")
                        );
                    }

                    if (rule.Max.HasValue && numericValue > rule.Max.Value)
                    {
                        validationErrors.Add(
                            $"Constant '{constantKey}' value {numericValue} is above maximum {rule.Max.Value}. " +
                            $"Value must be <= {rule.Max.Value}. " +
                            (string.IsNullOrEmpty(rule.Description) ? "" : $"({rule.Description})")
                        );
                    }
                }
            }

            if (validationErrors.Any())
            {
                throw new InvalidOperationException(
                    "Constant validation failed:\n" + string.Join("\n", validationErrors)
                );
            }
        }

        public T Get<T>(string key) where T : struct
        {
            ValidateKey(key);

            // Check cache first (avoid deserialization)
            if (_valueCache.TryGetValue(key, out var cached) && cached is T typed)
            {
                return typed;
            }

            if (!_rawConstants.TryGetValue(key, out var element))
            {
                throw new KeyNotFoundException(
                    $"Constant '{key}' not found. Ensure it is defined in a mod's Constants definition."
                );
            }

            var value = DeserializeAndValidate<T>(key, element);
            _valueCache[key] = value; // Cache for future access
            return value;
        }

        public string GetString(string key)
        {
            ValidateKey(key);

            // Check cache first
            if (_valueCache.TryGetValue(key, out var cached) && cached is string str)
            {
                return str;
            }

            if (!_rawConstants.TryGetValue(key, out var element))
            {
                throw new KeyNotFoundException(
                    $"Constant '{key}' not found. Ensure it is defined in a mod's Constants definition."
                );
            }

            if (element.ValueKind != JsonValueKind.String)
            {
                throw new InvalidCastException(
                    $"Constant '{key}' is not a string. Found: {element.ValueKind}"
                );
            }

            var value = element.GetString() ?? throw new InvalidOperationException(
                $"Constant '{key}' has null string value."
            );
            
            _valueCache[key] = value; // Cache for future access
            return value;
        }

        public bool TryGet<T>(string key, out T value) where T : struct
        {
            value = default;
            if (string.IsNullOrEmpty(key) || !_rawConstants.TryGetValue(key, out var element))
            {
                return false;
            }

            // Check cache first
            if (_valueCache.TryGetValue(key, out var cached) && cached is T typed)
            {
                value = typed;
                return true;
            }

            try
            {
                value = DeserializeAndValidate<T>(key, element);
                _valueCache[key] = value; // Cache for future access
                return true;
            }
            catch (InvalidCastException)
            {
                // Type mismatch is expected, return false
                return false;
            }
            catch (JsonException ex)
            {
                // Log JSON errors but don't fail
                _logger.Warning("Failed to deserialize constant '{Key}': {Error}", key, ex.Message);
                return false;
            }
            // Let other exceptions propagate (fail-fast)
        }

        public bool TryGetString(string key, out string? value)
        {
            value = null;
            if (string.IsNullOrEmpty(key) || !_rawConstants.TryGetValue(key, out var element))
            {
                return false;
            }

            // Check cache first
            if (_valueCache.TryGetValue(key, out var cached) && cached is string str)
            {
                value = str;
                return true;
            }

            if (element.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            value = element.GetString();
            if (value != null)
            {
                _valueCache[key] = value; // Cache for future access
            }
            return value != null;
        }

        public bool Contains(string key)
        {
            return !string.IsNullOrEmpty(key) && _rawConstants.ContainsKey(key);
        }

        private static void ValidateKey(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                throw new ArgumentException("Key cannot be null or empty.", nameof(key));
            }
        }

        private T DeserializeAndValidate<T>(string key, JsonElement element) where T : struct
        {
            // Validate number precision for integer types
            if (typeof(T) == typeof(int) && element.ValueKind == JsonValueKind.Number)
            {
                var dbl = element.GetDouble();
                if (dbl != Math.Floor(dbl))
                {
                    throw new InvalidCastException(
                        $"Constant '{key}' must be an integer. Found: {dbl}"
                    );
                }
            }

            try
            {
                return JsonSerializer.Deserialize<T>(
                    element.GetRawText()
                );
            }
            catch (JsonException ex)
            {
                throw new InvalidCastException(
                    $"Failed to deserialize constant '{key}' to type {typeof(T).Name}. " +
                    $"Value: {element.GetRawText()}. Error: {ex.Message}",
                    ex
                );
            }
        }
    }
}
```

### Constant Definition Model

```csharp
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MonoBall.Core.Constants
{
    /// <summary>
    /// Represents a constants definition loaded from a mod.
    /// Contains multiple constants grouped together (e.g., all game constants or all message box constants).
    /// </summary>
    public class ConstantDefinition
    {
        /// <summary>
        /// Gets or sets the unique identifier of this constants definition.
        /// </summary>
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the definition type (should be "ConstantsDefinitions").
        /// </summary>
        [JsonPropertyName("definitionType")]
        public string DefinitionType { get; set; } = "ConstantsDefinitions";

        /// <summary>
        /// Gets or sets the dictionary of constant keys to their JSON values.
        /// Each key-value pair represents one constant (e.g., "TileChunkSize": 16).
        /// </summary>
        [JsonPropertyName("constants")]
        public Dictionary<string, JsonElement> Constants { get; set; } = new();

        /// <summary>
        /// Gets or sets the optional validation rules for constants.
        /// Maps constant keys to their validation constraints (min/max values).
        /// Follows the same pattern as ScriptParameterDefinition and ShaderParameterDefinition.
        /// </summary>
        [JsonPropertyName("validationRules")]
        public Dictionary<string, ConstantValidationRule>? ValidationRules { get; set; }
    }

    /// <summary>
    /// Validation rule for a constant value.
    /// Similar to ScriptParameterDefinition and ShaderParameterDefinition, provides min/max validation.
    /// Note: Unlike Script/Shader parameters (which are objects with inline min/max properties),
    /// constants are primitive values, so validation rules are defined separately in a dictionary.
    /// </summary>
    public class ConstantValidationRule
    {
        /// <summary>
        /// Gets or sets the optional minimum value (for numeric constants).
        /// </summary>
        [JsonPropertyName("min")]
        public double? Min { get; set; }

        /// <summary>
        /// Gets or sets the optional maximum value (for numeric constants).
        /// </summary>
        [JsonPropertyName("max")]
        public double? Max { get; set; }

        /// <summary>
        /// Gets or sets the optional description of the validation rule.
        /// </summary>
        [JsonPropertyName("description")]
        public string? Description { get; set; }
    }
}
```

## Migration Strategy

### Phase 1: Create Constants System

1. Ensure `<Nullable>enable</Nullable>` is set in the project file
2. **Update `JsonElementMerger.Merge()`** to merge nested objects recursively for `modify` operations (system-wide
   improvement)
3. Create `IConstantsService` interface
4. Create `ConstantsService` implementation (with caching and validation)
5. Create `ConstantDefinition` and `ConstantValidationRule` models (following
   ScriptParameterDefinition/ShaderParameterDefinition pattern)
6. Register service in `MonoBallGame.LoadModsSynchronously()` **after** mods are loaded
7. Call `ValidateRequiredConstants()` with critical constant keys
8. Call `ValidateConstants()` to validate all constants against their validation rules

### Phase 2: Create Default Constants Definitions in Core Mod

1. Create `Mods/core/Definitions/Constants/` folder
2. Create `game.json` with all `GameConstants` values (ID: `base:constants:game`)
3. Create `messagebox.json` with all `MessageBoxConstants` values (ID: `base:constants:messagebox`)
4. Update `Mods/core/mod.json` to include `"ConstantsDefinitions": "Definitions/Constants"` in `contentFolders`

### Phase 3: Update Code to Use Service

1. Inject `IConstantsService` into systems/components that need constants
2. Replace `GameConstants.X` with `_constantsService.Get<int>("X")`
3. Replace `MessageBoxConstants.X` with `_constantsService.Get<int>("X")` or `GetString("X")`
4. Remove old constant classes after migration complete

### Phase 4: Add Validation Rules (Optional)

1. Add validation rules to core mod JSON files (optional but recommended)
2. Validation rules are merged the same way as constants (later mods override earlier ones)

**Service Registration Pattern:**

```csharp
// In MonoBallGame.LoadModsSynchronously(), after mods load:
var constantsService = new ConstantsService(modManager, logger);

// Validate required constants exist
constantsService.ValidateRequiredConstants(new[] { 
    "TileChunkSize", 
    "DefaultPlayerMovementSpeed",
    "GbaReferenceWidth",
    "GbaReferenceHeight",
    // ... other critical constants
});

// Validate constants against validation rules (if defined)
constantsService.ValidateConstants();

// Register service (following same pattern as ModManager and FontService)
Services.AddService(typeof(IConstantsService), constantsService);
```

**Note**: Consider creating a `ConstantsServiceFactory` similar to `FontServiceFactory` if you need to prevent duplicate
registration or preserve cached instances. For now, direct registration is sufficient since ConstantsService is created
once after mods load.

## Usage Examples

### In a System

**Important**: Cache frequently-used constants in the constructor to avoid repeated lookups in Update/Render loops.

```csharp
public class PlayerSystem : BaseSystem<World, float>
{
    private readonly IConstantsService _constants;
    private readonly float _movementSpeed;
    private readonly float _bufferTimeout;
    private readonly string _spriteSheetId;

    public PlayerSystem(World world, IConstantsService constants) : base(world)
    {
        _constants = constants ?? throw new ArgumentNullException(nameof(constants));
        
        // Cache constants used in Update/Render loops
        _movementSpeed = _constants.Get<float>("DefaultPlayerMovementSpeed");
        _bufferTimeout = _constants.Get<float>("InputBufferTimeoutSeconds");
        _spriteSheetId = _constants.GetString("DefaultPlayerSpriteSheetId");
    }

    public override void Update(in float deltaTime)
    {
        // Use cached constants (no lookup overhead)
        // Use _movementSpeed, _bufferTimeout, _spriteSheetId...
    }
}
```

### In a Component Initialization

```csharp
var popupWidth = _constants.Get<int>("PopupBackgroundWidth");
var popupHeight = _constants.Get<int>("PopupBackgroundHeight");
```

### With TryGet for Optional Constants

**Important**: Use `TryGet<T>()` only for truly optional constants (e.g., mod-specific extensions). Never use as a
fallback for required constants - use `Get<T>()` which fails fast.

```csharp
// ✅ Good: Optional mod-specific constant
if (_constants.TryGet<int>("CustomModConstant", out var value))
{
    // Use custom constant
}

// ❌ Bad: Using TryGet as fallback for required constant
if (_constants.TryGet<int>("TileChunkSize", out var size))
{
    // Should use Get<int>() instead - fail fast if missing
}
```

## Benefits

1. **Moddability**: Mods can customize game behavior without code changes
2. **Type Safety**: Generic methods provide compile-time type checking
3. **Performance**: Dictionary lookup is O(1), no allocations in hot paths
4. **Validation**: Fail-fast with clear error messages
5. **Consistency**: Uses existing mod system patterns
6. **Flexibility**: Supports any JSON-serializable type

## Design Decision: Option 2 - Improved Consistency

This design follows **Option 2** from the consistency analysis: keeping multiple constants per file while improving
alignment with other definition patterns.

**Key Consistency Improvements:**

- Uses `IModManager.GetDefinition<T>()` instead of direct registry access (like FontService, SpriteLoaderService)
- Uses `IModManager.GetDefinitionMetadata()` for metadata (consistent with other services)
- Service wraps registry access and provides domain-specific APIs (same pattern as other services)
- ConstantDefinition uses `[JsonPropertyName]` attributes (consistent with FontDefinition, SpriteDefinition)

**Trade-offs:**

- Multiple constants per file (more practical than one file per constant)
- Constants are flattened from definition files (unique to constants, but necessary for the grouped structure)
- Override pattern merges nested `constants` dictionary recursively (same granularity as other definitions with nested
  structures)

## Considerations

1. **Core Mod Location**: Constants are defined in the core mod (`base:monoball-core`), which loads first (priority 0)
2. **Service Initialization**: Service must be created **after** mods are loaded (in `LoadModsSynchronously()`)
3. **Service Pattern**: Follows same pattern as FontService and SpriteLoaderService - wraps `IModManager` and provides
   domain-specific APIs
4. **Type Conversion**: JSON numbers are always `double`; validation ensures integer precision for `int` types
5. **Default Values**: No fallback values - fail-fast if constant missing
6. **String Constants**: Separate method for strings since they're reference types (can't use `where T : struct`)
7. **Load Order**: Constants loaded in mod load order; core mod loads first, other mods can override
8. **Validation**: `ValidateRequiredConstants()` should be called after service creation to fail-fast
9. **Core Mod Dependency**: All constants must be defined in core mod; other mods can only override, not create new
   required constants
10. **Performance**: Constants are cached after first access; systems should cache frequently-used constants in
    constructors
11. **Caching**: Deserialized values are cached to avoid allocations in hot paths (`Get<T>()` and `GetString()`)
12. **Override Pattern**: Mods override individual constants using `$operation: "modify"` - the `constants` dictionary
    merges recursively, so only specified constants need to be included
13. **Recursive Merging**: The `modify` operation merges nested objects recursively (system-wide improvement to
    `JsonElementMerger`), allowing granular overrides without replacing entire nested structures

## Pre-calculated Constants

Some constants were previously computed from frame-based values (assuming 60 FPS). These values are now pre-calculated
and stored directly in JSON:

**Text Speed Delays** (seconds per character, converted from frame delays):

- `TextSpeedSlowSeconds = 8 frames / 60 FPS = 0.133333` → `0.133333`
- `TextSpeedMediumSeconds = 4 frames / 60 FPS = 0.066667` → `0.066667`
- `TextSpeedFastSeconds = 1 frame / 60 FPS = 0.016667` → `0.016667`
- `TextSpeedInstantSeconds = 0.0` (instant, no delay)

**Scroll Speeds** (pixels per second, converted from pixels per frame at 60 FPS):

- `ScrollSpeedSlowPixelsPerSecond = 1 pixel/frame * 60 FPS = 60.0`
- `ScrollSpeedMediumPixelsPerSecond = 2 pixels/frame * 60 FPS = 120.0`
- `ScrollSpeedFastPixelsPerSecond = 4 pixels/frame * 60 FPS = 240.0`
- `ScrollSpeedInstantPixelsPerSecond = 6 pixels/frame * 60 FPS = 360.0`

**Approach**: All values are pre-calculated and stored directly in JSON. This simplifies the code, avoids runtime
computation, and allows mods to override any value independently.

## Constant Naming Strategy

### Flat Naming (Recommended)

Use descriptive, flat names:

- `DefaultPlayerMovementSpeed`
- `MessageBoxInteriorWidth`
- `PopupBackgroundHeight`

**Pros**: Simple, clear, easy to search
**Cons**: Can get verbose

### Grouped Naming (Alternative)

Use namespace-like prefixes:

- `game:player:movementSpeed`
- `ui:messagebox:interiorWidth`
- `map:popup:backgroundHeight`

**Pros**: Organized, prevents collisions
**Cons**: More complex parsing, harder to type

**Decision**: Use flat naming for simplicity and clarity. Constants are already scoped by their definition file.

## Architecture Analysis

See [constants-system-design-analysis.md](./constants-system-design-analysis.md) for detailed analysis of:

- Architecture issues and SOLID/DRY violations
- Performance concerns and optimization opportunities
- ECS/event system integration considerations
- Mod system integration patterns

See [constants-system-design-cursorrules-analysis.md](./constants-system-design-cursorrules-analysis.md) for
.cursorrules compliance analysis:

- Verification against all critical rules
- .NET 10 C# best practices compliance
- SOLID/DRY principle evaluation
- Implementation recommendations

See [constants-system-design-inconsistencies.md](./constants-system-design-inconsistencies.md) for consistency analysis:

- Comparison with other definition types (FontDefinitions, SpriteDefinitions, etc.)
- Access pattern inconsistencies
- Structure and override pattern differences
- Recommendations for alignment

### Critical Issues Identified

1. **Service Initialization**: Must create service after mods load (not in constructor)
2. **Performance**: Cache deserialized values to avoid allocations in hot paths
3. **Exception Handling**: Don't swallow all exceptions in `TryGet<T>()`
4. **Validation**: Add null checks and required constant validation
5. **DRY**: Extract duplicate validation logic

### Recommended Fixes

The analysis document provides detailed recommendations and code examples for addressing these issues.

## Implementation Requirement: Recursive Nested Object Merging

**System-Wide Improvement**: The `modify` operation merges nested objects recursively, not just top-level properties.
This is implemented as part of the constants system design but benefits all definition types.

**Implementation Details:**

The `JsonElementMerger.Merge()` method must be updated to merge nested objects recursively for `modify` operations:

```csharp
// Current behavior (line 67-70): Replaces nested objects
else
{
    merged[prop.Name] = prop.Value.Clone(); // Replaces entire nested object
}

// Updated behavior: Merge nested objects recursively for modify operation
else if (merged.ContainsKey(prop.Name) && 
         merged[prop.Name].ValueKind == JsonValueKind.Object &&
         prop.Value.ValueKind == JsonValueKind.Object)
{
    // Merge nested objects recursively (same as extend)
    merged[prop.Name] = Merge(merged[prop.Name], prop.Value, false);
}
else
{
    merged[prop.Name] = prop.Value.Clone();
}
```

**Benefits:**

- Constants can override individual values without replacing entire dictionary
- All definition types with nested structures benefit from this improvement
- Consistent behavior: `modify` merges recursively, `extend` merges recursively, `replace` replaces entirely

**Migration Note**: This change affects the behavior of `modify` operations for all definition types. Existing mods
using `modify` on definitions with nested objects will now merge recursively instead of replacing entirely.

## Validation Rules

Constants support validation rules similar to `ScriptParameterDefinition` and `ShaderParameterDefinition`, but
structured differently due to the data model.

**Pattern Comparison:**

**ScriptDefinition/ShaderDefinition** (parameters are objects):

```json
{
  "parameters": [
    {
      "name": "minWaitTime",
      "type": "float",
      "defaultValue": 1.0,
      "min": 0.0,
      "max": 10.0,
      "description": "Minimum wait time"
    }
  ]
}
```

- Parameters are an **array of objects**
- Validation rules (`min`/`max`) are **inline** with each parameter

**ConstantDefinition** (constants are primitive values):

```json
{
  "constants": {
    "DefaultPlayerMovementSpeed": 4.0
  },
  "validationRules": {
    "DefaultPlayerMovementSpeed": {
      "min": 0.1,
      "max": 20.0,
      "description": "Movement speed must be between 0.1 and 20.0"
    }
  }
}
```

- Constants are a **dictionary of key-value pairs** (primitive values)
- Validation rules are defined in a **separate dictionary** mapping constant keys to rules

**Why Different:**

- Constants are stored as primitive values (`"TileChunkSize": 16`), not objects with properties
- Cannot add metadata (min/max) directly to primitive values
- Separate `validationRules` dictionary is necessary to map constant keys to their validation constraints

**Purpose:**

- Ensure values are within expected bounds (e.g., `DefaultPlayerMovementSpeed > 0`, `TileChunkSize > 0`)
- Prevent invalid configurations and catch mod errors early
- Not for overflow protection (JSON uses doubles, and type conversion already validates precision for int types)

**Validation:**

- Validation rules are optional - constants without rules are not validated
- Validation runs after constants are loaded via `ValidateConstants()` method
- Validation fails fast with clear error messages if any constant violates its rules
- Uses the same validation logic pattern as `ScriptDefinition` and `ShaderDefinition` parameter validation

**Example:**

```json
{
  "id": "base:constants:game",
  "constants": {
    "DefaultPlayerMovementSpeed": 4.0,
    "TileChunkSize": 16
  },
  "validationRules": {
    "DefaultPlayerMovementSpeed": {
      "min": 0.1,
      "max": 20.0,
      "description": "Movement speed must be between 0.1 and 20.0 tiles per second"
    },
    "TileChunkSize": {
      "min": 1,
      "description": "Tile chunk size must be positive"
    }
  }
}
```

## Future Enhancements

1. **Hot Reload**: Support reloading constants during development (applies to all definitions)

**Note**: Future enhancements should align with patterns used by other definitions. Features like validation rules and
hot reload would benefit all definition types, not just constants. Type-safe wrappers are not included since they don't
exist for other definition types (which also use string-based dictionary lookups), and would be inconsistent with the
rest of the codebase.

