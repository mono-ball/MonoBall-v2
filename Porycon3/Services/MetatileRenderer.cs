using System.IO.Compression;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Porycon3.Models;
using Porycon3.Infrastructure;
using static Porycon3.Infrastructure.TileConstants;

namespace Porycon3.Services;

/// <summary>
/// Renders metatiles as 16x16 pixel images by compositing 8 individual 8x8 tiles
/// with palette application, flip handling, and layer type distribution.
/// </summary>
public class MetatileRenderer : IDisposable
{
    private const int NumTilesInPrimaryVram = 512;

    private readonly string _pokeemeraldPath;
    private readonly TilesetPathResolver _resolver;

    // Cache indexed tile data (palette indices) and dimensions
    private readonly Dictionary<string, (byte[] IndexedPixels, int Width, int Height)?> _tilesetCache = new();
    private readonly Dictionary<string, Rgba32[]?[]> _paletteCache = new();
    private readonly object _cacheLock = new(); // Thread safety for cache access

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
    /// Render a metatile with tile substitutions for animation frames.
    /// Tile IDs in the substitution dictionary are replaced with the provided 8x8 images.
    /// </summary>
    public (Image<Rgba32> Bottom, Image<Rgba32> Top) RenderMetatileWithSubstitution(
        Metatile metatile,
        string primaryTileset,
        string secondaryTileset,
        Dictionary<int, Image<Rgba32>> tileSubstitutions)
    {
        var bottomTiles = metatile.BottomTiles;
        var topTiles = metatile.TopTiles;

        var bottomImage = RenderTileGridWithSubstitution(bottomTiles, primaryTileset, secondaryTileset, tileSubstitutions);
        var topImage = RenderTileGridWithSubstitution(topTiles, primaryTileset, secondaryTileset, tileSubstitutions);

        return (bottomImage, topImage);
    }

