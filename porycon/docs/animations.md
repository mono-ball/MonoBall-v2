# Tile Animations in Porycon

**Last Updated**: December 4, 2025  
**Status**: Comprehensive guide to tile animation conversion

---

## Overview

Porycon converts pokeemerald tile animations to Tiled format, supporting both **automatic animations** (time-based, like water and flowers) and **trigger animations** (event-based, like TVs and doors).

---

## 1. Automatic Tile Animations

### What Are Automatic Animations?

Automatic animations are **time-based** and loop continuously:
- Water ripples
- Flowing waterfalls
- Swaying flowers
- Lava bubbling

### How Pokeemerald Handles Them

Pokeemerald uses a **tile replacement system** where animation frames are directly copied to VRAM (video memory) every frame.

#### Global Frame Counter

Every frame (60fps), pokeemerald increments a global counter:

```c
static u16 sPrimaryTilesetAnimCounter;      // 0-255, wraps to 0
static u16 sSecondaryTilesetAnimCounter;    // 0-255, wraps to 0
```

This counter is **shared by all animations** in a tileset, ensuring they stay synchronized.

#### Update Loop (Every Frame)

```c
void UpdateTilesetAnimations(void)
{
    // Increment counters (wrap at 256)
    if (++sPrimaryTilesetAnimCounter >= 256)
        sPrimaryTilesetAnimCounter = 0;
    if (++sSecondaryTilesetAnimCounter >= 256)
        sSecondaryTilesetAnimCounter = 0;

    // Call tileset-specific animation functions
    if (sPrimaryTilesetAnimCallback)
        sPrimaryTilesetAnimCallback(sPrimaryTilesetAnimCounter);
    if (sSecondaryTilesetAnimCallback)
        sSecondaryTilesetAnimCallback(sSecondaryTilesetAnimCounter);
}
```

#### Tileset-Specific Animation Function

Each tileset has a callback that schedules animations using **modulo checks**:

```c
static void TilesetAnim_General(u16 timer)
{
    // Flower: Updates every 16 frames (timer % 16 == 0)
    if (timer % 16 == 0)
        QueueAnimTiles_General_Flower(timer / 16);

    // Water: Updates every 16 frames, offset by 1 (timer % 16 == 1)
    if (timer % 16 == 1)
        QueueAnimTiles_General_Water(timer / 16);

    // Sand/Water Edge: Updates every 16 frames, offset by 2
    if (timer % 16 == 2)
        QueueAnimTiles_General_SandWaterEdge(timer / 16);
}
```

**Key Insight**: Animations are **staggered** across frames to spread out VRAM transfers. Only one animation updates per frame.

### How Porycon Converts Them

#### 1. Animation Detection

The `AnimationScanner` class scans for animation frames in the `anim` folders:
- `data/tilesets/primary/{tileset_name}/anim/{animation_name}/0.png, 1.png, ...`
- `data/tilesets/secondary/{tileset_name}/anim/{animation_name}/0.png, 1.png, ...`

#### 2. Animation Mappings

Animations are mapped based on hardcoded tile offsets from `tileset_anims.c`:

```python
ANIMATION_MAPPINGS = {
    "general": {
        "water": {
            "base_tile_id": 432,
            "num_tiles": 30,
            "anim_folder": "water",
            "duration_ms": 200
        },
        # ... more animations
    }
}
```

#### 3. Tile Extraction

For each animation:
1. All frame images are loaded from the `anim` folder
2. Tiles are extracted from each frame (frames contain multiple tiles laid out horizontally)
3. Animation tiles are added to the tileset image after the regular tiles

#### 4. Tiled Format Export

Animations are added to the tileset JSON in Tiled's format:

```json
{
  "tiles": [
    {
      "id": 42,
      "animation": [
        {"tileid": 100, "duration": 200},
        {"tileid": 101, "duration": 200},
        {"tileid": 102, "duration": 200}
      ]
    }
  ]
}
```

### Supported Automatic Animations

#### Primary Tilesets
- **general**: flower, water, sand_water_edge, waterfall, land_water_edge
- **building**: tv_turned_on (when configured as automatic)

