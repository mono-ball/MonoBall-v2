#!/usr/bin/env python3
"""
Script to apply transparency to popup outline sprite sheets.
Makes the center area transparent so backgrounds show through.
Also makes white/background colors transparent in the border regions.
"""

import os
from PIL import Image

def process_outline(input_path, output_path=None, corner_size=8, make_center_transparent=True, transparent_color=(255, 255, 255)):
    """
    Process an outline sprite sheet to apply transparency.
    
    Args:
        input_path: Path to input PNG file
        output_path: Path to output PNG file (defaults to overwriting input)
        corner_size: Size of corners in pixels (for 9-slice layout)
        make_center_transparent: If True, makes the center region fully transparent
        transparent_color: RGB color to make transparent (default: white)
    """
    if output_path is None:
        output_path = input_path
    
    print(f"Processing: {input_path}")
    
    # Load image and convert to RGBA if not already
    img = Image.open(input_path)
    if img.mode != 'RGBA':
        img = img.convert('RGBA')
    
    width, height = img.size
    pixels = img.load()
    
    # Calculate center region bounds (9-slice layout)
    center_left = corner_size
    center_right = width - corner_size
    center_top = corner_size
    center_bottom = height - corner_size
    
    # Process each pixel
    for y in range(height):
        for x in range(width):
            r, g, b, a = pixels[x, y]
            
            # Make center region fully transparent
            if make_center_transparent:
                if center_left <= x < center_right and center_top <= y < center_bottom:
                    pixels[x, y] = (r, g, b, 0)
                    continue
            
            # Make matching colors transparent in border regions
            # This removes backgrounds and makes only the actual border visible
            if transparent_color:
                color_match = (
                    abs(r - transparent_color[0]) < 10 and
                    abs(g - transparent_color[1]) < 10 and
                    abs(b - transparent_color[2]) < 10
                )
                if color_match:
                    pixels[x, y] = (r, g, b, 0)
    
    # Save with transparency
    img.save(output_path, 'PNG')
    print(f"  Saved to: {output_path}")
    print(f"  Size: {width}x{height}, Center region: ({center_left},{center_top})-({center_right},{center_bottom})")

def process_all_outlines(directory='.', corner_size=8):
    """
    Process all outline PNG files in a directory.
    
    Args:
        directory: Directory containing outline PNG files
        corner_size: Size of corners for 9-slice layout
    """
    print(f"Processing all outline PNGs in: {directory}")
    print(f"Corner size: {corner_size}px")
    print("-" * 60)
    
    processed_count = 0
    for filename in os.listdir(directory):
        if filename.endswith('_outline.png') or filename.endswith('-outline.png'):
            filepath = os.path.join(directory, filename)
            try:
                process_outline(
                    filepath,
                    corner_size=corner_size,
                    make_center_transparent=True,
                    transparent_color=(255, 255, 255)  # Make white transparent
                )
                processed_count += 1
                print()
            except Exception as e:
                print(f"  ERROR: {e}")
                print()
    
    print("-" * 60)
    print(f"Processed {processed_count} outline files")

if __name__ == '__main__':
    import sys
    
    # Check if PIL is installed
    try:
        from PIL import Image
    except ImportError:
        print("ERROR: PIL (Pillow) is not installed.")
        print("Please install it with: pip install Pillow")
        sys.exit(1)
    
    # Get directory from command line or use current directory
    if len(sys.argv) > 1:
        directory = sys.argv[1]
    else:
        directory = '.'
    
    # Get corner size from command line or use default
    corner_size = 8
    if len(sys.argv) > 2:
        try:
            corner_size = int(sys.argv[2])
        except ValueError:
            print("WARNING: Invalid corner size, using default: 8")
    
    process_all_outlines(directory, corner_size)
    
    print("\nDone! All outline sprite sheets have been processed.")
    print("The center regions are now transparent, and white backgrounds have been removed.")



