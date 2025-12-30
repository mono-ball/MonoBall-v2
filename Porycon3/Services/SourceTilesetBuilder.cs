using System.IO.Compression;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Porycon3.Infrastructure;
using static Porycon3.Infrastructure.TileConstants;

namespace Porycon3.Services;

/// <summary>
/// Tracks unique 8x8 tiles per source tileset (primary or secondary).
/// Outputs separate tilesheets that minimize duplication across maps.
/// </summary>
public class SourceTilesetBuilder : IDisposable
{

    private readonly string _pokeemeraldPath;
    private readonly TilesetPathResolver _resolver;

    // Per-tileset tracking: tilesetName -> (hash -> localId, tiles list)
    private readonly Dictionary<string, TilesetData> _tilesets = new();
    private readonly object _lock = new();

    // Cache for source tileset pixel data
    private readonly Dictionary<string, (byte[] Pixels, int Width, int Height)?> _sourceCache = new();
    private readonly Dictionary<string, Rgba32[]?[]> _paletteCache = new();

    public SourceTilesetBuilder(string pokeemeraldPath)
    {
        _pokeemeraldPath = pokeemeraldPath;
        _resolver = new TilesetPathResolver(pokeemeraldPath);
    }

    /// <summary>
    /// Track usage of an 8x8 tile. Returns the local tile ID within that tileset's output sheet.
    /// </summary>
    public int TrackTile(
        string tilesetName,
        int sourceTileId,
        int paletteIndex,
        bool flipH,
        bool flipV)
    {
        var tileImage = RenderSourceTile(tilesetName, sourceTileId, paletteIndex);
        if (tileImage == null)
            return 0;

        try
        {
            // Apply flips to get the canonical (non-flipped) version for deduplication
            // We store the base tile and track flip flags separately
            var baseImage = tileImage;
            if (flipH || flipV)
            {
                baseImage = tileImage.Clone();
                if (flipH) baseImage.Mutate(x => x.Flip(FlipMode.Horizontal));
                if (flipV) baseImage.Mutate(x => x.Flip(FlipMode.Vertical));
            }

            var hash = ComputeImageHash(baseImage);

            lock (_lock)
            {
                if (!_tilesets.TryGetValue(tilesetName, out var data))
                {
                    data = new TilesetData();
                    _tilesets[tilesetName] = data;
                }

                // Check if we already have this tile
                if (data.HashToLocalId.TryGetValue(hash, out var existingId))
                {
                    if (baseImage != tileImage) baseImage.Dispose();
                    return existingId;
                }

                // New unique tile
                var localId = data.Tiles.Count;
                data.Tiles.Add(baseImage != tileImage ? baseImage : baseImage.Clone());
                data.HashToLocalId[hash] = localId;

                if (baseImage != tileImage) { } // Already added clone
                else tileImage = null; // Prevent dispose of stored image

                return localId;
            }
        }
        finally
        {
            tileImage?.Dispose();
        }
    }

    /// <summary>
    /// Get all unique tileset names that have been tracked.
    /// </summary>
    public IEnumerable<string> GetTrackedTilesets()
    {
        lock (_lock)
        {
            return _tilesets.Keys.ToList();
        }
    }

    /// <summary>
    /// Get the type (primary/secondary) for a tileset.
    /// </summary>
    public string GetTilesetType(string tilesetName)
    {
        var result = _resolver.FindTilesetPath(tilesetName);
        return result?.Type ?? "unknown";
    }

    /// <summary>
    /// Build the tilesheet image for a specific tileset.
    /// </summary>
    public Image<Rgba32>? BuildTilesheetImage(string tilesetName)
    {
        lock (_lock)
        {
            if (!_tilesets.TryGetValue(tilesetName, out var data) || data.Tiles.Count == 0)
                return null;

            var cols = Math.Min(TilesPerRow, data.Tiles.Count);
            var rows = (data.Tiles.Count + TilesPerRow - 1) / TilesPerRow;

            var tilesheet = new Image<Rgba32>(cols * TileSize, rows * TileSize);

            // Initialize transparent
            tilesheet.ProcessPixelRows(accessor =>
            {
                var transparent = new Rgba32(0, 0, 0, 0);
                for (int y = 0; y < accessor.Height; y++)
                {
                    accessor.GetRowSpan(y).Fill(transparent);
                }
            });

            for (int i = 0; i < data.Tiles.Count; i++)
            {
                var x = (i % TilesPerRow) * TileSize;
                var y = (i / TilesPerRow) * TileSize;
                tilesheet.Mutate(ctx => ctx.DrawImage(data.Tiles[i], new Point(x, y), 1f));
            }

            return tilesheet;
        }
    }

