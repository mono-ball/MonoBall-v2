"""
Map reader - handles reading and parsing map files from pokeemerald format.

This module extracts file I/O and parsing logic from MapConverter to improve
testability and separation of concerns.
"""

import struct
from pathlib import Path
from typing import Dict, List, Tuple, Optional
from .utils import TilesetPathResolver
from .logging_config import get_logger

logger = get_logger('map_reader')


class MapReader:
    """Handles reading and parsing map files from pokeemerald format."""
    
    def __init__(self, input_dir: Path):
        """
        Initialize MapReader.
        
        Args:
            input_dir: Path to pokeemerald root directory
        """
        self.input_dir = Path(input_dir)
        self.path_resolver = TilesetPathResolver(self.input_dir)
    
    def read_map_bin(self, map_bin_path: Path, width: int, height: int) -> List[List[int]]:
        """
        Read a map.bin file containing metatile data.
        
        Format: Each entry is u16 with:
        - Bits 0-9: Metatile ID
        - Bits 10-11: Collision
        - Bits 12-15: Elevation
        
        Args:
            map_bin_path: Path to map.bin file
            width: Expected map width in metatiles
            height: Expected map height in metatiles
        
        Returns:
            2D list [y][x] of metatile entries (u16 values)
        
        Raises:
            ValueError: If file size doesn't match expected dimensions
        """
        if not map_bin_path.exists():
            raise FileNotFoundError(f"map.bin not found: {map_bin_path}")
        
        with open(map_bin_path, 'rb') as f:
            data = f.read()
        
        # Each entry is 2 bytes (u16)
        entries = []
        for i in range(0, len(data), 2):
            if i + 1 < len(data):
                entry = struct.unpack('<H', data[i:i+2])[0]
                entries.append(entry)
        
        # Reshape to 2D [y][x]
        if len(entries) != width * height:
            raise ValueError(f"Expected {width * height} entries, got {len(entries)}")
        
        map_data = []
        for y in range(height):
            row = []
            for x in range(width):
                idx = y * width + x
                row.append(entries[idx])
            map_data.append(row)
        
        return map_data
    
    def read_metatile_attributes(self, tileset_name: str) -> Dict[int, int]:
        """
        Load metatile attributes to get layer types.
        
        Format: u16 per metatile
        - Bits 0-7: Behavior
        - Bits 12-15: Layer Type
        
        Args:
            tileset_name: Name of the tileset (e.g., "General")
        
        Returns:
            Dictionary mapping metatile_id -> layer_type (0-15)
        """
        # Get tileset directory
        result = self.path_resolver.find_tileset_path(tileset_name)
        if not result:
            logger.warning(f"Tileset '{tileset_name}' not found")
            return {}
        
        category, tileset_dir = result
        attributes_path = tileset_dir / "metatile_attributes.bin"
        
        if not attributes_path.exists():
            logger.debug(f"metatile_attributes.bin not found for {tileset_name} at {attributes_path}")
            return {}
        
        try:
            with open(attributes_path, 'rb') as f:
                data = f.read()
            
            attributes = {}
            for i in range(0, len(data), 2):
                if i + 1 < len(data):
                    attr = struct.unpack('<H', data[i:i+2])[0]
                    metatile_id = i // 2
                    # Extract layer type (bits 12-15)
                    layer_type = (attr >> 12) & 0x0F
                    attributes[metatile_id] = layer_type
            
            return attributes
        except Exception as e:
            logger.warning(f"Error reading {attributes_path}: {e}")
            return {}
    
    def read_metatiles_with_attributes(self, tileset_name: str) -> List[Tuple[int, int, int]]:
        """
        Load metatiles with full tile attributes (tile_id, flip_flags, palette_index).
        
        Args:
            tileset_name: Name of the tileset (e.g., "General")
        
        Returns:
            List of tuples: (tile_id, flip_flags, palette_index)
            - tile_id: bits 0-9 (0-1023)
            - flip_flags: bits 10-11 (0-3: 0=none, 1=h, 2=v, 3=hv)
            - palette_index: bits 12-15 (0-15)
        """
        # Get tileset directory
        result = self.path_resolver.find_tileset_path(tileset_name)
        if not result:
            logger.warning(f"Tileset '{tileset_name}' not found")
            return []
        
        category, tileset_dir = result
        metatiles_path = tileset_dir / "metatiles.bin"
        
        if not metatiles_path.exists():
            logger.warning(f"metatiles.bin not found for {tileset_name} at {metatiles_path}")
            return []
        
        try:
            with open(metatiles_path, 'rb') as f:
                data = f.read()
            
            metatiles = []
            for i in range(0, len(data), 2):
                if i + 1 < len(data):
                    tile_attr = struct.unpack('<H', data[i:i+2])[0]
                    # Extract components
                    tile_id = tile_attr & 0x3FF  # Bits 0-9
                    flip_flags = (tile_attr >> 10) & 0x3  # Bits 10-11
                    palette_index = (tile_attr >> 12) & 0xF  # Bits 12-15
                    metatiles.append((tile_id, flip_flags, palette_index))
            
            if len(metatiles) > 0:
                # Debug: show sample palette indices
                sample_palettes = [m[2] for m in metatiles[:20]]
                unique_palettes = set(sample_palettes)
                if len(unique_palettes) > 1:
                    logger.debug(f"Loaded {len(metatiles)} metatiles with attributes for {tileset_name}, sample palettes: {sorted(unique_palettes)}")
            
            return metatiles
        except Exception as e:
            logger.warning(f"Error reading {metatiles_path}: {e}")
            return []

