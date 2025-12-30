using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Porycon3.Models;
using Porycon3.Infrastructure;

namespace Porycon3.Services;

/// <summary>
/// Builds a tileset for a single source tileset (primary or secondary).
/// Tracks unique 16x16 metatile images with flip-aware deduplication.
/// </summary>
public class IndividualTilesetBuilder : IDisposable
{
    private const int MetatileSize = 16;
    private const int TilesPerRow = 16;
    private const uint FLIP_H = 0x80000000;
    private const uint FLIP_V = 0x40000000;

    private readonly string _pokeemeraldPath;
    private readonly string _tilesetName;
    private readonly string _normalizedName;
    private readonly TilesetPathResolver _resolver;

    // Unique metatile images
    private readonly List<Image<Rgba32>> _uniqueImages = new();
    private readonly Dictionary<ulong, int> _imageHashToGid = new();
    private readonly Dictionary<ulong, (int Gid, bool FlipH, bool FlipV)> _variantLookup = new();

    // Track processed metatiles: metatileId -> GID with flip flags
    private readonly Dictionary<int, uint> _processedMetatiles = new();

    // Animation tracking
    private readonly List<TileAnimation> _animations = new();

    // Tile properties tracking: GID -> properties (only stores for first metatile that created the GID)
    private readonly Dictionary<int, TileProperty> _tileProperties = new();

    private int _nextGid = 1;
    private readonly object _lock = new();

    public string TilesetName => _tilesetName;
    public string NormalizedName => _normalizedName;
    public string TilesetType { get; }

    public IndividualTilesetBuilder(string pokeemeraldPath, string tilesetName)
    {
        _pokeemeraldPath = pokeemeraldPath;
        _tilesetName = tilesetName;
        _normalizedName = NormalizeName(tilesetName);
        _resolver = new TilesetPathResolver(pokeemeraldPath);

        // Determine if this is primary or secondary
        var result = _resolver.FindTilesetPath(tilesetName);
        TilesetType = result?.Type ?? "secondary";
    }

    /// <summary>
    /// Process a metatile image and return GID with flip flags encoded.
    /// Thread-safe for parallel processing.
    /// </summary>
    public uint ProcessMetatileImage(int metatileId, Image<Rgba32> image)
    {
        lock (_lock)
        {
            // Check if already processed
            if (_processedMetatiles.TryGetValue(metatileId, out var existing))
                return existing;

            // Assign GID with flip detection
            var gid = AssignGidWithFlipDetection(image);
            _processedMetatiles[metatileId] = gid;

            return gid;
        }
    }

    /// <summary>
    /// Check if a metatile has already been processed.
    /// </summary>
    public bool HasMetatile(int metatileId)
    {
        lock (_lock)
        {
            return _processedMetatiles.ContainsKey(metatileId);
        }
    }

    /// <summary>
    /// Get GID for an already-processed metatile.
    /// </summary>
    public uint? GetMetatileGid(int metatileId)
    {
        lock (_lock)
        {
            return _processedMetatiles.TryGetValue(metatileId, out var gid) ? gid : null;
        }
    }

    /// <summary>
    /// Assign a GID to an image, checking for flipped variants.
    /// </summary>
    private uint AssignGidWithFlipDetection(Image<Rgba32> image)
    {
        var hash = ComputeImageHash(image);

        // Check if exact match or variant exists
        if (_variantLookup.TryGetValue(hash, out var existing))
        {
            uint gid = (uint)existing.Gid;
            if (existing.FlipH) gid |= FLIP_H;
            if (existing.FlipV) gid |= FLIP_V;
            return gid;
        }

        // Generate flipped variants and check
        using var flipH = image.Clone();
        flipH.Mutate(x => x.Flip(FlipMode.Horizontal));
        var hashH = ComputeImageHash(flipH);

        if (_variantLookup.TryGetValue(hashH, out var matchH))
        {
            uint gid = (uint)matchH.Gid;
            bool finalFlipH = !matchH.FlipH;
            bool finalFlipV = matchH.FlipV;
            if (finalFlipH) gid |= FLIP_H;
            if (finalFlipV) gid |= FLIP_V;
            _variantLookup[hash] = ((int)(gid & 0x0FFFFFFF), finalFlipH, finalFlipV);
            return gid;
        }

        using var flipV = image.Clone();
        flipV.Mutate(x => x.Flip(FlipMode.Vertical));
        var hashV = ComputeImageHash(flipV);

        if (_variantLookup.TryGetValue(hashV, out var matchV))
        {
            uint gid = (uint)matchV.Gid;
            bool finalFlipH = matchV.FlipH;
            bool finalFlipV = !matchV.FlipV;
            if (finalFlipH) gid |= FLIP_H;
            if (finalFlipV) gid |= FLIP_V;
            _variantLookup[hash] = ((int)(gid & 0x0FFFFFFF), finalFlipH, finalFlipV);
            return gid;
        }

        using var flipHV = flipH.Clone();
        flipHV.Mutate(x => x.Flip(FlipMode.Vertical));
        var hashHV = ComputeImageHash(flipHV);

        if (_variantLookup.TryGetValue(hashHV, out var matchHV))
        {
            uint gid = (uint)matchHV.Gid;
            bool finalFlipH = !matchHV.FlipH;
            bool finalFlipV = !matchHV.FlipV;
            if (finalFlipH) gid |= FLIP_H;
            if (finalFlipV) gid |= FLIP_V;
            _variantLookup[hash] = ((int)(gid & 0x0FFFFFFF), finalFlipH, finalFlipV);
            return gid;
        }

        // New unique image
        var newGid = _nextGid++;
        _uniqueImages.Add(image.Clone());
        _imageHashToGid[hash] = newGid;

        // Register all variants
        _variantLookup[hash] = (newGid, false, false);
        _variantLookup[hashH] = (newGid, true, false);
        _variantLookup[hashV] = (newGid, false, true);
        _variantLookup[hashHV] = (newGid, true, true);

        return (uint)newGid;
    }

