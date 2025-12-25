using System;
using System.IO;
using Microsoft.Xna.Framework.Graphics;
using MonoBall.Core.Mods;
using MonoBall.Core.Mods.Definitions;
using Serilog;

namespace MonoBall.Core.Rendering
{
    /// <summary>
    /// Loads compiled shader effects from mod directories.
    /// </summary>
    public class ShaderLoader
    {
        private readonly GraphicsDevice _graphicsDevice;
        private readonly ILogger _logger;

        /// <summary>
        /// Initializes a new instance of the ShaderLoader.
        /// </summary>
        /// <param name="graphicsDevice">The graphics device for creating effects.</param>
        /// <param name="logger">The logger for logging operations.</param>
        /// <exception cref="ArgumentNullException">Thrown when graphicsDevice or logger is null.</exception>
        public ShaderLoader(GraphicsDevice graphicsDevice, ILogger logger)
        {
            _graphicsDevice =
                graphicsDevice ?? throw new ArgumentNullException(nameof(graphicsDevice));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Loads a compiled shader effect from a mod.
        /// </summary>
        /// <param name="shaderDefinition">The shader definition.</param>
        /// <param name="modManifest">The mod manifest containing the shader.</param>
        /// <returns>The loaded Effect.</returns>
        /// <exception cref="ArgumentNullException">Thrown when shaderDefinition or modManifest is null.</exception>
        /// <exception cref="FileNotFoundException">Thrown when compiled shader file is not found.</exception>
        /// <exception cref="InvalidOperationException">Thrown when shader bytecode is invalid.</exception>
        public Effect LoadShader(ShaderDefinition shaderDefinition, ModManifest modManifest)
        {
            if (shaderDefinition == null)
                throw new ArgumentNullException(nameof(shaderDefinition));
            if (modManifest == null)
                throw new ArgumentNullException(nameof(modManifest));

            string mgfxoPath = Path.Combine(modManifest.ModDirectory, shaderDefinition.SourceFile);
            mgfxoPath = Path.GetFullPath(mgfxoPath);

            _logger.Debug("Resolved shader path: {ShaderPath}", mgfxoPath);

            if (!File.Exists(mgfxoPath))
            {
                throw new FileNotFoundException(
                    $"Compiled shader not found: {mgfxoPath}. "
                        + "Ensure shaders are compiled during build.",
                    mgfxoPath
                );
            }

            try
            {
                byte[] bytecode = File.ReadAllBytes(mgfxoPath);
                var effect = new Effect(_graphicsDevice, bytecode);
                _logger.Debug(
                    "Loaded shader effect: {ShaderId} from {ShaderPath}",
                    shaderDefinition.Id,
                    mgfxoPath
                );
                return effect;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Failed to create Effect from bytecode for shader '{shaderDefinition.Id}': {ex.Message}",
                    ex
                );
            }
        }
    }
}
