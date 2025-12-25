using System;
using System.Collections.Generic;
using Arch.Core;
using Arch.System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoBall.Core;
using MonoBall.Core.ECS.Components;
using MonoBall.Core.ECS.Services;
using MonoBall.Core.ECS.Systems;
using MonoBall.Core.Logging;
using MonoBall.Core.Maps;
using MonoBall.Core.Mods;
using MonoBall.Core.Rendering;
using MonoBall.Core.Scenes.Systems;
using Serilog;

namespace MonoBall.Core.ECS
{
    /// <summary>
    /// Manages all ECS systems, their initialization, updates, and rendering.
    /// </summary>
    public class SystemManager : IDisposable
    {
        private readonly World _world;
        private readonly GraphicsDevice _graphicsDevice;
        private readonly IModManager _modManager;
        private readonly ITilesetLoaderService _tilesetLoader;
        private readonly Game _game;
        private readonly ILogger _logger;
        private ISpriteLoaderService _spriteLoader = null!; // Initialized in Initialize()
        private ICameraService _cameraService = null!; // Initialized in Initialize()
        private Services.IVariableSpriteResolver? _variableSpriteResolver; // Initialized in Initialize()
        private SpriteBatch? _spriteBatch;

        private Group<float> _updateSystems = null!; // Initialized in Initialize()
        private MapLoaderSystem _mapLoaderSystem = null!; // Initialized in Initialize()
        private MapConnectionSystem _mapConnectionSystem = null!; // Initialized in Initialize()
        private CameraSystem _cameraSystem = null!; // Initialized in Initialize()
        private CameraViewportSystem _cameraViewportSystem = null!; // Initialized in Initialize()
        private MapRendererSystem _mapRendererSystem = null!; // Initialized in Initialize()
        private MapBorderRendererSystem _mapBorderRendererSystem = null!; // Initialized in Initialize()
        private AnimatedTileSystem _animatedTileSystem = null!; // Initialized in Initialize()
        private SpriteAnimationSystem _spriteAnimationSystem = null!; // Initialized in Initialize()
        private SpriteRendererSystem _spriteRendererSystem = null!; // Initialized in Initialize()
        private SpriteSheetSystem _spriteSheetSystem = null!; // Initialized in Initialize()
        private PlayerSystem _playerSystem = null!; // Initialized in Initialize()
        private InputSystem _inputSystem = null!; // Initialized in Initialize()
        private MovementSystem _movementSystem = null!; // Initialized in Initialize()
        private SceneManagerSystem _sceneManagerSystem = null!; // Initialized in Initialize()
        private SceneInputSystem _sceneInputSystem = null!; // Initialized in Initialize()
        private SceneRendererSystem _sceneRendererSystem = null!; // Initialized in Initialize()
        private Services.InputBindingService _inputBindingService = null!; // Initialized in Initialize()
        private Scenes.Systems.DebugBarRendererSystem? _debugBarRendererSystem; // Initialized in Initialize(), may be null
        private MapTransitionDetectionSystem _mapTransitionDetectionSystem = null!; // Initialized in Initialize()
        private MapPopupOrchestratorSystem _mapPopupOrchestratorSystem = null!; // Initialized in Initialize()
        private MapPopupSystem _mapPopupSystem = null!; // Initialized in Initialize()
        private MapPopupRendererSystem _mapPopupRendererSystem = null!; // Initialized in Initialize()
        private Scenes.Systems.LoadingSceneRendererSystem? _loadingSceneRendererSystem; // Initialized in Initialize(), may be null
        private VisibilityFlagSystem _visibilityFlagSystem = null!; // Initialized in Initialize()
        private ActiveMapManagementSystem _activeMapManagementSystem = null!; // Initialized in Initialize()
        private Services.IActiveMapFilterService _activeMapFilterService = null!; // Initialized in Initialize()
        private Rendering.RenderTargetManager? _renderTargetManager; // Initialized in Initialize(), may be null
        private ShaderManagerSystem? _shaderManagerSystem; // Initialized in Initialize(), may be null
        private ShaderRendererSystem? _shaderRendererSystem; // Initialized in Initialize(), may be null
        private ShaderParameterAnimationSystem? _shaderParameterAnimationSystem; // Initialized in Initialize(), may be null

