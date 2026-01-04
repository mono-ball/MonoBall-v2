using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Porycon3.Models;
using Porycon3.Infrastructure;
using static Porycon3.Infrastructure.TileConstants;

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
    private const int NumMetatilesInPrimary = 512;

    private readonly string _pokeemeraldPath;
    private readonly MetatileRenderer _renderer;
    private readonly AnimationScanner _animScanner;

    // Individual builders for primary and secondary tilesets
    private readonly IndividualTilesetBuilder _primaryBuilder;
    private readonly IndividualTilesetBuilder _secondaryBuilder;
    private readonly bool _ownsBuilders;

    // Track processed metatiles: key → result
    private readonly Dictionary<(int MetatileId, string Tileset, MetatileLayerType LayerType), MetatileGidResult> _processedMetatiles = new();

    // Animation tracking - now keyed by metatile ID to avoid GID deduplication issues
    // Maps (tileset, animName) -> set of (metatileId, bottomGid) pairs
    private readonly Dictionary<(string Tileset, string AnimName), HashSet<(int MetatileId, int BottomGid)>> _animatedMetatiles = new();
    private readonly Dictionary<(string Tileset, string AnimName, int FrameIdx), int> _animationFrameGids = new();

    // Store metatile data for animated metatiles - keyed by metatileId for uniqueness
    // Now includes topGid and flags for which layers use animated tiles
    private readonly Dictionary<int, (Metatile Metatile, int MetatileId, int BottomGid, int TopGid, bool IsSecondary, bool BottomUsesAnim, bool TopUsesAnim)> _animatedMetatileData = new();

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

        // Check if this metatile uses animated source tiles BEFORE assigning GIDs
        // This prevents animated metatiles from being deduplicated with non-animated tiles
        var usesAnimatedTiles = MetatileUsesAnimatedTiles(metatile);

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
            // Use unique GIDs for animated metatiles to prevent deduplication issues
            uint bottomGid, topGid;
            if (usesAnimatedTiles)
            {
                // Force unique GIDs for animated metatiles
                bottomGid = builder.ProcessMetatileImageUnique(localMetatileId * 2, bottomImage);
                topGid = builder.ProcessMetatileImageUnique(localMetatileId * 2 + 1, topImage);
            }
            else
            {
                // Normal deduplication for non-animated metatiles
                bottomGid = builder.ProcessMetatileImage(localMetatileId * 2, bottomImage);
                topGid = builder.ProcessMetatileImage(localMetatileId * 2 + 1, topImage);
            }

            // Track tile properties (behavior, terrain) for each GID
            // Only the base GID matters (flip flags stripped), properties are per unique image
            var bottomBaseGid = (int)(bottomGid & GidMask);
            var topBaseGid = (int)(topGid & GidMask);
            builder.TrackTileProperty(bottomBaseGid, metatile.Behavior, metatile.TerrainType);
            builder.TrackTileProperty(topBaseGid, metatile.Behavior, metatile.TerrainType);

            // Track animations
            TrackAnimatedMetatile(metatile, metatileId, tilesetName, bottomBaseGid, topBaseGid, isSecondary);

            var result = new MetatileGidResult(bottomGid, topGid, isSecondary);
            _processedMetatiles[key] = result;

            // Dispose rendered images
            bottomImage.Dispose();
            topImage.Dispose();

            return result;
        }
    }

    /// <summary>
    /// Check if a metatile uses animated source tiles (e.g., water, waterfall, flowers).
    /// Must be called early to determine if unique GIDs are needed.
    /// </summary>
    private bool MetatileUsesAnimatedTiles(Metatile metatile)
    {
        // Check both primary and secondary tileset animations
        var primaryAnimDefs = _animScanner.GetAnimationsForTileset(TilesetPair.PrimaryTileset);
        var secondaryAnimDefs = _animScanner.GetAnimationsForTileset(TilesetPair.SecondaryTileset);

        // Check BOTH bottom and top layer tiles against all animation ranges
        // Flowers are rendered on top of grass base, so we must check TopTiles too
        var allTiles = metatile.BottomTiles.Concat(metatile.TopTiles);
        foreach (var tile in allTiles)
        {
            // Check primary tileset animations (tiles 0-511)
            foreach (var animDef in primaryAnimDefs)
            {
                if (!animDef.IsSecondary)
                {
                    int startTileId = animDef.BaseTileId;
                    int endTileId = animDef.BaseTileId + animDef.NumTiles - 1;
                    if (tile.TileId >= startTileId && tile.TileId <= endTileId && tile.TileId < 512)
                        return true;
                }
            }

            // Check secondary tileset animations (tiles >= 512)
            foreach (var animDef in secondaryAnimDefs)
            {
                if (animDef.IsSecondary)
                {
                    int startTileId = animDef.BaseTileId + 512;
                    int endTileId = animDef.BaseTileId + animDef.NumTiles - 1 + 512;
                    if (tile.TileId >= startTileId && tile.TileId <= endTileId)
                        return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Track if metatile uses animated tiles.
    /// </summary>
    private void TrackAnimatedMetatile(Metatile metatile, int metatileId, string tilesetName, int bottomGid, int topGid, bool metatileIsSecondary)
    {
        CheckTilesetAnimations(metatile, metatileId, TilesetPair.PrimaryTileset, false, bottomGid, topGid, metatileIsSecondary);
        CheckTilesetAnimations(metatile, metatileId, TilesetPair.SecondaryTileset, true, bottomGid, topGid, metatileIsSecondary);
    }

    private void CheckTilesetAnimations(Metatile metatile, int metatileId, string tilesetName, bool animIsSecondary, int bottomGid, int topGid, bool metatileIsSecondary)
    {
        var animDefs = _animScanner.GetAnimationsForTileset(tilesetName);
        if (animDefs.Length == 0) return;

        foreach (var animDef in animDefs)
        {
            if (animDef.IsSecondary != animIsSecondary) continue;

            int startTileId = animDef.BaseTileId;
            int endTileId = animDef.BaseTileId + animDef.NumTiles - 1;

            if (animIsSecondary)
            {
                startTileId += 512;
                endTileId += 512;
            }

            // Check if this metatile actually uses animated tiles by checking source tile IDs
            // Check BOTH bottom and top layers SEPARATELY - need to know which layer uses animation
            // Also verify the tile is from the correct tileset (primary vs secondary)
            bool CheckTileForAnimation(TileData t)
            {
                // For primary animations: tile must be in range 508-511 (NOT offset by 512)
                // For secondary animations: tile must be >= 512 AND (tileId - 512) in the animation range
                if (!animIsSecondary)
                {
                    // Primary animation - tile ID should be 0-511 and in the animation range
                    return t.TileId >= startTileId && t.TileId <= endTileId && t.TileId < 512;
                }
                else
                {
                    // Secondary animation - tile ID should be >= 512 and (tileId - 512) in the base range
                    return t.TileId >= startTileId && t.TileId <= endTileId;
                }
            }

            // Check each layer separately - we need to animate BOTH layers if BOTH use animated tiles
            bool bottomUsesAnim = metatile.BottomTiles.Any(CheckTileForAnimation);
            bool topUsesAnim = metatile.TopTiles.Any(CheckTileForAnimation);
            bool usesAnimatedTiles = bottomUsesAnim || topUsesAnim;

            if (usesAnimatedTiles)
            {
                var animKey = (tilesetName, animDef.Name);
                if (!_animatedMetatiles.TryGetValue(animKey, out var metatileSet))
                {
                    metatileSet = new HashSet<(int, int)>();
                    _animatedMetatiles[animKey] = metatileSet;
                }

                // Store metatile ID with its bottomGid - each metatile is tracked uniquely
                metatileSet.Add((metatileId, bottomGid));

                // Store metatile data keyed by metatileId for uniqueness
                // Now includes topGid and flags for which layers use animated tiles
                // This ensures each animated metatile is processed separately for BOTH layers
                if (!_animatedMetatileData.ContainsKey(metatileId))
                {
                    _animatedMetatileData[metatileId] = (metatile, metatileId, bottomGid, topGid, metatileIsSecondary, bottomUsesAnim, topUsesAnim);
                }

                // Override properties for animated metatiles so their properties are used
                // instead of the first non-animated metatile that shared this GID
                var builder = metatileIsSecondary ? _secondaryBuilder : _primaryBuilder;
                builder.TrackTileProperty(bottomGid, metatile.Behavior, metatile.TerrainType, forceOverride: true);
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
            if (!_animatedMetatiles.TryGetValue(animKey, out var metatilesToAnimate) || metatilesToAnimate.Count == 0)
                continue;

            var frames = _animScanner.ExtractAnimationFrames(tilesetName, animDef, palettes);
            if (frames.Count == 0) continue;

            var frameSequence = animDef.FrameSequence ?? Enumerable.Range(0, frames.Count).ToArray();

            if (frames[0].Width == 16 && frames[0].Height == 16)
            {
                ProcessMetatileAnimation(tilesetName, animDef, frames, frameSequence, metatilesToAnimate, isSecondary);
            }
            else
            {
                // Tile strip animation (8x8 tiles laid out in a strip)
                ProcessTileStripAnimation(tilesetName, animDef, frames, frameSequence, metatilesToAnimate, isSecondary);
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
        HashSet<(int MetatileId, int BottomGid)> metatilesToAnimate,
        bool isSecondary)
    {
        var builder = isSecondary ? _secondaryBuilder : _primaryBuilder;
        var frameGids = new int[frames.Count];

        for (int i = 0; i < frames.Count; i++)
        {
            // Animation frames go through same deduplication via the individual builder
            var gid = builder.ProcessMetatileImage(1000000 + i, frames[i]); // Use high ID for animation frames
            frameGids[i] = (int)(gid & GidMask);
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

        // Apply animation only to metatiles from the SAME tileset type
        // 16x16 metatile animations are pre-rendered frames designed for specific metatile layouts
        // Only apply to metatiles where the animation's tileset type matches the metatile's tileset type
        foreach (var (metatileId, bottomGid) in metatilesToAnimate)
        {
            // Check if this metatile is from the correct tileset type
            if (_animatedMetatileData.TryGetValue(metatileId, out var metatileInfo))
            {
                var (_, _, storedBottomGid, storedTopGid, metatileIsSecondary, bottomUsesAnim, topUsesAnim) = metatileInfo;
                // Only apply primary animations to primary metatiles, secondary to secondary
                if (metatileIsSecondary != isSecondary)
                    continue;

                // Apply animation to bottom layer if it uses animated tiles
                if (bottomUsesAnim)
                {
                    builder.AddAnimation(new TileAnimation(bottomGid - 1, animFrames.ToArray()));
                }

                // Apply animation to top layer if it uses animated tiles
                // 16x16 pre-rendered animations apply the same frames to both layers
                if (topUsesAnim)
                {
                    builder.AddAnimation(new TileAnimation(storedTopGid - 1, animFrames.ToArray()));
                }
            }
            else
            {
                // Fallback: apply to bottom only
                builder.AddAnimation(new TileAnimation(bottomGid - 1, animFrames.ToArray()));
            }
        }
    }

    /// <summary>
    /// Process tile strip animations (8x8 tiles laid out in strips like water, waterfall).
    /// Re-renders affected metatiles with substituted animation tiles for each frame.
    /// Now processes each metatile individually to avoid GID sharing issues.
    /// CRITICAL FIX: Now generates animations for BOTH bottom and top layers when needed.
    /// In the original GBA, when tiles 508-511 animate, they animate everywhere they appear
    /// in VRAM - including both bottom AND top layers of a metatile.
    /// </summary>
    private void ProcessTileStripAnimation(
        string tilesetName,
        AnimationDefinition animDef,
        List<Image<Rgba32>> frames,
        int[] frameSequence,
        HashSet<(int MetatileId, int BottomGid)> metatilesToAnimate,
        bool animIsSecondary)
    {
        if (metatilesToAnimate.Count == 0 || frames.Count == 0)
            return;

        // Calculate the base tile ID for substitution
        // Secondary tileset tiles are offset by 512 in the VRAM
        int baseTileId = animDef.BaseTileId;
        if (animIsSecondary)
        {
            baseTileId += 512;
        }

        // For each animated metatile, we need to generate frame images by re-rendering
        // the metatile with substituted 8x8 tiles
        // CRITICAL: Each metatile is processed individually by metatileId, not by shared GID
        foreach (var (metatileId, bottomGid) in metatilesToAnimate)
        {
            if (!_animatedMetatileData.TryGetValue(metatileId, out var metatileInfo))
                continue;

            var (metatile, _, storedBottomGid, storedTopGid, metatileIsSecondary, bottomUsesAnim, topUsesAnim) = metatileInfo;
            var builder = metatileIsSecondary ? _secondaryBuilder : _primaryBuilder;

            // Generate frame GIDs for BOTH bottom and top layers
            var bottomFrameGids = new int[frames.Count];
            var topFrameGids = new int[frames.Count];

            for (int frameIdx = 0; frameIdx < frames.Count; frameIdx++)
            {
                // Extract 8x8 tiles from this animation frame
                var frameTiles = _animScanner.ExtractTilesFromFrame(frames[frameIdx], animDef.NumTiles, 8);

                if (frameTiles.Count == 0)
                    continue;

                // Build substitution dictionary: tile ID -> frame tile image
                var substitutions = new Dictionary<int, Image<Rgba32>>();
                for (int tileOffset = 0; tileOffset < Math.Min(animDef.NumTiles, frameTiles.Count); tileOffset++)
                {
                    int tileId = baseTileId + tileOffset;
                    substitutions[tileId] = frameTiles[tileOffset];
                }

                // Re-render the metatile with substituted tiles
                // This will substitute tiles in BOTH bottom and top layers
                var (bottomFrame, topFrame) = _renderer.RenderMetatileWithSubstitution(
                    metatile,
                    TilesetPair.PrimaryTileset,
                    TilesetPair.SecondaryTileset,
                    substitutions);

                // Add bottom frame to tileset if bottom layer uses animated tiles
                if (bottomUsesAnim)
                {
                    var bottomFrameId = 2000000 + (metatileId * 100) + frameIdx;
                    var gid = builder.ProcessMetatileImage(bottomFrameId, bottomFrame);
                    bottomFrameGids[frameIdx] = (int)(gid & GidMask);
                }

                // Add top frame to tileset if top layer uses animated tiles
                // Use different ID range to ensure uniqueness
                if (topUsesAnim)
                {
                    var topFrameId = 3000000 + (metatileId * 100) + frameIdx;
                    var gid = builder.ProcessMetatileImage(topFrameId, topFrame);
                    topFrameGids[frameIdx] = (int)(gid & GidMask);
                }

                // Clean up frame images
                bottomFrame.Dispose();
                topFrame.Dispose();
                foreach (var tile in frameTiles)
                    tile.Dispose();
            }

            // Build and apply animation for bottom layer
            if (bottomUsesAnim)
            {
                var bottomAnimFrames = new List<AnimationFrame>();
                foreach (var seqIdx in frameSequence)
                {
                    if (seqIdx < bottomFrameGids.Length && bottomFrameGids[seqIdx] > 0)
                    {
                        bottomAnimFrames.Add(new AnimationFrame(bottomFrameGids[seqIdx] - 1, animDef.DurationMs));
                    }
                }

                if (bottomAnimFrames.Count > 0)
                {
                    builder.AddAnimation(new TileAnimation(bottomGid - 1, bottomAnimFrames.ToArray()));
                }
            }

            // Build and apply animation for top layer
            // This is the critical fix - top layer now also gets animation when it uses animated tiles
            if (topUsesAnim)
            {
                var topAnimFrames = new List<AnimationFrame>();
                foreach (var seqIdx in frameSequence)
                {
                    if (seqIdx < topFrameGids.Length && topFrameGids[seqIdx] > 0)
                    {
                        topAnimFrames.Add(new AnimationFrame(topFrameGids[seqIdx] - 1, animDef.DurationMs));
                    }
                }

                if (topAnimFrames.Count > 0)
                {
                    builder.AddAnimation(new TileAnimation(storedTopGid - 1, topAnimFrames.ToArray()));
                }
            }
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

    /// <summary>Actual tileset type (from folder structure) for the primary slot tileset.</summary>
    public string PrimaryTilesetType => _primaryBuilder.TilesetType;

    /// <summary>Actual tileset type (from folder structure) for the secondary slot tileset.</summary>
    public string SecondaryTilesetType => _secondaryBuilder.TilesetType;

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
