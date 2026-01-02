namespace MonoBall.Core.Maps.Utilities;

/// <summary>
///     Constants for tile GID encoding, including flip flags used by Tiled/TMX format.
///     GIDs are 32-bit values where the high 4 bits encode flip/rotation flags.
/// </summary>
public static class TileConstants
{
    /// <summary>
    ///     Flag for horizontal flip (0x80000000).
    /// </summary>
    public const uint FlipHorizontal = 0x80000000;

    /// <summary>
    ///     Flag for vertical flip (0x40000000).
    /// </summary>
    public const uint FlipVertical = 0x40000000;

    /// <summary>
    ///     Flag for diagonal flip/rotation (0x20000000).
    /// </summary>
    public const uint FlipDiagonal = 0x20000000;

    /// <summary>
    ///     Mask to extract the actual GID from a flagged value (0x0FFFFFFF).
    /// </summary>
    public const uint GidMask = 0x0FFFFFFF;

    /// <summary>
    ///     Combined mask of all flip flags.
    /// </summary>
    public const uint FlipMask = FlipHorizontal | FlipVertical | FlipDiagonal;

    /// <summary>
    ///     Extracts the raw GID (tile index) from a flagged GID value.
    /// </summary>
    /// <param name="gid">The GID value potentially containing flip flags.</param>
    /// <returns>The raw GID without flip flags.</returns>
    public static int GetRawGid(int gid)
    {
        // Convert to uint to handle the high bit correctly, mask, then convert back
        return (int)((uint)gid & GidMask);
    }

    /// <summary>
    ///     Checks if a GID has the horizontal flip flag set.
    /// </summary>
    public static bool IsFlippedHorizontally(int gid) => ((uint)gid & FlipHorizontal) != 0;

    /// <summary>
    ///     Checks if a GID has the vertical flip flag set.
    /// </summary>
    public static bool IsFlippedVertically(int gid) => ((uint)gid & FlipVertical) != 0;

    /// <summary>
    ///     Checks if a GID has the diagonal flip flag set.
    /// </summary>
    public static bool IsFlippedDiagonally(int gid) => ((uint)gid & FlipDiagonal) != 0;

    /// <summary>
    ///     Checks if a GID has any flip flags set.
    /// </summary>
    public static bool HasFlipFlags(int gid) => ((uint)gid & FlipMask) != 0;
}
