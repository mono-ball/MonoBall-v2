namespace Porycon3.Models;

/// <summary>
/// Result of a single map conversion operation.
/// </summary>
public class ConversionResult
{
    /// <summary>
    /// The map identifier that was converted.
    /// </summary>
    public required string MapId { get; init; }

    /// <summary>
    /// Whether the conversion succeeded.
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// Error message if conversion failed.
    /// </summary>
    public string? Error { get; init; }

    /// <summary>
    /// Duration of the conversion operation.
    /// </summary>
    public TimeSpan Duration { get; init; }

    /// <summary>
    /// Number of layers generated.
    /// </summary>
    public int LayerCount { get; init; }

    /// <summary>
    /// Number of tiles processed.
    /// </summary>
    public int TileCount { get; init; }
}
