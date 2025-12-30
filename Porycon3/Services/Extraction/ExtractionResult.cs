namespace Porycon3.Services.Extraction;

/// <summary>
/// Standardized result from any extraction operation.
/// All extractors should return this type for consistency.
/// </summary>
public record ExtractionResult
{
    /// <summary>Name of the extractor that produced this result.</summary>
    public required string ExtractorName { get; init; }

    /// <summary>Whether the extraction completed successfully.</summary>
    public bool Success { get; init; } = true;

    /// <summary>Primary count of items extracted (e.g., Pokemon, Sprites, Maps).</summary>
    public int ItemCount { get; init; }

    /// <summary>Secondary counts for additional categories.</summary>
    public Dictionary<string, int> AdditionalCounts { get; init; } = new();

    /// <summary>Total duration of the extraction.</summary>
    public TimeSpan Duration { get; init; }

    /// <summary>List of errors encountered during extraction.</summary>
    public List<ExtractionError> Errors { get; init; } = new();

    /// <summary>List of warnings encountered during extraction.</summary>
    public List<string> Warnings { get; init; } = new();

    /// <summary>Output path where files were written.</summary>
    public string? OutputPath { get; init; }

    /// <summary>Whether there were any errors.</summary>
    public bool HasErrors => Errors.Count > 0;

    /// <summary>Whether there were any warnings.</summary>
    public bool HasWarnings => Warnings.Count > 0;

    /// <summary>Total count including all additional counts.</summary>
    public int TotalCount => ItemCount + AdditionalCounts.Values.Sum();

    /// <summary>Create a success result with counts.</summary>
    public static ExtractionResult Succeeded(string extractorName, int itemCount, TimeSpan duration)
        => new()
        {
            ExtractorName = extractorName,
            Success = true,
            ItemCount = itemCount,
            Duration = duration
        };

    /// <summary>Create a failure result with error.</summary>
    public static ExtractionResult Failed(string extractorName, string error, TimeSpan duration)
        => new()
        {
            ExtractorName = extractorName,
            Success = false,
            Duration = duration,
            Errors = [new ExtractionError("", error)]
        };
}

/// <summary>
/// Represents an error that occurred during extraction.
/// </summary>
public record ExtractionError(string ItemName, string Message, Exception? Exception = null);