    /// <summary>
    /// Get tile count for a specific tileset.
    /// </summary>
    public int GetTileCount(string tilesetName)
    {
        lock (_lock)
        {
            return _tilesets.TryGetValue(tilesetName, out var data) ? data.Tiles.Count : 0;
        }
    }

    /// <summary>
    /// Render a single 8x8 tile from a source tileset with palette applied.
    /// </summary>
    private Image<Rgba32>? RenderSourceTile(string tilesetName, int tileId, int paletteIndex)
    {
        var indexed = LoadIndexedTileset(tilesetName);
        if (indexed == null) return null;

        var (pixels, width, height) = indexed.Value;
        var tilesPerRow = width / TileSize;
        var maxTileId = (width / TileSize) * (height / TileSize) - 1;

        if (tileId < 0 || tileId > maxTileId) return null;

        var srcX = (tileId % tilesPerRow) * TileSize;
        var srcY = (tileId / tilesPerRow) * TileSize;

        var palettes = LoadPalettes(tilesetName);
        Rgba32[]? palette = null;
        if (palettes != null && paletteIndex >= 0 && paletteIndex < palettes.Length)
        {
            palette = palettes[paletteIndex];
        }

        var tile = new Image<Rgba32>(TileSize, TileSize);

        tile.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < TileSize; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (int x = 0; x < TileSize; x++)
                {
                    var pixelIndex = (srcY + y) * width + (srcX + x);
                    if (pixelIndex >= pixels.Length) continue;

                    var colorIndex = pixels[pixelIndex];

                    if (colorIndex == 0)
                    {
                        row[x] = new Rgba32(0, 0, 0, 0);
                    }
                    else if (palette != null && colorIndex < palette.Length)
                    {
                        row[x] = palette[colorIndex];
                    }
                    else
                    {
                        var gray = (byte)(colorIndex * 17);
                        row[x] = new Rgba32(gray, gray, gray, 255);
                    }
                }
            }
        });

        return tile;
    }

    private (byte[] Pixels, int Width, int Height)? LoadIndexedTileset(string tilesetName)
    {
        lock (_lock)
        {
            if (_sourceCache.TryGetValue(tilesetName, out var cached))
                return cached;
        }

        var imagePath = _resolver.FindTilesetImagePath(tilesetName);
        if (imagePath == null)
        {
            lock (_lock) { _sourceCache[tilesetName] = null; }
            return null;
        }

        try
        {
            var pngBytes = File.ReadAllBytes(imagePath);
            var (indexedPixels, width, height, _) = IndexedPngLoader.ExtractPixelIndices(pngBytes);

            if (indexedPixels == null || width == 0 || height == 0)
            {
                lock (_lock) { _sourceCache[tilesetName] = null; }
                return null;
            }

            var result = (indexedPixels, width, height);
            lock (_lock) { _sourceCache[tilesetName] = result; }
            return result;
        }
        catch
        {
            lock (_lock) { _sourceCache[tilesetName] = null; }
            return null;
        }
    }

    private Rgba32[]?[]? LoadPalettes(string tilesetName)
    {
        lock (_lock)
        {
            if (_paletteCache.TryGetValue(tilesetName, out var cached))
                return cached;
        }

        var result = _resolver.FindTilesetPath(tilesetName);
        if (result == null)
        {
            lock (_lock) { _paletteCache[tilesetName] = Array.Empty<Rgba32[]?>(); }
            return null;
        }

        var palettes = PaletteLoader.LoadTilesetPalettes(result.Value.Path);
        lock (_lock) { _paletteCache[tilesetName] = palettes; }
        return palettes;
    }

    private static ulong ComputeImageHash(Image<Rgba32> image)
    {
        ulong hash = 14695981039346656037UL;
        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                foreach (var pixel in row)
                {
                    hash ^= pixel.R; hash *= 1099511628211UL;
                    hash ^= pixel.G; hash *= 1099511628211UL;
                    hash ^= pixel.B; hash *= 1099511628211UL;
                    hash ^= pixel.A; hash *= 1099511628211UL;
                }
            }
        });
        return hash;
    }

    private static (int Width, int Height, int BitDepth, byte[]? Indices) ExtractPngIndices(byte[] pngData)
    {
        int width = 0, height = 0, bitDepth = 0, colorType = 0;
        var idatChunks = new List<byte[]>();
        var pos = 8;

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
            else if (type == "IEND") break;

            pos += 12 + length;
        }

        if (colorType != 3 || width == 0 || height == 0)
            return (width, height, bitDepth, null);

        var totalLength = idatChunks.Sum(c => c.Length);
        var compressedData = new byte[totalLength];
        var offset = 0;
        foreach (var chunk in idatChunks)
        {
            Array.Copy(chunk, 0, compressedData, offset, chunk.Length);
            offset += chunk.Length;
        }

        byte[] decompressedData;
        try
        {
            using var compressedStream = new MemoryStream(compressedData, 2, compressedData.Length - 2);
            using var deflateStream = new DeflateStream(compressedStream, CompressionMode.Decompress);
            using var decompressedStream = new MemoryStream();
            deflateStream.CopyTo(decompressedStream);
            decompressedData = decompressedStream.ToArray();
        }
        catch { return (width, height, bitDepth, null); }

        var pixelsPerByte = 8 / bitDepth;
        var bytesPerRow = (width + pixelsPerByte - 1) / pixelsPerByte;
        var rowSize = bytesPerRow + 1;

        var indices = new byte[width * height];
        var prevRow = new byte[bytesPerRow];

        for (var y = 0; y < height; y++)
        {
            var rowStart = y * rowSize;
            if (rowStart >= decompressedData.Length) break;

            var filterType = decompressedData[rowStart];
            var currentRow = new byte[bytesPerRow];

            var dataStart = rowStart + 1;
            var copyLen = Math.Min(bytesPerRow, decompressedData.Length - dataStart);
            if (copyLen > 0) Array.Copy(decompressedData, dataStart, currentRow, 0, copyLen);

            switch (filterType)
            {
                case 0: break;
                case 1:
                    for (var i = 1; i < bytesPerRow; i++)
                        currentRow[i] = (byte)(currentRow[i] + currentRow[i - 1]);
                    break;
                case 2:
                    for (var i = 0; i < bytesPerRow; i++)
                        currentRow[i] = (byte)(currentRow[i] + prevRow[i]);
                    break;
                case 3:
                    for (var i = 0; i < bytesPerRow; i++)
                    {
                        var left = i > 0 ? currentRow[i - 1] : 0;
                        currentRow[i] = (byte)(currentRow[i] + (left + prevRow[i]) / 2);
                    }
                    break;
                case 4:
                    for (var i = 0; i < bytesPerRow; i++)
                    {
                        var a = i > 0 ? currentRow[i - 1] : 0;
                        var b = prevRow[i];
                        var c = i > 0 ? prevRow[i - 1] : 0;
                        currentRow[i] = (byte)(currentRow[i] + IndexedPngLoader.PaethPredictor(a, b, c));
                    }
                    break;
            }

            for (var x = 0; x < width; x++)
            {
                int index;
                if (bitDepth == 8) index = x < currentRow.Length ? currentRow[x] : 0;
                else if (bitDepth == 4)
                {
                    var byteIndex = x / 2;
                    var nibble = x % 2;
                    index = byteIndex < currentRow.Length
                        ? (nibble == 0 ? (currentRow[byteIndex] >> 4) & 0x0F : currentRow[byteIndex] & 0x0F)
                        : 0;
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
                else index = 0;

                indices[y * width + x] = (byte)index;
            }

            Array.Copy(currentRow, prevRow, bytesPerRow);
        }

        return (width, height, bitDepth, indices);
    }

    public void Dispose()
    {
        foreach (var data in _tilesets.Values)
        {
            foreach (var tile in data.Tiles)
                tile.Dispose();
        }
        _tilesets.Clear();
    }

    private class TilesetData
    {
        public List<Image<Rgba32>> Tiles { get; } = new();
        public Dictionary<ulong, int> HashToLocalId { get; } = new();
    }
}
