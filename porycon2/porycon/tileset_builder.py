"""
Tileset builder - creates complete tilesets from all used tiles.

The goal is to create tilesets that contain all individual tiles (not metatiles)
so maps can be edited in Tiled without metatile constraints.
"""

from typing import Set, Dict, List, Tuple, Optional, Any
from pathlib import Path
import json
from PIL import Image
from .palette_loader import load_tileset_palettes, apply_palette_to_tile
from .animation_scanner import AnimationScanner
from .utils import camel_to_snake, TilesetPathResolver
from .logging_config import get_logger

logger = get_logger('tileset_builder')


class TilesetBuilder:
    """Builds complete tilesets by collecting all used tiles from maps."""
    
    def __init__(self, input_dir: str):
        self.input_dir = Path(input_dir)
        self.used_tiles: Dict[str, Set[int]] = {}  # tileset_name -> set of tile IDs
        self.used_tiles_with_palettes: Dict[str, Set[Tuple[int, int]]] = {}  # tileset_name -> set of (tile_id, palette) tuples
        self.tileset_info: Dict[str, Dict] = {}  # tileset_name -> tileset metadata
        self.animation_scanner = AnimationScanner(input_dir)
        # Track primary/secondary tileset relationships: tileset_name -> (primary_tileset, secondary_tileset) pairs
        # A tileset can be primary in some maps and secondary in others, so we track all pairs
        self.tileset_relationships: Dict[str, Set[Tuple[str, str]]] = {}  # tileset_name -> set of (primary, secondary) pairs
    
    def add_tiles(self, tileset_name: str, tile_ids: List[int]):
        """Record that these tiles are used in this tileset."""
        if tileset_name not in self.used_tiles:
            self.used_tiles[tileset_name] = set()
        self.used_tiles[tileset_name].update(tile_ids)
    
    def add_tiles_with_palettes(self, tileset_name: str, tile_palette_pairs: List[Tuple[int, int]]):
        """Record tiles with their palette indices."""
        if tileset_name not in self.used_tiles_with_palettes:
            self.used_tiles_with_palettes[tileset_name] = set()
        self.used_tiles_with_palettes[tileset_name].update(tile_palette_pairs)
        # Debug: verify we're storing palette info
        if len(tile_palette_pairs) > 0:
            unique_palettes = set(p[1] for p in tile_palette_pairs)
            if len(unique_palettes) > 1 or (len(unique_palettes) == 1 and 0 not in unique_palettes):
                # Only print if we have non-zero palettes to avoid spam
                pass  # Don't print here, too verbose
    
    def record_tileset_relationship(self, primary_tileset: str, secondary_tileset: str):
        """Record that a primary and secondary tileset are used together."""
        # Record for primary tileset
        if primary_tileset not in self.tileset_relationships:
            self.tileset_relationships[primary_tileset] = set()
        self.tileset_relationships[primary_tileset].add((primary_tileset, secondary_tileset))
        
        # Record for secondary tileset
        if secondary_tileset not in self.tileset_relationships:
            self.tileset_relationships[secondary_tileset] = set()
        self.tileset_relationships[secondary_tileset].add((primary_tileset, secondary_tileset))
    
    def load_tileset_graphics(self, tileset_name: str) -> Optional[Image.Image]:
        """
        Load tileset graphics from pokeemerald.
        
        Tilesets are in:
        - data/tilesets/primary/{tileset_name}/tiles.png
        - data/tilesets/secondary/{tileset_name}/tiles.png
        """
        # Use TilesetPathResolver for path resolution
        resolver = TilesetPathResolver(self.input_dir)
        tileset_path = resolver.find_tileset_image_path(tileset_name)
        
        if tileset_path and tileset_path.exists():
            img = Image.open(tileset_path)
            # Keep as indexed color (P mode) so we can apply palettes later
            # Don't convert to RGBA here - that happens when applying palettes
            # Ensure the image is in 'P' mode (it should be, but verify)
            if img.mode != 'P':
                # If it's not in P mode, try to convert it
                # This shouldn't happen for valid tileset images
                logger.warning(f"{tileset_name} tiles.png is not in 'P' mode (got {img.mode}), converting...")
                img = img.convert('P')
            return img
        
        return None
    
    def build_tileset_image(
        self,
        tileset_name: str,
        tile_size: int = 8,
        tiles_per_row: int = 16
    ) -> Tuple[Image.Image, Dict[Tuple[int, int], int]]:
        """
        Build a complete tileset image containing unique tiles for each (tile_id, palette) combination.
        
        Returns:
            (image, tile_mapping) where tile_mapping maps (old_tile_id, palette_index) -> new_tile_id
        """
        source_image = self.load_tileset_graphics(tileset_name)
        if source_image is None:
            # Create empty tileset if source not found
            used_count = len(self.used_tiles_with_palettes.get(tileset_name, set()))
            if used_count == 0:
                used_count = 1  # At least one tile
            
            cols = min(tiles_per_row, used_count)
            rows = (used_count + tiles_per_row - 1) // tiles_per_row
            image = Image.new('RGBA', (cols * tile_size, rows * tile_size), (0, 0, 0, 0))
            return image, {}
        
        # Get tileset directory for loading palettes
        resolver = TilesetPathResolver(self.input_dir)
        result = resolver.find_tileset_path(tileset_name)
        tileset_dir = result[1] if result else None
        
        # Load palettes from current tileset
        palettes = None
        if tileset_dir:
            palettes = load_tileset_palettes(tileset_dir)
            # Debug: check if palettes loaded
            if palettes:
                loaded_count = sum(1 for p in palettes if p is not None)
                logger.debug(f"Loaded {loaded_count}/16 palettes from {tileset_dir}")
                # Validate palette sizes
                for i, p in enumerate(palettes):
                    if p and len(p) < 16:
                        logger.warning(f"Palette {i:02d}.pal has only {len(p)} colors (expected 16)")
            else:
                logger.warning(f"No palettes loaded from {tileset_dir}")
        
        # CRITICAL: In Pokemon Emerald, palettes are combined in VRAM:
        # - Palette slots 0-5 come from primary tileset palettes 0-5
        # - Palette slots 6-12 come from secondary tileset palettes 6-12
        # For secondary tilesets, we need to load palettes from the primary tileset for indices 0-5
        # Determine which tileset is the primary tileset for this tileset
        primary_tileset_name = None
        primary_palettes = None
        
        # Check if this tileset is used as a secondary tileset (paired with a primary)
        if tileset_name in self.tileset_relationships:
            # Find the primary tileset this tileset is paired with
            # If this tileset appears as secondary in any relationship, use that primary
            for primary, secondary in self.tileset_relationships[tileset_name]:
                if secondary == tileset_name and primary != tileset_name:
                    primary_tileset_name = primary
                    break
            # If not found as secondary, check if it's always primary (paired with itself or no secondary)
            if primary_tileset_name is None:
                # This tileset is always used as primary, so it uses its own palettes for 0-5
                primary_tileset_name = tileset_name
        
        # Load primary tileset palettes if we found a primary tileset and it's different from current
        if primary_tileset_name and primary_tileset_name != tileset_name and (source_image is not None or tileset_dir is not None):
            primary_result = resolver.find_tileset_path(primary_tileset_name)
            if primary_result:
                primary_dir = primary_result[1]
                primary_palettes = load_tileset_palettes(primary_dir)
                if primary_palettes:
                    loaded_count = sum(1 for p in primary_palettes if p is not None)
                    logger.debug(f"Loaded {loaded_count}/16 palettes from {primary_tileset_name} tileset for palette indices 0-5")
        
        # Get used tiles with palettes, or fall back to tiles without palettes
        used_with_palettes = sorted(self.used_tiles_with_palettes.get(tileset_name, set()))
        if not used_with_palettes:
            # Debug: check if tileset exists with different case
            matching_keys = [k for k in self.used_tiles_with_palettes.keys() if k.lower() == tileset_name.lower()]
            if matching_keys:
                logger.debug(f"Found {tileset_name} with different case: {matching_keys[0]}")
                used_with_palettes = sorted(self.used_tiles_with_palettes.get(matching_keys[0], set()))
            else:
                # Fallback: use tiles without palette info (assume palette 0)
                # This means palette info wasn't extracted during conversion
                used_tiles = sorted(self.used_tiles.get(tileset_name, set()))
                used_with_palettes = [(tile_id, 0) for tile_id in used_tiles]
                logger.warning(f"No palette info found for {tileset_name}, using palette 0 for all tiles")
                # Debug: show available tileset names
                if len(self.used_tiles_with_palettes) > 0:
                    sample_keys = list(self.used_tiles_with_palettes.keys())[:5]
                    logger.debug(f"Available tilesets with palette info: {sample_keys}...")
        
        if not used_with_palettes:
            # No tiles used, return empty
            image = Image.new('RGBA', (tile_size, tile_size), (0, 0, 0, 0))
            return image, {}
        
        # Calculate dimensions
        num_tiles = len(used_with_palettes)
        cols = min(tiles_per_row, num_tiles)
        rows = (num_tiles + tiles_per_row - 1) // tiles_per_row
        
        # Create new image
        new_image = Image.new('RGBA', (cols * tile_size, rows * tile_size), (0, 0, 0, 0))
        
        # Source image dimensions
        source_width = source_image.width
        source_height = source_image.height
        source_cols = source_width // tile_size
        source_rows = source_height // tile_size
        source_total_tiles = source_cols * source_rows
        
        # Debug: log tileset info
        logger.debug(f"Source image: {source_width}x{source_height}, {source_cols}x{source_rows} tiles, total: {source_total_tiles}")
        if used_with_palettes:
            tile_ids = [t[0] for t in used_with_palettes]
            palette_indices = [t[1] for t in used_with_palettes]
            unique_palettes = set(palette_indices)
            logger.debug(f"Used tile IDs: {len(used_with_palettes)} unique (tile_id, palette) combinations, tile range: {min(tile_ids)}-{max(tile_ids)}")
            logger.debug(f"Palette indices used: {sorted(unique_palettes)}")
        
        # Map (tile_id, palette) to new tile IDs
        tile_mapping: Dict[Tuple[int, int], int] = {}
        skipped_tiles = []
        
        # Keep source image in original format for palette application
        # load_tileset_graphics now preserves 'P' mode, so this should already be indexed color
        source_image_p = source_image
        
        black_tile_count = 0
        for idx, (old_tile_id, palette_index) in enumerate(used_with_palettes):
            new_tile_id = idx + 1  # Tiled uses 1-based indexing (0 = empty)
            tile_mapping[(old_tile_id, palette_index)] = new_tile_id
            
            # Check if tile ID is within bounds
            if old_tile_id >= source_total_tiles:
                skipped_tiles.append((old_tile_id, palette_index))
                # Create empty/transparent tile
                tile = Image.new('RGBA', (tile_size, tile_size), (0, 0, 0, 0))
            else:
                # Calculate position in source image
                source_tile_x = old_tile_id % source_cols
                source_tile_y = old_tile_id // source_cols
                
                # Verify bounds
                if source_tile_y >= source_rows:
                    skipped_tiles.append((old_tile_id, palette_index))
                    tile = Image.new('RGBA', (tile_size, tile_size), (0, 0, 0, 0))
                else:
                    # Extract tile from source
                    source_x = source_tile_x * tile_size
                    source_y = source_tile_y * tile_size
                    tile = source_image_p.crop((
                        source_x, source_y,
                        source_x + tile_size, source_y + tile_size
                    ))
                    
                    # Apply palette if available
                    # The tile is in 'P' mode with pixel values 0-15 (palette indices)
                    # We need to apply the correct .pal file based on palette_index from metatiles.bin
                    # CRITICAL: Palette indices 0-5 come from primary tileset
                    #           Palette indices 6-12 come from secondary tileset (current tileset)
                    #           When building the primary tileset itself, use its own palettes for all indices
                    palette_to_use = None
                    if palette_index < 6:
                        # Palette indices 0-5: use primary tileset palettes
                        if primary_tileset_name == tileset_name:
                            # When building the primary tileset, use its own palettes
                            if palettes and 0 <= palette_index < len(palettes) and palettes[palette_index]:
                                palette_to_use = palettes[palette_index]
                        elif primary_palettes is not None:
                            # For secondary tilesets, use primary tileset palettes if available
                            if 0 <= palette_index < len(primary_palettes) and primary_palettes[palette_index]:
                                palette_to_use = primary_palettes[palette_index]
                            # Fallback: if primary palettes not available, try current tileset's palettes
                            elif palettes and 0 <= palette_index < len(palettes) and palettes[palette_index]:
                                palette_to_use = palettes[palette_index]
                        else:
                            # No primary palettes loaded (might be per-map tileset or no relationship recorded), use current tileset's palettes
                            if palettes and 0 <= palette_index < len(palettes) and palettes[palette_index]:
                                palette_to_use = palettes[palette_index]
                    else:
                        # Palette indices 6-12: use secondary tileset (current tileset) palettes
                        # For the primary tileset, this also uses its own palettes
                        if palettes and 0 <= palette_index < len(palettes) and palettes[palette_index]:
                            palette_to_use = palettes[palette_index]
                    
                    if palette_to_use:
                        if tile.mode == 'P':
                            # Apply the palette from .pal file
                            # The pixel values in the tile are color indices (0-15) within that palette
                            tile = apply_palette_to_tile(tile, palette_to_use)
                            # Check if tile is mostly black (might indicate palette issue)
                            if tile.mode == 'RGBA':
                                pixels = tile.load()
                                black_count = 0
                                total_pixels = tile.width * tile.height
                                for y in range(tile.height):
                                    for x in range(tile.width):
                                        r, g, b, a = pixels[x, y]
                                        # Count black pixels (but not transparent)
                                        if a > 0 and r == 0 and g == 0 and b == 0:
                                            black_count += 1
                                if black_count > total_pixels * 0.5:  # More than 50% black
                                    black_tile_count += 1
                        elif tile.mode != 'RGBA':
                            tile = tile.convert('RGBA')
                    else:
                        # No palette available or invalid palette_index
                        # Convert to RGBA using embedded palette from source image
                        # This should only happen if palettes weren't loaded or palette_index is invalid
                        if tile.mode == 'P':
                            # Use the embedded palette from the source image
                            # PIL will automatically handle transparency if present
                            tile = tile.convert('RGBA')
                            # Debug: log when we're using fallback
                            if idx < 5:  # Only log first few to avoid spam
                                reason = "palettes not loaded" if not palettes else f"palette_index {palette_index} out of range" if palette_index >= len(palettes) else f"palette {palette_index} is None"
                                logger.debug(f"Using embedded palette for tile {old_tile_id} (reason: {reason})")
                        elif tile.mode != 'RGBA':
                            tile = tile.convert('RGBA')
            
            # Calculate position in new image
            new_tile_x = idx % cols
            new_tile_y = idx // cols
            
            # Paste into new image
            # For RGBA images, we need to use the alpha channel as a mask to preserve transparency
            dest_x = new_tile_x * tile_size
            dest_y = new_tile_y * tile_size
            if tile.mode == 'RGBA':
                # Use the alpha channel as a mask to preserve transparency
                new_image.paste(tile, (dest_x, dest_y), tile)
            else:
                # For non-RGBA images, paste normally
                new_image.paste(tile, (dest_x, dest_y))
        
        if skipped_tiles:
            logger.warning(f"{len(skipped_tiles)} tile IDs out of bounds (max: {source_total_tiles-1}): {skipped_tiles[:10]}...")
        if black_tile_count > 0:
            logger.warning(f"{black_tile_count} tiles appear mostly black (may indicate palette issues)")
        
        return new_image, tile_mapping
    
    def add_animation_tiles_to_image(
        self,
        tileset_name: str,
        base_image: Image.Image,
        tile_mapping: Dict[Tuple[int, int], int],
        tile_size: int = 8,
        tiles_per_row: int = 16
    ) -> Tuple[Image.Image, Dict[int, int]]:
        """
        Add animation frame tiles to the tileset image.
        
        Returns:
            (updated_image, animation_tile_mapping) where animation_tile_mapping maps
            animation_tile_index -> new_tile_id
        """
        # Extract all animation data
        anim_data = self.animation_scanner.extract_all_animation_tiles(tileset_name, tile_size)
        
        if not anim_data:
            return base_image, {}
        
        # Calculate current image dimensions
        current_cols = base_image.width // tile_size
        current_rows = base_image.height // tile_size
        current_tile_count = current_cols * current_rows
        
        # Collect all animation tiles
        animation_tiles = []  # List of (anim_name, tile_offset, frame_idx, tile_image)
        animation_tile_mapping = {}  # animation_tile_index -> new_tile_id
        
        for anim_name, anim_info in anim_data.items():
            frames = anim_info["frames"]
            base_tile_id = anim_info["base_tile_id"]
            num_tiles = anim_info["num_tiles"]
            
            for frame_idx, frame_tiles in enumerate(frames):
                for tile_offset in range(min(num_tiles, len(frame_tiles))):
                    tile_image = frame_tiles[tile_offset]
                    # Create unique index for this animation tile
                    anim_tile_index = base_tile_id * 10000 + frame_idx * 1000 + tile_offset
                    animation_tiles.append((anim_name, tile_offset, frame_idx, tile_image, anim_tile_index))
        
        if not animation_tiles:
            return base_image, {}
        
        # Calculate new image dimensions
        total_tiles = current_tile_count + len(animation_tiles)
        new_cols = max(current_cols, min(tiles_per_row, total_tiles))
        new_rows = (total_tiles + new_cols - 1) // new_cols
        
        # Create new image
        new_image = Image.new('RGBA', (new_cols * tile_size, new_rows * tile_size), (0, 0, 0, 0))
        
        # Copy existing tiles
        new_image.paste(base_image, (0, 0))
        
        # Add animation tiles
        for idx, (anim_name, tile_offset, frame_idx, tile_image, anim_tile_index) in enumerate(animation_tiles):
            new_tile_id = current_tile_count + idx + 1  # Tiled uses 1-based indexing
            animation_tile_mapping[anim_tile_index] = new_tile_id
            
            # Calculate position
            tile_x = new_tile_id % new_cols
            tile_y = new_tile_id // new_cols
            
            # Paste tile
            dest_x = tile_x * tile_size
            dest_y = tile_y * tile_size
            if tile_image.mode == 'RGBA':
                new_image.paste(tile_image, (dest_x, dest_y), tile_image)
            else:
                new_image.paste(tile_image, (dest_x, dest_y))
        
        logger.debug(f"Added {len(animation_tiles)} animation tiles to {tileset_name}")
        
        return new_image, animation_tile_mapping
    
    def create_tiled_tileset(
        self,
        tileset_name: str,
        output_dir: str,
        region: str = "hoenn",
        tile_size: int = 8
    ) -> Dict[str, Any]:
        """
        Create a Tiled tileset JSON file.
        
        Returns:
            Tiled tileset JSON structure
        """
        # Build tileset image
        image, tile_mapping = self.build_tileset_image(tileset_name, tile_size)
        
        # Add animation tiles to the image
        animation_tile_mapping = {}
        if image:
            image, animation_tile_mapping = self.add_animation_tiles_to_image(
                tileset_name, image, tile_mapping, tile_size
            )
        
        # Save image
        image_filename = f"{tileset_name.lower()}.png"
        image_path = Path(output_dir) / "Tilesets" / region / image_filename
        image_path.parent.mkdir(parents=True, exist_ok=True)
        image.save(str(image_path))
        
        # Get tilecount from the built image (includes animation tiles)
        # Calculate from image dimensions
        image_cols = image.width // tile_size
        image_rows = image.height // tile_size
        tilecount = image_cols * image_rows
        
        # Also get total tiles from source image
        # This is stored for firstgid calculations (we need the full source tileset size)
        source_image = self.load_tileset_graphics(tileset_name)
        source_total_tiles = None
        if source_image:
            source_width = source_image.width
            source_height = source_image.height
            source_cols = source_width // tile_size
            source_rows = source_height // tile_size
            source_total_tiles = source_cols * source_rows
        
        # Store source_total_tiles in tileset JSON for firstgid calculations
        # The tilecount field is the number of unique tiles in the built image
        # But we also need source_total_tiles for calculating firstgid of next tileset
        
        # Calculate columns (Tiled prefers square-ish layouts)
        columns = min(16, tilecount)
        
        # Create tileset JSON
        tileset = {
            "columns": columns,
            "image": image_filename,
            "imageheight": image.height,
            "imagewidth": image.width,
            "margin": 0,
            "name": tileset_name.lower(),
            "spacing": 0,
            "tilecount": tilecount,  # Number of unique tiles in built image
            "tileheight": tile_size,
            "tilewidth": tile_size,
            "type": "tileset",
            "version": "1.11",
            "tiledversion": "1.11.2"
        }
        
        # Store source_total_tiles as a custom property for firstgid calculations
        properties = []
        if source_total_tiles is not None:
            properties.append({
                "name": "_source_total_tiles",
                "type": "int",
                "value": source_total_tiles
            })
        
        # Build animation data
        animations = self.animation_scanner.build_animation_data(
            tileset_name, tile_mapping, animation_tile_mapping, tile_size
        )
        
        # Add tiles array with animations if we have any
        if animations:
            tiles = []
            for anim in animations:
                tiles.append(anim)
            tileset["tiles"] = tiles
            logger.debug(f"Added {len(animations)} animations to {tileset_name}")
        else:
            # Debug: check if animations should exist for this tileset
            anim_defs = self.animation_scanner.get_animations_for_tileset(tileset_name)
            if anim_defs:
                logger.debug(f"Found {len(anim_defs)} animation definitions for {tileset_name} but no animations were created")
                logger.debug(f"Animation definitions: {list(anim_defs.keys())}")
                logger.debug(f"Tile mapping has {len(tile_mapping)} entries")
                logger.debug(f"Animation tile mapping has {len(animation_tile_mapping)} entries")
        
        # Add properties if we have any
        if properties:
            tileset["properties"] = properties
        
        # Save tileset JSON
        tileset_path = Path(output_dir) / "Tilesets" / region / f"{tileset_name.lower()}.json"
        tileset_path.parent.mkdir(parents=True, exist_ok=True)
        with open(tileset_path, 'w', encoding='utf-8') as f:
            json.dump(tileset, f, indent=2)
        
        return tileset, tile_mapping
    
    def get_tileset_path(self, tileset_name: str, region: str = "hoenn") -> str:
        """Get relative path to tileset JSON from map files."""
        return f"../../Tilesets/{region}/{tileset_name.lower()}.json"

