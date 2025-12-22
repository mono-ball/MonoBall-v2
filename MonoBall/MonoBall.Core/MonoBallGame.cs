using System;
using System.Collections.Generic;
using System.Globalization;
using Arch.Core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using MonoBall.Core.ECS;
using MonoBall.Core.ECS.Components;
using MonoBall.Core.Localization;
using MonoBall.Core.Logging;
using MonoBall.Core.Rendering;
using MonoBall.Core.Scenes;
using MonoBall.Core.Scenes.Components;
using Serilog;

namespace MonoBall.Core
{
    /// <summary>
    /// The main class for the game, responsible for managing game components, settings,
    /// and platform-specific configurations.
    /// </summary>
    public class MonoBallGame : Game
    {
        // Resources for drawing.
        private GraphicsDeviceManager graphicsDeviceManager;
        private SpriteBatch? spriteBatch;

        // Service and system management
        private GameServices? gameServices;
        private SystemManager? systemManager;
        private readonly ILogger _logger;

        /// <summary>
        /// Indicates if the game is running on a mobile platform.
        /// </summary>
        public readonly static bool IsMobile =
            OperatingSystem.IsAndroid() || OperatingSystem.IsIOS();

        /// <summary>
        /// Indicates if the game is running on a desktop platform.
        /// </summary>
        public readonly static bool IsDesktop =
            OperatingSystem.IsMacOS() || OperatingSystem.IsLinux() || OperatingSystem.IsWindows();

        /// <summary>
        /// Initializes a new instance of the game. Configures platform-specific settings,
        /// initializes services like settings and leaderboard managers, and sets up the
        /// screen manager for screen transitions.
        /// </summary>
        public MonoBallGame()
        {
            // Initialize logging first
            LoggerFactory.ConfigureLogger();
            _logger = LoggerFactory.CreateLogger<MonoBallGame>();
            _logger.Information("Initializing MonoBall game");

            graphicsDeviceManager = new GraphicsDeviceManager(this);

            // Set default window resolution
            graphicsDeviceManager.PreferredBackBufferWidth = 1280;
            graphicsDeviceManager.PreferredBackBufferHeight = 800;

            // Share GraphicsDeviceManager as a service.
            Services.AddService(typeof(GraphicsDeviceManager), graphicsDeviceManager);

            Content.RootDirectory = "Content";

            // Configure screen orientations.
            graphicsDeviceManager.SupportedOrientations =
                DisplayOrientation.LandscapeLeft | DisplayOrientation.LandscapeRight;

            // Enable window resizing
            Window.AllowUserResizing = true;
            IsMouseVisible = true;
        }

        /// <summary>
        /// Initializes the game, including setting up localization and core services.
        /// </summary>
        protected override void Initialize()
        {
            base.Initialize();

            // Load supported languages and set the default language.
            List<CultureInfo> cultures = LocalizationManager.GetSupportedCultures();
            var languages = new List<CultureInfo>();
            for (int i = 0; i < cultures.Count; i++)
            {
                languages.Add(cultures[i]);
            }

            // TODO You should load this from a settings file or similar,
            // based on what the user or operating system selected.
            var selectedLanguage = LocalizationManager.DEFAULT_CULTURE_CODE;
            LocalizationManager.SetCulture(selectedLanguage);

            // Initialize game services (mods, ECS world)
            // Only create if not already created (e.g., by LoadContent() being called first)
            if (gameServices == null)
            {
                gameServices = new GameServices(
                    this,
                    GraphicsDevice,
                    LoggerFactory.CreateLogger<GameServices>()
                );
                gameServices.Initialize();
            }
            else if (!gameServices.IsInitialized)
            {
                // If it was created but not initialized, initialize it now
                gameServices.Initialize();
            }
        }

