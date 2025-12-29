using System;
using System.Collections.Generic;
using System.IO;

namespace MonoBall.Core.Mods
{
    /// <summary>
    /// Represents a source for mod content (directory or archive).
    /// </summary>
    public interface IModSource : IDisposable
    {
        /// <summary>
        /// Gets the mod ID (from mod.json).
        /// </summary>
        string ModId { get; }

        /// <summary>
        /// Gets the path to the mod source (directory path or archive file path).
        /// </summary>
        string SourcePath { get; }

        /// <summary>
        /// Gets whether this is a compressed archive.
        /// </summary>
        bool IsCompressed { get; }

        /// <summary>
        /// Reads a file from the mod source.
        /// </summary>
        /// <param name="relativePath">Path relative to mod root (e.g., "Graphics/sprite.png").</param>
        /// <returns>File contents as byte array.</returns>
        /// <exception cref="FileNotFoundException">Thrown when file doesn't exist.</exception>
        byte[] ReadFile(string relativePath);

        /// <summary>
        /// Reads a text file from the mod source.
        /// </summary>
        /// <param name="relativePath">Path relative to mod root.</param>
        /// <returns>File contents as string (UTF-8).</returns>
        /// <exception cref="FileNotFoundException">Thrown when file doesn't exist.</exception>
        string ReadTextFile(string relativePath);

        /// <summary>
        /// Checks if a file exists in the mod source.
        /// </summary>
        /// <param name="relativePath">Path relative to mod root.</param>
        /// <returns>True if file exists.</returns>
        bool FileExists(string relativePath);

        /// <summary>
        /// Enumerates all files in the mod source matching a pattern.
        /// </summary>
        /// <param name="searchPattern">File pattern (e.g., "*.json").</param>
        /// <param name="searchOption">Search option (TopDirectoryOnly or AllDirectories).</param>
        /// <returns>Enumerable of relative file paths.</returns>
        IEnumerable<string> EnumerateFiles(string searchPattern, SearchOption searchOption);

        /// <summary>
        /// Gets the mod.json manifest.
        /// </summary>
        /// <returns>ModManifest deserialized from mod.json.</returns>
        /// <exception cref="InvalidOperationException">Thrown when mod.json is missing or invalid.</exception>
        ModManifest GetManifest();
    }
}
