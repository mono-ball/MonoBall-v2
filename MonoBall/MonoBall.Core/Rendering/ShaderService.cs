using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework.Graphics;
using MonoBall.Core.Mods;
using MonoBall.Core.Mods.Definitions;
using MonoBall.Core.Resources;
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
        private readonly IResourceManager _resourceManager;
        private readonly ILogger _logger;
        private bool _disposed = false;

        /// <summary>
        /// Initializes a new instance of the ShaderService.
        /// </summary>
        /// <param name="graphicsDevice">The graphics device.</param>
        /// <param name="modManager">The mod manager for accessing shader definitions.</param>
        /// <param name="resourceManager">The resource manager for loading shader effects.</param>
        /// <param name="logger">The logger for logging operations.</param>
        /// <exception cref="ArgumentNullException">Thrown when graphicsDevice, modManager, resourceManager, or logger is null.</exception>
        public ShaderService(
            GraphicsDevice graphicsDevice,
            IModManager modManager,
            IResourceManager resourceManager,
            ILogger logger
        )
        {
            _graphicsDevice =
                graphicsDevice ?? throw new ArgumentNullException(nameof(graphicsDevice));
            _modManager = modManager ?? throw new ArgumentNullException(nameof(modManager));
            _resourceManager =
                resourceManager ?? throw new ArgumentNullException(nameof(resourceManager));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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

            // Use ResourceManager to load shader (handles definition lookup, path resolution, and caching)
            return _resourceManager.LoadShader(shaderId);
        }

        /// <inheritdoc />
        public Effect GetShader(string shaderId)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ShaderService));

            if (string.IsNullOrEmpty(shaderId))
            {
                throw new ArgumentException("Shader ID cannot be null or empty.", nameof(shaderId));
            }

            // First check cache (fast path)
            var cached = _resourceManager.GetCachedShader(shaderId);
            if (cached != null)
            {
                return cached;
            }

            // Not cached - load it (fail fast per .cursorrules)
            return LoadShader(shaderId);
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

            // Shader exists in registry - return true (regardless of cache status)
            // The cache check is just for performance optimization, but HasShader() should
            // return true if the shader definition exists, even if not yet loaded.
            return true;
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

            // Use ResourceManager to unload shader
            _resourceManager.UnloadResource(shaderId, ResourceType.Shader);
            _logger.Debug("Unloaded shader from cache: {ShaderId}", shaderId);
        }

        /// <inheritdoc />
        public void UnloadAllShaders()
        {
            if (_disposed)
                return;

            // Use ResourceManager to unload all shaders
            _resourceManager.UnloadAll(ResourceType.Shader);
            _logger.Debug("Unloaded all shaders from cache");
        }

        /// <summary>
        /// Disposes the service. Shaders are managed by ResourceManager, so no cleanup needed here.
        /// </summary>
        public void Dispose()
        {
            if (!_disposed)
            {
                // Shaders are managed by ResourceManager, no cleanup needed
                _disposed = true;
            }
        }
    }
}
