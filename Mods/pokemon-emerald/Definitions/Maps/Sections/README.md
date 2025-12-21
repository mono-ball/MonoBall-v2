# Map Sections

This folder contains map section (MAPSEC) definitions extracted from pokeemerald.

## Purpose

Map sections define:

1. **Region Map Areas**: Where each map appears on the region map (x, y, width, height)
2. **Popup Theme Reference**: Which popup theme to use for each map section

## Files

### sections.json

Contains all MAPSEC definitions in a single file:

```json
{
  "sections": [
    {
      "id": "MAPSEC_LITTLEROOT_TOWN",
      "name": "LITTLEROOT TOWN",
      "x": 4,
      "y": 11,
      "width": 1,
      "height": 1,
      "popupTheme": "wood"
    },
    ...
  ],
  "totalSections": 213
}
```

**Fields:**

- `id`: Unique MAPSEC identifier (e.g., `"MAPSEC_LITTLEROOT_TOWN"`)
- `name`: Display name for the region map
- `x`, `y`: Grid position on region map (8x8 pixel tiles)
- `width`, `height`: Size on region map (in tiles)
- `popupTheme`: Theme ID reference (e.g., `"wood"`, `"marble"`, `"stone"`)

### section_registry.json

Quick lookup registry organized by theme:

```json
{
  "sections": [
    { "id": "MAPSEC_LITTLEROOT_TOWN", "name": "LITTLEROOT TOWN", "theme": "wood" },
    ...
  ],
  "themes": {
    "wood": ["MAPSEC_LITTLEROOT_TOWN", "MAPSEC_ROUTE_101", ...],
    "marble": ["MAPSEC_SLATEPORT_CITY", ...],
    ...
  },
  "totalSections": 213
}
```

## Themes

Popup themes are defined in `../Themes/` (6 theme files total):

- **wood.json** - Default wooden frame (143 sections)
- **marble.json** - Marble frame for major cities (9 sections)
- **stone.json** - Stone frame for caves/dungeons (29 sections)
- **brick.json** - Brick frame for some cities (4 sections)
- **underwater.json** - Underwater frame for water routes (17 sections)
- **stone2.json** - Stone variant 2 for deep underwater (11 sections)

Each theme file defines which background and outline assets to use:

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

## Usage in Code

### Loading Section Data

```csharp
// Load all sections
var sectionsData = LoadJson("Definitions/Maps/Sections/sections.json");
var sections = sectionsData["sections"];

// Find a specific section
var littlerootSection = sections.FirstOrDefault(s => s["id"] == "MAPSEC_LITTLEROOT_TOWN");
string themeId = littlerootSection["popupTheme"];  // "wood"
```

### Mapping Map to Popup Theme

```csharp
// 1. Get map's region section from map data
string regionSection = mapData.RegionMapSection;  // e.g., "MAPSEC_LITTLEROOT_TOWN"

// 2. Look up section to get theme ID
var section = FindSection(regionSection);
string themeId = section["popupTheme"];  // "wood"

// 3. Load theme definition
var theme = LoadJson($"Definitions/Maps/Themes/{themeId}.json");
string bgId = theme["background"];       // "wood"
string outlineId = theme["outline"];     // "wood_outline"

// 4. Load popup assets from registry
var background = popupRegistry.GetBackground(bgId);
var outline = popupRegistry.GetOutline(outlineId);
```

## Regenerating Data

To regenerate from pokeemerald:

```bash
cd porycon
python -m porycon --input /path/to/pokeemerald --output /path/to/PokeSharp/MonoBallFramework.Game/Assets --extract-sections
```

This will:

1. Parse `pokeemerald/src/data/region_map/region_map_sections.json` for section definitions
2. Parse `pokeemerald/src/map_name_popup.c` for theme mappings
3. Generate `sections.json` with all MAPSEC definitions
4. Generate `section_registry.json` for quick lookups
5. Generate 6 theme files in `../Themes/`

## Integration with Map Popups

When a map transition occurs:

1. Get the map's `regionMapSection` property (e.g., `"MAPSEC_LITTLEROOT_TOWN"`)
2. Look up the section in `sections.json` to get `popupTheme` (e.g., `"wood"`)
3. Load the theme from `../Themes/wood.json` to get `background` and `outline` IDs
4. Load the corresponding assets from `PopupRegistry`
5. Display the popup with pokeemerald-accurate themed graphics

This creates the authentic pokeemerald experience where different areas have different popup styles!
