# Map Popup Themes

This folder contains popup theme definitions for map location popups.

## Purpose

Themes define which background and outline assets to use for map popups. Each theme references specific popup graphics
that match a location type (towns, caves, underwater areas, etc.).

## Theme Files

Six theme files extracted from pokeemerald:

### wood.json

```json
{
  "id": "wood",
  "name": "Wood",
  "description": "Default wooden frame - used for towns, land routes, woods",
  "background": "wood",
  "outline": "wood_outline",
  "usageCount": 143
}
```

**Used for:**

- All towns (Littleroot, Oldale, Dewford, Lavaridge, Fallarbor, Verdanturf, Pacifidlog)
- Land routes (101-104, 110-121, 123)
- Woods and forests (Petalburg Woods)
- Safari Zone, Abandoned Ship, Southern Island

### marble.json

**Used for:** Major cities, modern facilities

- Cities: Slateport, Mauville, Rustboro, Lilycove, Sootopolis
- Modern: Battle Frontier, New Mauville, Trainer Hill, Dynamic

### stone.json

**Used for:** Caves, mountains, hideouts

- Caves: Granite Cave, Shoal Cave, Seafloor Cavern, Cave of Origin
- Mountains: Mt. Chimney, Mt. Pyre, Sky Pillar
- Hideouts: Aqua/Magma Hideout, Mirage Tower
- Victory Road, Meteor Falls, various ruins

### brick.json

**Used for:** Some cities

- Petalburg City, Fortree City, Mossdeep City, Ever Grande City

### underwater.json

**Used for:** Water routes

- Routes 105-109, 122, 124-134 (all ocean routes)

### stone2.json

**Used for:** Deep underwater areas

- Underwater 124, 125, 126, 127, 128, 129
- Underwater Sootopolis, Seafloor Cavern, Sealed Chamber, Marine Cave

## Theme Definition Format

```json
{
  "id": "theme_id",           // Unique identifier
  "name": "Display Name",     // Human-readable name
  "description": "...",       // Usage description
  "background": "bg_id",      // Background asset ID (from Popups/Backgrounds/)
  "outline": "outline_id",    // Outline asset ID (from Popups/Outlines/)
  "usageCount": 143           // Number of sections using this theme
}
```

## Usage in Code

### Loading a Theme

```csharp
// Load theme by ID
var theme = LoadJson($"Definitions/Maps/Themes/wood.json");
string backgroundId = theme["background"];  // "wood"
string outlineId = theme["outline"];        // "wood_outline"

// Get assets from popup registry
var background = popupRegistry.GetBackground(backgroundId);
var outline = popupRegistry.GetOutline(outlineId);
```

### Theme Selection Flow

```csharp
// 1. Get map's region section
string regionSection = map.RegionMapSection;  // "MAPSEC_LITTLEROOT_TOWN"

// 2. Look up section to get theme ID
var section = sectionRegistry.GetSection(regionSection);
string themeId = section.PopupTheme;  // "wood"

// 3. Load theme definition
var theme = themeRegistry.GetTheme(themeId);
string bgId = theme.Background;       // "wood"
string outlineId = theme.Outline;     // "wood_outline"

// 4. Load popup graphics
var background = popupRegistry.GetBackground(bgId);
var outline = popupRegistry.GetOutline(outlineId);

// 5. Display popup
ShowMapPopup(mapName, background, outline);
```

## Asset References

Theme files reference assets in these folders:

### Backgrounds

`../Popups/Backgrounds/`

- wood.json → `Graphics/Maps/Popups/Backgrounds/wood.png`
- marble.json → `Graphics/Maps/Popups/Backgrounds/marble.png`
- stone.json → `Graphics/Maps/Popups/Backgrounds/stone.png`
- brick.json → `Graphics/Maps/Popups/Backgrounds/brick.png`
- underwater.json → `Graphics/Maps/Popups/Backgrounds/underwater.png`
- stone2.json → `Graphics/Maps/Popups/Backgrounds/stone2.png`

### Outlines

`../Popups/Outlines/`

- wood_outline.json → `Graphics/Maps/Popups/Outlines/wood_outline.png`
- marble_outline.json → `Graphics/Maps/Popups/Outlines/marble_outline.png`
- stone_outline.json → `Graphics/Maps/Popups/Outlines/stone_outline.png`
- brick_outline.json → `Graphics/Maps/Popups/Outlines/brick_outline.png`
- underwater_outline.json → `Graphics/Maps/Popups/Outlines/underwater_outline.png`
- stone2_outline.json → `Graphics/Maps/Popups/Outlines/stone2_outline.png`

## Regenerating Themes

Themes are automatically regenerated when extracting sections:

```bash
cd porycon
python -m porycon --input /path/to/pokeemerald --output /path/to/PokeSharp/MonoBallFramework.Game/Assets --extract-sections
```

The extractor:

1. Parses `pokeemerald/src/map_name_popup.c` for theme assignments
2. Counts usage per theme across all sections
3. Generates theme definition files with usage statistics

## Theme Mapping (from pokeemerald)

Based on `pokeemerald/src/map_name_popup.c`:

| Theme      | Sections | Description                         |
|------------|----------|-------------------------------------|
| wood       | 143      | Default - towns, land routes, woods |
| stone      | 29       | Caves, mountains, hideouts          |
| underwater | 17       | Ocean/water routes                  |
| stone2     | 11       | Deep underwater areas               |
| marble     | 9        | Major cities, modern facilities     |
| brick      | 4        | Some cities                         |

**Total: 213 map sections**

