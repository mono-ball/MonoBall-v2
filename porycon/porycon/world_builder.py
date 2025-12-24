"""
World file builder - creates Tiled world files from map connections.
Builds a graph starting from Littleroot Town and following all connections.
"""

import json
from pathlib import Path
from typing import Dict, List, Any, Tuple, Set, Optional
from collections import deque
from .utils import load_json, save_json, sanitize_filename
from .logging_config import get_logger

logger = get_logger('world_builder')


class WorldBuilder:
    """Builds Tiled world files from map connections."""
    
    def __init__(self, output_dir: str):
        self.output_dir = Path(output_dir)
        self.map_data: Dict[str, Dict[str, Any]] = {}  # map_id -> map info
        self.worlds: Dict[str, Dict[str, Any]] = {}  # world_name -> world data
    
    def add_map(
        self,
        map_id: str,
        map_name: str,
        region: str,
        connections: List[Dict[str, Any]],
        map_width: int,
        map_height: int,
        map_data: Dict[str, Any]
    ):
        """
        Register a map for world building.
        
        Args:
            map_id: Map identifier (e.g., "MAP_LITTLEROOT_TOWN")
            map_name: Sanitized map filename
            region: Region name
            connections: List of connection objects from map.json
            map_width: Map width in tiles
            map_height: Map height in tiles
            map_data: Full map data (for accessing warp_events, etc.)
        """
        self.map_data[map_id] = {
            "map_id": map_id,
            "map_name": map_name,
            "region": region,
            "connections": connections,
            "width": map_width,
            "height": map_height,
            "map_data": map_data
        }
    
    def build_world(self, region: str, start_map_id: str = "MAP_LITTLEROOT_TOWN"):
        """
        Build a world graph starting from the specified map.
        
        Args:
            region: Region name
            start_map_id: Map ID to start from (default: Littleroot Town)
        """
        if start_map_id not in self.map_data:
            logger.warning(f"Start map {start_map_id} not found, skipping world build for {region}")
            return
        
        # Count maps in this region
        region_maps = {k: v for k, v in self.map_data.items() if v.get("region") == region}
        logger.info(f"Building world for {region}: {len(region_maps)} maps available, starting from {start_map_id}")
        
        # Build graph starting from start_map_id
        visited: Set[str] = set()
        map_positions: Dict[str, Tuple[int, int]] = {}
        queue = deque([(start_map_id, 0, 0)])  # (map_id, x, y)
        visited.add(start_map_id)
        map_positions[start_map_id] = (0, 0)
        
        missing_connections = []  # Track maps that are referenced but not found
        
        # Direction offsets for connections
        direction_offsets = {
            "up": (0, -1),
            "down": (0, 1),
            "left": (-1, 0),
            "right": (1, 0)
        }
        
        # Grid spacing (in pixels)
        # No spacing - maps are directly adjacent
        fixed_spacing = 0
        
        while queue:
            current_map_id, current_x, current_y = queue.popleft()
            current_map_info = self.map_data[current_map_id]
            
            # Get map connections (only map-to-map connections, not warps)
            connections = current_map_info.get("connections")
            if connections:  # Handle None or empty list
                for conn in connections:
                    connected_map_id = conn.get("map", "")
                    if not connected_map_id:
                        continue
                    if connected_map_id not in self.map_data:
                        # Track missing connections for debugging
                        if len(missing_connections) < 10:  # Only track first 10 to avoid spam
                            missing_connections.append((current_map_id, connected_map_id, conn.get("direction")))
                        continue
                    
                    if connected_map_id in visited:
                        # Already processed this map
                        continue
                    
                    # Calculate position based on connection direction and offset
                    direction = conn.get("direction", "")
                    offset = conn.get("offset", 0)  # Offset in old 8x8 tile units, convert to pixels
                    
                    # Skip special directions like "emerge" that aren't spatial connections
                    if direction not in direction_offsets:
                        continue
                    
                    # Mark as visited only after we've validated the direction
                    visited.add(connected_map_id)
                        
                    dx, dy = direction_offsets[direction]
                    connected_map_info = self.map_data[connected_map_id]
                    
                    # Convert map dimensions to pixels (16x16 tiles)
                    current_width_px = current_map_info["width"] * 16
                    current_height_px = current_map_info["height"] * 16
                    connected_width_px = connected_map_info["width"] * 16
                    connected_height_px = connected_map_info["height"] * 16
                    
                    # Convert offset from old 8x8 tile units to pixels
                    # Offset was in 8x8 tile units, but we're using 16x16 tiles now
                    # So we need to convert: offset was relative to 8px tiles, now relative to 16px tiles
                    # Actually, offset might be in pixels already, or in 8px units
                    # Let's assume it's in 8px units and convert: offset_px = offset * 8
                    offset_px = offset * 8
                    
                    # Find reverse connection to get offset on connected map's side
                    reverse_offset = 0
                    connected_map_data = connected_map_info.get("map_data", {})
                    reverse_connections = connected_map_data.get("connections", [])
                    for rev_conn in reverse_connections:
                        if rev_conn.get("map") == current_map_id:
                            reverse_offset = rev_conn.get("offset", 0) * 8  # Convert to pixels
                            break
                    
                    # Position connected map edge-to-edge, then apply offset alignment
                    if dx > 0:  # Right: place to the right of current map
                        new_x = current_x + current_width_px
                        # Align vertically using offset (offset adjusts where along the edge)
                        new_y = current_y + offset_px - reverse_offset
                    elif dx < 0:  # Left: place to the left of current map
                        new_x = current_x - connected_width_px
                        new_y = current_y + offset_px - reverse_offset
                    else:  # Vertical connection
                        new_x = current_x + offset_px - reverse_offset
                        if dy > 0:  # Down: place below current map
                            new_y = current_y + current_height_px
                        elif dy < 0:  # Up: place above current map
                            new_y = current_y - connected_height_px
                        else:
                            new_y = current_y
                        
                    map_positions[connected_map_id] = (new_x, new_y)
                    queue.append((connected_map_id, new_x, new_y))
        
        # Build world maps list
        world_maps = []
        for map_id in visited:
            if map_id not in map_positions:
                # Skip maps that were referenced but not properly positioned
                continue
            map_info = self.map_data[map_id]
            x, y = map_positions[map_id]
            
            world_maps.append({
                "fileName": f"../Maps/{region}/{map_info['map_name']}.json",
                "height": map_info["height"],
                "width": map_info["width"],
                "x": x,
                "y": y
            })
        
        # Debug output
        total_maps_in_data = len([m for m in self.map_data.values() if m.get("region") == region])
        logger.info(f"World graph built: {len(visited)} maps in connection graph (out of {total_maps_in_data} total maps in region)")
        
        if missing_connections:
            logger.warning(f"{len(missing_connections)} connection references to missing maps (showing first few):")
            for from_map, to_map, direction in missing_connections[:5]:
                logger.warning(f"    {from_map} -> {to_map} ({direction})")
        
        self.worlds[region] = {
            "maps": world_maps
        }
    
    def build_world_file(self, world_name: str) -> Optional[Dict[str, Any]]:
        """Build a Tiled world file structure."""
        if world_name not in self.worlds:
            return None
        
        world = {
            "maps": self.worlds[world_name]["maps"],
            "onlyShowAdjacentMaps": False,
            "type": "world"
        }
        
        return world
    
    def save_world(self, world_name: str):
        """Save world file to output directory."""
        world = self.build_world_file(world_name)
        if world is None:
            return
        
        world_path = self.output_dir / "Definitions" / "Worlds" / f"{world_name}.world"
        world_path.parent.mkdir(parents=True, exist_ok=True)
        save_json(world, str(world_path))
    
    def save_all_worlds(self):
        """Save all world files."""
        for world_name in self.worlds:
            self.save_world(world_name)

