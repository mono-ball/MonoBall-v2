using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoBall.Core.Mods;
using MonoBall.Core.Rendering;
using Serilog;

namespace MonoBall.Core.Mods.Utilities
{
    /// <summary>
    /// Factory for creating and registering FontService instances.
    /// Ensures FontService is created consistently and prevents duplicate registration.
    /// </summary>
    public static class FontServiceFactory
    {
        /// <summary>
        /// Creates and registers FontService if it doesn't already exist in Game.Services.
        /// Prevents duplicate registration and preserves existing FontService instances with cached fonts.
        /// </summary>
        /// <param name="game">The game instance for accessing services.</param>
        /// <param name="modManager">The mod manager for FontService.</param>
        /// <param name="graphicsDevice">The graphics device for FontService.</param>
        /// <param name="logger">The logger for logging operations.</param>
        /// <returns>The existing FontService if one exists, or the newly created FontService.</returns>
        public static FontService GetOrCreateFontService(
            Game game,
            IModManager modManager,
            GraphicsDevice graphicsDevice,
            ILogger logger
        )
        {
            if (game == null)
            {
                throw new ArgumentNullException(nameof(game));
            }
            if (modManager == null)
            {
                throw new ArgumentNullException(nameof(modManager));
            }
            if (graphicsDevice == null)
            {
                throw new ArgumentNullException(nameof(graphicsDevice));
            }
            if (logger == null)
            {
                throw new ArgumentNullException(nameof(logger));
            }

            // Check if FontService already exists
            var existingFontService = game.Services.GetService<FontService>();
            if (existingFontService != null)
            {
                logger.Debug(
                    "FontService already exists in Game.Services, reusing existing instance to preserve font cache"
                );
                return existingFontService;
            }

            // Create new FontService
            logger.Debug("Creating new FontService");
            var fontService = new FontService(modManager, graphicsDevice, logger);

            // Register in Game.Services
            game.Services.AddService(typeof(FontService), fontService);
            logger.Debug("FontService created and registered");

            return fontService;
        }
    }
}
