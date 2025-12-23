using System;
using System.Collections.Concurrent;
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
        private SceneManagerSystem? _earlySceneManager; // Early scene manager (replaced by SystemManager's later)
        private SceneRendererSystem? _earlySceneRenderer; // Early scene renderer (replaced by SystemManager's later)
        private LoadingSceneRendererSystem? _earlyLoadingRenderer; // Early loading renderer (replaced by SystemManager's later)
        private SpriteBatch? _loadingSpriteBatch;

        // Thread-safe progress update queue (marshals updates from async task to main thread)
        private readonly ConcurrentQueue<(float progress, string step)> _progressUpdateQueue =
            new();

        // Track if we've shown 100% progress for at least one frame before transitioning
        private bool _hasShownCompleteProgress = false;

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

            // Create sprite batch for loading screen
            _loadingSpriteBatch = new SpriteBatch(GraphicsDevice);

            // Create main world early (empty, just for scenes)
            // This ensures the world exists before async initialization starts
            var mainWorld = EcsWorld.Instance;
            _logger.Debug("Main world created early for loading scene");

            // Create early scene manager and renderer for loading scene
            // These will be replaced by SystemManager's systems when initialization completes
            _earlySceneManager = new SceneManagerSystem(
                mainWorld,
                LoggerFactory.CreateLogger<SceneManagerSystem>()
            );

            _earlySceneRenderer = new SceneRendererSystem(
                mainWorld,
                GraphicsDevice,
                _earlySceneManager,
                LoggerFactory.CreateLogger<SceneRendererSystem>()
            );
            _earlySceneRenderer.SetSpriteBatch(_loadingSpriteBatch);

            // Create loading scene renderer
            _earlyLoadingRenderer = new LoadingSceneRendererSystem(
                mainWorld,
                GraphicsDevice,
                _loadingSpriteBatch,
                this,
                LoggerFactory.CreateLogger<LoadingSceneRendererSystem>()
            );
            _earlySceneRenderer.SetLoadingSceneRendererSystem(_earlyLoadingRenderer);

            // Create initialization service with progress queue for thread-safe updates
            _initializationService = new GameInitializationService(
                this,
                GraphicsDevice,
                LoggerFactory.CreateLogger<GameInitializationService>(),
                _progressUpdateQueue
            );

            // Create loading scene in main world and start async initialization
            (_loadingSceneEntity, _initializationTask) =
                _initializationService.CreateLoadingSceneAndStartInitialization(
                    mainWorld,
                    _earlySceneManager
                );

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

            // Process thread-safe progress updates from async initialization task
            // This marshals updates from the background thread to the main thread's ECS world
            _initializationService?.ProcessProgressUpdates();

            // Check if initialization is complete
            if (_initializationTask != null && _initializationTask.IsCompleted)
            {
                if (!_initializationTask.IsFaulted && !_initializationTask.IsCanceled)
                {
                    var result = _initializationTask.Result;
                    if (result.Success && systemManager == null)
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

                        // Destroy loading scene (now managed by SystemManager's SceneManagerSystem)
                        // The scene entity exists in the world, SystemManager's SceneManagerSystem will handle it
                        if (_loadingSceneEntity != null && systemManager != null)
                        {
                            try
                            {
                                // Destroy scene from early manager first (if it still exists)
                                if (_earlySceneManager != null)
                                {
                                    _earlySceneManager.DestroyScene(_loadingSceneEntity.Value);
                                }
                                else
                                {
                                    // If early manager was already cleaned up, destroy directly via SystemManager's manager
                                    systemManager.SceneManagerSystem.DestroyScene(
                                        _loadingSceneEntity.Value
                                    );
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.Warning(
                                    ex,
                                    "Error destroying loading scene: {Error}",
                                    ex.Message
                                );
                                // Try to destroy via SystemManager's manager as fallback
                                try
                                {
                                    if (systemManager != null)
                                    {
                                        systemManager.SceneManagerSystem.DestroyScene(
                                            _loadingSceneEntity.Value
                                        );
                                    }
                                }
                                catch
                                {
                                    // If both fail, log and continue - scene will be cleaned up when world is destroyed
                                    _logger.Warning(
                                        "Failed to destroy loading scene via both managers"
                                    );
                                }
                            }
                            _loadingSceneEntity = null;
                        }

                        // Cleanup early scene systems (SystemManager now owns scene management)
                        if (_earlySceneManager != null)
                        {
                            try
                            {
                                _earlySceneManager.Cleanup(); // Unsubscribe from EventBus
                            }
                            catch (Exception ex)
                            {
                                _logger.Warning(ex, "Error cleaning up early scene manager");
                            }
                            _earlySceneManager = null;
                        }

                        _earlyLoadingRenderer?.Dispose();
                        _earlyLoadingRenderer = null;
                        _earlySceneRenderer = null; // SceneRendererSystem doesn't need disposal (no resources)

                        // Clean up loading resources
                        _loadingSpriteBatch?.Dispose();
                        _loadingSpriteBatch = null;
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
            Color backgroundColor;
            if (systemManager != null)
            {
                // Use SceneRendererSystem to determine background color based on active scenes
                backgroundColor = systemManager.SceneRendererSystem.GetBackgroundColor();
            }
            else if (_earlySceneRenderer != null)
            {
                // Early loading screen - use loading screen background color
                backgroundColor = Scenes.Systems.LoadingSceneRendererSystem.GetBackgroundColor();
            }
            else
            {
                // Fallback to black if no renderer available
                backgroundColor = Color.Black;
            }

            GraphicsDevice.Clear(backgroundColor);

            // Render ECS systems (includes loading scene if still loading, or game scene if loaded)
            // Loading scene blocks draw, so game scene won't render until loading completes
            if (systemManager != null)
            {
                // Use SystemManager's rendering (includes SceneRendererSystem)
                systemManager.Render(gameTime);
            }
            else if (_earlySceneRenderer != null)
            {
                // Use early scene renderer (before SystemManager is ready)
                _earlySceneRenderer.Render(gameTime);
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
                _loadingSpriteBatch?.Dispose();
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
