using System.Text.Json;
using System.Text.Json.Serialization;

namespace Porycon3.Services.Extraction;

/// <summary>
/// Centralized JSON serialization options for all extractors.
/// Ensures consistent formatting across all output files.
/// </summary>
public static class JsonOptions
{
    /// <summary>
    /// Standard options for writing definition JSON files.
    /// - camelCase property names
    /// - Indented for readability
    /// - Nulls omitted
    /// - Enums as strings
    /// </summary>
    public static readonly JsonSerializerOptions Default = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    /// <summary>
    /// Compact options for large files where size matters.
    /// Same as Default but not indented.
    /// </summary>
    public static readonly JsonSerializerOptions Compact = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };
}
