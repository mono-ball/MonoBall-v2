using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System.Text.Json;

namespace Porycon3.Services;

/// <summary>
/// Orchestrates tileset generation for converted maps.
/// </summary>
public class TilesetGenerationService
{
    private readonly TilesetBuilder _tilesetBuilder;
    private readonly string _outputPath;
    private readonly string _region;

    private const int TileSize = 8;
    private const int TilesPerRow = 16;

    public TilesetGenerationService(string pokeemeraldPath, string outputPath, string region)
    {
        _tilesetBuilder = new TilesetBuilder(pokeemeraldPath);
        _outputPath = outputPath;
        _region = region;
    }

    /// <summary>
    /// Get the tileset builder for adding used tiles.
    /// </summary>
    public TilesetBuilder TilesetBuilder => _tilesetBuilder;

    /// <summary>
    /// Generate all tilesets and return mapping for use in map conversion.
    /// </summary>
    public Dictionary<TileKey, int> GenerateAllTilesets()
    {
        var allMappings = new Dictionary<TileKey, int>();
        var tilesetDir = Path.Combine(_outputPath, "Definitions", "Assets", "Tilesets", _region);
        Directory.CreateDirectory(tilesetDir);

        foreach (var tilesetName in _tilesetBuilder.GetUsedTilesets())
        {
            var (image, mapping) = _tilesetBuilder.BuildTileset(tilesetName);

            if (mapping.Count > 0)
            {
                // Save tileset image
                var imagePath = Path.Combine(tilesetDir, $"{tilesetName}.png");
                image.SaveAsPng(imagePath);

                // Save tileset JSON definition
                var jsonPath = Path.Combine(tilesetDir, $"{tilesetName}.json");
                var tilesetJson = CreateTilesetJson(tilesetName, image, mapping.Count);
                File.WriteAllText(jsonPath, tilesetJson);

                // Store mapping
                _tilesetBuilder.StoreMapping(mapping);
                foreach (var (key, gid) in mapping)
                {
                    allMappings[key] = gid;
                }
            }

            image.Dispose();
        }

        return allMappings;
    }

    /// <summary>
    /// Generate a single per-map tileset and return the mapping.
    /// </summary>
    public (string TilesetId, Dictionary<TileKey, int> Mapping) GenerateMapTileset(
        string mapName,
        IEnumerable<TileKey> usedTiles)
    {
        var normalizedName = IdTransformer.Normalize(mapName);
        var tilesetId = $"base:tileset:{_region}/{normalizedName}";

        var tilesetDir = Path.Combine(_outputPath, "Definitions", "Assets", "Tilesets", _region);
        Directory.CreateDirectory(tilesetDir);

        // Add tiles to builder
        foreach (var key in usedTiles)
        {
            _tilesetBuilder.AddUsedTile(
                key.TilesetName,
                key.TileId,
                key.PaletteIndex,
                key.FlipH,
                key.FlipV);
        }

        // Get unique tilesets used
        var tilesetNames = usedTiles
            .Select(t => t.TilesetName)
            .Distinct()
            .ToList();

        // Build combined tileset image for this map
        var allTiles = usedTiles
            .OrderBy(t => t.TilesetName)
            .ThenBy(t => t.TileId)
            .ThenBy(t => t.PaletteIndex)
            .ThenBy(t => t.FlipH)
            .ThenBy(t => t.FlipV)
            .Distinct()
            .ToList();

        if (allTiles.Count == 0)
        {
            return (tilesetId, new Dictionary<TileKey, int>());
        }

        // Calculate dimensions
        var numTiles = allTiles.Count;
        var cols = Math.Min(TilesPerRow, numTiles);
        var rows = (numTiles + TilesPerRow - 1) / TilesPerRow;

        // For now, we'll build separate tilesets per-map using the builder
        // Each tileset will contain only the tiles used by that map
        var mapping = new Dictionary<TileKey, int>();

        // Build tilesets for each source tileset used
        var gidOffset = 1;
        foreach (var tilesetName in tilesetNames)
        {
            var (image, tilesetMapping) = _tilesetBuilder.BuildTileset(tilesetName);

            if (tilesetMapping.Count > 0)
            {
                // Adjust GIDs based on offset
                foreach (var (key, gid) in tilesetMapping)
                {
                    mapping[key] = gid + gidOffset - 1;
                }
                gidOffset += tilesetMapping.Count;
            }

            image.Dispose();
        }

        return (tilesetId, mapping);
    }

    /// <summary>
    /// Create Tiled-compatible tileset JSON.
    /// </summary>
    private string CreateTilesetJson(string tilesetName, Image<Rgba32> image, int tileCount)
    {
        var columns = Math.Min(TilesPerRow, tileCount);
        var imageFilename = $"{tilesetName}.png";

        var tileset = new
        {
            id = $"base:tileset:{_region}/{tilesetName}",
            name = tilesetName,
            type = "tileset",
            columns,
            image = imageFilename,
            imagewidth = image.Width,
            imageheight = image.Height,
            margin = 0,
            spacing = 0,
            tilecount = tileCount,
            tilewidth = TileSize,
            tileheight = TileSize,
            version = "1.11"
        };

        return JsonSerializer.Serialize(tileset, new JsonSerializerOptions
        {
            WriteIndented = true
        });
    }
}
