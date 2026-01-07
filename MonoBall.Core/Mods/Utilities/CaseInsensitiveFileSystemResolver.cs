using System;
using System.IO;

namespace MonoBall.Core.Mods.Utilities;

/// <summary>
///     Utility class for resolving file paths with case-insensitive matching.
///     Handles cross-platform compatibility where Windows is case-insensitive but macOS/Linux are case-sensitive.
/// </summary>
public static class CaseInsensitiveFileSystemResolver
{
    /// <summary>
    ///     Finds a file with case-insensitive matching, traversing directory components case-insensitively.
    /// </summary>
    /// <param name="baseDirectory">The base directory to start searching from.</param>
    /// <param name="relativePath">The relative path to find (with potentially incorrect casing).</param>
    /// <returns>The actual file path with correct casing, or null if not found.</returns>
    /// <exception cref="ArgumentNullException">Thrown when baseDirectory or relativePath is null.</exception>
    public static string? FindFile(string baseDirectory, string relativePath)
    {
        if (baseDirectory == null)
            throw new ArgumentNullException(nameof(baseDirectory));
        if (relativePath == null)
            throw new ArgumentNullException(nameof(relativePath));

        var normalizedPath = ModPathNormalizer.Normalize(relativePath);
        var fullPath = Path.Combine(baseDirectory, normalizedPath);
        fullPath = Path.GetFullPath(fullPath);

        // Try exact match first (fast path)
        if (File.Exists(fullPath))
            return fullPath;

        // Split into path components
        var pathParts = normalizedPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (pathParts.Length == 0)
            return null;

        // Start from base directory and traverse path components case-insensitively
        var currentDir = baseDirectory;
        for (var i = 0; i < pathParts.Length - 1; i++)
        {
            var part = pathParts[i];
            var foundDir = FindDirectory(currentDir, part);
            if (foundDir == null)
                return null; // Directory component not found
            currentDir = foundDir;
        }

        // Find the file in the final directory
        var fileName = pathParts[pathParts.Length - 1];
        return FindFileInDirectory(currentDir, fileName);
    }

    /// <summary>
    ///     Finds a directory with case-insensitive matching.
    /// </summary>
    /// <param name="parentDirectory">The parent directory to search in.</param>
    /// <param name="dirName">The directory name to find (with potentially incorrect casing).</param>
    /// <returns>The actual directory path with correct casing, or null if not found.</returns>
    /// <exception cref="ArgumentNullException">Thrown when parentDirectory or dirName is null.</exception>
    public static string? FindDirectory(string parentDirectory, string dirName)
    {
        if (parentDirectory == null)
            throw new ArgumentNullException(nameof(parentDirectory));
        if (dirName == null)
            throw new ArgumentNullException(nameof(dirName));

        if (!Directory.Exists(parentDirectory))
            return null;

        try
        {
            var subdirs = Directory.GetDirectories(parentDirectory, "*", SearchOption.TopDirectoryOnly);
            foreach (var subdir in subdirs)
            {
                var actualDirName = Path.GetFileName(subdir);
                if (string.Equals(actualDirName, dirName, StringComparison.OrdinalIgnoreCase))
                    return subdir;
            }
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }

        return null;
    }

    /// <summary>
    ///     Finds a file with case-insensitive matching in a directory.
    /// </summary>
    /// <param name="directory">The directory to search in.</param>
    /// <param name="fileName">The file name to find (with potentially incorrect casing).</param>
    /// <returns>The actual file path with correct casing, or null if not found.</returns>
    /// <exception cref="ArgumentNullException">Thrown when directory or fileName is null.</exception>
    public static string? FindFileInDirectory(string directory, string fileName)
    {
        if (directory == null)
            throw new ArgumentNullException(nameof(directory));
        if (fileName == null)
            throw new ArgumentNullException(nameof(fileName));

        if (!Directory.Exists(directory))
            return null;

        try
        {
            var files = Directory.GetFiles(directory, "*", SearchOption.TopDirectoryOnly);
            foreach (var file in files)
            {
                var actualFileName = Path.GetFileName(file);
                if (string.Equals(actualFileName, fileName, StringComparison.OrdinalIgnoreCase))
                    return file;
            }
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }

        return null;
    }
}
