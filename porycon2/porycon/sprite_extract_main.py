"""
Main entry point for sprite extraction.
"""

import argparse
import sys
from pathlib import Path
from .animation_parser import PokeemeraldAnimationParser
from .sprite_extractor import SpriteExtractor
from .logging_config import setup_logging, get_logger


def main():
    """Main entry point for sprite extraction."""
    parser = argparse.ArgumentParser(
        description="Extract Pokemon Emerald sprites to PokeSharp format"
    )
    parser.add_argument(
        "--pokeemerald",
        default="../pokeemerald",
        help="Path to pokeemerald root directory (default: ../pokeemerald)"
    )
    parser.add_argument(
        "--output",
        default="../PokeSharp.Game/Assets",
        help="Path to output directory (sprites will be written to Sprites/ subdirectory, default: ../PokeSharp.Game/Assets)"
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
    
    args = parser.parse_args()
    
    # Setup logging
    logger = setup_logging(args.verbose, args.debug)
    
    pokeemerald_path = Path(args.pokeemerald).resolve()
    output_path = Path(args.output).resolve()
    
    if not pokeemerald_path.exists():
        logger.error(f"Pokeemerald directory does not exist: {pokeemerald_path}")
        sys.exit(1)
    
    logger.info("=== Pokemon Emerald Sprite Extractor ===")
    logger.info(f"Source: {pokeemerald_path}")
    logger.info(f"Output: {output_path}")
    logger.info("")
    
    # Parse animation metadata from pokeemerald source
    animation_parser = PokeemeraldAnimationParser(str(pokeemerald_path))
    animation_data = animation_parser.parse_animation_data()
    filename_mapping = animation_parser.get_filename_mapping()
    
    pic_tables_path = pokeemerald_path / "src" / "data" / "object_events" / "object_event_pic_tables.h"
    graphics_path = pokeemerald_path / "src" / "data" / "object_events" / "object_event_graphics.h"
    pic_table_sources = animation_parser.parse_pic_table_sources(pic_tables_path, graphics_path)
    
    # Create extractor and extract all sprites
    extractor = SpriteExtractor(
        str(pokeemerald_path),
        str(output_path),
        animation_data,
        filename_mapping,
        pic_table_sources
    )
    extractor.extract_all_sprites()
    
    logger.info("\nExtraction complete!")


if __name__ == "__main__":
    main()

