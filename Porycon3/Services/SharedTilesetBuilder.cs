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
/// Also includes which tileset the metatile belongs to for separate output.
/// </summary>
public readonly record struct MetatileGidResult(uint BottomGid, uint TopGid, bool IsSecondary);

/// <summary>
/// Coordinates metatile processing between primary and secondary tilesets.
/// Delegates actual tile storage to IndividualTilesetBuilder instances.
///
/// Key optimizations:
/// 1. Separate primary and secondary tilesets - minimizes duplication
/// 2. Flip-aware deduplication - flipped metatiles reference base tile + flip flags
/// 3. Per-layer deduplication - bottom and top layers deduplicated independently
/// </summary>
public class SharedTilesetBuilder : IDisposable
{
    private const int TileSize = 8;
    private const int MetatileSize = 16;
    private const int TilesPerRow = 16;
    private const int NumMetatilesInPrimary = 512;

    // Tiled flip flags
    private const uint FLIP_H = 0x80000000;
    private const uint FLIP_V = 0x40000000;

    private readonly string _pokeemeraldPath;
    private readonly MetatileRenderer _renderer;
    private readonly AnimationScanner _animScanner;

    // Individual builders for primary and secondary tilesets
    private readonly IndividualTilesetBuilder _primaryBuilder;
    private readonly IndividualTilesetBuilder _secondaryBuilder;
    private readonly bool _ownsBuilders;

    // Track processed metatiles: key → result
    private readonly Dictionary<(int MetatileId, string Tileset, MetatileLayerType LayerType), MetatileGidResult> _processedMetatiles = new();

    // Animation tracking
    private readonly Dictionary<(string Tileset, string AnimName), HashSet<int>> _animatedGids = new();
    private readonly Dictionary<(string Tileset, string AnimName, int FrameIdx), int> _animationFrameGids = new();

    private readonly object _lock = new();

    public TilesetPairKey TilesetPair { get; }

    /// <summary>
    /// Create with shared IndividualTilesetBuilder instances (from registry).
    /// </summary>
    public SharedTilesetBuilder(
        string pokeemeraldPath,
        string primaryTileset,
        string secondaryTileset,
        IndividualTilesetBuilder primaryBuilder,
        IndividualTilesetBuilder secondaryBuilder)
    {
        _pokeemeraldPath = pokeemeraldPath;
        _renderer = new MetatileRenderer(pokeemeraldPath);
        _animScanner = new AnimationScanner(pokeemeraldPath);
        TilesetPair = new TilesetPairKey(primaryTileset, secondaryTileset);
        _primaryBuilder = primaryBuilder;
        _secondaryBuilder = secondaryBuilder;
        _ownsBuilders = false;
    }