        private bool _isInitialized;
        private bool _isDisposed;

        /// <summary>
        /// Initializes a new instance of the SystemManager.
        /// </summary>
        /// <param name="world">The ECS world.</param>
        /// <param name="graphicsDevice">The graphics device.</param>
        /// <param name="modManager">The mod manager.</param>
        /// <param name="tilesetLoader">The tileset loader service.</param>
        /// <param name="game">The game instance for accessing services.</param>
        /// <param name="logger">The logger for logging operations.</param>
        public SystemManager(
            World world,
            GraphicsDevice graphicsDevice,
            IModManager modManager,
            ITilesetLoaderService tilesetLoader,
            Game game,
            ILogger logger
        )
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _graphicsDevice =
                graphicsDevice ?? throw new ArgumentNullException(nameof(graphicsDevice));
            _modManager = modManager ?? throw new ArgumentNullException(nameof(modManager));
            _tilesetLoader =
                tilesetLoader ?? throw new ArgumentNullException(nameof(tilesetLoader));
            _game = game ?? throw new ArgumentNullException(nameof(game));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Gets the map loader system.
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown if systems are not initialized.</exception>
        public MapLoaderSystem MapLoaderSystem
        {
            get
            {
                if (!_isInitialized)
                {
                    throw new InvalidOperationException(
                        "Systems are not initialized. Call Initialize() first."
                    );
                }
                return _mapLoaderSystem;
            }
        }

        /// <summary>
        /// Gets the map renderer system.
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown if systems are not initialized.</exception>
        public MapRendererSystem MapRendererSystem
        {
            get
            {
                if (!_isInitialized)
                {
                    throw new InvalidOperationException(
                        "Systems are not initialized. Call Initialize() first."
                    );
                }
                return _mapRendererSystem;
            }
        }

        /// <summary>
        /// Gets the camera system.
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown if systems are not initialized.</exception>
        public CameraSystem CameraSystem
        {
            get
            {
                if (!_isInitialized)
                {
                    throw new InvalidOperationException(
                        "Systems are not initialized. Call Initialize() first."
                    );
                }
                return _cameraSystem;
            }
        }

        /// <summary>
        /// Gets the scene manager system.
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown if systems are not initialized.</exception>
        public SceneManagerSystem SceneManagerSystem
        {
            get
            {
                if (!_isInitialized)
                {
                    throw new InvalidOperationException(
                        "Systems are not initialized. Call Initialize() first."
                    );
                }
                return _sceneManagerSystem;
            }
        }

        /// <summary>
        /// Gets the scene input system.
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown if systems are not initialized.</exception>
        public SceneInputSystem SceneInputSystem
        {
            get
            {
                if (!_isInitialized)
                {
                    throw new InvalidOperationException(
                        "Systems are not initialized. Call Initialize() first."
                    );
                }
                return _sceneInputSystem;
            }
        }

        /// <summary>
        /// Gets the scene renderer system.
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown if systems are not initialized.</exception>
        public SceneRendererSystem SceneRendererSystem
        {
            get
            {
                if (!_isInitialized)
                {
                    throw new InvalidOperationException(
                        "Systems are not initialized. Call Initialize() first."
                    );
                }
                return _sceneRendererSystem;
            }
        }

        /// <summary>
        /// Gets the map popup renderer system.
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown if systems are not initialized.</exception>
        public MapPopupRendererSystem MapPopupRendererSystem
        {
            get
            {
                if (!_isInitialized)
                {
                    throw new InvalidOperationException(
                        "Systems are not initialized. Call Initialize() first."
                    );
                }
                return _mapPopupRendererSystem;
            }
        }

