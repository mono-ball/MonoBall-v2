using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Porycon3.Models;
using Porycon3.Infrastructure;

namespace Porycon3.Services;

/// <summary>
/// Key identifying a tileset pair (primary + secondary).
/// </summary>
public readonly record struct TilesetPairKey(string PrimaryTileset, string SecondaryTileset);

/// <summary>
/// Result of processing a metatile: GID for bottom and top layers, with flip flags encoded.
/// </summary>
public readonly record struct MetatileGidResult(uint BottomGid, uint TopGid);

/// <summary>
/// Builds shared tilesets at the 16x16 metatile level with flip-aware deduplication.
/// One tileset is created per primary+secondary tileset pair, shared across all maps using that pair.
///
/// Key optimizations:
/// 1. Shared across maps - same tileset pair = same tileset output
/// 2. Flip-aware deduplication - flipped metatiles reference base tile + flip flags
/// 3. Per-layer deduplication - bottom and top layers deduplicated independently
/// </summary>
public class SharedTilesetBuilder : IDisposable
{
    private const int TileSize = 8;
    private const int MetatileSize = 16;
    private const int TilesPerRow = 16;
    private const int NumTilesInPrimaryVram = 512;

    // Tiled flip flags
    private const uint FLIP_H = 0x80000000;
    private const uint FLIP_V = 0x40000000;

    private readonly string _pokeemeraldPath;
    private readonly MetatileRenderer _renderer;
    private readonly AnimationScanner _animScanner;

    // Unique metatile images (canonical, non-flipped versions)
    private readonly List<Image<Rgba32>> _uniqueImages = new();

    // Hash to GID mapping (for deduplication)
    private readonly Dictionary<ulong, int> _imageHashToGid = new();

    // All variant hashes: hash → (GID, flipH, flipV)
    private readonly Dictionary<ulong, (int Gid, bool FlipH, bool FlipV)> _variantLookup = new();

    // Track processed metatiles: key → (bottomGid with flags, topGid with flags)
    private readonly Dictionary<(int MetatileId, string Tileset, MetatileLayerType LayerType), MetatileGidResult> _processedMetatiles = new();

    // Animation tracking
    private readonly Dictionary<(string Tileset, string AnimName), HashSet<int>> _animatedGids = new();
    private readonly Dictionary<(string Tileset, string AnimName, int FrameIdx), int> _animationFrameGids = new();
    private readonly List<TileAnimation> _animations = new();

    private int _nextGid = 1; // Tiled uses 1-based GIDs
    private readonly object _lock = new(); // Thread safety for parallel map processing

    public TilesetPairKey TilesetPair { get; }

    public SharedTilesetBuilder(string pokeemeraldPath, string primaryTileset, string secondaryTileset)
    {
        _pokeemeraldPath = pokeemeraldPath;
        _renderer = new MetatileRenderer(pokeemeraldPath);
        _animScanner = new AnimationScanner(pokeemeraldPath);
        TilesetPair = new TilesetPairKey(primaryTileset, secondaryTileset);
    }

    /// <summary>
    /// Process a metatile and return GIDs (with flip flags encoded) for both layers.
    /// Thread-safe for parallel map processing.
    /// </summary>
    public MetatileGidResult ProcessMetatile(
        Metatile metatile,
        int metatileId,
        string tilesetName)
    {
        var key = (metatileId, tilesetName, metatile.LayerType);

        lock (_lock)
        {
            if (_processedMetatiles.TryGetValue(key, out var existing))
                return existing;
        }

        // Render the metatile (outside lock - rendering is expensive)
        var (bottomImage, topImage) = _renderer.RenderMetatile(
            metatile,
            TilesetPair.PrimaryTileset,
            TilesetPair.SecondaryTileset);

        lock (_lock)
        {
            // Double-check after acquiring lock
            if (_processedMetatiles.TryGetValue(key, out var existing))
            {
                bottomImage.Dispose();
                topImage.Dispose();
                return existing;
            }

            // Assign GIDs with flip-aware deduplication
            var bottomGid = AssignGidWithFlipDetection(bottomImage);
            var topGid = AssignGidWithFlipDetection(topImage);

            // Track animations
            TrackAnimatedMetatile(metatile, tilesetName, (int)(bottomGid & 0x0FFFFFFF), (int)(topGid & 0x0FFFFFFF));

            var result = new MetatileGidResult(bottomGid, topGid);
            _processedMetatiles[key] = result;

            // Dispose rendered images (we keep clones in _uniqueImages)
            bottomImage.Dispose();
            topImage.Dispose();

            return result;
        }
    }

    /// <summary>
    /// Assign a GID to an image, checking for flipped variants.
    /// Returns GID with flip flags encoded in high bits.
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
            // This image is the H-flip of an existing tile
            // So we reference that tile with H flip flag
            uint gid = (uint)matchH.Gid;
            bool finalFlipH = !matchH.FlipH; // XOR with H
            bool finalFlipV = matchH.FlipV;
            if (finalFlipH) gid |= FLIP_H;
            if (finalFlipV) gid |= FLIP_V;

            // Cache this variant
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
            bool finalFlipV = !matchV.FlipV; // XOR with V
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
            bool finalFlipH = !matchHV.FlipH; // XOR with H
            bool finalFlipV = !matchHV.FlipV; // XOR with V
            if (finalFlipH) gid |= FLIP_H;
            if (finalFlipV) gid |= FLIP_V;

