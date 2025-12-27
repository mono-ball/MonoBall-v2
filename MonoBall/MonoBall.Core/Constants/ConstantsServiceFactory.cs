using System;
using MonoBall.Core.Mods;
using Serilog;

namespace MonoBall.Core.Constants
{
    /// <summary>
    /// Factory for creating and registering ConstantsService instances.
    /// Ensures ConstantsService is created consistently and prevents duplicate registration.
    /// </summary>
    public static class ConstantsServiceFactory
    {
        /// <summary>
        /// Creates and registers ConstantsService if it doesn't already exist in Game.Services.
        /// Prevents duplicate registration and preserves existing ConstantsService instances with cached constants.
        /// </summary>
        /// <param name="game">The game instance for accessing services.</param>
        /// <param name="modManager">The mod manager for ConstantsService.</param>
        /// <param name="logger">The logger for logging operations.</param>
        /// <returns>The existing ConstantsService if one exists, or the newly created ConstantsService.</returns>
        public static ConstantsService GetOrCreateConstantsService(
            Microsoft.Xna.Framework.Game game,
            IModManager modManager,
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
            if (logger == null)
            {
                throw new ArgumentNullException(nameof(logger));
            }

            // Check if ConstantsService already exists
            var existingConstantsService = game.Services.GetService<ConstantsService>();
            if (existingConstantsService != null)
            {
                logger.Debug(
                    "ConstantsService already exists in Game.Services, reusing existing instance to preserve constants cache"
                );
                return existingConstantsService;
            }

            // Create new ConstantsService
            logger.Debug("Creating new ConstantsService");
            var constantsService = new ConstantsService(modManager, logger);

            // Register in Game.Services (using concrete type for consistency with FontService pattern)
            game.Services.AddService(typeof(ConstantsService), constantsService);
            logger.Debug("ConstantsService created and registered");

            return constantsService;
        }
    }
}
