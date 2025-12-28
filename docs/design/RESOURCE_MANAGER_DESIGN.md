# Resource Manager System Design

**Generated:** 2025-01-16  
**Updated:** 2025-01-16 (Full Unification Approach)  
**Status:** Design Proposal - Full Unification  
**Scope:** Unified resource loading system for MonoBall

---

## Executive Summary

This document proposes a **fully unified `ResourceManager` system** to consolidate all resource loading logic across the codebase. Currently, path resolution, mod manifest lookup, file loading, and caching logic is duplicated across `SpriteLoaderService`, `TilesetLoaderService`, `FontService`, `AudioContentLoader`, `ShaderLoader`, and various other locations.

The proposed solution introduces:
1. **ResourcePathResolver**: Centralized path resolution service
2. **ResourceManager**: Unified resource loading, caching, and lifecycle management service
3. **Type-specific resource loaders**: Internal loaders for each resource type

This design follows **Option 2: Full Unification** for the best architecture, eliminating all duplicated code and providing a single, consistent API for resource management.

---

## Current State Analysis

### Duplicated Logic Pattern

All resource loaders follow a similar pattern for resolving and loading resources:

1. **Get Definition**: `_modManager.GetDefinition<T>(resourceId)`
2. **Get Metadata**: `_modManager.GetDefinitionMetadata(resourceId)`
3. **Get Mod Manifest**: `_modManager.GetModManifest(metadata.OriginalModId)` or `GetModManifestByDefinitionId(resourceId)`
4. **Resolve Path**: `Path.Combine(modManifest.ModDirectory, definition.RelativePath)`
5. **Normalize Path**: `Path.GetFullPath(resolvedPath)`
6. **Validate File**: `File.Exists(fullPath)`
7. **Load Resource**: Load file and cache (varies by resource type)

### Services with Duplicated Logic

#### 1. SpriteLoaderService (`Maps/SpriteLoaderService.cs`)
- **Purpose**: Loads sprite textures and definitions
- **Duplicated Logic**: Path resolution for `definition.TexturePath`, caching
- **Cache**: `Dictionary<string, Texture2D>` for textures, `Dictionary<string, SpriteDefinition>` for definitions
- **Dependencies**: `GraphicsDevice`, `IModManager`, `ILogger`

#### 2. TilesetLoaderService (`Maps/TilesetLoaderService.cs`)
- **Purpose**: Loads tileset textures and definitions
- **Duplicated Logic**: Path resolution for `definition.TexturePath` (identical to SpriteLoaderService), caching
- **Cache**: `Dictionary<string, Texture2D>` for textures, `Dictionary<string, TilesetDefinition>` for definitions
- **Dependencies**: `GraphicsDevice`, `IModManager`, `ILogger`

#### 3. FontService (`Rendering/FontService.cs`)
- **Purpose**: Loads font files and creates FontSystem instances
- **Duplicated Logic**: Path resolution for `definition.FontPath`, caching
- **Cache**: `Dictionary<string, FontSystem>`
- **Dependencies**: `IModManager`, `GraphicsDevice`, `ILogger`

#### 4. AudioContentLoader (`Audio/AudioContentLoader.cs`)
- **Purpose**: Creates VorbisReader instances for audio files
- **Duplicated Logic**: Path resolution for `definition.AudioPath`
- **Cache**: None (creates new reader each time)
- **Dependencies**: `IModManager`, `ILogger`

#### 5. ShaderLoader (`Rendering/ShaderLoader.cs`) + ShaderService (`Rendering/ShaderService.cs`)
- **Purpose**: Loads compiled shader bytecode
- **Duplicated Logic**: Path resolution for `definition.SourceFile`
- **Cache**: Managed by `ShaderService` (LRU cache)
- **Dependencies**: `GraphicsDevice`, `ILogger`, `IModManager`

#### 6. MessageBoxSceneSystem (`Scenes/Systems/MessageBoxSceneSystem.cs`)
- **Method**: `LoadTextureFromDefinition()`
- **Duplicated Logic**: Full path resolution pattern for textures

### Common Issues

1. **Code Duplication**: Path resolution logic duplicated 6+ times
2. **Caching Duplication**: Multiple services implementing their own caching strategies
3. **Inconsistent Error Handling**: Some services return null, some throw exceptions
4. **Maintenance Burden**: Changes to path resolution or caching require updates in multiple places
5. **Testing Difficulty**: Hard to mock/test resource loading without duplicating path resolution logic

---