#### Secondary Tilesets
- **rustboro**: windy_water, fountain
- **dewford**: flag
- **slateport**: balloons
- **mauville**: flower_1, flower_2
- **lavaridge**: steam, lava
- **ever_grande**: flowers
- **pacifidlog**: log_bridges, water_currents
- **sootopolis**: stormy_water
- **underwater**: seaweed
- **cave**: lava
- **battle_frontier_outside_west/east**: flag
- **mauville_gym**: electric_gates
- **sootopolis_gym**: side_waterfall, front_waterfall

---

## 2. Trigger-Based Animations

### What Are Trigger Animations?

Trigger animations are **event-driven** and state-based:
- TV turning on/off
- Doors opening/closing
- Tall grass when stepped on
- Switches being flipped

### Key Differences

| Automatic Animations | Trigger Animations |
|---------------------|-------------------|
| Loop continuously | Play on demand |
| Time-driven | Event-driven |
| Always animating | State-based (on/off, open/closed) |
| Examples: Water, flowers | Examples: TV, doors, tall grass |

### How Pokeemerald Handles Them

Pokeemerald uses the `setmetatile` script command to change tiles at runtime:

```c
// Example: TV turning on
void EventScript_TurnOnTV(void)
{
    // Change metatile at position (x, y) from TV_OFF to TV_ON
    setmetatile x, y, METATILE_Building_TV_On;
}
```

### Animation States

Trigger animations typically have:
- **Base state**: Default appearance (TV off, door closed, normal grass)
- **Triggered state**: Changed appearance (TV on, door open, grass stepped on)
- **Animation frames**: Optional transition frames between states

### Common Patterns

#### TV On/Off
- **Base metatile**: `METATILE_Building_TV_Off` (static)
- **Triggered metatile**: `METATILE_Building_TV_On` (animated frames)
- **Trigger**: Script call (interaction)
- **State**: Toggle between on/off

#### Door Opening
- **Base metatile**: `METATILE_Building_Door_Closed`
- **Triggered metatile**: `METATILE_Building_Door_Open`
- **Trigger**: Script call (interaction or warp)
- **State**: One-way (closed → open) or toggle

#### Tall Grass
- **Base metatile**: `METATILE_General_TallGrass`
- **Triggered metatile**: `METATILE_General_TallGrass_Stepped` (temporary)
- **Trigger**: Step event
- **State**: Temporary (returns to base after animation)

### Conversion Strategy

#### Step 1: Identify Trigger Animations in Pokeemerald

Look for:
1. **Script files** that call `setmetatile` with animation-related metatiles
2. **Metatile definitions** that have multiple variants (e.g., `TV_Off`, `TV_On`)
3. **Animation folders** that contain state-based frames (e.g., `tv_turned_on/`)

#### Step 2: Mark Tiles in Tiled

Add custom properties to tiles in the tileset:

```json
{
  "id": 42,
  "properties": [
    {
      "name": "trigger_animation",
      "type": "class",
      "value": {
        "animationType": "toggle",     // or "one_way", "temporary"
        "baseState": "off",            // or "closed", "normal"
        "triggeredState": "on",        // or "open", "stepped"
        "baseTileId": 42,             // Current tile ID
        "triggeredTileId": 43,        // Target tile ID when triggered
        "animationFrames": [43, 44, 45], // Transition frames
        "frameDuration": 100          // ms per frame
      }
    }
  ]
}
```

#### Step 3: Game Engine Implementation

The game engine reads these properties and:
1. Creates `TriggeredTileAnimation` components
2. `TriggeredTileAnimationSystem` manages state changes
3. Scripts call `TriggerAnimation(x, y)` to activate

---

## 3. Implementation Details

### Porycon (Converter)

**Files**:
- `porycon/animation_scanner.py` - Scans and detects animations
- `porycon/tileset_builder.py` - Builds animated tilesets
- `porycon/converter.py` - Exports animation data to Tiled JSON

**Process**:
1. Scan `anim/` folders for animation frames
2. Match frames to base tile IDs using mappings
3. Extract tiles from frame images
4. Add animation data to Tiled tileset JSON
5. Mark trigger animations with custom properties

### MonoBall Framework (Game Engine)

**Automatic Animations**:
- Read from Tiled tileset JSON
- `TileAnimationSystem` updates tiles every frame
- Uses frame duration from JSON

