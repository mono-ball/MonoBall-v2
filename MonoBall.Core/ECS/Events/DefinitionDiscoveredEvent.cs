using MonoBall.Core.Mods;

namespace MonoBall.Core.ECS.Events;

/// <summary>
/// Event fired when a definition is discovered and loaded.
/// Contains essential fields only (struct per project rules).
/// </summary>
public struct DefinitionDiscoveredEvent
{
    /// <summary>
    /// The mod ID that loaded this definition.
    /// </summary>
    public string ModId { get; set; }

    /// <summary>
    /// The inferred definition type.
    /// </summary>
    public string DefinitionType { get; set; }

    /// <summary>
    /// The unique identifier of the definition.
    /// </summary>
    public string DefinitionId { get; set; }

    /// <summary>
    /// The file path relative to mod root where this definition was loaded from.
    /// </summary>
    public string FilePath { get; set; }

    /// <summary>
    /// The mod ID that originally defined this definition.
    /// </summary>
    public string SourceModId { get; set; }

    /// <summary>
    /// The operation that was applied when loading this definition.
    /// </summary>
    public DefinitionOperation Operation { get; set; }

    // Note: Full DefinitionMetadata not included - systems can query registry if needed
}
