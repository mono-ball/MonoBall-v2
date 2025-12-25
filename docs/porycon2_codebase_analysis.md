# Porycon2 Codebase Analysis Report

**Analysis Date**: December 24, 2025
**Analyzer**: Research Agent
**Project**: Pokemon Emerald to Tiled/PokeSharp Converter

---

## Executive Summary

Porycon2 is a Python-based conversion tool that transforms Pokemon Emerald decompilation data (maps, tilesets, sprites, audio) into formats suitable for the PokeSharp game engine. The tool converts GBA binary formats into Tiled JSON and custom PokeSharp Definition formats, handling complex transformations like metatile decomposition, palette management, and animation extraction.

**Key Statistics**:
- **11,575 total lines of Python code** across 25 source files
- **0 test files** (major gap)
- **Minimal dependencies**: Only Pillow for image processing
- **Parallel processing**: Uses multiprocessing for map conversion (2.8-4.4x speed improvement claimed)
- **Dual output modes**: Tiled JSON or PokeSharp entity format

---

## 1. Architecture Overview

### 1.1 Core Design Pattern

Porycon2 follows a **pipeline architecture** with specialized converters:

```
Input (pokeemerald) → Readers → Processors → Renderers → Converters → Output (Tiled/PokeSharp)
```

**Key Architectural Components**:

1. **Readers** - Binary file parsing (map.bin, metatiles.bin, attributes.bin)
2. **Processors** - Data transformation (metatile splitting, tile distribution)
3. **Renderers** - Image generation (tile rendering with palettes)
4. **Converters** - Format conversion (Tiled JSON, PokeSharp Definitions)
5. **Builders** - Asset construction (tilesets, worlds)

### 1.2 Module Breakdown

| Module | LOC | Purpose | Issues |
|--------|-----|---------|---------|
| `converter.py` | 2,036 | Main map conversion logic | Very large, needs splitting |
| `definition_converter.py` | 1,073 | Tiled → PokeSharp conversion | Complex, undocumented |
| `audio_converter.py` | 960 | MIDI → OGG conversion | External tool dependencies |
| `animation_scanner.py` | 804 | Tile animation extraction | Hardcoded mappings |
| `id_transformer.py` | 828 | ID transformations | Purpose unclear |
| `sprite_extractor.py` | 794 | NPC sprite extraction | Separate entry point |
| `__main__.py` | 735 | CLI entry point | Too many responsibilities |
| `tileset_builder.py` | 527 | Tileset construction | Palette complexity |
| `map_reader.py` | 173 | Binary file reading | Good separation |
| `utils.py` | 324 | Utility functions | Well-organized |

---

## 2. What Porycon2 Does

### 2.1 Primary Functions

#### A. Map Conversion (Core Feature)
Converts pokeemerald `map.json` + `map.bin` → Tiled JSON format:

1. **Metatile Decomposition**:
   - GBA uses 16x16px metatiles (2x2 grid of 8x8 tiles)
   - Porycon splits these into individual 8x8 tiles
   - Distributes across 3 background layers (BG1/BG2/BG3) based on layer type

2. **Layer Type System**:
   ```python
   NORMAL:  Bottom → BG2, Top → BG1  (most common)
   COVERED: Bottom → BG3, Top → BG2  (bridges, furniture)
   SPLIT:   Bottom → BG3, Top → BG1  (special cases)
   ```

3. **Palette Management**:
   - 16 palettes per tileset (0-15)
   - Palettes 0-5: Primary tileset
   - Palettes 6-12: Secondary tileset
   - Tracks (tile_id, palette_index) pairs for uniqueness

4. **Map Features Extracted**:
   - Terrain layers (Ground/Objects/Overhead)
   - Warp events (with destination lookup)
   - NPC object events (with movement behaviors)
   - Triggers (script triggers, weather zones)
   - Collisions (from metatile attributes)
   - Borders (map edge tiles)

#### B. Tileset Building
Constructs complete tilesets from used tiles:

- Scans all maps to find used tiles
- Builds tileset images with only used tiles (optimization)
- Generates Tiled tileset JSON with animations
- Handles tile ID remapping (old_id → new_id)
- Creates per-map tilesets for parallelization

