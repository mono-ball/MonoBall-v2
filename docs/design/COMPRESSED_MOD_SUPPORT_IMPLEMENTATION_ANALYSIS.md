# Compressed Mod Support Implementation Analysis

## Overview

This document analyzes the compressed mod support implementation for architecture issues, bugs, SOLID/DRY violations, and async/multithreading problems.

---

## 🔴 CRITICAL ISSUES

### 1. Resource Leak: IModSource Instances Never Disposed

**Location**: `ModLoader.DiscoverMods()`, `ModDiscovery.DiscoverModSources()`

**Issue**: 
- `ModDiscovery.DiscoverModSources()` creates `DirectoryModSource` and `ArchiveModSource` instances
- These instances are stored in `ModManifest.ModSource` property
- `ArchiveModSource` holds a `FileStream` that remains open indefinitely
- `DirectoryModSource` and `ArchiveModSource` implement `IDisposable` but are never disposed
- `ModLoader` and `ModManager` don't track or dispose these instances

**Impact**: 
- File handles remain open (especially problematic for `ArchiveModSource`)
- Memory leaks from undisposed resources
- Potential file locking issues on Windows

**Fix**:
```csharp
// Option 1: ModManager should track and dispose IModSource instances
public class ModManager : IDisposable
{
    private readonly List<IModSource> _modSources = new();
    
    public void Dispose()
    {
        foreach (var source in _modSources)
        {
            source?.Dispose();
        }
    }
}

// Option 2: Make ModManifest disposable and dispose ModSource
public class ModManifest : IDisposable
{
    public void Dispose()
    {
        ModSource?.Dispose();
    }
}
```

---

### 2. Potential Deadlock in ArchiveModSource.GetManifest()

**Location**: `ArchiveModSource.GetManifest()` (line 394-420)

**Issue**:
- `GetManifest()` acquires `_readLock` (line 401)
- Then calls `ReadTextFile("mod.json")` (line 416)
- `ReadTextFile()` calls `ReadFile()` (line 314)
- `ReadFile()` also tries to acquire `_readLock` (line 255)
- This causes a deadlock: same thread tries to acquire the same lock twice

**Impact**: Deadlock when `GetManifest()` is called

**Fix**:
```csharp
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

        if (!FileExists("mod.json"))
        {
            throw new InvalidOperationException(
                $"mod.json not found in archive '{_archivePath}'"
            );
        }

        // Read file WITHOUT calling ReadTextFile (which would deadlock)
        // Instead, read directly:
        var toc = GetTOC();
        var normalizedPath = ModPathNormalizer.Normalize("mod.json");
        
        if (!toc.TryGetValue(normalizedPath, out var entry))
        {
            throw new InvalidOperationException(
                $"mod.json not found in archive '{_archivePath}'"
            );
        }

        // Read and decompress directly (duplicate ReadFile logic but without lock)
        // OR: Extract ReadFileInternal() method that doesn't lock
        var jsonBytes = ReadFileInternal(entry); // New method without lock
        var jsonContent = Encoding.UTF8.GetString(jsonBytes);
        _cachedManifest = ModManifestLoader.LoadFromJson(jsonContent, this, _archivePath);

        return _cachedManifest;
    }
}
```

---

### 3. Race Condition in ArchiveModSource.ModId Property

**Location**: `ArchiveModSource.ModId` (line 59-72)

**Issue**:
- `ModId` property calls `GetManifest()` without synchronization
- `GetManifest()` uses double-checked locking internally
- However, `_cachedModId` is set AFTER `GetManifest()` returns
- Multiple threads could call `GetManifest()` simultaneously before `_cachedModId` is set
- This causes redundant manifest loading

**Impact**: Performance degradation, redundant work

**Fix**:
```csharp
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

            var manifest = GetManifest(); // This will also lock, but double-checked locking handles it
            _cachedModId = manifest.Id;
            return _cachedModId;
        }
    }
}
```

---

### 4. Inconsistent FileExists() Behavior on Disposal

**Location**: `DirectoryModSource.FileExists()` vs `ArchiveModSource.FileExists()`

**Issue**:
- `DirectoryModSource.FileExists()` throws `ObjectDisposedException` when disposed (line 126-129)
- `ArchiveModSource.FileExists()` returns `false` when disposed (line 325-328)
- Both implement `IModSource` interface, but behavior differs
- This violates the Liskov Substitution Principle (LSP)

**Impact**: Inconsistent behavior, potential bugs

**Fix**: Make both consistent - either both throw or both return false. Recommendation: both should throw for fail-fast behavior per .cursorrules.

```csharp
// ArchiveModSource.FileExists() - make consistent
public bool FileExists(string relativePath)
{
    if (_disposed)
    {
        throw new ObjectDisposedException(nameof(ArchiveModSource));
    }
    // ... rest of implementation
}
```

---

## 🟡 ARCHITECTURE ISSUES

### 5. TOC Dictionary Exposed Without Protection

