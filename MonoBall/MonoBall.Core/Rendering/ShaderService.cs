using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using Serilog;

namespace MonoBall.Core.Rendering
{
    /// <summary>
    /// Service for loading and caching shader effects with LRU eviction.
    /// </summary>
    public class ShaderService : IShaderService, IDisposable
    {
        private readonly ContentManager _content;
        private readonly GraphicsDevice _graphicsDevice;
        private readonly ILogger _logger;
        private readonly Dictionary<string, Effect> _cache = new();
        private readonly LinkedList<string> _accessOrder = new();
        private readonly object _lock = new();
        private const int MaxCacheSize = 20;
        private bool _disposed = false;

        /// <summary>
        /// Initializes a new instance of the ShaderService.
        /// </summary>
        /// <param name="content">The content manager for loading shaders.</param>
        /// <param name="graphicsDevice">The graphics device.</param>
        /// <param name="logger">The logger for logging operations.</param>
        public ShaderService(ContentManager content, GraphicsDevice graphicsDevice, ILogger logger)
        {
            _content = content ?? throw new ArgumentNullException(nameof(content));
            _graphicsDevice =
                graphicsDevice ?? throw new ArgumentNullException(nameof(graphicsDevice));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Gets the subdirectory and shader name for a shader based on its ID prefix.
        /// </summary>
        /// <param name="shaderId">The shader ID (e.g., "TileLayerColorGrading").</param>
        /// <returns>A tuple containing (subdirectory, shaderName).</returns>
        /// <exception cref="ArgumentException">Thrown when the shader ID has an unknown prefix.</exception>
        private (string subdirectory, string shaderName) GetShaderPathInfo(string shaderId)
        {
            if (shaderId.StartsWith("TileLayer"))
            {
                string shaderName = shaderId.Substring("TileLayer".Length);
                return ("TileLayer", shaderName);
            }
            if (shaderId.StartsWith("SpriteLayer"))
            {
                string shaderName = shaderId.Substring("SpriteLayer".Length);
                return ("SpriteLayer", shaderName);
            }
            if (shaderId.StartsWith("CombinedLayer"))
            {
                string shaderName = shaderId.Substring("CombinedLayer".Length);
                return ("CombinedLayer", shaderName);
            }
            if (shaderId.StartsWith("PerEntity"))
            {
                string shaderName = shaderId.Substring("PerEntity".Length);
                return ("PerEntity", shaderName);
            }

            throw new ArgumentException(
                $"Unknown shader layer prefix: {shaderId}",
                nameof(shaderId)
            );
        }

        /// <inheritdoc />
        public Effect LoadShader(string shaderId)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ShaderService));

            if (string.IsNullOrEmpty(shaderId))
            {
                throw new ArgumentNullException(nameof(shaderId));
            }

            var (subdirectory, shaderName) = GetShaderPathInfo(shaderId);
            // MonoGame ContentManager expects paths without file extension (auto-adds .xnb)
            string contentPath = $"Shaders/{subdirectory}/{shaderName}";

            _logger.Debug("Loading shader from content path: {ContentPath}", contentPath);

            try
            {
                Effect effect = _content.Load<Effect>(contentPath);
                _logger.Debug("Successfully loaded shader: {ShaderId}", shaderId);
                return effect;
            }
            catch (Microsoft.Xna.Framework.Content.ContentLoadException ex)
            {
                // Fail fast per .cursorrules - shader loading failure is a critical error
                throw new InvalidOperationException(
                    $"Failed to load shader '{shaderId}' from content path '{contentPath}': {ex.Message}",
                    ex
                );
            }
        }

        /// <inheritdoc />
        public Effect GetShader(string shaderId)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ShaderService));

            if (string.IsNullOrEmpty(shaderId))
            {
                throw new ArgumentNullException(nameof(shaderId));
            }

            lock (_lock)
            {
                // Check cache first
                if (_cache.TryGetValue(shaderId, out Effect? cachedEffect))
                {
                    // Move to front (most recently used)
                    _accessOrder.Remove(shaderId);
                    _accessOrder.AddFirst(shaderId);
                    _logger.Debug("Using cached shader: {ShaderId}", shaderId);
                    return cachedEffect;
                }

                // Load shader (throws on failure per .cursorrules)
                Effect effect = LoadShader(shaderId);

                // Add to cache
                AddToCache(shaderId, effect);
                return effect;
            }
        }

        /// <summary>
        /// Adds a shader to the cache, evicting LRU items if needed.
        /// </summary>
        private void AddToCache(string shaderId, Effect effect)
        {
            lock (_lock)
            {
                // Evict least recently used if at capacity
                if (_cache.Count >= MaxCacheSize)
                {
                    string? lruKey = _accessOrder.Last?.Value;
                    if (lruKey != null && _cache.TryGetValue(lruKey, out Effect? lruEffect))
                    {
                        _cache.Remove(lruKey);
                        _accessOrder.RemoveLast();
                        lruEffect?.Dispose();
                        _logger.Debug("Evicted LRU shader from cache: {ShaderId}", lruKey);
                    }
                }

                _cache[shaderId] = effect;
                _accessOrder.AddFirst(shaderId);
                _logger.Debug(
                    "Added shader to cache: {ShaderId} (Cache size: {CacheSize})",
                    shaderId,
                    _cache.Count
                );
            }
        }

        /// <inheritdoc />
        public bool HasShader(string shaderId)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ShaderService));

            if (string.IsNullOrEmpty(shaderId))
            {
                return false;
            }

            lock (_lock)
            {
                return _cache.ContainsKey(shaderId);
            }
        }

        /// <inheritdoc />
        public void UnloadShader(string shaderId)
        {
            if (_disposed)
                return;

            if (string.IsNullOrEmpty(shaderId))
            {
                return;
            }

            lock (_lock)
            {
                if (_cache.TryGetValue(shaderId, out Effect? effect))
                {
                    _cache.Remove(shaderId);
                    _accessOrder.Remove(shaderId);
                    effect?.Dispose();
                    _logger.Debug("Unloaded shader from cache: {ShaderId}", shaderId);
                }
            }
        }

        /// <inheritdoc />
        public void UnloadAllShaders()
        {
            if (_disposed)
                return;

            lock (_lock)
            {
                foreach (var effect in _cache.Values)
                {
                    effect?.Dispose();
                }

                _cache.Clear();
                _accessOrder.Clear();
                _logger.Debug("Unloaded all shaders from cache");
            }
        }

        /// <summary>
        /// Disposes the service and unloads all cached shaders.
        /// </summary>
        public void Dispose()
        {
            if (!_disposed)
            {
                UnloadAllShaders();
                _disposed = true;
            }
        }
    }
}