#### C. Animation System
Two types of animations:

1. **Automatic (Time-based)**:
   - Water ripples, waterfalls, flowers, lava
   - Hardcoded mappings in `animation_scanner.py`:
     ```python
     "general": {
       "water": {"base_tile_id": 432, "num_tiles": 30, "duration_ms": 200},
       "flower": {"base_tile_id": 228, "num_tiles": 4, "duration_ms": 200},
     }
     ```
   - Converts to Tiled animation frames

2. **Trigger (Event-based)**:
   - TVs, doors, objects with state changes
   - Stored as object properties, not automatic

#### D. Audio Extraction
MIDI → OGG conversion with loop support:

- Parses `midi.cfg` for track definitions
- Extracts loop markers (`[` and `]` MIDI markers)
- Converts using TiMidity++/FluidSynth
- Generates audio definition JSON files
- Categorizes: Music (Battle/Towns/Routes), SFX, Fanfares, Phonemes

#### E. Additional Extractors

1. **Map Popups**: Region map popup backgrounds/outlines
2. **Map Sections**: MAPSEC definitions, popup themes
3. **Text Windows**: Text window graphics
4. **Sprites**: NPC overworld sprites (separate tool)

### 2.2 Dual Output Modes

#### Mode 1: Tiled JSON (--tiled)
- Standard Tiled map editor format
- For manual editing in Tiled
- Includes world files for map connections

#### Mode 2: PokeSharp Definitions (default)
- Custom EF Core entity format
- For direct use in PokeSharp engine
- Structured as Definition DTOs
- Cleans up intermediate Tiled files after conversion

---

## 3. Technical Deep Dive

### 3.1 Metatile Processing

**Problem**: GBA uses 16x16 metatiles, Tiled uses 8x8 tiles.

**Solution**:
```python
# Each metatile = 8 tiles (2x2 bottom layer + 2x2 top layer)
# Bottom tiles (0-3): One layer based on type
# Top tiles (4-7): Another layer based on type

def convert_metatile_to_tile_layers(metatile_id, layer_type):
    tiles = unpack_metatile(metatile_id)  # 8 tiles
    bottom = tiles[0:4]  # First 4 tiles
    top = tiles[4:8]     # Last 4 tiles

    if layer_type == NORMAL:
        return ([], bottom, top)  # (BG3, BG2, BG1)
    elif layer_type == COVERED:
        return (bottom, top, [])
    elif layer_type == SPLIT:
        return (bottom, [], top)
```

### 3.2 Palette System

**Challenge**: Tiles reference palettes by index, not embedded colors.

**Implementation**:
1. Load `.pal` files (16 colors each, RGB555 format)
2. For each tile, apply correct palette based on palette_index
3. Track (tile_id, palette_index) as unique combination
4. Primary tilesets use palettes 0-5, secondary use 6-12

**Code Path**:
```
palette_loader.py:load_tileset_palettes()
  → tileset_builder.py:apply_palette_to_tile()
    → converter.py:process_metatiles()
```

### 3.3 Parallel Processing

**Strategy**: ProcessPoolExecutor for CPU-bound tasks, ThreadPoolExecutor for I/O

```python
# Map conversion (CPU-bound)
with ProcessPoolExecutor(max_workers=cpu_count()-1) as executor:
    futures = [executor.submit(convert_single_map, task) for task in tasks]

# Tileset building (I/O-bound)
with ThreadPoolExecutor(max_workers=4) as executor:
    futures = [executor.submit(create_tileset, name) for name in tilesets]
```

**Performance**:
- Claims 2.8-4.4x speed improvement
- No benchmarks provided in codebase
- Race condition prevention: Sequential result collection

### 3.4 Warp System

**Problem**: Warps reference destination map + warp index, need actual coordinates.

**Solution**: Pre-build warp lookup table
```python
warp_lookup[(dest_map_id, warp_index)] = (x, y, elevation)
```

Then during conversion:
```python
warp_dest = warp_lookup.get((dest_map, dest_warp), None)
if warp_dest:
    x, y, elevation = warp_dest
```

