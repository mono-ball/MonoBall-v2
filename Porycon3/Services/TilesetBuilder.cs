using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Porycon3.Models;
using Porycon3.Infrastructure;
using static Porycon3.Infrastructure.TileConstants;

namespace Porycon3.Services;

/// <summary>
/// Tile key for deduplication - identifies a unique tile by its source and rendering properties.
/// </summary>
public readonly record struct TileKey(
    string TilesetName,
    int TileId,
    int PaletteIndex,
    bool FlipH,
    bool FlipV);

/// <summary>
/// Builds optimized tilesets by collecting used tiles and creating
/// a deduplicated tileset image with proper GID mapping.
/// </summary>
public class TilesetBuilder
{
    private readonly string _pokeemeraldPath;
    private readonly TilesetPathResolver _resolver;
    private readonly TilesetImageLoader _imageLoader;

    // Track used tiles: (TilesetName, TileId, PaletteIndex) -> list of usages
    private readonly HashSet<TileKey> _usedTiles = new();

    // Track tileset relationships for palette loading
    private readonly Dictionary<string, string> _secondaryToPrimaryMap = new();

    // Output mapping: TileKey -> GID (1-based)
    private readonly Dictionary<TileKey, int> _tileToGid = new();

    private const int NumTilesInPrimaryVram = 512;

    public TilesetBuilder(string pokeemeraldPath)
    {
        _pokeemeraldPath = pokeemeraldPath;
        _resolver = new TilesetPathResolver(pokeemeraldPath);
        _imageLoader = new TilesetImageLoader(pokeemeraldPath);
    }

    /// <summary>
    /// Record that a tile is used (for a specific tileset/palette combination).
    /// </summary>
    public void AddUsedTile(
        string tilesetName,
        int tileId,
        int paletteIndex,
        bool flipH = false,
        bool flipV = false)
    {
        var key = new TileKey(tilesetName, tileId, paletteIndex, flipH, flipV);
        _usedTiles.Add(key);
    }

    /// <summary>
    /// Record a primary/secondary tileset relationship for palette loading.
    /// </summary>
    public void RecordTilesetRelationship(string primaryTileset, string secondaryTileset)
    {
        _secondaryToPrimaryMap[NormalizeTilesetName(secondaryTileset)] = NormalizeTilesetName(primaryTileset);
    }

    /// <summary>
    /// Build tileset image and return tile mapping.
    /// </summary>
    public (Image<Rgba32> Image, Dictionary<TileKey, int> Mapping) BuildTileset(string tilesetName)
    {
        var normalizedName = NormalizeTilesetName(tilesetName);

        // Get tiles used for this tileset
        var tilesForTileset = _usedTiles
            .Where(t => NormalizeTilesetName(t.TilesetName) == normalizedName)
            .OrderBy(t => t.TileId)
            .ThenBy(t => t.PaletteIndex)
            .ThenBy(t => t.FlipH)
            .ThenBy(t => t.FlipV)
            .ToList();

        if (tilesForTileset.Count == 0)
        {
            // Return empty tileset
            var emptyImg = new Image<Rgba32>(TileSize, TileSize, new Rgba32(0, 0, 0, 0));
            return (emptyImg, new Dictionary<TileKey, int>());
        }

        // Load source tileset image
        using var sourceImage = _imageLoader.LoadTilesetImage(tilesetName);
        if (sourceImage == null)
        {
            var emptyImg = new Image<Rgba32>(TileSize, TileSize, new Rgba32(0, 0, 0, 0));
            return (emptyImg, new Dictionary<TileKey, int>());
        }

        // Load palettes
        var tilesetResult = _resolver.FindTilesetPath(tilesetName);
        Rgba32[]?[] palettes = new Rgba32[]?[16];
        Rgba32[]?[] primaryPalettes = new Rgba32[]?[16];
        bool isSecondary = tilesetResult?.Type == "secondary";

        if (tilesetResult != null)
        {
            palettes = PaletteLoader.LoadTilesetPalettes(tilesetResult.Value.Path);
        }

        // Load primary tileset palettes if this is secondary
        if (isSecondary && _secondaryToPrimaryMap.TryGetValue(normalizedName, out var primaryName))
        {
            var primaryResult = _resolver.FindTilesetPath(primaryName);
            if (primaryResult != null)
            {
                primaryPalettes = PaletteLoader.LoadTilesetPalettes(primaryResult.Value.Path);
            }
        }
        else
        {
            primaryPalettes = palettes;
        }

        // Calculate output image dimensions
        var numTiles = tilesForTileset.Count;
        var cols = Math.Min(TilesPerRow, numTiles);
        var rows = (numTiles + TilesPerRow - 1) / TilesPerRow;

        var outputImage = new Image<Rgba32>(cols * TileSize, rows * TileSize, new Rgba32(0, 0, 0, 0));
        var mapping = new Dictionary<TileKey, int>();

        // Extract and place tiles
        var gid = 1; // Tiled uses 1-based GIDs
        for (int i = 0; i < tilesForTileset.Count; i++)
        {
            var tileKey = tilesForTileset[i];

            // Get the correct palette
            var palette = _imageLoader.GetPaletteForTile(
                tileKey.PaletteIndex,
                primaryPalettes,
                palettes,
                isSecondary);

            // Extract tile with palette applied
            using var tileImage = _imageLoader.ExtractTile(
                sourceImage,
                tileKey.TileId,
                palette,
                tileKey.FlipH,
                tileKey.FlipV);

            if (tileImage != null)
            {
                // Calculate position in output image
                var outX = (i % cols) * TileSize;
                var outY = (i / cols) * TileSize;

                // Copy tile to output image
                for (int y = 0; y < TileSize; y++)
                {
                    for (int x = 0; x < TileSize; x++)
                    {
                        outputImage[outX + x, outY + y] = tileImage[x, y];
                    }
                }
            }

            mapping[tileKey] = gid++;
        }

        return (outputImage, mapping);
    }

    /// <summary>
    /// Get the GID for a tile, or 0 if not found.
    /// </summary>
    public int GetGid(TileKey key)
    {
        return _tileToGid.TryGetValue(key, out var gid) ? gid : 0;
    }

    /// <summary>
    /// Store built mapping for later lookup.
    /// </summary>
    public void StoreMapping(Dictionary<TileKey, int> mapping)
    {
        foreach (var (key, gid) in mapping)
        {
            _tileToGid[key] = gid;
        }
    }

    /// <summary>
    /// Get all unique tilesets that have used tiles.
    /// </summary>
    public IEnumerable<string> GetUsedTilesets()
    {
        return _usedTiles
            .Select(t => NormalizeTilesetName(t.TilesetName))
            .Distinct()
            .OrderBy(n => n);
    }

    /// <summary>
    /// Clear all tracking data.
    /// </summary>
    public void Reset()
    {
        _usedTiles.Clear();
        _secondaryToPrimaryMap.Clear();
        _tileToGid.Clear();
    }

    private static string NormalizeTilesetName(string name)
    {
        if (name.StartsWith("gTileset_", StringComparison.OrdinalIgnoreCase))
            name = name[9..];
        return name.ToLowerInvariant();
    }
}
