"""
Extract and process text window graphics from pokeemerald.
Copies text window sprites with proper transparency.
"""

import json
from pathlib import Path
from typing import Tuple, Optional
from PIL import Image
from .logging_config import get_logger
from .id_transformer import IdTransformer

logger = get_logger('text_window_extractor')


class TextWindowExtractor:
    """Extracts text window graphics from pokeemerald and processes them."""
    
    def __init__(self, input_dir: str, output_dir: str):
        """
        Initialize text window extractor.
        
        Args:
            input_dir: Path to pokeemerald root directory
            output_dir: Path to output directory (should be Assets folder)
        """
        self.input_dir = Path(input_dir)
        self.output_dir = Path(output_dir)
        
        # Pokeemerald paths
        self.emerald_graphics = self.input_dir / "graphics" / "text_window"
        
        # Output paths - updated to use Definitions instead of Definitions
        self.output_graphics = self.output_dir / "Graphics" / "Sprites" / "TextWindow"
        self.output_definitions = self.output_dir / "Definitions" / "TextWindow"
        
    def extract_all(self) -> int:
        """
        Extract all text window graphics from pokeemerald.
        
        Returns:
            Number of text windows extracted
        """
        logger.info("Extracting text window graphics from pokeemerald...")
        
        if not self.emerald_graphics.exists():
            logger.warning(f"Text window graphics not found: {self.emerald_graphics}")
            return 0
        
        # Create output directories
        self.output_graphics.mkdir(parents=True, exist_ok=True)
        self.output_definitions.mkdir(parents=True, exist_ok=True)
        
        count = 0
        
        # Find all PNG files in text_window directory
        png_files = list(self.emerald_graphics.glob("*.png"))
        logger.info(f"Found {len(png_files)} PNG files in text_window directory")
        
        for png_file in png_files:
            if self._extract_text_window(png_file):
                count += 1
        
        logger.info(f"Extracted {count} text window graphics")
        return count
    
    def _extract_text_window(self, source_file: Path) -> bool:
        """
        Extract a single text window graphic and apply transparency.
        
        Args:
            source_file: Path to source PNG file
            
        Returns:
            True if successful
        """
        filename = source_file.stem
        logger.debug(f"Processing text window: {filename}")
        
        try:
            # Load image
            img = Image.open(source_file)
            
            # Convert indexed color (mode P) to RGBA with transparency
            if img.mode == 'P':
                # Mark palette index 0 as transparent if not already set
                if 'transparency' not in img.info:
                    img.info['transparency'] = 0
                img = img.convert('RGBA')
            elif img.mode != 'RGBA':
                img = img.convert('RGBA')
            
            # Apply transparency for common GBA mask colors
            # Check for magenta (#FF00FF) first
            self._apply_magenta_transparency(img)
            
            # Detect and apply background color transparency
            # Common background colors in GBA graphics
            mask_color = self._detect_background_color(img)
            if mask_color:
                self._apply_transparency(img, mask_color)
                logger.debug(f"  Applied background color transparency: {mask_color}")
            
            # Save processed PNG
            dest_png = self.output_graphics / f"{filename}.png"
            img.save(dest_png, 'PNG')
            logger.debug(f"  Saved sprite: {dest_png.name}")
            
            # Get image dimensions
            width, height = img.size
            
            # GBA text windows use 8x8 pixel tiles
            tile_width = 8
            tile_height = 8
            
            # Calculate grid dimensions
            tiles_per_row = width // tile_width
            tiles_per_col = height // tile_height
            tile_count = tiles_per_row * tiles_per_col
            
            # Generate tile definitions for all tiles in the sheet
            tiles = []
            for tile_idx in range(tile_count):
                row = tile_idx // tiles_per_row
                col = tile_idx % tiles_per_row
                tiles.append({
                    "index": tile_idx,
                    "x": col * tile_width,
                    "y": row * tile_height,
                    "width": tile_width,
                    "height": tile_height
                })

            # Create JSON definition - using TileSheet format like popup outlines
            # Note: Use camelCase to match C# JsonPropertyName conventions
            unified_id = IdTransformer.create_id("textwindow", "tilesheet", filename)
            json_def = {
                "id": unified_id,
                "displayName": filename.replace("_", " ").title(),
                "type": "TileSheet",
                "texturePath": f"Graphics/Sprites/TextWindow/{filename}.png",
                "tileWidth": tile_width,
                "tileHeight": tile_height,
                "tileCount": tile_count,
                "tiles": tiles,
                "description": "Text window tile sheet from Pokemon Emerald (GBA tile-based rendering)"
            }
            
            # Save definition JSON
            dest_json = self.output_definitions / f"{filename}.json"
            with open(dest_json, 'w') as f:
                json.dump(json_def, f, indent=2)
            logger.debug(f"  Created definition: {dest_json.name}")
            
            return True
            
        except Exception as e:
            logger.error(f"Failed to extract text window {filename}: {e}")
            return False
    
    def _apply_transparency(self, image: Image.Image, mask_color_hex: str) -> None:
        """Apply transparency by replacing mask color with transparent pixels."""
        # Parse hex color
        mask_color_hex = mask_color_hex.lstrip('#')
        if len(mask_color_hex) != 6:
            return
        
        try:
            r = int(mask_color_hex[0:2], 16)
            g = int(mask_color_hex[2:4], 16)
            b = int(mask_color_hex[4:6], 16)
        except ValueError:
            return
        
        # Ensure image is RGBA
        if image.mode != "RGBA":
            return
        
        # Get pixel data
        pixels = image.load()
        transparent_count = 0
        
        for y in range(image.height):
            for x in range(image.width):
                pixel = pixels[x, y]
                # Check if pixel matches mask color (RGB only, ignore alpha)
                if len(pixel) >= 3 and pixel[0] == r and pixel[1] == g and pixel[2] == b:
                    pixels[x, y] = (0, 0, 0, 0)  # Fully transparent
                    transparent_count += 1
        
        if transparent_count > 0:
            logger.debug(f"  Made {transparent_count} pixels transparent from mask color {mask_color_hex}")
    
    def _apply_magenta_transparency(self, image: Image.Image) -> None:
        """Apply transparency for magenta (#FF00FF) pixels, a common GBA transparency mask."""
        if image.mode != "RGBA":
            return
        
        pixels = image.load()
        transparent_count = 0
        
        for y in range(image.height):
            for x in range(image.width):
                r, g, b, a = pixels[x, y]
                # Magenta (#FF00FF) is commonly used as transparency mask in GBA graphics
                if r == 255 and g == 0 and b == 255 and a > 0:
                    pixels[x, y] = (0, 0, 0, 0)  # Fully transparent
                    transparent_count += 1
        
        if transparent_count > 0:
            logger.debug(f"  Made {transparent_count} magenta pixels transparent")
    
    def _detect_background_color(self, image: Image.Image) -> Optional[str]:
        """
        Detect the background color by finding the most common corner color.
        GBA graphics often use a consistent background color in corners.
        
        Args:
            image: PIL Image in RGBA mode
            
        Returns:
            Hex color string (e.g., "#00FF00") or None
        """
        if image.mode != "RGBA":
            return None
        
        width, height = image.size
        if width < 2 or height < 2:
            return None
        
        # Check corner pixels
        corners = [
            image.getpixel((0, 0)),           # Top-left
            image.getpixel((width - 1, 0)),    # Top-right
            image.getpixel((0, height - 1)),   # Bottom-left
            image.getpixel((width - 1, height - 1))  # Bottom-right
        ]
        
        # Count occurrences of each corner color
        color_counts = {}
        for corner in corners:
            if len(corner) >= 3:
                r, g, b = corner[0], corner[1], corner[2]
                color_key = (r, g, b)
                color_counts[color_key] = color_counts.get(color_key, 0) + 1
        
        # Find the most common corner color
        if color_counts:
            most_common = max(color_counts.items(), key=lambda x: x[1])
            if most_common[1] >= 2:  # At least 2 corners match
                r, g, b = most_common[0]
                return f"#{r:02X}{g:02X}{b:02X}"
        
        return None


def extract_text_windows(input_dir: str, output_dir: str) -> int:
    """
    Extract text window graphics from pokeemerald.
    
    Args:
        input_dir: Path to pokeemerald root directory
        output_dir: Path to output directory (should be Assets folder)
        
    Returns:
        Number of text windows extracted
    """
    extractor = TextWindowExtractor(input_dir, output_dir)
    return extractor.extract_all()

