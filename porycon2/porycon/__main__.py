"""
Main entry point for porycon converter.
"""

import argparse
import sys
from pathlib import Path
from concurrent.futures import ProcessPoolExecutor, ThreadPoolExecutor, as_completed
from multiprocessing import cpu_count, set_start_method
from .converter import MapConverter
from .world_builder import WorldBuilder
from .utils import find_map_files, find_layout_files, load_json, save_json
from .tileset_builder import TilesetBuilder
from .map_worker import convert_single_map
from .logging_config import setup_logging, get_logger
from .popup_extractor import extract_popups
from .section_extractor import extract_sections
from .text_window_extractor import extract_text_windows
from .audio_converter import AudioConverter, extract_audio
from .definition_converter import DefinitionConverter

# Set multiprocessing start method to 'spawn' for cross-platform compatibility
# This ensures functions can be pickled correctly when running as a module
try:
    set_start_method('spawn', force=True)
except RuntimeError:
    # Already set, ignore
    pass


def main():
    parser = argparse.ArgumentParser(
        description="Convert Pokemon Emerald maps to PokeSharp entity format (or Tiled with --tiled)"
    )
    parser.add_argument(
        "--input",
        required=True,
        help="Input directory (pokeemerald root)"
    )
    parser.add_argument(
        "--output",
        required=True,
        help="Output directory for Tiled files"
    )
    parser.add_argument(
        "--region",
        default=None,
        help="Region name for organizing output folders (default: use region from map data)"
    )
    parser.add_argument(
        "--verbose", "-v",
        action="store_true",
        help="Show detailed progress information"
    )
    parser.add_argument(
        "--debug", "-d",
        action="store_true",
        help="Show debug information (implies verbose)"
    )
    parser.add_argument(
        "--extract-popups",
        action="store_true",
        help="Extract map popup graphics (backgrounds and outlines) from pokeemerald"
    )
    parser.add_argument(
        "--extract-sections",
        action="store_true",
        help="Extract map section (MAPSEC) definitions and popup theme mappings from pokeemerald"
    )
    parser.add_argument(
        "--extract-text-windows",
        action="store_true",
        help="Extract text window graphics from pokeemerald"
    )
    parser.add_argument(
        "--extract-audio",
        action="store_true",
        help="Extract and convert audio (MIDI to OGG) from pokeemerald"
    )
    parser.add_argument(
        "--audio-music",
        action="store_true",
        default=True,
        help="Include music tracks when extracting audio (default: True)"
    )
    parser.add_argument(
        "--audio-sfx",
        action="store_true",
        default=True,
        help="Include sound effects when extracting audio (default: True)"
    )
    parser.add_argument(
        "--audio-phonemes",
        action="store_true",
        default=False,
        help="Include phoneme tracks when extracting audio (default: False)"
    )
    parser.add_argument(
        "--soundfont",
        type=str,
        default=None,
        help="Path to soundfont file for MIDI conversion (recommended for better quality)"
    )
    parser.add_argument(
        "--list-audio",
        action="store_true",
        help="List all audio tracks from midi.cfg without converting"
    )
    parser.add_argument(
        "--tiled",
        action="store_true",
        help="Output Tiled JSON format (default is PokeSharp entity format)"
    )
    parser.add_argument(
        "--entity",
        action="store_true",
        help="Output PokeSharp Definition format for EF Core (this is the default)"
    )

    args = parser.parse_args()
    
    # Setup logging
    logger = setup_logging(args.verbose, args.debug)
    
    input_dir = Path(args.input).resolve()
    output_dir = Path(args.output).resolve()
    
    if not input_dir.exists():
        logger.error(f"Input directory does not exist: {input_dir}")
        sys.exit(1)
    
    logger.info(f"Input directory: {input_dir}")
    logger.info(f"Output directory: {output_dir}")
    
    output_dir.mkdir(parents=True, exist_ok=True)
    
    # Handle popup extraction if requested
    if args.extract_popups:
        logger.info("Extracting map popup graphics...")
        bg_count, outline_count = extract_popups(str(input_dir), str(output_dir))
        logger.info(f"Popup extraction complete: {bg_count} backgrounds, {outline_count} outlines")
        logger.info("Outline tile sheets converted with palette transparency")
        return
    
    # Handle section extraction if requested
    if args.extract_sections:
        logger.info("Extracting map section definitions...")
        section_count, theme_count = extract_sections(str(input_dir), str(output_dir))
        logger.info(f"Section extraction complete: {section_count} sections, {theme_count} themes")
        return
    
    # Handle text window extraction if requested
    if args.extract_text_windows:
        logger.info("Extracting text window graphics...")
        count = extract_text_windows(str(input_dir), str(output_dir))
        logger.info(f"Text window extraction complete: {count} text windows extracted")
        logger.info("Text window sprites converted with transparency")
        return

    # Handle audio listing if requested
    if args.list_audio:
        logger.info("Listing audio tracks from midi.cfg...")
        converter = AudioConverter(str(input_dir), str(output_dir), args.soundfont)
        tracks = converter.list_tracks()

        # Group by category
        by_category = {}
        for track in tracks:
            cat = track['category']
            if cat not in by_category:
                by_category[cat] = []
            by_category[cat].append(track)

        print(f"\nFound {len(tracks)} audio tracks:\n")
        for category in sorted(by_category.keys()):
            cat_tracks = by_category[category]
            print(f"  {category}: {len(cat_tracks)} tracks")
            if args.verbose:
                for t in cat_tracks[:5]:
                    print(f"    - {t['id']} (vol: {t['volume']})")
                if len(cat_tracks) > 5:
                    print(f"    ... and {len(cat_tracks) - 5} more")
        return

    # Handle audio extraction if requested
    if args.extract_audio:
        logger.info("Extracting and converting audio from pokeemerald...")

        stats = extract_audio(
            str(input_dir),
            str(output_dir),
            include_music=args.audio_music,
            include_sfx=args.audio_sfx,
            include_phonemes=args.audio_phonemes,
            soundfont=args.soundfont,
            parallel=True
        )

        logger.info(f"Audio extraction complete:")
        logger.info(f"  Total tracks: {stats['total']}")
        logger.info(f"  Converted: {stats['converted']}")
        logger.info(f"  Failed: {stats['failed']}")
        logger.info(f"  Skipped: {stats['skipped']}")

        if stats['failed'] > 0 and not args.soundfont:
            logger.warning("Some conversions failed. Install timidity or fluidsynth for MIDI conversion:")
            logger.warning("  Ubuntu/Debian: sudo apt install timidity ffmpeg")
            logger.warning("  macOS: brew install timidity ffmpeg")
            logger.warning("  Or specify --soundfont for FluidSynth")
        return

    logger.info("Finding maps...")
    maps = find_map_files(str(input_dir))
    logger.info(f"Found {len(maps)} maps")
    
    logger.info("Finding layouts...")
    layouts = find_layout_files(str(input_dir))
    logger.info(f"Found {len(layouts)} layouts")
    
    # Create converter (pass tiled_mode to skip Definition generation in tiled mode)
    converter = MapConverter(str(input_dir), str(output_dir), tiled_mode=args.tiled)
    world_builder = WorldBuilder(str(output_dir))
    
    # Build warp lookup table before conversion
    logger.info("Building warp lookup table...")
    warp_lookup = MapConverter.build_warp_lookup(maps)
    logger.info(f"  Found {len(warp_lookup)} warp destinations")
    
    # Convert each map (parallelized)
    logger.info(f"Starting conversion of {len(maps)} maps...")
    
    # Prepare tasks for parallel execution
    max_workers = max(1, cpu_count() - 1)  # Use all but one CPU core
    conversion_tasks = []
    
    for map_id, map_info in maps.items():
        region_override = args.region if args.region else None
        conversion_tasks.append((
            map_id,
            map_info,
            input_dir,
            output_dir,
            layouts,  # Pass layouts dict
            region_override,
            warp_lookup,  # Pass warp lookup
            args.tiled  # Pass tiled_mode to skip Definition files in tiled mode
        ))
    
    # Execute conversions in parallel
    converted = 0
    skipped_layout = 0
    skipped_other = 0
    skipped_maps = []  # Track skipped maps for reporting
    world_builder_data = []  # Collect data for world builder
    # Collect used_tiles from all workers - initialized as empty dicts
    # Results are merged sequentially after workers complete (no race condition)
    all_used_tiles = {}  # Collect used_tiles from all workers
    all_used_tiles_with_palettes = {}  # Collect used_tiles_with_palettes from all workers
    
    # Use spawn method for ProcessPoolExecutor to ensure functions can be pickled
    # when running as a module (python -m porycon)
    with ProcessPoolExecutor(max_workers=max_workers) as executor:
        # Submit all tasks
        # Use fully qualified function reference to ensure it can be unpickled
        future_to_map = {
            executor.submit(convert_single_map, task): task[0] 
            for task in conversion_tasks
        }
        
        # Process results as they complete
        # NOTE: as_completed() processes results sequentially in this thread,
        # so dictionary updates are safe (no race condition)
        for future in as_completed(future_to_map):
            map_id = future_to_map[future]
            try:
                status, result_map_id, error_msg, world_data, tiled_map, used_tiles_dict, used_tiles_with_palettes_dict = future.result()
                
                if status == "success":
                    converted += 1
                    if world_data:
                        world_builder_data.append(world_data)
                    # Merge used_tiles from this worker (sequential - safe)
                    if used_tiles_dict:
                        for tileset_name, tile_ids in used_tiles_dict.items():
                            if tileset_name not in all_used_tiles:
                                all_used_tiles[tileset_name] = set()
                            # Update is safe because we're processing sequentially
                            all_used_tiles[tileset_name].update(tile_ids)
                    # Merge used_tiles_with_palettes from this worker (sequential - safe)
                    if used_tiles_with_palettes_dict:
                        for tileset_name, tile_palette_pairs in used_tiles_with_palettes_dict.items():
                            if tileset_name not in all_used_tiles_with_palettes:
                                all_used_tiles_with_palettes[tileset_name] = set()
                            # Update is safe because we're processing sequentially
                            all_used_tiles_with_palettes[tileset_name].update(tile_palette_pairs)
                elif status == "skipped_layout":
                    skipped_layout += 1
                    skipped_maps.append((result_map_id, "layout not found"))
                else:
                    skipped_other += 1
                    skipped_maps.append((result_map_id, error_msg or "unknown error"))
                    if skipped_other <= 3 and error_msg:
                        logger.warning(f"  Failed to convert {result_map_id}: {error_msg}")
            except Exception as e:
                skipped_other += 1
                skipped_maps.append((map_id, str(e)))
                if skipped_other <= 3:
                    logger.error(f"  Error processing {map_id}: {e}")
    
    # Merge collected used_tiles into main converter
    for tileset_name, tile_ids in all_used_tiles.items():
        converter.tileset_builder.add_tiles(tileset_name, list(tile_ids))
    
    # Merge collected used_tiles_with_palettes into main converter
    for tileset_name, tile_palette_pairs in all_used_tiles_with_palettes.items():
        converter.tileset_builder.add_tiles_with_palettes(tileset_name, list(tile_palette_pairs))
    
    # Add all maps to world builder
    for world_data in world_builder_data:
        world_builder.add_map(
            world_data["map_id"],
            world_data["map_name"],
            world_data["region"],
            world_data["connections"],
            world_data["width"],
            world_data["height"],
            world_data["map_data"]
        )
    
    logger.info(f"Converted {converted} maps")
    if skipped_layout > 0:
        logger.warning(f"Skipped {skipped_layout} maps (layout not found)")
        # Show first few skipped maps for debugging
        skipped_layout_maps = [m for m, reason in skipped_maps if "layout not found" in reason]
        if skipped_layout_maps:
            logger.warning(f"  Example skipped maps: {', '.join(skipped_layout_maps[:5])}")
    if skipped_other > 0:
        logger.warning(f"Skipped {skipped_other} maps (other reasons)")
        # Show first few skipped maps for debugging
        skipped_other_maps = [m for m, reason in skipped_maps if "layout not found" not in reason]
        if skipped_other_maps:
            logger.warning(f"  Example skipped maps: {', '.join(skipped_other_maps[:5])}")
    
    # Debug: Try converting the first map manually to see what happens
    if converted == 0 and len(maps) > 0:
        logger.debug("=== DEBUG: Attempting to convert first map ===")
        first_map_id = list(maps.keys())[0]
        first_map_info = maps[first_map_id]
        logger.debug(f"Map ID: {first_map_id}")
        logger.debug(f"Map info: {first_map_info}")
        
        try:
            map_data = load_json(first_map_info["map_file"])
            layout_id = first_map_info["layout_id"]
            logger.debug(f"Layout ID from map: {layout_id}")
            logger.debug(f"Layout ID in layouts: {layout_id in layouts}")
            
            if layout_id in layouts:
                layout = layouts[layout_id]
                logger.debug(f"Layout data: {list(layout.keys())}")
                logger.debug(f"Map bin: {layout.get('map_bin')}")
                logger.debug(f"Map bin exists: {layout.get('map_bin') and Path(layout['map_bin']).exists()}")
                
                logger.debug("Calling convert_map_with_metatiles...")
                region = first_map_info.get("region", "hoenn")
                tiled_map = converter.convert_map_with_metatiles(
                    first_map_id, map_data, layouts, region, warp_lookup
                )
                logger.debug(f"Result: {tiled_map is not None}")
                if tiled_map is None:
                    logger.debug("convert_map_with_metatiles returned None - check error messages above")
        except Exception as e:
            logger.debug(f"Exception during debug conversion: {e}", exc_info=True)
    
    # Debug: Show sample layout IDs
    if converted == 0 and len(layouts) > 0:
        logger.debug("Sample layout IDs found:")
        for i, (lid, layout) in enumerate(list(layouts.items())[:5]):
            map_bin = layout.get('map_bin', 'NO MAP.BIN')
            exists = "EXISTS" if map_bin and Path(map_bin).exists() else "MISSING"
            logger.debug(f"  {lid}: {map_bin} [{exists}]")
        
        logger.debug("Sample map layout IDs:")
        for i, (mid, minfo) in enumerate(list(maps.items())[:5]):
            layout_id = minfo.get('layout_id', 'NONE')
            found = "FOUND" if layout_id in layouts else "NOT FOUND"
            logger.debug(f"  {mid}: layout_id = '{layout_id}' [{found}]")
        
        # Check if any maps have matching layouts
        matching = sum(1 for mid, minfo in maps.items() if minfo.get('layout_id') in layouts)
        logger.debug(f"{matching} out of {len(maps)} maps have matching layouts")
        
        # Check if matching layouts have valid map.bin files
        if matching > 0:
            valid_bins = 0
            for mid, minfo in maps.items():
                layout_id = minfo.get('layout_id')
                if layout_id in layouts:
                    layout = layouts[layout_id]
                    if layout.get('map_bin') and Path(layout['map_bin']).exists():
                        valid_bins += 1
            logger.debug(f"{valid_bins} out of {matching} matching layouts have valid map.bin files")
        
        # Show first few that don't match
        unmatched = [(mid, minfo) for mid, minfo in maps.items() 
                     if minfo.get('layout_id') not in layouts][:5]
        if unmatched:
            logger.debug("Sample maps without matching layouts:")
            for mid, minfo in unmatched:
                logger.debug(f"  {mid}: looking for '{minfo.get('layout_id')}'")
        
        # Check if the sample layouts from maps actually exist
        sample_map_layouts = [minfo.get('layout_id') for _, minfo in list(maps.items())[:10]]
        logger.debug("Checking if sample map layouts exist:")
        for layout_id in sample_map_layouts:
            if layout_id:
                exists = layout_id in layouts
                status = "EXISTS" if exists else "MISSING"
                if exists:
                    layout = layouts[layout_id]
                    has_bin = layout.get('map_bin') and Path(layout['map_bin']).exists()
                    bin_status = "HAS BIN" if has_bin else "NO BIN"
                    logger.debug(f"  {layout_id}: {status} ({bin_status})")
                else:
                    logger.debug(f"  {layout_id}: {status}")
    
    # Build tilesets and collect tile mappings (parallelized)
    logger.info("Building tilesets...")
    tile_mappings = {}  # tileset_name -> {(old_tile_id, palette_index): new_tile_id}
    tileset_tilecounts = {}  # tileset_name -> tilecount (for tileset JSON)
    tileset_source_sizes = {}  # tileset_name -> source_total_tiles (for firstgid calculations)
    
    # Determine region for tilesets (use --region if provided, otherwise default)
    tileset_region = args.region if args.region else "hoenn"
    
    # For tileset building, we need the used_tiles from the converter
    # Since this is shared state, we'll build tilesets sequentially for now
    # but we can optimize the image processing within each tileset
    # Use union of both sets to ensure we build all tilesets
    tileset_names = list(set(converter.tileset_builder.used_tiles.keys()) | 
                         set(converter.tileset_builder.used_tiles_with_palettes.keys()))
    
    # Debug: Check if palette info is available
    tilesets_with_palettes = set(converter.tileset_builder.used_tiles_with_palettes.keys())
    tilesets_without_palettes = set(tileset_names) - tilesets_with_palettes
    if tilesets_without_palettes and len(tilesets_without_palettes) < 10:
        logger.debug(f"Tilesets without palette info: {sorted(tilesets_without_palettes)}")
    if tilesets_with_palettes:
        sample_tilesets = sorted(list(tilesets_with_palettes))[:5]
        logger.debug(f"Sample tilesets with palette info: {sample_tilesets}")
        # Show sample palette info for first tileset
        if sample_tilesets:
            sample_tileset = sample_tilesets[0]
            sample_pairs = list(converter.tileset_builder.used_tiles_with_palettes[sample_tileset])[:5]
            unique_palettes = set(p[1] for p in converter.tileset_builder.used_tiles_with_palettes[sample_tileset])
            logger.debug(f"{sample_tileset} has {len(converter.tileset_builder.used_tiles_with_palettes[sample_tileset])} tile+palette pairs, palettes: {sorted(unique_palettes)}")
    
    # Skip tileset building if no tilesets to build (per-map tilesets are created during conversion)
    if tileset_names:
        # Use ThreadPoolExecutor for I/O-bound tileset building (file operations)
        with ThreadPoolExecutor(max_workers=min(4, len(tileset_names))) as executor:
            future_to_tileset = {}
            for tileset_name in tileset_names:
                future = executor.submit(
                    converter.tileset_builder.create_tiled_tileset,
                    tileset_name,
                    str(output_dir),
                    tileset_region
                )
                future_to_tileset[future] = tileset_name
            
            for future in as_completed(future_to_tileset):
                tileset_name = future_to_tileset[future]
                try:
                    tileset_json, mapping = future.result()
                    tile_mappings[tileset_name] = mapping
                    tileset_tilecounts[tileset_name] = tileset_json.get("tilecount", 1)
                    # Get source_total_tiles for firstgid calculations (use full source size, not built size)
                    source_total = None
                    for prop in tileset_json.get("properties", []):
                        if prop.get("name") == "_source_total_tiles":
                            source_total = prop.get("value")
                            break
                    if source_total is None:
                        # Fallback: use tilecount if source_total not available
                        source_total = tileset_json.get("tilecount", 1)
                    tileset_source_sizes[tileset_name] = source_total
                    logger.info(f"  Built {tileset_name} ({tileset_json.get('tilecount', 1)} unique tiles, source: {source_total} tiles)")
                except Exception as e:
                    logger.error(f"  Error building {tileset_name}: {e}", exc_info=True)
    else:
        logger.info("  (Skipping consolidated tileset building - using per-map tilesets)")
    
    # Update firstgid values in all maps based on actual tileset tilecounts
    logger.info("Updating firstgid values in maps...")
    updated_maps = 0
    maps_dir = output_dir / "Definitions" / "Maps" / "Regions"
    if maps_dir.exists():
        for region_dir in maps_dir.iterdir():
            if region_dir.is_dir():
                for map_file in region_dir.glob("*.json"):
                    try:
                        map_data = load_json(str(map_file))
                        tilesets = map_data.get("tilesets", [])
                        if not tilesets:
                            continue
                        
                        # Update firstgid based on source tileset sizes (not built tilecounts)
                        # This ensures firstgid matches pokeemerald's structure (General: 1-512, Secondary: 513+)
                        current_firstgid = 1
                        updated = False
                        
                        for tileset in tilesets:
                            source = tileset.get("source", "")
                            tileset_name_from_path = Path(source).stem.lower()
                            
                            # Find matching tileset in source_sizes (case-insensitive)
                            matching_tileset = None
                            for ts_name in tileset_source_sizes.keys():
                                if ts_name.lower() == tileset_name_from_path:
                                    matching_tileset = ts_name
                                    break
                            
                            if matching_tileset:
                                source_size = tileset_source_sizes[matching_tileset]
                                if tileset.get("firstgid") != current_firstgid:
                                    tileset["firstgid"] = current_firstgid
                                    updated = True
                                current_firstgid += source_size  # Use source size for firstgid calculation
                            else:
                                # If tileset not found, keep existing firstgid and estimate
                                existing_firstgid = tileset.get("firstgid", current_firstgid)
                                if existing_firstgid >= current_firstgid:
                                    current_firstgid = existing_firstgid + 1  # Estimate
                        
                        if updated:
                            save_json(map_data, str(map_file))
                            updated_maps += 1
                    except Exception as e:
                        if updated_maps == 0:  # Only log first error
                            logger.error(f"  Error updating {map_file.name}: {e}")
    
    if updated_maps > 0:
        logger.info(f"  Updated firstgid in {updated_maps} maps")
    
    # Remap tile IDs in all converted maps (parallelized)
    if tile_mappings:
        logger.info("Remapping tile IDs in maps...")
        remapped_count = 0
        failed_count = 0
        
        # Find all map files
        map_files = []
        maps_dir = output_dir / "Definitions" / "Maps" / "Regions"
        if maps_dir.exists():
            for region_dir in maps_dir.iterdir():
                if region_dir.is_dir():
                    map_files.extend(region_dir.glob("*.json"))
        
        if map_files:
            def remap_single_map(args_tuple):
                """Remap a single map - designed for parallel execution."""
                map_file, tile_mappings_dict = args_tuple
                try:
                    from .converter import MapConverter
                    # Create a minimal converter just for remapping
                    # We don't need input_dir/output_dir for remapping, but the method requires it
                    temp_converter = MapConverter(".", ".")
                    return temp_converter.remap_map_tiles(map_file, tile_mappings_dict)
                except Exception as e:
                    return False
            
            # Use ThreadPoolExecutor for I/O-bound remapping (file read/write)
            max_remap_workers = min(8, len(map_files), cpu_count() * 2)
            with ThreadPoolExecutor(max_workers=max_remap_workers) as executor:
                future_to_file = {
                    executor.submit(remap_single_map, (map_file, tile_mappings)): map_file
                    for map_file in map_files
                }
                
                for future in as_completed(future_to_file):
                    map_file = future_to_file[future]
                    try:
                        if future.result():
                            remapped_count += 1
                        else:
                            failed_count += 1
                    except Exception as e:
                        failed_count += 1
                        if failed_count <= 3:
                            logger.error(f"  Error remapping {map_file.name}: {e}")
            
            logger.info(f"  Remapped {remapped_count} maps")
            if failed_count > 0:
                logger.warning(f"  Failed to remap {failed_count} maps")
    else:
        logger.info("No tile mappings available, skipping remapping (using per-map tilesets).")

    # Convert to entity format if not --tiled (default behavior)
    output_format = "tiled" if args.tiled else "entity"

    if output_format == "entity":
        logger.info("Converting to PokeSharp Definition format...")
        definition_converter = DefinitionConverter(str(output_dir))
        map_count = 0
        tileset_count = 0

        # Convert all Tiled maps to Definition format
        # Tiled maps are in Tiled/Regions/{Region}/*.json (actual map data)
        # IMPORTANT: Always use freshly generated Tiled maps from the conversion step above
        # This ensures that new properties (like triggers) are included in definitions
        tiled_maps_dir = output_dir / "Tiled" / "Regions"
        if tiled_maps_dir.exists():
            for region_dir in tiled_maps_dir.iterdir():
                if region_dir.is_dir():
                    region_name = region_dir.name.lower()
                    for map_file in region_dir.glob("*.json"):
                        try:
                            tiled_map = load_json(str(map_file))
                            if not tiled_map:
                                logger.warning(f"  Skipping empty Tiled map: {map_file.name}")
                                continue

                            map_name = map_file.stem
                            # Check if Tiled map has triggers layer (to verify it's up-to-date)
                            has_triggers = any(
                                layer.get("name", "").lower() == "triggers" 
                                and layer.get("type") == "objectgroup"
                                for layer in tiled_map.get("layers", [])
                            )
                            
                            definition = definition_converter.convert_tiled_map_to_definition(
                                tiled_map, map_name, region_name
                            )
                            # Always overwrite definition files to ensure they include new properties
                            definition_converter.save_map_definition(definition, map_name, region_name)
                            map_count += 1
                            
                            # Log if map doesn't have triggers (might indicate old Tiled map)
                            if not has_triggers and map_name == "lilycove_city":
                                logger.debug(f"  Note: {map_name} Tiled map does not have triggers layer")
                        except Exception as e:
                            logger.error(f"  Error converting map {map_file.name}: {e}")
                            if map_count == 0:
                                import traceback
                                logger.error(traceback.format_exc())

        # Convert all Tiled tilesets to Definition format
        tilesets_dir = output_dir / "Tilesets"
        if tilesets_dir.exists():
            for region_dir in tilesets_dir.iterdir():
                if region_dir.is_dir():
                    region_name = region_dir.name.lower()
                    for tileset_dir in region_dir.iterdir():
                        if tileset_dir.is_dir():
                            # Look for tileset JSON in map-specific tileset directories
                            tileset_file = tileset_dir / f"{tileset_dir.name}.json"
                            if tileset_file.exists():
                                try:
                                    tileset_json = load_json(str(tileset_file))
                                    if not tileset_json:
                                        continue

                                    tileset_name = tileset_file.stem
                                    definition = definition_converter.convert_tileset_to_definition(
                                        tileset_json, tileset_name, region_name
                                    )
                                    definition_converter.save_tileset_definition(definition, tileset_name, region_name)
                                    tileset_count += 1
                                except Exception as e:
                                    if tileset_count == 0:
                                        logger.error(f"  Error converting tileset {tileset_file.name}: {e}")

        # Generate definitions for referenced entities
        weather_count = definition_converter.generate_weather_definitions(tileset_region)
        scene_count = definition_converter.generate_battle_scene_definitions(tileset_region)

        # Generate region definition
        definition_converter.generate_region_definition(tileset_region)

        logger.info(f"  Converted {map_count} maps to Definitions/Maps/Regions/")
        logger.info(f"  Converted {tileset_count} tilesets to Definitions/Maps/Tilesets/")
        if weather_count > 0:
            logger.info(f"  Generated {weather_count} weather definitions")
        if scene_count > 0:
            logger.info(f"  Generated {scene_count} battle scene definitions")
        logger.info(f"  Generated region definition for {tileset_region.title()}")

        # Clean up intermediate Tiled/Tilesets folders (not needed for entity format)
        import shutil
        tiled_dir = output_dir / "Tiled"
        tilesets_dir = output_dir / "Tilesets"
        if tiled_dir.exists():
            shutil.rmtree(tiled_dir)
            logger.debug("  Cleaned up intermediate Tiled/ directory")
        if tilesets_dir.exists():
            shutil.rmtree(tilesets_dir)
            logger.debug("  Cleaned up intermediate Tilesets/ directory")
    else:
        logger.info("Output format: Tiled JSON (use without --tiled for Definition format)")
        # Skip Definition generation for Tiled mode

        # Build world files for Tiled mode only
        logger.info("Building world files...")
        # Build world graph starting from Littleroot Town for each region
        regions_found = set()
        for map_id, map_info in maps.items():
            regions_found.add(map_info.get("region", "hoenn"))

        for region in regions_found:
            # Find the starting map for this region (Littleroot for Hoenn)
            start_map_id = "MAP_LITTLEROOT_TOWN" if region == "hoenn" else None

            # If no specific start map, use first map in region
            if not start_map_id:
                for map_id, map_info in maps.items():
                    if map_info.get("region") == region:
                        start_map_id = map_id
                        break

            if start_map_id:
                logger.info(f"  Building world graph for {region} starting from {start_map_id}...")
                world_builder.build_world(region, start_map_id)
            else:
                logger.warning(f"  No maps found for region {region}")

        world_builder.save_all_worlds(tiled_mode=True)
    
    logger.info("Conversion complete!")
    logger.info(f"Output directory: {output_dir}")


if __name__ == "__main__":
    main()

