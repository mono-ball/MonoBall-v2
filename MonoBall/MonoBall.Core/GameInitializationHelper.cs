using System;
using System.Collections.Generic;
using Arch.Core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoBall.Core.ECS;
using MonoBall.Core.ECS.Components;
using MonoBall.Core.ECS.Services;
using MonoBall.Core.Localization;
using MonoBall.Core.Logging;
using MonoBall.Core.Mods;
using MonoBall.Core.Scenes;
using MonoBall.Core.Scenes.Components;
using Serilog;

namespace MonoBall.Core
{
    /// <summary>
    /// Helper class containing reusable game initialization steps.
    /// Eliminates code duplication between async and sync initialization paths.
    /// </summary>
    public static class GameInitializationHelper
    {
        /// <summary>
        /// Loads localization settings.
        /// </summary>
        /// <param name="logger">The logger for logging operations.</param>
        public static void LoadLocalization(ILogger logger)
        {
            var cultures = LocalizationManager.GetSupportedCultures();
            LocalizationManager.SetCulture(LocalizationManager.DEFAULT_CULTURE_CODE);
            logger.Debug("Localization loaded");
        }

        /// <summary>
        /// Initializes game services (mods, ECS world).
        /// </summary>
        /// <param name="game">The game instance.</param>
        /// <param name="graphicsDevice">The graphics device.</param>
        /// <param name="logger">The logger for logging operations.</param>
        /// <returns>The initialized game services.</returns>
        public static GameServices InitializeGameServices(
            Game game,
            GraphicsDevice graphicsDevice,
            ILogger logger
        )
        {
            var gameServices = new GameServices(
                game,
                graphicsDevice,
                LoggerFactory.CreateLogger<GameServices>()
            );
            gameServices.Initialize();
            logger.Debug("Game services initialized");
            return gameServices;
        }

        /// <summary>
        /// Loads content services (tileset loader).
        /// </summary>
        /// <param name="gameServices">The game services.</param>
        /// <param name="logger">The logger for logging operations.</param>
        public static void LoadContentServices(GameServices gameServices, ILogger logger)
        {
            gameServices.LoadContent();
            logger.Debug("Content services loaded");
        }

        /// <summary>
        /// Creates a sprite batch for game rendering.
        /// </summary>
        /// <param name="graphicsDevice">The graphics device.</param>
        /// <param name="logger">The logger for logging operations.</param>
        /// <returns>The created sprite batch.</returns>
        public static SpriteBatch CreateSpriteBatch(GraphicsDevice graphicsDevice, ILogger logger)
        {
            var spriteBatch = new SpriteBatch(graphicsDevice);
            logger.Debug("SpriteBatch created");
            return spriteBatch;
        }

        /// <summary>
        /// Initializes ECS systems.
        /// </summary>
        /// <param name="gameServices">The game services.</param>
        /// <param name="graphicsDevice">The graphics device.</param>
        /// <param name="spriteBatch">The sprite batch.</param>
        /// <param name="game">The game instance.</param>
        /// <param name="logger">The logger for logging operations.</param>
        /// <returns>The initialized system manager.</returns>
        /// <exception cref="InvalidOperationException">Thrown if required services are null.</exception>
        public static SystemManager InitializeEcsSystems(
            GameServices gameServices,
            GraphicsDevice graphicsDevice,
            SpriteBatch spriteBatch,
            Game game,
            ILogger logger
        )
        {
            if (
                gameServices.ModManager == null
                || gameServices.EcsService == null
                || gameServices.TilesetLoaderService == null
            )
            {
                throw new InvalidOperationException(
                    "Cannot initialize ECS systems - required services are null"
                );
            }

            var systemManager = new SystemManager(
                gameServices.EcsService.World,
                graphicsDevice,
                gameServices.ModManager,
                gameServices.TilesetLoaderService,
                game,
                LoggerFactory.CreateLogger<SystemManager>()
            );

            systemManager.Initialize(spriteBatch);
            logger.Debug("ECS systems initialized");
            return systemManager;
        }