### 3.5 Movement Behaviors

**Mapping**: 130+ pokeemerald movement types → PokeSharp behaviors

```python
MOVEMENT_TYPE_TO_BEHAVIOR = {
    "MOVEMENT_TYPE_WANDER_AROUND": ("wander", {}),
    "MOVEMENT_TYPE_FACE_DOWN": ("stationary", {"direction": "down"}),
    "MOVEMENT_TYPE_COPY_PLAYER": ("copy_player", {"mode": "normal"}),
    # ... 127 more mappings
}
```

Each behavior includes:
- Behavior ID (references Definition file)
- Default parameters (can be overridden)

---

## 4. Current Issues & Pain Points

### 4.1 Code Quality Issues

#### A. No Tests
- **0 test files** in entire codebase
- No unit tests for core conversion logic
- No integration tests for end-to-end conversion
- No fixtures or test data

**Impact**: High risk of regressions, difficult to refactor

#### B. Large Monolithic Files
- `converter.py`: 2,036 lines (should be split)
- `__main__.py`: 735 lines of CLI logic (too complex)
- Many files exceed 500 lines (violates stated best practices)

#### C. Hardcoded Data
- Animation mappings in `animation_scanner.py` (should be config)
- Movement type mappings in `constants.py` (could be JSON)
- Audio categorization hardcoded (should be data-driven)

**Example**:
```python
# Hardcoded in animation_scanner.py
ANIMATION_MAPPINGS = {
    "general": {
        "water": {"base_tile_id": 432, "num_tiles": 30},
        # ... more hardcoded values
    }
}
```

#### D. Inconsistent Error Handling
- Some functions silently fail and return None
- Others log warnings
- Some raise exceptions
- No unified error handling strategy

#### E. Documentation Gaps
- Many functions lack docstrings
- Complex algorithms not explained (e.g., firstgid calculation)
- No architectural documentation
- README is comprehensive but code comments are sparse

### 4.2 Architectural Issues

#### A. Tight Coupling
- `MapConverter` directly instantiates many dependencies
- No dependency injection
- Hard to unit test in isolation

```python
class MapConverter:
    def __init__(self, input_dir, output_dir):
        self.map_reader = MapReader(input_dir)  # Tight coupling
        self.tileset_builder = TilesetBuilder(input_dir)
        self.metatile_renderer = MetatileRenderer(input_dir)
        # ... more direct instantiation
```

#### B. God Object Pattern
- `MapConverter` does too much:
  - Reads binary files
  - Processes metatiles
  - Renders images
  - Converts formats
  - Saves outputs

**Better**: Split into separate classes with single responsibilities

#### C. Mixed Concerns
- CLI logic mixed with conversion logic in `__main__.py`
- Business logic mixed with I/O in many files
- Parallel processing logic scattered across files

#### D. Unclear Module Boundaries
- `id_transformer.py` - Purpose not clear from name
- `entity_converter.py` vs `definition_converter.py` - Overlap?
- `map_worker.py` - Just a wrapper for parallelization

### 4.3 Dependency Management

#### A. External Tool Dependencies (Audio)
Requires system tools not in requirements.txt:
- TiMidity++ (MIDI synthesis)
- FluidSynth (alternative MIDI synthesis)
- FFmpeg (audio conversion)

**Issue**: Installation instructions incomplete, fragile

#### B. Minimal Python Dependencies
```
Pillow>=10.0.0
pathlib2>=2.3.7; python_version < '3.4'
```

**Good**: Lightweight
**Bad**: No validation, no version pinning for reproducibility

### 4.4 Data Quality Issues

#### A. Incomplete Validation
- No validation of pokeemerald structure
- Assumes files exist without checking
- No schema validation for JSON files

#### B. Silent Failures
```python
if not map_bin_path.exists():
    logger.warning(f"map.bin not found")
    return None  # Silent failure, hard to debug
```

#### C. Race Conditions (Potential)
- Shared state in parallel processing (mitigated but risky):
  ```python
  all_used_tiles = {}  # Shared across processes
  # Updates are sequential, but design is fragile
  ```

