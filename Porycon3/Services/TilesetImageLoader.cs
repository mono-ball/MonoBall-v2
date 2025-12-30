using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Porycon3.Infrastructure;
using static Porycon3.Infrastructure.TileConstants;

namespace Porycon3.Services;

/// <summary>
/// Loads tileset images and extracts individual tiles with palette application.
/// </summary>
public class TilesetImageLoader
{
    private readonly TilesetPathResolver _resolver;

    public TilesetImageLoader(string pokeemeraldPath)
    {
        _resolver = new TilesetPathResolver(pokeemeraldPath);
    }

    /// <summary>
    /// Load tileset image as indexed color (keeping pixel values as palette indices).
    /// </summary>
    public byte[]? LoadIndexedTiles(string tilesetName, out int width, out int height)
    {
        width = height = 0;

        var imagePath = _resolver.FindTilesetImagePath(tilesetName);
        if (imagePath == null)
            return null;

        try
        {
            using var image = Image.Load<Rgba32>(imagePath);
            var imgWidth = image.Width;
            var imgHeight = image.Height;

            // Convert to indexed color (extract pixel values as 0-15)
            // pokeemerald tiles.png are 4bpp indexed PNGs
            var pixels = new byte[imgWidth * imgHeight];

            image.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < imgHeight; y++)
                {
                    var row = accessor.GetRowSpan(y);
                    for (int x = 0; x < imgWidth; x++)
                    {
                        // For indexed PNGs, we need the actual palette index
                        // Since ImageSharp loads as RGBA, we need to reverse-map
                        // For GBA tiles, pixel values are 0-15
                        // The R value often contains the index for grayscale indexed images
                        pixels[y * imgWidth + x] = (byte)(row[x].R / 16);
                    }
                }
            });

            width = imgWidth;
            height = imgHeight;
            return pixels;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Load tileset as RGBA image preserving original palette.
    /// </summary>
    public Image<Rgba32>? LoadTilesetImage(string tilesetName)
    {
        var imagePath = _resolver.FindTilesetImagePath(tilesetName);
        if (imagePath == null)
            return null;

        try
        {
            return Image.Load<Rgba32>(imagePath);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Extract a single tile from the source image and apply a palette.
    /// </summary>
    public Image<Rgba32>? ExtractTile(
        Image<Rgba32> sourceImage,
        int tileId,
        Rgba32[]? palette,
        bool flipH = false,
        bool flipV = false)
    {
        var tilesPerRow = sourceImage.Width / TileSize;
        var tileX = tileId % tilesPerRow;
        var tileY = tileId / tilesPerRow;

        if (tileX < 0 || tileY < 0 ||
            (tileX + 1) * TileSize > sourceImage.Width ||
            (tileY + 1) * TileSize > sourceImage.Height)
            return null;

        var tile = new Image<Rgba32>(TileSize, TileSize);
        var srcX = tileX * TileSize;
        var srcY = tileY * TileSize;

        sourceImage.ProcessPixelRows(tile, (srcAccessor, dstAccessor) =>
        {
            for (int y = 0; y < TileSize; y++)
            {
                var srcRow = srcAccessor.GetRowSpan(srcY + y);
                var dstRow = dstAccessor.GetRowSpan(flipV ? (TileSize - 1 - y) : y);

                for (int x = 0; x < TileSize; x++)
                {
                    var srcPixel = srcRow[srcX + x];
                    var dstX = flipH ? (TileSize - 1 - x) : x;

                    if (palette != null)
                    {
                        // Apply palette: pixel value is palette index
                        // For indexed images, we interpret R as the index (0-15)
                        var colorIndex = srcPixel.R / 16;
                        if (colorIndex < palette.Length)
                        {
                            dstRow[dstX] = palette[colorIndex];
                        }
                        else
                        {
                            dstRow[dstX] = srcPixel;
                        }
                    }
                    else
                    {
                        dstRow[dstX] = srcPixel;
                    }
                }
            }
        });

        return tile;
    }

    /// <summary>
    /// Get the palette to use for a tile based on palette index and tileset type.
    /// In Pokemon Emerald:
    /// - Palette indices 0-5 come from primary tileset
    /// - Palette indices 6-12 come from secondary tileset
    /// </summary>
    public Rgba32[]? GetPaletteForTile(
        int paletteIndex,
        Rgba32[]?[] primaryPalettes,
        Rgba32[]?[] secondaryPalettes,
        bool isSecondaryTileset)
    {
        if (paletteIndex < 0 || paletteIndex > 15)
            return null;

        // For primary tileset, always use its own palettes
        if (!isSecondaryTileset)
        {
            return primaryPalettes[paletteIndex];
        }

        // For secondary tileset:
        // - Palettes 0-5 come from primary tileset
        // - Palettes 6-12 come from secondary tileset
        if (paletteIndex < 6)
        {
            return primaryPalettes[paletteIndex];
        }
        else
        {
            return secondaryPalettes[paletteIndex];
        }
    }

    /// <summary>
    /// Get total number of tiles in a tileset image.
    /// </summary>
    public int GetTileCount(Image<Rgba32> sourceImage)
    {
        var tilesPerRow = sourceImage.Width / TileSize;
        var tilesPerCol = sourceImage.Height / TileSize;
        return tilesPerRow * tilesPerCol;
    }
}
