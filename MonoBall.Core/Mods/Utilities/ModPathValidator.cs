using System;
using System.IO;

namespace MonoBall.Core.Mods.Utilities;

/// <summary>
///     Utility class for validating mod file paths and preventing path traversal attacks.
/// </summary>
public static class ModPathValidator
{
    /// <summary>
    ///     Validates that a full file path is within the base directory, preventing path traversal attacks.
    /// </summary>
    /// <param name="fullPath">The full path to validate.</param>
    /// <param name="baseDirectory">The base directory that the path must be within.</param>
    /// <exception cref="UnauthorizedAccessException">Thrown when path traversal is detected.</exception>
    public static void ValidatePath(string fullPath, string baseDirectory)
    {
        if (fullPath == null)
            throw new ArgumentNullException(nameof(fullPath));

        if (baseDirectory == null)
            throw new ArgumentNullException(nameof(baseDirectory));

        if (!fullPath.StartsWith(baseDirectory, StringComparison.Ordinal))
            throw new UnauthorizedAccessException(
                $"Path traversal detected. Attempted to access: {fullPath}"
            );
    }

    /// <summary>
    ///     Checks if a full file path is within the base directory, returning false if path traversal is detected.
    /// </summary>
    /// <param name="fullPath">The full path to check.</param>
    /// <param name="baseDirectory">The base directory that the path must be within.</param>
    /// <returns>True if the path is valid and within the base directory, false otherwise.</returns>
    public static bool IsPathValid(string fullPath, string baseDirectory)
    {
        if (fullPath == null || baseDirectory == null)
            return false;

        return fullPath.StartsWith(baseDirectory, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Resolves and normalizes a relative path to a full path within the base directory.
    /// </summary>
    /// <param name="relativePath">The relative path to resolve.</param>
    /// <param name="baseDirectory">The base directory to resolve against.</param>
    /// <returns>The normalized full path.</returns>
    /// <exception cref="UnauthorizedAccessException">Thrown when path traversal is detected.</exception>
    public static string ResolveAndValidatePath(string relativePath, string baseDirectory)
    {
        if (relativePath == null)
            throw new ArgumentNullException(nameof(relativePath));

        if (baseDirectory == null)
            throw new ArgumentNullException(nameof(baseDirectory));

        var normalizedPath = ModPathNormalizer.Normalize(relativePath);
        var fullPath = Path.Combine(baseDirectory, normalizedPath);
        fullPath = Path.GetFullPath(fullPath);

        ValidatePath(fullPath, baseDirectory);

        return fullPath;
    }
}
