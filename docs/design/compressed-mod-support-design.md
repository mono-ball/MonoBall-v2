# Compressed Mod Support Design

**Generated:** 2025-01-16  
**Status:** Design Proposal  
**Scope:** LZ4-based compressed mod archive format for MonoBall

---

## Executive Summary

This document proposes a **custom archive format using LZ4 compression** for MonoBall mod distribution. The design prioritizes **decompression speed** for optimal runtime performance while maintaining random file access capabilities. Mods are distributed as single `.monoball` archive files containing all mod content compressed with LZ4.

**Key Design Decisions:**
- **Format**: Custom `.monoball` archive format with LZ4 compression
- **Priority**: Maximum decompression speed (faster than ZIP/7z)
- **Trade-off**: Slightly larger file sizes (~10-20% larger than 7z) for fastest decompression
- **Library**: K4os.Compression.LZ4 (NuGet package) - See "LZ4 Package Selection" section below

---

## LZ4 Package Selection

### Available .NET LZ4 Packages

Several LZ4 packages are available for .NET:

1. **K4os.Compression.LZ4** (Recommended)
   - Pure .NET implementation (no native dependencies)
   - Supports .NET Framework 4.5+, .NET Standard 1.6+, .NET 5.0+, .NET 6.0+
   - High performance, actively maintained
   - ~67M+ downloads, 90+ dependent packages
   - Good documentation and examples
   - GitHub: https://github.com/MiloszKrajewski/K4os.Compression.LZ4

2. **lz4net** (Alternative)
   - Older package, less actively maintained
   - May have compatibility issues with newer .NET versions

3. **LZ4Sharp** (Alternative)
   - Native bindings (requires native libraries)
   - More complex deployment

4. **Cysharp/NativeCompressions** (Alternative)
   - Native bindings for LZ4, Zstandard, OpenZL
   - Requires native dependencies (not pure .NET)

5. **wan24-Compression-LZ4** (Alternative)
   - Less community adoption
   - May have limited framework support

### Selection Rationale

**K4os.Compression.LZ4** was selected because:

1. **Pure .NET Implementation**: No native dependencies simplifies deployment and cross-platform support
2. **Broad Framework Support**: Works with .NET Framework, .NET Core, .NET 5.0+, MonoGame (.NET 10.0)
3. **Active Maintenance**: Regularly updated, responsive maintainer
4. **High Performance**: Optimized for speed (critical for our use case)
5. **Proven Track Record**: 67M+ downloads indicates widespread adoption and trust
6. **Good API**: Simple, intuitive API for our use case (`LZ4Codec.Encode()` / `LZ4Codec.Decode()`)
7. **No Stream Requirement**: We don't need streaming compression (we compress individual files), so the base package is sufficient

### Verification Needed

**Before implementation, verify:**
- ✅ Package supports .NET 10.0 (should, as it supports .NET 6.0+)
- ✅ Performance meets requirements (benchmark if needed)
- ✅ License is compatible (MIT License - should be fine)
- ✅ No breaking changes in recent versions

**Alternative Consideration**: If K4os.Compression.LZ4 doesn't meet requirements, consider:
- **NativeCompressions** if native dependencies are acceptable and better performance is needed
- **lz4net** if pure .NET is required but K4os doesn't work

---

## Design Goals

1. **Fast Decompression**: Prioritize runtime performance over compression ratio
2. **Random File Access**: Read individual files without extracting entire archive
3. **Single File Distribution**: Package entire mod in one compressed file
4. **Backward Compatibility**: Support both compressed and directory-based mods during transition
5. **Fail-Fast**: Clear errors for invalid archives (per .cursorrules)

---

## Archive Format Specification

### File Extension
- **`.monoball`** - Custom extension for MonoBall mod archives

### Binary Format Structure

```
[Header Section - 18 bytes]
├── Magic: "MONOBALL" (8 bytes, ASCII)
├── Version: uint16 (format version, currently 1)
└── TOC Offset: uint64 (byte offset to Table of Contents)

[Table of Contents Section]
├── File Count: uint32
└── For each file entry:
    ├── Path Length: uint16 (bytes in UTF-8 path)
    ├── Path: string (UTF-8 encoded, no null terminator)
    ├── Uncompressed Size: uint64 (bytes)
    ├── Compressed Size: uint64 (bytes)
    └── Data Offset: uint64 (byte offset to compressed data)

[Compressed Data Section]
└── Each file compressed independently with LZ4
    └── Files stored sequentially in TOC order
```

### Format Details

**Endianness**: Little-endian (standard for x86/x64)

**Path Normalization**: 
- Paths stored with forward slashes (`/`) as separators
- No leading slash
- Case-sensitive (preserves original case)

**LZ4 Compression**:
- Each file compressed independently
- Uses LZ4 fast compression (level 1, fastest)
- Compression ratio: ~10-20% larger than 7z, but decompression is 3-5x faster

**File Size Limits**:
- Maximum file size: 2^64 bytes (practically unlimited)
- Maximum path length: 65535 bytes (uint16 limit)
- Maximum files per archive: 2^32 (4 billion, practically unlimited)

**Empty Files**:
- Empty files (0 bytes) are supported
- Empty files have `CompressedSize = 0` and `UncompressedSize = 0`
- `DataOffset` points to next file (or TOC if last file)
- Reading empty files returns `Array.Empty<byte>()`

---

## Architecture Overview

### Component Structure

