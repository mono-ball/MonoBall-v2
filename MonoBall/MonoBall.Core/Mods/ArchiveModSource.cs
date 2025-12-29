using System;
using System.Buffers;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using K4os.Compression.LZ4;
using MonoBall.Core.Mods.Utilities;

namespace MonoBall.Core.Mods
{
    /// <summary>
    /// Mod source that reads from a compressed .monoball archive (LZ4 format).
    /// </summary>
    public class ArchiveModSource : IModSource, IDisposable
    {
        private readonly string _archivePath;
        private FileStream? _stream;
        private Dictionary<string, FileEntry>? _toc;
        private ModManifest? _cachedManifest;
        private string? _cachedModId;
        private readonly ReaderWriterLockSlim _tocLock = new ReaderWriterLockSlim();
        private readonly object _readLock = new object();
        private bool _disposed = false;

        private const string Magic = "MONOBALL";
        private const ushort CurrentVersion = 1;

        // Cache compiled regex patterns for EnumerateFiles
        private static readonly Dictionary<string, Regex> _patternCache =
            new Dictionary<string, Regex>();
        private static readonly object _patternCacheLock = new object();

        /// <summary>
        /// Initializes a new instance of ArchiveModSource.
        /// </summary>
        /// <param name="archivePath">Path to the .monoball archive file.</param>
        /// <exception cref="ArgumentNullException">Thrown when archivePath is null.</exception>
        /// <exception cref="FileNotFoundException">Thrown when archive file doesn't exist.</exception>
        public ArchiveModSource(string archivePath)
        {
            if (archivePath == null)
            {
                throw new ArgumentNullException(nameof(archivePath));
            }

            if (!File.Exists(archivePath))
            {
                throw new FileNotFoundException($"Archive not found: {archivePath}", archivePath);
            }

            _archivePath = archivePath;
        }

        /// <summary>
        /// Gets the mod ID (from mod.json).
        /// </summary>
        public string ModId
        {
            get
            {
                if (_cachedModId != null)
                {
                    return _cachedModId;
                }

                // Use same lock as GetManifest() to ensure atomicity
                lock (_readLock)
                {
                    if (_cachedModId != null)
                    {
                        return _cachedModId;
                    }

                    // Check if manifest already cached before calling GetManifestInternal
                    if (_cachedManifest == null)
                    {
                        GetManifestInternal(); // This will set _cachedManifest
                    }

                    _cachedModId = _cachedManifest!.Id;
                    return _cachedModId;
                }
            }
        }

        /// <summary>
        /// Gets the path to the mod source (archive file path).
        /// </summary>
        public string SourcePath => _archivePath;

        /// <summary>
        /// Gets whether this is a compressed archive (always true).
        /// </summary>
        public bool IsCompressed => true;

        /// <summary>
        /// Gets the TOC (Table of Contents). Thread-safe with reader-writer lock.
        /// Returns a read-only wrapper to prevent external modification.
        /// </summary>
        internal IReadOnlyDictionary<string, FileEntry> GetTOC()
        {
            _tocLock.EnterReadLock();
            try
            {
                if (_toc != null)
                {
                    return new ReadOnlyDictionary<string, FileEntry>(_toc);
                }
            }
            finally
            {
                _tocLock.ExitReadLock();
            }

            _tocLock.EnterWriteLock();
            try
            {
                // Double-check after acquiring write lock
                if (_toc != null)
                {
                    return new ReadOnlyDictionary<string, FileEntry>(_toc);
                }

                if (_disposed)
                {
                    throw new ObjectDisposedException(nameof(ArchiveModSource));
                }

                _stream = File.OpenRead(_archivePath);
                _toc = LoadTOC();

                return new ReadOnlyDictionary<string, FileEntry>(_toc);
            }
            finally
            {
                _tocLock.ExitWriteLock();
            }
        }

