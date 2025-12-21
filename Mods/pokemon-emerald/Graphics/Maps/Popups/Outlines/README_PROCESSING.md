# Processing Outline Sprite Sheets

This directory contains a Python script to apply transparency to outline sprite sheets.

## Why This Is Needed

Outline sprite sheets for map popups use 9-slice rendering, which requires:

1. **Transparent center region** - So the background texture shows through
2. **Transparent background colors** - Remove any white/colored backgrounds around the actual border pixels

Without transparency, the outlines will block the background and look wrong.

## Requirements

Python 3 with Pillow (PIL):

```bash
pip install Pillow
```

## Usage

### Process All Outlines in Current Directory

From this directory, run:

```bash
python process_transparency.py
```

This will:

- Find all files ending in `_outline.png` or `-outline.png`
- Apply transparency to the center 9-slice region
- Make white (#FFFFFF) pixels transparent
- Overwrite the original files with transparent versions

### Process Outlines in a Different Directory

```bash
python process_transparency.py /path/to/outlines
```

### Custom Corner Size

If your sprite sheets use different corner dimensions (default is 8px):

```bash
python process_transparency.py . 16
```

This sets corner size to 16px instead of 8px.

## What Gets Processed

The script:

1. Loads each outline PNG file
2. Converts to RGBA (if not already)
3. Makes the center region (9-slice center) fully transparent
4. Makes white pixels (RGB ~255,255,255) transparent
5. Saves back as PNG with alpha channel

## 9-Slice Layout

```
┌────────┬──────────┬────────┐
│ Corner │   Edge   │ Corner │  Corners: 8x8px (default)
│  TL    │   Top    │   TR   │  Kept opaque (border visible)
├────────┼──────────┼────────┤
│  Left  │  CENTER  │ Right  │  CENTER: Made transparent
│  Edge  │TRANSPRNT │  Edge  │  So background shows through
├────────┼──────────┼────────┤
│ Corner │  Bottom  │ Corner │
│  BL    │   Edge   │   BR   │  Edges: Kept opaque
└────────┴──────────┴────────┘
```

## Manual Processing

If you prefer to process manually in an image editor:

1. Open the outline PNG in your editor (GIMP, Photoshop, etc.)
2. Ensure it has an alpha channel
3. Select and delete the center region (8px from each edge)
4. Use "Select by Color" to select white/background color
5. Delete selected pixels to make them transparent
6. Export as PNG with transparency

## After Processing

After running the script, your outline sprite sheets will:

- Have transparent centers (backgrounds show through)
- Have transparent backgrounds (only border pixels visible)
- Work correctly with 9-slice rendering in the game
- Display crisp, pixel-perfect borders without stretching corners



