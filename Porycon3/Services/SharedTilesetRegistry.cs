using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Porycon3.Models;

namespace Porycon3.Services;

/// <summary>
/// Result of processing maps with shared tilesets.
/// </summary>
public record SharedTilesetResult(
    string TilesetId,
    string TilesetName,
    Image<Rgba32> TilesheetImage,
    int TileCount,
    int Columns
);

/// <summary>
/// Registry that manages shared tilesets across multiple maps.
/// Caches SharedTilesetBuilder instances per tileset pair.
/// </summary>
public class SharedTilesetRegistry : IDisposable
{
    private readonly string _pokeemeraldPath;
    private readonly Dictionary<TilesetPairKey, SharedTilesetBuilder> _builders = new();
    private readonly Dictionary<TilesetPairKey, List<string>> _mapsUsingTileset = new();
    private readonly object _lock = new();

    public SharedTilesetRegistry(string pokeemeraldPath)
    {
        _pokeemeraldPath = pokeemeraldPath;
    }

    /// <summary>
    /// Get or create a shared tileset builder for a tileset pair (thread-safe).
    /// </summary>
    public SharedTilesetBuilder GetOrCreateBuilder(string primaryTileset, string secondaryTileset)
    {
        var key = new TilesetPairKey(primaryTileset, secondaryTileset);

        lock (_lock)
        {
            if (_builders.TryGetValue(key, out var existing))
                return existing;

            var builder = new SharedTilesetBuilder(_pokeemeraldPath, primaryTileset, secondaryTileset);
            _builders[key] = builder;
            _mapsUsingTileset[key] = new List<string>();

            return builder;
        }
    }

    /// <summary>
    /// Register that a map uses a specific tileset pair (thread-safe).
    /// </summary>
    public void RegisterMapUsage(string mapName, string primaryTileset, string secondaryTileset)
    {
        var key = new TilesetPairKey(primaryTileset, secondaryTileset);
        lock (_lock)
        {
            if (!_mapsUsingTileset.ContainsKey(key))
                _mapsUsingTileset[key] = new List<string>();

            if (!_mapsUsingTileset[key].Contains(mapName))
                _mapsUsingTileset[key].Add(mapName);
        }
    }

    /// <summary>
    /// Get all tileset pairs that have been used.
    /// </summary>
    public IEnumerable<TilesetPairKey> GetAllTilesetPairs() => _builders.Keys;

    /// <summary>
    /// Get the builder for a specific tileset pair.
    /// </summary>
    public SharedTilesetBuilder? GetBuilder(TilesetPairKey key)
    {
        return _builders.TryGetValue(key, out var builder) ? builder : null;
    }

    /// <summary>
    /// Get all maps using a specific tileset pair.
    /// </summary>
    public IReadOnlyList<string> GetMapsUsingTileset(TilesetPairKey key)
    {
        return _mapsUsingTileset.TryGetValue(key, out var maps) ? maps : Array.Empty<string>();
    }

    /// <summary>
    /// Build all shared tilesets and return results.
    /// </summary>
    public IEnumerable<SharedTilesetResult> BuildAllTilesets()
    {
        foreach (var (key, builder) in _builders)
        {
            var tilesetName = GenerateTilesetName(key);
            var tilesetId = GenerateTilesetId(key);
            var image = builder.BuildTilesheetImage();

            yield return new SharedTilesetResult(
                tilesetId,
                tilesetName,
                image,
                builder.UniqueTileCount,
                builder.Columns
            );
        }
    }

    /// <summary>
    /// Generate a consistent name for a tileset pair.
    /// </summary>
    public static string GenerateTilesetName(TilesetPairKey key)
    {
        var primary = NormalizeTilesetName(key.PrimaryTileset);
        var secondary = NormalizeTilesetName(key.SecondaryTileset);
        return $"{primary}_{secondary}";
    }

    /// <summary>
    /// Generate a tileset ID for a tileset pair.
    /// </summary>
    public static string GenerateTilesetId(TilesetPairKey key)
    {
        var name = GenerateTilesetName(key);
        return $"base:tileset:shared/{name}";
    }

    /// <summary>
    /// Normalize tileset name (remove gTileset_ prefix, lowercase).
    /// </summary>
    private static string NormalizeTilesetName(string tilesetName)
    {
        var name = tilesetName;
        if (name.StartsWith("gTileset_", StringComparison.OrdinalIgnoreCase))
            name = name.Substring(9);
        return name.ToLowerInvariant();
    }

    public void Dispose()
    {
        foreach (var builder in _builders.Values)
        {
            builder.Dispose();
        }
        _builders.Clear();
        _mapsUsingTileset.Clear();
    }
}