        private Dictionary<string, FileEntry> LoadTOC()
        {
            if (_stream == null)
            {
                throw new InvalidOperationException("Stream not initialized");
            }

            var reader = new BinaryReader(_stream, Encoding.UTF8, leaveOpen: true);
            var archiveSize = _stream.Length;

            // Read header
            var magicBytes = reader.ReadBytes(8);
            var magic = Encoding.ASCII.GetString(magicBytes);

            if (magic != Magic)
            {
                throw new InvalidDataException(
                    $"Invalid archive format. Expected magic '{Magic}', got '{magic}'"
                );
            }

            var version = reader.ReadUInt16();
            if (version != CurrentVersion)
            {
                throw new NotSupportedException(
                    $"Archive version {version} not supported. Current version: {CurrentVersion}"
                );
            }

            var tocOffset = reader.ReadUInt64();

            // Validate TOC offset
            if (tocOffset >= (ulong)archiveSize)
            {
                throw new InvalidDataException(
                    $"Invalid archive: TOC offset {tocOffset} exceeds archive size {archiveSize}"
                );
            }

            // Read TOC
            _stream.Position = (long)tocOffset;
            var fileCount = reader.ReadUInt32();
            var toc = new Dictionary<string, FileEntry>((int)fileCount);

            for (uint i = 0; i < fileCount; i++)
            {
                var pathLen = reader.ReadUInt16();
                var pathBytes = reader.ReadBytes(pathLen);
                var path = Encoding.UTF8.GetString(pathBytes);
                var pathNormalized = ModPathNormalizer.Normalize(path);

                var entry = new FileEntry
                {
                    Path = pathNormalized,
                    UncompressedSize = reader.ReadUInt64(),
                    CompressedSize = reader.ReadUInt64(),
                    DataOffset = reader.ReadUInt64(),
                };

                // Validate TOC entry integrity
                if (entry.DataOffset >= (ulong)archiveSize)
                {
                    throw new InvalidDataException(
                        $"Invalid TOC entry for '{pathNormalized}': "
                            + $"DataOffset {entry.DataOffset} exceeds archive size {archiveSize}"
                    );
                }

                if (entry.DataOffset + entry.CompressedSize > (ulong)archiveSize)
                {
                    throw new InvalidDataException(
                        $"Invalid TOC entry for '{pathNormalized}': "
                            + "File extends beyond archive bounds"
                    );
                }

                // Validate sizes (empty files are allowed)
                if (entry.UncompressedSize == 0 && entry.CompressedSize > 0)
                {
                    throw new InvalidDataException(
                        $"Invalid TOC entry for '{pathNormalized}': "
                            + "Compressed size > 0 but uncompressed size = 0"
                    );
                }

                toc[pathNormalized] = entry;
            }

            return toc;
        }