### 4.5 Performance Issues

#### A. Memory Usage
- Loads entire tilesets into memory
- No streaming for large files
- Could exhaust memory on large projects

#### B. Redundant I/O
- Reads same files multiple times (layouts, tilesets)
- No caching of parsed data
- Could use in-memory cache

#### C. Inefficient Palette Lookups
```python
# O(n) search for palette, done repeatedly
for i, p in enumerate(palettes):
    if p and palette_index == i:
        palette_to_use = p
```

### 4.6 Maintainability Issues

#### A. Magic Numbers
Despite having `constants.py`, magic numbers still appear:
```python
if black_count > total_pixels * 0.5:  # What is 0.5?
if skipped_other <= 3:  # Why 3?
cols = min(tiles_per_row, num_tiles)  # tiles_per_row=16, magic
```

#### B. Dead Code Suspicions
- `entity_converter.py` (408 lines) - Used?
- Multiple tileset building paths - Confusing
- Commented-out code blocks

#### C. Inconsistent Naming
- `map_bin` vs `mapBin` (snake_case vs camelCase)
- `tileset_name` vs `tilesetName`
- Not PEP 8 compliant throughout

---

## 5. Dependencies Analysis

### 5.1 Python Dependencies

```
Pillow>=10.0.0           # Image processing (PNG read/write)
pathlib2>=2.3.7          # Python 2.x compat (unnecessary for 3.8+)
```

**Assessment**:
- ✅ Minimal dependencies (good)
- ❌ No version upper bounds (could break)
- ❌ pathlib2 unnecessary for Python 3.8+
- ❌ Missing: pytest, mypy, black (dev tools)

### 5.2 System Dependencies (Audio Conversion)

**Required** (one of):
- TiMidity++ + FFmpeg
- FluidSynth + FFmpeg

**Optional**:
- Soundfont file (.sf2) for better MIDI quality

**Issues**:
- Not in requirements.txt
- Installation platform-specific
- No fallback for missing tools
- subprocess calls fragile

### 5.3 Input Dependencies (pokeemerald)

**Required Structure**:
```
pokeemerald/
├── data/
│   ├── maps/
│   │   └── {MapName}/
│   │       └── map.json
│   ├── layouts/
│   │   └── layouts.json
│   └── tilesets/
│       ├── primary/{tileset}/
│       │   ├── tiles.png
│       │   ├── metatiles.bin
│       │   ├── metatile_attributes.bin
│       │   └── palettes/*.pal
│       └── secondary/{tileset}/
│           └── ...
├── graphics/
│   ├── object_events/sprites/
│   ├── map_popup/
│   └── ...
└── sound/
    └── songs/*.mid
```

**Fragility**: Hardcoded paths, no version detection

---

## 6. Key Features & Functionality

### 6.1 Strengths

1. **Comprehensive Coverage**:
   - Maps, tilesets, animations, audio, sprites
   - Handles 99% of pokeemerald assets

2. **Dual Output Modes**:
   - Tiled JSON for manual editing
   - PokeSharp Definitions for engine integration

3. **Parallelization**:
   - Significant performance gains
   - Proper process/thread pool usage

4. **Animation Support**:
   - Automatic tile animations (water, flowers)
   - Trigger-based animations (TVs, doors)

5. **Warp System**:
   - Resolves warp destinations to coordinates
   - Handles multi-map connections

6. **Movement Behaviors**:
   - 130+ movement types mapped
   - Configurable behavior parameters

### 6.2 Notable Implementations

#### A. Metatile Rendering
Complex palette application with primary/secondary tileset coordination:

```python
# Tiles from primary tileset use palettes 0-5 from primary
# Tiles from secondary tileset:
#   - Palettes 0-5 from primary tileset
#   - Palettes 6-12 from secondary tileset
```

This matches GBA VRAM layout perfectly.

#### B. World Building
Graph-based world file generation from map connections:

```python
# Builds connectivity graph
# Uses BFS to arrange maps spatially
# Generates Tiled world file
```

