using System;
using Arch.Core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoBall.Core.Constants;
using MonoBall.Core.ECS;
using MonoBall.Core.ECS.Components;
using MonoBall.Core.ECS.Services;
using MonoBall.Core.Logging;
using MonoBall.Core.Mods;
using MonoBall.Core.Rendering;
using MonoBall.Core.Scripting.Services;
using Serilog;

namespace MonoBall.Core;

/// <summary>
///     Helper methods for game initialization.
/// </summary>
public static class GameInitializationHelper
{
    /// <summary>
    ///     Ensures EcsService exists in Game.Services, creating it if necessary.
    /// </summary>
    /// <param name="game">The game instance.</param>
    /// <param name="logger">The logger for logging operations.</param>
    /// <returns>The EcsService instance.</returns>
    public static EcsService EnsureEcsService(Game game, ILogger logger)
    {
        if (game == null)
            throw new ArgumentNullException(nameof(game));
        if (logger == null)
            throw new ArgumentNullException(nameof(logger));

        var existingEcsService = game.Services.GetService<EcsService>();
        if (existingEcsService == null)
        {
            var ecsService = new EcsService();
            game.Services.AddService(typeof(EcsService), ecsService);
            logger.Debug("EcsService created and registered");
            return ecsService;
        }

        logger.Debug("Reusing existing EcsService from Game.Services");
        return existingEcsService;
    }

    /// <summary>
    ///     Ensures FlagVariableService exists in Game.Services, creating it if necessary.
    /// </summary>
    /// <param name="game">The game instance.</param>
    /// <param name="ecsService">The ECS service (must not be null).</param>
    /// <param name="logger">The logger for logging operations.</param>
    /// <returns>The FlagVariableService instance.</returns>
    public static IFlagVariableService EnsureFlagVariableService(
        Game game,
        EcsService ecsService,
        ILogger logger
    )
    {
        if (game == null)
            throw new ArgumentNullException(nameof(game));
        if (ecsService == null)
            throw new ArgumentNullException(nameof(ecsService));
        if (logger == null)
            throw new ArgumentNullException(nameof(logger));

        var existingFlagService = game.Services.GetService<IFlagVariableService>();
        if (existingFlagService == null)
        {
            var flagVariableService = new FlagVariableService(
                ecsService.World,
                LoggerFactory.CreateLogger<FlagVariableService>()
            );
            game.Services.AddService(typeof(IFlagVariableService), flagVariableService);
            logger.Debug("FlagVariableService created and registered");
            return flagVariableService;
        }

        logger.Debug("Reusing existing FlagVariableService from Game.Services");
        return existingFlagService;
    }

    /// <summary>
    ///     Initializes core services (mods, ECS world). Should be called from Game.Initialize().
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
    ///     Creates a sprite batch for game rendering.
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
    ///     Initializes ECS systems.
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
        if (gameServices?.ModManager == null)
            throw new InvalidOperationException(
                "GameServices.ModManager is null. Ensure GameServices.Initialize() was called successfully."
            );

        var resourceManager = gameServices.ResourceManager;
        if (resourceManager == null)
            throw new InvalidOperationException(
                "GameServices.ResourceManager is null. Ensure LoadModsSynchronously() was called before async initialization."
            );

        // Resolve ALL dependencies explicitly (no Service Locator in SystemManager)
        var shaderService = game.Services.GetService<IShaderService>();
        if (shaderService == null)
            throw new InvalidOperationException(
                "IShaderService is not registered. Ensure LoadModsSynchronously() was called."
            );

        var shaderParameterValidator = game.Services.GetService<IShaderParameterValidator>();
        if (shaderParameterValidator == null)
            throw new InvalidOperationException(
                "IShaderParameterValidator is not registered. Ensure LoadModsSynchronously() was called."
            );

        var flagVariableService = game.Services.GetService<IFlagVariableService>();
        if (flagVariableService == null)
            throw new InvalidOperationException(
                "IFlagVariableService is not registered. Ensure GameServices.Initialize() was called."
            );

        var scriptCompilationCache = game.Services.GetService<IScriptCompilationCache>();
        if (scriptCompilationCache == null)
            throw new InvalidOperationException(
                "IScriptCompilationCache is not registered. Ensure CreateAndRegisterCompilationCache() was called."
            );

        var constantsService = game.Services.GetService<ConstantsService>();
        if (constantsService == null)
            throw new InvalidOperationException(
                "ConstantsService is not registered. Ensure LoadModsSynchronously() was called."
            );