## Proposed Design - Full Unification

### Architecture Overview

The new system consists of three main components:

1. **ResourcePathResolver**: Centralized path resolution service
2. **ResourceManager**: Unified resource loading, caching, and management service
3. **Internal Resource Loaders**: Type-specific loaders (internal implementation detail)

```
┌─────────────────────────────────────────────────────────────┐
│                     ResourceManager                          │
│  ┌──────────────────┐  ┌──────────────────┐                │
│  │ ResourcePath     │  │ Internal         │                │
│  │ Resolver         │  │ Resource         │                │
│  │                  │  │ Loaders          │                │
│  └──────────────────┘  └──────────────────┘                │
│           │                    │                            │
│           └─────────┬──────────┘                            │
│                     │                                        │
│              ┌──────▼──────┐                                │
│              │   Unified   │                                │
│              │   Cache     │                                │
│              │  Manager    │                                │
│              └─────────────┘                                │
└─────────────────────────────────────────────────────────────┘
           │                    │                    │
           ▼                    ▼                    ▼
    ┌──────────┐         ┌──────────┐         ┌──────────┐
    │ Textures │         │  Fonts   │         │  Audio   │
    └──────────┘         └──────────┘         └──────────┘
```

### Component 1: ResourcePathResolver

**Purpose**: Centralizes path resolution logic for all resource types.

**Interface**:
```csharp
namespace MonoBall.Core.Resources
{
    /// <summary>
    /// Service for resolving resource file paths from mod definitions.
    /// Fails fast with exceptions per .cursorrules (no fallback code).
    /// </summary>
    public interface IResourcePathResolver
    {
        /// <summary>
        /// Resolves the full file path for a resource definition.
        /// Fails fast with exceptions per .cursorrules (no fallback code).
        /// </summary>
        /// <param name="resourceId">The resource definition ID.</param>
        /// <param name="relativePath">The relative path from the definition (e.g., TexturePath, FontPath).</param>
        /// <returns>The full absolute path.</returns>
        /// <exception cref="ArgumentException">Thrown when resourceId or relativePath is null/empty.</exception>
        /// <exception cref="InvalidOperationException">Thrown when mod manifest cannot be found.</exception>
        /// <exception cref="FileNotFoundException">Thrown when resolved file does not exist.</exception>
        string ResolveResourcePath(string resourceId, string relativePath);
        
        /// <summary>
        /// Gets the mod manifest that owns a resource definition.
        /// Fails fast with exception if not found (no fallback code).
        /// </summary>
        /// <param name="resourceId">The resource definition ID.</param>
        /// <returns>The mod manifest.</returns>
        /// <exception cref="ArgumentException">Thrown when resourceId is null/empty.</exception>
        /// <exception cref="InvalidOperationException">Thrown when mod manifest cannot be found.</exception>
        ModManifest GetResourceModManifest(string resourceId);
    }
}
```

**Implementation Details**:
- Encapsulates the common path resolution pattern
- Uses `IModManager.GetModManifestByDefinitionId()` for manifest lookup
- Handles path normalization with `Path.GetFullPath()`
- **Fail-fast design**: Throws exceptions instead of returning null (per .cursorrules)
- **No fallback code**: Uses single lookup method, fails if not found

### Component 2: ResourceManager (Unified Service)

**Purpose**: Unified resource loading, caching, and lifecycle management for all resource types.

**Key Design Decisions**:
- **Single resourceId parameter**: ResourceManager looks up definitions internally, callers only provide resourceId
- **Type detection**: Uses `DefinitionMetadata.DefinitionType` to determine resource type
- **Unified caching**: Single cache manager for all resource types
- **Fail-fast**: Throws exceptions on errors (per .cursorrules)