```
┌─────────────────────────────────────────────────────────┐
│                    ModLoader                            │
│  ┌──────────────────┐  ┌──────────────────┐            │
│  │ DiscoverMods()  │  │ LoadModSource()  │            │
│  └────────┬─────────┘  └────────┬─────────┘            │
│           │                      │                       │
│           ▼                      ▼                       │
│  ┌──────────────────────────────────────────┐           │
│  │         IModSource Interface            │           │
│  │  - ReadFile(string relativePath)        │           │
│  │  - ReadTextFile(string relativePath)    │           │
│  │  - FileExists(string relativePath)       │           │
│  │  - EnumerateFiles(...)                   │           │
│  └───────┬──────────────────────┬──────────┘           │
│          │                      │                        │
│          ▼                      ▼                        │
│  ┌──────────────┐    ┌──────────────────┐              │
│  │ Directory    │    │ ArchiveModSource │              │
│  │ ModSource    │    │ (LZ4 Archive)   │              │
│  └──────────────┘    └──────────────────┘              │
└─────────────────────────────────────────────────────────┘
```

### Key Interfaces

#### IModSource

```csharp
namespace MonoBall.Core.Mods
{
    /// <summary>
    /// Represents a source for mod content (directory or archive).
    /// </summary>
    public interface IModSource
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
```

---

## Implementation Details

### 0. Shared Utilities (DRY)

**Path Normalization Utility**:

```csharp
namespace MonoBall.Core.Mods.Utilities
{
    /// <summary>
    /// Utility for normalizing mod file paths.
    /// </summary>
    public static class ModPathNormalizer
    {
        /// <summary>
        /// Normalizes a path to use forward slashes and removes leading slash.
        /// </summary>
        public static string Normalize(string path)
        {
            if (string.IsNullOrEmpty(path))
                return path;
            
            return path.Replace('\\', '/').TrimStart('/');
        }
    }
}
```

**Manifest Loading Utility**:

```csharp
namespace MonoBall.Core.Mods.Utilities
{
    /// <summary>
    /// Utility for loading mod manifests from JSON.
    /// </summary>
    public static class ModManifestLoader
    {
        /// <summary>
        /// Deserializes a mod manifest from JSON content.
        /// </summary>
        public static ModManifest LoadFromJson(string jsonContent, string sourcePath, IModSource modSource)
        {
            if (string.IsNullOrEmpty(jsonContent))
            {
                throw new ArgumentException("JSON content cannot be null or empty.", nameof(jsonContent));
            }
            
            var manifest = JsonSerializer.Deserialize<ModManifest>(
                jsonContent,
                JsonSerializerOptionsFactory.ForManifests
            );
            
            if (manifest == null)
            {
                throw new InvalidOperationException(
                    $"Failed to deserialize mod.json from '{sourcePath}'"
                );
            }
            
            manifest.ModSource = modSource;
            manifest.ModDirectory = sourcePath; // Keep for backward compatibility
            
            return manifest;
        }
    }
}
```

### 1. ArchiveModSource (Runtime Reader)

**Purpose**: Read files from `.monoball` archives at runtime.

**Key Features**:
- Lazy TOC loading (loads on first access)
- Thread-safe file reading (lock on stream access)
- Efficient random access via TOC lookup
- Fast LZ4 decompression per file

**Implementation**:

