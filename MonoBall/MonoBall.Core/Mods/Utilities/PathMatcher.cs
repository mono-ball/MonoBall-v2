using System;

namespace MonoBall.Core.Mods.Utilities;

/// <summary>
/// Utility for matching file paths against patterns (DRY - used in multiple places).
/// </summary>
public static class PathMatcher
{
    /// <summary>
    /// Checks if a file path matches a pattern.
    /// Uses case-insensitive comparison by default for cross-platform compatibility.
    /// </summary>
    /// <param name="filePath">The file path to check.</param>
    /// <param name="pattern">The pattern to match against.</param>
    /// <param name="comparison">String comparison type (defaults to OrdinalIgnoreCase for cross-platform compatibility).</param>
    /// <returns>True if the path matches the pattern.</returns>
    public static bool MatchesPath(
        string filePath,
        string pattern,
        StringComparison comparison = StringComparison.OrdinalIgnoreCase
    )
    {
        var normalized = ModPathNormalizer.Normalize(filePath);
        var normalizedPattern = ModPathNormalizer.Normalize(pattern);
        // Use case-insensitive comparison to handle Windows case-insensitive file systems
        // while preserving original casing for case-sensitive file systems (Linux)
        return normalized.StartsWith(normalizedPattern + "/", comparison)
            || normalized == normalizedPattern;
    }
}
