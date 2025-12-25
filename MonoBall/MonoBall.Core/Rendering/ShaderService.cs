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
        public Effect LoadShader(string shaderId)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ShaderService));

            if (string.IsNullOrEmpty(shaderId))
            {
                throw new ArgumentException("Shader ID cannot be null or empty.", nameof(shaderId));
            }

            ValidateShaderIdFormat(shaderId);

            var metadata = _modManager.GetDefinitionMetadata(shaderId);
            if (metadata == null)
            {
                throw new InvalidOperationException(
                    $"Shader definition not found: {shaderId}. "
                        + "Ensure the shader is defined in a loaded mod."
                );
            }

            if (metadata.DefinitionType != "Shaders")
            {
                throw new InvalidOperationException(
                    $"Definition '{shaderId}' is not a shader definition (type: {metadata.DefinitionType}). "
                        + "Expected definition type: Shaders."
                );
            }

            var shaderDef = _modManager.GetDefinition<ShaderDefinition>(shaderId);
            if (shaderDef == null)
            {
                throw new InvalidOperationException(
                    $"Failed to deserialize shader definition: {shaderId}. "
                        + "The definition file may be malformed."
                );
            }

            var modManifest = _modManager.GetModManifestByDefinitionId(shaderId);
            if (modManifest == null)
            {
                throw new InvalidOperationException(
                    $"Mod manifest not found for shader {shaderId} (mod: {metadata.OriginalModId}). "
                        + "The mod that defines this shader may not be loaded."
                );
            }

            // Let exceptions propagate (fail fast per .cursorrules)
            return _shaderLoader.LoadShader(shaderDef, modManifest);
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

            // Validate format (fail fast for invalid IDs)
            try
            {
                ValidateShaderIdFormat(shaderId);
            }
            catch (ArgumentException)
            {
                // Invalid format - return null (GetShader is meant to be safe)
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

                // Load shader (catch exceptions and return null - GetShader is meant to be safe)
                try
                {
                    Effect effect = LoadShader(shaderId);
                    // Add to cache
                    AddToCache(shaderId, effect);
                    return effect;
                }
                catch (Exception ex)
                {
                    _logger.Warning(
                        ex,
                        "Failed to get shader: {ShaderId}. Returning null (shader may be optional).",
                        shaderId
                    );
                    return null;
                }
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

            // Validate format (return false for invalid IDs)
            try
            {
                ValidateShaderIdFormat(shaderId);
            }
            catch (ArgumentException)
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
