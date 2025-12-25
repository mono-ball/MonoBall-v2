namespace Porycon3.Models;

/// <summary>
/// A single frame in an animation.
/// </summary>
public record AnimationFrame(int TileId, int DurationMs);

/// <summary>
/// Animation definition for a tile.
/// </summary>
public record TileAnimation(int LocalTileId, AnimationFrame[] Frames);

/// <summary>
/// Definition of an animation source from pokeemerald.
/// </summary>
public record AnimationDefinition(
    string Name,
    int BaseTileId,
    int NumTiles,
    string AnimFolder,
    int DurationMs,
    bool IsSecondary,
    int[]? FrameSequence
);
