namespace Porycon3.Models;

/// <summary>
/// Reference to a tile in the shared tileset, including flip flags.
/// Used to store tile references that may be flipped versions of canonical tiles.
/// </summary>
public readonly record struct TileReference(int TileId, bool FlipH, bool FlipV)
{
    /// <summary>
    /// Encode as a Tiled-compatible GID with flip flags in high bits.
    /// Bit 31 (0x80000000): Horizontal flip
    /// Bit 30 (0x40000000): Vertical flip
    /// </summary>
    public uint ToTiledGid(int firstGid = 1)
    {
        uint gid = (uint)(TileId + firstGid);
        if (FlipH) gid |= 0x80000000;
        if (FlipV) gid |= 0x40000000;
        return gid;
    }

    /// <summary>
    /// Create from a Tiled GID, extracting flip flags.
    /// </summary>
    public static TileReference FromTiledGid(uint gid, int firstGid = 1)
    {
        bool flipH = (gid & 0x80000000) != 0;
        bool flipV = (gid & 0x40000000) != 0;
        int tileId = (int)(gid & 0x0FFFFFFF) - firstGid;
        return new TileReference(tileId, flipH, flipV);
    }

    /// <summary>
    /// Empty/transparent tile reference.
    /// </summary>
    public static TileReference Empty => new(-1, false, false);

    public bool IsEmpty => TileId < 0;
}