**Interface**:
```csharp
namespace MonoBall.Core.Resources
{
    /// <summary>
    /// Unified resource manager for loading and caching all game resources.
    /// </summary>
    public interface IResourceManager
    {
        // Texture Loading (from SpriteDefinition or TilesetDefinition)
        Texture2D LoadTexture(string resourceId);
        Texture2D? GetCachedTexture(string resourceId);
        bool HasTexture(string resourceId);
        
        // Font Loading (from FontDefinition)
        FontSystem LoadFont(string resourceId);
        FontSystem? GetCachedFont(string resourceId);
        bool HasFont(string resourceId);
        
        // Audio Loading (from AudioDefinition)
        /// <summary>
        /// Loads an audio reader for the specified audio resource, caching it for reuse.
        /// The reader position is automatically reset to the beginning when returned from cache.
        /// </summary>
        /// <param name="resourceId">The audio resource ID.</param>
        /// <returns>The VorbisReader instance (cached if previously loaded, position reset to start).</returns>
        /// <remarks>
        /// <para>
        /// Cached readers are shared instances. The reader position is reset to the beginning
        /// each time it's returned from cache to ensure proper playback. For concurrent playback
        /// of the same audio, callers should manage their own reader instances.
        /// </para>
        /// </remarks>
        VorbisReader LoadAudioReader(string resourceId);
        VorbisReader? GetCachedAudioReader(string resourceId);
        bool HasAudio(string resourceId);
        
        // Shader Loading (from ShaderDefinition)
        Effect LoadShader(string resourceId);
        Effect? GetCachedShader(string resourceId);
        bool HasShader(string resourceId);
        
        // Cache Management
        void UnloadResource(string resourceId, ResourceType type);
        void UnloadAll(ResourceType? type = null);
        /// <summary>
        /// Clears the cache for the specified resource type(s). Equivalent to UnloadAll().
        /// </summary>
        /// <param name="type">The resource type to clear, or null for all types.</param>
        void ClearCache(ResourceType? type = null);
        
        // Definition Access (for services that need definition data, not just resources)
        T? GetDefinition<T>(string resourceId) where T : class;
    }
    
    /// <summary>
    /// Resource type enumeration for cache management.
    /// </summary>
    public enum ResourceType
    {
        Texture,
        Font,
        Audio,
        Shader
    }
}
```

**Implementation Strategy**:

1. **Dependency Injection**: Requires `GraphicsDevice`, `IModManager`, `IResourcePathResolver`, `ILogger`
2. **Definition Lookup**: Uses `IModManager.GetDefinition<T>()` and `GetDefinitionMetadata()` to determine resource type
3. **Path Extraction**: Extracts appropriate path property based on definition type:
   - `SpriteDefinition` / `TilesetDefinition`: `TexturePath`
   - `FontDefinition`: `FontPath`
   - `AudioDefinition`: `AudioPath`
   - `ShaderDefinition`: `SourceFile`
4. **Unified Caching**: Single cache manager with type-specific dictionaries
5. **LRU Eviction**: For textures, audio readers, and shaders (similar to current ShaderService)
6. **Disposal**: Implements `IDisposable` for proper cleanup
7. **Fail-Fast**: All Load methods throw exceptions on failure (no nullable returns for Load methods)

**Note**: Type detection logic is not needed in the current implementation since each Load method knows the expected definition type. Type detection could be added in the future for generic resource loading if needed.

### Component 3: Internal Resource Loaders

**Purpose**: Type-specific loading logic (internal implementation detail, not exposed).

These are private methods within `ResourceManager`:

- `Texture2D LoadTextureInternal(string fullPath)`: Uses `Texture2D.FromFile()`
- `FontSystem LoadFontInternal(string fullPath)`: Uses FontStashSharp with verification
- `VorbisReader LoadAudioReaderInternal(string fullPath)`: Creates new VorbisReader (not cached)
- `Effect LoadShaderInternal(string fullPath)`: Loads bytecode and creates Effect

---

## Detailed Design

### ResourcePathResolver Implementation

```csharp
using System;
using System.IO;
using MonoBall.Core.Mods;
using Serilog;

namespace MonoBall.Core.Resources
{
    /// <summary>
    /// Service for resolving resource file paths from mod definitions.
    /// </summary>
    public class ResourcePathResolver : IResourcePathResolver
    {
        private readonly IModManager _modManager;
        private readonly ILogger _logger;
        
        public ResourcePathResolver(IModManager modManager, ILogger logger)
        {
            _modManager = modManager ?? throw new ArgumentNullException(nameof(modManager));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }
        
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
            
            // Resolve path
            string fullPath = Path.Combine(modManifest.ModDirectory, relativePath);
            fullPath = Path.GetFullPath(fullPath);
            
            // Fail fast if file doesn't exist
            if (!File.Exists(fullPath))
            {
                throw new FileNotFoundException(
                    $"Resource file not found: {fullPath} (resource: {resourceId})",
                    fullPath
                );
            }
            
            return fullPath;
        }
        
        public ModManifest GetResourceModManifest(string resourceId)
        {
            if (string.IsNullOrEmpty(resourceId))
            {
                throw new ArgumentException("Resource ID cannot be null or empty.", nameof(resourceId));
            }
            
            // Use GetModManifestByDefinitionId - fail fast if not found (no fallback code)
            var modManifest = _modManager.GetModManifestByDefinitionId(resourceId);
            if (modManifest == null)
            {
                throw new InvalidOperationException(
                    $"Mod manifest not found for resource '{resourceId}'. " +
                    "Ensure the resource is defined in a loaded mod."
                );
            }
            
            return modManifest;
        }
    }
}
```

