# Missing Definition Types Analysis

**Date:** 2025-01-XX  
**Purpose:** Analyze actual mod directory structures to identify definition types not covered in the convention-based discovery design and determine their proper placement.

---

## Current Mod Structure Analysis

### Mods Analyzed

1. **core** (`base:monoball-core`)
2. **pokemon-emerald** (`pokemon:emerald`)
3. **test-shaders** (`base:test-shaders`)

---

## Missing Definition Types

### 1. ColorPalettes

**Current Location:** `Definitions/ColorPalettes/`

**Examples:**
- `Mods/core/Definitions/ColorPalettes/fire.json`
- `Mods/core/Definitions/ColorPalettes/ice.json`
- `Mods/core/Definitions/ColorPalettes/poison.json`

**Nature:** Asset definition - defines color data used by text effects for color cycling

**Proposed Location:** `Definitions/Entities/Text/ColorPalettes/`

**Inferred Type:** `ColorPalette`

**Rationale:** 
- Pure data/configuration definition (color arrays) - no external file references
- Similar to Constants (game configuration/logic)
- Grouped with TextEffects under `Text/` parent directory
- Similar to how Popups groups Backgrounds and Outlines under `UI/Popups/`
- Shows relationship between ColorPalettes and TextEffects

**Rationale:** 
- References color data (not external files, but still an asset)
- Used by TextEffect definitions
- Similar to other asset definitions

---

### 2. Constants

**Current Location:** `Definitions/Constants/`

**Examples:**
- `Mods/core/Definitions/Constants/Player.json`
- `Mods/core/Definitions/Constants/Camera.json`
- `Mods/core/Definitions/Constants/Game.json`
- `Mods/core/Definitions/Constants/MessageBox.json`
- `Mods/core/Definitions/Constants/Popup.json`

**Nature:** Entity definition - game configuration/constants (player spawn, movement speed, camera settings)

**Proposed Location:** `Definitions/Entities/Constants/`

**Inferred Type:** `Constants` (or `GameConstants`)

**Rationale:**
- Defines game configuration/logic constants
- Not referencing external files
- Game data/logic, similar to Regions, Maps, etc.
- Could be considered a special entity type

**Alternative Consideration:** Could be `Definitions/Config/` or `Definitions/GameConfig/` if we want to separate configuration from game entities, but `Entities/Constants/` follows the pattern better.

---

### 3. TextEffects

**Current Location:** `Definitions/TextEffects/`

**Examples:**
- `Mods/core/Definitions/TextEffects/bouncy.json`
- `Mods/core/Definitions/TextEffects/wave.json`
- `Mods/core/Definitions/TextEffects/shake.json`

**Nature:** Asset definition - defines text effect parameters (animation, color cycling, etc.)

**Proposed Location:** `Definitions/Entities/Text/TextEffects/`

**Inferred Type:** `TextEffect`

**Rationale:**
- Pure data/configuration definition (effect parameters) - no external file references
- Similar to Constants (game configuration/logic)
- Grouped with ColorPalettes under `Text/` parent directory
- Similar to how Popups groups Backgrounds and Outlines under `UI/Popups/`
- Shows relationship between TextEffects and ColorPalettes

**Rationale:**
- Defines rendering/visual effect parameters
- Used by text rendering system
- Similar to other visual asset definitions
- May reference ColorPalette assets

---

### 4. Shaders

**Current Location:** `Definitions/Shaders/`

**Examples:**
- `Mods/test-shaders/Definitions/Shaders/Screen/pixelrain.json`
- `Mods/test-shaders/Definitions/Shaders/Screen/prismgrade.json`
- `Mods/test-shaders/Definitions/Shaders/Entity/ghost.json`

**Subcategories:**
- `Shaders/Screen/` - Screen-space shaders
- `Shaders/Entity/` - Entity shaders

**Nature:** Asset definition - references shader files (.mgfxo) and defines parameters

**Proposed Location:** `Definitions/Assets/Shaders/`

**Inferred Type:** `ShaderAsset`

**Rationale:**
- References external shader files (`.mgfxo`)
- Defines shader parameters
- Clear asset definition pattern

**Subcategory Handling:**
- `Definitions/Assets/Shaders/Screen/` → Type: `ShaderAsset` (subcategory: Screen)
- `Definitions/Assets/Shaders/Entity/` → Type: `ShaderAsset` (subcategory: Entity)
- Subcategories are informational only, type remains `ShaderAsset`

---

### 5. TileBehaviors

