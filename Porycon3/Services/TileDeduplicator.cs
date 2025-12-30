using Porycon3.Models;
using static Porycon3.Infrastructure.TileConstants;

namespace Porycon3.Services;

/// <summary>
/// Deduplicates tiles at the pixel level, detecting flipped variants.
/// When a tile is added, checks if it matches any existing tile (including flipped versions).
/// If a match is found, returns a reference to the existing tile with appropriate flip flags.
/// </summary>
public class TileDeduplicator
{
    private const int PixelsPerTile = TileSize * TileSize;

    // Canonical tiles: hash of unflipped tile → tile ID
    private readonly Dictionary<ulong, int> _canonicalTiles = new();

    // All variant hashes: hash → TileReference (tileId + flip flags to get canonical)
    private readonly Dictionary<ulong, TileReference> _variantLookup = new();

    // Actual tile pixel data (indexed colors 0-15)
    private readonly List<byte[]> _tileData = new();

    /// <summary>
    /// Add a tile and get a reference (potentially to an existing tile with flip flags).
    /// </summary>
    /// <param name="indexedPixels">8x8 tile as indexed color values (0-15)</param>
    /// <returns>TileReference with tile ID and any required flip flags</returns>
    public TileReference AddTile(ReadOnlySpan<byte> indexedPixels)
    {
        if (indexedPixels.Length != PixelsPerTile)
            throw new ArgumentException($"Expected {PixelsPerTile} pixels, got {indexedPixels.Length}");

        var inputHash = ComputeHash(indexedPixels);

        // Check if exact match exists (most common case)
        if (_variantLookup.TryGetValue(inputHash, out var existing))
            return existing;

        // Check all flip variants
        Span<byte> buffer = stackalloc byte[PixelsPerTile];

        // Check horizontal flip
        FlipHorizontal(indexedPixels, buffer);
        var hashH = ComputeHash(buffer);
        if (_canonicalTiles.TryGetValue(hashH, out var matchIdH))
        {
            var reference = new TileReference(matchIdH, true, false);
            _variantLookup[inputHash] = reference;
            return reference;
        }

        // Check vertical flip
        FlipVertical(indexedPixels, buffer);
        var hashV = ComputeHash(buffer);
        if (_canonicalTiles.TryGetValue(hashV, out var matchIdV))
        {
            var reference = new TileReference(matchIdV, false, true);
            _variantLookup[inputHash] = reference;
            return reference;
        }

        // Check both flips (apply H flip to V-flipped buffer)
        FlipHorizontal(buffer, buffer);
        var hashHV = ComputeHash(buffer);
        if (_canonicalTiles.TryGetValue(hashHV, out var matchIdHV))
        {
            var reference = new TileReference(matchIdHV, true, true);
            _variantLookup[inputHash] = reference;
            return reference;
        }

        // New unique tile - add as canonical
        var newId = _tileData.Count;
        var tilePixels = indexedPixels.ToArray();
        _tileData.Add(tilePixels);
        _canonicalTiles[inputHash] = newId;

        // Register all variants for this tile
        RegisterAllVariants(indexedPixels, newId);

        return new TileReference(newId, false, false);
    }

    /// <summary>
    /// Add a tile from a source indexed image at a specific position.
    /// </summary>
    public TileReference AddTileFromImage(
        byte[] indexedPixels,
        int imageWidth,
        int tileId,
        bool sourceFlipH = false,
        bool sourceFlipV = false)
    {
        // Extract tile pixels
        var tilesPerRow = imageWidth / TileSize;
        var tileX = tileId % tilesPerRow;
        var tileY = tileId / tilesPerRow;
        var srcX = tileX * TileSize;
        var srcY = tileY * TileSize;

        Span<byte> tilePixels = stackalloc byte[PixelsPerTile];

        for (int y = 0; y < TileSize; y++)
        {
            int srcYCoord = sourceFlipV ? (TileSize - 1 - y) : y;
            for (int x = 0; x < TileSize; x++)
            {
                int srcXCoord = sourceFlipH ? (TileSize - 1 - x) : x;
                var srcIdx = (srcY + srcYCoord) * imageWidth + (srcX + srcXCoord);
                tilePixels[y * TileSize + x] = indexedPixels[srcIdx];
            }
        }

        return AddTile(tilePixels);
    }

    private void RegisterAllVariants(ReadOnlySpan<byte> pixels, int tileId)
    {
        Span<byte> buffer = stackalloc byte[PixelsPerTile];

        // Original
        _variantLookup[ComputeHash(pixels)] = new TileReference(tileId, false, false);

        // Horizontal flip
        FlipHorizontal(pixels, buffer);
        _variantLookup[ComputeHash(buffer)] = new TileReference(tileId, true, false);

        // Vertical flip
        FlipVertical(pixels, buffer);
        _variantLookup[ComputeHash(buffer)] = new TileReference(tileId, false, true);

        // Both flips
        FlipHorizontal(buffer, buffer);
        _variantLookup[ComputeHash(buffer)] = new TileReference(tileId, true, true);
    }

    private static void FlipHorizontal(ReadOnlySpan<byte> src, Span<byte> dst)
    {
        for (int y = 0; y < TileSize; y++)
        {
            for (int x = 0; x < TileSize; x++)
            {
                dst[y * TileSize + (TileSize - 1 - x)] = src[y * TileSize + x];
            }
        }
    }

    private static void FlipVertical(ReadOnlySpan<byte> src, Span<byte> dst)
    {
        for (int y = 0; y < TileSize; y++)
        {
            for (int x = 0; x < TileSize; x++)
            {
                dst[(TileSize - 1 - y) * TileSize + x] = src[y * TileSize + x];
            }
        }
    }

    /// <summary>
    /// FNV-1a 64-bit hash for tile pixel data.
    /// </summary>
    private static ulong ComputeHash(ReadOnlySpan<byte> pixels)
    {
        ulong hash = 14695981039346656037UL;
        foreach (var b in pixels)
        {
            hash ^= b;
            hash *= 1099511628211UL;
        }
        return hash;
    }

    /// <summary>
    /// Get pixel data for a tile by ID.
    /// </summary>
    public byte[] GetTilePixels(int tileId) => _tileData[tileId];

    /// <summary>
    /// Get total number of unique canonical tiles.
    /// </summary>
    public int UniqueTileCount => _tileData.Count;

    /// <summary>
    /// Get all canonical tile data.
    /// </summary>
    public IReadOnlyList<byte[]> AllTiles => _tileData;
}