#### C. Audio Loop Markers
Proper GBA loop marker parsing:

```python
# Detects '[' and ']' MIDI markers
# Calculates loop points in samples
# Embeds loop metadata in OGG
```

---

## 7. Identified Pain Points

### 7.1 Developer Experience

#### A. Setup Complexity
1. Install Python 3.8+
2. Install pip dependencies
3. Install system audio tools (platform-specific)
4. Clone pokeemerald decompilation
5. Build pokeemerald (to generate binary files)
6. Run porycon with correct paths

**Total time**: 30-60 minutes for first-time setup

#### B. Error Messages
```python
logger.warning(f"Layout {layout_id} not found")  # Not helpful
# Better: Show available layouts, suggest fixes
```

#### C. Debugging Difficulty
- No verbose logging for complex processes
- Parallel processing hides errors
- Silent failures common

### 7.2 Extensibility Issues

#### A. Adding New Animations
Requires code changes in multiple places:
1. Add images to `anim/` folder
2. Update `ANIMATION_MAPPINGS` dict
3. Potentially update `animation_scanner.py` logic

**Should be**: Config-driven with auto-detection

#### B. Adding New Tilesets
1. Ensure tileset follows naming convention
2. Check primary vs secondary categorization
3. Add to pokeemerald
4. Hope it works (no validation)

#### C. Custom Movement Behaviors
Requires updating `MOVEMENT_TYPE_TO_BEHAVIOR` dict

**Should be**: Plugin system or config file

### 7.3 Performance Bottlenecks

1. **Redundant File I/O**: Same files read multiple times
2. **Large Tileset Images**: Loaded entirely into memory
3. **No Incremental Updates**: Full rebuild every time
4. **Palette Lookups**: O(n) searches repeated frequently

### 7.4 Maintenance Burden

**Current State**:
- 11,575 lines of code
- 0 tests
- Minimal documentation
- Complex interdependencies

**Estimated Maintenance Cost**: High

**Bus Factor**: 1 (appears to be single developer)

---

## 8. Recommendations

### 8.1 Critical (Do First)

1. **Add Tests** (Priority: CRITICAL)
   - Unit tests for core conversions
   - Integration tests for full pipeline
   - Test fixtures with sample pokeemerald data
   - Target: 70%+ coverage

2. **Split Large Files** (Priority: HIGH)
   - Break `converter.py` into 5-6 smaller modules
   - Separate CLI from business logic in `__main__.py`
   - Extract processing logic to dedicated classes

3. **Document Architecture** (Priority: HIGH)
   - Add architecture diagram
   - Document data flow
   - Explain key algorithms (metatile processing, palette system)

4. **Error Handling** (Priority: HIGH)
   - Unified error handling strategy
   - Informative error messages
   - Validation of input structure
   - Fail fast with clear messages

### 8.2 High Priority

5. **Configuration System** (Priority: MEDIUM)
   - Move hardcoded mappings to JSON/YAML
   - Config for animation mappings
   - Config for tileset paths
   - Config for output structure

6. **Dependency Management** (Priority: MEDIUM)
   - Add setup.py extras for audio tools
   - Document system dependencies clearly
   - Add dependency validation on startup
   - Pin version ranges

7. **Logging Improvements** (Priority: MEDIUM)
   - Structured logging (JSON)
   - Configurable verbosity levels
   - Progress indicators for long operations
   - Error context (show what was being processed)

### 8.3 Medium Priority

8. **Performance Optimizations**
   - Cache parsed files in memory
   - Streaming for large files
   - Incremental update detection
   - Optimize palette lookups (dict instead of list)

9. **Code Quality**
   - Type hints (use mypy)
   - Black formatting
   - Flake8 linting
   - Pre-commit hooks

10. **Refactoring**
    - Dependency injection
    - Single Responsibility Principle
    - Clear module boundaries
    - Remove dead code

### 8.4 Low Priority

11. **Feature Additions**
    - Plugin system for custom processors
    - Watch mode (auto-rebuild on changes)
    - Diff mode (only convert changed maps)
    - Validation mode (check pokeemerald structure)

