using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Porycon3.Models;
using Porycon3.Infrastructure;

namespace Porycon3.Services;

/// <summary>
/// Result of processing maps with shared tilesets.
/// </summary>
public record SharedTilesetResult(
    string TilesetId,
    string TilesetName,
    string TilesetType, // "primary" or "secondary"
    Image<Rgba32> TilesheetImage,
    int TileCount,
    int Columns,
    List<TileAnimation> Animations,
    List<TileProperty> TileProperties
);

/// <summary>
/// Registry that manages shared tilesets across multiple maps.
/// Now keys by individual tileset (primary OR secondary) to minimize duplication.
/// </summary>
public class SharedTilesetRegistry : IDisposable
{
    private readonly string _pokeemeraldPath;
    private readonly TilesetPathResolver _resolver;

    // Key by individual tileset name (not pairs) for better deduplication
    private readonly Dictionary<string, IndividualTilesetBuilder> _builders = new();
    private readonly Dictionary<string, List<string>> _mapsUsingTileset = new();

    // Still track pairs for compatibility with existing map processing
    private readonly Dictionary<TilesetPairKey, (string Primary, string Secondary)> _pairToIndividual = new();

    // Cache SharedTilesetBuilder instances to preserve animation tracking state
    private readonly Dictionary<TilesetPairKey, SharedTilesetBuilder> _sharedBuilders = new();

    private readonly object _lock = new();

    public SharedTilesetRegistry(string pokeemeraldPath)
    {
        _pokeemeraldPath = pokeemeraldPath;
        _resolver = new TilesetPathResolver(pokeemeraldPath);
    }

    /// <summary>
    /// Get or create builders for a tileset pair.
    /// Returns a wrapper that delegates to individual primary and secondary builders.
    /// The wrapper is cached to preserve animation tracking state.
    /// </summary>
    public SharedTilesetBuilder GetOrCreateBuilder(string primaryTileset, string secondaryTileset)
    {
        var pairKey = new TilesetPairKey(primaryTileset, secondaryTileset);

        lock (_lock)
        {
            // Return cached builder if it exists
            if (_sharedBuilders.TryGetValue(pairKey, out var cached))
                return cached;

            // Get or create individual builders for each tileset
            var primaryBuilder = GetOrCreateIndividualBuilder(primaryTileset);
            var secondaryBuilder = GetOrCreateIndividualBuilder(secondaryTileset);

            _pairToIndividual[pairKey] = (primaryTileset, secondaryTileset);

            // Create and cache the wrapper that coordinates both builders
            var sharedBuilder = new SharedTilesetBuilder(_pokeemeraldPath, primaryTileset, secondaryTileset,
                primaryBuilder, secondaryBuilder);
            _sharedBuilders[pairKey] = sharedBuilder;

            return sharedBuilder;
        }
    }

    /// <summary>
    /// Get or create an individual tileset builder.
    /// </summary>
    private IndividualTilesetBuilder GetOrCreateIndividualBuilder(string tilesetName)
    {
        var normalized = NormalizeTilesetName(tilesetName);
        if (_builders.TryGetValue(normalized, out var existing))
            return existing;

        var builder = new IndividualTilesetBuilder(_pokeemeraldPath, tilesetName);
        _builders[normalized] = builder;
        _mapsUsingTileset[normalized] = new List<string>();

        return builder;
    }

    /// <summary>
    /// Register that a map uses a specific tileset pair.
    /// </summary>
    public void RegisterMapUsage(string mapName, string primaryTileset, string secondaryTileset)
    {
        lock (_lock)
        {
            var primaryNorm = NormalizeTilesetName(primaryTileset);
            var secondaryNorm = NormalizeTilesetName(secondaryTileset);

            if (!_mapsUsingTileset.ContainsKey(primaryNorm))
                _mapsUsingTileset[primaryNorm] = new List<string>();
            if (!_mapsUsingTileset[primaryNorm].Contains(mapName))
                _mapsUsingTileset[primaryNorm].Add(mapName);

            if (!_mapsUsingTileset.ContainsKey(secondaryNorm))
                _mapsUsingTileset[secondaryNorm] = new List<string>();
            if (!_mapsUsingTileset[secondaryNorm].Contains(mapName))
                _mapsUsingTileset[secondaryNorm].Add(mapName);
        }
    }

