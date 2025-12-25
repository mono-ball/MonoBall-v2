using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Porycon3.Models;

namespace Porycon3.Services;

/// <summary>
/// Key for identifying a metatile by its ID, tileset, and layer type.
/// </summary>
public readonly record struct MetatileKey(int MetatileId, string Tileset, MetatileLayerType LayerType);

/// <summary>
/// Builds per-map tilesheets containing rendered 16x16 metatile images.
/// Deduplicates identical images to minimize tilesheet size.
/// </summary>
public class MapTilesheetBuilder : IDisposable
{
    private const int MetatileSize = 16;
    private const int TilesPerRow = 16;

    private readonly string _pokeemeraldPath;
    private readonly MetatileRenderer _renderer;
    private readonly AnimationScanner _animScanner;

    // Map metatile key to rendered images (bottom, top)
    private readonly Dictionary<MetatileKey, (Image<Rgba32> Bottom, Image<Rgba32> Top)> _renderedMetatiles = new();

    // Map image bytes to GID (for deduplication)
    private readonly Dictionary<string, int> _imageHashToGid = new();

    // Map (metatile_key, is_top) to GID
    private readonly Dictionary<(MetatileKey Key, bool IsTop), int> _metatileToGid = new();

    // All unique images in GID order
    private readonly List<Image<Rgba32>> _uniqueImages = new();

    // Animation frame tiles: (tileset, animName, frameIdx) -> GID
    private readonly Dictionary<(string Tileset, string AnimName, int FrameIdx), int> _animationFrameGids = new();

    // Track GIDs that need animation: (tilesetName, animName) -> set of GIDs
    private readonly Dictionary<(string Tileset, string AnimName), HashSet<int>> _animatedGids = new();

    // Animation definitions for output
    private readonly List<TileAnimation> _animations = new();

    private int _nextGid = 1; // Tiled uses 1-based GIDs (0 = empty)

    public MapTilesheetBuilder(string pokeemeraldPath)
    {
        _pokeemeraldPath = pokeemeraldPath;
        _renderer = new MetatileRenderer(pokeemeraldPath);
        _animScanner = new AnimationScanner(pokeemeraldPath);
    }

    /// <summary>
    /// Process a metatile and add it to the tilesheet.
    /// Returns the GIDs for bottom and top layers.
    /// </summary>
    public (int BottomGid, int TopGid) ProcessMetatile(
        Metatile metatile,
        int metatileId,
        string tilesetName,
        string primaryTileset,
        string secondaryTileset)
    {
        var key = new MetatileKey(metatileId, tilesetName, metatile.LayerType);

        // Check if already processed
        if (_metatileToGid.TryGetValue((key, false), out var existingBottomGid) &&
            _metatileToGid.TryGetValue((key, true), out var existingTopGid))
        {
            return (existingBottomGid, existingTopGid);
        }

        // Render the metatile
        var (bottomImage, topImage) = _renderer.RenderMetatile(metatile, primaryTileset, secondaryTileset);

        // Cache rendered images
        _renderedMetatiles[key] = (bottomImage, topImage);

        // Assign GIDs with deduplication
        var bottomGid = AssignGid(bottomImage);
        var topGid = AssignGid(topImage);

        _metatileToGid[(key, false)] = bottomGid;
        _metatileToGid[(key, true)] = topGid;

        // Check if this metatile uses animated tiles
        TrackAnimatedMetatile(metatile, tilesetName, primaryTileset, secondaryTileset, bottomGid, topGid);

        return (bottomGid, topGid);
    }

    /// <summary>
    /// Check if a metatile uses animated tile IDs and track it for animation.
    /// </summary>
    private void TrackAnimatedMetatile(Metatile metatile, string tilesetName, string primaryTileset, string secondaryTileset, int bottomGid, int topGid)
    {
        // Check primary tileset animations
        CheckTilesetAnimations(metatile, primaryTileset, false, bottomGid, topGid);

        // Check secondary tileset animations
        CheckTilesetAnimations(metatile, secondaryTileset, true, bottomGid, topGid);
    }

    private void CheckTilesetAnimations(Metatile metatile, string tilesetName, bool isSecondary, int bottomGid, int topGid)
    {
        var animDefs = _animScanner.GetAnimationsForTileset(tilesetName);
        if (animDefs.Length == 0) return;

        // All tiles in the metatile
        var allTiles = metatile.BottomTiles.Concat(metatile.TopTiles).ToArray();

        foreach (var animDef in animDefs)
        {
            // Skip animations that don't match secondary status
            if (animDef.IsSecondary != isSecondary) continue;

            // Calculate tile ID range for this animation
            int startTileId = animDef.BaseTileId;
            int endTileId = animDef.BaseTileId + animDef.NumTiles - 1;

            // If secondary, tile IDs in metatile are offset by 512
            if (isSecondary)
            {
                startTileId += 512;
                endTileId += 512;
            }

            // Check if any tiles in the metatile are in the animated range
            bool usesAnimatedTiles = allTiles.Any(t => t.TileId >= startTileId && t.TileId <= endTileId);

            if (usesAnimatedTiles)
            {
                var animKey = (tilesetName, animDef.Name);
                if (!_animatedGids.TryGetValue(animKey, out var gidSet))
                {
                    gidSet = new HashSet<int>();
                    _animatedGids[animKey] = gidSet;
                }

                // Track which GIDs need animation (both bottom and top might need it)
                // Bottom layer typically has the main animated tiles
                gidSet.Add(bottomGid);
            }
        }
    }

