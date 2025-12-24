"""
Palette loading utilities for pokeemerald tilesets.
"""

from pathlib import Path
from typing import List, Tuple, Optional
from PIL import Image
from .logging_config import get_logger

logger = get_logger('palette_loader')


def load_palette(palette_path: Path) -> Optional[List[Tuple[int, int, int, int]]]:
    """
    Load a JASC-PAL format palette file.
    
    Format:
    JASC-PAL
    0100
    <num_colors>
    <r> <g> <b>
    ...
    
    Returns:
        List of RGBA tuples (r, g, b, 255) or None if file not found/invalid
    """
    if not palette_path.exists():
        return None
    
    try:
        with open(palette_path, 'r') as f:
            lines = f.readlines()
        
        if len(lines) < 3:
            return None
        
        # Check header
        if lines[0].strip() != "JASC-PAL":
            return None
        
        # Skip version line (0100)
        # Read number of colors
        num_colors = int(lines[2].strip())
        
        palette = []
        for i in range(3, 3 + num_colors):
            if i >= len(lines):
                break
            parts = lines[i].strip().split()
            if len(parts) >= 3:
                r = int(parts[0])
                g = int(parts[1])
                b = int(parts[2])
                # Color 0 is typically transparent in Game Boy Color
                # But .pal files don't specify alpha, so we use full alpha for all colors
                # Transparency is handled by the tile image itself
                alpha = 255
                # If color is pure black (0,0,0), it might be intended as transparent
                # But we'll keep it as opaque black for now - let the tile image handle transparency
                palette.append((r, g, b, alpha))
        
        # Ensure palette has at least 16 colors (pad with black if needed)
        # Game Boy Color palettes typically have 16 colors
        while len(palette) < 16:
            palette.append((0, 0, 0, 255))
        
        # Truncate to 16 colors if it has more
        if len(palette) > 16:
            palette = palette[:16]
        
        return palette
    except Exception as e:
        logger.warning(f"Error loading palette {palette_path}: {e}")
        return None


def load_tileset_palettes(tileset_dir: Path) -> List[Optional[List[Tuple[int, int, int, int]]]]:
    """
    Load all 16 palettes (00.pal through 15.pal) from a tileset's palettes directory.
    
    Returns:
        List of 16 palettes (or None for missing palettes)
    """
    palettes = []
    palettes_dir = tileset_dir / "palettes"
    
    for i in range(16):
        palette_file = palettes_dir / f"{i:02d}.pal"
        palette = load_palette(palette_file)
        palettes.append(palette)
    
    return palettes


def apply_palette_to_tile(tile_image: Image.Image, palette: List[Tuple[int, int, int, int]]) -> Image.Image:
    """
    Apply a palette to an indexed color tile image.
    
    In pokeemerald/GBA, color index 0 is typically transparent.
    We also check for magenta (#FF00FF) as a transparency mask color.
    
    Args:
        tile_image: PIL Image in 'P' (palette) mode
        palette: List of RGBA color tuples (should have 16 colors)
    
    Returns:
        RGBA image with palette applied and transparency handled
    """
    if tile_image.mode != 'P':
        # Already converted, return as-is
        if tile_image.mode != 'RGBA':
            return tile_image.convert('RGBA')
        return tile_image
    
    # Validate palette
    if not palette or len(palette) < 16:
        # Invalid palette - convert using embedded palette as fallback
        # This preserves transparency if the original image has it
        rgba_image = tile_image.convert('RGBA')
        # Make color index 0 transparent if it exists
        _make_color_0_transparent(rgba_image, tile_image)
        return rgba_image
    
    # In GBA/pokeemerald, color index 0 is typically transparent
    # Make sure palette[0] has alpha=0
    if len(palette) > 0:
        palette = list(palette)  # Make a copy so we can modify it
        # Color 0 should be transparent
        palette[0] = (palette[0][0], palette[0][1], palette[0][2], 0)
    
    # Use PIL's built-in palette conversion mechanism
    # Create a new 'P' mode image with the new palette
    # First, create a flat palette list (RGBRGBRGB...) for putpalette
    flat_palette = []
    for color in palette:
        flat_palette.extend(color[:3])  # Only RGB, alpha handled separately
    
    # Pad to 256 colors (PIL requires 256 color palettes for 'P' mode)
    while len(flat_palette) < 768:  # 256 colors * 3 (RGB)
        flat_palette.append(0)
    
    # Create a copy of the tile and apply the new palette
    # Important: copy() preserves the image data and info, including transparency
    new_tile = tile_image.copy()
    new_tile.putpalette(flat_palette)
    
    # Set transparency for color index 0
    # In GBA, color 0 is always transparent
    new_tile.info['transparency'] = 0
    
    # Convert to RGBA - PIL will handle transparency correctly
    rgba_image = new_tile.convert('RGBA')
    
    # Manually ensure color index 0 pixels are transparent
    # Also check for magenta (#FF00FF) as a transparency mask
    original_pixels = tile_image.load()
    rgba_pixels = rgba_image.load()
    
    for y in range(tile_image.height):
        for x in range(tile_image.width):
            color_index = original_pixels[x, y]
            
            # Color index 0 is always transparent in GBA
            if color_index == 0:
                rgba_pixels[x, y] = (0, 0, 0, 0)
            else:
                # Check if the resulting color is magenta (#FF00FF), which is also used as transparency
                r, g, b, a = rgba_pixels[x, y]
                if r == 255 and g == 0 and b == 255:
                    rgba_pixels[x, y] = (0, 0, 0, 0)
    
    return rgba_image


def _make_color_0_transparent(rgba_image: Image.Image, original_p_image: Image.Image):
    """
    Helper function to make color index 0 pixels transparent in an RGBA image.
    """
    if original_p_image.mode != 'P':
        return
    
    original_pixels = original_p_image.load()
    rgba_pixels = rgba_image.load()
    
    for y in range(original_p_image.height):
        for x in range(original_p_image.width):
            if original_pixels[x, y] == 0:
                rgba_pixels[x, y] = (0, 0, 0, 0)