**Current Location:** Not present in current mods, but referenced in `core/mod.json` as `Definitions/TileBehaviors`

**Examples (from oldmonoball):**
- `oldmonoball/Mods/pokemon-emerald/Definitions/TileBehaviors/ice.json`
- `oldmonoball/Mods/pokemon-emerald/Definitions/TileBehaviors/impassable.json`

**Nature:** Behavior definition - defines tile interaction behaviors (collision, movement, effects)

**Proposed Location:** `Definitions/Behaviors/` (alongside NPC behaviors)

**Inferred Type:** `Behavior` (same as NPC behaviors)

**Rationale:**
- Similar to NPC behaviors
- Could be distinguished by subcategory: `Definitions/Behaviors/Tiles/` vs `Definitions/Behaviors/NPCs/`
- Or could be separate: `Definitions/TileBehaviors/` → Type: `TileBehavior`

**Recommendation:** Keep separate for clarity:
- `Definitions/TileBehaviors/` → Type: `TileBehavior`
- `Definitions/Behaviors/` → Type: `Behavior` (NPC behaviors)

---

## Updated Directory Convention

### Complete Structure with Missing Types

```
ModRoot/
├── Definitions/
│   ├── Assets/                          # Asset definitions (reference files/data)
│   │   ├── Audio/                       → Type: "AudioAsset"
│   │   ├── Battle/                      → Type: "BattleAsset"
│   │   ├── Characters/                  → Type: "CharacterAsset"
│   │   ├── DoorAnimations/              → Type: "DoorAnimationAsset"
│   │   ├── FieldEffects/                → Type: "FieldEffectAsset"
│   │   ├── Fonts/                       → Type: "FontAsset"
│   │   ├── Objects/                     → Type: "ObjectAsset"
│   │   ├── Pokemon/                     → Type: "PokemonAsset"
│   │   ├── Shaders/                     → Type: "ShaderAsset" ⭐ NEW
│   │   │   ├── Screen/                  → Type: "ShaderAsset" (subcategory)
│   │   │   └── Entity/                  → Type: "ShaderAsset" (subcategory)
│   │   ├── Sprites/                     → Type: "SpriteAsset"
│   │   ├── Tilesets/                    → Type: "TilesetAsset"
│   │   ├── UI/
│   │   │   ├── Interface/               → Type: "InterfaceAsset"
│   │   │   ├── Popups/
│   │   │   │   ├── Backgrounds/         → Type: "PopupBackgroundAsset"
│   │   │   │   └── Outlines/           → Type: "PopupOutlineAsset"
│   │   │   └── TextWindows/             → Type: "TextWindowAsset"
│   │   └── Weather/                     → Type: "WeatherAsset"
│   │
│   ├── Entities/                        # Entity definitions (game data/logic)
│   │   ├── BattleScenes/                → Type: "BattleScene"
│   │   ├── Constants/                   → Type: "Constants" ⭐ NEW
│   │   ├── Maps/                        → Type: "Map"
│   │   ├── MapSections/                 → Type: "MapSection"
│   │   ├── Pokemon/                      → Type: "Pokemon"
│   │   ├── PopupThemes/                 → Type: "PopupTheme"
│   │   ├── Regions/                      → Type: "Region"
│   │   ├── Text/                         ⭐ NEW
│   │   │   ├── ColorPalettes/           → Type: "ColorPalette" ⭐ NEW
│   │   │   └── TextEffects/             → Type: "TextEffect" ⭐ NEW
│   │   └── Weather/                     → Type: "Weather"
│   │
│   ├── Behaviors/                       → Type: "Behavior" (NPC behaviors)
│   ├── TileBehaviors/                   → Type: "TileBehavior" ⭐ NEW
│   └── Scripts/                          → Type: "Script"
│
├── Graphics/                            # Non-definition assets
├── Audio/                               # Non-definition assets
└── Scripts/                              # Script files (.csx)
```

---

## Updated Path Mappings

### Additions to KnownPathMappings

```csharp
private static readonly Dictionary<string, string> KnownPathMappings = new()
{
    // ... existing mappings ...
    
    // NEW: Missing asset types
    { "Definitions/Assets/Shaders", "ShaderAsset" },
    
    // NEW: Missing entity types
    { "Definitions/Entities/Constants", "Constants" },
    { "Definitions/Entities/Text/ColorPalettes", "ColorPalette" },
    { "Definitions/Entities/Text/TextEffects", "TextEffect" },
    
    // NEW: Missing behavior types
    { "Definitions/TileBehaviors", "TileBehavior" },
    
    // Legacy/flat structure support (for backward compatibility)
    { "Definitions/ColorPalettes", "ColorPalette" },
    { "Definitions/Constants", "Constants" },
    { "Definitions/TextEffects", "TextEffect" },
    { "Definitions/Shaders", "Shader" },
    { "Definitions/TileBehaviors", "TileBehavior" },
};
```

