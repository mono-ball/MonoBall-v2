using System;
using System.Threading.Tasks;
using Arch.Core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoBall.Core.Constants;
using MonoBall.Core.ECS;
using MonoBall.Core.Scenes;
using MonoBall.Core.Scenes.Components;
using MonoBall.Core.Scenes.Events;
using MonoBall.Core.Scenes.Systems;
using Serilog;

namespace MonoBall.Core;

/// <summary>
///     Constants for initialization progress percentages.
/// </summary>
public static class InitializationProgress
{
    public const float Mods = 0.1f;
    public const float ContentServices = 0.3f;
    public const float Rendering = 0.4f;
    public const float GameSystems = 0.5f;
    public const float Camera = 0.6f;
    public const float Player = 0.7f;
    public const float InitialMap = 0.85f;
    public const float GameScene = 0.95f;
    public const float Complete = 1.0f;
}

/// <summary>
///     Service that handles asynchronous game initialization with progress tracking.
/// </summary>
public class GameInitializationService
{
    private readonly Game _game;
    private readonly GraphicsDevice _graphicsDevice;
    private readonly ILogger _logger;
    private Task<InitializationResult>? _initializationTask;
    private Entity? _loadingSceneEntity;
    private LoadingSceneSystem? _loadingSceneSystem; // Reference to loading scene system for progress updates
    private World? _mainWorld; // Reference to main world (not owned by this service)