            _variantLookup[hash] = ((int)(gid & 0x0FFFFFFF), finalFlipH, finalFlipV);
            return gid;
        }

        // New unique image - add as canonical
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
    /// Compute a hash from image pixel data using FNV-1a.
    /// </summary>
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

    /// <summary>
    /// Track if metatile uses animated tiles.
    /// </summary>
    private void TrackAnimatedMetatile(Metatile metatile, string tilesetName, int bottomGid, int topGid)
    {
        CheckTilesetAnimations(metatile, TilesetPair.PrimaryTileset, false, bottomGid, topGid);
        CheckTilesetAnimations(metatile, TilesetPair.SecondaryTileset, true, bottomGid, topGid);
    }

    private void CheckTilesetAnimations(Metatile metatile, string tilesetName, bool isSecondary, int bottomGid, int topGid)
    {
        var animDefs = _animScanner.GetAnimationsForTileset(tilesetName);
        if (animDefs.Length == 0) return;

        var allTiles = metatile.BottomTiles.Concat(metatile.TopTiles).ToArray();

        foreach (var animDef in animDefs)
        {
            if (animDef.IsSecondary != isSecondary) continue;

            int startTileId = animDef.BaseTileId;
            int endTileId = animDef.BaseTileId + animDef.NumTiles - 1;

            if (isSecondary)
            {
                startTileId += 512;
                endTileId += 512;
            }

            bool usesAnimatedTiles = allTiles.Any(t => t.TileId >= startTileId && t.TileId <= endTileId);

            if (usesAnimatedTiles)
            {
                var animKey = (tilesetName, animDef.Name);
                if (!_animatedGids.TryGetValue(animKey, out var gidSet))
                {
                    gidSet = new HashSet<int>();
                    _animatedGids[animKey] = gidSet;
                }
                gidSet.Add(bottomGid);
            }
        }
    }

    /// <summary>
    /// Process animations for the tileset.
    /// </summary>
    public void ProcessAnimations(Rgba32[]?[]? primaryPalettes, Rgba32[]?[]? secondaryPalettes)
    {
        ProcessTilesetAnimations(TilesetPair.PrimaryTileset, primaryPalettes, false);
        ProcessTilesetAnimations(TilesetPair.SecondaryTileset, secondaryPalettes, true);
    }

    private void ProcessTilesetAnimations(string tilesetName, Rgba32[]?[]? palettes, bool isSecondary)
    {
        var animDefs = _animScanner.GetAnimationsForTileset(tilesetName);
        if (animDefs.Length == 0) return;

        foreach (var animDef in animDefs)
        {
            if (animDef.IsSecondary != isSecondary) continue;

            var animKey = (tilesetName, animDef.Name);
            if (!_animatedGids.TryGetValue(animKey, out var gidsToAnimate) || gidsToAnimate.Count == 0)
                continue;

            var frames = _animScanner.ExtractAnimationFrames(tilesetName, animDef, palettes);
            if (frames.Count == 0) continue;

            var frameSequence = animDef.FrameSequence ?? Enumerable.Range(0, frames.Count).ToArray();

            if (frames[0].Width == 16 && frames[0].Height == 16)
            {
                ProcessMetatileAnimation(tilesetName, animDef, frames, frameSequence, gidsToAnimate);
            }

            foreach (var frame in frames)
                frame.Dispose();
        }
    }

    private void ProcessMetatileAnimation(
        string tilesetName,
        AnimationDefinition animDef,
        List<Image<Rgba32>> frames,
        int[] frameSequence,
        HashSet<int> gidsToAnimate)
    {
        var frameGids = new int[frames.Count];

        for (int i = 0; i < frames.Count; i++)
        {
            // Animation frames go through same deduplication
            var gid = AssignGidWithFlipDetection(frames[i]);
            frameGids[i] = (int)(gid & 0x0FFFFFFF); // Strip flip flags for animation frames
            _animationFrameGids[(tilesetName, animDef.Name, i)] = frameGids[i];
        }

        var animFrames = new List<AnimationFrame>();
        foreach (var seqIdx in frameSequence)
        {
            if (seqIdx < frameGids.Length)
            {
                animFrames.Add(new AnimationFrame(frameGids[seqIdx] - 1, animDef.DurationMs));
            }
        }

        foreach (var gid in gidsToAnimate)
        {
            _animations.Add(new TileAnimation(gid - 1, animFrames.ToArray()));
        }
    }

    /// <summary>
    /// Build the final tilesheet image.
    /// </summary>
    public Image<Rgba32> BuildTilesheetImage()
    {
        if (_uniqueImages.Count == 0)
            return new Image<Rgba32>(MetatileSize, MetatileSize);

        var cols = Math.Min(TilesPerRow, _uniqueImages.Count);
        var rows = (_uniqueImages.Count + cols - 1) / cols;

        var tilesheet = new Image<Rgba32>(cols * MetatileSize, rows * MetatileSize);

        for (int i = 0; i < _uniqueImages.Count; i++)
        {
            var x = (i % cols) * MetatileSize;
            var y = (i / cols) * MetatileSize;
            tilesheet.Mutate(ctx => ctx.DrawImage(_uniqueImages[i], new Point(x, y), 1f));
        }

        return tilesheet;
    }

    public int UniqueTileCount => _uniqueImages.Count;
    public int Columns => Math.Min(TilesPerRow, Math.Max(1, _uniqueImages.Count));
    public List<TileAnimation> GetAnimations() => _animations;

    public void Dispose()
    {
        foreach (var img in _uniqueImages)
            img.Dispose();
        _uniqueImages.Clear();
        _renderer.Dispose();
    }
}
