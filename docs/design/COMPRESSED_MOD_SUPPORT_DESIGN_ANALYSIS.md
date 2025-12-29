# Compressed Mod Support Design - Analysis

**Generated:** 2025-01-16  
**Status:** Design Analysis  
**Scope:** Critical issues found in COMPRESSED_MOD_SUPPORT_DESIGN.md

---

## Executive Summary

This document analyzes the compressed mod support design for architecture issues, SOLID/DRY violations, performance problems, and other concerns. **Critical issues** must be addressed before implementation.

---

## Critical Architecture Issues

### 1. ❌ ModLoader Cannot Load Definitions from Archives

**Problem**: `ModLoader.LoadModDefinitions()` and `LoadDefinitionsFromDirectory()` use `Directory.GetFiles()` and `Path.Combine(mod.ModDirectory, ...)` which **will not work for archives**.

**Location**: Lines 436-599 in current `ModLoader.cs`, design document doesn't address this

**Impact**: **CRITICAL** - Definition loading will fail for compressed mods

**Current Code**:
```csharp
var definitionsPath = Path.Combine(mod.ModDirectory, relativePath);
if (!Directory.Exists(definitionsPath)) // ❌ Always false for archives
    continue;

var jsonFiles = Directory.GetFiles(directory, "*.json", ...); // ❌ Won't work for archives
var jsonContent = File.ReadAllText(jsonFile); // ❌ jsonFile is a directory path
```

**Required Fix**: Update `LoadModDefinitions()` to use `IModSource`:
```csharp
private void LoadModDefinitions(ModManifest mod, List<string> errors)
{
    if (mod.ModSource == null)
    {
        throw new InvalidOperationException($"Mod '{mod.Id}' has no ModSource");
    }
    
    // Use IModSource.EnumerateFiles() instead of Directory.GetFiles()
    foreach (var (folderType, relativePath) in mod.ContentFolders)
    {
        if (string.IsNullOrEmpty(relativePath))
            continue;
        
        // Enumerate JSON files from mod source
        var jsonFiles = mod.ModSource.EnumerateFiles("*.json", SearchOption.AllDirectories)
            .Where(p => p.StartsWith(relativePath + "/", StringComparison.Ordinal));
        
        foreach (var jsonFile in jsonFiles)
        {
            var jsonContent = mod.ModSource.ReadTextFile(jsonFile);
            // ... process definition ...
        }
    }
}
```

---

### 2. ❌ ModValidator Cannot Validate Archives

**Problem**: `ModValidator.CollectDefinitionIds()` uses `Directory.GetFiles()` which won't work for archives.

**Location**: `ModValidator.cs` line 268

**Impact**: **CRITICAL** - Mod validation will fail for compressed mods

**Required Fix**: Update `ModValidator` to accept `IModSource` instead of directory path.

---

### 3. ❌ ResourcePathResolver Fallback Code Violates .cursorrules

**Problem**: `ResourcePathResolver.ResolveResourcePath()` has fallback logic (checks `ModSource`, falls back to `ModDirectory`).

**Location**: Design document lines 755-785

**Impact**: **VIOLATION** - Per .cursorrules: "NO FALLBACK CODE - Fail fast with clear exceptions"

**Current Design**:
```csharp
if (modManifest.ModSource != null)
{
    // Use ModSource
}
else
{
    // Fall back to ModDirectory ❌ FALLBACK CODE
}
```

**Required Fix**: Fail fast if `ModSource` is null:
```csharp
if (modManifest.ModSource == null)
{
    throw new InvalidOperationException(
        $"Mod '{modManifest.Id}' has no ModSource. " +
        "All mods must have a ModSource (directory or archive)."
    );
}

// Use ModSource only - no fallback
if (!modManifest.ModSource.FileExists(relativePath))
{
    throw new FileNotFoundException(...);
}

return $"mod://{modManifest.Id}/{relativePath}";
```

---

### 4. ❌ Inconsistent Path Return Types

**Problem**: `ResourcePathResolver` returns `mod://` virtual paths, but `ResourceManager` must parse them. This creates coupling and inconsistency.

