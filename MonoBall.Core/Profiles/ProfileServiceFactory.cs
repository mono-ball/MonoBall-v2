using System;
using Microsoft.Xna.Framework;
using MonoBall.Core.Mods;
using Serilog;

namespace MonoBall.Core.Profiles;

/// <summary>
///     Factory for creating and registering ProfileService instances.
///     Ensures ProfileService is created consistently.
/// </summary>
public static class ProfileServiceFactory
{
    /// <summary>
    ///     Creates and registers ProfileService in Game.Services.
    ///     Ensures ProfileService is created consistently.
    /// </summary>
    /// <param name="game">The game instance for accessing services.</param>
    /// <param name="modManager">The mod manager for ProfileService.</param>
    /// <param name="logger">The logger for logging operations.</param>
    /// <returns>The newly created ProfileService instance.</returns>
    /// <exception cref="ArgumentNullException">Thrown if game, modManager, or logger is null.</exception>
    public static IProfileService CreateProfileService(
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

        // Create new ProfileService
        logger.Debug("Creating new ProfileService");
        var profileService = new ProfileService(modManager, logger);

        // Register in Game.Services as interface type (for consistency with IResourceManager pattern)
        game.Services.AddService(typeof(IProfileService), profileService);
        logger.Debug("ProfileService created and registered");

        return profileService;
    }
}