    /// <summary>
    /// Add an animation for this tileset.
    /// </summary>
    public void AddAnimation(TileAnimation animation)
    {
        lock (_lock)
        {
            _animations.Add(animation);
        }
    }

    /// <summary>
    /// Track tile properties for a GID. Only stores the first property set for each GID.
    /// This associates metatile interaction/terrain/collision with the rendered tile image.
    /// Tiles with all-default properties (normal interaction, normal terrain, passable) are skipped.
    /// The behavior parameter contains the raw 16-bit attribute value with behavior in bits 0-7.
    /// </summary>
    public void TrackTileProperty(int gid, int behavior, int terrainType)
    {
        lock (_lock)
        {
            // Only store if we don't already have properties for this GID
            if (_tileProperties.ContainsKey(gid))
                return;

            var interactionId = IdTransformer.TileInteractionId(behavior);
            var terrainId = IdTransformer.TerrainTypeId(terrainType);
            var collisionId = IdTransformer.DeriveCollisionId(behavior);

            // Skip tiles with all-default properties (all null)
            if (interactionId == null && terrainId == null && collisionId == null)
                return;

            _tileProperties[gid] = new TileProperty(
                gid - 1,  // LocalTileId is 0-based
                interactionId,
                terrainId,
                collisionId
            );
        }
    }

    /// <summary>
    /// Get all tile properties for this tileset.
    /// </summary>
    public List<TileProperty> GetTileProperties()
    {
        lock (_lock)
        {
            return _tileProperties.Values.OrderBy(p => p.LocalTileId).ToList();
        }
    }

    /// <summary>
    /// Build the tilesheet image.
    /// </summary>
    public Image<Rgba32> BuildTilesheetImage()
    {
        lock (_lock)
        {
            if (_uniqueImages.Count == 0)
                return new Image<Rgba32>(MetatileSize, MetatileSize);

            var cols = Math.Min(TilesPerRow, _uniqueImages.Count);
            var rows = (_uniqueImages.Count + TilesPerRow - 1) / TilesPerRow;

            var tilesheet = new Image<Rgba32>(cols * MetatileSize, rows * MetatileSize);

            // Initialize transparent
            tilesheet.ProcessPixelRows(accessor =>
            {
                var transparent = new Rgba32(0, 0, 0, 0);
                for (int y = 0; y < accessor.Height; y++)
                {
                    accessor.GetRowSpan(y).Fill(transparent);
                }
            });

            for (int i = 0; i < _uniqueImages.Count; i++)
            {
                var x = (i % TilesPerRow) * MetatileSize;
                var y = (i / TilesPerRow) * MetatileSize;
                tilesheet.Mutate(ctx => ctx.DrawImage(_uniqueImages[i], new Point(x, y), 1f));
            }

            return tilesheet;
        }
    }

    public int UniqueTileCount => _uniqueImages.Count;
    public int Columns => Math.Min(TilesPerRow, Math.Max(1, _uniqueImages.Count));
    public List<TileAnimation> GetAnimations() => _animations.ToList();

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

    private static string NormalizeName(string name)
    {
        if (name.StartsWith("gTileset_", StringComparison.OrdinalIgnoreCase))
            name = name[9..];
        return name.ToLowerInvariant();
    }

    public void Dispose()
    {
        foreach (var img in _uniqueImages)
            img.Dispose();
        _uniqueImages.Clear();
    }
}
