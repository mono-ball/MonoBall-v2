using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using MonoBall.Core.Constants;
using MonoBall.Core.ECS;
using MonoBall.Core.Logging;
using MonoBall.Core.Mods;
using MonoBall.Core.Mods.Utilities;
using MonoBall.Core.Rendering;
using MonoBall.Core.Resources;
using MonoBall.Core.Scripting.Services;
using Serilog;

namespace MonoBall.Core;

/// <summary>
///     The main class for the game, responsible for managing game components, settings,
///     and platform-specific configurations.
/// </summary>
public class MonoBallGame : Game
{
    /// <summary>
    ///     Indicates if the game is running on a desktop platform.
    /// </summary>
    public static readonly bool IsDesktop =
        OperatingSystem.IsMacOS() || OperatingSystem.IsLinux() || OperatingSystem.IsWindows();

    private readonly ILogger _logger;

    // Resources for drawing.
    private readonly GraphicsDeviceManager graphicsDeviceManager;

    // Track if initialization is complete
    private bool _initializationComplete;

    // Async initialization
    private GameInitializationService? _initializationService;
    private Task<GameInitializationService.InitializationResult>? _initializationTask;

    // Loading renderer (used during initialization, before SystemManager exists)
    private ILoadingRenderer? _loadingRenderer;

    // Service and system management
    private GameServices? gameServices;
    private SpriteBatch? spriteBatch;
    private SystemManager? systemManager;

    /// <summary>
    ///     Initializes a new instance of the game. Configures platform-specific settings,
    ///     initializes services like settings and leaderboard managers, and sets up the
    ///     screen manager for screen transitions.
    /// </summary>
    public MonoBallGame()
    {
        // Initialize logging first
        LoggerFactory.ConfigureLogger();
        _logger = LoggerFactory.CreateLogger<MonoBallGame>();
        _logger.Information("Initializing MonoBall game");

        // Initialize EventBus with main thread reference (must be done early on main thread)
        EventBus.Initialize();
        EventBus.SetErrorHandler(
            (eventType, ex) =>
                _logger.Error(ex, "EventBus handler error for {EventType}", eventType)
        );

        graphicsDeviceManager = new GraphicsDeviceManager(this);

        // Set desktop graphics settings (required for DesktopVK)
        if (!IsDesktop)
        {
            throw new PlatformNotSupportedException("Only desktop platforms are supported.");
        }

        graphicsDeviceManager.IsFullScreen = false;
        IsMouseVisible = true;

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
    }

    /// <summary>
    ///     Initializes the game. Sets up minimal initialization for loading screen.
    ///     Actual game loading happens asynchronously in LoadContent().
    /// </summary>
    protected override void Initialize()
    {
        base.Initialize();

        _logger.Information("Game window initialized, preparing for async loading");

        // Note: We don't load mods or services here - that happens asynchronously
        // This allows the window to appear immediately while loading happens in the background
    }

    /// <summary>
    ///     Loads game content. Creates LoadingRenderer for immediate visual feedback,
    ///     and starts async initialization.
    /// </summary>
    protected override void LoadContent()
    {
        base.LoadContent();

        _logger.Information("Starting async content loading");

        // Load all mods synchronously first for system-critical resources (fonts, etc.)
        // Core mod (slot 0 in mod.manifest) loads first, then other mods
        // This ensures fonts are available when the loading screen renders
        LoadModsSynchronously();

        // Get ResourceManager from Game.Services (should already exist from LoadModsSynchronously)
        var resourceManager = Services.GetService<IResourceManager>();
        if (resourceManager == null)
            throw new InvalidOperationException(
                "ResourceManager not found in Game.Services. Ensure LoadModsSynchronously() created it."
            );

        // CREATE AND REGISTER COMPILATION CACHE BEFORE SYSTEMMANAGER
        GameInitializationHelper.CreateAndRegisterCompilationCache(this, _logger);

        // Create LoadingRenderer for immediate visual feedback (no ECS, no events, no SystemManager)
        _loadingRenderer = new LoadingRenderer(
            GraphicsDevice,
            resourceManager,
            LoggerFactory.CreateLogger<LoadingRenderer>()
        );

        // Create initialization service
        _initializationService = new GameInitializationService(
            this,
            GraphicsDevice,
            LoggerFactory.CreateLogger<GameInitializationService>()
        );

        // Set progress callback to update LoadingRenderer
        _initializationService.SetProgressCallback(
            (progress, message) =>
            {
                // Thread-safe: LoadingRenderer.SetProgress can be called from any thread
                _loadingRenderer?.SetProgress(progress, message);
            },
            errorMessage =>
            {
                // Completion callback
                if (errorMessage != null)
                {
                    // Set error on loading renderer
                    if (_loadingRenderer is LoadingRenderer renderer)
                    {
                        renderer.SetError(errorMessage);
                    }
                }
                _initializationComplete = true;
            }
        );

        // Start async initialization (creates the ONLY SystemManager)
        _initializationTask = _initializationService.StartInitialization();

        _logger.Information("LoadingRenderer created, async initialization started");
    }