        /// <summary>
        /// Reads a file from the mod source.
        /// </summary>
        /// <param name="relativePath">Path relative to mod root.</param>
        /// <returns>File contents as byte array.</returns>
        /// <exception cref="FileNotFoundException">Thrown when file doesn't exist.</exception>
        public byte[] ReadFile(string relativePath)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(ArchiveModSource));
            }

            if (string.IsNullOrEmpty(relativePath))
            {
                throw new ArgumentException(
                    "Relative path cannot be null or empty.",
                    nameof(relativePath)
                );
            }

            var toc = GetTOC();
            var normalizedPath = ModPathNormalizer.Normalize(relativePath);

            if (!toc.TryGetValue(normalizedPath, out var entry))
            {
                throw new FileNotFoundException(
                    $"File '{relativePath}' not found in archive '{_archivePath}'"
                );
            }

            // Handle empty files
            if (entry.UncompressedSize == 0)
            {
                return Array.Empty<byte>();
            }

            lock (_readLock)
            {
                return ReadFileInternal(entry);
            }
        }

        /// <summary>
        /// Reads a text file from the mod source.
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
        /// Checks if a file exists in the mod source.
        /// </summary>
        /// <param name="relativePath">Path relative to mod root.</param>
        /// <returns>True if file exists.</returns>
        /// <exception cref="ObjectDisposedException">Thrown when ArchiveModSource is disposed.</exception>
        public bool FileExists(string relativePath)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(ArchiveModSource));
            }

            if (string.IsNullOrEmpty(relativePath))
            {
                return false;
            }

            var toc = GetTOC();
            var normalizedPath = ModPathNormalizer.Normalize(relativePath);
            return toc.ContainsKey(normalizedPath);
        }

        /// <summary>
        /// Enumerates all files in the mod source matching a pattern.
        /// </summary>
        /// <param name="searchPattern">File pattern (e.g., "*.json").</param>
        /// <param name="searchOption">Search option (TopDirectoryOnly or AllDirectories).</param>
        /// <returns>Enumerable of relative file paths.</returns>
        public IEnumerable<string> EnumerateFiles(string searchPattern, SearchOption searchOption)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(ArchiveModSource));
            }

            var toc = GetTOC();

            // Get or create compiled regex pattern
            Regex pattern;
            lock (_patternCacheLock)
            {
                if (!_patternCache.TryGetValue(searchPattern, out pattern))
                {
                    pattern = new Regex(
                        "^" + Regex.Escape(searchPattern).Replace("\\*", ".*") + "$",
                        RegexOptions.IgnoreCase | RegexOptions.Compiled
                    );
                    _patternCache[searchPattern] = pattern;
                }
            }

            foreach (var path in toc.Keys)
            {
                var fileName = Path.GetFileName(path);
                if (pattern.IsMatch(fileName))
                {
                    // Handle SearchOption
                    if (searchOption == SearchOption.TopDirectoryOnly)
                    {
                        // Only include files in root directory (no path separators)
                        if (path.Contains('/', StringComparison.Ordinal))
                        {
                            continue;
                        }
                    }

                    yield return path;
                }
            }
        }

        /// <summary>
        /// Gets the mod.json manifest.
        /// </summary>
        /// <returns>ModManifest deserialized from mod.json.</returns>
        /// <exception cref="InvalidOperationException">Thrown when mod.json is missing or invalid.</exception>
        public ModManifest GetManifest()
        {
            if (_cachedManifest != null)
            {
                return _cachedManifest;
            }

            lock (_readLock)
            {
                // Double-check after acquiring lock
                if (_cachedManifest != null)
                {
                    return _cachedManifest;
                }

                return GetManifestInternal();
            }
        }

        /// <summary>
        /// Internal method to load manifest without acquiring lock (must be called within lock).
        /// </summary>
        private ModManifest GetManifestInternal()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(ArchiveModSource));
            }

            var toc = GetTOC();
            var normalizedPath = ModPathNormalizer.Normalize("mod.json");

            if (!toc.TryGetValue(normalizedPath, out var entry))
            {
                throw new InvalidOperationException(
                    $"mod.json not found in archive '{_archivePath}'"
                );
            }

            // Read file directly without calling ReadTextFile (which would deadlock)
            var jsonBytes = ReadFileInternal(entry);
            var jsonContent = Encoding.UTF8.GetString(jsonBytes);
            _cachedManifest = ModManifestLoader.LoadFromJson(jsonContent, this, _archivePath);

            return _cachedManifest;
        }

        /// <summary>
        /// Internal method to read file without acquiring lock (must be called within lock).
        /// </summary>
        private byte[] ReadFileInternal(FileEntry entry)
        {
            if (_stream == null)
            {
                throw new InvalidOperationException("Stream not initialized");
            }

            // Handle empty files
            if (entry.UncompressedSize == 0)
            {
                return Array.Empty<byte>();
            }

            // Read compressed data using ArrayPool
            var compressedPooled = ArrayPool<byte>.Shared.Rent((int)entry.CompressedSize);
            var decompressedPooled = ArrayPool<byte>.Shared.Rent((int)entry.UncompressedSize);

            try
            {
                _stream.Position = (long)entry.DataOffset;
                var bytesRead = _stream.Read(compressedPooled, 0, (int)entry.CompressedSize);

                if (bytesRead != (int)entry.CompressedSize)
                {
                    throw new IOException(
                        $"Failed to read compressed data. "
                            + $"Expected {entry.CompressedSize} bytes, read {bytesRead} bytes."
                    );
                }

                // Decompress with LZ4
                var decoded = LZ4Codec.Decode(
                    compressedPooled.AsSpan(0, (int)entry.CompressedSize),
                    decompressedPooled.AsSpan(0, (int)entry.UncompressedSize)
                );

                if (decoded != (int)entry.UncompressedSize)
                {
                    throw new InvalidDataException(
                        $"Decompression failed. "
                            + $"Expected {entry.UncompressedSize} bytes, got {decoded} bytes."
                    );
                }

                // Copy to result array
                var result = new byte[entry.UncompressedSize];
                Array.Copy(decompressedPooled, 0, result, 0, (int)entry.UncompressedSize);
                return result;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(compressedPooled);
                ArrayPool<byte>.Shared.Return(decompressedPooled);
            }
        }

        /// <summary>
        /// Disposes the ArchiveModSource.
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _tocLock.EnterWriteLock();
            try
            {
                if (_disposed)
                {
                    return;
                }

                _stream?.Dispose();
                _stream = null;
                _toc = null;
                _cachedManifest = null;
                _cachedModId = null;
                _disposed = true;
            }
            finally
            {
                _tocLock.ExitWriteLock();
            }

            _tocLock.Dispose();
        }

        /// <summary>
        /// Internal file entry structure for TOC.
        /// </summary>
        internal class FileEntry
        {
            public string Path { get; set; } = string.Empty;
            public ulong UncompressedSize { get; set; }
            public ulong CompressedSize { get; set; }
            public ulong DataOffset { get; set; }
        }
    }
}