**Location**: Design document lines 739-786, 794-842

**Impact**: **ARCHITECTURE** - Tight coupling between `ResourcePathResolver` and `ResourceManager`

**Better Design**: `ResourcePathResolver` should return a structured type or `ResourceManager` should use `IModSource` directly.

**Option 1**: Return `IModSource` + relative path:
```csharp
public (IModSource source, string relativePath) ResolveResourceSource(string resourceId, string relativePath);
```

**Option 2**: `ResourceManager` uses `IModManager` directly (bypasses `ResourcePathResolver`):
```csharp
var modManifest = _modManager.GetModManifestByDefinitionId(resourceId);
var fileData = modManifest.ModSource.ReadFile(relativePath);
```

---

## SOLID/DRY Violations

### 5. ❌ Path Normalization Duplicated

**Problem**: `NormalizePath()` logic duplicated in `ArchiveModSource` and `DirectoryModSource`.

**Location**: Design document lines 454-458, 575

**Impact**: **DRY VIOLATION** - Changes must be made in two places

**Fix**: Extract to shared utility:
```csharp
namespace MonoBall.Core.Mods.Utilities
{
    public static class ModPathNormalizer
    {
        public static string Normalize(string path)
        {
            return path.Replace('\\', '/').TrimStart('/');
        }
    }
}
```

---

### 6. ❌ Manifest Loading Duplicated

**Problem**: `GetManifest()` logic duplicated between `ArchiveModSource` and `DirectoryModSource`.

**Location**: Design document lines 422-452, 579-610

**Impact**: **DRY VIOLATION** - JSON deserialization logic duplicated

**Fix**: Extract to base class or utility:
```csharp
public abstract class ModSourceBase : IModSource
{
    protected ModManifest? _cachedManifest;
    
    protected ModManifest LoadManifestFromJson(string jsonContent, string sourcePath)
    {
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
        
        manifest.ModSource = this;
        manifest.ModDirectory = SourcePath;
        return manifest;
    }
    
    public abstract ModManifest GetManifest();
}
```

---

### 7. ❌ Single Responsibility Violation

**Problem**: `ArchiveModSource` handles both archive reading AND manifest caching/loading.

**Location**: Design document lines 208-483

**Impact**: **SRP VIOLATION** - Class does too much

**Better Design**: Separate concerns:
- `ArchiveModSource` - Archive reading only
- `ModManifestLoader` - Manifest loading (shared utility)

---

## Performance Issues

### 8. ⚠️ FileStream Kept Open Indefinitely

**Problem**: `ArchiveModSource` opens `FileStream` on first TOC access and keeps it open until disposal. For many archives, this could exhaust file handles.

**Location**: Design document lines 263, 344-375

**Impact**: **PERFORMANCE** - File handle exhaustion with many mods

**Fix**: Consider closing stream after TOC load, reopening for file reads (with caching):
```csharp
private Dictionary<string, FileEntry> LoadTOC()
{
    // ... read TOC ...
    _stream.Dispose(); // Close after TOC load
    _stream = null;
    return toc;
}

public byte[] ReadFile(string relativePath)
{
    // Reopen stream for reading
    if (_stream == null)
    {
        _stream = File.OpenRead(_archivePath);
    }
    // ... read file ...
}
```

**Alternative**: Use `FileShare.Read` and keep open (current approach is acceptable if file handles are managed).

---

### 9. ⚠️ Inefficient FileExists() Implementation

**Problem**: `ArchiveModSource.FileExists()` catches exceptions and returns false, which is inefficient.

**Location**: Design document lines 384-399

**Impact**: **PERFORMANCE** - Exception handling overhead

**Current Code**:
```csharp
public bool FileExists(string relativePath)
{
    try
    {
        var toc = GetTOC(); // May throw
        return toc.ContainsKey(normalizedPath);
    }
    catch
    {
        return false; // ❌ Inefficient
    }
}
```

**Fix**: Check disposed flag explicitly:
```csharp
public bool FileExists(string relativePath)
{
    if (_disposed)
        return false;
    
    if (string.IsNullOrEmpty(relativePath))
        return false;
    
    var toc = GetTOC();
    var normalizedPath = NormalizePath(relativePath);
    return toc.ContainsKey(normalizedPath);
}
```

