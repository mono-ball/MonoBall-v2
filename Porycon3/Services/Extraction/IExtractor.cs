namespace Porycon3.Services.Extraction;

/// <summary>
/// Standard interface for all asset extractors.
/// Provides consistent contract for extraction operations.
/// </summary>
public interface IExtractor
{
    /// <summary>Display name of the extractor.</summary>
    string Name { get; }

    /// <summary>Short description of what this extractor does.</summary>
    string Description { get; }

    /// <summary>
    /// When true, suppresses all console output (for use with orchestrator).
    /// </summary>
    bool QuietMode { get; set; }

    /// <summary>
    /// Optional callback to report progress (0.0 to 1.0) during extraction.
    /// Used by orchestrators to update external progress displays.
    /// </summary>
    IProgress<double>? ProgressCallback { get; set; }

    /// <summary>
    /// Execute the extraction operation.
    /// </summary>
    /// <returns>Result containing counts, duration, and any errors.</returns>
    ExtractionResult ExtractAll();
}
