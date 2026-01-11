using System;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoBall.Core.Constants;
using MonoBall.Core.ECS;
using MonoBall.Core.Scenes.Events;
using Serilog;

namespace MonoBall.Core;

/// <summary>
///     Constants for initialization progress percentages.
///     Weighted to reflect actual time spent in each phase.
/// </summary>
public static class InitializationProgress
{
    // Phase 1: Game services setup (fast - ECS world creation)
    public const float GameServices = 0.05f;

    // Phase 2: Rendering setup (fast - SpriteBatch creation)
    public const float Rendering = 0.08f;

    // Phase 3: ECS systems (slow - includes script compilation)
    public const float SystemsStart = 0.10f;
    public const float ScriptCompilation = 0.35f; // Script compilation is ~25% of total time
    public const float SystemsComplete = 0.45f;

    // Phase 4: Initial map loading (medium - texture loading, chunk creation)
    public const float MapLoading = 0.50f;
    public const float MapComplete = 0.70f;

    // Phase 5: Camera setup (fast)
    public const float Camera = 0.75f;

    // Phase 6: Player setup (fast)
    public const float Player = 0.80f;

    // Phase 7: Game state setup (fast)
    public const float GameState = 0.85f;

    // Phase 8: Game scene creation (fast)
    public const float GameScene = 0.95f;

    // Complete
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
    private Action<float, string>? _progressCallback; // Callback for progress updates (thread-safe enqueue)
    private Action<string?>? _completeCallback; // Callback when initialization completes

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
    ///     Sets the progress callback for reporting initialization progress.
    ///     This callback is invoked from background threads, so it should be thread-safe.
    /// </summary>
    /// <param name="progressCallback">Callback that receives progress (0.0-1.0) and message.</param>
    /// <param name="completeCallback">Callback invoked when initialization completes (with error message or null for success).</param>
    public void SetProgressCallback(
        Action<float, string> progressCallback,
        Action<string?>? completeCallback = null
    )
    {
        _progressCallback =
            progressCallback ?? throw new ArgumentNullException(nameof(progressCallback));
        _completeCallback = completeCallback;
    }

    /// <summary>
    ///     Starts asynchronous game initialization.
    ///     Progress is reported via the callback set by SetProgressCallback().
    /// </summary>
    /// <returns>The initialization task.</returns>
    public Task<InitializationResult> StartInitialization()
    {
        _logger.Information("Starting async game initialization");

        if (_progressCallback == null)
        {
            _logger.Warning(
                "StartInitialization called without progress callback set. Progress will not be reported."
            );
        }

        _initializationTask = InitializeGameAsync();
        return _initializationTask;
    }

    /// <summary>
    ///     Updates the loading progress via callback.
    /// </summary>
    /// <param name="progress">Progress value between 0.0 and 1.0.</param>
    /// <param name="step">Current step description.</param>
    public void UpdateProgress(float progress, string step)
    {
        _progressCallback?.Invoke(Math.Clamp(progress, 0.0f, 1.0f), step ?? "Loading...");
    }

    /// <summary>
    ///     Marks loading as complete with optional error message.
    /// </summary>
    /// <param name="errorMessage">Error message if loading failed, or null if successful.</param>
    public void MarkComplete(string? errorMessage = null)
    {
        // Report final progress
        if (errorMessage == null)
        {
            _progressCallback?.Invoke(1.0f, "Complete!");
        }
        else
        {
            _progressCallback?.Invoke(1.0f, $"Error: {errorMessage}");
        }

        // Invoke completion callback
        _completeCallback?.Invoke(errorMessage);

        // Fire loading complete event (for any listeners)
        var completeEvent = new LoadingCompleteEvent
        {
            Success = errorMessage == null,
            ErrorMessage = errorMessage,
        };
        EventBus.Send(ref completeEvent);
    }

    /// <summary>
    ///     Asynchronously initializes the game.
    /// </summary>
    /// <returns>The initialization result.</returns>
    private async Task<InitializationResult> InitializeGameAsync()
    {
        try
        {
            // Phase 1: Initialize game services (ECS world)
            // Note: Mods and content services (ResourceManager, ShaderService, etc.) are already
            // loaded synchronously by LoadModsSynchronously() before async init starts
            UpdateProgress(InitializationProgress.GameServices, "Creating ECS world...");
            await Task.Yield();
            var gameServices = GameInitializationHelper.InitializeGameServices(
                _game,
                _graphicsDevice,
                _logger
            );

            // Phase 2: Create sprite batch for main game rendering
            UpdateProgress(InitializationProgress.Rendering, "Initializing rendering...");
            await Task.Yield();
            var spriteBatch = GameInitializationHelper.CreateSpriteBatch(_graphicsDevice, _logger);

            // Phase 3: Initialize ECS systems (includes script compilation - slowest phase)
            UpdateProgress(InitializationProgress.SystemsStart, "Creating game systems...");
            await Task.Yield();

            // Sub-progress: Script compilation is the heaviest part
            UpdateProgress(InitializationProgress.ScriptCompilation, "Compiling scripts...");
            await Task.Yield();

            var systemManager = GameInitializationHelper.InitializeEcsSystems(
                gameServices,
                _graphicsDevice,
                spriteBatch,
                _game,
                _logger
            );

            UpdateProgress(InitializationProgress.SystemsComplete, "Game systems ready...");
            await Task.Yield();

            // Get ConstantsService for initial map and camera creation
            var constantsService = _game.Services.GetService<IConstantsService>();
            if (constantsService == null)
                throw new InvalidOperationException(
                    "IConstantsService is not available in Game.Services. "
                        + "Ensure ConstantsService was registered after mods were loaded."
                );

            // Phase 4: Load initial map (medium - texture loading, chunk creation)
            UpdateProgress(InitializationProgress.MapLoading, "Loading initial map...");
            await Task.Yield();
            var initialMapId = constantsService.GetString("PlayerInitialMapId");
            systemManager.LoadMap(initialMapId);
            _logger.Information("Initial map loaded: {MapId}", initialMapId);

            UpdateProgress(InitializationProgress.MapComplete, "Map loaded...");
            await Task.Yield();

            // Phase 5: Create default camera (after map is loaded so we have tile dimensions)
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

            // Phase 6: Initialize player
            UpdateProgress(InitializationProgress.Player, "Initializing player...");
            await Task.Yield();
            var playerEntity = GameInitializationHelper.InitializePlayer(
                systemManager,
                cameraEntity,
                _logger
            );

            // Phase 7: Setup initial game state (variables and flags)
            UpdateProgress(InitializationProgress.GameState, "Setting up game state...");
            await Task.Yield();
            GameInitializationHelper.SetupInitialGameState(_game, _logger);

            // Set camera to follow player
            GameInitializationHelper.SetupCameraFollow(
                systemManager,
                cameraEntity,
                playerEntity,
                _logger
            );

            // Phase 8: Create game scene
            UpdateProgress(InitializationProgress.GameScene, "Creating game scene...");
            await Task.Yield();
            GameInitializationHelper.CreateGameScene(systemManager, _logger);

            // Mark loading as complete - invokes the completion callback
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