**Trigger Animations**:
- Read `trigger_animation` properties from tileset
- Create `TriggeredTileAnimation` components
- `TriggeredTileAnimationSystem` handles state changes
- Scripts/events trigger animations via API

---

## 4. Adding New Animations

### For Automatic Animations

1. **Add animation frames** to pokeemerald:
   ```
   data/tilesets/primary/{tileset}/anim/{animation_name}/0.png
   data/tilesets/primary/{tileset}/anim/{animation_name}/1.png
   ...
   ```

2. **Add mapping** to `animation_scanner.py`:
   ```python
   ANIMATION_MAPPINGS = {
       "your_tileset": {
           "your_animation": {
               "base_tile_id": 100,    # First tile to animate
               "num_tiles": 10,        # Number of tiles in animation
               "anim_folder": "your_animation",
               "duration_ms": 200      # Frame duration
           }
       }
   }
   ```

3. **Run porycon** to regenerate tileset

### For Trigger Animations

1. **Add animation states** to pokeemerald metatiles
2. **Mark as trigger** in `animation_scanner.py`:
   ```python
   TRIGGER_ANIMATIONS = {
       "your_tileset": ["tv_on_off", "door_open"]
   }
   ```

3. **Run porycon** to add trigger properties
4. **Implement script** in game to trigger the animation

---

## 5. Troubleshooting

### Animation Not Showing

**Check**:
1. Animation frames exist in `anim/` folder
2. Mapping is correct in `animation_scanner.py`
3. `base_tile_id` matches actual tile position
4. Tileset JSON has `animation` property

### Wrong Animation Speed

**Fix**: Adjust `duration_ms` in animation mapping

### Tiles in Wrong Position

**Check**:
1. Frame images have tiles in horizontal layout
2. `num_tiles` matches actual tile count
3. Tile extraction logic in `metatile_renderer.py`

### Trigger Animation Not Working

**Check**:
1. Custom properties are in tileset JSON
2. Game engine reads `trigger_animation` property
3. Script correctly calls trigger API
4. State machine is properly implemented

---

## 6. Technical Notes

### Performance Considerations

- **Automatic animations**: One VRAM transfer per frame per animation
- **Staggered updates**: Pokeemerald spreads updates across frames
- **Porycon optimization**: Only includes used tiles in tileset

### Tile ID Remapping

Porycon remaps tile IDs to be sequential:
- Original pokeemerald: Sparse tile IDs with gaps
- Porycon output: Sequential 1-based IDs
- Animation mappings preserve relationships

### Frame Duration

- Pokeemerald: Hardcoded frame counts (e.g., every 16 frames)
- Porycon: Converts to milliseconds (200ms default)
- Tiled: Uses millisecond durations per frame

---

## 7. Future Enhancements

### Planned Features

- [ ] Auto-detect animations without manual mappings
- [ ] Support for variable frame durations
- [ ] Export trigger animation logic to Tiled scripts
- [ ] Animated metatiles (4-tile animations)
- [ ] Palette animations (color cycling)

### Known Limitations

- Manual mapping required for new animations
- Trigger animations need game engine support
- No support for palette-based animations yet
- Animated metatiles not yet implemented

---

## 8. Summary

### Quick Reference

| Animation Type | Time-Based | Event-Based | Use Case |
|---------------|------------|-------------|----------|
| **Automatic** | ✅ Yes | ❌ No | Water, flowers, waterfalls |
| **Trigger** | ❌ No | ✅ Yes | TVs, doors, tall grass |

### Conversion Flow

```
Pokeemerald
    ↓
1. Animation frames in anim/ folders
    ↓
2. Porycon scans and maps animations
    ↓
3. Tiles extracted from frames
    ↓
4. Tiled JSON with animation data
    ↓
5. Game engine reads and animates
```

### Key Files

- **Porycon**: `animation_scanner.py`, `tileset_builder.py`
- **Pokeemerald**: `src/tileset_anims.c`, `data/tilesets/*/anim/`
- **Output**: `Tilesets/{tileset}/{tileset}.json`

---

**Need Help?**

- Check `porycon/README.md` for converter usage
- See animation mappings in `animation_scanner.py`
- Review Tiled documentation for animation format

---

**Status**: ✅ Automatic animations fully supported | 🟡 Trigger animations in development