        /// <summary>
        /// Creates the default camera entity.
        /// </summary>
        /// <param name="world">The ECS world.</param>
        /// <param name="modManager">The mod manager for tile size configuration.</param>
        /// <param name="graphicsDevice">The graphics device.</param>
        /// <param name="logger">The logger for logging operations.</param>
        /// <returns>The created camera entity.</returns>
        /// <exception cref="InvalidOperationException">Thrown if ModManager is null.</exception>
        public static Entity CreateDefaultCamera(
            World world,
            IModManager modManager,
            GraphicsDevice graphicsDevice,
            ILogger logger
        )
        {
            if (modManager == null)
            {
                throw new InvalidOperationException(
                    "Cannot initialize camera: ModManager is required for tile size configuration."
                );
            }

            int tileWidth = modManager.GetTileWidth();
            int tileHeight = modManager.GetTileHeight();

            var camera = new CameraComponent
            {
                Position = new Vector2(10, 10),
                Zoom = GameConstants.DefaultCameraZoom,
                Rotation = GameConstants.DefaultCameraRotation,
                TileWidth = tileWidth,
                TileHeight = tileHeight,
                IsActive = true,
                IsDirty = true,
            };

            Rendering.CameraViewportSystem.UpdateViewportForResize(
                ref camera,
                graphicsDevice.Viewport.Width,
                graphicsDevice.Viewport.Height,
                GameConstants.GbaReferenceWidth,
                GameConstants.GbaReferenceHeight
            );

            var cameraEntity = world.Create(camera);
            logger.Information("Created default camera entity {EntityId}", cameraEntity.Id);
            return cameraEntity;
        }

        /// <summary>
        /// Initializes the player system and sets up camera following.
        /// </summary>
        /// <param name="systemManager">The system manager.</param>
        /// <param name="cameraEntity">The camera entity.</param>
        /// <param name="logger">The logger for logging operations.</param>
        /// <returns>The player entity, or null if not found.</returns>
        public static Entity? InitializePlayer(
            SystemManager systemManager,
            Entity cameraEntity,
            ILogger logger
        )
        {
            systemManager.PlayerSystem.InitializePlayer();
            logger.Information("Player system initialized");

            var playerEntity = systemManager.PlayerSystem.GetPlayerEntity();
            if (playerEntity.HasValue)
            {
                systemManager.CameraSystem.SetCameraFollowEntity(cameraEntity, playerEntity.Value);
                logger.Information(
                    "Camera set to follow player entity {EntityId}",
                    playerEntity.Value.Id
                );
            }

            return playerEntity;
        }

        /// <summary>
        /// Loads the initial map.
        /// </summary>
        /// <param name="systemManager">The system manager.</param>
        /// <param name="mapId">The map ID to load.</param>
        /// <param name="playerEntity">The player entity (optional, for camera positioning).</param>
        /// <param name="cameraEntity">The camera entity (optional, for camera positioning).</param>
        /// <param name="logger">The logger for logging operations.</param>
        public static void LoadInitialMap(
            SystemManager systemManager,
            string mapId,
            Entity? playerEntity,
            Entity? cameraEntity,
            ILogger logger
        )
        {
            logger.Information("Loading initial map: {MapId}", mapId);
            systemManager.MapLoaderSystem.LoadMap(mapId);
            logger.Information("Initial map loaded");

            if (playerEntity.HasValue && cameraEntity.HasValue)
            {
                systemManager.CameraSystem.UpdateCameraPosition(cameraEntity.Value);
                logger.Information("Camera position updated after map load");
            }
        }

