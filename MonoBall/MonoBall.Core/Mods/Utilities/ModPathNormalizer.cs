using System;

namespace MonoBall.Core.Mods.Utilities
{
    /// <summary>
    /// Utility class for normalizing mod file paths to a consistent format.
    /// </summary>
    public static class ModPathNormalizer
    {
        /// <summary>
        /// Normalizes a mod file path to use forward slashes and remove leading slashes.
        /// </summary>
        /// <param name="path">The path to normalize.</param>
        /// <returns>The normalized path.</returns>
        /// <exception cref="ArgumentNullException">Thrown when path is null.</exception>
        public static string Normalize(string path)
        {
            if (path == null)
            {
                throw new ArgumentNullException(nameof(path));
            }

            // Replace backslashes with forward slashes
            var normalized = path.Replace('\\', '/');

            // Remove leading slash
            if (normalized.StartsWith("/", StringComparison.Ordinal))
            {
                normalized = normalized.Substring(1);
            }

            return normalized;
        }
    }
}
