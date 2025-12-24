# Porycon - Pokemon Emerald to Tiled Converter

A Python tool to convert Pokemon Emerald decompilation maps to Tiled JSON format, replacing metatiles with individual tile layers for easier editing.

## Features

- Converts pokeemerald map.json files to Tiled format
- Splits metatiles into individual tiles across separate BG layers
- Creates complete tilesets (no metatiles) for Tiled editing
- Generates Tiled world files from map connections
- **Tile animations**: Converts automatic and trigger-based animations (see [Animation Guide](docs/animations.md))
- **Map popup graphics**: Extracts and processes region map popup backgrounds and outlines
- Organizes output: Maps in region folders, Worlds at root, Tilesets in region folders

## Project Structure

```
porycon/
├── porycon/
│   ├── __init__.py
│   ├── converter.py          # Main conversion logic
│   ├── metatile.py          # Metatile to tile conversion
│   ├── tileset_builder.py   # Complete tileset generation
│   ├── world_builder.py     # World file generation
│   ├── popup_extractor.py   # Map popup graphics extractor
│   └── utils.py             # Utility functions
├── tests/
├── requirements.txt
└── README.md
```

## Installation

```bash
cd porycon
pip install -e .
```

Or install dependencies directly:
```bash
pip install -r requirements.txt
```

## Usage

### Convert Maps

Convert all maps:
```bash
python -m porycon --input /path/to/pokeemerald --output /path/to/output
```

Convert specific region:
```bash
python -m porycon --input /path/to/pokeemerald --output /path/to/output --region hoenn
```

### Extract Map Popup Graphics

Extract popup backgrounds and outlines from pokeemerald:
```bash
python -m porycon --input /path/to/pokeemerald --output /path/to/PokeSharp --extract-popups
```

This will:
- Find all popup graphics in `pokeemerald/graphics/map_popup/`
- Copy backgrounds to `MonoBallFramework.Game/Assets/Graphics/Maps/Popups/Backgrounds/`
- Copy outlines to `MonoBallFramework.Game/Assets/Graphics/Maps/Popups/Outlines/`
- Convert outline tile sheets with palette transparency

### Extract Map Section Definitions

Extract map sections (MAPSEC) and popup theme mappings:
```bash
python -m porycon --input /path/to/pokeemerald --output /path/to/PokeSharp/MonoBallFramework.Game/Assets --extract-sections
```

This will:
- Parse `pokeemerald/src/data/region_map/region_map_sections.json` for section definitions
- Parse `pokeemerald/src/map_name_popup.c` for popup theme mappings
- Generate individual section JSON files in `MonoBallFramework.Game/Assets/Definitions/Maps/Sections/`
- Create `section_registry.json` (master list of all sections)
- Create `theme_summary.json` (theme usage statistics)
- Copy and process outlines to `MonoBallFramework.Game/Assets/Graphics/Maps/Popups/Outlines/`
- **Apply transparency** to outline sprite sheets for 9-slice rendering
- Create JSON definition files in `Assets/Definitions/Maps/Popups/`

**Example:**
```bash
python -m porycon --input C:/pokeemerald --output C:/Users/nate0/RiderProjects/PokeSharp --extract-popups
```

### Output Structure

The converter creates the following structure:

```
output/
├── Maps/
│   └── hoenn/
│       ├── mauvillecity.json
│       ├── littleroottown.json
│       └── ...
├── Worlds/
│   ├── hoenn.world
│   └── ...
├── Tilesets/
│   └── hoenn/
│       ├── general.json
│       ├── general.png
│       ├── mauville.json
│       ├── mauville.png
│       └── ...
└── MonoBallFramework.Game/
    └── Assets/
        ├── Graphics/Maps/Popups/
        │   ├── Backgrounds/
        │   │   ├── wood.png
        │   │   └── ...
        │   └── Outlines/
        │       ├── wood_outline.png  (transparency applied)
        │       └── ...
        └── Definitions/Maps/Popups/
            ├── Backgrounds/
            │   ├── wood.json
            │   └── ...
            └── Outlines/
                ├── wood_outline.json
                └── ...
```

### How It Works

1. **Metatile Conversion**: Each 2x2 metatile (8 tiles) is split into individual tiles
2. **Layer Distribution**: Tiles are distributed across 3 BG layers based on metatile layer type:
   - **NORMAL**: Bottom tiles → Objects layer, Top tiles → Overhead layer
   - **COVERED**: Bottom tiles → Ground layer, Top tiles → Objects layer
   - **SPLIT**: Bottom tiles → Ground layer, Top tiles → Overhead layer
3. **Tileset Building**: Complete tilesets are created containing only tiles actually used in maps
4. **World Files**: Tiled world files are generated from map connections
5. **Popup Graphics**: Backgrounds and outlines are extracted, with outlines getting 9-slice transparency processing

### Map Popup Graphics

Popup graphics are processed specially:

