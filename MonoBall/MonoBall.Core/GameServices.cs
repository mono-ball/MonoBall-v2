using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoBall.Core.ECS;
using MonoBall.Core.Logging;
using MonoBall.Core.Maps;
using MonoBall.Core.Mods;
using MonoBall.Core.Mods.Utilities;
using MonoBall.Core.Rendering;
using Serilog;

namespace MonoBall.Core
{
    /// <summary>
    /// Manages all game services, including mod loading, ECS world, and content services.
    /// Handles initialization and registration of services with Game.Services.
    /// </summary>
    public class GameServices
    {
        private readonly Game _game;
        private readonly GraphicsDevice _graphicsDevice;
        private readonly ILogger _logger;

        /// <summary>
        /// Gets the mod manager. Will be null if mods failed to load.
        /// </summary>
        public ModManager? ModManager { get; private set; }

        /// <summary>
        /// Gets the ECS service.
        /// </summary>
        public EcsService? EcsService { get; private set; }

        /// <summary>
        /// Gets the tileset loader service. Will be null until LoadContent() is called.
        /// </summary>
        public TilesetLoaderService? TilesetLoaderService { get; private set; }

        /// <summary>
        /// Gets a value indicating whether services have been initialized.
        /// </summary>
        public bool IsInitialized { get; private set; }

        /// <summary>
        /// Initializes a new instance of the GameServices class.
        /// </summary>
        /// <param name="game">The game instance.</param>
        /// <param name="graphicsDevice">The graphics device.</param>
        /// <param name="logger">The logger for logging operations.</param>
        public GameServices(Game game, GraphicsDevice graphicsDevice, ILogger logger)
        {
            _game = game ?? throw new ArgumentNullException(nameof(game));
            _graphicsDevice =
                graphicsDevice ?? throw new ArgumentNullException(nameof(graphicsDevice));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Initializes core services (mods, ECS world). Should be called from Game.Initialize().
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

            // Initialize ECS world
            if (ModManager != null)
            {
                EcsService = new EcsService();
                _game.Services.AddService(typeof(EcsService), EcsService);
                _logger.Debug("ECS service registered");
            }
            else
            {
                _logger.Warning("ModManager is null, ECS service not initialized");
            }

            IsInitialized = true;
            _logger.Information("Core services initialized");
        }

        /// <summary>
        /// Loads content services (tileset loader). Should be called from Game.LoadContent().
        /// </summary>
        public void LoadContent()
        {
            if (!IsInitialized)
            {
                _logger.Warning("Services not initialized. Call Initialize() first.");
                return;
            }

            if (TilesetLoaderService != null)
            {
                _logger.Warning("Content services already loaded");
                return;
            }

            _logger.Information("Loading content services");

            if (ModManager == null)
            {
                _logger.Warning("ModManager is null, cannot create TilesetLoaderService");
                return;
            }

            // Create tileset loader service
            TilesetLoaderService = new TilesetLoaderService(
                _graphicsDevice,
                ModManager,
                LoggerFactory.CreateLogger<TilesetLoaderService>()
            );
            _game.Services.AddService(typeof(TilesetLoaderService), TilesetLoaderService);
            _logger.Debug("TilesetLoaderService registered");

            _logger.Information("Content services loaded");
        }

        /// <summary>
        /// Loads all mods from the Mods directory.
        /// Reuses existing ModManager from Game.Services if already loaded (e.g., by LoadModsSynchronously).
        /// </summary>
        private void LoadMods()
        {
            // Check if ModManager already exists in Game.Services (loaded synchronously for loading screen)
            var existingModManager = _game.Services.GetService<ModManager>();
            if (existingModManager != null)
            {
                _logger.Information("Reusing existing ModManager from Game.Services");
                ModManager = existingModManager;

                // Ensure FontService exists (should already exist from LoadModsSynchronously)
                // Use factory method to get or create FontService (preserves existing instance if present)
                var fontService = Mods.Utilities.FontServiceFactory.GetOrCreateFontService(
                    _game,
                    existingModManager,
                    _graphicsDevice,
                    LoggerFactory.CreateLogger<Rendering.FontService>()
                );
                _logger.Debug(
                    "FontService available (existing: {IsExisting})",
                    _game.Services.GetService<Rendering.FontService>() == fontService
                );
                return;
            }

            string? modsDirectory = ModsPathResolver.FindModsDirectory();

            if (string.IsNullOrEmpty(modsDirectory) || !Directory.Exists(modsDirectory))
            {
                _logger.Warning("Mods directory not found. Mod system will not be available.");
                return;
            }

            // Initialize mod manager
            ModManager = new ModManager(modsDirectory, LoggerFactory.CreateLogger<ModManager>());

            // Load mods and collect any errors
            var errors = new List<string>();
            bool success = ModManager.Load(errors);

            // Log errors collected from mod loading
            if (errors.Count > 0)
            {
                _logger.Warning("=== Mod Loading Issues ===");
                foreach (var error in errors)
                {
                    _logger.Warning("{Error}", error);
                }
                _logger.Warning("==========================");
            }

            if (success && ModManager != null)
            {
                _logger.Information(
                    "Successfully loaded {ModCount} mod(s)",
                    ModManager.LoadedMods.Count
                );
                foreach (var mod in ModManager.LoadedMods)
                {
                    _logger.Information(
                        "  - {ModName} ({ModId}) v{ModVersion}",
                        mod.Name,
                        mod.Id,
                        mod.Version
                    );
                }

                // Register ModManager as a service (if not already registered)
                if (_game.Services.GetService<ModManager>() == null)
                {
                    _game.Services.AddService(typeof(ModManager), ModManager);
                    _logger.Debug("ModManager registered");
                }

                // Create and register FontService immediately after mods load (if not already registered)
                // This ensures fonts are available for the loading screen
                // Uses factory method to prevent duplicate registration and preserve cached fonts
                Mods.Utilities.FontServiceFactory.GetOrCreateFontService(
                    _game,
                    ModManager,
                    _graphicsDevice,
                    LoggerFactory.CreateLogger<Rendering.FontService>()
                );
            }
            else
            {
                _logger.Warning(
                    "Mod loading completed with errors. Some mods may not be available."
                );
            }
        }
    }
}
