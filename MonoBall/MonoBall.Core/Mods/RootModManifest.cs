using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace MonoBall.Core.Mods;

/// <summary>
/// Represents the root mod.manifest file that specifies explicit load order.
/// </summary>
internal class RootModManifest
{
    /// <summary>
    /// Gets or sets the ordered list of mod IDs that specifies the load order.
    /// </summary>
    [JsonPropertyName("modOrder")]
    public List<string> ModOrder { get; set; } = new List<string>();
}