    /// <summary>
    /// Assign a GID to an image, deduplicating identical images.
    /// </summary>
    private int AssignGid(Image<Rgba32> image)
    {
        // Create hash from image bytes
        var hash = ComputeImageHash(image);

        if (_imageHashToGid.TryGetValue(hash, out var existingGid))
        {
            return existingGid;
        }

        // New unique image
        var gid = _nextGid++;
        _imageHashToGid[hash] = gid;
        _uniqueImages.Add(image.Clone());

        return gid;
    }

    /// <summary>
    /// Compute a hash string from image pixel data.
    /// </summary>
    private static string ComputeImageHash(Image<Rgba32> image)
    {
        // Use pixel data bytes as hash key
        var pixelData = new byte[image.Width * image.Height * 4];
        image.CopyPixelDataTo(pixelData);
        return Convert.ToBase64String(pixelData);
    }

    /// <summary>
    /// Get the GID for a metatile's bottom or top layer.
    /// </summary>
    public int GetGid(int metatileId, string tilesetName, MetatileLayerType layerType, bool isTop)
    {
        var key = new MetatileKey(metatileId, tilesetName, layerType);
        if (_metatileToGid.TryGetValue((key, isTop), out var gid))
            return gid;
        return 0; // Not found
    }

    /// <summary>
    /// Build the final tilesheet image from all unique tiles.
    /// </summary>
    public Image<Rgba32> BuildTilesheetImage()
    {
        if (_uniqueImages.Count == 0)
        {
            // Return empty image
            return new Image<Rgba32>(MetatileSize, MetatileSize);
        }

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

    /// <summary>
    /// Get the total number of unique tiles.
    /// </summary>
    public int TileCount => _uniqueImages.Count;

    /// <summary>
    /// Get all metatile-to-GID mappings for remapping layer data.
    /// </summary>
    public Dictionary<(MetatileKey Key, bool IsTop), int> GetAllGidMappings()
    {
        return new Dictionary<(MetatileKey Key, bool IsTop), int>(_metatileToGid);
    }

    /// <summary>
    /// Process animations for tilesets used in a map.
    /// Adds animation frame tiles to the tilesheet and builds animation data.
    /// </summary>
    public void ProcessAnimations(string primaryTileset, string secondaryTileset, Rgba32[]?[]? primaryPalettes, Rgba32[]?[]? secondaryPalettes)
    {
        // Process primary tileset animations
        ProcessTilesetAnimations(primaryTileset, primaryPalettes, false);

        // Process secondary tileset animations
        ProcessTilesetAnimations(secondaryTileset, secondaryPalettes, true);
    }

    private void ProcessTilesetAnimations(string tilesetName, Rgba32[]?[]? palettes, bool isSecondary)
    {
        var animDefs = _animScanner.GetAnimationsForTileset(tilesetName);
        if (animDefs.Length == 0) return;

        foreach (var animDef in animDefs)
        {
            // Only process animations matching the tileset type
            if (animDef.IsSecondary != isSecondary) continue;

            // Check if any GIDs use this animation
            var animKey = (tilesetName, animDef.Name);
            if (!_animatedGids.TryGetValue(animKey, out var gidsToAnimate) || gidsToAnimate.Count == 0)
                continue;

            var frames = _animScanner.ExtractAnimationFrames(tilesetName, animDef, palettes);
            if (frames.Count == 0)
            {
                continue;
            }

            // Determine frame sequence
            var frameSequence = animDef.FrameSequence ?? Enumerable.Range(0, frames.Count).ToArray();

            // For metatile animations (16x16), add frames and create animations
            if (frames[0].Width == 16 && frames[0].Height == 16)
            {
                ProcessMetatileAnimation(tilesetName, animDef, frames, frameSequence, gidsToAnimate);
            }

            // Dispose frame images
            foreach (var frame in frames)
            {
                frame.Dispose();
            }
        }
    }

    private void ProcessMetatileAnimation(
        string tilesetName,
        AnimationDefinition animDef,
        List<Image<Rgba32>> frames,
        int[] frameSequence,
        HashSet<int> gidsToAnimate)
    {
        // Add all animation frame tiles to the tilesheet
        var frameGids = new int[frames.Count];

        for (int i = 0; i < frames.Count; i++)
        {
            var frameGid = AssignGid(frames[i]);
            frameGids[i] = frameGid;
            _animationFrameGids[(tilesetName, animDef.Name, i)] = frameGid;
        }

        // Build animation frames using the sequence
        var animFrames = new List<AnimationFrame>();
        foreach (var seqIdx in frameSequence)
        {
            if (seqIdx < frameGids.Length)
            {
                // tileId in animation is 0-based (GID - 1)
                animFrames.Add(new AnimationFrame(frameGids[seqIdx] - 1, animDef.DurationMs));
            }
        }

        // Create animation for each tracked GID
        foreach (var gid in gidsToAnimate)
        {
            // localTileId is 0-based (GID - 1)
            _animations.Add(new TileAnimation(gid - 1, animFrames.ToArray()));
        }
    }

    /// <summary>
    /// Get all animations for the tileset JSON.
    /// </summary>
    public List<TileAnimation> GetAnimations() => _animations;

    public void Dispose()
    {
        foreach (var (bottom, top) in _renderedMetatiles.Values)
        {
            bottom.Dispose();
            top.Dispose();
        }
        _renderedMetatiles.Clear();

        foreach (var image in _uniqueImages)
        {
            image.Dispose();
        }
        _uniqueImages.Clear();

        _renderer.Dispose();
    }
}
