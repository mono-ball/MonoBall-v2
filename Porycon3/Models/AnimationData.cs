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
/// Properties for a tile in the tileset, following the ID transformation conventions.
/// Null values indicate defaults (normal behavior, normal terrain, passable collision).
/// </summary>
public record TileProperty(
    /// <summary>Local tile ID (0-based index in the tileset)</summary>
    int LocalTileId,
    /// <summary>Behavior ID (e.g., "base:behavior:tiles/tall_grass"), null if normal</summary>
    string? BehaviorId,
    /// <summary>Terrain type ID (e.g., "base:terrain:grass"), null if normal</summary>
    string? TerrainId,
    /// <summary>Collision ID (e.g., "base:collision:water"), null if passable</summary>
    string? CollisionId
);

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