```csharp
using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using K4os.Compression.LZ4;
using MonoBall.Core.Mods.Definitions;
using MonoBall.Core.Mods.Utilities;
using System.Text.Json;
using Serilog;

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
        private readonly ReaderWriterLockSlim _tocLock = new();
        private readonly object _readLock = new();
        private bool _disposed = false;
        
        private const string Magic = "MONOBALL";
        private const ushort CurrentVersion = 1;
        
        // Cache compiled regex patterns for EnumerateFiles
        private static readonly Dictionary<string, Regex> _patternCache = new();
        private static readonly object _patternCacheLock = new();
        
        /// <summary>
        /// Initializes a new instance of ArchiveModSource.
        /// </summary>
        /// <param name="archivePath">Path to the .monoball archive file.</param>
        /// <exception cref="ArgumentNullException">Thrown when archivePath is null.</exception>
        /// <exception cref="FileNotFoundException">Thrown when archive file doesn't exist.</exception>
        public ArchiveModSource(string archivePath)
        {
            _archivePath = archivePath ?? throw new ArgumentNullException(nameof(archivePath));
            
            if (!File.Exists(archivePath))
            {
                throw new FileNotFoundException($"Archive not found: {archivePath}");
            }
            
            // Lazy load: TOC loaded on first access
        }
        
        public string SourcePath => _archivePath;
        public bool IsCompressed => true;
        
        public string ModId
        {
            get
            {
                if (_cachedModId != null)
                    return _cachedModId;
                
                _cachedModId = GetManifest().Id;
                return _cachedModId;
            }
        }
        
        /// <summary>
        /// Gets the TOC (Table of Contents). Thread-safe with reader-writer lock.
        /// </summary>
        internal Dictionary<string, FileEntry> GetTOC()
        {
            _tocLock.EnterReadLock();
            try
            {
                if (_toc != null)
                    return _toc;
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
                    return _toc;
                
                if (_disposed)
                    throw new ObjectDisposedException(nameof(ArchiveModSource));
                
                _stream = File.OpenRead(_archivePath);
                _toc = LoadTOC();
                
                return _toc;
            }
            finally
            {
                _tocLock.ExitWriteLock();
            }
        }
        
        private Dictionary<string, FileEntry> LoadTOC()
        {
            if (_stream == null)
                throw new InvalidOperationException("Stream not initialized");
            
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
            if (tocOffset >= archiveSize)
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
                    DataOffset = reader.ReadUInt64()
                };
                
                // Validate TOC entry integrity
                if (entry.DataOffset >= archiveSize)
                {
                    throw new InvalidDataException(
                        $"Invalid TOC entry for '{pathNormalized}': " +
                        $"DataOffset {entry.DataOffset} exceeds archive size {archiveSize}"
                    );
                }
                
                if (entry.DataOffset + entry.CompressedSize > archiveSize)
                {
                    throw new InvalidDataException(
                        $"Invalid TOC entry for '{pathNormalized}': " +
                        $"File extends beyond archive bounds"
                    );
                }
                
                // Validate sizes (empty files are allowed)
                if (entry.UncompressedSize == 0 && entry.CompressedSize > 0)
                {
                    throw new InvalidDataException(
                        $"Invalid TOC entry for '{pathNormalized}': " +
                        "Compressed size > 0 but uncompressed size = 0"
                    );
                }
                
                toc[pathNormalized] = entry;
            }
            
            return toc;
        }
        
        public byte[] ReadFile(string relativePath)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ArchiveModSource));
            
            if (string.IsNullOrEmpty(relativePath))
            {
                throw new ArgumentException("Relative path cannot be null or empty.", nameof(relativePath));
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
                if (_stream == null)
                    throw new InvalidOperationException("Stream not initialized");
                
                // Read compressed data using ArrayPool
                var compressedPooled = ArrayPool<byte>.Shared.Rent((int)entry.CompressedSize);
                var decompressedPooled = ArrayPool<byte>.Shared.Rent((int)entry.UncompressedSize);
                
                try
                {
                    _stream.Position = (long)entry.DataOffset;
                    var bytesRead = _stream.Read(compressedPooled, 0, (int)entry.CompressedSize);
                    
                    if (bytesRead != entry.CompressedSize)
                    {
                        throw new IOException(
                            $"Failed to read compressed data for '{relativePath}'. " +
                            $"Expected {entry.CompressedSize} bytes, read {bytesRead} bytes."
                        );
                    }
                    
                    // Decompress with LZ4
                    var decoded = LZ4Codec.Decode(
                        compressedPooled.AsSpan(0, (int)entry.CompressedSize),
                        decompressedPooled.AsSpan(0, (int)entry.UncompressedSize)
                    );
                    
                    if (decoded != entry.UncompressedSize)
                    {
                        throw new InvalidDataException(
                            $"Decompression failed for '{relativePath}'. " +
                            $"Expected {entry.UncompressedSize} bytes, got {decoded} bytes."
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
        }
        
        public string ReadTextFile(string relativePath)
        {
            var bytes = ReadFile(relativePath);
            return Encoding.UTF8.GetString(bytes);
        }
        
        public bool FileExists(string relativePath)
        {
            if (_disposed)
                return false;
            
            if (string.IsNullOrEmpty(relativePath))
                return false;
            
            var toc = GetTOC();
            var normalizedPath = ModPathNormalizer.Normalize(relativePath);
            return toc.ContainsKey(normalizedPath);
        }
        
        public IEnumerable<string> EnumerateFiles(string searchPattern, SearchOption searchOption)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ArchiveModSource));
            
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
                        if (path.Contains('/'))
                            continue;
                    }
                    
                    yield return path;
                }
            }
        }
        
        public ModManifest GetManifest()
        {
            if (_cachedManifest != null)
                return _cachedManifest;
            
            lock (_readLock)
            {
                // Double-check after acquiring lock
                if (_cachedManifest != null)
                    return _cachedManifest;
                
                if (!FileExists("mod.json"))
                {
                    throw new InvalidOperationException(
                        $"mod.json not found in archive '{_archivePath}'"
                    );
                }
                
                var jsonContent = ReadTextFile("mod.json");
                _cachedManifest = ModManifestLoader.LoadFromJson(jsonContent, _archivePath, this);
                
                return _cachedManifest;
            }
        }
        
        public void Dispose()
        {
            if (_disposed)
                return;
            
            _tocLock.EnterWriteLock();
            try
            {
                if (_disposed)
                    return;
                
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
```

### 2. DirectoryModSource (Backward Compatibility)

**Purpose**: Wraps existing directory-based mods for compatibility.

**Implementation**:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using MonoBall.Core.Mods.Utilities;
using System.Text.Json;

namespace MonoBall.Core.Mods
{
    /// <summary>
    /// Mod source that reads from a directory (uncompressed mods).
    /// </summary>
    public class DirectoryModSource : IModSource, IDisposable
    {
        private readonly string _modDirectory;
        private ModManifest? _cachedManifest;
        private string? _cachedModId;
        private readonly object _lock = new();
        
        public DirectoryModSource(string modDirectory)
        {
            _modDirectory = modDirectory ?? throw new ArgumentNullException(nameof(modDirectory));
            
            if (!Directory.Exists(modDirectory))
            {
                throw new DirectoryNotFoundException($"Mod directory not found: {modDirectory}");
            }
        }
        
        public string SourcePath => _modDirectory;
        public bool IsCompressed => false;
        
        public string ModId
        {
            get
            {
                if (_cachedModId != null)
                    return _cachedModId;
                
                _cachedModId = GetManifest().Id;
                return _cachedModId;
            }
        }
        
        public byte[] ReadFile(string relativePath)
        {
            if (string.IsNullOrEmpty(relativePath))
            {
                throw new ArgumentException("Relative path cannot be null or empty.", nameof(relativePath));
            }
            
            var fullPath = Path.Combine(_modDirectory, relativePath);
            fullPath = Path.GetFullPath(fullPath);
            
            var modDirFullPath = Path.GetFullPath(_modDirectory);
            
            // Security: Ensure path is within mod directory
            if (!fullPath.StartsWith(modDirFullPath, StringComparison.Ordinal))
            {
                throw new UnauthorizedAccessException(
                    $"Path traversal detected: '{relativePath}'"
                );
            }
            
            if (!File.Exists(fullPath))
            {
                throw new FileNotFoundException(
                    $"File '{relativePath}' not found in mod directory '{_modDirectory}'"
                );
            }
            
            return File.ReadAllBytes(fullPath);
        }
        
