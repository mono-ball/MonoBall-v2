using System.Text.Json;
using System.Text.Json.Serialization;

namespace MonoBall.Core.Mods.Utilities;

/// <summary>
/// Factory for creating consistent JsonSerializerOptions across the mod system.
/// </summary>
internal static class JsonSerializerOptionsFactory
{
    /// <summary>
    /// Gets the default JSON serializer options for mod system.
    /// </summary>
    public static JsonSerializerOptions Default =>
        new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            Converters = { new JsonStringEnumConverter() },
        };

    /// <summary>
    /// Gets JSON serializer options for reading mod manifests.
    /// </summary>
    public static JsonSerializerOptions ForManifests => Default;
}
