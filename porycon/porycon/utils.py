"""
Utility functions for porycon.
"""

import json
import os
import re
from pathlib import Path
from typing import Dict, Any, Optional, Tuple
from .logging_config import get_logger

logger = get_logger('utils')


def load_json(filepath: str) -> Dict[str, Any]:
    """Load a JSON file."""
    with open(filepath, 'r', encoding='utf-8') as f:
        return json.load(f)


def save_json(data: Dict[str, Any], filepath: str, indent: int = 2) -> None:
    """Save data to a JSON file."""
    os.makedirs(os.path.dirname(filepath), exist_ok=True)
    with open(filepath, 'w', encoding='utf-8') as f:
        json.dump(data, f, indent=indent, ensure_ascii=False)


def find_map_files(input_dir: str) -> Dict[str, Dict[str, str]]:
    """
    Find all map.json files in pokeemerald data/maps structure.
    
    Returns:
        Dict mapping map_id -> {
            'map_file': path to map.json,
            'layout_id': layout ID from map.json,
            'region': inferred from directory structure
        }
    """
    maps = {}
    maps_dir = Path(input_dir) / "data" / "maps"
    
    if not maps_dir.exists():
        return maps
    
    for map_dir in maps_dir.iterdir():
        if not map_dir.is_dir():
            continue
        
        map_file = map_dir / "map.json"
        if not map_file.exists():
            continue
        
        try:
            map_data = load_json(str(map_file))
            map_id = map_data.get("id", "")
            layout_id = map_data.get("layout", "")
            
            # Infer region from map data
            # region_map_section is like "MAPSEC_MAUVILLE_CITY" or "MAPSEC_ROUTE_101"
            # For Pokemon Emerald, all maps are in Hoenn region by default
            # But we can extract more specific region info if needed
            region_map_section = map_data.get("region_map_section", "")
            
            # Try to extract region from region_map_section
            # Most are in Hoenn, but we can be more specific
            if region_map_section:
                # Extract base region name (e.g., "hoenn" from "MAPSEC_MAUVILLE_CITY")
                # For now, default to "hoenn" for all Pokemon Emerald maps
                region = "hoenn"
            else:
                region = "hoenn"  # Default
            
            maps[map_id] = {
                'map_file': str(map_file),
                'layout_id': layout_id,
                'region': region,
                'name': map_data.get("name", "")
            }
        except Exception as e:
            logger.warning(f"Failed to load {map_file}: {e}")
            continue
    
    return maps


def find_layout_files(input_dir: str) -> Dict[str, Dict[str, Any]]:
    """
    Find all layout data from layouts.json and map.bin files.
    
    Args:
        input_dir: Absolute path to pokeemerald root directory
    """
    """
    Find all layout data from layouts.json and map.bin files.
    
    Returns:
        Dict mapping layout_id -> {
            'width': int,
            'height': int,
            'primary_tileset': str,
            'secondary_tileset': str,
            'map_bin': path to map.bin,
            'border_bin': path to border.bin
        }
    """
    layouts = {}
    input_path = Path(input_dir).resolve()  # Ensure absolute path
    layouts_json = input_path / "data" / "layouts" / "layouts.json"
    
    if not layouts_json.exists():
        logger.warning(f"layouts.json not found at {layouts_json}")
        return layouts
    
    layouts_data = load_json(str(layouts_json))
    layouts_list = layouts_data.get("layouts", [])
    
    for layout in layouts_list:
        layout_id = layout.get("id", "")
        if not layout_id:
            continue
        
        # Find map.bin and border.bin files
        blockdata_path = layout.get("blockdata_filepath", "")
        border_path = layout.get("border_filepath", "")
        
        # Convert relative paths to absolute
        # Paths in layouts.json are relative to pokeemerald root
        if blockdata_path:
            # Handle both absolute and relative paths
            blockdata_path_obj = Path(blockdata_path)
            if blockdata_path_obj.is_absolute():
                map_bin = blockdata_path_obj
            else:
                # Relative path - join with input_dir
                map_bin = input_path / blockdata_path
        else:
            map_bin = None
        
        if border_path:
            border_path_obj = Path(border_path)
            if border_path_obj.is_absolute():
                border_bin = border_path_obj
            else:
                border_bin = input_path / border_path
        else:
            border_bin = None
        
        # Store absolute path as string, but check existence
        map_bin_str = None
        border_bin_str = None
        
        if map_bin:
            # Resolve to absolute path
            try:
                map_bin_abs = map_bin.resolve()
                if map_bin_abs.exists():
                    map_bin_str = str(map_bin_abs)
                elif map_bin.exists():
                    # If resolve() fails but exists() works, use absolute() instead
                    map_bin_str = str(map_bin.absolute())
            except (OSError, RuntimeError):
                # If resolve() fails (e.g., path doesn't exist), try absolute()
                if map_bin.exists():
                    map_bin_str = str(map_bin.absolute())
        
        if border_bin:
            try:
                border_bin_abs = border_bin.resolve()
                if border_bin_abs.exists():
                    border_bin_str = str(border_bin_abs)
                elif border_bin.exists():
                    border_bin_str = str(border_bin.absolute())
            except (OSError, RuntimeError):
                if border_bin.exists():
                    border_bin_str = str(border_bin.absolute())
        
        layouts[layout_id] = {
            'width': layout.get("width", 0),
            'height': layout.get("height", 0),
            'primary_tileset': layout.get("primary_tileset", ""),
            'secondary_tileset': layout.get("secondary_tileset", ""),
            'map_bin': map_bin_str,
            'border_bin': border_bin_str,
        }
    
    return layouts


