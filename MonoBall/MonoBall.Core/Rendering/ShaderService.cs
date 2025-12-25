using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework.Graphics;
using MonoBall.Core.Mods;
using MonoBall.Core.Mods.Definitions;
using Serilog;

namespace MonoBall.Core.Rendering
{
    /// <summary>
    /// Service for loading and caching shader effects with LRU eviction.
    /// </summary>
    public class ShaderService : IShaderService, IDisposable
    {
        private readonly GraphicsDevice _graphicsDevice;
        private readonly IModManager _modManager;
        private readonly ILogger _logger;
        private readonly ShaderLoader _shaderLoader;
        private readonly Dictionary<string, Effect> _cache = new();
        private readonly LinkedList<string> _accessOrder = new();
        private readonly object _lock = new();
        private const int MaxCacheSize = 20;
        private bool _disposed = false;

        /// <summary>
        /// Initializes a new instance of the ShaderService.
        /// </summary>
        /// <param name="graphicsDevice">The graphics device.</param>
        /// <param name="modManager">The mod manager for accessing shader definitions.</param>
        /// <param name="shaderLoader">The shader loader for loading compiled shader files.</param>
        /// <param name="logger">The logger for logging operations.</param>
        /// <exception cref="ArgumentNullException">Thrown when graphicsDevice, modManager, shaderLoader, or logger is null.</exception>
        public ShaderService(
            GraphicsDevice graphicsDevice,
            IModManager modManager,
            ShaderLoader shaderLoader,
            ILogger logger
        )
        {
            _graphicsDevice =
                graphicsDevice ?? throw new ArgumentNullException(nameof(graphicsDevice));
            _modManager = modManager ?? throw new ArgumentNullException(nameof(modManager));
            _shaderLoader = shaderLoader ?? throw new ArgumentNullException(nameof(shaderLoader));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Validates that a shader ID matches the mod shader format and is all lowercase.
        /// </summary>
        /// <param name="shaderId">The shader ID to validate.</param>
        /// <exception cref="ArgumentException">Thrown when shader ID format is invalid.</exception>
        private void ValidateShaderIdFormat(string shaderId)
        {
            if (string.IsNullOrEmpty(shaderId))
            {
                throw new ArgumentNullException(nameof(shaderId));
            }

            if (!shaderId.Contains(":shader:"))
            {
                throw new ArgumentException(
                    $"Shader ID '{shaderId}' does not match mod shader format. "
                        + "Expected format: {{namespace}}:shader:{{name}} (all lowercase).",
                    nameof(shaderId)
                );
            }

            if (shaderId != shaderId.ToLowerInvariant())
            {
                throw new ArgumentException(
                    $"Shader ID '{shaderId}' must be all lowercase. "
                        + "Expected format: {{namespace}}:shader:{{name}} (all lowercase).",
                    nameof(shaderId)
                );
            }
        }

        /// <inheritdoc />
        public Effect? LoadShader(string shaderId)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ShaderService));

            if (string.IsNullOrEmpty(shaderId))
            {
                _logger.Warning("Attempted to load shader with null or empty ID");
                return null;
            }

            ValidateShaderIdFormat(shaderId);

            var metadata = _modManager.GetDefinitionMetadata(shaderId);
            if (metadata == null)
            {
                _logger.Warning("Shader definition not found: {ShaderId}", shaderId);
                return null;
            }

            if (metadata.DefinitionType != "Shaders")
            {
                _logger.Warning(
                    "Definition '{ShaderId}' is not a shader definition (type: {DefinitionType})",
                    shaderId,
                    metadata.DefinitionType
                );
                return null;
            }

            var shaderDef = _modManager.GetDefinition<ShaderDefinition>(shaderId);
            if (shaderDef == null)
            {
                _logger.Warning("Failed to deserialize shader definition: {ShaderId}", shaderId);
                return null;
            }

            var modManifest = _modManager.GetModManifestByDefinitionId(shaderId);
            if (modManifest == null)
            {
                _logger.Warning(
                    "Mod manifest not found for shader {ShaderId} (mod: {ModId})",
                    shaderId,
                    metadata.OriginalModId
                );
                return null;
            }

            try
            {
                return _shaderLoader.LoadShader(shaderDef, modManifest);
            }
            catch (Exception ex)
            {
                _logger.Error(
                    ex,
                    "Failed to load shader: {ShaderId} from mod {ModId}",
                    shaderId,
                    modManifest.Id
                );
                return null;
            }
        }

        /// <inheritdoc />
        public Effect? GetShader(string shaderId)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ShaderService));

            if (string.IsNullOrEmpty(shaderId))
            {
                _logger.Warning("Attempted to get shader with null or empty ID");
                return null;
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

                // Load shader (returns null on failure, consistent with other resource loaders)
                Effect? effect = LoadShader(shaderId);
                if (effect == null)
                {
                    return null;
                }

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

            // Check if shader exists in registry
            var metadata = _modManager.GetDefinitionMetadata(shaderId);
            if (metadata == null || metadata.DefinitionType != "Shaders")
            {
                return false;
            }

            // Also check cache (for performance - shader might be loaded)
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
