using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace MonoBall.Core.Mods;

/// <summary>
///     Represents a mod manifest file (mod.json) that defines mod metadata and content structure.
/// </summary>
public class ModManifest
{
    /// <summary>
    ///     Unique identifier for the mod (e.g., "base:monoball-core").
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    ///     Display name of the mod.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    ///     Author of the mod.
    /// </summary>
    [JsonPropertyName("author")]
    public string Author { get; set; } = string.Empty;

    /// <summary>
    ///     Version of the mod (semantic versioning recommended).
    /// </summary>
    [JsonPropertyName("version")]
    public string Version { get; set; } = "1.0.0";

    /// <summary>
    ///     Description of what the mod provides.
    /// </summary>
    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    /// <summary>
    ///     Load priority. Lower values load first. Default is 0.
    /// </summary>
    [JsonPropertyName("priority")]
    public int Priority { get; set; } = 0;

    /// <summary>
    ///     Mapping of content folder types to their relative paths within the mod directory.
    /// </summary>
    [JsonPropertyName("contentFolders")]
    public Dictionary<string, string> ContentFolders { get; set; } = new();

    /// <summary>
    ///     List of plugin script file paths relative to the mod root.
    ///     Plugin scripts are standalone scripts not attached to entities.
    /// </summary>
    [JsonPropertyName("plugins")]
    public List<string> Plugins { get; set; } = new();

    /// <summary>
    ///     List of compiled assembly (DLL) file paths relative to the mod root.
    ///     These assemblies are made available to other mods' scripts that depend on this mod.
    ///     Assemblies should contain public types that other mods can reference in their scripts.
    /// </summary>
    [JsonPropertyName("assemblies")]
    public List<string> Assemblies { get; set; } = new();

    /// <summary>
    ///     List of patch definitions (for modifying other mods' content).
    /// </summary>
    [JsonPropertyName("patches")]
    public List<string> Patches { get; set; } = new();

    /// <summary>
    ///     List of mod IDs that this mod depends on.
    ///     When compiling scripts, assemblies from dependency mods are automatically included as references.
    /// </summary>
    [JsonPropertyName("dependencies")]
    public List<string> Dependencies { get; set; } = new();

    /// <summary>
    ///     Tile width in pixels for maps in this mod.
    ///     Used when maps don't specify tileWidth explicitly.
    ///     Defaults to 16 if not specified.
    /// </summary>
    [JsonPropertyName("tileWidth")]
    public int TileWidth { get; set; } = 16;

    /// <summary>
    ///     Tile height in pixels for maps in this mod.
    ///     Used when maps don't specify tileHeight explicitly.
    ///     Defaults to 16 if not specified.
    /// </summary>
    [JsonPropertyName("tileHeight")]
    public int TileHeight { get; set; } = 16;

    /// <summary>
    ///     Full path to the mod directory. Set by the loader.
    ///     Kept for backward compatibility.
    /// </summary>
    [JsonIgnore]
    public string ModDirectory { get; set; } = string.Empty;

    /// <summary>
    ///     The mod source (directory or archive) that provides this mod's content.
    /// </summary>
    [JsonIgnore]
    public IModSource? ModSource { get; set; }
}