---

## Migration Path for Existing Mods

### core Mod

**Current Structure:**
```
Definitions/
├── ColorPalettes/
├── Constants/
├── Fonts/
└── TextEffects/
```

**Proposed Structure:**
```
Definitions/
├── Assets/
│   └── Fonts/
└── Entities/
    ├── Constants/
    └── Text/
        ├── ColorPalettes/
        └── TextEffects/
```

**Migration Steps:**
1. Move `Fonts/` → `Assets/Fonts/`
2. Move `Constants/` → `Entities/Constants/`
3. Create `Entities/Text/` directory
4. Move `ColorPalettes/` → `Entities/Text/ColorPalettes/`
5. Move `TextEffects/` → `Entities/Text/TextEffects/`

---

### pokemon-emerald Mod

**Current Structure:**
```
Definitions/
├── Audio/
├── BattleScenes/
├── Behaviors/
├── Maps/
├── Regions/
├── Scripts/
├── Sprites/
├── TextWindow/
└── Weather/
```

**Proposed Structure:**
```
Definitions/
├── Assets/
│   ├── Audio/
│   ├── Sprites/
│   └── UI/
│       └── TextWindows/  (from TextWindow/)
└── Entities/
    ├── BattleScenes/
    ├── Maps/
    ├── Regions/
    └── Weather/
├── Behaviors/
└── Scripts/
```

**Migration Steps:**
1. Move `Audio/` → `Assets/Audio/`
2. Move `Sprites/` → `Assets/Sprites/`
3. Move `TextWindow/` → `Assets/UI/TextWindows/`
4. Move `BattleScenes/` → `Entities/BattleScenes/`
5. Move `Maps/` → `Entities/Maps/`
6. Move `Regions/` → `Entities/Regions/`
7. Move `Weather/` → `Entities/Weather/`
8. Keep `Behaviors/` and `Scripts/` at top level

---

### test-shaders Mod

**Current Structure:**
```
Definitions/
└── Shaders/
    ├── Screen/
    └── Entity/
```

**Proposed Structure:**
```
Definitions/
└── Assets/
    └── Shaders/
        ├── Screen/
        └── Entity/
```

**Migration Steps:**
1. Move `Shaders/` → `Assets/Shaders/`

---

## Summary of Changes Needed

### Design Document Updates

1. **Add missing asset types:**
   - `ColorPaletteAsset` - `Definitions/Assets/ColorPalettes/`
   - `ShaderAsset` - `Definitions/Assets/Shaders/`
   - `TextEffectAsset` - `Definitions/Assets/TextEffects/`

2. **Add missing entity types:**
   - `Constants` - `Definitions/Entities/Constants/`

3. **Add missing behavior types:**
   - `TileBehavior` - `Definitions/TileBehaviors/`

4. **Update path mappings** in the design document

5. **Update examples** to include these types

6. **Add migration guide** for existing mods

---

## Recommendations

### 1. Constants Placement

**Decision:** Place in `Definitions/Entities/Constants/` → Type: `Constants`

**Reasoning:**
- Constants are game configuration/logic data
- Similar to other entity types (Regions, Maps)
- Not referencing external files
- Could be considered "game state entities"

### 2. TileBehaviors Placement

**Decision:** Keep separate from Behaviors → `Definitions/TileBehaviors/` → Type: `TileBehavior`

**Reasoning:**
- Tile behaviors are conceptually different from NPC behaviors
- Clear separation improves organization
- Matches existing mod.json structure

### 3. Subcategory Handling

**Decision:** Subcategories (like `Shaders/Screen/` and `Shaders/Entity/`) don't change the type

**Reasoning:**
- Type remains `ShaderAsset` regardless of subcategory
- Subcategories are informational/organizational only
- Keeps type inference simple

---

## Next Steps

1. Update `convention-based-definition-discovery-design.md` with:
   - Missing definition types
   - Updated path mappings
   - Updated examples
   - Migration guide

2. Create migration script/tool to help mod authors reorganize their mods

3. Update mod.json files to remove `contentFolders` after migration

4. Test convention-based discovery with all definition types