    /// <summary>
    /// Render a 2x2 grid of tiles with optional tile substitutions.
    /// </summary>
    private Image<Rgba32> RenderTileGridWithSubstitution(
        TileData[] tiles,
        string primaryTileset,
        string secondaryTileset,
        Dictionary<int, Image<Rgba32>> tileSubstitutions)
    {
        var gridImage = new Image<Rgba32>(MetatileSize, MetatileSize);

        gridImage.ProcessPixelRows(accessor =>
        {
            var transparent = new Rgba32(0, 0, 0, 0);
            for (int y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                row.Fill(transparent);
            }
        });

        var positions = new[] { (0, 0), (TileSize, 0), (0, TileSize), (TileSize, TileSize) };

        for (int i = 0; i < Math.Min(4, tiles.Length); i++)
        {
            var tile = tiles[i];
            var (destX, destY) = positions[i];

            // Check if this tile should be substituted
            if (tileSubstitutions.TryGetValue(tile.TileId, out var substituteTile))
            {
                // Use the substituted tile image (with flip handling)
                RenderSubstituteTile(gridImage, substituteTile, destX, destY, tile.FlipHorizontal, tile.FlipVertical);
                continue;
            }

            // Normal tile rendering (same as RenderTileGrid)
            string tilesetName;
            int actualTileId;

            if (tile.TileId < NumTilesInPrimaryVram)
            {
                tilesetName = primaryTileset;
                actualTileId = tile.TileId;
            }
            else
            {
                tilesetName = secondaryTileset;
                actualTileId = tile.TileId - NumTilesInPrimaryVram;
            }

            var tilesetData = LoadIndexedTileset(tilesetName);
            if (tilesetData == null)
            {
                if (tilesetName != primaryTileset)
                {
                    tilesetData = LoadIndexedTileset(primaryTileset);
                }
                if (tilesetData == null)
                    continue;
            }

            var (indexedPixels, tilesetWidth, tilesetHeight) = tilesetData.Value;
            var tilesPerRow = tilesetWidth / TileSize;
            var tilesPerCol = tilesetHeight / TileSize;
            var maxTileId = tilesPerRow * tilesPerCol - 1;

            if (actualTileId < 0 || actualTileId > maxTileId)
                continue;

            var paletteSourceTileset = tile.PaletteIndex >= 6 ? secondaryTileset : primaryTileset;
            var palettes = LoadPalettes(paletteSourceTileset);
            Rgba32[]? palette = null;

            if (palettes != null && tile.PaletteIndex >= 0 && tile.PaletteIndex < palettes.Length)
            {
                palette = palettes[tile.PaletteIndex];
            }

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
    /// Render a substituted tile image to the grid with flip handling.
    /// </summary>
    private void RenderSubstituteTile(
        Image<Rgba32> gridImage,
        Image<Rgba32> sourceTile,
        int destX,
        int destY,
        bool flipH,
        bool flipV)
    {
        // Clone and apply flips if needed
        using var tile = sourceTile.Clone();

        if (flipH && flipV)
            tile.Mutate(x => x.RotateFlip(RotateMode.Rotate180, FlipMode.None));
        else if (flipH)
            tile.Mutate(x => x.Flip(FlipMode.Horizontal));
        else if (flipV)
            tile.Mutate(x => x.Flip(FlipMode.Vertical));

        // Copy pixels
        tile.ProcessPixelRows(gridImage, (srcAccessor, destAccessor) =>
        {
            for (int y = 0; y < Math.Min(TileSize, srcAccessor.Height); y++)
            {
                var srcRow = srcAccessor.GetRowSpan(y);
                if (destY + y >= destAccessor.Height) continue;
                var destRow = destAccessor.GetRowSpan(destY + y);

                for (int x = 0; x < Math.Min(TileSize, srcAccessor.Width); x++)
                {
                    if (destX + x >= destRow.Length) continue;
                    var pixel = srcRow[x];
                    // Only copy non-transparent pixels (blend over existing)
                    if (pixel.A > 0)
                    {
                        destRow[destX + x] = pixel;
                    }
                }
            }
        });
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

        // Initialize with transparent pixels (ImageSharp defaults to black)
        gridImage.ProcessPixelRows(accessor =>
        {
            var transparent = new Rgba32(0, 0, 0, 0);
            for (int y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                row.Fill(transparent);
            }
        });

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
    /// Load tileset as indexed pixel data (palette indices 0-15). Thread-safe.
    /// </summary>
    private (byte[] IndexedPixels, int Width, int Height)? LoadIndexedTileset(string tilesetName)
    {
        lock (_cacheLock)
        {
            if (_tilesetCache.TryGetValue(tilesetName, out var cached))
                return cached;
        }

        var imagePath = _resolver.FindTilesetImagePath(tilesetName);
        if (imagePath == null)
        {
            lock (_cacheLock)
            {
                _tilesetCache[tilesetName] = null;
            }
            return null;
        }

        try
        {
            // Load the PNG file and extract raw palette indices
            var pngBytes = File.ReadAllBytes(imagePath);
            var (indexedPixels, width, height, bitDepth) = IndexedPngLoader.ExtractPixelIndices(pngBytes);

            if (indexedPixels == null || width == 0 || height == 0)
            {
                // Fallback: PNG is not indexed, try to use RGBA with grayscale heuristic
                using var image = Image.Load<Rgba32>(imagePath);
                width = image.Width;
                height = image.Height;
                indexedPixels = new byte[width * height];

                image.ProcessPixelRows(accessor =>
                {
                    for (int y = 0; y < height; y++)
                    {
                        var row = accessor.GetRowSpan(y);
                        for (int x = 0; x < width; x++)
                        {
                            var pixel = row[x];
                            // Fallback for non-indexed: use grayscale heuristic
                            indexedPixels[y * width + x] = (byte)(15 - (pixel.R + 8) / 17);
                        }
                    }
                });
            }

            var result = (indexedPixels, width, height);
            lock (_cacheLock)
            {
                _tilesetCache[tilesetName] = result;
            }
            return result;
        }
        catch
        {
            lock (_cacheLock)
            {
                _tilesetCache[tilesetName] = null;
            }
            return null;
        }
    }

    /// <summary>
    /// Load palettes for a tileset with caching. Thread-safe.
    /// </summary>
    private Rgba32[]?[]? LoadPalettes(string tilesetName)
    {
        lock (_cacheLock)
        {
            if (_paletteCache.TryGetValue(tilesetName, out var cached))
                return cached;
        }

        var result = _resolver.FindTilesetPath(tilesetName);
        if (result == null)
        {
            lock (_cacheLock)
            {
                _paletteCache[tilesetName] = Array.Empty<Rgba32[]?>();
            }
            return null;
        }

        var palettes = PaletteLoader.LoadTilesetPalettes(result.Value.Path);
        lock (_cacheLock)
        {
            _paletteCache[tilesetName] = palettes;
        }
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

    /// <summary>
    /// Extract raw palette indices from PNG IDAT chunks.
    /// Handles PNG decompression and filtering to get actual palette indices.
    /// </summary>
    private static (int Width, int Height, int BitDepth, byte[]? Indices) ExtractPngIndices(byte[] pngData)
    {
        int width = 0, height = 0, bitDepth = 0, colorType = 0;
        var idatChunks = new List<byte[]>();
        var pos = 8; // Skip PNG signature

        // Parse chunks to get IHDR and IDAT data
        while (pos < pngData.Length - 12)
        {
            var length = (pngData[pos] << 24) | (pngData[pos + 1] << 16) |
                         (pngData[pos + 2] << 8) | pngData[pos + 3];
            var type = System.Text.Encoding.ASCII.GetString(pngData, pos + 4, 4);

            if (type == "IHDR")
            {
                var dataStart = pos + 8;
                width = (pngData[dataStart] << 24) | (pngData[dataStart + 1] << 16) |
                        (pngData[dataStart + 2] << 8) | pngData[dataStart + 3];
                height = (pngData[dataStart + 4] << 24) | (pngData[dataStart + 5] << 16) |
                         (pngData[dataStart + 6] << 8) | pngData[dataStart + 7];
                bitDepth = pngData[dataStart + 8];
                colorType = pngData[dataStart + 9];
            }
            else if (type == "IDAT")
            {
                var chunk = new byte[length];
                Array.Copy(pngData, pos + 8, chunk, 0, length);
                idatChunks.Add(chunk);
            }
            else if (type == "IEND")
            {
                break;
            }

            pos += 12 + length;
        }

        // Only handle indexed color (colorType 3)
        if (colorType != 3 || width == 0 || height == 0)
        {
            return (width, height, bitDepth, null);
        }

        // Combine all IDAT chunks
        var totalLength = idatChunks.Sum(c => c.Length);
        var compressedData = new byte[totalLength];
        var offset = 0;
        foreach (var chunk in idatChunks)
        {
            Array.Copy(chunk, 0, compressedData, offset, chunk.Length);
            offset += chunk.Length;
        }

        // Decompress zlib data (skip first 2 bytes - zlib header)
        byte[] decompressedData;
        try
        {
            using var compressedStream = new MemoryStream(compressedData, 2, compressedData.Length - 2);
            using var deflateStream = new DeflateStream(compressedStream, CompressionMode.Decompress);
            using var decompressedStream = new MemoryStream();
            deflateStream.CopyTo(decompressedStream);
            decompressedData = decompressedStream.ToArray();
        }
        catch
        {
            return (width, height, bitDepth, null);
        }

        // Calculate bytes per row (including filter byte)
        var pixelsPerByte = 8 / bitDepth;
        var bytesPerRow = (width + pixelsPerByte - 1) / pixelsPerByte;
        var rowSize = bytesPerRow + 1; // +1 for filter byte

        // Un-filter and extract palette indices
        var indices = new byte[width * height];
        var prevRow = new byte[bytesPerRow];

        for (var y = 0; y < height; y++)
        {
            var rowStart = y * rowSize;
            if (rowStart >= decompressedData.Length) break;

            var filterType = decompressedData[rowStart];
            var currentRow = new byte[bytesPerRow];

            // Copy row data
            var dataStart = rowStart + 1;
            var copyLen = Math.Min(bytesPerRow, decompressedData.Length - dataStart);
            if (copyLen > 0)
            {
                Array.Copy(decompressedData, dataStart, currentRow, 0, copyLen);
            }

            // Apply PNG filter
            switch (filterType)
            {
                case 0: // None
                    break;
                case 1: // Sub
                    for (var i = 1; i < bytesPerRow; i++)
                        currentRow[i] = (byte)(currentRow[i] + currentRow[i - 1]);
                    break;
                case 2: // Up
                    for (var i = 0; i < bytesPerRow; i++)
                        currentRow[i] = (byte)(currentRow[i] + prevRow[i]);
                    break;
                case 3: // Average
                    for (var i = 0; i < bytesPerRow; i++)
                    {
                        var left = i > 0 ? currentRow[i - 1] : 0;
                        currentRow[i] = (byte)(currentRow[i] + (left + prevRow[i]) / 2);
                    }
                    break;
                case 4: // Paeth
                    for (var i = 0; i < bytesPerRow; i++)
                    {
                        var a = i > 0 ? currentRow[i - 1] : 0;
                        var b = prevRow[i];
                        var c = i > 0 ? prevRow[i - 1] : 0;
                        currentRow[i] = (byte)(currentRow[i] + IndexedPngLoader.PaethPredictor(a, b, c));
                    }
                    break;
            }

            // Extract palette indices from row
            for (var x = 0; x < width; x++)
            {
                int index;
                if (bitDepth == 8)
                {
                    index = x < currentRow.Length ? currentRow[x] : 0;
                }
                else if (bitDepth == 4)
                {
                    var byteIndex = x / 2;
                    var nibble = x % 2;
                    if (byteIndex < currentRow.Length)
                    {
                        index = nibble == 0
                            ? (currentRow[byteIndex] >> 4) & 0x0F
                            : currentRow[byteIndex] & 0x0F;
                    }
                    else
                    {
                        index = 0;
                    }
                }
                else if (bitDepth == 2)
                {
                    var byteIndex = x / 4;
                    var shift = 6 - (x % 4) * 2;
                    index = byteIndex < currentRow.Length ? (currentRow[byteIndex] >> shift) & 0x03 : 0;
                }
                else if (bitDepth == 1)
                {
                    var byteIndex = x / 8;
                    var shift = 7 - (x % 8);
                    index = byteIndex < currentRow.Length ? (currentRow[byteIndex] >> shift) & 0x01 : 0;
                }
                else
                {
                    index = 0;
                }

                indices[y * width + x] = (byte)index;
            }

            Array.Copy(currentRow, prevRow, bytesPerRow);
        }

        return (width, height, bitDepth, indices);
    }

    public void Dispose()
    {
        _tilesetCache.Clear();
        _paletteCache.Clear();
    }
}
