namespace Porycon3.Models;

/// <summary>
/// Layer data with uint GIDs to preserve Tiled flip flags in high bits.
/// Bit 31 (0x80000000): Horizontal flip
/// Bit 30 (0x40000000): Vertical flip
/// </summary>
public class SharedLayerData
{
    public required string Name { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public uint[] Data { get; init; } = Array.Empty<uint>();
    public int Elevation { get; init; } = 0;
}

/// <summary>
/// Represents a single tile layer in the output map.
/// Compatible with Tiled map format.
/// </summary>
public class LayerData
{
    /// <summary>
    /// Layer name (e.g., "Bg3", "Bg2", "Bg1").
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Width of the layer in tiles.
    /// </summary>
    public int Width { get; init; }

    /// <summary>
    /// Height of the layer in tiles.
    /// </summary>
    public int Height { get; init; }

    /// <summary>
    /// Tile data as a flat array of GIDs (row-major order).
    /// 0 = empty tile, >0 = tile GID with optional flip flags.
    /// </summary>
    public int[] Data { get; init; } = Array.Empty<int>();

    /// <summary>
    /// Elevation level for this layer (0-15).
    /// Determines rendering priority and collision behavior.
    /// 0 = ground, 1-14 = specific levels, 15 = bridge.
    /// </summary>
    public int Elevation { get; init; } = 0;

    /// <summary>
    /// Optional layer opacity (0.0 - 1.0).
    /// </summary>
    public float Opacity { get; init; } = 1.0f;

    /// <summary>
    /// Whether the layer is visible.
    /// </summary>
    public bool Visible { get; init; } = true;
}
