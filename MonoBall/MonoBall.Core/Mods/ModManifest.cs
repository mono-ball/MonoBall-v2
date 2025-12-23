using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace MonoBall.Core.Mods
{
    /// <summary>
    /// Represents a mod manifest file (mod.json) that defines mod metadata and content structure.
    /// </summary>
    public class ModManifest
    {
        /// <summary>
        /// Unique identifier for the mod (e.g., "base:monoball-core").
        /// </summary>
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// Display name of the mod.
        /// </summary>
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Author of the mod.
        /// </summary>
        [JsonPropertyName("author")]
        public string Author { get; set; } = string.Empty;

        /// <summary>
        /// Version of the mod (semantic versioning recommended).
        /// </summary>
        [JsonPropertyName("version")]
        public string Version { get; set; } = "1.0.0";

        /// <summary>
        /// Description of what the mod provides.
        /// </summary>
        [JsonPropertyName("description")]
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// Load priority. Lower values load first. Default is 0.
        /// </summary>
        [JsonPropertyName("priority")]
        public int Priority { get; set; } = 0;

        /// <summary>
        /// Mapping of content folder types to their relative paths within the mod directory.
        /// </summary>
        [JsonPropertyName("contentFolders")]
        public Dictionary<string, string> ContentFolders { get; set; } =
            new Dictionary<string, string>();

        /// <summary>
        /// List of script file paths relative to the mod root.
        /// </summary>
        [JsonPropertyName("scripts")]
        public List<string> Scripts { get; set; } = new List<string>();

        /// <summary>
        /// List of patch definitions (for modifying other mods' content).
        /// </summary>
        [JsonPropertyName("patches")]
        public List<string> Patches { get; set; } = new List<string>();

        /// <summary>
        /// List of mod IDs that this mod depends on.
        /// </summary>
        [JsonPropertyName("dependencies")]
        public List<string> Dependencies { get; set; } = new List<string>();

        /// <summary>
        /// Tile width in pixels for maps in this mod.
        /// Used when maps don't specify tileWidth explicitly.
        /// Defaults to 16 if not specified.
        /// </summary>
        [JsonPropertyName("tileWidth")]
        public int TileWidth { get; set; } = 16;

        /// <summary>
        /// Tile height in pixels for maps in this mod.
        /// Used when maps don't specify tileHeight explicitly.
        /// Defaults to 16 if not specified.
        /// </summary>
        [JsonPropertyName("tileHeight")]
        public int TileHeight { get; set; } = 16;

        /// <summary>
        /// Full path to the mod directory. Set by the loader.
        /// </summary>
        [JsonIgnore]
        public string ModDirectory { get; set; } = string.Empty;
    }
}