        /// <summary>
        /// Sets up initial game state variables and flags.
        /// This is a temporary setup for testing variable sprite resolution.
        /// </summary>
        /// <param name="game">The game instance.</param>
        /// <param name="logger">The logger for logging operations.</param>
        public static void SetupInitialGameState(Game game, ILogger logger)
        {
            var flagVariableService = game.Services.GetService<IFlagVariableService>();
            if (flagVariableService == null)
            {
                logger.Warning(
                    "FlagVariableService not found in Game.Services. Cannot set up initial game state."
                );
                return;
            }

            // Set variable sprite ID for rival
            flagVariableService.SetVariable(
                "base:sprite:npcs/generic/var_rival",
                "base:sprite:players/brendan/normal"
            );
            logger.Information(
                "Set game state variable base:sprite:npcs/generic/var_rival = base:sprite:players/brendan/normal"
            );

            // Set visibility flag for littleroot town rival (temporary)
            flagVariableService.SetFlag("base:flag:visibility/littleroot_town_rival", true);
            logger.Information(
                "Set game state flag base:flag:visibility/littleroot_town_rival = true"
            );
        }

        /// <summary>
        /// Creates the initial game scene.
        /// </summary>
        /// <param name="systemManager">The system manager.</param>
        /// <param name="logger">The logger for logging operations.</param>
        public static void CreateGameScene(SystemManager systemManager, ILogger logger)
        {
            var gameSceneComponent = new SceneComponent
            {
                SceneId = "game:main",
                Priority = ScenePriorities.GameScene,
                CameraMode = SceneCameraMode.GameCamera,
                CameraEntityId = null,
                BlocksUpdate = false,
                BlocksDraw = false,
                BlocksInput = false,
                IsActive = true,
                IsPaused = false,
            };

            systemManager.SceneManagerSystem.CreateScene(
                gameSceneComponent,
                new GameSceneComponent()
            );
            logger.Information("Created initial GameScene");
        }

        /// <summary>
        /// Creates a test shader entity for testing shader functionality.
        /// </summary>
        /// <param name="world">The ECS world.</param>
        /// <param name="logger">The logger for logging operations.</param>
        public static void CreateTestShader(World world, ILogger logger)
        {
            // Test 1: Tile layer shader with color grading effect
            // Uncomment to test tile layer shader
            /*
            var tileShaderComponent = new LayerShaderComponent
            {
                Layer = ShaderLayer.TileLayer,
                ShaderId = "TileLayerColorGrading",
                IsEnabled = true,
                RenderOrder = 0,
                Parameters = new Dictionary<string, object>
                {
                    { "Brightness", 0.1f }, // Slightly brighter
                    { "Contrast", 1.2f }, // Increased contrast
                    { "Saturation", 0.9f }, // Slightly desaturated
                    { "ColorTint", new Vector3(1.0f, 1.0f, 1.0f) }, // White tint (no change)
                },
            };

            var tileShaderEntity = world.Create(tileShaderComponent);
            logger.Information(
                "Created test tile layer shader entity {EntityId} with shader {ShaderId}",
                tileShaderEntity.Id,
                tileShaderComponent.ShaderId
            );
            */

            // No default combined layer shader - start with no post-processing effects
            // Users can press F4 to cycle through available shaders
            logger.Debug("No default combined layer shader - press F4 to cycle through shaders");

            // Alternative: Chromatic Aberration effect (color separation at edges)
            // Uncomment to try chromatic aberration instead of bloom
            /*
            var chromaticShaderComponent = new LayerShaderComponent
            {
                Layer = ShaderLayer.CombinedLayer,
                ShaderId = "CombinedLayerChromaticAberration",
                IsEnabled = true,
                RenderOrder = 0,
                Parameters = new Dictionary<string, object>
                {
                    { "AberrationAmount", 0.008f }, // How much color separation
                    // ScreenSize is set dynamically, don't include it here
                },
            };

            var chromaticShaderEntity = world.Create(chromaticShaderComponent);
            logger.Information(
                "Created test combined layer shader entity {EntityId} with shader {ShaderId}",
                chromaticShaderEntity.Id,
                chromaticShaderComponent.ShaderId
            );
            */
        }
    }
}