        /// <summary>
        /// Gets the player system.
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown if systems are not initialized.</exception>
        public PlayerSystem PlayerSystem
        {
            get
            {
                if (!_isInitialized)
                {
                    throw new InvalidOperationException(
                        "Systems are not initialized. Call Initialize() first."
                    );
                }
                return _playerSystem;
            }
        }

        /// <summary>
        /// Initializes all ECS systems. Should be called from LoadContent().
        /// </summary>
        /// <param name="spriteBatch">The sprite batch for rendering.</param>
        public void Initialize(SpriteBatch spriteBatch)
        {
            if (_isInitialized)
            {
                _logger.Warning("Systems already initialized");
                return;
            }

            if (_isDisposed)
            {
                throw new ObjectDisposedException(nameof(SystemManager));
            }

            _spriteBatch = spriteBatch ?? throw new ArgumentNullException(nameof(spriteBatch));

            _logger.Information("Initializing ECS systems");

            // TODO: Register components with Arch.Persistence when persistence is implemented
            // Components to register:
            // - FlagsComponent
            // - VariablesComponent
            // - EntityFlagsComponent
            // - EntityVariablesComponent
            // - FlagVariableMetadataComponent
            // Example: world.RegisterComponent<FlagsComponent>();

            // Get FlagVariableService from Game.Services
            var flagVariableService = _game.Services.GetService<Services.IFlagVariableService>();
            if (flagVariableService == null)
            {
                throw new InvalidOperationException(
                    "IFlagVariableService is not available in Game.Services. "
                        + "Ensure GameServices.Initialize() was called."
                );
            }

            // Create VariableSpriteResolver
            _variableSpriteResolver = new Services.VariableSpriteResolver(
                flagVariableService,
                LoggerFactory.CreateLogger<Services.VariableSpriteResolver>()
            );

            // Create services
            _spriteLoader = new SpriteLoaderService(
                _graphicsDevice,
                _modManager,
                _variableSpriteResolver,
                LoggerFactory.CreateLogger<SpriteLoaderService>()
            );
            _cameraService = new CameraService(_world, LoggerFactory.CreateLogger<CameraService>());

            // Create active map filter service (used by multiple systems for filtering entities by active maps)
            // Must be created before render systems that depend on it
            _activeMapFilterService = new Services.ActiveMapFilterService(_world);

            // Create shader services and systems
            var shaderService = _game.Services.GetService<Rendering.IShaderService>();
            var shaderParameterValidator =
                _game.Services.GetService<Rendering.IShaderParameterValidator>();

            if (shaderService != null && shaderParameterValidator != null)
            {
                _renderTargetManager = new Rendering.RenderTargetManager(
                    _graphicsDevice,
                    LoggerFactory.CreateLogger<Rendering.RenderTargetManager>()
                );
                _shaderManagerSystem = new ShaderManagerSystem(
                    _world,
                    shaderService,
                    shaderParameterValidator,
                    _graphicsDevice,
                    LoggerFactory.CreateLogger<ShaderManagerSystem>()
                );
                _shaderRendererSystem = new ShaderRendererSystem(
                    LoggerFactory.CreateLogger<ShaderRendererSystem>()
                );
                _shaderParameterAnimationSystem = new ShaderParameterAnimationSystem(
                    _world,
                    _shaderManagerSystem,
                    LoggerFactory.CreateLogger<ShaderParameterAnimationSystem>()
                );
            }

            // Create update systems
            _mapLoaderSystem = new MapLoaderSystem(
                _world,
                _modManager.Registry,
                _tilesetLoader,
                _spriteLoader,
                flagVariableService,
                _variableSpriteResolver,
                LoggerFactory.CreateLogger<MapLoaderSystem>()
            );
            _mapConnectionSystem = new MapConnectionSystem(
                _world,
                LoggerFactory.CreateLogger<MapConnectionSystem>()
            );
            _cameraSystem = new CameraSystem(
                _world,
                _spriteLoader,
                LoggerFactory.CreateLogger<CameraSystem>()
            );
            _cameraViewportSystem = new CameraViewportSystem(
                _world,
                _graphicsDevice,
                GameConstants.GbaReferenceWidth,
                GameConstants.GbaReferenceHeight,
                LoggerFactory.CreateLogger<CameraViewportSystem>()
            ); // GBA resolution

            // Create render systems
            _mapRendererSystem = new MapRendererSystem(
                _world,
                _graphicsDevice,
                _tilesetLoader,
                _cameraService,
                LoggerFactory.CreateLogger<MapRendererSystem>(),
                _shaderManagerSystem,
                _shaderRendererSystem,
                _renderTargetManager
            );
            _mapRendererSystem.SetSpriteBatch(_spriteBatch);
            _mapBorderRendererSystem = new MapBorderRendererSystem(
                _world,
                _graphicsDevice,
                _tilesetLoader,
                _cameraService,
                _activeMapFilterService,
                LoggerFactory.CreateLogger<MapBorderRendererSystem>()
            );
            _mapBorderRendererSystem.SetSpriteBatch(_spriteBatch);
            _spriteRendererSystem = new SpriteRendererSystem(
                _world,
                _graphicsDevice,
                _spriteLoader,
                _cameraService,
                LoggerFactory.CreateLogger<SpriteRendererSystem>(),
                _shaderManagerSystem,
                shaderService,
                _shaderRendererSystem,
                _renderTargetManager
            );
            _spriteRendererSystem.SetSpriteBatch(_spriteBatch);

            // Create animation systems
            _animatedTileSystem = new AnimatedTileSystem(
                _world,
                _tilesetLoader,
                LoggerFactory.CreateLogger<AnimatedTileSystem>()
            );
            _spriteAnimationSystem = new SpriteAnimationSystem(
                _world,
                _spriteLoader,
                LoggerFactory.CreateLogger<SpriteAnimationSystem>()
            );

            // Create sprite sheet system (handles sprite sheet switching for entities with SpriteSheetComponent)
            // Must be initialized before systems that might publish SpriteSheetChangeRequestEvent
            _spriteSheetSystem = new SpriteSheetSystem(
                _world,
                _spriteLoader,
                LoggerFactory.CreateLogger<SpriteSheetSystem>()
            );

            // Create player system
            _playerSystem = new PlayerSystem(
                _world,
                _cameraService,
                _spriteLoader,
                _modManager,
                LoggerFactory.CreateLogger<PlayerSystem>()
            );

            // Create input and movement services
            var inputBuffer = new Services.InputBuffer(
                LoggerFactory.CreateLogger<Services.InputBuffer>(),
                GameConstants.InputBufferMaxSize,
                GameConstants.InputBufferTimeoutSeconds
            );
            _inputBindingService = new Services.InputBindingService(
                LoggerFactory.CreateLogger<Services.InputBindingService>()
            );
            var nullInputBlocker = new Services.NullInputBlocker();
            var nullCollisionService = new Services.NullCollisionService();

            // Create active map management system (manages ActiveMapEntity tag component)
            _activeMapManagementSystem = new ActiveMapManagementSystem(
                _world,
                _activeMapFilterService,
                LoggerFactory.CreateLogger<ActiveMapManagementSystem>()
            );

            // Create input system
            _inputSystem = new InputSystem(
                _world,
                nullInputBlocker,
                inputBuffer,
                _inputBindingService,
                LoggerFactory.CreateLogger<InputSystem>()
            );

            // Create movement system (handles animation state directly, matching oldmonoball architecture)
            _movementSystem = new MovementSystem(
                _world,
                nullCollisionService,
                _activeMapFilterService,
                _modManager,
                LoggerFactory.CreateLogger<MovementSystem>()
            );

            // Create scene systems
            _sceneManagerSystem = new SceneManagerSystem(
                _world,
                LoggerFactory.CreateLogger<SceneManagerSystem>()
            );
            _sceneInputSystem = new SceneInputSystem(
                _world,
                _sceneManagerSystem,
                LoggerFactory.CreateLogger<SceneInputSystem>()
            );
            _sceneRendererSystem = new SceneRendererSystem(
                _world,
                _graphicsDevice,
                _sceneManagerSystem,
                LoggerFactory.CreateLogger<SceneRendererSystem>(),
                _shaderManagerSystem,
                _renderTargetManager,
                _shaderRendererSystem
            );
            _sceneRendererSystem.SetSpriteBatch(_spriteBatch);
            _sceneRendererSystem.SetMapRendererSystem(_mapRendererSystem);
            _sceneRendererSystem.SetMapBorderRendererSystem(_mapBorderRendererSystem);
            _sceneRendererSystem.SetSpriteRendererSystem(_spriteRendererSystem);

            // Get FontService from Game.Services (created earlier in GameServices.LoadMods)
            var fontService = _game.Services.GetService<Rendering.FontService>();
            if (fontService == null)
            {
                throw new InvalidOperationException(
                    "FontService is not available in Game.Services. "
                        + "Ensure GameServices.Initialize() was called and mods were loaded successfully."
                );
            }
            var performanceStatsSystem = new PerformanceStatsSystem(
                _world,
                LoggerFactory.CreateLogger<PerformanceStatsSystem>()
            );

            // Create debug bar systems
            var debugBarToggleSystem = new Scenes.Systems.DebugBarToggleSystem(
                _world,
                _sceneManagerSystem,
                _inputBindingService,
                LoggerFactory.CreateLogger<Scenes.Systems.DebugBarToggleSystem>()
            );
            _debugBarRendererSystem = new Scenes.Systems.DebugBarRendererSystem(
                _world,
                _graphicsDevice,
                fontService,
                performanceStatsSystem,
                _spriteBatch,
                LoggerFactory.CreateLogger<Scenes.Systems.DebugBarRendererSystem>()
            );
            _sceneRendererSystem.SetDebugBarRendererSystem(_debugBarRendererSystem);

            // Create shader cycle system (for cycling through shader effects with F4)
            Scenes.Systems.ShaderCycleSystem? shaderCycleSystem = null;
            if (_shaderManagerSystem != null)
            {
                shaderCycleSystem = new Scenes.Systems.ShaderCycleSystem(
                    _world,
                    _inputBindingService,
                    _shaderManagerSystem,
                    LoggerFactory.CreateLogger<Scenes.Systems.ShaderCycleSystem>()
                );
            }

            // Create player shader cycle system (for cycling through player shader effects with F5)
            Scenes.Systems.PlayerShaderCycleSystem? playerShaderCycleSystem = null;
            if (_shaderManagerSystem != null)
            {
                playerShaderCycleSystem = new Scenes.Systems.PlayerShaderCycleSystem(
                    _world,
                    _inputBindingService,
                    _playerSystem,
                    _shaderManagerSystem,
                    LoggerFactory.CreateLogger<Scenes.Systems.PlayerShaderCycleSystem>()
                );
            }

            // Create map transition detection system (detects when player crosses map boundaries)
            _mapTransitionDetectionSystem = new MapTransitionDetectionSystem(
                _world,
                _activeMapFilterService,
                LoggerFactory.CreateLogger<MapTransitionDetectionSystem>()
            );

            // Create popup systems (after SceneManagerSystem, before SceneRendererSystem)
            _mapPopupOrchestratorSystem = new MapPopupOrchestratorSystem(
                _world,
                _modManager,
                LoggerFactory.CreateLogger<MapPopupOrchestratorSystem>()
            );
            _mapPopupSystem = new MapPopupSystem(
                _world,
                _sceneManagerSystem,
                fontService,
                _modManager,
                LoggerFactory.CreateLogger<MapPopupSystem>()
            );
            _mapPopupRendererSystem = new MapPopupRendererSystem(
                _world,
                _graphicsDevice,
                _spriteBatch,
                fontService,
                _modManager,
                LoggerFactory.CreateLogger<MapPopupRendererSystem>()
            );
            _sceneRendererSystem.SetMapPopupRendererSystem(_mapPopupRendererSystem);

            // Create loading scene renderer system (optional, for loading screen)
            _loadingSceneRendererSystem = new Scenes.Systems.LoadingSceneRendererSystem(
                _world,
                _graphicsDevice,
                _spriteBatch,
                _game,
                LoggerFactory.CreateLogger<Scenes.Systems.LoadingSceneRendererSystem>()
            );
            _sceneRendererSystem.SetLoadingSceneRendererSystem(_loadingSceneRendererSystem);

            // Create visibility flag system (reacts to flag changes)
            _visibilityFlagSystem = new VisibilityFlagSystem(
                _world,
                flagVariableService,
                LoggerFactory.CreateLogger<VisibilityFlagSystem>()
            );

            // Update render systems to track draw calls
            _mapRendererSystem.SetPerformanceStatsSystem(performanceStatsSystem);
            _mapBorderRendererSystem.SetPerformanceStatsSystem(performanceStatsSystem);
            _spriteRendererSystem.SetPerformanceStatsSystem(performanceStatsSystem);

            // Group update systems (including scene systems)
            // SpriteSheetSystem is added early to ensure it's initialized before systems that might publish SpriteSheetChangeRequestEvent
            // ActiveMapManagementSystem runs early to tag entities in active maps (needed by other systems)
            // InputSystem runs first (Priority 0) to process input and create MovementRequest components
            // MovementSystem runs after InputSystem (Priority 90) to process MovementRequest, update movement, AND handle animation
            if (_shaderParameterAnimationSystem != null)
            {
                var systems = new List<BaseSystem<World, float>>
                {
                    _mapLoaderSystem,
                    _mapConnectionSystem,
                    _activeMapManagementSystem, // Manage ActiveMapEntity tags (runs early, before systems that filter by active maps)
                    _playerSystem, // Player initialization only (no per-frame updates)
                    _inputSystem, // Priority 0: Process input, create MovementRequest
                    _movementSystem, // Priority 90: Process MovementRequest, update movement and animation
                    _mapTransitionDetectionSystem, // Detect map transitions after movement updates
                    _cameraSystem, // Camera follows player (runs after movement updates)
                    _cameraViewportSystem,
                    _animatedTileSystem,
                    _spriteAnimationSystem, // Animation frame updates (CurrentFrame, FrameTimer)
                    _spriteSheetSystem,
                    _visibilityFlagSystem, // Update entity visibility based on flags
                    performanceStatsSystem, // Track performance stats each frame
                    _sceneManagerSystem,
                    _sceneInputSystem,
                    _mapPopupOrchestratorSystem, // Listen for map transitions and trigger popups
                    _mapPopupSystem, // Manage popup lifecycle and animation
                    debugBarToggleSystem, // Handle debug bar toggle input
                    _shaderParameterAnimationSystem, // Animate shader parameters
                };

                if (shaderCycleSystem != null)
                {
                    systems.Add(shaderCycleSystem); // Handle shader cycling input (F4)
                }

                if (playerShaderCycleSystem != null)
                {
                    systems.Add(playerShaderCycleSystem); // Handle player shader cycling input (F5)
                }

                _updateSystems = new Group<float>("UpdateSystems", systems.ToArray());
            }
            else
            {
                var systems = new List<BaseSystem<World, float>>
                {
                    _mapLoaderSystem,
                    _mapConnectionSystem,
                    _activeMapManagementSystem, // Manage ActiveMapEntity tags (runs early, before systems that filter by active maps)
                    _playerSystem, // Player initialization only (no per-frame updates)
                    _inputSystem, // Priority 0: Process input, create MovementRequest
                    _movementSystem, // Priority 90: Process MovementRequest, update movement and animation
                    _mapTransitionDetectionSystem, // Detect map transitions after movement updates
                    _cameraSystem, // Camera follows player (runs after movement updates)
                    _cameraViewportSystem,
                    _animatedTileSystem,
                    _spriteAnimationSystem, // Animation frame updates (CurrentFrame, FrameTimer)
                    _spriteSheetSystem,
                    _visibilityFlagSystem, // Update entity visibility based on flags
                    performanceStatsSystem, // Track performance stats each frame
                    _sceneManagerSystem,
                    _sceneInputSystem,
                    _mapPopupOrchestratorSystem, // Listen for map transitions and trigger popups
                    _mapPopupSystem, // Manage popup lifecycle and animation
                    debugBarToggleSystem, // Handle debug bar toggle input
                };

                if (shaderCycleSystem != null)
                {
                    systems.Add(shaderCycleSystem); // Handle shader cycling input (F4)
                }

                if (playerShaderCycleSystem != null)
                {
                    systems.Add(playerShaderCycleSystem); // Handle player shader cycling input (F5)
                }

                _updateSystems = new Group<float>("UpdateSystems", systems.ToArray());
            }

            _updateSystems.Initialize();

            _isInitialized = true;
            _logger.Information("ECS systems initialized successfully");
        }