    /// <summary>
    ///     Initializes a new instance of the GameInitializationService.
    /// </summary>
    /// <param name="game">The game instance.</param>
    /// <param name="graphicsDevice">The graphics device.</param>
    /// <param name="logger">The logger for logging operations.</param>
    public GameInitializationService(Game game, GraphicsDevice graphicsDevice, ILogger logger)
    {
        _game = game ?? throw new ArgumentNullException(nameof(game));
        _graphicsDevice = graphicsDevice ?? throw new ArgumentNullException(nameof(graphicsDevice));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    ///     Gets the loading scene entity.
    /// </summary>
    public Entity? LoadingSceneEntity => _loadingSceneEntity;

    /// <summary>
    ///     Sets the loading scene system for progress updates.
    ///     Must be called before starting initialization.
    /// </summary>
    /// <param name="loadingSceneSystem">The loading scene system.</param>
    public void SetLoadingSceneSystem(LoadingSceneSystem loadingSceneSystem)
    {
        _loadingSceneSystem =
            loadingSceneSystem ?? throw new ArgumentNullException(nameof(loadingSceneSystem));
    }

    /// <summary>
    ///     Creates a loading scene in the main world and starts asynchronous game initialization.
    /// </summary>
    /// <param name="mainWorld">The main ECS world (created early for loading scene).</param>
    /// <param name="sceneSystem">The scene manager system for the main world.</param>
    /// <param name="graphicsDevice">The graphics device.</param>
    /// <param name="spriteBatch">The sprite batch for rendering.</param>
    /// <returns>The loading scene entity and the initialization task.</returns>
    public (
        Entity loadingSceneEntity,
        Task<InitializationResult> initializationTask
    ) CreateLoadingSceneAndStartInitialization(
        World mainWorld,
        SceneSystem sceneSystem,
        GraphicsDevice graphicsDevice,
        SpriteBatch spriteBatch
    )
    {
        _logger.Information(
            "Creating loading scene in main world and starting async initialization"
        );

        if (mainWorld == null)
            throw new ArgumentNullException(nameof(mainWorld));
        if (sceneSystem == null)
            throw new ArgumentNullException(nameof(sceneSystem));

        _mainWorld = mainWorld;

        // Create loading scene entity in main world
        var loadingSceneComponent = new SceneComponent
        {
            SceneId = "loading:main",
            Priority = ScenePriorities.LoadingScreen, // Above game scenes, below debug overlays
            CameraMode = SceneCameraMode.ScreenCamera,
            CameraEntityId = null,
            BlocksUpdate = true, // Block all updates while loading
            BlocksDraw = true, // Block all drawing while loading
            BlocksInput = true, // Block all input while loading
            IsActive = true,
            IsPaused = false,
            BackgroundColor = LoadingSceneSystem.GetBackgroundColor(),
        };

        var loadingProgressComponent = new LoadingProgressComponent
        {
            Progress = 0.0f,
            CurrentStep = "Initializing...",
            IsComplete = false,
            ErrorMessage = null,
        };

        _loadingSceneEntity = sceneSystem.CreateScene(
            loadingSceneComponent,
            new LoadingSceneComponent(),
            loadingProgressComponent
        );

        _logger.Information(
            "Loading scene created in main world (entity {EntityId})",
            _loadingSceneEntity.Value.Id
        );

        // Start async initialization
        _initializationTask = InitializeGameAsync(_mainWorld, _loadingSceneEntity.Value);

        return (_loadingSceneEntity.Value, _initializationTask);
    }

    /// <summary>
    ///     Updates the loading progress. Thread-safe: enqueues update to LoadingSceneSystem.
    /// </summary>
    /// <param name="progress">Progress value between 0.0 and 1.0.</param>
    /// <param name="step">Current step description.</param>
    public void UpdateProgress(float progress, string step)
    {
        if (_loadingSceneSystem == null)
        {
            _logger.Warning(
                "UpdateProgress called but LoadingSceneSystem not set. Call SetLoadingSceneSystem() first."
            );
            return;
        }

        _loadingSceneSystem.EnqueueProgress(progress, step ?? "Loading...");
    }

    /// <summary>
    ///     Marks loading as complete with optional error message.
    /// </summary>
    /// <param name="errorMessage">Error message if loading failed, or null if successful.</param>
    public void MarkComplete(string? errorMessage = null)
    {
        if (_mainWorld == null || _loadingSceneEntity == null)
            return;

        if (_mainWorld.Has<LoadingProgressComponent>(_loadingSceneEntity.Value))
        {
            ref var progressComponent = ref _mainWorld.Get<LoadingProgressComponent>(
                _loadingSceneEntity.Value
            );
            progressComponent.IsComplete = true;
            progressComponent.Progress = errorMessage == null ? 1.0f : progressComponent.Progress;
            progressComponent.ErrorMessage = errorMessage;
            progressComponent.CurrentStep =
                errorMessage == null ? "Complete!" : $"Error: {errorMessage}";

            // Fire loading complete event
            var completeEvent = new LoadingCompleteEvent
            {
                Success = errorMessage == null,
                ErrorMessage = errorMessage,
            };
            EventBus.Send(ref completeEvent);
        }
    }

    /// <summary>
    ///     Asynchronously initializes the game.
    /// </summary>
    /// <param name="mainWorld">The main world (same world where loading scene exists).</param>
    /// <param name="loadingSceneEntity">The loading scene entity.</param>
    /// <returns>The initialization result.</returns>
    private async Task<InitializationResult> InitializeGameAsync(
        World mainWorld,
        Entity loadingSceneEntity
    )
    {
        try
        {
            _logger.Information("Starting async game initialization");

            // Step 1: Initialize game services (mods, ECS world)
            UpdateProgress(InitializationProgress.Mods, "Loading mods...");
            await Task.Yield();
            var gameServices = GameInitializationHelper.InitializeGameServices(
                _game,
                _graphicsDevice,
                _logger
            );

            // Step 3: Load content services
            UpdateProgress(InitializationProgress.ContentServices, "Loading content services...");
            await Task.Yield();
            GameInitializationHelper.LoadContentServices(gameServices, _logger);

            // Step 3: Create sprite batch
            // Note: SpriteBatch is created here, but loading screen uses _loadingSpriteBatch from MonoBallGame
            // This spriteBatch will be used for the main game rendering
            UpdateProgress(InitializationProgress.Rendering, "Initializing rendering...");
            await Task.Yield();
            var spriteBatch = GameInitializationHelper.CreateSpriteBatch(_graphicsDevice, _logger);

            // Step 4: Initialize ECS systems
            UpdateProgress(InitializationProgress.GameSystems, "Initializing game systems...");
            await Task.Yield();
            var systemManager = GameInitializationHelper.InitializeEcsSystems(
                gameServices,
                _graphicsDevice,
                spriteBatch,
                _game,
                _logger
            );

            // Get ConstantsService for initial map and camera creation
            var constantsService = _game.Services.GetService<ConstantsService>();
            if (constantsService == null)
                throw new InvalidOperationException(
                    "ConstantsService is not available in Game.Services. "
                        + "Ensure ConstantsService was registered after mods were loaded."
                );

            // Step 5: Load initial map FIRST (needed for tile dimensions and spawn position)
            UpdateProgress(InitializationProgress.InitialMap, "Loading initial map...");
            await Task.Yield();
            var initialMapId = constantsService.GetString("PlayerInitialMapId");
            systemManager.LoadMap(initialMapId);
            _logger.Information("Initial map loaded: {MapId}", initialMapId);

            // Step 6: Create default camera (after map is loaded so we have tile dimensions)
            UpdateProgress(InitializationProgress.Camera, "Setting up camera...");
            await Task.Yield();
            var world = gameServices.EcsService!.World;

            var cameraEntity = GameInitializationHelper.CreateDefaultCamera(
                world,
                gameServices.ModManager!,
                _graphicsDevice,
                _logger,
                constantsService
            );

            // Step 7: Initialize player at default spawn position (10, 8 tiles - center-ish of map)
            UpdateProgress(InitializationProgress.Player, "Initializing player...");
            await Task.Yield();
            var playerEntity = GameInitializationHelper.InitializePlayer(
                systemManager,
                cameraEntity,
                _logger
            );

            // Step 7.5: Setup initial game state (variables and flags)
            UpdateProgress(InitializationProgress.Player + 0.05f, "Setting up game state...");
            await Task.Yield();
            GameInitializationHelper.SetupInitialGameState(_game, _logger);

            // Step 8: Set camera to follow player (after both are created)
            GameInitializationHelper.SetupCameraFollow(
                systemManager,
                cameraEntity,
                playerEntity,
                _logger
            );

            // Step 9: Create game scene
            UpdateProgress(InitializationProgress.GameScene, "Creating game scene...");
            await Task.Yield();
            GameInitializationHelper.CreateGameScene(systemManager, _logger);

            // Mark loading as complete - this sets Progress = 1.0f and IsComplete = true
            // MarkComplete() directly modifies the world component, so no need to go through the queue
            // The main thread will check IsComplete on the next Update() call
            MarkComplete();

            return new InitializationResult
            {
                Success = true,
                GameServices = gameServices,
                SystemManager = systemManager,
                SpriteBatch = spriteBatch,
            };
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Game initialization failed");
            MarkComplete(ex.Message);
            return new InitializationResult
            {
                Success = false,
                ErrorMessage = ex.Message,
                Exception = ex,
            };
        }
    }

    /// <summary>
    ///     Result of game initialization.
    /// </summary>
    public class InitializationResult
    {
        /// <summary>
        ///     Whether initialization was successful.
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        ///     The game services (only set if Success is true).
        /// </summary>
        public GameServices? GameServices { get; set; }

        /// <summary>
        ///     The system manager (only set if Success is true).
        /// </summary>
        public SystemManager? SystemManager { get; set; }

        /// <summary>
        ///     The sprite batch (only set if Success is true).
        /// </summary>
        public SpriteBatch? SpriteBatch { get; set; }

        /// <summary>
        ///     Error message if initialization failed.
        /// </summary>
        public string? ErrorMessage { get; set; }

        /// <summary>
        ///     Exception if initialization failed.
        /// </summary>
        public Exception? Exception { get; set; }
    }
}
