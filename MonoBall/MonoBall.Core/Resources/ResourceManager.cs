using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FontStashSharp;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoBall.Core.Audio; // For AudioDefinition
using MonoBall.Core.Audio.Core;
using MonoBall.Core.ECS.Services;
using MonoBall.Core.Maps;
using MonoBall.Core.Mods;
using MonoBall.Core.Mods.Definitions;
using Serilog;

namespace MonoBall.Core.Resources
{
    /// <summary>
    /// Unified resource manager for loading and caching all game resources.
    /// </summary>
    public class ResourceManager : IResourceManager, IDisposable
    {
        /// <summary>
        /// Threshold for determining if frame duration is in milliseconds.
        /// Durations greater than this value are assumed to be in milliseconds and converted to seconds.
        /// </summary>
        private const double MillisecondsThreshold = 100.0;

        private readonly GraphicsDevice _graphicsDevice;
        private readonly IModManager _modManager;
        private readonly IResourcePathResolver _pathResolver;
        private readonly ILogger _logger;
        private readonly IVariableSpriteResolver? _variableSpriteResolver;

        // Unified caches
        private readonly Dictionary<string, Texture2D> _textureCache = new();
        private readonly Dictionary<string, FontSystem> _fontCache = new();

        // NOTE: Audio readers are NOT cached - VorbisReader is stateful and cannot be safely shared
        private readonly Dictionary<string, Effect> _shaderCache = new();
        private readonly Dictionary<string, SpriteDefinition> _spriteDefinitionCache = new();
        private readonly Dictionary<
            (string spriteId, string animationName),
            List<SpriteAnimationFrame>
        > _animationFrameCache = new();
        private readonly Dictionary<string, TilesetDefinition> _tilesetDefinitionCache = new();
        private readonly Dictionary<
            (string tilesetId, int localTileId),
            IReadOnlyList<TileAnimationFrame>
        > _tileAnimationCache = new();

        // LRU tracking for eviction
        private readonly LinkedList<string> _textureAccessOrder = new();
        private readonly LinkedList<string> _shaderAccessOrder = new();
        private readonly object _lock = new();

        private const int MaxTextureCacheSize = 100;
        private const int MaxShaderCacheSize = 20;

        private bool _disposed = false;

        /// <summary>
        /// Initializes a new instance of the ResourceManager.
        /// </summary>
        /// <param name="graphicsDevice">The graphics device for loading textures and shaders.</param>
        /// <param name="modManager">The mod manager for accessing definitions.</param>
        /// <param name="pathResolver">The resource path resolver.</param>
        /// <param name="logger">The logger for logging operations.</param>
        /// <param name="variableSpriteResolver">Optional variable sprite resolver for resolving variable sprite IDs.</param>
        /// <exception cref="ArgumentNullException">Thrown when any required parameter is null.</exception>
        public ResourceManager(
            GraphicsDevice graphicsDevice,
            IModManager modManager,
            IResourcePathResolver pathResolver,
            ILogger logger,
            IVariableSpriteResolver? variableSpriteResolver = null
        )
        {
            _graphicsDevice =
                graphicsDevice ?? throw new ArgumentNullException(nameof(graphicsDevice));
            _modManager = modManager ?? throw new ArgumentNullException(nameof(modManager));
            _pathResolver = pathResolver ?? throw new ArgumentNullException(nameof(pathResolver));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _variableSpriteResolver = variableSpriteResolver;
        }