**Location**: `ArchiveModSource.GetTOC()` (line 87-125)

**Issue**:
- `GetTOC()` returns a direct reference to `_toc` dictionary
- While the dictionary itself is read-only after creation, the reference could be stored and used after disposal
- No protection against accessing disposed state through stored TOC reference

**Impact**: Potential `NullReferenceException` or access to disposed resources

**Fix**: Return a read-only wrapper or copy:
```csharp
internal IReadOnlyDictionary<string, FileEntry> GetTOC()
{
    // ... existing lock logic ...
    return new ReadOnlyDictionary<string, FileEntry>(_toc);
}
```

---

### 6. ModDiscovery Creates Instances Without Ownership

**Location**: `ModDiscovery.DiscoverModSources()` (line 20-55)

**Issue**:
- `ModDiscovery` creates `IModSource` instances but has no way to dispose them
- Caller (`ModLoader`) doesn't track these instances for disposal
- Static utility method creates disposable resources without lifecycle management

**Impact**: Resource leaks, unclear ownership

**Fix**: 
- Option 1: Return `(IModSource source, bool isValid)` tuples and let caller manage disposal
- Option 2: Make `ModDiscovery` non-static and track instances
- Option 3: Document that caller must dispose instances (current approach, but needs documentation)

---

## 🟡 SOLID/DRY VIOLATIONS

### 7. Duplicated Path Filtering Logic

**Location**: 
- `ModLoader.LoadModDefinitions()` (line 273-281)
- `ModValidator.CollectDefinitionIds()` (line 274-281)

**Issue**:
- Same path filtering logic duplicated:
```csharp
.Where(p =>
    p.StartsWith(normalizedPath + "/", StringComparison.Ordinal)
    || p == normalizedPath
    || p.StartsWith(normalizedPath + "\\", StringComparison.Ordinal)
)
```

**Impact**: Code duplication, maintenance burden

**Fix**: Extract to utility method:
```csharp
public static class ModPathFilter
{
    public static IEnumerable<string> FilterByContentFolder(
        IEnumerable<string> paths,
        string contentFolderPath
    )
    {
        var normalized = ModPathNormalizer.Normalize(contentFolderPath);
        return paths.Where(p =>
            p.StartsWith(normalized + "/", StringComparison.Ordinal)
            || p == normalized
            || p.StartsWith(normalized + "\\", StringComparison.Ordinal)
        );
    }
}
```

---

### 8. ParseModPath() Should Be Utility

**Location**: `ResourceManager.ParseModPath()` (line 1112-1150)

**Issue**:
- `ParseModPath()` is a private method in `ResourceManager`
- Logic is specific to path parsing, not resource management
- Could be reused elsewhere

**Impact**: Violates Single Responsibility Principle

**Fix**: Move to `ModPathNormalizer` or create `ModPathParser` utility:
```csharp
public static class ModPathParser
{
    public static (string modId, string relativePath) ParseModPath(string virtualPath)
    {
        // ... existing logic ...
    }
}
```

---

### 9. ModId Property Logic Duplicated

**Location**: 
- `DirectoryModSource.ModId` (line 49-62)
- `ArchiveModSource.ModId` (line 59-72)

**Issue**: Identical caching logic in both classes

**Impact**: Code duplication

