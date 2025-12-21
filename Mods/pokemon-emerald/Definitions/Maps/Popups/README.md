# Map Popup Manifests

This folder contains manifest files for map popup graphics extracted from pokeemerald.

## Structure

```
Popups/
├── Backgrounds/
│   ├── wood.json           # Background bitmap manifests
│   ├── stone.json
│   └── ...
└── Outlines/
    ├── wood_outline.json   # Outline tile sheet manifests
    ├── stone_outline.json
    └── ...
```

## Background Manifests

Background files are **bitmap images** that fill the interior of the popup.

### Format

```json
{
  "Id": "wood",
  "DisplayName": "Wood",
  "Type": "Bitmap",
  "TexturePath": "Graphics/Maps/Popups/Backgrounds/wood.png",
  "Width": 80,
  "Height": 24,
  "Description": "Background bitmap for map popup"
}
```

### Fields

- **Id**: Unique identifier
- **DisplayName**: Human-readable name
- **Type**: Always "Bitmap" for backgrounds
- **TexturePath**: Path to the texture file (relative to Assets/)
- **Width/Height**: Dimensions in pixels (80×24 for all pokeemerald popups)

## Outline Manifests

Outline files are **tile sheets** containing individual 8×8 pixel tiles used to construct the popup frame.

### Format

```json
{
  "Id": "wood_outline",
  "DisplayName": "Wood Outline",
  "Type": "TileSheet",
  "TexturePath": "Graphics/Maps/Popups/Outlines/wood_outline.png",
  "TileWidth": 8,
  "TileHeight": 8,
  "TileCount": 30,
  "Tiles": [
    { "Index": 0, "X": 0, "Y": 0, "Width": 8, "Height": 8 },
    { "Index": 1, "X": 8, "Y": 0, "Width": 8, "Height": 8 },
    ...
  ],
  "TileUsage": {
    "TopEdge": [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11],
    "LeftTopCorner": 12,
    "RightTopCorner": 13,
    "LeftMiddle": 14,
    "RightMiddle": 15,
    "LeftBottomCorner": 16,
    "RightBottomCorner": 17,
    "BottomEdge": [18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29]
  },
  "Description": "9-patch frame tile sheet for map popup (GBA tile-based rendering)"
}
```

### Fields

- **Id**: Unique identifier
- **DisplayName**: Human-readable name
- **Type**: Always "TileSheet" for outlines
- **TexturePath**: Path to the tile sheet texture
- **TileWidth/TileHeight**: Size of each tile (8×8 for GBA)
- **TileCount**: Total number of tiles (30 for pokeemerald popups)
- **Tiles**: Array of tile definitions with positions
    - **Index**: Tile number (0-29)
    - **X/Y**: Position in the sprite sheet
    - **Width/Height**: Size of this tile (8×8)
- **TileUsage**: Maps tile indices to their purpose in the frame
    - **TopEdge**: Tiles for top border (12 tiles)
    - **LeftTopCorner/RightTopCorner**: Corner tiles
    - **LeftMiddle/RightMiddle**: Vertical edge tiles
    - **LeftBottomCorner/RightBottomCorner**: Corner tiles
    - **BottomEdge**: Tiles for bottom border (12 tiles)

## Tile Sheet Layout

Outline tile sheets are 80×24 pixels (10 tiles wide × 3 tiles tall):

```
Row 0: Tiles  0- 9
Row 1: Tiles 10-19
Row 2: Tiles 20-29
```

### Tile Reading Order

Tiles are read **left-to-right, top-to-bottom**:

```
┌─────┬─────┬─────┬─────┬─────┬─────┬─────┬─────┬─────┬─────┐
│  0  │  1  │  2  │  3  │  4  │  5  │  6  │  7  │  8  │  9  │ Row 0
├─────┼─────┼─────┼─────┼─────┼─────┼─────┼─────┼─────┼─────┤
│ 10  │ 11  │ 12  │ 13  │ 14  │ 15  │ 16  │ 17  │ 18  │ 19  │ Row 1
├─────┼─────┼─────┼─────┼─────┼─────┼─────┼─────┼─────┼─────┤
│ 20  │ 21  │ 22  │ 23  │ 24  │ 25  │ 26  │ 27  │ 28  │ 29  │ Row 2
└─────┴─────┴─────┴─────┴─────┴─────┴─────┴─────┴─────┴─────┘
```

### Frame Assembly

The game assembles these tiles to create a popup frame:

1. **Top edge**: Tiles 0-11 (repeated as needed)
2. **Corners**: Tiles 12, 13 (top), 16, 17 (bottom)
3. **Sides**: Tiles 14, 15 (repeated for height)
4. **Bottom edge**: Tiles 18-29 (repeated as needed)

## GBA Rendering Notes

These tile sheets match pokeemerald's GBA tile-based rendering:

- **Palette index 0**: Transparent (converted to alpha in RGBA PNG)
- **8×8 tiles**: Standard GBA tile size
- **30 tiles total**: 960 bytes in .4bpp format (4 bits per pixel)
- **Assembly at runtime**: Game places individual tiles to build the frame

This differs from modern 9-slice sprites which use continuous image regions. The tile-based approach was necessary for
GBA's hardware limitations.

## Usage in PokeSharp

To render a map popup:

1. Load background bitmap and stretch to popup size
2. Load outline tile sheet
3. Use TileUsage to select appropriate tiles for each frame section
4. Place tiles at 8-pixel intervals to build the border

See `MonoBallFramework.Game/Engine/Rendering/Popups/` for implementation details.



