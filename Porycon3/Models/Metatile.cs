namespace Porycon3.Models;

/// <summary>
/// 16x16 metatile composed of 8 tiles (2x2 bottom + 2x2 top layers).
/// </summary>
public sealed class Metatile
{
    public required int Id { get; init; }
    public required TileData[] BottomTiles { get; init; } // 4 tiles (2x2)
    public required TileData[] TopTiles { get; init; }    // 4 tiles (2x2)
    public required int Behavior { get; init; }
    public required int TerrainType { get; init; }

    /// <summary>
    /// Layer type determines how metatile layers map to BG layers.
    /// Extracted from behavior: (behavior >> 5) & 0x3
    /// </summary>
    public MetatileLayerType LayerType => (MetatileLayerType)((Behavior >> 5) & 0x3);
}

public enum MetatileLayerType
{
    /// <summary>Bottom -> Bg2 (objects), Top -> Bg1 (overhead)</summary>
    Normal = 0,
    /// <summary>Bottom -> Bg3 (ground), Top -> Bg2 (objects)</summary>
    Covered = 1,
    /// <summary>Bottom -> Bg3 (ground), Top -> Bg1 (overhead)</summary>
    Split = 2
}
