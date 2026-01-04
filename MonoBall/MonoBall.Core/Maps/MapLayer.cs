using System.Text.Json.Serialization;

namespace MonoBall.Core.Maps;

/// <summary>
///     Represents a layer in a map definition.
/// </summary>
public class MapLayer
{
    /// <summary>
    ///     The unique identifier for the layer.
    ///     Note: JSON uses "id" but property is named LayerId for clarity.
    /// </summary>
    [JsonPropertyName("id")]
    public string LayerId { get; set; } = string.Empty;

    /// <summary>
    ///     The name of the layer.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    ///     The type of layer (e.g., "tilelayer").
    /// </summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    /// <summary>
    ///     The width of the layer in tiles.
    /// </summary>
    [JsonPropertyName("width")]
    public int Width { get; set; }

    /// <summary>
    ///     The height of the layer in tiles.
    /// </summary>
    [JsonPropertyName("height")]
    public int Height { get; set; }

    /// <summary>
    ///     Whether the layer is visible.
    /// </summary>
    [JsonPropertyName("visible")]
    public bool Visible { get; set; } = true;

    /// <summary>
    ///     The opacity of the layer (0.0 to 1.0).
    /// </summary>
    [JsonPropertyName("opacity")]
    public float Opacity { get; set; } = 1.0f;

    /// <summary>
    ///     The X offset of the layer in pixels.
    /// </summary>
    [JsonPropertyName("offsetX")]
    public int OffsetX { get; set; }

    /// <summary>
    ///     The Y offset of the layer in pixels.
    /// </summary>
    [JsonPropertyName("offsetY")]
    public int OffsetY { get; set; }

    /// <summary>
    ///     The base64-encoded tile data.
    /// </summary>
    [JsonPropertyName("tileData")]
    public string? TileData { get; set; }

    /// <summary>
    ///     Optional image path for the layer.
    /// </summary>
    [JsonPropertyName("imagePath")]
    public string? ImagePath { get; set; }
}
