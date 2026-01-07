using System;

namespace MonoBall.Core.Scripting.Services;

/// <summary>
///     Tracks and cleans up temporary files created during script compilation.
/// </summary>
public interface ITempFileManager : IDisposable
{
    /// <summary>
    ///     Tracks a temp file for cleanup.
    /// </summary>
    /// <param name="modId">The mod ID that owns the temp file.</param>
    /// <param name="tempFilePath">The path to the temp file.</param>
    /// <exception cref="ArgumentException">Thrown when modId or tempFilePath is null or empty.</exception>
    void TrackTempFile(string modId, string tempFilePath);

    /// <summary>
    ///     Cleans up all temp files for a mod.
    /// </summary>
    /// <param name="modId">The mod ID to clean up temp files for.</param>
    void CleanupModTempFiles(string modId);

    /// <summary>
    ///     Cleans up all tracked temp files.
    /// </summary>
    void CleanupAllTempFiles();
}
