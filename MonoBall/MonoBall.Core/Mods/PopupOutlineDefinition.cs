using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace MonoBall.Core.Mods;

/// <summary>
///     Definition for a popup outline tile sheet or 9-slice configuration.
///     Outlines form the frame/border around the popup.
/// </summary>
public class PopupOutlineDefinition
{
    /// <summary>
    ///     Gets or sets the unique identifier for the outline.
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets the display name of the outline.
    /// </summary>
    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets the type of outline ("TileSheet" or "9Slice").
    /// </summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    /// <summary>
    ///     Gets a value indicating whether this is a tile sheet outline.
    /// </summary>
    public bool IsTileSheet => Type == "TileSheet";

    /// <summary>
    ///     Gets or sets the path to the texture file relative to the mod root.
    /// </summary>
    [JsonPropertyName("texturePath")]
    public string TexturePath { get; set; } = string.Empty;

    // Tile sheet properties
    /// <summary>
    ///     Gets or sets the width of each tile in pixels (for tile sheet mode).
    /// </summary>
    [JsonPropertyName("tileWidth")]
    public int TileWidth { get; set; }

    /// <summary>
    ///     Gets or sets the height of each tile in pixels (for tile sheet mode).
    /// </summary>
    [JsonPropertyName("tileHeight")]
    public int TileHeight { get; set; }

    /// <summary>
    ///     Gets or sets the total number of tiles in the sheet (for tile sheet mode).
    /// </summary>
    [JsonPropertyName("tileCount")]
    public int TileCount { get; set; }

    /// <summary>
    ///     Gets or sets the list of tile definitions (for tile sheet mode).
    /// </summary>
    [JsonPropertyName("tiles")]
    public List<PopupTileDefinition> Tiles { get; set; } = new();

    /// <summary>
    ///     Gets or sets the tile usage mapping (for tile sheet mode).
    /// </summary>
    [JsonPropertyName("tileUsage")]
    public PopupTileUsage? TileUsage { get; set; }

    // 9-slice properties (legacy support)
    /// <summary>
    ///     Gets or sets the corner width in pixels (for 9-slice mode).
    /// </summary>
    [JsonPropertyName("cornerWidth")]
    public int? CornerWidth { get; set; }

    /// <summary>
    ///     Gets or sets the corner height in pixels (for 9-slice mode).
    /// </summary>
    [JsonPropertyName("cornerHeight")]
    public int? CornerHeight { get; set; }
}