    /// <summary>
    ///     Updates the game's logic, called once per frame.
    /// </summary>
    /// <param name="gameTime">
    ///     Provides a snapshot of timing values used for game updates.
    /// </param>
    protected override void Update(GameTime gameTime)
    {
        // Process any cross-thread events queued from background tasks
        EventBus.ProcessMainThreadQueue();

        // Exit the game if the Back button (GamePad) or Escape key (Keyboard) is pressed.
        if (
            GamePad.GetState(PlayerIndex.One).Buttons.Back == ButtonState.Pressed
            || Keyboard.GetState().IsKeyDown(Keys.Escape)
        )
            Exit();

        // During loading, update the loading renderer
        if (_loadingRenderer != null)
        {
            _loadingRenderer.Update(gameTime);

            // Check if initialization is complete and successful
            if (
                _initializationComplete
                && _initializationTask != null
                && _initializationTask.IsCompleted
            )
            {
                if (!_initializationTask.IsFaulted && !_initializationTask.IsCanceled)
                {
                    var result = _initializationTask.Result;
                    if (result.Success)
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
                            // Keep loading renderer visible to show error
                            return;
                        }

                        // Transition from loading to game
                        _logger.Information("Game initialization complete, transitioning to game");

                        // Assign the SystemManager, GameServices, and SpriteBatch from async init
                        gameServices = result.GameServices;
                        systemManager = result.SystemManager;
                        spriteBatch = result.SpriteBatch;

                        // Dispose loading renderer (we no longer need it)
                        _loadingRenderer.Dispose();
                        _loadingRenderer = null;

                        _initializationService = null;
                        _initializationTask = null;

                        _logger.Information("Transitioned to game systems");
                    }
                    else
                    {
                        _logger.Error(
                            "Game initialization failed: {ErrorMessage}",
                            result.ErrorMessage
                        );
                        // Keep loading renderer visible to show error
                    }
                }
            }