---

### 10. ⚠️ Regex Created Per EnumerateFiles() Call

**Problem**: `ArchiveModSource.EnumerateFiles()` creates new `Regex` for each call.

**Location**: Design document lines 401-420

**Impact**: **PERFORMANCE** - Regex compilation overhead

**Fix**: Cache compiled regex patterns or use simpler string matching:
```csharp
// Option 1: Simple glob matching (faster)
private bool MatchesPattern(string fileName, string pattern)
{
    if (pattern == "*")
        return true;
    
    if (!pattern.Contains('*'))
        return fileName.Equals(pattern, StringComparison.OrdinalIgnoreCase);
    
    // Simple wildcard matching
    var parts = pattern.Split('*');
    // ... implement simple matching ...
}

// Option 2: Cache compiled regex
private static readonly Dictionary<string, Regex> _patternCache = new();
```

---

### 11. ⚠️ ModId Property Calls GetManifest()

**Problem**: `IModSource.ModId` property calls `GetManifest()` which is expensive (JSON deserialization).

**Location**: Design document lines 241-248, 516-523

**Impact**: **PERFORMANCE** - Expensive operation in property getter

**Fix**: Cache mod ID after first manifest load:
```csharp
private string? _cachedModId;

public string ModId
{
    get
    {
        if (_cachedModId == null)
        {
            _cachedModId = GetManifest().Id;
        }
        return _cachedModId;
    }
}
```

---

### 12. ⚠️ Lock Contention in ArchiveModSource

**Problem**: Single lock (`_lock`) used for both TOC loading and file reading, causing contention.

**Location**: Design document lines 214, 255-267, 344-375

**Impact**: **PERFORMANCE** - Serialized access to different files

**Fix**: Use separate locks or reader-writer lock:
```csharp
private readonly ReaderWriterLockSlim _tocLock = new();
private readonly object _readLock = new();

private Dictionary<string, FileEntry> GetTOC()
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
        // Double-check
        if (_toc != null)
            return _toc;
        
        // Load TOC
        _toc = LoadTOC();
        return _toc;
    }
    finally
    {
        _tocLock.ExitWriteLock();
    }
}

public byte[] ReadFile(string relativePath)
{
    var toc = GetTOC(); // Uses reader lock
    // ... get entry ...
    
    lock (_readLock) // Separate lock for file I/O
    {
        // Read file
    }
}
```

---

### 13. ⚠️ Memory Allocations in ReadFile()

**Problem**: `ReadFile()` allocates temporary buffers for compressed/decompressed data.

**Location**: Design document lines 351-374

**Impact**: **PERFORMANCE** - GC pressure

**Fix**: Use `ArrayPool<byte>` for temporary buffers:
```csharp
using System.Buffers;

public byte[] ReadFile(string relativePath)
{
    // ... get entry ...
    
    var compressedPooled = ArrayPool<byte>.Shared.Rent((int)entry.CompressedSize);
    var decompressedPooled = ArrayPool<byte>.Shared.Rent((int)entry.UncompressedSize);
    
    try
    {
        // Read compressed data
        _stream.Read(compressedPooled, 0, (int)entry.CompressedSize);
        
        // Decompress
        var decoded = LZ4Codec.Decode(
            compressedPooled.AsSpan(0, (int)entry.CompressedSize),
            decompressedPooled.AsSpan(0, (int)entry.UncompressedSize)
        );
        
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
```

---

### 14. ⚠️ TOC Loaded Lazily But Could Be Eager

**Problem**: TOC loaded on first access, but could be loaded during mod discovery for better error handling.

**Location**: Design document lines 250-268

**Impact**: **PERFORMANCE** - Delayed error detection