- **Backgrounds**: Simple textures, copied as-is
- **Outlines**: Sprite sheets for 9-slice rendering
  - Center region made fully transparent (so background shows through)
  - White/background colors removed from border regions
  - Corners and edges preserved for pixel-perfect rendering

This ensures popups render correctly with proper transparency and no distortion.

## Requirements

- Python 3.8+
- Pillow (for image processing)
- See requirements.txt for full dependencies

## Documentation

- **[Animation Guide](docs/animations.md)** - Complete guide to tile animations (automatic & trigger-based)
- **Project Structure** - See directory layout above

## Notes

- The converter creates tilesets with only used tiles (not all tiles from source)
- Tile IDs are remapped to be sequential (1-based for Tiled)
- Maps reference tilesets via relative paths
- World files use a simple grid layout (can be improved with graph algorithms)
- Animation support includes water, flowers, waterfalls, and more (see Animation Guide)
- Popup outlines are automatically processed with transparency for 9-slice rendering

## Command Line Options

### Map Conversion
- `--input <path>`: Input directory (pokeemerald root) [required]
- `--output <path>`: Output directory for Tiled files [required]
- `--region <name>`: Region name for organizing output folders
- `--extract-popups`: Extract map popup graphics instead of converting maps
- `--extract-sections`: Extract map section definitions and popup theme mappings
- `--extract-text-windows`: Extract text window graphics

### Audio Extraction
- `--extract-audio`: Extract and convert MIDI audio to OGG format
- `--list-audio`: List all audio tracks without converting
- `--audio-music`: Include music tracks (default: True)
- `--audio-sfx`: Include sound effects (default: True)
- `--audio-phonemes`: Include phoneme tracks (default: False)
- `--soundfont <path>`: Path to soundfont file for MIDI conversion

### General
- `--verbose, -v`: Show detailed progress information
- `--debug, -d`: Show debug information (implies verbose)

## Audio Extraction

Extract and convert audio from pokeemerald MIDI files to OGG format:

```bash
python -m porycon --input /path/to/pokeemerald \
    --output /path/to/PokeSharp/MonoBallFramework.Game/Assets \
    --extract-audio
```

### Prerequisites for MIDI Conversion

MIDI to OGG conversion requires one of the following tools:
- **TiMidity++** (recommended): `sudo apt install timidity ffmpeg`
- **FluidSynth** (with soundfont): `sudo apt install fluidsynth ffmpeg`
- **FFmpeg** (limited MIDI support)

For best quality with a GBA-style soundfont:
```bash
python -m porycon --input /path/to/pokeemerald \
    --output /path/to/PokeSharp/MonoBallFramework.Game/Assets \
    --extract-audio --soundfont /path/to/soundfont.sf2
```

### List Audio Tracks

View all available audio tracks before extracting:
```bash
python -m porycon --input /path/to/pokeemerald --output . --list-audio -v
```

### Output Structure

Audio extraction creates the following structure in the output directory:
```
output/
├── Audio/
│   ├── Music/
│   │   ├── Battle/      # Battle and encounter music
│   │   ├── Fanfares/    # Short victory/obtain jingles
│   │   ├── Routes/      # Route and cycling music
│   │   ├── Special/     # Gyms, caves, special areas
│   │   └── Towns/       # Town and city themes
│   └── SFX/
│       ├── Battle/      # Battle sound effects
│       ├── Environment/ # Weather, ambient sounds
│       ├── UI/          # Menu, selection sounds
│       └── Phonemes/    # Bard singing phonemes
└── Definitions/Audio/
    ├── audio_index.json      # Master index of all tracks
    ├── music_battle.json     # Battle music definitions
    ├── music_towns.json      # Town music definitions
    └── ...
```

**Example:** To extract audio to PokeSharp's Assets folder:
```bash
python -m porycon --input /path/to/pokeemerald \
    --output /path/to/PokeSharp/MonoBallFramework.Game/Assets \
    --extract-audio
```

### Audio Definition Format

Each track definition includes:
```json
{
  "id": "mus_littleroot",
  "name": "Littleroot",
  "filename": "Music/Towns/mus_littleroot.ogg",
  "category": "Music/Towns",
  "volume": 0.787,
  "loop": true,
  "fade_in": 0.5,
  "fade_out": 0.5,
  "tags": ["music", "emerald", "ruby", "sapphire"]
}
```

### Track Categories

| Category | Description | Example Tracks |
|----------|-------------|----------------|
| Music/Battle | Battle themes, encounters | mus_vs_wild, mus_vs_trainer |
| Music/Towns | Town/city themes | mus_littleroot, mus_lilycove |
| Music/Routes | Routes, cycling, surfing | mus_route101, mus_cycling |
| Music/Special | Gyms, caves, special areas | mus_gym, mus_cave_of_origin |
| Music/Fanfares | Short jingles | mus_heal, mus_caught |
| SFX/Battle | Battle effects | se_ball_throw, se_effective |
| SFX/UI | Menu sounds | se_select, se_door |
| SFX/Environment | Ambient effects | se_rain, se_thunder |