12. **Developer Experience**
    - Interactive setup wizard
    - Better error messages with suggestions
    - Dry-run mode
    - Stats/metrics output

---

## 9. Conversion to C# (Porycon3 Considerations)

### 9.1 Why C# Makes Sense

**Benefits**:
1. **Type Safety**: Stronger typing than Python, catch errors at compile time
2. **Performance**: Faster execution, especially for image processing
3. **Integration**: Native .NET integration with PokeSharp
4. **Tooling**: Better IDE support (Visual Studio, Rider)
5. **Deployment**: Single executable, no Python runtime needed

**Challenges**:
1. **Image Processing**: Need to port Pillow code to ImageSharp/SkiaSharp
2. **Parallel Processing**: Use TPL (Task Parallel Library)
3. **Binary Reading**: Use BinaryReader (similar to struct)
4. **Audio Conversion**: Still need external tools (FFmpeg)

### 9.2 Architecture Recommendations for C#

#### A. Clean Architecture Layers

```
Porycon3.Domain         # Core business logic, entities
Porycon3.Application    # Use cases, interfaces
Porycon3.Infrastructure # File I/O, external tools
Porycon3.Presentation   # CLI, future GUI
```

#### B. Dependency Injection

```csharp
services.AddScoped<IMapConverter, MapConverter>();
services.AddScoped<ITilesetBuilder, TilesetBuilder>();
services.AddScoped<IMapReader, MapReader>();
```

#### C. CQRS Pattern

```csharp
// Commands
ConvertMapCommand
ConvertTilesetCommand
ExtractAudioCommand

// Queries
GetMapInfoQuery
GetTilesetInfoQuery
```

#### D. Unit Testing from Day 1

```csharp
[Fact]
public void ConvertMetatile_NormalType_SplitsCorrectly()
{
    // Arrange
    var metatile = new Metatile { Id = 1, Type = LayerType.Normal };

    // Act
    var result = _converter.ConvertMetatile(metatile);

    // Assert
    Assert.Equal(4, result.Bg2Tiles.Count);
    Assert.Equal(4, result.Bg1Tiles.Count);
    Assert.Empty(result.Bg3Tiles);
}
```

### 9.3 Technology Stack for C#

**Core**:
- .NET 8+ (latest LTS)
- C# 12+

**Libraries**:
- **ImageSharp** (image processing, replacement for Pillow)
- **Newtonsoft.Json** or **System.Text.Json** (JSON)
- **Serilog** (logging)
- **CommandLineParser** (CLI)
- **xUnit** (testing)
- **Moq** (mocking)
- **FluentAssertions** (test assertions)

**Optional**:
- **Spectre.Console** (rich CLI output)
- **MediatR** (CQRS)
- **AutoMapper** (DTO mapping)

### 9.4 Migration Strategy

#### Phase 1: Core Data Structures
- Port binary readers (map.bin, metatiles.bin)
- Port data structures (Metatile, Tile, Layer)
- Unit tests for binary parsing

#### Phase 2: Conversion Logic
- Port metatile processing
- Port tileset building
- Port palette system
- Integration tests for conversions

#### Phase 3: Output Formats
- Port Tiled JSON generation
- Port PokeSharp Definition generation
- End-to-end tests

#### Phase 4: Additional Features
- Port animation system
- Port audio conversion
- Port sprite extraction

#### Phase 5: Optimizations
- Parallel processing with TPL
- Memory optimizations
- Performance benchmarks

### 9.5 Estimated Effort

**Total Lines of Code**: 11,575 Python → ~15,000 C# (more verbose)

**Development Time** (with tests):
- Phase 1: 2-3 weeks
- Phase 2: 3-4 weeks
- Phase 3: 2 weeks
- Phase 4: 3 weeks
- Phase 5: 1-2 weeks

**Total**: 11-14 weeks (2.5-3.5 months)

**Team Size**: 1-2 developers

---

## 10. Conclusion

### 10.1 Summary

Porycon2 is a **functional but fragile** conversion tool with:

**Strengths**:
- ✅ Comprehensive feature coverage
- ✅ Handles complex GBA formats correctly
- ✅ Parallelized for performance
- ✅ Dual output modes (Tiled + PokeSharp)

