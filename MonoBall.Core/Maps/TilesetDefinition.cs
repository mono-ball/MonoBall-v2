using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace MonoBall.Core.Maps;

/// <summary>
///     Represents a tileset definition loaded from JSON.
/// </summary>
public class TilesetDefinition
{
    /// <summary>
    ///     The unique identifier for the tileset.
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    ///     The name of the tileset.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    ///     The path to the texture image, relative to the mod directory.
    /// </summary>
    [JsonPropertyName("texturePath")]
    public string TexturePath { get; set; } = string.Empty;

    /// <summary>
    ///     The width of each tile in pixels.
    /// </summary>
    [JsonPropertyName("tileWidth")]
    public int TileWidth { get; set; }

    /// <summary>
    ///     The height of each tile in pixels.
    /// </summary>
    [JsonPropertyName("tileHeight")]
    public int TileHeight { get; set; }

    /// <summary>
    ///     The total number of tiles in the tileset.
    /// </summary>
    [JsonPropertyName("tileCount")]
    public int TileCount { get; set; }

    /// <summary>
    ///     The number of columns in the tileset image.
    /// </summary>
    [JsonPropertyName("columns")]
    public int Columns { get; set; }

    /// <summary>
    ///     The width of the tileset image in pixels.
    /// </summary>
    [JsonPropertyName("imageWidth")]
    public int ImageWidth { get; set; }

    /// <summary>
    ///     The height of the tileset image in pixels.
    /// </summary>
    [JsonPropertyName("imageHeight")]
    public int ImageHeight { get; set; }

    /// <summary>
    ///     The spacing between tiles in pixels.
    /// </summary>
    [JsonPropertyName("spacing")]
    public int Spacing { get; set; }

    /// <summary>
    ///     The margin around the tileset image in pixels.
    /// </summary>
    [JsonPropertyName("margin")]
    public int Margin { get; set; }

    /// <summary>
    ///     Array of tile definitions with special properties.
    /// </summary>
    [JsonPropertyName("tiles")]
    public List<TilesetTile> Tiles { get; set; } = new();
}
