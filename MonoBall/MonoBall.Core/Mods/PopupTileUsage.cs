using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace MonoBall.Core.Mods;

/// <summary>
///     Maps tile indices to their usage in a popup outline frame.
///     Used for tile-based 9-patch rendering of popup frames.
/// </summary>
public class PopupTileUsage
{
    /// <summary>
    ///     Gets or sets the list of tile indices for the top edge (repeated horizontally).
    /// </summary>
    [JsonPropertyName("topEdge")]
    public List<int> TopEdge { get; set; } = new();

    /// <summary>
    ///     Gets or sets the tile index for the left top corner.
    /// </summary>
    [JsonPropertyName("leftTopCorner")]
    public int LeftTopCorner { get; set; }

    /// <summary>
    ///     Gets or sets the tile index for the right top corner.
    /// </summary>
    [JsonPropertyName("rightTopCorner")]
    public int RightTopCorner { get; set; }

    /// <summary>
    ///     Gets or sets the list of tile indices for the left edge (repeated vertically).
    /// </summary>
    [JsonPropertyName("leftEdge")]
    public List<int> LeftEdge { get; set; } = new();

    /// <summary>
    ///     Gets or sets the list of tile indices for the right edge (repeated vertically).
    /// </summary>
    [JsonPropertyName("rightEdge")]
    public List<int> RightEdge { get; set; } = new();

    /// <summary>
    ///     Gets or sets the tile index for the left middle edge (repeated vertically).
    ///     Legacy property for backwards compatibility.
    /// </summary>
    [JsonPropertyName("leftMiddle")]
    public int LeftMiddle { get; set; }

    /// <summary>
    ///     Gets or sets the tile index for the right middle edge (repeated vertically).
    ///     Legacy property for backwards compatibility.
    /// </summary>
    [JsonPropertyName("rightMiddle")]
    public int RightMiddle { get; set; }

    /// <summary>
    ///     Gets or sets the tile index for the left bottom corner.
    /// </summary>
    [JsonPropertyName("leftBottomCorner")]
    public int LeftBottomCorner { get; set; }

    /// <summary>
    ///     Gets or sets the tile index for the right bottom corner.
    /// </summary>
    [JsonPropertyName("rightBottomCorner")]
    public int RightBottomCorner { get; set; }

    /// <summary>
    ///     Gets or sets the list of tile indices for the bottom edge (repeated horizontally).
    /// </summary>
    [JsonPropertyName("bottomEdge")]
    public List<int> BottomEdge { get; set; } = new();
}