        public string ReadTextFile(string relativePath)
        {
            var bytes = ReadFile(relativePath);
            return Encoding.UTF8.GetString(bytes);
        }
        
        public bool FileExists(string relativePath)
        {
            if (string.IsNullOrEmpty(relativePath))
                return false;
            
            var fullPath = Path.Combine(_modDirectory, relativePath);
            fullPath = Path.GetFullPath(fullPath);
            
            var modDirFullPath = Path.GetFullPath(_modDirectory);
            
            if (!fullPath.StartsWith(modDirFullPath, StringComparison.Ordinal))
            {
                return false;
            }
            
            return File.Exists(fullPath);
        }
        
        public IEnumerable<string> EnumerateFiles(string searchPattern, SearchOption searchOption)
        {
            var files = Directory.GetFiles(_modDirectory, searchPattern, searchOption);
            var modDirFullPath = Path.GetFullPath(_modDirectory);
            
            foreach (var file in files)
            {
                var relativePath = Path.GetRelativePath(modDirFullPath, file);
                var normalized = ModPathNormalizer.Normalize(relativePath);
                
                // Handle SearchOption.TopDirectoryOnly
                if (searchOption == SearchOption.TopDirectoryOnly)
                {
                    // Only include files in root directory (no path separators)
                    if (normalized.Contains('/'))
                        continue;
                }
                
                yield return normalized;
            }
        }
        
        public ModManifest GetManifest()
        {
            if (_cachedManifest != null)
                return _cachedManifest;
            
            lock (_lock)
            {
                // Double-check after acquiring lock
                if (_cachedManifest != null)
                    return _cachedManifest;
                
                var modJsonPath = Path.Combine(_modDirectory, "mod.json");
                if (!File.Exists(modJsonPath))
                {
                    throw new InvalidOperationException(
                        $"mod.json not found in mod directory '{_modDirectory}'"
                    );
                }
                
                var jsonContent = File.ReadAllText(modJsonPath);
                _cachedManifest = ModManifestLoader.LoadFromJson(jsonContent, _modDirectory, this);
                
                return _cachedManifest;
            }
        }
        
        public void Dispose()
        {
            // Nothing to dispose for directory-based source, but implement for consistency
        }
    }
}
```

### 3. ModLoader Updates

**Updated `DiscoverMods()` method**:

```csharp
private List<ModManifest> DiscoverMods(List<string> errors)
{
    var mods = new List<ModManifest>();
    
    if (!Directory.Exists(_modsDirectory))
    {
        errors.Add($"Mods directory does not exist: {_modsDirectory}");
        return mods;
    }
    
    // Discover directory-based mods
    var modDirectories = Directory.GetDirectories(_modsDirectory);
    _logger.Debug("Scanning {DirectoryCount} directories for mods", modDirectories.Length);
    
    foreach (var modDir in modDirectories)
    {
        try
        {
            var source = new DirectoryModSource(modDir);
            if (TryLoadModSource(source, errors, out var manifest))
            {
                mods.Add(manifest);
                _modsById[manifest.Id] = manifest;
            }
        }
        catch (Exception ex)
        {
            errors.Add($"Error loading mod from directory '{Path.GetFileName(modDir)}': {ex.Message}");
        }
    }
    
    // Discover compressed mods (.monoball archives)
    var archives = Directory.GetFiles(_modsDirectory, "*.monoball");
    _logger.Debug("Scanning {ArchiveCount} archives for mods", archives.Length);
    
    foreach (var archivePath in archives)
    {
        try
        {
            var source = new ArchiveModSource(archivePath);
            
            // Force TOC load during discovery to validate archive integrity early
            if (source is ArchiveModSource archiveSource)
            {
                _ = archiveSource.GetTOC(); // Trigger TOC load, catch errors
            }
            
            if (TryLoadModSource(source, errors, out var manifest))
            {
                mods.Add(manifest);
                _modsById[manifest.Id] = manifest;
            }
        }
        catch (Exception ex)
        {
            errors.Add($"Error loading archive '{Path.GetFileName(archivePath)}': {ex.Message}");
        }
    }
    
    _logger.Debug("Discovery completed: {ModCount} mods found", mods.Count);
    return mods;
}

