"""
Worker function for parallel map conversion.
This module exists separately so it can be properly pickled for multiprocessing.
"""

from pathlib import Path


def convert_single_map(args_tuple):
    """Convert a single map - designed for parallel execution."""
    map_id, map_info, input_dir, output_dir, layouts_dict, region_override, warp_lookup = args_tuple
    
    try:
        from .converter import MapConverter
        from .utils import load_json
        
        # Create a converter instance for this worker
        local_converter = MapConverter(str(input_dir), str(output_dir))
        
        # Use --region argument if provided, otherwise use region from map data
        region = region_override if region_override else map_info.get("region", "hoenn")
        
        map_data = load_json(map_info["map_file"])
        layout_id = map_info["layout_id"]
        
        if not layout_id:
            return ("skipped", map_id, "No layout_id", None, None, {}, {})
        
        if layout_id not in layouts_dict:
            return ("skipped_layout", map_id, f"Layout {layout_id} not found", None, None, {}, {})
        
        layout = layouts_dict[layout_id]
        map_bin = layout.get("map_bin")
        if not map_bin or not Path(map_bin).exists():
            return ("skipped", map_id, f"map.bin not found", None, None, {}, {})
        
        # Use new metatile-based conversion
        try:
            tiled_map = local_converter.convert_map_with_metatiles(map_id, map_data, layouts_dict, region, warp_lookup)
        except Exception as e:
            import traceback
            tb_str = traceback.format_exc()
            # Extract the file and line number from traceback
            tb_lines = tb_str.split('\n')
            error_location = None
            for i, line in enumerate(tb_lines):
                if 'File "' in line and ('metatile' in line.lower() or 'converter' in line.lower()):
                    # Get the next line which should have the actual code
                    if i + 1 < len(tb_lines):
                        error_location = f"{line.strip()} -> {tb_lines[i+1].strip()}"
                        break
            error_details = f"{type(e).__name__}: {str(e)}"
            if error_location:
                error_details += f"\n  Location: {error_location}"
            return ("error", map_id, error_details, None, None, {}, {})
        
        if tiled_map:
            map_name = map_id.replace("MAP_", "").lower()
            local_converter.save_map(map_id, tiled_map, region, map_data)
            
            # With per-map tilesets, we don't need to collect used_tiles anymore
            # Each map has its own tileset
            used_tiles_dict = {}
            used_tiles_with_palettes_dict = {}
            
            # Return data for world builder
            connections = map_data.get("connections", [])
            return ("success", map_id, None, {
                "map_id": map_id,
                "map_name": map_name,
                "region": region,
                "connections": connections,
                "width": tiled_map["width"],
                "height": tiled_map["height"],
                "map_data": map_data
            }, tiled_map, used_tiles_dict, used_tiles_with_palettes_dict)
        else:
            # Try to get more specific error information
            layout_id = map_info.get("layout_id", "unknown")
            layout = layouts_dict.get(layout_id, {})
            map_bin = layout.get("map_bin", "unknown")
            error_msg = f"convert_map_with_metatiles returned None (layout={layout_id}, map_bin={map_bin})"
            return ("failed", map_id, error_msg, None, None, {}, {})
    
    except Exception as e:
        import traceback
        error_details = f"{type(e).__name__}: {str(e)}"
        return ("error", map_id, error_details, None, None, {}, {})