**Fix**: Extract to base class or interface default implementation (C# 8.0+):
```csharp
public interface IModSource
{
    string ModId { get; }
    
    ModManifest GetManifest();
}

// Default implementation (C# 8.0+)
public static class ModSourceExtensions
{
    private static readonly Dictionary<IModSource, string> _modIdCache = new();
    private static readonly object _cacheLock = new();
    
    public static string GetCachedModId(this IModSource source)
    {
        lock (_cacheLock)
        {
            if (_modIdCache.TryGetValue(source, out var cached))
            {
                return cached;
            }
            
            var modId = source.GetManifest().Id;
            _modIdCache[source] = modId;
            return modId;
        }
    }
}
```

---

## 🟡 MULTITHREADING ISSUES

### 10. FileStream Not Thread-Safe for Concurrent Reads

**Location**: `ArchiveModSource.ReadFile()` (line 255-303)

**Issue**:
- `FileStream` is not thread-safe for concurrent reads
- Multiple threads calling `ReadFile()` will serialize on `_readLock`
- This defeats the purpose of having separate locks for TOC vs file reading
- `FileStream` should support concurrent reads (it does on Windows, but not guaranteed)

**Impact**: Performance degradation, potential data corruption

**Fix**: 
- Option 1: Use `FileStream` with `FileShare.Read` and ensure thread-safe position management
- Option 2: Create new `FileStream` for each read (expensive but safe)
- Option 3: Use memory-mapped files for better concurrency

```csharp
// Option 2: Create stream per read (safer but slower)
lock (_readLock)
{
    using (var stream = File.OpenRead(_archivePath))
    {
        stream.Position = (long)entry.DataOffset;
        // ... read logic ...
    }
}
```

---

### 11. BinaryReader Not Thread-Safe

**Location**: `ArchiveModSource.LoadTOC()` (line 134)

**Issue**:
- `BinaryReader` wraps `FileStream` and is not thread-safe
- `LoadTOC()` is called from `GetTOC()` which uses `ReaderWriterLockSlim`
- However, `BinaryReader` instance is created fresh each time, so this is actually safe
- **Note**: This is actually fine, but worth documenting

**Impact**: None (current implementation is safe)

---

### 12. Dictionary Enumeration Not Thread-Safe

**Location**: `ArchiveModSource.EnumerateFiles()` (line 369)

**Issue**:
- `EnumerateFiles()` gets TOC dictionary and enumerates `toc.Keys`
- While TOC is read-only after creation, enumeration could fail if dictionary is modified during enumeration
- However, TOC is never modified after creation, so this is safe
- **Note**: This is actually fine, but worth documenting

**Impact**: None (current implementation is safe)

---

## 🟡 BUGS

### 13. GetManifest() May Fail If TOC Not Loaded

**Location**: `ArchiveModSource.GetManifest()` (line 409)

**Issue**:
- `GetManifest()` calls `FileExists("mod.json")` (line 409)
- `FileExists()` calls `GetTOC()` (line 335)
- `GetTOC()` loads TOC if not already loaded
- However, if `GetManifest()` is called before any file operations, TOC might not be loaded
- Actually, `FileExists()` will load TOC, so this is fine
- **Note**: This is actually fine, but the call chain is complex

**Impact**: None (works correctly, but complex)

---

### 14. Path Normalization Inconsistency

**Location**: `ModPathNormalizer.Normalize()` vs `Directory.GetRelativePath()`

**Issue**:
- `DirectoryModSource.EnumerateFiles()` uses `Path.GetRelativePath()` which may return paths with backslashes on Windows
- Then normalizes with `ModPathNormalizer.Normalize()`
- `ArchiveModSource.EnumerateFiles()` returns paths that are already normalized
- Both should be consistent

**Impact**: Potential path matching issues

**Fix**: Ensure all paths are normalized consistently:
```csharp
// DirectoryModSource.EnumerateFiles() - already does this correctly
var relativePath = Path.GetRelativePath(_directoryPath, file);
var normalized = ModPathNormalizer.Normalize(relativePath); // ✅ Correct
```

---

### 15. Empty File Handling Inconsistency

**Location**: `ArchiveModSource.ReadFile()` (line 250-252)

**Issue**:
- Empty files (0 bytes) return `Array.Empty<byte>()`
- This is correct, but `ReadTextFile()` will return empty string `""`
- Both behaviors are correct, but worth documenting

**Impact**: None (correct behavior)

---

## 🟡 ASYNC ISSUES

### 16. All I/O Operations Are Synchronous

**Location**: All `IModSource` implementations

**Issue**:
- `ReadFile()`, `ReadTextFile()`, `FileExists()`, `EnumerateFiles()` are all synchronous
- In async contexts, these will block threads
- No async versions available

**Impact**: Thread pool starvation in async contexts

**Fix**: Add async versions:
```csharp
public interface IModSource
{
    Task<byte[]> ReadFileAsync(string relativePath);
    Task<string> ReadTextFileAsync(string relativePath);
    Task<bool> FileExistsAsync(string relativePath);
    IAsyncEnumerable<string> EnumerateFilesAsync(string searchPattern, SearchOption searchOption);
}
```

---

## 📋 SUMMARY

### Critical Issues (Must Fix)
1. ✅ Resource leak: IModSource instances never disposed
2. ✅ Deadlock in ArchiveModSource.GetManifest()
3. ✅ Race condition in ArchiveModSource.ModId
4. ✅ Inconsistent FileExists() behavior

### Architecture Issues (Should Fix)
5. TOC dictionary exposed without protection
6. ModDiscovery creates instances without ownership

### SOLID/DRY Violations (Should Fix)
7. Duplicated path filtering logic
8. ParseModPath() should be utility
9. ModId property logic duplicated

### Multithreading Issues (Consider Fixing)
10. FileStream not thread-safe for concurrent reads
11. BinaryReader thread safety (actually fine)
12. Dictionary enumeration thread safety (actually fine)

### Bugs (Minor)
13. GetManifest() call chain complexity (actually fine)
14. Path normalization consistency (verify)
15. Empty file handling (actually fine)

### Async Issues (Future Enhancement)
16. All I/O operations are synchronous

---

## RECOMMENDED FIX PRIORITY

1. **P0 (Critical)**: Fix resource leaks (#1) and deadlock (#2)
2. **P1 (High)**: Fix race condition (#3) and inconsistent behavior (#4)
3. **P2 (Medium)**: Extract duplicated code (#7, #8, #9)
4. **P3 (Low)**: Improve thread safety (#10)
5. **P4 (Future)**: Add async support (#16)