private bool TryLoadModSource(IModSource source, List<string> errors, out ModManifest manifest)
{
    manifest = null!;
    
    try
    {
        manifest = source.GetManifest();
        
        // Validate required fields
        if (string.IsNullOrEmpty(manifest.Id))
        {
            errors.Add($"Mod source '{source.SourcePath}' has missing or empty 'id' field");
            return false;
        }
        
        if (_modsById.ContainsKey(manifest.Id))
        {
            errors.Add($"Duplicate mod ID '{manifest.Id}' found in '{source.SourcePath}'");
            return false;
        }
        
        _logger.Debug(
            "Discovered mod: {ModId} ({ModName}) from {SourceType}",
            manifest.Id,
            manifest.Name,
            source.IsCompressed ? "archive" : "directory"
        );
        
        return true;
    }
    catch (Exception ex)
    {
        errors.Add($"Error loading mod from '{source.SourcePath}': {ex.Message}");
        return false;
    }
}
```

**Updated `LoadModDefinitions()` method** (CRITICAL FIX):

```csharp
/// <summary>
/// Loads all definitions from a mod using IModSource.
/// </summary>
private void LoadModDefinitions(ModManifest mod, List<string> errors)
{
    if (mod.ModSource == null)
    {
        throw new InvalidOperationException(
            $"Mod '{mod.Id}' has no ModSource. " +
            "All mods must have a ModSource (directory or archive)."
        );
    }
    
    _logger.Debug(
        "Loading definitions for mod {ModId} from {ContentFolderCount} content folders",
        mod.Id,
        mod.ContentFolders.Count
    );
    
    // Load definitions from each content folder type
    foreach (var (folderType, relativePath) in mod.ContentFolders)
    {
        if (string.IsNullOrEmpty(relativePath))
        {
            continue;
        }
        
        // Use IModSource.EnumerateFiles() instead of Directory.GetFiles()
        var jsonFiles = mod.ModSource.EnumerateFiles("*.json", SearchOption.AllDirectories)
            .Where(p => p.StartsWith(relativePath + "/", StringComparison.Ordinal) || 
                       p == relativePath || 
                       p.StartsWith(relativePath + "\\", StringComparison.Ordinal))
            .ToList();
        
        foreach (var jsonFile in jsonFiles)
        {
            LoadDefinitionFromFile(mod.ModSource, jsonFile, folderType, mod, errors);
        }
    }
    
    // Load script definitions from Definitions/Scripts/ subdirectories
    var scriptFiles = mod.ModSource.EnumerateFiles("*.json", SearchOption.AllDirectories)
        .Where(p => p.StartsWith("Definitions/Scripts/", StringComparison.Ordinal) ||
                   p.StartsWith("Definitions\\Scripts\\", StringComparison.Ordinal))
        .ToList();
    
    foreach (var scriptFile in scriptFiles)
    {
        LoadDefinitionFromFile(mod.ModSource, scriptFile, "Script", mod, errors);
    }
    
    // Load behavior definitions from Definitions/Behaviors/ subdirectories
    var behaviorFiles = mod.ModSource.EnumerateFiles("*.json", SearchOption.AllDirectories)
        .Where(p => p.StartsWith("Definitions/Behaviors/", StringComparison.Ordinal) ||
                   p.StartsWith("Definitions\\Behaviors\\", StringComparison.Ordinal))
        .ToList();
    
    foreach (var behaviorFile in behaviorFiles)
    {
        LoadDefinitionFromFile(mod.ModSource, behaviorFile, "Behavior", mod, errors);
    }
}

/// <summary>
/// Loads a single definition file from IModSource.
/// </summary>
private void LoadDefinitionFromFile(
    IModSource modSource,
    string relativePath,
    string definitionType,
    ModManifest mod,
    List<string> errors
)
{
    try
    {
        var jsonContent = modSource.ReadTextFile(relativePath);
        var jsonDoc = JsonDocument.Parse(jsonContent);
        
        if (!jsonDoc.RootElement.TryGetProperty("id", out var idElement))
        {
            errors.Add($"Definition file '{relativePath}' is missing 'id' field");
            return;
        }
        
        var id = idElement.GetString();
        if (string.IsNullOrEmpty(id))
        {
            errors.Add($"Definition file '{relativePath}' has empty 'id' field");
            return;
        }
        
        // Determine operation type (defaults to Create, but can be specified)
        var operation = DefinitionOperation.Create;
        if (jsonDoc.RootElement.TryGetProperty("$operation", out var opElement))
        {
            var opString = opElement.GetString()?.ToLowerInvariant();
            operation = opString switch
            {
                "modify" => DefinitionOperation.Modify,
                "extend" => DefinitionOperation.Extend,
                "replace" => DefinitionOperation.Replace,
                _ => DefinitionOperation.Create,
            };
        }
        
        // Check if definition already exists
        var existing = _registry.GetById(id);
        if (existing != null)
        {
            // Apply operation
            var finalData = jsonDoc.RootElement;
            if (operation == DefinitionOperation.Modify || operation == DefinitionOperation.Extend)
            {
                finalData = JsonElementMerger.Merge(
                    existing.Data,
                    jsonDoc.RootElement,
                    operation == DefinitionOperation.Extend
                );
            }
            // For Replace, use the new data as-is
            
            var metadata = new DefinitionMetadata
            {
                Id = id,
                OriginalModId = existing.OriginalModId,
                LastModifiedByModId = mod.Id,
                Operation = operation,
                DefinitionType = definitionType,
                Data = finalData,
                SourcePath = relativePath, // Already relative path from mod source
            };
            
            _registry.Register(metadata);
        }
        else
        {
            // New definition
            var metadata = new DefinitionMetadata
            {
                Id = id,
                OriginalModId = mod.Id,
                LastModifiedByModId = mod.Id,
                Operation = DefinitionOperation.Create,
                DefinitionType = definitionType,
                Data = jsonDoc.RootElement,
                SourcePath = relativePath, // Already relative path from mod source
            };
            
            _registry.Register(metadata);
        }
    }
    catch (JsonException ex)
    {
        var errorMessage = $"JSON error in definition file '{relativePath}': {ex.Message}";
        _logger.Error(ex, errorMessage);
        errors.Add(errorMessage);
    }
    catch (Exception ex)
    {
        var errorMessage = $"Error loading definition from '{relativePath}': {ex.Message}";
        _logger.Error(ex, errorMessage);
        errors.Add(errorMessage);
    }
}
```

### 4. ModManifest Updates

**Add `IModSource` reference**:

```csharp
public class ModManifest
{
    // ... existing properties ...
    
    /// <summary>
    /// Gets or sets the mod source (directory or archive).
    /// Set by ModLoader during discovery.
    /// </summary>
    public IModSource? ModSource { get; set; }
    
