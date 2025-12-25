using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Porycon3.Models;
using Porycon3.Infrastructure;

namespace Porycon3.Services;

/// <summary>
/// Renders metatiles as 16x16 pixel images by compositing 8 individual 8x8 tiles
/// with palette application, flip handling, and layer type distribution.
/// </summary>
public class MetatileRenderer : IDisposable
{
    private const int TileSize = 8;
    private const int MetatileSize = 16;
    private const int NumTilesInPrimaryVram = 512;

    private readonly string _pokeemeraldPath;
    private readonly TilesetPathResolver _resolver;

    // Cache indexed tile data (palette indices) and dimensions
    private readonly Dictionary<string, (byte[] IndexedPixels, int Width, int Height)?> _tilesetCache = new();
    private readonly Dictionary<string, Rgba32[]?[]> _paletteCache = new();

    public MetatileRenderer(string pokeemeraldPath)
    {
        _pokeemeraldPath = pokeemeraldPath;
        _resolver = new TilesetPathResolver(pokeemeraldPath);
    }

    /// <summary>
    /// Render a metatile as two 16x16 images (bottom layer and top layer).
    /// </summary>
    public (Image<Rgba32> Bottom, Image<Rgba32> Top) RenderMetatile(
        Metatile metatile,
        string primaryTileset,
        string secondaryTileset)
    {
        // Split into bottom (tiles 0-3) and top (tiles 4-7)
        var bottomTiles = metatile.BottomTiles;
        var topTiles = metatile.TopTiles;

        // Render each as 2x2 grid of 8x8 tiles -> 16x16 image
        var bottomImage = RenderTileGrid(bottomTiles, primaryTileset, secondaryTileset);
        var topImage = RenderTileGrid(topTiles, primaryTileset, secondaryTileset);

        return (bottomImage, topImage);
    }

    /// <summary>
    /// Render a 2x2 grid of tiles into a 16x16 image.
    /// </summary>
    private Image<Rgba32> RenderTileGrid(
        TileData[] tiles,
        string primaryTileset,
        string secondaryTileset)
    {
        var gridImage = new Image<Rgba32>(MetatileSize, MetatileSize);

        // Positions for 4 tiles: TL, TR, BL, BR
        var positions = new[] { (0, 0), (TileSize, 0), (0, TileSize), (TileSize, TileSize) };

        for (int i = 0; i < Math.Min(4, tiles.Length); i++)
        {
            var tile = tiles[i];
            var (destX, destY) = positions[i];

            // Determine which tileset and tile ID to use
            string tilesetName;
            int actualTileId;

            if (tile.TileId < NumTilesInPrimaryVram)
            {
                // Tiles 0-511 come from primary tileset
                tilesetName = primaryTileset;
                actualTileId = tile.TileId;
            }
            else
            {
                // Tiles 512+ come from secondary tileset (offset by 512)
                tilesetName = secondaryTileset;
                actualTileId = tile.TileId - NumTilesInPrimaryVram;
            }

            // Load tileset indexed data
            var tilesetData = LoadIndexedTileset(tilesetName);
            if (tilesetData == null)
            {
                // Try fallback to primary if secondary failed
                if (tilesetName != primaryTileset)
                {
                    tilesetData = LoadIndexedTileset(primaryTileset);
                }
                if (tilesetData == null)
                    continue;
            }

            var (indexedPixels, tilesetWidth, tilesetHeight) = tilesetData.Value;

            // Validate tile ID bounds
            var tilesPerRow = tilesetWidth / TileSize;
            var tilesPerCol = tilesetHeight / TileSize;
            var maxTileId = tilesPerRow * tilesPerCol - 1;

            if (actualTileId < 0 || actualTileId > maxTileId)
                continue;

            // Determine palette source
            // Palette indices 0-5 come from primary tileset
            // Palette indices 6-12 come from secondary tileset
            var paletteSourceTileset = tile.PaletteIndex >= 6 ? secondaryTileset : primaryTileset;
            var palettes = LoadPalettes(paletteSourceTileset);
            Rgba32[]? palette = null;

            if (palettes != null && tile.PaletteIndex >= 0 && tile.PaletteIndex < palettes.Length)
            {
                palette = palettes[tile.PaletteIndex];
            }

            // Extract and render the tile with palette applied
            RenderTileToGrid(
                gridImage,
                indexedPixels,
                tilesetWidth,
                actualTileId,
                destX,
                destY,
                palette,
                tile.FlipHorizontal,
                tile.FlipVertical);
        }

        return gridImage;
    }