def sanitize_filename(name: str) -> str:
    """Convert a name to a safe filename."""
    # Replace invalid characters
    invalid_chars = '<>:"/\\|?*'
    for char in invalid_chars:
        name = name.replace(char, '_')
    return name


def get_tileset_name(tileset_id: str) -> str:
    """Extract tileset name from ID like 'gTileset_General' -> 'General'."""
    if tileset_id.startswith("gTileset_"):
        return tileset_id.replace("gTileset_", "")
    return tileset_id


def camel_to_snake(name: str) -> str:
    """
    Convert CamelCase to snake_case (e.g., 'InsideShip' -> 'inside_ship').
    
    Args:
        name: CamelCase string to convert
    
    Returns:
        snake_case string
    """
    # Insert underscore before uppercase letters (except the first one)
    s1 = re.sub('(.)([A-Z][a-z]+)', r'\1_\2', name)
    # Insert underscore before uppercase letters that follow lowercase
    s2 = re.sub('([a-z0-9])([A-Z])', r'\1_\2', s1)
    return s2.lower()


def create_map_definition_dto(
    map_id: str,
    map_name: str,
    region: str,
    map_data: Dict[str, Any]
) -> Dict[str, Any]:
    """Create a map definition DTO for MonoBallFramework."""
    display_name = map_data.get("name", "")
    if not display_name:
        display_name = map_name.replace("_", " ").title()

    region_capitalized = region.capitalize()

    return {
        "id": f"base:map:{region.lower()}/{map_name}",
        "name": display_name,
        "type": "map",
        "region": region.lower(),
        "description": "",
        "tiledPath": f"Tiled/Regions/{region_capitalized}/{map_name}.json"
    }


def save_map_definition_dto(
    dto: Dict[str, Any],
    output_dir: Path,
    region: str,
    map_name: str
) -> None:
    """Save map definition DTO to Definitions/Maps/Regions directory."""
    region_capitalized = region.capitalize()
    dto_path = output_dir / "Definitions" / "Maps" / "Regions" / region_capitalized / f"{map_name}.json"
    dto_path.parent.mkdir(parents=True, exist_ok=True)
    save_json(dto, str(dto_path))


class TilesetPathResolver:
    """Centralized tileset path resolution."""

    def __init__(self, input_dir: Path):
        """
        Initialize tileset path resolver.

        Args:
            input_dir: Path to pokeemerald root directory
        """
        self.input_dir = Path(input_dir)
    
    def find_tileset_path(self, tileset_name: str) -> Optional[Tuple[str, Path]]:
        """
        Find tileset directory.
        
        Args:
            tileset_name: Name of the tileset (e.g., "General", "InsideShip")
        
        Returns:
            Tuple of (category, path) where category is "primary" or "secondary",
            or None if tileset not found
        """
        # Try different name formats
        name_variants = [
            camel_to_snake(tileset_name),  # InsideShip -> inside_ship
            tileset_name.lower(),  # insideship
            tileset_name.replace("_", "").lower(),  # Remove existing underscores
        ]
        
        for tileset_lower in name_variants:
            # Try primary first
            primary_path = self.input_dir / "data" / "tilesets" / "primary" / tileset_lower
            if primary_path.exists():
                return ("primary", primary_path)
            
            # Try secondary
            secondary_path = self.input_dir / "data" / "tilesets" / "secondary" / tileset_lower
            if secondary_path.exists():
                return ("secondary", secondary_path)
        
        # Try without category (old structure)
        for tileset_lower in name_variants:
            direct_path = self.input_dir / "data" / "tilesets" / tileset_lower
            if direct_path.exists():
                return ("", direct_path)
        
        return None
    
    def find_tileset_image_path(self, tileset_name: str) -> Optional[Path]:
        """
        Find tileset image (tiles.png) path.
        
        Args:
            tileset_name: Name of the tileset
        
        Returns:
            Path to tiles.png, or None if not found
        """
        result = self.find_tileset_path(tileset_name)
        if result:
            _, tileset_dir = result
            tiles_path = tileset_dir / "tiles.png"
            if tiles_path.exists():
                return tiles_path
        return None