    /// <summary>
    /// Get all tileset pairs that have been used.
    /// </summary>
    public IEnumerable<TilesetPairKey> GetAllTilesetPairs() => _pairToIndividual.Keys;

    /// <summary>
    /// Get the cached builder for a specific tileset pair.
    /// Returns the same instance that was used during map processing to preserve animation tracking.
    /// </summary>
    public SharedTilesetBuilder? GetBuilder(TilesetPairKey key)
    {
        lock (_lock)
        {
            return _sharedBuilders.TryGetValue(key, out var builder) ? builder : null;
        }
    }

    /// <summary>
    /// Get all individual tilesets that have been used.
    /// </summary>
    public IEnumerable<string> GetAllIndividualTilesets()
    {
        lock (_lock)
        {
            return _builders.Keys.ToList();
        }
    }

    /// <summary>
    /// Get all maps using a specific tileset pair.
    /// </summary>
    public IReadOnlyList<string> GetMapsUsingTileset(TilesetPairKey key)
    {
        if (!_pairToIndividual.TryGetValue(key, out var pair))
            return Array.Empty<string>();

        var secondaryNorm = NormalizeTilesetName(pair.Secondary);
        return _mapsUsingTileset.TryGetValue(secondaryNorm, out var maps) ? maps : Array.Empty<string>();
    }

    /// <summary>
    /// Build all individual tilesets and return results.
    /// Each tileset is output separately (primary/* or secondary/*).
    /// </summary>
    public IEnumerable<SharedTilesetResult> BuildAllTilesets()
    {
        foreach (var (normalizedName, builder) in _builders)
        {
            var tilesetType = builder.TilesetType;
            var tilesetId = IdTransformer.TilesetId(normalizedName, tilesetType);
            var image = builder.BuildTilesheetImage();

            yield return new SharedTilesetResult(
                tilesetId,
                normalizedName,
                tilesetType,
                image,
                builder.UniqueTileCount,
                builder.Columns,
                builder.GetAnimations(),
                builder.GetTileProperties()
            );
        }
    }

    /// <summary>
    /// Generate tileset name for a pair (legacy compatibility).
    /// </summary>
    public static string GenerateTilesetName(TilesetPairKey key)
    {
        var primary = NormalizeTilesetName(key.PrimaryTileset);
        var secondary = NormalizeTilesetName(key.SecondaryTileset);
        return $"{primary}_{secondary}";
    }

    /// <summary>
    /// Generate tileset ID for a pair (legacy compatibility).
    /// </summary>
    public static string GenerateTilesetId(TilesetPairKey key)
    {
        var name = GenerateTilesetName(key);
        return $"base:tileset:shared/{name}";
    }

    /// <summary>
    /// Normalize tileset name (remove gTileset_ prefix, lowercase).
    /// </summary>
    public static string NormalizeTilesetName(string tilesetName)
    {
        var name = tilesetName;
        if (name.StartsWith("gTileset_", StringComparison.OrdinalIgnoreCase))
            name = name.Substring(9);
        return name.ToLowerInvariant();
    }

    public void Dispose()
    {
        // Dispose shared builders (which will dispose their MetatileRenderer)
        foreach (var builder in _sharedBuilders.Values)
        {
            builder.Dispose();
        }
        _sharedBuilders.Clear();

        // Dispose individual builders
        foreach (var builder in _builders.Values)
        {
            builder.Dispose();
        }
        _builders.Clear();
        _mapsUsingTileset.Clear();
        _pairToIndividual.Clear();
    }
}