        public Texture2D LoadTexture(string resourceId)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ResourceManager));

            if (string.IsNullOrEmpty(resourceId))
            {
                throw new ArgumentException(
                    "Resource ID cannot be null or empty.",
                    nameof(resourceId)
                );
            }

            // Fast path: check cache (with lock)
            lock (_lock)
            {
                if (_textureCache.TryGetValue(resourceId, out var cached))
                {
                    // Update LRU
                    _textureAccessOrder.Remove(resourceId);
                    _textureAccessOrder.AddFirst(resourceId);
                    return cached;
                }
            }

            // Slow path: load file OUTSIDE lock (file I/O should not block other threads)
            string relativePath = ExtractTexturePath(resourceId);
            string fullPath = _pathResolver.ResolveResourcePath(resourceId, relativePath);
            var texture = Texture2D.FromFile(_graphicsDevice, fullPath);

            // Update cache (acquire lock again)
            lock (_lock)
            {
                // Double-check: another thread might have loaded it while we were loading
                if (_textureCache.TryGetValue(resourceId, out var cached))
                {
                    texture.Dispose(); // Dispose our copy, use cached one
                    _textureAccessOrder.Remove(resourceId);
                    _textureAccessOrder.AddFirst(resourceId);
                    return cached;
                }

                // Evict LRU if at capacity
                EvictLRUTexture();

                // Add to cache
                _textureCache[resourceId] = texture;
                _textureAccessOrder.AddFirst(resourceId);
                _logger.Debug("Loaded and cached texture: {ResourceId}", resourceId);

                return texture;
            }
        }

        /// <summary>
        /// Evicts the least recently used texture from cache. Must be called within lock.
        /// </summary>
        private void EvictLRUTexture()
        {
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
                throw new ArgumentException(
                    "Resource ID cannot be null or empty.",
                    nameof(resourceId)
                );
            }

            // Fast path: check cache (with lock)
            lock (_lock)
            {
                if (_fontCache.TryGetValue(resourceId, out var cached))
                {
                    return cached;
                }
            }

            // Slow path: load file OUTSIDE lock
            var fontDef = _modManager.GetDefinition<FontDefinition>(resourceId);
            if (fontDef == null || string.IsNullOrEmpty(fontDef.FontPath))
            {
                throw new InvalidOperationException(
                    $"Font definition not found or has no FontPath: {resourceId}"
                );
            }

            string fullPath = _pathResolver.ResolveResourcePath(resourceId, fontDef.FontPath);
            byte[] fontData = File.ReadAllBytes(fullPath);

            // Create font system and verify
            var fontSystem = new FontSystem();
            fontSystem.AddFont(fontData);

            var testFont = fontSystem.GetFont(12);
            if (testFont == null)
            {
                throw new InvalidOperationException(
                    $"Font file is invalid or corrupted: {resourceId}"
                );
            }

            // Update cache (acquire lock again)
            lock (_lock)
            {
                // Double-check: another thread might have loaded it
                if (_fontCache.TryGetValue(resourceId, out var cached))
                {
                    fontSystem.Dispose(); // Dispose our copy, use cached one
                    return cached;
                }

                // Cache (unlimited cache for fonts)
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
                throw new ArgumentException(
                    "Resource ID cannot be null or empty.",
                    nameof(resourceId)
                );
            }

            // CRITICAL: VorbisReader is stateful (maintains position) and cannot be safely shared.
            // Each audio playback needs its own reader instance. Do NOT cache readers.
            // Always create a new reader for each playback to avoid interference between concurrent playbacks.

            var audioDef = _modManager.GetDefinition<AudioDefinition>(resourceId);
            if (audioDef == null || string.IsNullOrEmpty(audioDef.AudioPath))
            {
                throw new InvalidOperationException(
                    $"Audio definition not found or has no AudioPath: {resourceId}"
                );
            }

            string fullPath = _pathResolver.ResolveResourcePath(resourceId, audioDef.AudioPath);
            var reader = new VorbisReader(fullPath);

            _logger.Debug("Created new audio reader: {ResourceId}", resourceId);
            return reader;
        }

        public Effect LoadShader(string resourceId)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ResourceManager));

            if (string.IsNullOrEmpty(resourceId))
            {
                throw new ArgumentException(
                    "Resource ID cannot be null or empty.",
                    nameof(resourceId)
                );
            }

            // Fast path: check cache (with lock)
            lock (_lock)
            {
                if (_shaderCache.TryGetValue(resourceId, out var cached))
                {
                    // Update LRU
                    _shaderAccessOrder.Remove(resourceId);
                    _shaderAccessOrder.AddFirst(resourceId);
                    return cached;
                }
            }

            // Slow path: load file OUTSIDE lock
            var shaderDef = _modManager.GetDefinition<ShaderDefinition>(resourceId);
            if (shaderDef == null || string.IsNullOrEmpty(shaderDef.SourceFile))
            {
                throw new InvalidOperationException(
                    $"Shader definition not found or has no SourceFile: {resourceId}"
                );
            }

            string fullPath = _pathResolver.ResolveResourcePath(resourceId, shaderDef.SourceFile);
            byte[] bytecode = File.ReadAllBytes(fullPath);
            var effect = new Effect(_graphicsDevice, bytecode);

            // Update cache (acquire lock again)
            lock (_lock)
            {
                // Double-check: another thread might have loaded it
                if (_shaderCache.TryGetValue(resourceId, out var cached))
                {
                    effect.Dispose(); // Dispose our copy, use cached one
                    _shaderAccessOrder.Remove(resourceId);
                    _shaderAccessOrder.AddFirst(resourceId);
                    return cached;
                }

                // Evict LRU if at capacity
                EvictLRUShader();

                // Add to cache
                _shaderCache[resourceId] = effect;
                _shaderAccessOrder.AddFirst(resourceId);
                _logger.Debug("Loaded and cached shader: {ResourceId}", resourceId);

                return effect;
            }
        }

        /// <summary>
        /// Evicts the least recently used shader from cache. Must be called within lock.
        /// </summary>
        private void EvictLRUShader()
        {
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
        }

        public SpriteDefinition GetSpriteDefinition(string spriteId)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ResourceManager));

            if (string.IsNullOrEmpty(spriteId))
            {
                throw new ArgumentException("Sprite ID cannot be null or empty.", nameof(spriteId));
            }

            lock (_lock)
            {
                // Resolve variable sprites - fail fast if cannot resolve
                string actualSpriteId = ResolveVariableSpriteIfNeeded(
                    spriteId,
                    "sprite definition"
                );
                if (actualSpriteId == null)
                {
                    throw new InvalidOperationException(
                        $"Failed to resolve variable sprite '{spriteId}'. Variable sprite should be resolved before loading."
                    );
                }

                // Check cache first
                if (_spriteDefinitionCache.TryGetValue(actualSpriteId, out var cached))
                {
                    return cached;
                }

                // Load from registry - fail fast if not found
                var definition = _modManager.GetDefinition<SpriteDefinition>(actualSpriteId);
                if (definition == null)
                {
                    throw new InvalidOperationException(
                        $"Sprite definition not found: {actualSpriteId}"
                    );
                }

                _spriteDefinitionCache[actualSpriteId] = definition;
                // Pre-compute animation frames when definition is loaded
                PrecomputeAnimationFrames(actualSpriteId, definition);

                return definition;
            }
        }

        private string ResolveVariableSpriteIfNeeded(string spriteId, string context)
        {
            if (string.IsNullOrEmpty(spriteId))
            {
                throw new ArgumentException("Sprite ID cannot be null or empty.", nameof(spriteId));
            }

            // If not a variable sprite, return as-is
            if (_variableSpriteResolver?.IsVariableSprite(spriteId) != true)
            {
                return spriteId;
            }

            // Attempt to resolve variable sprite - fail fast if cannot resolve
            try
            {
                var resolved = _variableSpriteResolver.ResolveVariableSprite(spriteId);
                if (resolved == null)
                {
                    throw new InvalidOperationException(
                        $"Failed to resolve variable sprite '{spriteId}' for {context}. Variable sprite should be resolved before loading."
                    );
                }
                return resolved;
            }
            catch (InvalidOperationException)
            {
                // Re-throw as-is (already has good message)
                throw;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Cannot resolve variable sprite '{spriteId}' for {context}. Game state variable is not set. Variable sprite should be resolved before loading.",
                    ex
                );
            }
        }

        private void PrecomputeAnimationFrames(string spriteId, SpriteDefinition definition)
        {
            if (definition.Animations == null || definition.Frames == null)
            {
                return;
            }

            foreach (var animation in definition.Animations)
            {
                var frameList = new List<SpriteAnimationFrame>();

                if (animation.FrameIndices == null || animation.FrameDurations == null)
                {
                    continue;
                }

                for (int i = 0; i < animation.FrameIndices.Count; i++)
                {
                    int frameIndex = animation.FrameIndices[i];
                    double frameDuration =
                        i < animation.FrameDurations.Count ? animation.FrameDurations[i] : 0.0;

                    // Handle frame duration unit conversion
                    float durationSeconds = (float)frameDuration;
                    if (frameDuration > MillisecondsThreshold)
                    {
                        durationSeconds = (float)(frameDuration / 1000.0);
                    }

                    // Find the frame definition
                    var frameDef = definition.Frames.FirstOrDefault(f => f.Index == frameIndex);
                    if (frameDef != null)
                    {
                        var animationFrame = new SpriteAnimationFrame
                        {
                            SourceRectangle = new Rectangle(
                                frameDef.X,
                                frameDef.Y,
                                frameDef.Width,
                                frameDef.Height
                            ),
                            DurationSeconds = durationSeconds,
                        };
                        frameList.Add(animationFrame);
                    }
                    else
                    {
                        _logger.Warning(
                            "Frame index {FrameIndex} not found in sprite {SpriteId}",
                            frameIndex,
                            spriteId
                        );
                    }
                }

                if (frameList.Count > 0)
                {
                    var key = (spriteId, animation.Name);
                    _animationFrameCache[key] = frameList;
                }
            }
        }

        public IReadOnlyList<SpriteAnimationFrame> GetAnimationFrames(
            string spriteId,
            string animationName
        )
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ResourceManager));

            if (string.IsNullOrEmpty(spriteId))
            {
                throw new ArgumentException("Sprite ID cannot be null or empty.", nameof(spriteId));
            }

            if (string.IsNullOrEmpty(animationName))
            {
                throw new ArgumentException(
                    "Animation name cannot be null or empty.",
                    nameof(animationName)
                );
            }

            lock (_lock)
            {
                // Resolve variable sprites - fail fast if cannot resolve
                string actualSpriteId = ResolveVariableSpriteIfNeeded(spriteId, "animation frames");

                var key = (actualSpriteId, animationName);
                if (_animationFrameCache.TryGetValue(key, out var frames))
                {
                    return frames;
                }

                // Try to load sprite definition if not already loaded (this will throw if not found)
                var definition = GetSpriteDefinition(actualSpriteId);

                // Check cache again (PrecomputeAnimationFrames should have populated it)
                if (_animationFrameCache.TryGetValue(key, out frames))
                {
                    return frames;
                }

                // Animation not found - fail fast
                throw new InvalidOperationException(
                    $"Animation '{animationName}' not found for sprite '{actualSpriteId}'"
                );
            }
        }

        public Rectangle GetAnimationFrameRectangle(
            string spriteId,
            string animationName,
            int frameIndex
        )
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ResourceManager));

            var frames = GetAnimationFrames(spriteId, animationName); // This will throw if not found

            if (frameIndex < 0 || frameIndex >= frames.Count)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(frameIndex),
                    $"Frame index {frameIndex} is out of range for animation '{animationName}' of sprite '{spriteId}' (total frames: {frames.Count})"
                );
            }

            return frames[frameIndex].SourceRectangle;
        }

        public bool ValidateSpriteDefinition(string spriteId)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ResourceManager));

            if (string.IsNullOrEmpty(spriteId))
            {
                return false;
            }

            try
            {
                GetSpriteDefinition(spriteId);
                return true;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        public bool ValidateAnimation(string spriteId, string animationName)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ResourceManager));

            if (string.IsNullOrEmpty(spriteId) || string.IsNullOrEmpty(animationName))
            {
                return false;
            }

            try
            {
                var definition = GetSpriteDefinition(spriteId);
                return definition.Animations?.Any(a => a.Name == animationName) ?? false;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        public bool GetAnimationLoops(string spriteId, string animationName)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ResourceManager));

            if (string.IsNullOrEmpty(spriteId))
            {
                throw new ArgumentException("Sprite ID cannot be null or empty.", nameof(spriteId));
            }

            if (string.IsNullOrEmpty(animationName))
            {
                throw new ArgumentException(
                    "Animation name cannot be null or empty.",
                    nameof(animationName)
                );
            }

            var definition = GetSpriteDefinition(spriteId); // This will throw if not found

            var animation = definition.Animations?.FirstOrDefault(a => a.Name == animationName);
            if (animation == null)
            {
                throw new InvalidOperationException(
                    $"Animation '{animationName}' not found for sprite '{spriteId}'"
                );
            }

            return animation.Loop;
        }

        public bool GetAnimationFlipHorizontal(string spriteId, string animationName)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ResourceManager));

            if (string.IsNullOrEmpty(spriteId))
            {
                throw new ArgumentException("Sprite ID cannot be null or empty.", nameof(spriteId));
            }

            if (string.IsNullOrEmpty(animationName))
            {
                throw new ArgumentException(
                    "Animation name cannot be null or empty.",
                    nameof(animationName)
                );
            }

            var definition = GetSpriteDefinition(spriteId); // This will throw if not found

            var animation = definition.Animations?.FirstOrDefault(a => a.Name == animationName);
            if (animation == null)
            {
                throw new InvalidOperationException(
                    $"Animation '{animationName}' not found for sprite '{spriteId}'"
                );
            }

            return animation.FlipHorizontal;
        }

        public TilesetDefinition GetTilesetDefinition(string tilesetId)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ResourceManager));

            if (string.IsNullOrEmpty(tilesetId))
            {
                throw new ArgumentException(
                    "Tileset ID cannot be null or empty.",
                    nameof(tilesetId)
                );
            }

            lock (_lock)
            {
                // Check cache first
                if (_tilesetDefinitionCache.TryGetValue(tilesetId, out var cached))
                {
                    return cached;
                }

                // Load from registry - fail fast if not found
                var definition = _modManager.GetDefinition<TilesetDefinition>(tilesetId);
                if (definition == null)
                {
                    throw new InvalidOperationException(
                        $"Tileset definition not found: {tilesetId}"
                    );
                }

                _tilesetDefinitionCache[tilesetId] = definition;
                return definition;
            }
        }

        public IReadOnlyList<TileAnimationFrame> GetTileAnimation(string tilesetId, int localTileId)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ResourceManager));

            if (string.IsNullOrEmpty(tilesetId))
            {
                throw new ArgumentException(
                    "Tileset ID cannot be null or empty.",
                    nameof(tilesetId)
                );
            }

            lock (_lock)
            {
                var cacheKey = (tilesetId, localTileId);

                // Check cache first
                if (_tileAnimationCache.TryGetValue(cacheKey, out var cached))
                {
                    return cached;
                }

                // Get tileset definition - fail fast if not found
                var definition = GetTilesetDefinition(tilesetId);

                // Search for the tile in the tiles list
                if (definition.Tiles != null)
                {
                    foreach (var tile in definition.Tiles)
                    {
                        if (tile.LocalTileId == localTileId)
                        {
                            if (tile.Animation != null && tile.Animation.Count > 0)
                            {
                                // Cache and return as readonly list
                                var readonlyFrames = tile.Animation.AsReadOnly();
                                _tileAnimationCache[cacheKey] = readonlyFrames;
                                return readonlyFrames;
                            }
                            else
                            {
                                // Tile exists but has no animation - fail fast
                                throw new InvalidOperationException(
                                    $"Tile {localTileId} in tileset '{tilesetId}' has no animation"
                                );
                            }
                        }
                    }
                }

                // Tile not found - fail fast
                throw new InvalidOperationException(
                    $"Tile {localTileId} not found in tileset '{tilesetId}'"
                );
            }
        }

        public IReadOnlyList<TileAnimationFrame>? GetCachedTileAnimation(
            string tilesetId,
            int localTileId
        )
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ResourceManager));

            if (string.IsNullOrEmpty(tilesetId))
            {
                return null;
            }

            lock (_lock)
            {
                var cacheKey = (tilesetId, localTileId);
                return _tileAnimationCache.TryGetValue(cacheKey, out var cached) ? cached : null;
            }
        }

        public Rectangle CalculateTilesetSourceRectangle(string tilesetId, int gid, int firstGid)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ResourceManager));

            if (string.IsNullOrEmpty(tilesetId))
            {
                throw new ArgumentException(
                    "Tileset ID cannot be null or empty.",
                    nameof(tilesetId)
                );
            }

            // Get tileset definition - fail fast if not found
            var definition = GetTilesetDefinition(tilesetId);

            // Calculate local tile ID
            int localTileId = gid - firstGid;

            // Validate local tile ID - fail fast if invalid
            if (localTileId < 0)
            {
                throw new ArgumentException(
                    $"GID {gid} results in negative local tile ID {localTileId} for tileset '{tilesetId}' (firstGid: {firstGid})",
                    nameof(gid)
                );
            }

            if (localTileId >= definition.TileCount)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(gid),
                    $"GID {gid} (local tile ID {localTileId}) is out of range for tileset '{tilesetId}' (total tiles: {definition.TileCount})"
                );
            }

            // Calculate tile position in tileset
            int column = localTileId % definition.Columns;
            int row = localTileId / definition.Columns;

            // Calculate source rectangle accounting for spacing and margin
            int sourceX = column * (definition.TileWidth + definition.Spacing) + definition.Margin;
            int sourceY = row * (definition.TileHeight + definition.Spacing) + definition.Margin;

            // Validate source rectangle is within image bounds - fail fast if invalid
            if (
                sourceX + definition.TileWidth > definition.ImageWidth
                || sourceY + definition.TileHeight > definition.ImageHeight
            )
            {
                throw new InvalidOperationException(
                    $"Calculated source rectangle for tile {localTileId} in tileset '{tilesetId}' is out of image bounds. "
                        + $"Source: ({sourceX}, {sourceY}, {definition.TileWidth}, {definition.TileHeight}), "
                        + $"Image: ({definition.ImageWidth}, {definition.ImageHeight})"
                );
            }

            return new Rectangle(sourceX, sourceY, definition.TileWidth, definition.TileHeight);
        }

        public T? GetDefinition<T>(string resourceId)
            where T : class
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ResourceManager));

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
            // Audio readers are not cached - always returns null
            // Use LoadAudioReader() to create a new reader instance
            return null;
        }

        public bool HasAudio(string resourceId)
        {
            // Audio readers are not cached - always returns false
            // Use LoadAudioReader() to create a new reader instance
            return false;
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
                        // Audio readers are not cached - nothing to unload
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
                    // Audio readers are not cached - nothing to clear
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
            if (_disposed)
                return;

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

                // Audio readers are not cached - nothing to dispose

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