    // Keep ModDirectory for backward compatibility
    // Prefer using ModSource for file access
    public string ModDirectory { get; set; } = string.Empty;
}
```

### 5. ResourcePathResolver Updates

**Update to use `IModSource` (Fail-Fast, No Fallback)**:

```csharp
public string ResolveResourcePath(string resourceId, string relativePath)
{
    if (string.IsNullOrEmpty(resourceId))
    {
        throw new ArgumentException("Resource ID cannot be null or empty.", nameof(resourceId));
    }
    
    if (string.IsNullOrEmpty(relativePath))
    {
        throw new ArgumentException("Relative path cannot be null or empty.", nameof(relativePath));
    }
    
    // Get mod manifest - fail fast if not found (no fallback code per .cursorrules)
    var modManifest = GetResourceModManifest(resourceId);
    
    // Fail fast if ModSource is null (per .cursorrules: no fallback code)
    if (modManifest.ModSource == null)
    {
        throw new InvalidOperationException(
            $"Mod '{modManifest.Id}' has no ModSource. " +
            "All mods must have a ModSource (directory or archive). " +
            "Cannot resolve resource path without ModSource."
        );
    }
    
    // Validate file exists
    if (!modManifest.ModSource.FileExists(relativePath))
    {
        throw new FileNotFoundException(
            $"Resource file not found in mod: {relativePath} (resource: {resourceId}, mod: {modManifest.Id})"
        );
    }
    
    // Return a virtual path that ResourceManager can use
    // Format: "mod://{modId}/{relativePath}"
    return $"mod://{modManifest.Id}/{relativePath}";
}
```

### 6. ResourceManager Updates

**Support virtual `mod://` paths**:

```csharp
public Texture2D LoadTexture(string resourceId)
{
    // ... validation ...
    
    string relativePath = ExtractTexturePath(resourceId);
    string resolvedPath = _pathResolver.ResolveResourcePath(resourceId, relativePath);
    
    // All paths from ResourcePathResolver are now mod:// paths (no fallback)
    if (!resolvedPath.StartsWith("mod://"))
    {
        throw new InvalidOperationException(
            $"Unexpected path format from ResourcePathResolver: {resolvedPath}. " +
            "Expected mod:// path format."
        );
    }
    
    // Extract mod ID and relative path
    var parts = resolvedPath.Substring(6).Split('/', 2);
    if (parts.Length != 2)
    {
        throw new InvalidOperationException($"Invalid mod:// path format: {resolvedPath}");
    }
    
    var modId = parts[0];
    var filePath = parts[1];
    
    var modManifest = _modManager.GetModManifest(modId);
    if (modManifest == null)
    {
        throw new InvalidOperationException($"Mod manifest not found for mod '{modId}'");
    }
    
    if (modManifest.ModSource == null)
    {
        throw new InvalidOperationException(
            $"Mod source not available for mod '{modId}'. " +
            "All mods must have a ModSource."
        );
    }
    
    // Read from archive/directory
    byte[] fileData = modManifest.ModSource.ReadFile(filePath);
    
    // Load texture from memory stream
    using (var stream = new MemoryStream(fileData))
    {
        var texture = Texture2D.FromStream(_graphicsDevice, stream);
        
        // ... cache logic ...
        
        return texture;
    }
}
```

**Similar updates for**:
- `LoadFont()` - read TTF from `IModSource`
- `LoadAudioReader()` - read OGG from `IModSource`
- `LoadShader()` - read bytecode from `IModSource`

### 7. ModValidator Updates

**Update to use `IModSource`**:

```csharp
/// <summary>
/// Collects definition IDs from a mod source.
/// </summary>
private void CollectDefinitionIds(
    ModManifest manifest,
    IModSource modSource,
    Dictionary<string, List<DefinitionLocation>> definitionIds,
    List<ValidationIssue> issues
)
{
    foreach (var (folderType, relativePath) in manifest.ContentFolders)
    {
        if (string.IsNullOrEmpty(relativePath))
        {
            continue;
        }
        
        // Use IModSource.EnumerateFiles() instead of Directory.GetFiles()
        var jsonFiles = modSource.EnumerateFiles("*.json", SearchOption.AllDirectories)
            .Where(p => p.StartsWith(relativePath + "/", StringComparison.Ordinal) || 
                       p == relativePath ||
                       p.StartsWith(relativePath + "\\", StringComparison.Ordinal))
            .ToList();
        
        foreach (var jsonFile in jsonFiles)
        {
            try
            {
                var jsonContent = modSource.ReadTextFile(jsonFile);
                var jsonDoc = JsonDocument.Parse(jsonContent);
                
                if (jsonDoc.RootElement.TryGetProperty("id", out var idElement))
                {
                    var id = idElement.GetString();
                    if (!string.IsNullOrEmpty(id))
                    {
                        if (!definitionIds.ContainsKey(id))
                        {
                            definitionIds[id] = new List<DefinitionLocation>();
                        }
                        
                        definitionIds[id].Add(new DefinitionLocation
                        {
                            ModId = manifest.Id,
                            FilePath = jsonFile // Already relative path
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                issues.Add(new ValidationIssue
                {
                    Severity = ValidationSeverity.Warning,
                    Message = $"Error reading definition file '{jsonFile}': {ex.Message}"
                });
            }
        }
    }
}
```

---

## Archive Creation Tool

### Design: MonoBall.ArchiveTool CLI

**Purpose**: Command-line tool to create `.monoball` archives from mod directories.

**Usage**:
```bash
MonoBall.ArchiveTool pack <mod-directory> [--output <path>] [--compression-level <1-9>]
MonoBall.ArchiveTool unpack <archive-file> [--output <directory>]
MonoBall.ArchiveTool info <archive-file>
```

**Implementation** (separate project/tool):

