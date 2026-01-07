using System;
using System.IO;

namespace MonoBall.Core.Mods.Utilities;

/// <summary>
///     Utility class for resolving the Mods directory path in various scenarios.
/// </summary>
public static class ModsPathResolver
{
    /// <summary>
    ///     Finds the Mods directory by checking multiple possible locations.
    /// </summary>
    /// <param name="baseDirectory">The base directory to start searching from. Defaults to executable directory.</param>
    /// <returns>The path to the Mods directory, or null if not found.</returns>
    public static string? FindModsDirectory(string? baseDirectory = null)
    {
        baseDirectory ??= AppDomain.CurrentDomain.BaseDirectory;

        // Try 1: Relative to base directory (for deployed builds)
        var modsPath = Path.Combine(baseDirectory, "Mods");
        if (Directory.Exists(modsPath))
            return modsPath;

        // Try 2: Go up from base directory to find project root (for development)
        // Executable is typically at: bin/Debug/net10.0 or bin/Release/net10.0
        var currentDir = new DirectoryInfo(baseDirectory);
        for (var i = 0; i < 4; i++) // Go up max 4 levels
        {
            currentDir = currentDir.Parent;
            if (currentDir == null)
                break;

            modsPath = Path.Combine(currentDir.FullName, "Mods");
            if (Directory.Exists(modsPath))
                return modsPath;
        }

        // Try 3: Check relative path from base directory
        modsPath = Path.Combine(baseDirectory, "..", "..", "..", "..", "Mods");
        modsPath = Path.GetFullPath(modsPath);
        if (Directory.Exists(modsPath))
            return modsPath;

        return null;
    }

    /// <summary>
    ///     Resolves a mods directory path, making it absolute if it's relative.
    /// </summary>
    /// <param name="modsDirectory">The mods directory path (can be relative or absolute).</param>
    /// <param name="baseDirectory">The base directory for resolving relative paths. Defaults to executable directory.</param>
    /// <returns>The absolute path to the mods directory.</returns>
    public static string ResolveModsDirectory(string modsDirectory, string? baseDirectory = null)
    {
        baseDirectory ??= AppDomain.CurrentDomain.BaseDirectory;

        if (Path.IsPathRooted(modsDirectory))
            return modsDirectory;

        return Path.Combine(baseDirectory, modsDirectory);
    }
}