### ResourceManager Implementation

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using FontStashSharp;
using Microsoft.Xna.Framework.Graphics;
using MonoBall.Core.Audio.Core;
using MonoBall.Core.Maps;
using MonoBall.Core.Mods;
using MonoBall.Core.Mods.Definitions;
using MonoBall.Core.Rendering;
using Serilog;

namespace MonoBall.Core.Resources
{
    /// <summary>
    /// Unified resource manager for loading and caching all game resources.
    /// </summary>
    public class ResourceManager : IResourceManager, IDisposable
    {
        private readonly GraphicsDevice _graphicsDevice;
        private readonly IModManager _modManager;
        private readonly IResourcePathResolver _pathResolver;
        private readonly ILogger _logger;
        
        // Unified caches
        private readonly Dictionary<string, Texture2D> _textureCache = new();
        private readonly Dictionary<string, FontSystem> _fontCache = new();
        private readonly Dictionary<string, VorbisReader> _audioCache = new();
        private readonly Dictionary<string, Effect> _shaderCache = new();
        
        // LRU tracking for eviction
        private readonly LinkedList<string> _textureAccessOrder = new();
        private readonly LinkedList<string> _audioAccessOrder = new();
        private readonly LinkedList<string> _shaderAccessOrder = new();
        private readonly object _lock = new();
        private const int MaxTextureCacheSize = 100;
        private const int MaxAudioCacheSize = 50;
        private const int MaxShaderCacheSize = 20;
        
        private bool _disposed = false;
        
        public ResourceManager(
            GraphicsDevice graphicsDevice,
            IModManager modManager,
            IResourcePathResolver pathResolver,
            ILogger logger)
        {
            _graphicsDevice = graphicsDevice ?? throw new ArgumentNullException(nameof(graphicsDevice));
            _modManager = modManager ?? throw new ArgumentNullException(nameof(modManager));
            _pathResolver = pathResolver ?? throw new ArgumentNullException(nameof(pathResolver));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }
        