        var systemManager = new SystemManager(
            gameServices.EcsService!.World,
            graphicsDevice,
            gameServices.ModManager,
            resourceManager,
            shaderService,
            shaderParameterValidator,
            flagVariableService,
            scriptCompilationCache,
            constantsService,
            game,
            LoggerFactory.CreateLogger<SystemManager>()
        );
        systemManager.Initialize(spriteBatch);
        logger.Debug("ECS systems initialized");
        return systemManager;
    }

    /// <summary>
    ///     Creates a default camera entity in the world.
    /// </summary>
    /// <param name="world">The ECS world.</param>
    /// <param name="modManager">The mod manager for getting default tile sizes.</param>
    /// <param name="graphicsDevice">The graphics device for viewport setup.</param>
    /// <param name="logger">The logger for logging operations.</param>
    /// <param name="constants">The constants service for accessing game constants. Required.</param>
    /// <returns>The created camera entity.</returns>
    public static Entity CreateDefaultCamera(
        World world,
        IModManager modManager,
        GraphicsDevice graphicsDevice,
        ILogger logger,
        IConstantsService constants
    )
    {
        if (world == null)
            throw new ArgumentNullException(nameof(world));
        if (modManager == null)
            throw new ArgumentNullException(nameof(modManager));
        if (graphicsDevice == null)
            throw new ArgumentNullException(nameof(graphicsDevice));
        if (logger == null)
            throw new ArgumentNullException(nameof(logger));
        if (constants == null)
            throw new ArgumentNullException(nameof(constants));

        // Get default tile dimensions from constants service
        var tileWidth = constants.Get<int>("TileWidth");
        var tileHeight = constants.Get<int>("TileHeight");

        // Get reference resolution (GBA: 240x160)
        var referenceWidth = constants.Get<int>("ReferenceWidth");
        var referenceHeight = constants.Get<int>("ReferenceHeight");
        var baseZoom = constants.Get<float>("CameraZoom");
        var rotation = constants.Get<float>("CameraRotation");
        var smoothingSpeed = constants.Get<float>("CameraSmoothingSpeed");

        // Calculate integer scale that fits in window (same logic as CameraViewportSystem)
        var windowWidth = graphicsDevice.Viewport.Width;
        var windowHeight = graphicsDevice.Viewport.Height;
        var scaleX = Math.Max(1, windowWidth / referenceWidth);
        var scaleY = Math.Max(1, windowHeight / referenceHeight);
        var scale = Math.Min(scaleX, scaleY);

        // Calculate viewport dimensions using integer scale
        var viewportWidth = referenceWidth * scale;
        var viewportHeight = referenceHeight * scale;

        // Calculate letterbox/pillarbox offset for centering
        var virtualX = (windowWidth - viewportWidth) / 2;
        var virtualY = (windowHeight - viewportHeight) / 2;

        // Calculate initial zoom: scale factor * base zoom from constants
        // Base zoom of 1.0 means "show reference resolution", >1 zooms in, <1 zooms out
        var initialZoom = scale * baseZoom;

        // Create camera component with properly initialized viewport and zoom
        // This ensures the camera shows the correct world area even before CameraViewportSystem runs
        var cameraComponent = new CameraComponent
        {
            Position = Vector2.Zero,
            Zoom = initialZoom,
            Rotation = rotation,
            Viewport = new Rectangle(0, 0, viewportWidth, viewportHeight),
            VirtualViewport = new Rectangle(virtualX, virtualY, viewportWidth, viewportHeight),
            ReferenceWidth = windowWidth, // Track window dimensions for resize detection
            ReferenceHeight = windowHeight,
            TileWidth = tileWidth,
            TileHeight = tileHeight,
            MapBounds = Rectangle.Empty, // No bounds initially
            FollowTarget = null,
            FollowEntity = null,
            IsFollowingLocked = false,
            SmoothingSpeed = smoothingSpeed,
            IsActive = true,
            IsDirty = true,
        };

        var cameraEntity = world.Create(cameraComponent);
        logger.Information(
            "Created default camera entity {EntityId} (viewport: {ViewportWidth}x{ViewportHeight}, zoom: {Zoom}, baseZoom: {BaseZoom})",
            cameraEntity.Id,
            viewportWidth,
            viewportHeight,
            initialZoom,
            baseZoom
        );
        return cameraEntity;
    }

    /// <summary>
    ///     Initializes the player entity using the PlayerSystem.
    /// </summary>
    /// <param name="systemManager">The system manager containing the PlayerSystem.</param>
    /// <param name="cameraEntity">The camera entity (used for spawn position).</param>
    /// <param name="logger">The logger for logging operations.</param>
    /// <returns>The created player entity.</returns>
    public static Entity InitializePlayer(
        SystemManager systemManager,
        Entity cameraEntity,
        ILogger logger
    )
    {
        if (systemManager == null)
            throw new ArgumentNullException(nameof(systemManager));
        if (logger == null)
            throw new ArgumentNullException(nameof(logger));

        // Pass camera entity directly to ensure it's used (avoids query timing issues)
        systemManager.InitializePlayer(cameraEntity);
        var playerEntity = systemManager.GetPlayerEntity();
        if (!playerEntity.HasValue)
            throw new InvalidOperationException(
                "Player entity was not created after InitializePlayer() call."
            );
        logger.Information("Player entity initialized: {EntityId}", playerEntity.Value.Id);
        return playerEntity.Value;
    }

    /// <summary>
    ///     Sets up initial game state (flags and variables).
    /// </summary>
    /// <param name="game">The game instance.</param>
    /// <param name="logger">The logger for logging operations.</param>
    public static void SetupInitialGameState(Game game, ILogger logger)
    {
        if (game == null)
            throw new ArgumentNullException(nameof(game));
        if (logger == null)
            throw new ArgumentNullException(nameof(logger));

        // Get FlagVariableService from Game.Services
        var flagVariableService = game.Services.GetService<IFlagVariableService>();
        if (flagVariableService == null)
            throw new InvalidOperationException(
                "IFlagVariableService is not available in Game.Services. "
                    + "Ensure GameServices.Initialize() was called."
            );

        // FlagVariableService automatically creates the game state entity on first access
        // We just need to ensure it's initialized by accessing it
        // The service will create the singleton entity with FlagsComponent, VariablesComponent, and FlagVariableMetadataComponent
        // No explicit initialization needed - the service handles it internally
        logger.Debug("Game state initialized (flags and variables singleton entity created)");
    }

    /// <summary>
    ///     Sets up camera to follow player. Should be called after both camera and player are created.
    /// </summary>
    /// <param name="systemManager">The system manager containing the CameraSystem.</param>
    /// <param name="cameraEntity">The camera entity.</param>
    /// <param name="playerEntity">The player entity.</param>
    /// <param name="logger">The logger for logging operations.</param>
    public static void SetupCameraFollow(
        SystemManager systemManager,
        Entity cameraEntity,
        Entity playerEntity,
        ILogger logger
    )
    {
        if (systemManager == null)
            throw new ArgumentNullException(nameof(systemManager));
        if (logger == null)
            throw new ArgumentNullException(nameof(logger));

        // Set camera to follow player
        systemManager.CameraSystem.SetCameraFollowEntity(cameraEntity, playerEntity);
        logger.Information(
            "Camera {CameraEntityId} set to follow player {PlayerEntityId}",
            cameraEntity.Id,
            playerEntity.Id
        );
    }

    /// <summary>
    ///     Creates the game scene using GameSceneHelper.
    /// </summary>
    /// <param name="systemManager">The system manager containing the SceneSystem.</param>
    /// <param name="logger">The logger for logging operations.</param>
    public static void CreateGameScene(SystemManager systemManager, ILogger logger)
    {
        if (systemManager == null)
            throw new ArgumentNullException(nameof(systemManager));
        if (logger == null)
            throw new ArgumentNullException(nameof(logger));

        // Create game scene using SceneSystem
        systemManager.CreateGameScene();
        logger.Information("Game scene created");
    }

    /// <summary>
    ///     Creates and registers the script compilation cache as a singleton in Game.Services.
    ///     Must be called before creating any SystemManager.
    /// </summary>
    /// <param name="game">The game instance to register the cache with.</param>
    /// <param name="logger">The logger for logging operations.</param>
    /// <returns>The created compilation cache instance.</returns>
    /// <exception cref="ArgumentNullException">Thrown when game is null.</exception>
    public static IScriptCompilationCache CreateAndRegisterCompilationCache(
        Game game,
        ILogger logger
    )
    {
        if (game == null)
            throw new ArgumentNullException(nameof(game));
        if (logger == null)
            throw new ArgumentNullException(nameof(logger));

        var compilationCacheLogger = Log.ForContext("SourceContext", "ScriptCompilationCache");
        var compilationCache = new ScriptCompilationCache(
            new ScriptTypeCache(compilationCacheLogger),
            new DependencyReferenceCache(compilationCacheLogger),
            new ScriptFactoryCache(compilationCacheLogger),
            new TempFileManager(compilationCacheLogger)
        );

        game.Services.AddService(typeof(IScriptCompilationCache), compilationCache);
        logger.Debug("Registered IScriptCompilationCache singleton");

        return compilationCache;
    }
}
