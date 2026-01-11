using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoBall.Core.ECS;
using MonoBall.Core.Logging;
using MonoBall.Core.Mods;
using MonoBall.Core.Mods.Utilities;
using MonoBall.Core.Profiles;
using MonoBall.Core.Rendering;
using MonoBall.Core.Resources;
using Serilog;

namespace MonoBall.Core;

/// <summary>
///     Manages all game services, including mod loading, ECS world, and content services.
///     Handles initialization and registration of services with Game.Services.
/// </summary>
public class GameServices
{
    private readonly Game _game;
    private readonly GraphicsDevice _graphicsDevice;
    private readonly ILogger _logger;

    /// <summary>
    ///     Initializes a new instance of the GameServices class.
    /// </summary>
    /// <param name="game">The game instance.</param>
    /// <param name="graphicsDevice">The graphics device.</param>
    /// <param name="logger">The logger for logging operations.</param>
    public GameServices(Game game, GraphicsDevice graphicsDevice, ILogger logger)
    {
        _game = game ?? throw new ArgumentNullException(nameof(game));
        _graphicsDevice = graphicsDevice ?? throw new ArgumentNullException(nameof(graphicsDevice));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    ///     Gets the mod manager. Will be null if mods failed to load.
    /// </summary>
    public ModManager? ModManager { get; private set; }

    /// <summary>
    ///     Gets the ECS service.
    /// </summary>
    public EcsService? EcsService { get; private set; }

    /// <summary>
    ///     Gets the resource manager. Will be null until LoadContent() is called.
    /// </summary>
    public IResourceManager? ResourceManager { get; private set; }

    /// <summary>
    ///     Gets a value indicating whether services have been initialized.
    /// </summary>
    public bool IsInitialized { get; private set; }

    /// <summary>
    ///     Initializes core services (mods, ECS world). Should be called from Game.Initialize().
    /// </summary>
    public void Initialize()
    {
        if (IsInitialized)
        {
            _logger.Warning("Services already initialized");
            return;
        }

        _logger.Information("Initializing core services");

        // Load mods
        LoadMods();

        // Initialize ECS world (only if not already initialized)
        if (ModManager != null)
        {
            EcsService = GameInitializationHelper.EnsureEcsService(_game, _logger);
            GameInitializationHelper.EnsureFlagVariableService(_game, EcsService, _logger);
        }
        else
        {
            _logger.Warning("ModManager is null, ECS service not initialized");
        }

        IsInitialized = true;
        _logger.Information("Core services initialized");
    }

    /// <summary>
    ///     Loads all mods from the Mods directory.
    ///     Reuses existing ModManager from Game.Services if already loaded (e.g., by LoadModsSynchronously).
    /// </summary>
    private void LoadMods()
    {
        // Check if ModManager already exists in Game.Services (loaded synchronously for loading screen)
        var existingModManager = _game.Services.GetService<ModManager>();
        if (existingModManager != null)
        {
            _logger.Information("Reusing existing ModManager from Game.Services");
            ModManager = existingModManager;

            // Ensure ProfileService exists (should already exist from LoadModsSynchronously)
            var existingProfileService = _game.Services.GetService<IProfileService>();
            if (existingProfileService == null)
            {
                _logger.Warning("ProfileService not found in Game.Services, creating it now");
                ProfileServiceFactory.CreateProfileService(
                    _game,
                    ModManager,
                    LoggerFactory.CreateLogger<ProfileService>()
                );
            }

            // Ensure ResourceManager exists (should already exist from LoadModsSynchronously)
            var existingResourceManager = _game.Services.GetService<IResourceManager>();
            if (existingResourceManager != null)
            {
                ResourceManager = existingResourceManager;
                _logger.Debug("ResourceManager already available from Game.Services");
            }

            return;
        }

        var modsDirectory = ModsPathResolver.FindModsDirectory();

        if (string.IsNullOrEmpty(modsDirectory) || !Directory.Exists(modsDirectory))
        {
            _logger.Warning("Mods directory not found. Mod system will not be available.");
            return;
        }

        // Initialize mod manager
        ModManager = new ModManager(LoggerFactory.CreateLogger<ModManager>(), modsDirectory);

        // Load mods and collect any errors
        var errors = new List<string>();
        var success = ModManager.Load(errors);

        // Log errors collected from mod loading
        if (errors.Count > 0)
        {
            _logger.Warning("=== Mod Loading Issues ===");
            foreach (var error in errors)
                _logger.Warning("{Error}", error);
            _logger.Warning("==========================");
        }

        if (success && ModManager != null)
        {
            _logger.Information(
                "Successfully loaded {ModCount} mod(s)",
                ModManager.LoadedMods.Count
            );
            foreach (var mod in ModManager.LoadedMods)
                _logger.Information(
                    "  - {ModName} ({ModId}) v{ModVersion}",
                    mod.Name,
                    mod.Id,
                    mod.Version
                );

            // Register ModManager as a service (if not already registered)
            if (_game.Services.GetService<ModManager>() == null)
            {
                _game.Services.AddService(typeof(ModManager), ModManager);
                _logger.Debug("ModManager registered");
            }

            // Create and register ProfileService BEFORE ResourceManager (CRITICAL ORDER)
            // Check if ProfileService already exists (from MonoBallGame.LoadModsSynchronously)
            var existingProfileService = _game.Services.GetService<IProfileService>();
            IProfileService profileService;
            if (existingProfileService == null)
            {
                profileService = ProfileServiceFactory.CreateProfileService(
                    _game,
                    ModManager,
                    LoggerFactory.CreateLogger<ProfileService>()
                );
                _logger.Debug("ProfileService created and registered");
            }
            else
            {
                profileService = existingProfileService;
                _logger.Debug("ProfileService already registered");
            }

            // Create and register ResourceManager immediately after ProfileService (if not already registered)
            // This ensures resources are available for the loading screen
            // NOTE: ResourceManager now requires IProfileService (CRITICAL: ProfileService must be available before any sprite loading)
            var existingResourceManager = _game.Services.GetService<IResourceManager>();
            if (existingResourceManager == null)
            {
                var pathResolver = new ResourcePathResolver(
                    ModManager,
                    LoggerFactory.CreateLogger<ResourcePathResolver>()
                );
                _game.Services.AddService(typeof(IResourcePathResolver), pathResolver);

                ResourceManager = new ResourceManager(
                    _graphicsDevice,
                    ModManager,
                    pathResolver,
                    profileService, // NEW: Required dependency - ProfileService must be available before sprite loading
                    LoggerFactory.CreateLogger<ResourceManager>()
                );
                _game.Services.AddService(typeof(IResourceManager), ResourceManager);
                _logger.Debug("ResourceManager created and registered for loading screen");
            }
            else
            {
                ResourceManager = existingResourceManager;
                _logger.Debug("ResourceManager already registered");
            }
        }
        else
        {
            _logger.Warning("Mod loading completed with errors. Some mods may not be available.");
        }
    }
}
