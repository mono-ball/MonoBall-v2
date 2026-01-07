using System.Text.Json;

namespace MonoBall.Core.Mods;

/// <summary>
///     Metadata about a loaded definition, including its source mod and operation type.
/// </summary>
public class DefinitionMetadata
{
    /// <summary>
    ///     The unique identifier of the definition.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    ///     The mod ID that originally defined this definition.
    /// </summary>
    public string OriginalModId { get; set; } = string.Empty;

    /// <summary>
    ///     The mod ID that last modified this definition.
    /// </summary>
    public string LastModifiedByModId { get; set; } = string.Empty;

    /// <summary>
    ///     The operation that was applied when this definition was loaded.
    /// </summary>
    public DefinitionOperation Operation { get; set; } = DefinitionOperation.Create;

    /// <summary>
    ///     The definition type/category (e.g., "Font", "TileBehavior", "Behavior").
    /// </summary>
    public string DefinitionType { get; set; } = string.Empty;

    /// <summary>
    ///     The actual definition data as a JsonElement for flexible querying.
    /// </summary>
    public JsonElement Data { get; set; }

    /// <summary>
    ///     File path relative to the mod root where this definition was loaded from.
    /// </summary>
    public string SourcePath { get; set; } = string.Empty;
}