    /// <summary>
    /// Load tileset as indexed pixel data (palette indices 0-15).
    /// </summary>
    private (byte[] IndexedPixels, int Width, int Height)? LoadIndexedTileset(string tilesetName)
    {
        if (_tilesetCache.TryGetValue(tilesetName, out var cached))
            return cached;

        var imagePath = _resolver.FindTilesetImagePath(tilesetName);
        if (imagePath == null)
        {
            _tilesetCache[tilesetName] = null;
            return null;
        }

        try
        {
            // Load the image - pokeemerald tiles.png files are indexed color PNGs
            using var image = Image.Load<Rgba32>(imagePath);
            var width = image.Width;
            var height = image.Height;

            // Extract indexed color values from the image
            // In indexed PNGs loaded by ImageSharp, we need to look at pixel values
            // The grayscale values represent palette indices (0-15 mapped to grayscale)
            var indexedPixels = new byte[width * height];

            image.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < height; y++)
                {
                    var row = accessor.GetRowSpan(y);
                    for (int x = 0; x < width; x++)
                    {
                        var pixel = row[x];
                        // pokeemerald tiles.png are 4bpp indexed images with inverted grayscale:
                        // Index 0 = white (R=255), Index 15 = black (R=0)
                        // Formula: 15 - round(R/17) with +8 for rounding
                        indexedPixels[y * width + x] = (byte)(15 - (pixel.R + 8) / 17);
                    }
                }
            });

            var result = (indexedPixels, width, height);
            _tilesetCache[tilesetName] = result;
            return result;
        }
        catch
        {
            _tilesetCache[tilesetName] = null;
            return null;
        }
    }

    /// <summary>
    /// Load palettes for a tileset with caching.
    /// </summary>
    private Rgba32[]?[]? LoadPalettes(string tilesetName)
    {
        if (_paletteCache.TryGetValue(tilesetName, out var cached))
            return cached;

        var result = _resolver.FindTilesetPath(tilesetName);
        if (result == null)
        {
            _paletteCache[tilesetName] = Array.Empty<Rgba32[]?>();
            return null;
        }

        var palettes = PaletteLoader.LoadTilesetPalettes(result.Value.Path);
        _paletteCache[tilesetName] = palettes;
        return palettes;
    }

    /// <summary>
    /// Render a single tile from indexed data to the grid image with palette and flips applied.
    /// </summary>
    private void RenderTileToGrid(
        Image<Rgba32> gridImage,
        byte[] indexedPixels,
        int tilesetWidth,
        int tileId,
        int destX,
        int destY,
        Rgba32[]? palette,
        bool flipH,
        bool flipV)
    {
        var tilesPerRow = tilesetWidth / TileSize;
        var srcTileX = (tileId % tilesPerRow) * TileSize;
        var srcTileY = (tileId / tilesPerRow) * TileSize;

        gridImage.ProcessPixelRows(accessor =>
        {
            for (int ty = 0; ty < TileSize; ty++)
            {
                // Apply vertical flip
                int srcY = flipV ? (TileSize - 1 - ty) : ty;
                int gridY = destY + ty;

                if (gridY >= accessor.Height)
                    continue;

                var row = accessor.GetRowSpan(gridY);

                for (int tx = 0; tx < TileSize; tx++)
                {
                    // Apply horizontal flip
                    int srcX = flipH ? (TileSize - 1 - tx) : tx;
                    int gridX = destX + tx;

                    if (gridX >= accessor.Width)
                        continue;

                    // Get palette index from indexed pixels
                    var pixelIndex = (srcTileY + srcY) * tilesetWidth + (srcTileX + srcX);
                    if (pixelIndex >= indexedPixels.Length)
                        continue;

                    var colorIndex = indexedPixels[pixelIndex];

                    // Color index 0 is always transparent in GBA
                    if (colorIndex == 0)
                    {
                        row[gridX] = new Rgba32(0, 0, 0, 0);
                    }
                    else if (palette != null && colorIndex < palette.Length)
                    {
                        row[gridX] = palette[colorIndex];
                    }
                    else
                    {
                        // No palette or out of range - use grayscale fallback
                        var gray = (byte)(colorIndex * 17);
                        row[gridX] = new Rgba32(gray, gray, gray, 255);
                    }
                }
            }
        });
    }

    public void Dispose()
    {
        _tilesetCache.Clear();
        _paletteCache.Clear();
    }
}