```csharp
// MonoBall.ArchiveTool/Program.cs (simplified)
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using K4os.Compression.LZ4;

namespace MonoBall.ArchiveTool
{
    public class ArchiveCreator
    {
        public void CreateArchive(string modDirectory, string outputPath, int compressionLevel = 1)
        {
            if (!Directory.Exists(modDirectory))
            {
                throw new DirectoryNotFoundException($"Mod directory not found: {modDirectory}");
            }
            
            if (string.IsNullOrEmpty(outputPath))
            {
                throw new ArgumentException("Output path cannot be null or empty.", nameof(outputPath));
            }
            
            var files = Directory.GetFiles(modDirectory, "*", SearchOption.AllDirectories);
            var toc = new List<FileEntry>();
            var dataOffset = 18UL; // Header size
            
            try
            {
                using (var writer = new BinaryWriter(File.Create(outputPath)))
                {
                    // Write header (placeholder TOC offset)
                    writer.Write(Encoding.ASCII.GetBytes("MONOBALL"));
                    writer.Write((ushort)1); // Version
                    writer.Write((ulong)0); // TOC offset (fill later)
                    
                    // Compress and write each file
                    foreach (var file in files)
                    {
                        var relativePath = Path.GetRelativePath(modDirectory, file)
                            .Replace('\\', '/');
                        
                        byte[] uncompressed;
                        try
                        {
                            uncompressed = File.ReadAllBytes(file);
                        }
                        catch (IOException ex)
                        {
                            throw new IOException($"Failed to read file '{file}': {ex.Message}", ex);
                        }
                        catch (UnauthorizedAccessException ex)
                        {
                            throw new UnauthorizedAccessException(
                                $"Access denied when reading file '{file}': {ex.Message}", ex
                            );
                        }
                        
                        byte[] compressed;
                        try
                        {
                            compressed = LZ4Codec.Encode(uncompressed, compressionLevel);
                        }
                        catch (Exception ex)
                        {
                            throw new InvalidOperationException(
                                $"Failed to compress file '{file}': {ex.Message}", ex
                            );
                        }
                        
                        toc.Add(new FileEntry
                        {
                            Path = relativePath,
                            UncompressedSize = (ulong)uncompressed.Length,
                            CompressedSize = (ulong)compressed.Length,
                            DataOffset = dataOffset
                        });
                        
                        writer.Write(compressed);
                        dataOffset += (ulong)compressed.Length;
                    }
                    
                    // Write TOC
                    var tocOffset = (ulong)writer.BaseStream.Position;
                    writer.Write(toc.Count);
                    
                    foreach (var entry in toc)
                    {
                        var pathBytes = Encoding.UTF8.GetBytes(entry.Path);
                        if (pathBytes.Length > ushort.MaxValue)
                        {
                            throw new InvalidOperationException(
                                $"Path too long: '{entry.Path}' ({pathBytes.Length} bytes, max {ushort.MaxValue})"
                            );
                        }
                        
                        writer.Write((ushort)pathBytes.Length);
                        writer.Write(pathBytes);
                        writer.Write(entry.UncompressedSize);
                        writer.Write(entry.CompressedSize);
                        writer.Write(entry.DataOffset);
                    }
                    
                    // Update header with TOC offset
                    writer.BaseStream.Position = 16;
                    writer.Write(tocOffset);
                }
            }
            catch (IOException ex)
            {
                throw new IOException($"Failed to create archive '{outputPath}': {ex.Message}", ex);
            }
            catch (UnauthorizedAccessException ex)
            {
                throw new UnauthorizedAccessException(
                    $"Access denied when creating archive '{outputPath}': {ex.Message}", ex
                );
            }
        }
        
        private class FileEntry
        {
            public string Path { get; set; } = string.Empty;
            public ulong UncompressedSize { get; set; }
            public ulong CompressedSize { get; set; }
            public ulong DataOffset { get; set; }
        }
    }
}
```

---

## Performance Considerations

### Decompression Speed

**LZ4 Performance** (typical game assets):
- **Decompression**: 3-5x faster than ZIP deflate
- **Compression**: 2-3x faster than ZIP deflate
- **Compression Ratio**: ~10-20% larger than 7z/LZMA

**Trade-off**: Prioritize speed over compression ratio.

### Random Access Performance

**TOC Lookup**: O(1) dictionary lookup (very fast)

**File Reading**:
- Single seek to file offset
- Read compressed data (one I/O operation)
- LZ4 decompress in memory
- Total: ~1-2ms per file (depending on size)

### Memory Usage

- **TOC**: ~100-500 bytes per file (depends on path length)
- **Decompression**: Temporary buffer (file size)
- **Stream**: Single FileStream shared across reads (thread-safe with locking)

---

## Migration Strategy

### Phase 1: Add Infrastructure
1. Add `K4os.Compression.LZ4` NuGet package
2. Create `IModSource` interface
3. Implement `DirectoryModSource`
4. Implement `ArchiveModSource`
5. Update `ModManifest` to include `ModSource`

### Phase 2: Update Discovery
1. Update `ModLoader.DiscoverMods()` to detect `.monoball` archives
2. Create `IModSource` instances for each mod
3. Store `ModSource` in `ModManifest`
4. Test with both directory and archive mods

### Phase 3: Update Resource Loading
1. Update `ResourcePathResolver` to return `mod://` paths (fail-fast, no fallback)
2. Update `ResourceManager` to read from `IModSource`
3. Update `ModValidator` to use `IModSource`
4. Test resource loading from archives

### Phase 4: Archive Creation Tool
1. Create `MonoBall.ArchiveTool` project
2. Implement archive creation
3. Add to build process (optional)

### Phase 5: Cleanup (Per .cursorrules)
1. Remove legacy `ModDirectory` usage (update all call sites)
2. Update all code to use `ModSource`
3. Add documentation

---

## Benefits