        public Texture2D LoadTexture(string resourceId)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ResourceManager));
            
            if (string.IsNullOrEmpty(resourceId))
            {
                throw new ArgumentException("Resource ID cannot be null or empty.", nameof(resourceId));
            }
            
            lock (_lock)
            {
                // Check cache first
                if (_textureCache.TryGetValue(resourceId, out var cached))
                {
                    // Update LRU
                    _textureAccessOrder.Remove(resourceId);
                    _textureAccessOrder.AddFirst(resourceId);
                    return cached;
                }
                
                // Get definition and extract path
                string relativePath = ExtractTexturePath(resourceId);
                
                // Resolve full path
                string fullPath = _pathResolver.ResolveResourcePath(resourceId, relativePath);
                
                // Load texture
                var texture = Texture2D.FromFile(_graphicsDevice, fullPath);
                
                // Evict LRU if at capacity
                if (_textureCache.Count >= MaxTextureCacheSize)
                {
                    string? lruKey = _textureAccessOrder.Last?.Value;
                    if (lruKey != null && _textureCache.TryGetValue(lruKey, out var lruTexture))
                    {
                        _textureCache.Remove(lruKey);
                        _textureAccessOrder.RemoveLast();
                        lruTexture.Dispose();
                        _logger.Debug("Evicted LRU texture from cache: {ResourceId}", lruKey);
                    }
                }
                
                // Add to cache
                _textureCache[resourceId] = texture;
                _textureAccessOrder.AddFirst(resourceId);
                _logger.Debug("Loaded and cached texture: {ResourceId}", resourceId);
                
                return texture;
            }
        }
        
        private string ExtractTexturePath(string resourceId)
        {
            // Try SpriteDefinition first
            var spriteDef = _modManager.GetDefinition<SpriteDefinition>(resourceId);
            if (spriteDef != null && !string.IsNullOrEmpty(spriteDef.TexturePath))
            {
                return spriteDef.TexturePath;
            }
            
            // Try TilesetDefinition
            var tilesetDef = _modManager.GetDefinition<TilesetDefinition>(resourceId);
            if (tilesetDef != null && !string.IsNullOrEmpty(tilesetDef.TexturePath))
            {
                return tilesetDef.TexturePath;
            }
            
            throw new InvalidOperationException(
                $"Texture definition not found or has no TexturePath: {resourceId}"
            );
        }
        
        public FontSystem LoadFont(string resourceId)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ResourceManager));
            
            if (string.IsNullOrEmpty(resourceId))
            {
                throw new ArgumentException("Resource ID cannot be null or empty.", nameof(resourceId));
            }
            
            lock (_lock)
            {
                // Check cache first
                if (_fontCache.TryGetValue(resourceId, out var cached))
                {
                    return cached;
                }
                
                // Get definition
                var fontDef = _modManager.GetDefinition<FontDefinition>(resourceId);
                if (fontDef == null || string.IsNullOrEmpty(fontDef.FontPath))
                {
                    throw new InvalidOperationException(
                        $"Font definition not found or has no FontPath: {resourceId}"
                    );
                }
                
                // Resolve full path
                string fullPath = _pathResolver.ResolveResourcePath(resourceId, fontDef.FontPath);
                
                // Load font (with verification like FontService)
                var fontSystem = new FontSystem();
                byte[] fontData = File.ReadAllBytes(fullPath);
                fontSystem.AddFont(fontData);
                
                // Verify font was added
                var testFont = fontSystem.GetFont(12);
                if (testFont == null)
                {
                    throw new InvalidOperationException(
                        $"Font file is invalid or corrupted: {resourceId}"
                    );
                }
                
                // Cache
                _fontCache[resourceId] = fontSystem;
                _logger.Debug("Loaded and cached font: {ResourceId}", resourceId);
                
                return fontSystem;
            }
        }
        
        public VorbisReader LoadAudioReader(string resourceId)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ResourceManager));
            
            if (string.IsNullOrEmpty(resourceId))
            {
                throw new ArgumentException("Resource ID cannot be null or empty.", nameof(resourceId));
            }
            
            lock (_lock)
            {
                // Check cache first
                if (_audioCache.TryGetValue(resourceId, out var cached))
                {
                    // CRITICAL: Reset position to start before returning cached reader
                    // This ensures cached readers always start from the beginning
                    // Note: Cached readers are shared instances. For concurrent playback,
                    // callers should create separate reader instances (not use cached ones).
                    cached.Reset();
                    
                    // Update LRU
                    _audioAccessOrder.Remove(resourceId);
                    _audioAccessOrder.AddFirst(resourceId);
                    return cached;
                }
                
                // Get definition
                var audioDef = _modManager.GetDefinition<AudioDefinition>(resourceId);
                if (audioDef == null || string.IsNullOrEmpty(audioDef.AudioPath))
                {
                    throw new InvalidOperationException(
                        $"Audio definition not found or has no AudioPath: {resourceId}"
                    );
                }
                
                // Resolve full path
                string fullPath = _pathResolver.ResolveResourcePath(resourceId, audioDef.AudioPath);
                
                // Create new reader
                var reader = new VorbisReader(fullPath);
                
                // Evict LRU if at capacity
                if (_audioCache.Count >= MaxAudioCacheSize)
                {
                    string? lruKey = _audioAccessOrder.Last?.Value;
                    if (lruKey != null && _audioCache.TryGetValue(lruKey, out var lruReader))
                    {
                        _audioCache.Remove(lruKey);
                        _audioAccessOrder.RemoveLast();
                        lruReader.Dispose();
                        _logger.Debug("Evicted LRU audio reader from cache: {ResourceId}", lruKey);
                    }
                }
                
                // Add to cache
                _audioCache[resourceId] = reader;
                _audioAccessOrder.AddFirst(resourceId);
                _logger.Debug("Loaded and cached audio reader: {ResourceId}", resourceId);
                
                return reader;
            }
        }
        
        public Effect LoadShader(string resourceId)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ResourceManager));
            
            if (string.IsNullOrEmpty(resourceId))
            {
                throw new ArgumentException("Resource ID cannot be null or empty.", nameof(resourceId));
            }
            
            lock (_lock)
            {
                // Check cache first
                if (_shaderCache.TryGetValue(resourceId, out var cached))
                {
                    // Update LRU
                    _shaderAccessOrder.Remove(resourceId);
                    _shaderAccessOrder.AddFirst(resourceId);
                    return cached;
                }
                
                // Get definition
                var shaderDef = _modManager.GetDefinition<ShaderDefinition>(resourceId);
                if (shaderDef == null || string.IsNullOrEmpty(shaderDef.SourceFile))
                {
                    throw new InvalidOperationException(
                        $"Shader definition not found or has no SourceFile: {resourceId}"
                    );
                }
                
                // Resolve full path
                string fullPath = _pathResolver.ResolveResourcePath(resourceId, shaderDef.SourceFile);
                
                // Load shader bytecode
                byte[] bytecode = File.ReadAllBytes(fullPath);
                var effect = new Effect(_graphicsDevice, bytecode);
                
                // Evict LRU if at capacity
                if (_shaderCache.Count >= MaxShaderCacheSize)
                {
                    string? lruKey = _shaderAccessOrder.Last?.Value;
                    if (lruKey != null && _shaderCache.TryGetValue(lruKey, out var lruShader))
                    {
                        _shaderCache.Remove(lruKey);
                        _shaderAccessOrder.RemoveLast();
                        lruShader.Dispose();
                        _logger.Debug("Evicted LRU shader from cache: {ResourceId}", lruKey);
                    }
                }
                
                // Add to cache
                _shaderCache[resourceId] = effect;
                _shaderAccessOrder.AddFirst(resourceId);
                _logger.Debug("Loaded and cached shader: {ResourceId}", resourceId);
                
                return effect;
            }
        }
        
        public T? GetDefinition<T>(string resourceId) where T : class
        {
            return _modManager.GetDefinition<T>(resourceId);
        }
        
        public Texture2D? GetCachedTexture(string resourceId)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ResourceManager));
            
            lock (_lock)
            {
                return _textureCache.TryGetValue(resourceId, out var texture) ? texture : null;
            }
        }
        
        public bool HasTexture(string resourceId)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ResourceManager));
            
            lock (_lock)
            {
                return _textureCache.ContainsKey(resourceId);
            }
        }
        
        public FontSystem? GetCachedFont(string resourceId)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ResourceManager));
            
            lock (_lock)
            {
                return _fontCache.TryGetValue(resourceId, out var font) ? font : null;
            }
        }
        
        public bool HasFont(string resourceId)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ResourceManager));
            
            lock (_lock)
            {
                return _fontCache.ContainsKey(resourceId);
            }
        }
        
        public VorbisReader? GetCachedAudioReader(string resourceId)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ResourceManager));
            
            lock (_lock)
            {
                return _audioCache.TryGetValue(resourceId, out var reader) ? reader : null;
            }
        }
        
        public bool HasAudio(string resourceId)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ResourceManager));
            
            lock (_lock)
            {
                return _audioCache.ContainsKey(resourceId);
            }
        }
        
        public Effect? GetCachedShader(string resourceId)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ResourceManager));
            
            lock (_lock)
            {
                return _shaderCache.TryGetValue(resourceId, out var shader) ? shader : null;
            }
        }
        
        public bool HasShader(string resourceId)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ResourceManager));
            
            lock (_lock)
            {
                return _shaderCache.ContainsKey(resourceId);
            }
        }
        
        public void UnloadResource(string resourceId, ResourceType type)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ResourceManager));
            
            lock (_lock)
            {
                switch (type)
                {
                    case ResourceType.Texture:
                        if (_textureCache.TryGetValue(resourceId, out var texture))
                        {
                            _textureCache.Remove(resourceId);
                            _textureAccessOrder.Remove(resourceId);
                            texture.Dispose();
                        }
                        break;
                    case ResourceType.Font:
                        if (_fontCache.TryGetValue(resourceId, out var font))
                        {
                            _fontCache.Remove(resourceId);
                            font.Dispose();
                        }
                        break;
                    case ResourceType.Audio:
                        if (_audioCache.TryGetValue(resourceId, out var reader))
                        {
                            _audioCache.Remove(resourceId);
                            _audioAccessOrder.Remove(resourceId);
                            reader.Dispose();
                        }
                        break;
                    case ResourceType.Shader:
                        if (_shaderCache.TryGetValue(resourceId, out var shader))
                        {
                            _shaderCache.Remove(resourceId);
                            _shaderAccessOrder.Remove(resourceId);
                            shader.Dispose();
                        }
                        break;
                }
            }
        }
        
        public void UnloadAll(ResourceType? type = null)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ResourceManager));
            
            lock (_lock)
            {
                if (type == null || type == ResourceType.Texture)
                {
                    foreach (var texture in _textureCache.Values)
                        texture.Dispose();
                    _textureCache.Clear();
                    _textureAccessOrder.Clear();
                }
                
                if (type == null || type == ResourceType.Font)
                {
                    foreach (var font in _fontCache.Values)
                        font.Dispose();
                    _fontCache.Clear();
                }
                
                if (type == null || type == ResourceType.Audio)
                {
                    foreach (var reader in _audioCache.Values)
                        reader.Dispose();
                    _audioCache.Clear();
                    _audioAccessOrder.Clear();
                }
                
                if (type == null || type == ResourceType.Shader)
                {
                    foreach (var shader in _shaderCache.Values)
                        shader.Dispose();
                    _shaderCache.Clear();
                    _shaderAccessOrder.Clear();
                }
            }
        }
        
        public void ClearCache(ResourceType? type = null)
        {
            // Same as UnloadAll - clears and disposes
            UnloadAll(type);
        }
        
        public void Dispose()
        {
            if (_disposed) return;
            
            lock (_lock)
            {
                // Dispose all cached resources
                foreach (var texture in _textureCache.Values)
                {
                    texture.Dispose();
                }
                _textureCache.Clear();
                _textureAccessOrder.Clear();
                
                foreach (var font in _fontCache.Values)
                {
                    font.Dispose();
                }
                _fontCache.Clear();
                
                foreach (var reader in _audioCache.Values)
                {
                    reader.Dispose();
                }
                _audioCache.Clear();
                _audioAccessOrder.Clear();
                
                foreach (var shader in _shaderCache.Values)
                {
                    shader.Dispose();
                }
                _shaderCache.Clear();
                _shaderAccessOrder.Clear();
            }
            
            _disposed = true;
        }
    }
}
```

---

## Migration Strategy

**Note**: Per .cursorrules, we do NOT maintain backward compatibility. All existing services will be replaced with `ResourceManager`.

### Phase 1: Create Core Infrastructure

1. Create `IResourcePathResolver` interface
2. Implement `ResourcePathResolver` class (fail-fast, no fallbacks)
3. Create `IResourceManager` interface
4. Implement `ResourceManager` class
5. Add unit tests for both services
6. Register services in `GameServices` or `MonoBallGame`

### Phase 2: Replace Existing Services

**Replace** (not refactor) all existing services:

1. **Remove `SpriteLoaderService`** → Use `ResourceManager.LoadTexture()`
2. **Remove `TilesetLoaderService`** → Use `ResourceManager.LoadTexture()`
3. **Remove `FontService`** → Use `ResourceManager.LoadFont()`
4. **Remove `AudioContentLoader`** → Use `ResourceManager.LoadAudioReader()`
5. **Remove `ShaderLoader`** → Use `ResourceManager.LoadShader()`
6. **Update `ShaderService`** → Use `ResourceManager.LoadShader()` internally
7. **Remove duplicate `LoadTextureFromDefinition`** in `MessageBoxSceneSystem`
8. **Update all call sites** to use `ResourceManager`

### Phase 3: Cleanup and Optimization

1. Remove old service files
2. Update integration tests
3. Performance benchmarking
4. Fine-tune cache sizes if needed

---

## API Migration Examples

### Before (SpriteLoaderService)
```csharp
var spriteLoader = services.GetService<ISpriteLoaderService>();
var texture = spriteLoader?.GetSpriteTexture(spriteId);
```

### After (ResourceManager)
```csharp
var resourceManager = services.GetService<IResourceManager>();
var texture = resourceManager.LoadTexture(spriteId);
```

### Before (FontService)
```csharp
var fontService = services.GetService<FontService>();
var fontSystem = fontService?.GetFontSystem(fontId);
```

### After (ResourceManager)
```csharp
var resourceManager = services.GetService<IResourceManager>();
var fontSystem = resourceManager.LoadFont(fontId);
```

### Before (AudioContentLoader)
```csharp
var audioLoader = services.GetService<IAudioContentLoader>();
var (definition, manifest) = GetAudioDefinitionAndManifest(audioId);
var reader = audioLoader?.CreateVorbisReader(audioId, definition, manifest);
```

### After (ResourceManager)
```csharp
var resourceManager = services.GetService<IResourceManager>();
var reader = resourceManager.LoadAudioReader(audioId);
```

---

## Benefits

1. **DRY Principle**: Single source of truth for all resource loading
2. **Unified Caching**: Consistent cache management strategy
3. **Simplified API**: One service, one pattern for all resources
4. **Maintainability**: Changes to resource loading only need to be made once
5. **Consistency**: All resources follow the same loading pattern
6. **Testability**: Resource loading can be mocked easily
7. **Performance**: Unified cache management, LRU eviction for large resource sets
8. **Error Handling**: Consistent fail-fast error handling across all resource types

---

## Considerations

### No Backward Compatibility (Per .cursorrules)

- Per .cursorrules, we do NOT maintain backward compatibility
- All existing services will be replaced, not refactored
- All call sites will be updated to use `ResourceManager`
- This ensures cleaner code and prevents maintenance of duplicate code paths

### Performance

- Unified cache management with LRU eviction for textures, audio readers, and shaders
- Fonts cached indefinitely (typically few fonts, unlimited cache size)
- Audio readers cached with LRU eviction (frequently reused sounds benefit from caching, position automatically reset)
- Path resolution overhead is minimal (single lookup)

### Resource-Specific Logic

- Font verification logic preserved (from FontService)
- Audio reader caching added (was not cached in AudioContentLoader, now cached for performance)
- Shader bytecode loading preserved (from ShaderLoader)
- All type-specific logic encapsulated within ResourceManager

### Audio Reader Caching

- Audio readers (`VorbisReader`) are cached with LRU eviction
- Cache size: 50 readers (configurable via `MaxAudioCacheSize`)
- Benefits: Frequently played sounds (e.g., UI sounds, footsteps) benefit from caching
- **Important**: Cached readers automatically have their position reset to the beginning when returned from cache (via `Reset()`)
- **Concurrent Playback**: Cached readers are shared instances. For true concurrent playback of the same audio, callers should manage separate reader instances (not use the cache)

### Definition Access

- `ResourceManager.GetDefinition<T>()` provides access to definitions for services that need definition data
- Example: Sprite animation systems need `SpriteDefinition` for frame data, not just textures

---

## Implementation Checklist

### Phase 1: Core Infrastructure
- [ ] Create `IResourcePathResolver` interface
- [ ] Implement `ResourcePathResolver` class (fail-fast, no fallbacks)
- [ ] Create `IResourceManager` interface
- [ ] Implement `ResourceManager` class
- [ ] Add unit tests for path resolution
- [ ] Add unit tests for resource loading
- [ ] Register `IResourcePathResolver` in `GameServices` or `MonoBallGame`
- [ ] Register `IResourceManager` in `GameServices` or `MonoBallGame`

### Phase 2: Replace Existing Services
- [ ] Update all call sites for `SpriteLoaderService` → `ResourceManager.LoadTexture()`
- [ ] Update all call sites for `TilesetLoaderService` → `ResourceManager.LoadTexture()`
- [ ] Update all call sites for `FontService` → `ResourceManager.LoadFont()`
- [ ] Update all call sites for `AudioContentLoader` → `ResourceManager.LoadAudioReader()`
- [ ] Update `ShaderService` to use `ResourceManager.LoadShader()`
- [ ] Remove duplicate `LoadTextureFromDefinition` in `MessageBoxSceneSystem`
- [ ] Remove `SpriteLoaderService` and `ISpriteLoaderService`
- [ ] Remove `TilesetLoaderService` and `ITilesetLoaderService`
- [ ] Remove `FontService`
- [ ] Remove `AudioContentLoader` and `IAudioContentLoader`
- [ ] Remove `ShaderLoader` (or keep minimal wrapper if needed)
- [ ] Update all tests

### Phase 3: Cleanup
- [ ] Remove old service files
- [ ] Update integration tests
- [ ] Performance benchmarking
- [ ] Fine-tune cache sizes if needed
- [ ] Update documentation

---

## References

- Current implementations:
  - `MonoBall.Core/Maps/SpriteLoaderService.cs`
  - `MonoBall.Core/Maps/TilesetLoaderService.cs`
  - `MonoBall.Core/Rendering/FontService.cs`
  - `MonoBall.Core/Audio/AudioContentLoader.cs`
  - `MonoBall.Core/Rendering/ShaderLoader.cs`
  - `MonoBall.Core/Rendering/ShaderService.cs`
  - `MonoBall.Core/Scenes/Systems/MessageBoxSceneSystem.cs` (LoadTextureFromDefinition method)

---

## Next Steps

1. Review and approve design
2. Begin Phase 1 implementation (core infrastructure)
3. Test ResourcePathResolver and ResourceManager thoroughly
4. Execute Phase 2 (replace all existing services)
5. Cleanup and optimization (Phase 3)
