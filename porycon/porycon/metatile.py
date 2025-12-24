"""
Metatile conversion utilities.

Converts Pokemon Emerald metatiles (2x2 groups of 8 tiles) into individual tiles
distributed across BG layers based on layer type.
"""

from typing import List, Tuple
from enum import IntEnum
from .constants import (
    NUM_TILES_PER_METATILE,
    METATILE_WIDTH,
    METATILE_HEIGHT,
    METATILE_ID_MASK
)


class MetatileLayerType(IntEnum):
    """Metatile layer type determines how tiles are distributed across BG layers."""
    NORMAL = 0   # Bottom 4 tiles -> Bg2 (middle), Top 4 tiles -> Bg1 (top)
    COVERED = 1  # Bottom 4 tiles -> Bg3 (bottom), Top 4 tiles -> Bg2 (middle)
    SPLIT = 2    # Bottom 4 tiles -> Bg3 (bottom), Top 4 tiles -> Bg1 (top)


# Metatile structure: 8 tiles total
# Tiles 0-3: Bottom layer (2x2)
# Tiles 4-7: Top layer (2x2)
# Note: NUM_TILES_PER_METATILE, METATILE_WIDTH, METATILE_HEIGHT are imported from constants


def unpack_metatile_data_with_attrs(metatile_id: int, metatile_data: List[Tuple[int, int, int]]) -> List[Tuple[int, int, int]]:
    """
    Extract the 8 tiles from a metatile with full attributes.
    
    Args:
        metatile_id: The metatile ID (0-1023)
        metatile_data: The full metatiles array with (tile_id, flip_flags, palette_index) tuples
        
    Returns:
        List of 8 tuples: [(tile_id, flip, palette), ...]
    """
    if metatile_id < 0 or metatile_id * NUM_TILES_PER_METATILE >= len(metatile_data):
        return [(0, 0, 0)] * NUM_TILES_PER_METATILE
    
    start_idx = metatile_id * NUM_TILES_PER_METATILE
    return metatile_data[start_idx:start_idx + NUM_TILES_PER_METATILE]


def convert_metatile_to_tile_layers_with_attrs(
    metatile_id: int,
    metatile_data: List[Tuple[int, int, int]],
    layer_type: MetatileLayerType,
    x: int,
    y: int,
    map_width: int
) -> Tuple[dict, dict, dict]:
    """
    Convert a metatile with attributes to tile layers, preserving palette information.
    
    Args:
        metatile_id: Metatile ID
        metatile_data: List of (tile_id, flip_flags, palette_index) tuples
        layer_type: How to distribute across layers
        x: Metatile X position
        y: Metatile Y position
        map_width: Map width in metatiles
    
    Returns:
        Tuple of three dicts: (bg3_tiles, bg2_tiles, bg1_tiles)
        Each dict maps (tile_x, tile_y) -> (tile_id, palette_index)
    """
    metatile_tiles = unpack_metatile_data_with_attrs(metatile_id, metatile_data)
    
    # Split into bottom (0-3) and top (4-7) tiles
    bottom_tiles = metatile_tiles[0:4]  # [(tile_id, flip, palette), ...]
    top_tiles = metatile_tiles[4:8]
    
    # Determine which layers get which tiles based on layer_type
    if layer_type == MetatileLayerType.NORMAL:
        # Bottom -> Bg2, Top -> Bg1, Bg3 empty
        bg3_tiles_data = []
        bg2_tiles_data = bottom_tiles
        bg1_tiles_data = top_tiles
    elif layer_type == MetatileLayerType.COVERED:
        # Bottom -> Bg3, Top -> Bg2, Bg1 empty
        bg3_tiles_data = bottom_tiles
        bg2_tiles_data = top_tiles
        bg1_tiles_data = []
    elif layer_type == MetatileLayerType.SPLIT:
        # Bottom -> Bg3, Top -> Bg1, Bg2 empty
        bg3_tiles_data = bottom_tiles
        bg2_tiles_data = []
        bg1_tiles_data = top_tiles
    else:
        # Default to NORMAL
        bg3_tiles_data = []
        bg2_tiles_data = bottom_tiles
        bg1_tiles_data = top_tiles
    
    # Convert to dicts with palette info
    base_tile_x = x * 2
    base_tile_y = y * 2
    
    def create_tile_dict(tiles_data: List[Tuple[int, int, int]]) -> dict:
        """Create dict mapping (tile_x, tile_y) -> (tile_id, palette_index) for a 2x2 grid."""
        result = {}
        tile_idx = 0
        for ty in range(2):
            for tx in range(2):
                tile_x = base_tile_x + tx
                tile_y = base_tile_y + ty
                if tile_idx < len(tiles_data):
                    tile_id, flip_flags, palette_idx = tiles_data[tile_idx]
                    # Extract palette index from the tuple (it's the 3rd element)
                    if tile_id != 0:  # Only add non-empty tiles
                        result[(tile_x, tile_y)] = (tile_id, palette_idx)
                tile_idx += 1
        return result
    
    bg3_tiles = create_tile_dict(bg3_tiles_data)
    bg2_tiles = create_tile_dict(bg2_tiles_data)
    bg1_tiles = create_tile_dict(bg1_tiles_data)
    
    return bg3_tiles, bg2_tiles, bg1_tiles


def extract_metatile_id(entry: int) -> int:
    """Extract metatile ID from map entry (bits 0-9)."""
    return entry & METATILE_ID_MASK