        /// <summary>
        /// Loads game content, such as textures and particle systems.
        /// </summary>
        protected override void LoadContent()
        {
            base.LoadContent();

            _logger.Information("Starting content loading");

            spriteBatch = new SpriteBatch(GraphicsDevice);
            _logger.Debug("SpriteBatch created");

            // Ensure gameServices is initialized (in case LoadContent() is called before Initialize() completes)
            if (gameServices == null)
            {
                _logger.Warning(
                    "GameServices is null, initializing now (Initialize() may not have completed)"
                );
                gameServices = new GameServices(
                    this,
                    GraphicsDevice,
                    LoggerFactory.CreateLogger<GameServices>()
                );
                gameServices.Initialize();
            }

            // Load content services (tileset loader)
            gameServices.LoadContent();

            // Initialize ECS systems
            if (
                gameServices.ModManager != null
                && gameServices.EcsService != null
                && gameServices.TilesetLoaderService != null
            )
            {
                systemManager = new SystemManager(
                    gameServices.EcsService.World,
                    GraphicsDevice,
                    gameServices.ModManager,
                    gameServices.TilesetLoaderService,
                    LoggerFactory.CreateLogger<SystemManager>()
                );

                systemManager.Initialize(spriteBatch);

                // Create default camera
                var world = gameServices.EcsService!.World;
                var camera = new CameraComponent
                {
                    Position = new Vector2(10, 10), // Center at tile (10, 10)
                    Zoom = GameConstants.DefaultCameraZoom,
                    Rotation = GameConstants.DefaultCameraRotation,
                    TileWidth = GameConstants.DefaultTileWidth,
                    TileHeight = GameConstants.DefaultTileHeight,
                    IsActive = true,
                    IsDirty = true,
                };

                // Initialize viewport (GBA resolution: 240x160)
                // Viewport will be updated by CameraViewportSystem, but we initialize it here
                Rendering.CameraViewportSystem.UpdateViewportForResize(
                    ref camera,
                    GraphicsDevice.Viewport.Width,
                    GraphicsDevice.Viewport.Height,
                    GameConstants.GbaReferenceWidth,
                    GameConstants.GbaReferenceHeight
                );

                var cameraEntity = world.Create(camera);
                _logger.Information(
                    "Created default camera entity {EntityId} at tile position (10, 10) with viewport {ViewportWidth}x{ViewportHeight}",
                    cameraEntity.Id,
                    camera.Viewport.Width,
                    camera.Viewport.Height
                );

                // Initialize player system (creates player entity at camera position)
                systemManager.PlayerSystem.InitializePlayer();
                _logger.Information("Player system initialized");

                // Set camera to follow player
                var playerEntity = systemManager.PlayerSystem.GetPlayerEntity();
                if (playerEntity.HasValue)
                {
                    systemManager.CameraSystem.SetCameraFollowEntity(
                        cameraEntity,
                        playerEntity.Value
                    );
                    _logger.Information(
                        "Camera set to follow player entity {EntityId}",
                        playerEntity.Value.Id
                    );
                }
                else
                {
                    _logger.Warning("Player entity not found, camera will not follow player");
                }

                // Load the initial map
                _logger.Information("Loading initial map: base:map:hoenn/littleroot_town");
                systemManager.MapLoaderSystem.LoadMap("base:map:hoenn/littleroot_town");
                _logger.Information("Initial map loaded");

                // Force camera position update after map load to ensure player is centered
                if (playerEntity.HasValue)
                {
                    systemManager.CameraSystem.UpdateCameraPosition(cameraEntity);
                    _logger.Information("Camera position updated after map load");
                }

                // Create initial GameScene entity
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
                _logger.Information("Created initial GameScene");
            }
            else
            {
                _logger.Warning("Cannot initialize ECS systems - required services are null");
            }

            _logger.Information("Content loading completed");
        }

        /// <summary>
        /// Updates the game's logic, called once per frame.
        /// </summary>
        /// <param name="gameTime">
        /// Provides a snapshot of timing values used for game updates.
        /// </param>
        protected override void Update(GameTime gameTime)
        {
            // Exit the game if the Back button (GamePad) or Escape key (Keyboard) is pressed.
            if (
                GamePad.GetState(PlayerIndex.One).Buttons.Back == ButtonState.Pressed
                || Keyboard.GetState().IsKeyDown(Keys.Escape)
            )
                Exit();

            // Update ECS systems (CameraViewportSystem handles window resize)
            systemManager?.Update(gameTime);

            // Process input for scenes
            if (systemManager != null)
            {
                var keyboardState = Keyboard.GetState();
                var mouseState = Mouse.GetState();
                var gamePadState = GamePad.GetState(PlayerIndex.One);
                systemManager.SceneInputSystem.ProcessInput(
                    keyboardState,
                    mouseState,
                    gamePadState
                );
            }

            base.Update(gameTime);
        }

        /// <summary>
        /// Draws the game's graphics, called once per frame.
        /// </summary>
        /// <param name="gameTime">
        /// Provides a snapshot of timing values used for rendering.
        /// </param>
        protected override void Draw(GameTime gameTime)
        {
            // Clears the screen with the MonoGame orange color before drawing.
            GraphicsDevice.Clear(Color.MonoGameOrange);

            // Render ECS systems
            systemManager?.Render(gameTime);

            base.Draw(gameTime);
        }

        /// <summary>
        /// Performs cleanup when the game is disposed.
        /// </summary>
        /// <param name="disposing">True if managed resources should be disposed.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                systemManager?.Dispose();
                spriteBatch?.Dispose();
                EcsWorld.Reset();

                _logger.Information("Shutting down MonoBall game");
                LoggerFactory.CloseAndFlush();
            }
            base.Dispose(disposing);
        }
    }
}
