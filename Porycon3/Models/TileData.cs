namespace Porycon3.Models;

/// <summary>
/// Represents a single 8x8 pixel tile with flip and palette info.
/// Parsed from raw GBA tile data (16-bit value).
/// </summary>
public readonly record struct TileData(
    int TileId,
    int PaletteIndex,
    bool FlipHorizontal,
    bool FlipVertical)
{
    /// <summary>
    /// Parse from raw 16-bit GBA tile value.
    /// Bits 0-9: Tile ID (0-1023)
    /// Bit 10: Horizontal flip
    /// Bit 11: Vertical flip
    /// Bits 12-15: Palette index (0-15)
    /// </summary>
    public static TileData FromRaw(ushort raw) => new(
        TileId: raw & 0x3FF,
        PaletteIndex: (raw >> 12) & 0xF,
        FlipHorizontal: (raw & 0x400) != 0,
        FlipVertical: (raw & 0x800) != 0);

    public static TileData Empty => new(0, 0, false, false);
}