            // Don't update game systems while loading
            base.Update(gameTime);
            return;
        }

        // Update ECS systems (CameraViewportSystem handles window resize)
        systemManager?.Update(gameTime);

        // Process input for scenes
        if (systemManager != null)
        {
            var keyboardState = Keyboard.GetState();
            var mouseState = Mouse.GetState();
            var gamePadState = GamePad.GetState(PlayerIndex.One);
            systemManager.SceneInputSystem.ProcessInput(keyboardState, mouseState, gamePadState);
        }

        base.Update(gameTime);
    }

    /// <summary>
    ///     Draws the game's graphics, called once per frame.
    /// </summary>
    /// <param name="gameTime">
    ///     Provides a snapshot of timing values used for rendering.
    /// </param>
    protected override void Draw(GameTime gameTime)
    {
        // During loading, render the loading screen
        if (_loadingRenderer != null)
        {
            // LoadingRenderer handles its own Clear and SpriteBatch
            _loadingRenderer.Render(gameTime);
            base.Draw(gameTime);
            return;
        }

        // After loading, use SystemManager for rendering
        if (systemManager == null)
            throw new InvalidOperationException(
                "Cannot render: SystemManager is null. "
                    + "Ensure game initialization completed successfully before calling Draw()."
            );

        // Use SceneSystems to determine background color based on active scenes
        var backgroundColor = systemManager.SceneSystems.GetBackgroundColor();
        GraphicsDevice.Clear(backgroundColor);

        // Use SystemManager's rendering (includes SceneRendererSystem)
        systemManager.Render(gameTime);

        base.Draw(gameTime);
    }

    /// <summary>
    ///     Loads all mods synchronously before async initialization, ensuring core mod (slot 0 in mod.manifest) loads first.
    ///     This ensures system-critical resources like fonts are available for the loading screen.
    /// </summary>
    private void LoadModsSynchronously()
    {
        _logger.Information(
            "Loading all mods synchronously for system-critical resources (core mod loads first)"
        );

        var modsDirectory = ModsPathResolver.FindModsDirectory();
        if (string.IsNullOrEmpty(modsDirectory) || !Directory.Exists(modsDirectory))
            throw new InvalidOperationException(
                $"Mods directory not found: {modsDirectory}. "
                    + "Cannot load mods. Ensure Mods directory exists."
            );

        // Create ModManager and load all mods (core mod loads first)
        var modManager = new ModManager(LoggerFactory.CreateLogger<ModManager>(), modsDirectory);

        // Load mods (core mod loads first, then others)
        var errors = new List<string>();
        var success = modManager.Load(errors);

        if (!success)
            throw new InvalidOperationException(
                $"Failed to load mods. Errors: {string.Join("; ", errors)}"
            );

        // Register ModManager in Game.Services
        Services.AddService(typeof(ModManager), modManager);
        _logger.Debug(
            "ModManager loaded and registered with {ModCount} mod(s)",
            modManager.LoadedMods.Count
        );

        // Create and register ResourceManager immediately after mods load
        // This ensures resources (fonts, etc.) are available for the loading screen
        var pathResolver = new ResourcePathResolver(
            modManager,
            LoggerFactory.CreateLogger<ResourcePathResolver>()
        );
        Services.AddService(typeof(IResourcePathResolver), pathResolver);

        var resourceManager = new ResourceManager(
            GraphicsDevice,
            modManager,
            pathResolver,
            LoggerFactory.CreateLogger<ResourceManager>()
        );
        Services.AddService(typeof(IResourceManager), resourceManager);
        _logger.Debug("ResourceManager created and registered");

        // Create shader service (depends on ResourceManager)
        var shaderService = new ShaderService(
            GraphicsDevice,
            modManager,
            resourceManager,
            LoggerFactory.CreateLogger<ShaderService>()
        );
        Services.AddService(typeof(IShaderService), shaderService);
        _logger.Debug("ShaderService created and registered");

        // Create shader parameter validator
        var shaderParameterValidator = new ShaderParameterValidator(
            shaderService,
            LoggerFactory.CreateLogger<ShaderParameterValidator>(),
            modManager
        );
        Services.AddService(typeof(IShaderParameterValidator), shaderParameterValidator);
        _logger.Debug("ShaderParameterValidator created and registered");

        // Create and register ConstantsService immediately after mods load
        // Use factory pattern for consistency with FontService
        var constantsService = ConstantsServiceFactory.GetOrCreateConstantsService(
            this,
            modManager,
            LoggerFactory.CreateLogger<ConstantsService>()
        );

        // Validate required constants exist (fail-fast)
        constantsService.ValidateRequiredConstants(
            new[]
            {
                "TileChunkSize",
                "TileWidth",
                "TileHeight",
                "PlayerSpriteSheetId",
                "PlayerInitialAnimation",
                "PlayerInitialMapId",
                "PlayerSpawnX",
                "PlayerSpawnY",
                "PlayerMovementSpeed",
                "ReferenceWidth",
                "ReferenceHeight",
                "CameraZoom",
                "CameraRotation",
                "CameraSmoothingSpeed",
                "ScenePriorityOffset",
                "DefaultFontId",
            }
        );

        // Validate constants against validation rules (if defined)
        constantsService.ValidateConstants();
        _logger.Debug("ConstantsService validated");

        _logger.Information(
            "All mods loaded successfully ({ModCount} mods, core mod: {CoreModId}), FontService and ConstantsService available",
            modManager.LoadedMods.Count,
            modManager.CoreMod?.Id ?? "unknown"
        );
    }

    /// <summary>
    ///     Performs cleanup when the game is disposed.
    /// </summary>
    /// <param name="disposing">True if managed resources should be disposed.</param>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            // Dispose loading renderer if still active
            _loadingRenderer?.Dispose();
            _loadingRenderer = null;

            systemManager?.Dispose();
            spriteBatch?.Dispose();

            // Dispose ResourceManager to clean up cached resources
            var resourceManager = Services.GetService<IResourceManager>();
            if (resourceManager is IDisposable resourceManagerDisposable)
                resourceManagerDisposable.Dispose();

            // Cleanup temp files from script compilation
            var compilationCache = Services.GetService<IScriptCompilationCache>();
            if (compilationCache?.TempFileManager is IDisposable tempFileManagerDisposable)
            {
                tempFileManagerDisposable.Dispose();
                _logger.Debug("Cleaned up script compilation temp files");
            }

            EcsWorld.Reset();

            _logger.Information("Shutting down MonoBall game");
            LoggerFactory.CloseAndFlush();
        }

        base.Dispose(disposing);
    }
}
