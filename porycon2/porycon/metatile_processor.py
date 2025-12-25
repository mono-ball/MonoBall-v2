"""
Metatile processor - handles metatile to tile conversion logic.

This module extracts metatile processing logic from MapConverter to improve
testability and separation of concerns.
"""

from typing import Dict, List, Tuple, Optional
from PIL import Image
from .metatile import MetatileLayerType, NUM_TILES_PER_METATILE
from .metatile_renderer import MetatileRenderer
from .constants import (
    NUM_METATILES_IN_PRIMARY,
    NUM_TILES_IN_PRIMARY_VRAM,
    METATILE_SIZE
)
from .logging_config import get_logger

logger = get_logger('metatile_processor')


class MetatileProcessor:
    """Handles metatile to tile conversion logic."""
    
    def __init__(self, metatile_renderer: MetatileRenderer):
        """
        Initialize MetatileProcessor.
        
        Args:
            metatile_renderer: MetatileRenderer instance for rendering metatiles
        """
        self.renderer = metatile_renderer
    
    def determine_tileset_for_metatile(
        self,
        metatile_id: int,
        primary_tileset: str,
        secondary_tileset: str
    ) -> Tuple[str, int]:
        """
        Determine which tileset a metatile belongs to based on its ID.
        
        Args:
            metatile_id: The metatile ID from map data
            primary_tileset: Name of primary tileset
            secondary_tileset: Name of secondary tileset
        
        Returns:
            Tuple of (tileset_name, actual_metatile_id)
            - tileset_name: 'primary' or 'secondary' tileset name
            - actual_metatile_id: The metatile ID within that tileset (0-based)
        """
        if metatile_id < NUM_METATILES_IN_PRIMARY:
            return (primary_tileset, metatile_id)
        else:
            return (secondary_tileset, metatile_id - NUM_METATILES_IN_PRIMARY)
    
    def determine_tileset_for_tile(
        self,
        tile_id: int,
        current_tileset_name: str,
        primary_tileset: str,
        secondary_tileset: str
    ) -> Tuple[str, int]:
        """
        Determine which tileset a tile belongs to based on its ID.
        
        For secondary tilesets: tile IDs 0-511 are from primary, 512+ are from secondary.
        
        Args:
            tile_id: The tile ID
            current_tileset_name: The tileset the metatile belongs to
            primary_tileset: Name of primary tileset
            secondary_tileset: Name of secondary tileset
        
        Returns:
            Tuple of (tileset_name, adjusted_tile_id)
            - tileset_name: Which tileset the tile actually belongs to
            - adjusted_tile_id: The tile ID within that tileset
        """
        if current_tileset_name == primary_tileset:
            # Primary tileset tiles always belong to primary
            return (primary_tileset, tile_id)
        else:
            # Secondary tileset: check if tile ID references primary or secondary
            if tile_id < NUM_TILES_IN_PRIMARY_VRAM:
                # Tile ID 0-511 refers to PRIMARY tileset
                return (primary_tileset, tile_id)
            else:
                # Tile ID 512+ refers to SECONDARY tileset (offset by 512)
                return (secondary_tileset, tile_id - NUM_TILES_IN_PRIMARY_VRAM)
    
    def validate_metatile_bounds(
        self,
        actual_metatile_id: int,
        metatiles_with_attrs: List[Tuple[int, int, int]]
    ) -> bool:
        """
        Validate that a metatile ID is within bounds.
        
        Args:
            actual_metatile_id: The metatile ID within its tileset
            metatiles_with_attrs: List of metatiles with attributes
        
        Returns:
            True if valid, False if out of bounds
        """
        if actual_metatile_id < 0:
            return False
        
        start_idx = actual_metatile_id * NUM_TILES_PER_METATILE
        end_idx = start_idx + NUM_TILES_PER_METATILE
        
        if (start_idx < 0 or 
            end_idx > len(metatiles_with_attrs) or
            len(metatiles_with_attrs) < NUM_TILES_PER_METATILE):
            return False
        
        return True
    
    def process_single_metatile(
        self,
        actual_metatile_id: int,
        tileset_name: str,
        metatiles_with_attrs: List[Tuple[int, int, int]],
        attributes: Dict[int, int],
        primary_tileset: str,
        secondary_tileset: str,
        used_metatiles: Dict[Tuple[int, str, int], Tuple[Image.Image, Image.Image]],
        image_to_gid: Dict[bytes, int],
        next_gid: int
    ) -> Tuple[
        Optional[Tuple[Image.Image, Image.Image]],
        Dict[Tuple[int, str, int, bool], int],
        Dict[Tuple[int, str], List[Tuple[int, Tuple[int, str, int], int]]],
        Optional[List[Tuple[int, int, int]]],
        Dict[bytes, int],
        int
    ]:
        """
        Process a single metatile: render it and assign GIDs.
        
        Args:
            actual_metatile_id: Metatile ID within its tileset
            tileset_name: Name of the tileset this metatile belongs to
            metatiles_with_attrs: List of all metatiles with attributes
            attributes: Dictionary of metatile attributes (for layer type)
            primary_tileset: Name of primary tileset
            secondary_tileset: Name of secondary tileset
            used_metatiles: Dictionary of already-processed metatiles
            image_to_gid: Dictionary mapping image bytes to GID (for deduplication)
            next_gid: Next available GID
        
        Returns:
            Tuple of:
            - metatile_images: (bottom_img, top_img) or None if invalid
            - metatile_to_gid: Dictionary mapping (metatile_id, tileset, layer_type, is_top) -> GID
            - tile_id_to_gids: Dictionary mapping (tile_id, tileset) -> list of (layer_gid, metatile_key, tile_position)
            - metatile_tiles: List of (tile_id, flip_flags, palette_index) or None if invalid
            - updated_image_to_gid: Updated image_to_gid dictionary
            - updated_next_gid: Updated next_gid
        """
        # Get layer type
        layer_type_val = attributes.get(actual_metatile_id, 0)
        layer_type = MetatileLayerType(layer_type_val)
        
        # Validate bounds
        if not self.validate_metatile_bounds(actual_metatile_id, metatiles_with_attrs):
            # Create empty metatile
            key = (actual_metatile_id, tileset_name, layer_type_val)
            if key not in used_metatiles:
                empty_img = Image.new('RGBA', (METATILE_SIZE, METATILE_SIZE), (0, 0, 0, 0))
                used_metatiles[key] = (empty_img, empty_img)
                empty_bytes = empty_img.tobytes()
                if empty_bytes in image_to_gid:
                    empty_gid = image_to_gid[empty_bytes]
                else:
                    empty_gid = next_gid
                    image_to_gid[empty_bytes] = empty_gid
                    next_gid += 1
                
                metatile_to_gid = {
                    (actual_metatile_id, tileset_name, layer_type_val, False): empty_gid,
                    (actual_metatile_id, tileset_name, layer_type_val, True): empty_gid
                }
                return (None, metatile_to_gid, {}, None, image_to_gid, next_gid)
            else:
                # Already processed, return existing GIDs
                bottom_img, top_img = used_metatiles[key]
                bottom_bytes = bottom_img.tobytes()
                top_bytes = top_img.tobytes()
                bottom_gid = image_to_gid.get(bottom_bytes, next_gid)
                top_gid = image_to_gid.get(top_bytes, next_gid + 1)
                if bottom_bytes not in image_to_gid:
                    image_to_gid[bottom_bytes] = bottom_gid
                    next_gid += 1
                if top_bytes not in image_to_gid:
                    image_to_gid[top_bytes] = top_gid
                    next_gid += 1
                metatile_to_gid = {
                    (actual_metatile_id, tileset_name, layer_type_val, False): bottom_gid,
                    (actual_metatile_id, tileset_name, layer_type_val, True): top_gid
                }
                return ((bottom_img, top_img), metatile_to_gid, {}, None, image_to_gid, next_gid)
        
        # Safe to access: bounds validated
        start_idx = actual_metatile_id * NUM_TILES_PER_METATILE
        metatile_tiles = metatiles_with_attrs[start_idx:start_idx + NUM_TILES_PER_METATILE]
        
        # Validate we got expected number of tiles
        if len(metatile_tiles) != NUM_TILES_PER_METATILE:
            # Create empty metatile
            key = (actual_metatile_id, tileset_name, layer_type_val)
            if key not in used_metatiles:
                empty_img = Image.new('RGBA', (METATILE_SIZE, METATILE_SIZE), (0, 0, 0, 0))
                used_metatiles[key] = (empty_img, empty_img)
                empty_bytes = empty_img.tobytes()
                if empty_bytes in image_to_gid:
                    empty_gid = image_to_gid[empty_bytes]
                else:
                    empty_gid = next_gid
                    image_to_gid[empty_bytes] = empty_gid
                    next_gid += 1
                
                metatile_to_gid = {
                    (actual_metatile_id, tileset_name, layer_type_val, False): empty_gid,
                    (actual_metatile_id, tileset_name, layer_type_val, True): empty_gid
                }
                return (None, metatile_to_gid, {}, None, image_to_gid, next_gid)
        
        # Render metatile
        key = (actual_metatile_id, tileset_name, layer_type_val)
        if key not in used_metatiles:
            bottom_img, top_img = self.renderer.render_metatile(
                metatile_tiles,
                primary_tileset,
                secondary_tileset,
                layer_type
            )
            if bottom_img is None:
                bottom_img = Image.new('RGBA', (METATILE_SIZE, METATILE_SIZE), (0, 0, 0, 0))
            if top_img is None:
                top_img = Image.new('RGBA', (METATILE_SIZE, METATILE_SIZE), (0, 0, 0, 0))
            used_metatiles[key] = (bottom_img, top_img)
            
            # Assign GIDs with deduplication
            bottom_bytes = bottom_img.tobytes()
            top_bytes = top_img.tobytes()
            
            if bottom_bytes in image_to_gid:
                bottom_gid = image_to_gid[bottom_bytes]
            else:
                bottom_gid = next_gid
                image_to_gid[bottom_bytes] = bottom_gid
                next_gid += 1
            
            if top_bytes in image_to_gid:
                top_gid = image_to_gid[top_bytes]
            else:
                top_gid = next_gid
                image_to_gid[top_bytes] = top_gid
                next_gid += 1
            
            metatile_to_gid = {
                (actual_metatile_id, tileset_name, layer_type_val, False): bottom_gid,
                (actual_metatile_id, tileset_name, layer_type_val, True): top_gid
            }
            
            # Build (tile_id, tileset) -> GID mapping for animations
            tile_id_to_gids: Dict[Tuple[int, str], List[Tuple[int, Tuple[int, str, int], int]]] = {}
            for tile_position, (tile_id, _, _) in enumerate(metatile_tiles):
                tile_source_tileset, _ = self.determine_tileset_for_tile(
                    tile_id, tileset_name, primary_tileset, secondary_tileset
                )
                tile_key = (tile_id, tile_source_tileset)
                if tile_key not in tile_id_to_gids:
                    tile_id_to_gids[tile_key] = []
                metatile_key = (actual_metatile_id, tileset_name, layer_type_val)
                layer_gid = bottom_gid if tile_position < 4 else top_gid
                tile_id_to_gids[tile_key].append((layer_gid, metatile_key, tile_position))
            
            return ((bottom_img, top_img), metatile_to_gid, tile_id_to_gids, metatile_tiles, image_to_gid, next_gid)
        else:
            # Already processed, return existing GIDs
            bottom_img, top_img = used_metatiles[key]
            bottom_bytes = bottom_img.tobytes()
            top_bytes = top_img.tobytes()
            bottom_gid = image_to_gid.get(bottom_bytes)
            top_gid = image_to_gid.get(top_bytes)
            
            if bottom_gid is None or top_gid is None:
                # Shouldn't happen, but handle gracefully
                if bottom_bytes in image_to_gid:
                    bottom_gid = image_to_gid[bottom_bytes]
                else:
                    bottom_gid = next_gid
                    image_to_gid[bottom_bytes] = bottom_gid
                    next_gid += 1
                
                if top_bytes in image_to_gid:
                    top_gid = image_to_gid[top_bytes]
                else:
                    top_gid = next_gid
                    image_to_gid[top_bytes] = top_gid
                    next_gid += 1
            
            metatile_to_gid = {
                (actual_metatile_id, tileset_name, layer_type_val, False): bottom_gid,
                (actual_metatile_id, tileset_name, layer_type_val, True): top_gid
            }
            
            # Build tile_id_to_gids for this metatile
            tile_id_to_gids: Dict[Tuple[int, str], List[Tuple[int, Tuple[int, str, int], int]]] = {}
            for tile_position, (tile_id, _, _) in enumerate(metatile_tiles):
                tile_source_tileset, _ = self.determine_tileset_for_tile(
                    tile_id, tileset_name, primary_tileset, secondary_tileset
                )
                tile_key = (tile_id, tile_source_tileset)
                if tile_key not in tile_id_to_gids:
                    tile_id_to_gids[tile_key] = []
                metatile_key = (actual_metatile_id, tileset_name, layer_type_val)
                layer_gid = bottom_gid if tile_position < 4 else top_gid
                tile_id_to_gids[tile_key].append((layer_gid, metatile_key, tile_position))
            
            return ((bottom_img, top_img), metatile_to_gid, tile_id_to_gids, metatile_tiles, image_to_gid, next_gid)