**Fix**: Load TOC during discovery (in `TryLoadModSource()`):
```csharp
private bool TryLoadModSource(IModSource source, List<string> errors, out ModManifest manifest)
{
    try
    {
        // Force TOC load for archives (validates archive integrity)
        if (source is ArchiveModSource archiveSource)
        {
            _ = archiveSource.GetTOC(); // Trigger TOC load, catch errors
        }
        
        manifest = source.GetManifest();
        // ... rest of validation ...
    }
    catch (Exception ex)
    {
        errors.Add($"Error loading mod from '{source.SourcePath}': {ex.Message}");
        return false;
    }
}
```

---

## Other Issues

### 15. ⚠️ Missing Using Statements

**Problem**: Code examples missing `using` statements (e.g., `System.Text`, `System.Text.RegularExpressions`).

**Location**: Throughout design document

**Impact**: **MINOR** - Code won't compile as-is

**Fix**: Add all required `using` statements to code examples.

---

### 16. ⚠️ FileEntry Class Not Accessible for Testing

**Problem**: `FileEntry` is private nested class, making testing difficult.

**Location**: Design document lines 475-481

**Impact**: **TESTABILITY** - Can't test TOC structure independently

**Fix**: Make `FileEntry` public or internal:
```csharp
internal class FileEntry // or public
{
    public string Path { get; set; } = string.Empty;
    public ulong UncompressedSize { get; set; }
    public ulong CompressedSize { get; set; }
    public ulong DataOffset { get; set; }
}
```

---

### 17. ⚠️ No Handling for Empty Files

**Problem**: Archive format doesn't specify how to handle empty files (0 bytes).

**Location**: Archive format specification

**Impact**: **EDGE CASE** - Empty files might cause issues

**Fix**: Document behavior: Empty files stored with `CompressedSize = 0`, `DataOffset` points to next file (or TOC).

---

### 18. ⚠️ No TOC Integrity Validation

**Problem**: No validation that TOC offsets/sizes are valid (within archive bounds).

**Location**: Design document `LoadTOC()` method

**Impact**: **SECURITY/RELIABILITY** - Corrupted archives could cause crashes

**Fix**: Validate TOC entries:
```csharp
private Dictionary<string, FileEntry> LoadTOC()
{
    // ... read TOC ...
    
    var archiveSize = _stream.Length;
    
    foreach (var entry in toc.Values)
    {
        // Validate offset
        if (entry.DataOffset >= archiveSize)
        {
            throw new InvalidDataException(
                $"Invalid TOC entry: DataOffset {entry.DataOffset} exceeds archive size {archiveSize}"
            );
        }
        
        // Validate compressed size
        if (entry.DataOffset + entry.CompressedSize > archiveSize)
        {
            throw new InvalidDataException(
                $"Invalid TOC entry: File extends beyond archive bounds"
            );
        }
        
        // Validate sizes are reasonable
        if (entry.UncompressedSize == 0 && entry.CompressedSize > 0)
        {
            throw new InvalidDataException("Invalid TOC entry: Compressed size > 0 but uncompressed size = 0");
        }
    }
    
    return toc;
}
```

---

### 19. ⚠️ DirectoryModSource.EnumerateFiles() SearchOption Not Fully Implemented

**Problem**: `DirectoryModSource.EnumerateFiles()` doesn't properly handle `SearchOption.TopDirectoryOnly`.

**Location**: Design document lines 567-577

**Impact**: **BUG** - Incorrect behavior for `TopDirectoryOnly`

**Fix**: Filter by directory depth:
```csharp
public IEnumerable<string> EnumerateFiles(string searchPattern, SearchOption searchOption)
{
    var files = Directory.GetFiles(_modDirectory, searchPattern, searchOption);
    var modDirFullPath = Path.GetFullPath(_modDirectory);
    
    foreach (var file in files)
    {
        var relativePath = Path.GetRelativePath(modDirFullPath, file);
        
        if (searchOption == SearchOption.TopDirectoryOnly)
        {
            // Only include files in root directory
            if (relativePath.Contains('/') || relativePath.Contains('\\'))
                continue;
        }
        
        yield return relativePath.Replace('\\', '/');
    }
}
```

---

### 20. ⚠️ Thread Safety: _cachedManifest Race Condition

**Problem**: `_cachedManifest` could have race condition if accessed concurrently before first load.

**Location**: Design document lines 213, 422-452

