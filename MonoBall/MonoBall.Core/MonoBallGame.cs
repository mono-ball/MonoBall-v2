using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using Arch.Core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using MonoBall.Core.ECS;
using MonoBall.Core.ECS.Components;
using MonoBall.Core.Localization;
using MonoBall.Core.Logging;
using MonoBall.Core.Mods;
using MonoBall.Core.Rendering;
using MonoBall.Core.Scenes;
using MonoBall.Core.Scenes.Components;
using MonoBall.Core.Scenes.Systems;
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

        // Async initialization
        private GameInitializationService? _initializationService;
        private Task<GameInitializationService.InitializationResult>? _initializationTask;
        private Entity? _loadingSceneEntity;

        // Track if we've shown 100% progress for at least one frame before transitioning
        private bool _hasShownCompleteProgress = false;

        /// <summary>
        /// Indicates if the game is running on a mobile platform.
        /// </summary>
        public static readonly bool IsMobile =
            OperatingSystem.IsAndroid() || OperatingSystem.IsIOS();

        /// <summary>
        /// Indicates if the game is running on a desktop platform.
        /// </summary>
        public static readonly bool IsDesktop =
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
        /// Initializes the game. Sets up minimal initialization for loading screen.
        /// Actual game loading happens asynchronously in LoadContent().
        /// </summary>
        protected override void Initialize()
        {
            base.Initialize();

            _logger.Information("Game window initialized, preparing for async loading");

            // Note: We don't load mods or services here - that happens asynchronously
            // This allows the window to appear immediately while loading happens in the background
        }

        /// <summary>
        /// Loads game content. Creates main world early, loading scene, and starts async initialization.
        /// </summary>
        protected override void LoadContent()
        {
            base.LoadContent();

            _logger.Information("Starting async content loading");

            // Load all mods synchronously first for system-critical resources (fonts, etc.)
            // Core mod (slot 0 in mod.manifest) loads first, then other mods
            // This ensures FontService is available when the loading screen renders
            LoadModsSynchronously();

            // Create main world early (empty, just for scenes)
            // This ensures the world exists before async initialization starts
            var mainWorld = EcsWorld.Instance;
            _logger.Debug("Main world created early for loading scene");

            // Create minimal GameServices for early SystemManager initialization
            var modManager = Services.GetService<Mods.ModManager>();
            if (modManager == null)
            {
                throw new InvalidOperationException(
                    "ModManager not found in Game.Services after LoadModsSynchronously()"
                );
            }

            // Create minimal services required for early SystemManager initialization
            var ecsService = GameInitializationHelper.EnsureEcsService(this, _logger);
            var flagVariableService = GameInitializationHelper.EnsureFlagVariableService(
                this,
                ecsService,
                _logger
            );
            var tilesetLoaderService = GameInitializationHelper.EnsureTilesetLoaderService(
                this,
                GraphicsDevice,
                modManager,
                _logger
            );

            // Create sprite batch for early systems
            var loadingSpriteBatch = new SpriteBatch(GraphicsDevice);

            // Initialize SystemManager early (creates SceneSystem, LoadingSceneRendererSystem, LoadingSceneSystem first)
            var earlySystemManager = new SystemManager(
                mainWorld,
                GraphicsDevice,
                modManager,
                tilesetLoaderService,
                this,
                LoggerFactory.CreateLogger<SystemManager>()
            );
            earlySystemManager.Initialize(loadingSpriteBatch);

            // Create initialization service
            _initializationService = new GameInitializationService(
                this,
                GraphicsDevice,
                LoggerFactory.CreateLogger<GameInitializationService>()
            );

            // Set LoadingSceneSystem for progress updates
            _initializationService.SetLoadingSceneSystem(earlySystemManager.LoadingSceneSystem);

            // Create loading scene in main world and start async initialization
            (_loadingSceneEntity, _initializationTask) =
                _initializationService.CreateLoadingSceneAndStartInitialization(
                    mainWorld,
                    earlySystemManager.SceneSystem,
                    GraphicsDevice,
                    loadingSpriteBatch
                );

            // Store early SystemManager temporarily (will be replaced by async initialization result)
            systemManager = earlySystemManager;
            spriteBatch = loadingSpriteBatch;

            _logger.Information(
                "Loading scene created in main world, async initialization started"
            );
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

            // Check if initialization is complete
            if (_initializationTask != null && _initializationTask.IsCompleted)
            {
                if (!_initializationTask.IsFaulted && !_initializationTask.IsCanceled)
                {
                    var result = _initializationTask.Result;
                    // Check if we need to transition to the new SystemManager from async initialization
                    if (result.Success && systemManager != result.SystemManager)
                    {
                        // Validate initialization result
                        if (
                            result.GameServices == null
                            || result.SystemManager == null
                            || result.SpriteBatch == null
                        )
                        {
                            _logger.Error(
                                "Initialization succeeded but required properties are null. GameServices: {GameServices}, SystemManager: {SystemManager}, SpriteBatch: {SpriteBatch}",
                                result.GameServices != null,
                                result.SystemManager != null,
                                result.SpriteBatch != null
                            );
                            // Keep loading scene visible to show error
                            return;
                        }

                        // Check if we've shown 100% progress (wait for at least one frame after completion)
                        if (_loadingSceneEntity != null)
                        {
                            var world = EcsWorld.Instance;
                            if (world.Has<LoadingProgressComponent>(_loadingSceneEntity.Value))
                            {
                                ref var progressComponent = ref world.Get<LoadingProgressComponent>(
                                    _loadingSceneEntity.Value
                                );

                                // Check if progress is 100% and complete
                                bool isComplete =
                                    progressComponent.IsComplete
                                    && Math.Abs(progressComponent.Progress - 1.0f) < 0.001f;

                                if (isComplete)
                                {
                                    // Mark that we've seen 100% - next frame we can transition
                                    if (!_hasShownCompleteProgress)
                                    {
                                        _hasShownCompleteProgress = true;
                                        _logger.Debug(
                                            "Loading reached 100% - will transition next frame"
                                        );
                                        return; // Wait one more frame to show 100%
                                    }
                                }
                                else
                                {
                                    // Not complete yet, wait
                                    return;
                                }
                            }
                            else
                            {
                                // Component missing, wait
                                return;
                            }
                        }
                        else
                        {
                            // Entity missing, wait
                            return;
                        }

                        // Transition from loading to game
                        _logger.Information(
                            "Game initialization complete, transitioning to game scene"
                        );

                        gameServices = result.GameServices;
                        systemManager = result.SystemManager;
                        spriteBatch = result.SpriteBatch;

                        // Dispose early SystemManager (will be replaced by the one from async initialization)
                        var earlySystemManager = systemManager;
                        if (
                            earlySystemManager != null
                            && earlySystemManager != result.SystemManager
                        )
                        {
                            try
                            {
                                earlySystemManager.Dispose();
                            }
                            catch (Exception ex)
                            {
                                _logger.Warning(ex, "Error disposing early SystemManager");
                            }
                        }

                        // Destroy loading scene (now managed by new SystemManager's SceneSystem)
                        if (_loadingSceneEntity != null && result.SystemManager != null)
                        {
                            try
                            {
                                result.SystemManager.SceneSystem.DestroyScene(
                                    _loadingSceneEntity.Value
                                );
                            }
                            catch (Exception ex)
                            {
                                _logger.Warning(
                                    ex,
                                    "Error destroying loading scene: {Error}",
                                    ex.Message
                                );
                            }
                            _loadingSceneEntity = null;
                        }

                        _initializationService = null;
                        _initializationTask = null;
                    }
                    else if (!result.Success)
                    {
                        _logger.Error(
                            "Game initialization failed: {ErrorMessage}",
                            result.ErrorMessage
                        );
                        // Keep loading scene visible to show error
                    }
                }
            }

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
            // Determine background color from active scene system
            // Fail fast if systemManager is not initialized
            if (systemManager == null)
            {
                throw new InvalidOperationException(
                    "Cannot render: SystemManager is null. "
                        + "Ensure game initialization completed successfully before calling Draw()."
                );
            }

            // Use SceneSystem to determine background color based on active scenes
            Color backgroundColor = systemManager.SceneSystem.GetBackgroundColor();

            GraphicsDevice.Clear(backgroundColor);

            // Render ECS systems (includes loading scene if still loading, or game scene if loaded)
            // Loading scene blocks draw, so game scene won't render until loading completes
            if (systemManager != null)
            {
                // Use SystemManager's rendering (includes SceneRendererSystem)
                systemManager.Render(gameTime);
            }

            base.Draw(gameTime);
        }

        /// <summary>
        /// Loads all mods synchronously before async initialization, ensuring core mod (slot 0 in mod.manifest) loads first.
        /// This ensures system-critical resources like fonts are available for the loading screen.
        /// </summary>
        private void LoadModsSynchronously()
        {
            _logger.Information(
                "Loading all mods synchronously for system-critical resources (core mod loads first)"
            );

            string? modsDirectory = Mods.Utilities.ModsPathResolver.FindModsDirectory();
            if (string.IsNullOrEmpty(modsDirectory) || !Directory.Exists(modsDirectory))
            {
                throw new InvalidOperationException(
                    $"Mods directory not found: {modsDirectory}. "
                        + "Cannot load mods. Ensure Mods directory exists."
                );
            }

            // Create ModManager and load all mods (core mod loads first)
            var modManager = new Mods.ModManager(
                modsDirectory,
                LoggerFactory.CreateLogger<Mods.ModManager>()
            );

            // Load mods (core mod loads first, then others)
            var errors = new List<string>();
            bool success = modManager.Load(errors);

            if (!success)
            {
                throw new InvalidOperationException(
                    $"Failed to load mods. Errors: {string.Join("; ", errors)}"
                );
            }

            // Register ModManager in Game.Services
            Services.AddService(typeof(Mods.ModManager), modManager);
            _logger.Debug(
                "ModManager loaded and registered with {ModCount} mod(s)",
                modManager.LoadedMods.Count
            );

            // Create and register FontService immediately after mods load
            // This ensures fonts are available for the loading screen
            Mods.Utilities.FontServiceFactory.GetOrCreateFontService(
                this,
                modManager,
                GraphicsDevice,
                LoggerFactory.CreateLogger<Rendering.FontService>()
            );
            _logger.Debug("FontService created and registered");

            _logger.Information(
                "All mods loaded successfully ({ModCount} mods, core mod: {CoreModId}), FontService available",
                modManager.LoadedMods.Count,
                modManager.CoreMod?.Id ?? "unknown"
            );
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
