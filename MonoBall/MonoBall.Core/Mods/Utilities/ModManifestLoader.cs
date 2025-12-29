using System;
using System.IO;
using System.Text.Json;
using MonoBall.Core.Mods;

namespace MonoBall.Core.Mods.Utilities
{
    /// <summary>
    /// Utility class for loading mod manifests from JSON.
    /// </summary>
    public static class ModManifestLoader
    {
        /// <summary>
        /// Loads a mod manifest from JSON content.
        /// </summary>
        /// <param name="jsonContent">The JSON content to deserialize.</param>
        /// <param name="modSource">The mod source that provides this manifest.</param>
        /// <param name="sourcePath">The source path (directory or archive path) for backward compatibility.</param>
        /// <returns>The deserialized mod manifest.</returns>
        /// <exception cref="ArgumentNullException">Thrown when jsonContent or modSource is null.</exception>
        /// <exception cref="JsonException">Thrown when JSON deserialization fails.</exception>
        public static ModManifest LoadFromJson(
            string jsonContent,
            IModSource modSource,
            string sourcePath
        )
        {
            if (jsonContent == null)
            {
                throw new ArgumentNullException(nameof(jsonContent));
            }

            if (modSource == null)
            {
                throw new ArgumentNullException(nameof(modSource));
            }

            var manifest = JsonSerializer.Deserialize<ModManifest>(
                jsonContent,
                JsonSerializerOptionsFactory.ForManifests
            );

            if (manifest == null)
            {
                throw new InvalidOperationException(
                    "Failed to deserialize mod manifest. JSON content may be invalid."
                );
            }

            // Set ModSource and ModDirectory
            manifest.ModSource = modSource;
            manifest.ModDirectory = sourcePath ?? string.Empty;

            return manifest;
        }
    }
}
