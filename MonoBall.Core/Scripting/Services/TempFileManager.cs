using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Serilog;

namespace MonoBall.Core.Scripting.Services;

/// <summary>
///     Thread-safe manager for temporary files created during script compilation.
/// </summary>
public class TempFileManager : ITempFileManager
{
    private readonly ConcurrentDictionary<string, ConcurrentBag<string>> _tempFiles = new();
    private readonly ILogger? _logger;
    private bool _disposed;

    /// <summary>
    ///     Initializes a new instance of the TempFileManager class.
    /// </summary>
    /// <param name="logger">Optional logger for debugging.</param>
    public TempFileManager(ILogger? logger = null)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public void TrackTempFile(string modId, string tempFilePath)
    {
        if (string.IsNullOrWhiteSpace(modId))
            throw new ArgumentException("Mod ID cannot be null or empty.", nameof(modId));
        if (string.IsNullOrWhiteSpace(tempFilePath))
            throw new ArgumentException(
                "Temp file path cannot be null or empty.",
                nameof(tempFilePath)
            );

        var bag = _tempFiles.GetOrAdd(modId, _ => new ConcurrentBag<string>());
        bag.Add(tempFilePath);
        _logger?.Debug("Tracking temp file for mod {ModId}: {TempFilePath}", modId, tempFilePath);
    }

    /// <inheritdoc />
    public void CleanupModTempFiles(string modId)
    {
        if (string.IsNullOrWhiteSpace(modId))
            throw new ArgumentException("Mod ID cannot be null or empty.", nameof(modId));

        if (_tempFiles.TryRemove(modId, out var files))
        {
            foreach (var file in files)
            {
                try
                {
                    if (File.Exists(file))
                    {
                        File.Delete(file);
                        _logger?.Debug("Deleted temp file: {TempFilePath}", file);
                    }
                }
                catch (Exception ex)
                {
                    _logger?.Warning(
                        ex,
                        "Failed to delete temp file during cleanup: {TempFilePath}",
                        file
                    );
                    // Don't re-throw - cleanup failures shouldn't crash the game
                }
            }
        }
    }

    /// <inheritdoc />
    public void CleanupAllTempFiles()
    {
        // Get all mod IDs atomically
        var allMods = new List<string>();
        foreach (var kvp in _tempFiles)
            allMods.Add(kvp.Key);

        // Cleanup each mod
        foreach (var modId in allMods)
            CleanupModTempFiles(modId);

        // Final pass: cleanup any remaining files added during iteration
        while (!_tempFiles.IsEmpty)
        {
            var remaining = _tempFiles.Keys.ToList();
            if (remaining.Count == 0)
                break;

            foreach (var modId in remaining)
                CleanupModTempFiles(modId);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (!_disposed)
        {
            CleanupAllTempFiles();
            _tempFiles.Clear();
            _disposed = true;
        }
    }
}
