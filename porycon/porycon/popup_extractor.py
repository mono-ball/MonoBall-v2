"""
Extract and process map popup graphics from pokeemerald.
Copies backgrounds and outline tile sheets with proper transparency.
"""

import json
from pathlib import Path
from typing import Dict, List, Tuple, Optional
from PIL import Image
from .logging_config import get_logger
from .id_transformer import IdTransformer

logger = get_logger('popup_extractor')


class PopupExtractor:
    """Extracts map popup graphics from pokeemerald and processes them."""
    
    def __init__(self, input_dir: str, output_dir: str):
        """
        Initialize popup extractor.
        
        Args:
            input_dir: Path to pokeemerald root directory
            output_dir: Path to output directory
        """
        self.input_dir = Path(input_dir)
        self.output_dir = Path(output_dir)
        
        # Pokeemerald paths
        self.emerald_graphics = self.input_dir / "graphics" / "map_popup"
        
        # Output paths
        self.output_graphics_bg = self.output_dir / "Graphics" / "Maps" / "Popups" / "Backgrounds"
        self.output_graphics_outline = self.output_dir / "Graphics" / "Maps" / "Popups" / "Outlines"
        self.output_data_bg = self.output_dir / "Definitions" / "Maps" / "Popups" / "Backgrounds"
        self.output_data_outline = self.output_dir / "Definitions" / "Maps" / "Popups" / "Outlines"
        
    def extract_all(self) -> Tuple[int, int]:
        """
        Extract all popup graphics from pokeemerald.
        
        Returns:
            Tuple of (backgrounds_extracted, outlines_extracted)
        """
        logger.info("Extracting map popup graphics from pokeemerald...")
        
        if not self.emerald_graphics.exists():
            logger.warning(f"Map popup graphics not found: {self.emerald_graphics}")
            return (0, 0)
        
        # Create output directories
        self.output_graphics_bg.mkdir(parents=True, exist_ok=True)
        self.output_graphics_outline.mkdir(parents=True, exist_ok=True)
        self.output_data_bg.mkdir(parents=True, exist_ok=True)
        self.output_data_outline.mkdir(parents=True, exist_ok=True)
        
        bg_count = 0
        outline_count = 0
        
        # Known popup styles from pokeemerald
        popup_styles = self._discover_popup_styles()
        
        for style_name in popup_styles:
            logger.info(f"Processing popup style: {style_name}")
            
            # Process background
            if self._extract_background(style_name):
                bg_count += 1
            
            # Process outline (with transparency)
            if self._extract_outline(style_name):
                outline_count += 1
        
        logger.info(f"Extracted {bg_count} backgrounds and {outline_count} outlines")
        return (bg_count, outline_count)
    
    def _discover_popup_styles(self) -> List[str]:
        """
        Discover available popup styles in pokeemerald.
        Looks for common naming patterns in graphics/map_popup/
        
        Returns:
            List of style names
        """
        styles = set()
        
        # Check for standard pokeemerald popup graphics
        # In pokeemerald, these are typically in graphics/map_popup/
        # Look for files like: wood_bg.png, wood_outline.png
        
        if self.emerald_graphics.exists():
            logger.debug(f"Scanning directory: {self.emerald_graphics}")
            png_files = list(self.emerald_graphics.glob("*.png"))
            logger.debug(f"Found {len(png_files)} PNG files")
            
            for png_file in png_files:
                filename = png_file.stem
                logger.debug(f"  Checking file: {filename}")
                
                # Check for outline pattern first (e.g., wood_outline.png)
                if "_outline" in filename:
                    style_name = filename.replace("_outline", "")
                    styles.add(style_name)
                    logger.debug(f"    -> Found outline style: {style_name}")
                
                # Check for other outline patterns
                elif "_border" in filename or "_frame" in filename:
                    style_name = filename.replace("_border", "").replace("_frame", "")
                    styles.add(style_name)
                    logger.debug(f"    -> Found outline style: {style_name}")
                
                # Check for background with explicit suffix
                elif "_bg" in filename or "_background" in filename:
                    style_name = filename.replace("_bg", "").replace("_background", "")
                    styles.add(style_name)
                    logger.debug(f"    -> Found background style: {style_name}")
                
                # Otherwise, it's probably a background without suffix (e.g., wood.png)
                # Check if there's a corresponding outline file to confirm
                else:
                    possible_outline = self.emerald_graphics / f"{filename}_outline.png"
                    if possible_outline.exists():
                        styles.add(filename)
                        logger.debug(f"    -> Found background+outline pair: {filename}")
        
        styles_list = sorted(list(styles))
        
        # Fallback: use default pokeemerald styles
        if not styles_list:
            logger.warning("No popup graphics found in map_popup folder")
            logger.info("Using default style names (you may need to check file names manually)")
            styles_list = ["wood", "stone", "brick", "marble", "underwater"]
        
        logger.info(f"Discovered {len(styles_list)} popup styles: {', '.join(styles_list)}")
        return styles_list
    
    def _extract_background(self, style_name: str) -> bool:
        """
        Extract and copy a background texture.
        
        Args:
            style_name: Name of the popup style
            
        Returns:
            True if successful
        """
        # Try various naming conventions (pokeemerald uses just {style}.png for backgrounds)
        possible_names = [
            f"{style_name}.png",              # Standard: wood.png
            f"{style_name}_bg.png",           # Alternate: wood_bg.png
            f"{style_name}_background.png",   # Alternate: wood_background.png
        ]
        
        source_file = None
        for name in possible_names:
            candidate = self.emerald_graphics / name
            logger.debug(f"  Trying background: {candidate}")
            if candidate.exists():
                source_file = candidate
                logger.debug(f"    -> Found!")
                break
        
        if not source_file:
            logger.warning(f"Background not found for style: {style_name} (tried: {', '.join(possible_names)})")
            return False
        
        # Copy PNG
        dest_png = self.output_graphics_bg / f"{style_name}.png"
        try:
            img = Image.open(source_file)
            # Convert to RGBA if needed
            if img.mode != 'RGBA':
                img = img.convert('RGBA')
            img.save(dest_png, 'PNG')
            logger.debug(f"  Copied background: {dest_png.name}")
        except Exception as e:
            logger.error(f"Failed to copy background {style_name}: {e}")
            return False
        
        # Create JSON definition for background bitmap with unified ID
        unified_id = IdTransformer.create_id("popup", "background", style_name)
        json_def = {
            "id": unified_id,
            "name": style_name.replace("_", " ").title(),
            "type": "Bitmap",
            "texturePath": f"Graphics/Maps/Popups/Backgrounds/{style_name}.png",
            "width": 80,
            "height": 24,
            "description": "Background bitmap for map popup"
        }
        
        dest_json = self.output_data_bg / f"{style_name}.json"
        try:
            with open(dest_json, 'w') as f:
                json.dump(json_def, f, indent=2)
            logger.debug(f"  Created definition: {dest_json.name}")
        except Exception as e:
            logger.error(f"Failed to create background definition {style_name}: {e}")
            return False
        
        return True
    
    def _extract_outline(self, style_name: str) -> bool:
        """
        Extract an outline tile sheet and convert transparency.
        
        Args:
            style_name: Name of the popup style
            
        Returns:
            True if successful
        """
        # Try various naming conventions (pokeemerald uses {style}_outline.png)
        possible_names = [
            f"{style_name}_outline.png",      # Standard: wood_outline.png
            f"{style_name}_border.png",       # Alternate: wood_border.png
            f"{style_name}_frame.png",        # Alternate: wood_frame.png
        ]
        
        source_file = None
        for name in possible_names:
            candidate = self.emerald_graphics / name
            logger.debug(f"  Trying outline: {candidate}")
            if candidate.exists():
                source_file = candidate
                logger.debug(f"    -> Found!")
                break
        
        if not source_file:
            logger.warning(f"Outline not found for style: {style_name} (tried: {', '.join(possible_names)})")
            return False
        
        # Load and convert PNG
        # Note: These are tile sheets (10x3 = 30 tiles of 8x8 pixels), not 9-slice sprites!
        # Palette index 0 is transparent in GBA
        dest_png = self.output_graphics_outline / f"{style_name}_outline.png"
        try:
            img = Image.open(source_file)
            
            # Convert indexed color (mode P) to RGBA with transparency
            if img.mode == 'P':
                # Mark palette index 0 as transparent if not already set
                if 'transparency' not in img.info:
                    img.info['transparency'] = 0
                img = img.convert('RGBA')
            elif img.mode != 'RGBA':
                img = img.convert('RGBA')
            
            # Save as RGBA PNG
            img.save(dest_png, 'PNG')
            logger.debug(f"  Copied outline: {dest_png.name}")
        except Exception as e:
            logger.error(f"Failed to copy outline {style_name}: {e}")
            return False
        
        # Create JSON definition for outline tile sheet
        # Generate tile definitions for all 30 tiles (10x3 grid, 8x8 each)
        tiles = []
        for tile_idx in range(30):
            row = tile_idx // 10
            col = tile_idx % 10
            tiles.append({
                "index": tile_idx,
                "x": col * 8,
                "y": row * 8,
                "width": 8,
                "height": 8
            })
        
        # Create JSON definition with unified ID
        unified_id = IdTransformer.create_id("popup", "outline", f"{style_name}_outline")
        json_def = {
            "id": unified_id,
            "name": f"{style_name.replace('_', ' ').title()} Outline",
            "type": "TileSheet",
            "texturePath": f"Graphics/Maps/Popups/Outlines/{style_name}_outline.png",
            "tileWidth": 8,
            "tileHeight": 8,
            "tileCount": 30,
            "tiles": tiles,
            "tileUsage": {
                "topEdge": list(range(0, 12)),
                "leftEdge": [12, 14, 16],
                "rightEdge": [13, 15, 17],
                "bottomEdge": list(range(18, 30))
            },
            "description": "9-patch frame tile sheet for map popup (GBA tile-based rendering)"
        }
        
        dest_json = self.output_data_outline / f"{style_name}_outline.json"
        try:
            with open(dest_json, 'w') as f:
                json.dump(json_def, f, indent=2)
            logger.debug(f"  Created definition: {dest_json.name}")
        except Exception as e:
            logger.error(f"Failed to create outline definition {style_name}: {e}")
            return False
        
        return True


def extract_popups(input_dir: str, output_dir: str) -> Tuple[int, int]:
    """
    Extract popup graphics from pokeemerald.
    
    Args:
        input_dir: Path to pokeemerald root directory
        output_dir: Path to output directory (should be Assets folder)
        
    Returns:
        Tuple of (backgrounds_extracted, outlines_extracted)
    """
    extractor = PopupExtractor(input_dir, output_dir)
    return extractor.extract_all()