**Weaknesses**:
- ❌ No tests
- ❌ Large monolithic files
- ❌ Hardcoded data
- ❌ Poor error handling
- ❌ Limited documentation
- ❌ High maintenance burden

### 10.2 Risk Assessment

**Current Risks**:
1. **No tests** → High regression risk
2. **Single developer** → Bus factor = 1
3. **Complex code** → Hard to modify
4. **External dependencies** → Fragile setup
5. **Silent failures** → Hard to debug

**Overall Risk Level**: **HIGH**

### 10.3 Path Forward

**Recommended Approach**: **Rewrite in C#** (Porycon3)

**Rationale**:
1. Fixing all issues in Python would take 60-80% of rewrite time
2. C# provides better tooling, type safety, and integration
3. Opportunity to apply clean architecture from start
4. Tests can be written alongside (TDD approach)
5. Better alignment with PokeSharp (.NET ecosystem)

**Alternative**: If rewrite not feasible, prioritize:
1. Add tests (critical)
2. Split large files (high)
3. Document architecture (high)
4. Fix error handling (high)
5. Extract hardcoded data (medium)

### 10.4 Next Steps

1. **Decision**: Rewrite vs. Refactor?
2. **If Rewrite**: Follow Phase 1-5 plan above
3. **If Refactor**: Start with critical recommendations
4. **Immediate**: Create test fixtures from working conversions

---

## Appendix A: File Inventory

### Source Files (25 total)

| File | LOC | Complexity | Notes |
|------|-----|------------|-------|
| converter.py | 2,036 | Very High | Needs splitting |
| definition_converter.py | 1,073 | High | Complex transformations |
| audio_converter.py | 960 | High | MIDI parsing |
| animation_scanner.py | 804 | Medium | Hardcoded mappings |
| id_transformer.py | 828 | Medium | Purpose unclear |
| sprite_extractor.py | 794 | Medium | Separate tool |
| __main__.py | 735 | High | Too much CLI logic |
| tileset_builder.py | 527 | High | Palette complexity |
| entity_converter.py | 408 | Medium | Dead code? |
| animation_parser.py | 408 | Medium | Animation logic |
| metatile_renderer.py | 373 | Medium | Image rendering |
| metatile_processor.py | 326 | Medium | Tile processing |
| utils.py | 324 | Low | Helper functions |
| popup_extractor.py | 315 | Low | Simple extractor |
| section_extractor.py | 286 | Low | Simple extractor |
| text_window_extractor.py | 270 | Low | Simple extractor |
| world_builder.py | 232 | Medium | Graph algorithms |
| palette_loader.py | 189 | Low | Binary parsing |
| map_reader.py | 173 | Low | Binary parsing |
| metatile.py | 130 | Low | Data structures |
| constants.py | 134 | Low | Configuration |
| map_worker.py | 89 | Low | Wrapper |
| sprite_extract_main.py | 80 | Low | CLI entry |
| logging_config.py | 61 | Low | Logging setup |
| __init__.py | 20 | Low | Package init |

### Documentation (2 files)

- README.md (288 lines) - Comprehensive
- docs/animations.md (300+ lines) - Detailed

### Configuration (3 files)

- setup.py (37 lines) - Minimal
- requirements.txt (8 lines) - Very minimal
- .gitignore (15 lines) - Basic

---

## Appendix B: External References

**pokeemerald Decompilation**:
- https://github.com/pret/pokeemerald

**Tiled Map Editor**:
- https://www.mapeditor.org/
- JSON format: https://doc.mapeditor.org/en/stable/reference/json-map-format/

**GBA Graphics Format**:
- Metatiles: 16x16px, composed of 8x8 tiles
- Palettes: 16 colors, RGB555 format
- VRAM: Separate background layers (BG0-BG3)

**Audio Tools**:
- TiMidity++: http://timidity.sourceforge.net/
- FluidSynth: https://www.fluidsynth.org/
- FFmpeg: https://ffmpeg.org/

---

**End of Analysis Report**