    /// <summary>
    /// Create with own IndividualTilesetBuilder instances (legacy mode).
    /// </summary>
    public SharedTilesetBuilder(string pokeemeraldPath, string primaryTileset, string secondaryTileset)
    {
        _pokeemeraldPath = pokeemeraldPath;
        _renderer = new MetatileRenderer(pokeemeraldPath);
        _animScanner = new AnimationScanner(pokeemeraldPath);
        TilesetPair = new TilesetPairKey(primaryTileset, secondaryTileset);
        _primaryBuilder = new IndividualTilesetBuilder(pokeemeraldPath, primaryTileset);
        _secondaryBuilder = new IndividualTilesetBuilder(pokeemeraldPath, secondaryTileset);
        _ownsBuilders = true;
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

        // Determine if this metatile is from primary or secondary based on metatile ID
        // Metatiles 0-511 are from primary, 512+ are from secondary
        var isSecondary = metatileId >= NumMetatilesInPrimary;
        var builder = isSecondary ? _secondaryBuilder : _primaryBuilder;

        // Use local metatile ID for secondary (offset by 512)
        var localMetatileId = isSecondary ? metatileId - NumMetatilesInPrimary : metatileId;

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

            // Assign GIDs using the appropriate individual builder
            var bottomGid = builder.ProcessMetatileImage(localMetatileId * 2, bottomImage);
            var topGid = builder.ProcessMetatileImage(localMetatileId * 2 + 1, topImage);

            // Track tile properties (behavior, terrain) for each GID
            // Only the base GID matters (flip flags stripped), properties are per unique image
            var bottomBaseGid = (int)(bottomGid & 0x0FFFFFFF);
            var topBaseGid = (int)(topGid & 0x0FFFFFFF);
            builder.TrackTileProperty(bottomBaseGid, metatile.Behavior, metatile.TerrainType);
            builder.TrackTileProperty(topBaseGid, metatile.Behavior, metatile.TerrainType);

            // Track animations
            TrackAnimatedMetatile(metatile, tilesetName, bottomBaseGid, topBaseGid);

            var result = new MetatileGidResult(bottomGid, topGid, isSecondary);
            _processedMetatiles[key] = result;

            // Dispose rendered images
            bottomImage.Dispose();
            topImage.Dispose();

            return result;
        }
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
                ProcessMetatileAnimation(tilesetName, animDef, frames, frameSequence, gidsToAnimate, isSecondary);
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
        HashSet<int> gidsToAnimate,
        bool isSecondary)
    {
        var builder = isSecondary ? _secondaryBuilder : _primaryBuilder;
        var frameGids = new int[frames.Count];

        for (int i = 0; i < frames.Count; i++)
        {
            // Animation frames go through same deduplication via the individual builder
            var gid = builder.ProcessMetatileImage(1000000 + i, frames[i]); // Use high ID for animation frames
            frameGids[i] = (int)(gid & 0x0FFFFFFF);
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
            builder.AddAnimation(new TileAnimation(gid - 1, animFrames.ToArray()));
        }
    }

    /// <summary>
    /// Build the final tilesheet image (legacy - returns primary builder's sheet).
    /// Use BuildAllTilesheets() for separated primary/secondary output.
    /// </summary>
    public Image<Rgba32> BuildTilesheetImage()
    {
        // For legacy compatibility, combine both sheets
        var primaryImage = _primaryBuilder.BuildTilesheetImage();
        var secondaryImage = _secondaryBuilder.BuildTilesheetImage();

        if (_secondaryBuilder.UniqueTileCount == 0)
            return primaryImage;

        if (_primaryBuilder.UniqueTileCount == 0)
            return secondaryImage;

        // Combine both sheets vertically
        var combinedHeight = primaryImage.Height + secondaryImage.Height;
        var combinedWidth = Math.Max(primaryImage.Width, secondaryImage.Width);
        var combined = new Image<Rgba32>(combinedWidth, combinedHeight);

        combined.ProcessPixelRows(accessor =>
        {
            var transparent = new Rgba32(0, 0, 0, 0);
            for (int y = 0; y < accessor.Height; y++)
                accessor.GetRowSpan(y).Fill(transparent);
        });

        combined.Mutate(ctx =>
        {
            ctx.DrawImage(primaryImage, new Point(0, 0), 1f);
            ctx.DrawImage(secondaryImage, new Point(0, primaryImage.Height), 1f);
        });

        primaryImage.Dispose();
        secondaryImage.Dispose();

        return combined;
    }

    /// <summary>
    /// Build separate tilesheet images for primary and secondary.
    /// </summary>
    public (Image<Rgba32> Primary, Image<Rgba32> Secondary) BuildAllTilesheets()
    {
        return (_primaryBuilder.BuildTilesheetImage(), _secondaryBuilder.BuildTilesheetImage());
    }

    public int UniqueTileCount => _primaryBuilder.UniqueTileCount + _secondaryBuilder.UniqueTileCount;
    public int PrimaryTileCount => _primaryBuilder.UniqueTileCount;
    public int SecondaryTileCount => _secondaryBuilder.UniqueTileCount;
    public int Columns => Math.Min(TilesPerRow, Math.Max(1, UniqueTileCount));

    public List<TileAnimation> GetAnimations()
    {
        var all = new List<TileAnimation>();
        all.AddRange(_primaryBuilder.GetAnimations());
        all.AddRange(_secondaryBuilder.GetAnimations());
        return all;
    }

    public void Dispose()
    {
        _renderer.Dispose();
        if (_ownsBuilders)
        {
            _primaryBuilder.Dispose();
            _secondaryBuilder.Dispose();
        }
    }
}
