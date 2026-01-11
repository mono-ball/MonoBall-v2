using System;
using Microsoft.Xna.Framework;
using MonoBall.Core.Mods;
using Serilog;

namespace MonoBall.Core.Constants;

/// <summary>
///     Factory for creating and registering ConstantsService instances.
///     Ensures ConstantsService is created consistently.
/// </summary>
public static class ConstantsServiceFactory
{
    /// <summary>
    ///     Creates and registers ConstantsService in Game.Services.
    ///     Ensures ConstantsService is created consistently.
    /// </summary>
    /// <param name="game">The game instance for accessing services.</param>
    /// <param name="modManager">The mod manager for ConstantsService.</param>
    /// <param name="logger">The logger for logging operations.</param>
    /// <returns>The newly created ConstantsService instance.</returns>
    /// <exception cref="ArgumentNullException">Thrown if game, modManager, or logger is null.</exception>
    public static IConstantsService CreateConstantsService(
        Game game,
        IModManager modManager,
        ILogger logger
    )
    {
        if (game == null)
            throw new ArgumentNullException(nameof(game));
        if (modManager == null)
            throw new ArgumentNullException(nameof(modManager));
        if (logger == null)
            throw new ArgumentNullException(nameof(logger));

        // Create new ConstantsService
        logger.Debug("Creating new ConstantsService");
        var constantsService = new ConstantsService(modManager, logger);

        // Register in Game.Services as interface type (for consistency with IResourceManager pattern)
        game.Services.AddService(typeof(IConstantsService), constantsService);
        logger.Debug("ConstantsService created and registered");

        return constantsService;
    }
}
