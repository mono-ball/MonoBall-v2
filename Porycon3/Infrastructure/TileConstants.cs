namespace Porycon3.Infrastructure;

/// <summary>
/// Shared constants for tile and metatile dimensions.
/// </summary>
public static class TileConstants
{
    /// <summary>Base tile size in pixels (8x8).</summary>
    public const int TileSize = 8;

    /// <summary>Metatile size in pixels (16x16, composed of 4 tiles).</summary>
    public const int MetatileSize = 16;

    /// <summary>Number of tiles per row in a standard tilesheet.</summary>
    public const int TilesPerRow = 16;

    /// <summary>Number of metatiles per row in a standard metatile sheet.</summary>
    public const int MetatilesPerRow = 8;

    /// <summary>Flip horizontal flag for GID encoding.</summary>
    public const uint FlipHorizontal = 0x80000000;

    /// <summary>Flip vertical flag for GID encoding.</summary>
    public const uint FlipVertical = 0x40000000;

    /// <summary>Flip diagonal flag for GID encoding.</summary>
    public const uint FlipDiagonal = 0x20000000;

    /// <summary>Mask to extract base GID without flip flags.</summary>
    public const uint GidMask = 0x0FFFFFFF;
}