**Impact**: **THREAD SAFETY** - Potential duplicate manifest loading

**Fix**: Use double-checked locking:
```csharp
public ModManifest GetManifest()
{
    if (_cachedManifest != null)
        return _cachedManifest;
    
    lock (_lock)
    {
        if (_cachedManifest != null)
            return _cachedManifest;
        
        // Load manifest
        _cachedManifest = LoadManifest();
        return _cachedManifest;
    }
}
```

---

### 21. ⚠️ ArchiveModSource.Dispose() Doesn't Check _disposed Before Lock

**Problem**: `Dispose()` locks before checking `_disposed`, which could cause issues if called multiple times.

**Location**: Design document lines 460-473

**Impact**: **MINOR** - Unnecessary locking

**Fix**: Check `_disposed` before locking:
```csharp
public void Dispose()
{
    if (_disposed)
        return;
    
    lock (_lock)
    {
        if (_disposed)
            return;
        
        _stream?.Dispose();
        _stream = null;
        _toc = null;
        _cachedManifest = null;
        _disposed = true;
    }
}
```

---

### 22. ⚠️ Missing IDisposable on DirectoryModSource

**Problem**: `DirectoryModSource` doesn't implement `IDisposable` for consistency, even though it doesn't need it.

**Impact**: **CONSISTENCY** - Inconsistent interface implementation

**Fix**: Implement `IDisposable` (empty implementation):
```csharp
public class DirectoryModSource : IModSource, IDisposable
{
    public void Dispose()
    {
        // Nothing to dispose, but implement for consistency
    }
}
```

---

### 23. ⚠️ Archive Creation Tool: Missing Error Handling

**Problem**: `ArchiveCreator.CreateArchive()` doesn't handle errors (file access, compression failures, etc.).

**Location**: Design document lines 876-940

**Impact**: **RELIABILITY** - Tool could crash on errors

**Fix**: Add try-catch and proper error handling:
```csharp
public void CreateArchive(string modDirectory, string outputPath, int compressionLevel = 1)
{
    if (!Directory.Exists(modDirectory))
    {
        throw new DirectoryNotFoundException($"Mod directory not found: {modDirectory}");
    }
    
    try
    {
        // ... archive creation ...
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
```

---

## Summary of Critical Issues

### Must Fix Before Implementation:

1. ❌ **ModLoader cannot load definitions from archives** (Critical)
2. ❌ **ModValidator cannot validate archives** (Critical)
3. ❌ **ResourcePathResolver fallback code violates .cursorrules** (Critical)
4. ❌ **Inconsistent path return types** (Architecture)

### Should Fix:

5. ❌ Path normalization duplicated (DRY)
6. ❌ Manifest loading duplicated (DRY)
7. ⚠️ FileStream kept open indefinitely (Performance)
8. ⚠️ Inefficient FileExists() (Performance)
9. ⚠️ Regex created per call (Performance)
10. ⚠️ ModId property calls GetManifest() (Performance)
11. ⚠️ Lock contention (Performance)
12. ⚠️ Memory allocations (Performance)
13. ⚠️ No TOC integrity validation (Reliability)

### Nice to Fix:

14. ⚠️ Missing using statements
15. ⚠️ FileEntry not accessible for testing
16. ⚠️ No handling for empty files
17. ⚠️ SearchOption not fully implemented
18. ⚠️ Thread safety issues
19. ⚠️ Missing IDisposable consistency
20. ⚠️ Archive creation tool error handling

---

## Recommended Action Plan

1. **Phase 1**: Fix critical architecture issues (#1, #2, #3, #4)
2. **Phase 2**: Fix DRY violations (#5, #6)
3. **Phase 3**: Address performance issues (#7-#13)
4. **Phase 4**: Fix remaining issues (#14-#23)

---

## References

- Original Design: `docs/design/COMPRESSED_MOD_SUPPORT_DESIGN.md`
- Current ModLoader: `MonoBall.Core/Mods/ModLoader.cs`
- Current ResourcePathResolver: `MonoBall.Core/Resources/ResourcePathResolver.cs`
- .cursorrules: No fallback code, fail-fast principles

