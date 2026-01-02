namespace MonoBall.Core.Mods;

/// <summary>
/// Result type for definition loading (unified error handling).
/// </summary>
internal struct DefinitionLoadResult
{
    public DefinitionMetadata? Metadata { get; set; }
    public string? Error { get; set; }

    public bool IsError => Error != null;

    public static DefinitionLoadResult Success(DefinitionMetadata metadata) =>
        new() { Metadata = metadata };

    public static DefinitionLoadResult Failure(string error) => new() { Error = error };
}
