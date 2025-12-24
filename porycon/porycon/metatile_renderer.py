"""
Metatile rendering - renders metatiles as 16x16 images.
"""

from typing import List, Tuple, Optional, Dict
from pathlib import Path
from collections import OrderedDict
from PIL import Image
from .palette_loader import load_tileset_palettes, apply_palette_to_tile
from .metatile import MetatileLayerType, NUM_TILES_PER_METATILE
from .utils import camel_to_snake, TilesetPathResolver
from .constants import (
    NUM_TILES_IN_PRIMARY_VRAM,
    TILE_SIZE,
    METATILE_SIZE,
    FLIP_HORIZONTAL,
    FLIP_VERTICAL
)
from .logging_config import get_logger

logger = get_logger('metatile_renderer')


class MetatileRenderer:
    """Renders metatiles as 16x16 images."""
    
    def __init__(self, input_dir: str, max_cache_size: int = 50):
        """
        Initialize metatile renderer.
        
        Args:
            input_dir: Path to pokeemerald root directory
            max_cache_size: Maximum number of tilesets/palettes to cache (default: 50)
        """
        self.input_dir = Path(input_dir)
        self.tile_size = TILE_SIZE  # Each tile in a metatile is 8x8
        self.metatile_size = METATILE_SIZE  # Metatiles are 16x16 (2x2 tiles)
        self.max_cache_size = max_cache_size
        # Use OrderedDict for LRU cache behavior
        self._tileset_cache: OrderedDict[str, Image.Image] = OrderedDict()  # Cache loaded tileset images
        self._palette_cache: OrderedDict[str, List] = OrderedDict()  # Cache loaded palettes
    
    def load_tileset_image(self, tileset_name: str) -> Optional[Image.Image]:
        """Load tileset graphics, caching the result with LRU eviction."""
        # Check cache first (move to end if found for LRU)
        if tileset_name in self._tileset_cache:
            # Move to end (most recently used)
            self._tileset_cache.move_to_end(tileset_name)
            return self._tileset_cache[tileset_name]
        
        # Use TilesetPathResolver for path resolution
        resolver = TilesetPathResolver(self.input_dir)
        tileset_path = resolver.find_tileset_image_path(tileset_name)
        
        if tileset_path and tileset_path.exists():
            img = Image.open(tileset_path)
            if img.mode != 'P':
                img = img.convert('P')
            # Cache with size limit (LRU eviction)
            self._cache_tileset_image(tileset_name, img)
            return img
        
        # Track tried paths for error message
        tried_paths = []
        name_variants = [
            camel_to_snake(tileset_name),
            tileset_name.lower(),
            tileset_name.replace("_", "").lower(),
        ]
        for tileset_lower in name_variants:
            primary_path = self.input_dir / "data" / "tilesets" / "primary" / tileset_lower / "tiles.png"
            tried_paths.append(str(primary_path))
            secondary_path = self.input_dir / "data" / "tilesets" / "secondary" / tileset_lower / "tiles.png"
            tried_paths.append(str(secondary_path))
        
        # Store None in cache to avoid repeated lookups, but log the failure
        # Only log once per tileset to avoid spam
        if tileset_name not in getattr(self, '_missing_tilesets_logged', set()):
            if not hasattr(self, '_missing_tilesets_logged'):
                self._missing_tilesets_logged = set()
            self._missing_tilesets_logged.add(tileset_name)
            logger.warning(f"Tileset '{tileset_name}' not found. Tried paths:")
            for path in tried_paths[:3]:  # Only show first 3 to avoid spam
                logger.warning(f"      - {path}")
        
        # Cache None to avoid repeated lookups (with size limit)
        self._cache_tileset_image(tileset_name, None)
        return None
    
    def _cache_tileset_image(self, tileset_name: str, image: Optional[Image.Image]):
        """Cache tileset image with LRU eviction when cache is full."""
        # Remove if already exists (will be re-added at end)
        if tileset_name in self._tileset_cache:
            del self._tileset_cache[tileset_name]
        
        # Evict least recently used if cache is full
        if len(self._tileset_cache) >= self.max_cache_size:
            # Remove oldest (first) item
            self._tileset_cache.popitem(last=False)
        
        # Add to end (most recently used)
        self._tileset_cache[tileset_name] = image
    
    def load_tileset_palettes_cached(self, tileset_name: str) -> List:
        """Load palettes for a tileset, caching the result with LRU eviction."""
        # Check cache first (move to end if found for LRU)
        if tileset_name in self._palette_cache:
            # Move to end (most recently used)
            self._palette_cache.move_to_end(tileset_name)
            return self._palette_cache[tileset_name]
        
        # Use TilesetPathResolver for path resolution
        resolver = TilesetPathResolver(self.input_dir)
        result = resolver.find_tileset_path(tileset_name)
        
        if result:
            _, tileset_dir = result
            palettes = load_tileset_palettes(tileset_dir)
            # Cache with size limit (LRU eviction)
            self._cache_palettes(tileset_name, palettes)
            return palettes
        
        # Cache empty list to avoid repeated lookups
        self._cache_palettes(tileset_name, [])
        return []
    
    def _cache_palettes(self, tileset_name: str, palettes: List):
        """Cache palettes with LRU eviction when cache is full."""
        # Remove if already exists (will be re-added at end)
        if tileset_name in self._palette_cache:
            del self._palette_cache[tileset_name]
        
        # Evict least recently used if cache is full
        if len(self._palette_cache) >= self.max_cache_size:
            # Remove oldest (first) item
            self._palette_cache.popitem(last=False)
        
        # Add to end (most recently used)
        self._palette_cache[tileset_name] = palettes
    
    def clear_cache(self):
        """Clear all caches. Useful for freeing memory after large batch operations."""
        self._tileset_cache.clear()
        self._palette_cache.clear()
    
    def extract_tile(self, tileset_image: Image.Image, tile_id: int) -> Image.Image:
        """
        Extract a single 8x8 tile from a tileset image.
        
        Args:
            tileset_image: The tileset image (in 'P' mode)
            tile_id: The tile ID (0-based)
        
        Returns:
            8x8 tile image in 'P' mode
        """
        # Calculate tile position in tileset
        tiles_per_row = tileset_image.width // self.tile_size
        tile_x = (tile_id % tiles_per_row) * self.tile_size
        tile_y = (tile_id // tiles_per_row) * self.tile_size
        
        # Extract tile (bounds should already be validated before calling this)
        tile = tileset_image.crop((
            tile_x, tile_y,
            tile_x + self.tile_size, tile_y + self.tile_size
        ))
        
        return tile
    
    def render_metatile(
        self,
        metatile_tiles: List[Tuple[int, int, int]],  # List of (tile_id, flip_flags, palette_index)
        primary_tileset_name: str,
        secondary_tileset_name: str,
        layer_type: MetatileLayerType
    ) -> Tuple[Optional[Image.Image], Optional[Image.Image]]:
        """
        Render a metatile as one or two 16x16 images.
        
        Args:
            metatile_tiles: 8 tiles with (tile_id, flip_flags, palette_index)
            primary_tileset_name: Name of primary tileset
            secondary_tileset_name: Name of secondary tileset
            layer_type: How the metatile should be split across layers
        
        Returns:
            Tuple of (bottom_tile_image, top_tile_image) - both are 16x16 RGBA images
            For NORMAL: (None, full_metatile)
            For COVERED: (full_metatile, None)
            For SPLIT: (bottom_half, top_half) - each with transparent areas
        """
        if len(metatile_tiles) != NUM_TILES_PER_METATILE:
            return None, None
        
        # Split into bottom (0-3) and top (4-7) tiles
        bottom_tiles = metatile_tiles[0:4]  # [tl, tr, bl, br]
        top_tiles = metatile_tiles[4:8]
        
        # Render bottom and top as 2x2 grids of 8x8 tiles (each is 16x16)
        bottom_image = self._render_tile_grid(bottom_tiles, primary_tileset_name, secondary_tileset_name)
        top_image = self._render_tile_grid(top_tiles, primary_tileset_name, secondary_tileset_name)
        
        if layer_type == MetatileLayerType.NORMAL:
            # NORMAL: Bottom 4 tiles -> Bg2, Top 4 tiles -> Bg1
            # Both are full 16x16 images
            return bottom_image, top_image
            
        elif layer_type == MetatileLayerType.COVERED:
            # COVERED: Bottom 4 tiles -> Bg3, Top 4 tiles -> Bg2
            # Both are full 16x16 images
            return bottom_image, top_image
            
        elif layer_type == MetatileLayerType.SPLIT:
            # SPLIT: Bottom 4 tiles -> Bg3, Top 4 tiles -> Bg1
            # Based on field_camera.c:DrawMetatile, SPLIT metatiles don't actually split pixels!
            # Instead, they assign full tiles to different layers:
            # - Tiles 0-3 (bottom row) go to Bg3 (Ground layer) as full 16x16
            # - Tiles 4-7 (top row) go to Bg1 (Overhead layer) as full 16x16
            # - Bg2 (Objects layer) gets empty/transparent tiles
            
            # Ensure we have images (create empty transparent ones if None)
            if bottom_image is None:
                bottom_image = Image.new('RGBA', (self.metatile_size, self.metatile_size), (0, 0, 0, 0))
            if top_image is None:
                top_image = Image.new('RGBA', (self.metatile_size, self.metatile_size), (0, 0, 0, 0))
            
            # Return full images - no pixel-level splitting!
            # bottom_image (tiles 0-3) goes to Bg3
            # top_image (tiles 4-7) goes to Bg1
            return bottom_image, top_image
        else:
            # Default to NORMAL behavior
            return bottom_image, top_image
    
    def _render_tile_grid(
        self,
        tiles: List[Tuple[int, int, int]],  # 4 tiles: [tl, tr, bl, br]
        primary_tileset_name: str,
        secondary_tileset_name: str
    ) -> Optional[Image.Image]:
        """
        Render a 2x2 grid of 8x8 tiles into a 16x16 image.
        
        Args:
            tiles: List of 4 (tile_id, flip_flags, palette_index) tuples
            primary_tileset_name: Name of primary tileset
            secondary_tileset_name: Name of secondary tileset
        
        Returns:
            16x16 RGBA image
        """
        if len(tiles) != 4:
            return None
        
        grid_image = Image.new('RGBA', (self.metatile_size, self.metatile_size), (0, 0, 0, 0))
        
        positions = [
            (0, 0),      # Top-left
            (self.tile_size, 0),  # Top-right
            (0, self.tile_size),  # Bottom-left
            (self.tile_size, self.tile_size)  # Bottom-right
        ]
        
        for idx, (tile_id, flip_flags, palette_index) in enumerate(tiles):
            if tile_id == 0:
                continue  # Skip empty tiles
            
            # Determine which tileset this tile belongs to
            # CRITICAL: In Pokemon Emerald's VRAM system:
            # - VRAM slots 0-511 are ALWAYS filled from the primary tileset
            # - VRAM slots 512-1023 are filled from the secondary tileset (if it has enough tiles)
            # - Tile IDs in metatiles.bin reference VRAM positions directly (0-1023)
            # - If secondary tileset has fewer than 512 tiles, higher VRAM slots (672-1023) remain empty/black
            # - When a metatile references an empty VRAM slot, we fall back to primary tileset
            if tile_id < NUM_TILES_IN_PRIMARY_VRAM:
                # Tile IDs 0-511 always reference primary tileset (VRAM 0-511)
                tileset_name = primary_tileset_name
                actual_tile_id = tile_id
                use_fallback = False
            else:
                # Tile IDs 512+ reference secondary tileset (VRAM 512-1023)
                # But if secondary doesn't have enough tiles, we fall back to General
                tileset_name = secondary_tileset_name
                actual_tile_id = tile_id - NUM_TILES_IN_PRIMARY_VRAM  # Convert VRAM slot to secondary tileset index
                use_fallback = True  # May need to fall back if secondary tileset is too small
            
            # Load tileset image
            tileset_image = self.load_tileset_image(tileset_name)
            if not tileset_image:
                # Tileset not found - if this was a secondary tileset, fall back to primary tileset
                if use_fallback and tileset_name != primary_tileset_name:
                    tileset_name = primary_tileset_name
                    actual_tile_id = tile_id - NUM_TILES_IN_PRIMARY_VRAM  # Keep the same offset
                    tileset_image = self.load_tileset_image(tileset_name)
                    use_fallback = False  # Don't fall back again
                
                if not tileset_image:
                    # Still not found - skip this tile
                    continue
            
            # Validate tile ID is within bounds
            tiles_per_row = tileset_image.width // self.tile_size
            tiles_per_col = tileset_image.height // self.tile_size
            max_tile_id = (tiles_per_row * tiles_per_col) - 1
            
            if actual_tile_id < 0 or actual_tile_id > max_tile_id:
                # Tile ID out of bounds - if this was a secondary tileset, try primary tileset as fallback
                if use_fallback and tileset_name != primary_tileset_name:
                    # Fall back to primary tileset
                    fallback_tileset_image = self.load_tileset_image(primary_tileset_name)
                    if fallback_tileset_image:
                        fallback_tiles_per_row = fallback_tileset_image.width // self.tile_size
                        fallback_tiles_per_col = fallback_tileset_image.height // self.tile_size
                        fallback_max_tile_id = (fallback_tiles_per_row * fallback_tiles_per_col) - 1
                        if 0 <= actual_tile_id <= fallback_max_tile_id:
                            # Primary tileset has this tile - use it
                            tileset_name = primary_tileset_name
                            tileset_image = fallback_tileset_image
                            max_tile_id = fallback_max_tile_id
                            use_fallback = False
                        else:
                            # Still out of bounds even in primary tileset - skip
                            continue
                    else:
                        # Primary tileset not found - skip
                        continue
                else:
                    # No fallback possible - skip this tile
                    continue
            
            # Extract tile (should never fail now that we've validated bounds)
            try:
                tile = self.extract_tile(tileset_image, actual_tile_id)
                if tile is None:
                    continue  # Skip if extraction failed
            except Exception:
                continue  # Skip if extraction failed
            
            # Apply palette
            # CRITICAL: In Pokemon Emerald, palettes are combined in VRAM:
            # - Palette slots 0-5 come from primary tileset palettes 0-5
            # - Palette slots 6-12 come from secondary tileset palettes 6-12
            # The palette_index directly references the palette slot (0-12), so:
            # - If palette_index < 6: use primary tileset's palette[palette_index]
            # - If palette_index >= 6: use secondary tileset's palette[palette_index]
            # This is true regardless of which tileset the tile graphic comes from!
            if palette_index >= 6 and secondary_tileset_name:
                # Use secondary tileset's palette for slots 6-12
                palette_source_tileset = secondary_tileset_name
            else:
                # Use primary tileset's palette for slots 0-5
                palette_source_tileset = primary_tileset_name
            
            palettes = self.load_tileset_palettes_cached(palette_source_tileset)
            if palettes and 0 <= palette_index < len(palettes) and palettes[palette_index]:
                tile = apply_palette_to_tile(tile, palettes[palette_index])
            else:
                # No palette available or invalid palette index - convert to RGBA
                # This is expected for some tilesets, not an error
                tile = tile.convert('RGBA')
            
            # Apply flips
            if flip_flags & FLIP_HORIZONTAL:
                tile = tile.transpose(Image.FLIP_LEFT_RIGHT)
            if flip_flags & FLIP_VERTICAL:
                tile = tile.transpose(Image.FLIP_TOP_BOTTOM)
            
            # Paste into grid
            x, y = positions[idx]
            grid_image.paste(tile, (x, y), tile)
        
        return grid_image

