using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MonoBall.Core.Mods.Utilities
{
    /// <summary>
    /// Utility class for discovering mod sources (directories and archives).
    /// </summary>
    public static class ModDiscovery
    {
        /// <summary>
        /// Discovers all mod sources in the mods directory (both directories and archives).
        /// </summary>
        /// <param name="modsDirectory">The mods directory to scan.</param>
        /// <returns>Enumerable of IModSource instances for discovered mods.</returns>
        /// <exception cref="ArgumentNullException">Thrown when modsDirectory is null.</exception>
        /// <exception cref="DirectoryNotFoundException">Thrown when modsDirectory doesn't exist.</exception>
        public static IEnumerable<IModSource> DiscoverModSources(string modsDirectory)
        {
            if (modsDirectory == null)
            {
                throw new ArgumentNullException(nameof(modsDirectory));
            }

            if (!Directory.Exists(modsDirectory))
            {
                throw new DirectoryNotFoundException(
                    $"Mods directory does not exist: {modsDirectory}"
                );
            }

            // Discover directory-based mods
            var modDirectories = Directory.GetDirectories(modsDirectory);
            foreach (var modDir in modDirectories)
            {
                var modJsonPath = Path.Combine(modDir, "mod.json");
                if (File.Exists(modJsonPath))
                {
                    yield return new DirectoryModSource(modDir);
                }
            }

            // Discover archive-based mods
            var archiveFiles = Directory.GetFiles(
                modsDirectory,
                "*.monoball",
                SearchOption.TopDirectoryOnly
            );
            foreach (var archiveFile in archiveFiles)
            {
                yield return new ArchiveModSource(archiveFile);
            }
        }
    }
}
