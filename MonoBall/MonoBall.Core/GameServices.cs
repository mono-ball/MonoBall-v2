using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoBall.Core.ECS;
using MonoBall.Core.Maps;
using MonoBall.Core.Mods;
using MonoBall.Core.Mods.Utilities;
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
        public GameServices(Game game, GraphicsDevice graphicsDevice)
        {
            _game = game ?? throw new ArgumentNullException(nameof(game));
            _graphicsDevice =
                graphicsDevice ?? throw new ArgumentNullException(nameof(graphicsDevice));
        }

        /// <summary>
        /// Initializes core services (mods, ECS world). Should be called from Game.Initialize().
        /// </summary>
        public void Initialize()
        {
            if (IsInitialized)
            {
                Log.Warning("GameServices.Initialize: Services already initialized");
                return;
            }

            Log.Information("GameServices.Initialize: Initializing core services");

            // Load mods
            LoadMods();

            // Initialize ECS world
            if (ModManager != null)
            {
                EcsService = new EcsService();
                _game.Services.AddService(typeof(EcsService), EcsService);
                Log.Debug("GameServices.Initialize: ECS service registered");
            }
            else
            {
                Log.Warning(
                    "GameServices.Initialize: ModManager is null, ECS service not initialized"
                );
            }

            IsInitialized = true;
            Log.Information("GameServices.Initialize: Core services initialized");
        }

        /// <summary>
        /// Loads content services (tileset loader). Should be called from Game.LoadContent().
        /// </summary>
        public void LoadContent()
        {
            if (!IsInitialized)
            {
                Log.Warning(
                    "GameServices.LoadContent: Services not initialized. Call Initialize() first."
                );
                return;
            }

            if (TilesetLoaderService != null)
            {
                Log.Warning("GameServices.LoadContent: Content services already loaded");
                return;
            }

            Log.Information("GameServices.LoadContent: Loading content services");

            if (ModManager == null)
            {
                Log.Warning(
                    "GameServices.LoadContent: ModManager is null, cannot create TilesetLoaderService"
                );
                return;
            }

            // Create tileset loader service
            TilesetLoaderService = new TilesetLoaderService(_graphicsDevice, ModManager);
            _game.Services.AddService(typeof(TilesetLoaderService), TilesetLoaderService);
            Log.Debug("GameServices.LoadContent: TilesetLoaderService registered");

            Log.Information("GameServices.LoadContent: Content services loaded");
        }

        /// <summary>
        /// Loads all mods from the Mods directory.
        /// </summary>
        private void LoadMods()
        {
            string? modsDirectory = ModsPathResolver.FindModsDirectory();

            if (string.IsNullOrEmpty(modsDirectory) || !Directory.Exists(modsDirectory))
            {
                Log.Warning(
                    "GameServices.LoadMods: Mods directory not found. Mod system will not be available."
                );
                return;
            }

            // Initialize mod manager
            ModManager = new ModManager(modsDirectory);

            // Load mods and collect any errors
            var errors = new List<string>();
            bool success = ModManager.Load(errors);

            // Log errors and warnings
            if (errors.Count > 0)
            {
                Log.Warning("=== Mod Loading Issues ===");
                foreach (var error in errors)
                {
                    Log.Warning("{Error}", error);
                }
                Log.Warning("==========================");
            }

            if (success && ModManager != null)
            {
                Log.Information(
                    "Successfully loaded {ModCount} mod(s)",
                    ModManager.LoadedMods.Count
                );
                foreach (var mod in ModManager.LoadedMods)
                {
                    Log.Information(
                        "  - {ModName} ({ModId}) v{ModVersion}",
                        mod.Name,
                        mod.Id,
                        mod.Version
                    );
                }

                // Register ModManager as a service
                _game.Services.AddService(typeof(ModManager), ModManager);
                Log.Debug("GameServices.LoadMods: ModManager registered");
            }
            else
            {
                Log.Warning(
                    "GameServices.LoadMods: Mod loading completed with errors. Some mods may not be available."
                );
            }
        }
    }
}