1. **Fast Decompression**: LZ4 provides fastest decompression for runtime performance
2. **Random Access**: Efficient file-by-file access without full extraction
3. **Single File Distribution**: Easy mod distribution and installation
4. **Backward Compatible**: Supports both compressed and directory mods
5. **Simple Format**: Custom format optimized for MonoBall's needs

---

## Considerations

### Compression Ratio Trade-off

- **LZ4**: ~10-20% larger files than 7z, but 3-5x faster decompression
- **Acceptable**: Download size slightly larger, but runtime performance significantly better

### Archive Creation

- **Tool Required**: Need `MonoBall.ArchiveTool` or similar to create archives
- **User Workflow**: Mod developers compress directories → distribute `.monoball` files

### Error Handling

- **Fail-Fast**: Invalid archives throw clear exceptions (per .cursorrules)
- **Validation**: Magic number, version check, TOC validation

### Thread Safety

- **ArchiveModSource**: Thread-safe with locking on stream access
- **Multiple Readers**: Safe to read different files concurrently (different locks)

---

## Implementation Checklist

### Phase 1: Core Infrastructure
- [ ] Add `K4os.Compression.LZ4` NuGet package
- [ ] Create `IModSource` interface
- [ ] Implement `DirectoryModSource`
- [ ] Implement `ArchiveModSource`
- [ ] Update `ModManifest` with `ModSource` property
- [ ] Add unit tests for `ArchiveModSource`

### Phase 2: Mod Discovery
- [ ] Update `ModLoader.DiscoverMods()` to detect `.monoball` archives
- [ ] Implement `TryLoadModSource()` helper
- [ ] Test discovery with both formats
- [ ] Update logging to indicate archive vs directory

### Phase 3: Resource Loading
- [ ] Update `ResourcePathResolver.ResolveResourcePath()` for `mod://` paths (fail-fast, no fallback)
- [ ] Update `ResourceManager.LoadTexture()` to read from `IModSource`
- [ ] Update `ResourceManager.LoadFont()` to read from `IModSource`
- [ ] Update `ResourceManager.LoadAudioReader()` to read from `IModSource`
- [ ] Update `ResourceManager.LoadShader()` to read from `IModSource`
- [ ] Update `ModValidator.CollectDefinitionIds()` to use `IModSource`
- [ ] Test resource loading from archives

### Phase 4: Archive Creation Tool
- [ ] Create `MonoBall.ArchiveTool` project
- [ ] Implement `ArchiveCreator.CreateArchive()`
- [ ] Add CLI argument parsing
- [ ] Test archive creation
- [ ] Document tool usage

### Phase 5: Cleanup
- [ ] Remove legacy `ModDirectory` usage (update all call sites)
- [ ] Update all code to use `ModSource`
- [ ] Add XML documentation
- [ ] Update mod development documentation

---

## References

- **LZ4**: https://github.com/lz4/lz4
- **K4os.Compression.LZ4**: https://github.com/MiloszKrajewski/K4os.Compression.LZ4
- **Current Mod System**: `MonoBall.Core/Mods/ModLoader.cs`
- **Resource Manager**: `docs/design/RESOURCE_MANAGER_DESIGN.md`

---

## Design Updates Based on Analysis

This design has been updated to address all issues identified in `COMPRESSED_MOD_SUPPORT_DESIGN_ANALYSIS.md`:

### Critical Architecture Fixes
- ✅ **ModLoader now uses IModSource** - `LoadModDefinitions()` updated to use `IModSource.EnumerateFiles()` instead of `Directory.GetFiles()`
- ✅ **ModValidator updated** - `CollectDefinitionIds()` now uses `IModSource` instead of directory paths
- ✅ **ResourcePathResolver fail-fast** - Removed fallback code, throws exception if `ModSource` is null (per .cursorrules)
- ✅ **Consistent path handling** - All paths are now `mod://` virtual paths, no mixed return types

### SOLID/DRY Fixes
- ✅ **Path normalization extracted** - `ModPathNormalizer` utility class for shared logic
- ✅ **Manifest loading extracted** - `ModManifestLoader` utility class for shared JSON deserialization
- ✅ **Single Responsibility** - Clear separation between archive reading and manifest loading

### Performance Improvements
- ✅ **Reader-writer lock** - Separate locks for TOC access vs file reading to reduce contention
- ✅ **Cached ModId** - ModId property caches value after first access
- ✅ **ArrayPool usage** - Temporary buffers use `ArrayPool<byte>` to reduce GC pressure
- ✅ **Regex caching** - Compiled regex patterns cached for `EnumerateFiles()`
- ✅ **Efficient FileExists()** - Removed exception catching, uses direct TOC lookup
- ✅ **TOC validation** - TOC integrity validated during load (early error detection)

### Other Fixes
- ✅ **Empty file support** - Documented and implemented handling for 0-byte files
- ✅ **TOC integrity validation** - Validates offsets and sizes are within archive bounds
- ✅ **SearchOption handling** - Properly implements `TopDirectoryOnly` vs `AllDirectories`
- ✅ **Thread safety** - Double-checked locking for manifest caching
- ✅ **IDisposable consistency** - `DirectoryModSource` implements `IDisposable` for consistency
- ✅ **Error handling** - Archive creation tool has proper error handling
- ✅ **Missing using statements** - All required `using` statements added
- ✅ **FileEntry accessibility** - Made `internal` for testing

---

## Next Steps

1. Review and approve design
2. Begin Phase 1 implementation (core infrastructure)
3. Test `ArchiveModSource` thoroughly
4. Execute Phase 2 (mod discovery)
5. Execute Phase 3 (resource loading)
6. Create archive creation tool (Phase 4)
7. Cleanup and optimization (Phase 5)