        /// <summary>
        /// Updates all ECS systems. Should be called from Game.Update().
        /// </summary>
        /// <param name="gameTime">The game time.</param>
        public void Update(GameTime gameTime)
        {
            if (!_isInitialized || _isDisposed)
            {
                return;
            }

            float deltaTime = (float)gameTime.ElapsedGameTime.TotalSeconds;
            _updateSystems.BeforeUpdate(in deltaTime);
            _updateSystems.Update(in deltaTime);
            _updateSystems.AfterUpdate(in deltaTime);
        }

        /// <summary>
        /// Renders all ECS systems. Should be called from Game.Draw().
        /// </summary>
        /// <param name="gameTime">The game time.</param>
        public void Render(GameTime gameTime)
        {
            if (!_isInitialized || _isDisposed)
            {
                return;
            }

            // Render scenes (which will call MapRendererSystem for GameScene)
            _sceneRendererSystem.Render(gameTime);
        }

        /// <summary>
        /// Disposes of all systems and resources.
        /// </summary>
        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            _logger.Debug("Disposing systems");

            if (_isInitialized)
            {
                _updateSystems.Dispose();
                _sceneManagerSystem?.Cleanup();
            }

            // Reset to null after disposal (systems are no longer valid)
            _updateSystems = null!;
            _mapLoaderSystem = null!;
            _mapConnectionSystem = null!;
            _cameraSystem = null!;
            _cameraViewportSystem = null!;
            _mapRendererSystem = null!;
            _animatedTileSystem = null!;
            _spriteAnimationSystem = null!;
            _spriteRendererSystem = null!;
            _spriteSheetSystem = null!;
            _playerSystem = null!;
            _inputSystem = null!;
            _movementSystem = null!;
            _sceneManagerSystem = null!;
            _sceneInputSystem = null!;
            _sceneRendererSystem = null!;
            _debugBarRendererSystem?.Dispose();
            _debugBarRendererSystem = null;
            _mapTransitionDetectionSystem = null!;
            _mapPopupOrchestratorSystem?.Dispose();
            _mapPopupOrchestratorSystem = null!;
            _mapPopupSystem?.Dispose();
            _mapPopupSystem = null!;
            _mapPopupRendererSystem = null!;

            // Dispose shader systems
            _shaderParameterAnimationSystem?.Dispose();
            _shaderParameterAnimationSystem = null;
            _renderTargetManager?.Dispose();
            _renderTargetManager = null;
            // ShaderManagerSystem doesn't need disposal (no managed resources)

            // Dispose services
            _variableSpriteResolver?.Dispose();
            _variableSpriteResolver = null;

            _isDisposed = true;
        }
    }
}
