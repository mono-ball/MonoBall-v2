using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using MonoBall.Core.Mods.Utilities;

namespace MonoBall.Core.Mods;

/// <summary>
///     Implementation of IModSource for directory-based mods.
/// </summary>
public class DirectoryModSource : IModSource, IDisposable
{
    private readonly object _manifestLock = new();
    private readonly string _normalizedDirectoryPath;
    private ModManifest? _cachedManifest;
    private string? _cachedModId;
    private bool _disposed;

    /// <summary>
    ///     Initializes a new instance of the DirectoryModSource.
    /// </summary>
    /// <param name="directoryPath">The full path to the mod directory.</param>
    /// <exception cref="ArgumentNullException">Thrown when directoryPath is null.</exception>
    /// <exception cref="DirectoryNotFoundException">Thrown when directory doesn't exist.</exception>
    public DirectoryModSource(string directoryPath)
    {
        if (directoryPath == null)
            throw new ArgumentNullException(nameof(directoryPath));

        if (!Directory.Exists(directoryPath))
            throw new DirectoryNotFoundException($"Mod directory does not exist: {directoryPath}");

        SourcePath = directoryPath;
        _normalizedDirectoryPath = Path.GetFullPath(directoryPath);
    }

    /// <summary>
    ///     Gets the mod ID (from mod.json).
    /// </summary>
    public string ModId
    {
        get
        {
            if (_cachedModId != null)
                return _cachedModId;

            // Double-checked locking for thread safety
            lock (_manifestLock)
            {
                if (_cachedModId != null)
                    return _cachedModId;

                // Check if manifest already cached before calling GetManifest
                if (_cachedManifest == null)
                    GetManifest(); // This will set _cachedManifest

                _cachedModId = _cachedManifest!.Id;
                return _cachedModId;
            }
        }
    }

    /// <summary>
    ///     Gets the path to the mod source (directory path).
    /// </summary>
    public string SourcePath { get; }

    /// <summary>
    ///     Gets whether this is a compressed archive (always false for directories).
    /// </summary>
    public bool IsCompressed => false;

    /// <summary>
    ///     Reads a file from the mod source.
    /// </summary>
    /// <param name="relativePath">Path relative to mod root.</param>
    /// <returns>File contents as byte array.</returns>
    /// <exception cref="FileNotFoundException">Thrown when file doesn't exist.</exception>
    public byte[] ReadFile(string relativePath)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(DirectoryModSource));

        var fullPath = ModPathValidator.ResolveAndValidatePath(
            relativePath,
            _normalizedDirectoryPath
        );

        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"File not found in mod: {relativePath}", fullPath);

        return File.ReadAllBytes(fullPath);
    }

    /// <summary>
    ///     Reads a text file from the mod source.
    /// </summary>
    /// <param name="relativePath">Path relative to mod root.</param>
    /// <returns>File contents as string (UTF-8).</returns>
    /// <exception cref="FileNotFoundException">Thrown when file doesn't exist.</exception>
    public string ReadTextFile(string relativePath)
    {
        var bytes = ReadFile(relativePath);
        return Encoding.UTF8.GetString(bytes);
    }

    /// <summary>
    ///     Checks if a file exists in the mod source.
    /// </summary>
    /// <param name="relativePath">Path relative to mod root.</param>
    /// <returns>True if file exists.</returns>
    public bool FileExists(string relativePath)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(DirectoryModSource));

        try
        {
            var normalizedPath = ModPathNormalizer.Normalize(relativePath);
            var fullPath = Path.Combine(SourcePath, normalizedPath);
            fullPath = Path.GetFullPath(fullPath);

            // Path traversal protection - return false instead of throwing
            if (!ModPathValidator.IsPathValid(fullPath, _normalizedDirectoryPath))
                return false;

            return File.Exists(fullPath);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    ///     Enumerates all files in the mod source matching a pattern.
    ///     Ignores hidden files and directories (starting with '.').
    /// </summary>
    /// <param name="searchPattern">File pattern (e.g., "*.json").</param>
    /// <param name="searchOption">Search option (TopDirectoryOnly or AllDirectories).</param>
    /// <returns>Enumerable of relative file paths.</returns>
    public IEnumerable<string> EnumerateFiles(string searchPattern, SearchOption searchOption)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(DirectoryModSource));

        if (!Directory.Exists(SourcePath))
            yield break;

        var files = Directory.GetFiles(SourcePath, searchPattern, searchOption);
        foreach (var file in files)
        {
            var relativePath = Path.GetRelativePath(SourcePath, file);
            var normalized = ModPathNormalizer.Normalize(relativePath);

            // Skip hidden files and directories (starting with '.')
            if (IsHiddenPath(normalized))
                continue;

            // For TopDirectoryOnly, filter out paths with directory separators
            if (searchOption == SearchOption.TopDirectoryOnly)
                if (normalized.Contains("/", StringComparison.Ordinal))
                    continue;

            yield return normalized;
        }
    }

    /// <summary>
    ///     Checks if a path contains any hidden files or directories (starting with '.').
    /// </summary>
    /// <param name="path">The normalized path to check.</param>
    /// <returns>True if the path contains hidden files or directories.</returns>
    private static bool IsHiddenPath(string path)
    {
        // Check each path component for hidden files/directories
        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            if (part.Length > 0 && part[0] == '.')
                return true;
        }
        return false;
    }

    /// <summary>
    ///     Gets the mod.json manifest.
    /// </summary>
    /// <returns>ModManifest deserialized from mod.json.</returns>
    /// <exception cref="InvalidOperationException">Thrown when mod.json is missing or invalid.</exception>
    public ModManifest GetManifest()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(DirectoryModSource));

        // Double-checked locking for thread safety
        if (_cachedManifest != null)
            return _cachedManifest;

        lock (_manifestLock)
        {
            if (_cachedManifest != null)
                return _cachedManifest;

            if (!FileExists("mod.json"))
                throw new InvalidOperationException(
                    $"mod.json not found in mod directory: {SourcePath}"
                );

            var jsonContent = ReadTextFile("mod.json");
            _cachedManifest = ModManifestLoader.LoadFromJson(jsonContent, this);

            return _cachedManifest;
        }
    }

    /// <summary>
    ///     Disposes the DirectoryModSource.
    /// </summary>
    public void Dispose()
    {
        if (!_disposed)
            _disposed = true;
    }
}
