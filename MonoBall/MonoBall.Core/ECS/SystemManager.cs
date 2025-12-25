using System;
using System.Collections.Generic;
using System.Linq;
using Arch.Core;
using Arch.System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoBall.Core;
using MonoBall.Core.ECS;
using MonoBall.Core.ECS.Components;
using MonoBall.Core.ECS.Services;
using MonoBall.Core.ECS.Systems;
using MonoBall.Core.Logging;
using MonoBall.Core.Maps;
using MonoBall.Core.Mods;
using MonoBall.Core.Rendering;
using MonoBall.Core.Scenes.Events;
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
        private readonly List<BaseSystem<World, float>> _registeredUpdateSystems = new();
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
        private SceneSystem _sceneSystem = null!; // Initialized in Initialize()
        private SceneInputSystem _sceneInputSystem = null!; // Initialized in Initialize()
        private GameSceneSystem _gameSceneSystem = null!; // Initialized in Initialize()
        private LoadingSceneSystem _loadingSceneSystem = null!; // Initialized in Initialize()
        private DebugBarSceneSystem _debugBarSceneSystem = null!; // Initialized in Initialize()
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
        private Rendering.ShaderTemplateSystem? _shaderTemplateSystem; // Initialized in Initialize(), may be null

        private bool _isInitialized;
        private bool _isDisposed;
        private bool _cachedIsUpdateBlocked;
        private bool _isUpdateBlockedCacheValid;

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
        /// Gets the scene system.
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown if systems are not initialized.</exception>
        public SceneSystem SceneSystem
        {
            get
            {
                if (!_isInitialized)
                {
                    throw new InvalidOperationException(
                        "Systems are not initialized. Call Initialize() first."
                    );
                }
                return _sceneSystem;
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
        /// Gets the loading scene system.
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown if systems are not initialized.</exception>
        public LoadingSceneSystem LoadingSceneSystem
        {
            get
            {
                if (!_isInitialized)
                {
                    throw new InvalidOperationException(
                        "Systems are not initialized. Call Initialize() first."
                    );
                }
                return _loadingSceneSystem;
            }
        }

        /// <summary>
        /// Gets the loading scene renderer system.
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown if systems are not initialized.</exception>
        public Scenes.Systems.LoadingSceneRendererSystem? LoadingSceneRendererSystem
        {
            get
            {
                if (!_isInitialized)
                {
                    throw new InvalidOperationException(
                        "Systems are not initialized. Call Initialize() first."
                    );
                }
                return _loadingSceneRendererSystem;
            }
        }

        /// <summary>
        /// Registers an update system with priority-based sorting.
        /// </summary>
        /// <param name="system">The system to register.</param>
        /// <exception cref="ArgumentNullException">Thrown if system is null.</exception>
        /// <exception cref="ArgumentException">Thrown if system does not implement IPrioritizedSystem.</exception>
        private void RegisterUpdateSystem(BaseSystem<World, float> system)
        {
            if (system == null)
            {
                throw new ArgumentNullException(nameof(system));
            }

            if (system is not IPrioritizedSystem prioritizedSystem)
            {
                throw new ArgumentException(
                    $"System {system.GetType().Name} does not implement IPrioritizedSystem.",
                    nameof(system)
                );
            }

            int priority = prioritizedSystem.Priority;
            if (priority < 0)
            {
                _logger.Warning(
                    "System {SystemName} has negative priority {Priority}",
                    system.GetType().Name,
                    priority
                );
            }

            // Check for duplicate priorities (warn but allow)
            var existingSystem = _registeredUpdateSystems.FirstOrDefault(s =>
                s is IPrioritizedSystem ps && ps.Priority == priority
            );
            if (existingSystem != null)
            {
                _logger.Warning(
                    "Duplicate priority {Priority} found: {SystemName} and {ExistingSystemName}",
                    priority,
                    system.GetType().Name,
                    existingSystem.GetType().Name
                );
            }

            _registeredUpdateSystems.Add(system);
            // Note: Sorting is deferred until all systems are registered to avoid O(n log n) on every registration.
            // Sort will be done once in Initialize() before creating the Group.

            _logger.Debug(
                "Registered update system: {SystemName} (Priority: {Priority})",
                system.GetType().Name,
                priority
            );
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

            // Initialize core services
            InitializeCoreServices();

            // Create scene systems (needed early for loading scene)
            CreateSceneSystems();

            // Create SceneSystem, LoadingSceneRendererSystem, and LoadingSceneSystem FIRST
            // These are needed early for loading scene creation
            _sceneSystem = new SceneSystem(_world, LoggerFactory.CreateLogger<SceneSystem>());
            RegisterUpdateSystem(_sceneSystem);

            // Subscribe to scene events to invalidate update blocking cache
            EventBus.Subscribe<SceneCreatedEvent>(OnSceneCreated);
            EventBus.Subscribe<SceneDestroyedEvent>(OnSceneDestroyed);
            EventBus.Subscribe<SceneActivatedEvent>(OnSceneActivated);
            EventBus.Subscribe<SceneDeactivatedEvent>(OnSceneDeactivated);
            EventBus.Subscribe<ScenePausedEvent>(OnScenePaused);
            EventBus.Subscribe<SceneResumedEvent>(OnSceneResumed);

            // Get FontService from Game.Services (needed for scene systems)
            var fontService = _game.Services.GetService<Rendering.FontService>();
            if (fontService == null)
            {
                throw new InvalidOperationException(
                    "FontService is not available in Game.Services. "
                        + "Ensure GameServices.Initialize() was called and mods were loaded successfully."
                );
            }

            // Create loading scene renderer system (needed for LoadingSceneSystem)
            _loadingSceneRendererSystem = new Scenes.Systems.LoadingSceneRendererSystem(
                _world,
                _graphicsDevice,
                _spriteBatch,
                _game,
                LoggerFactory.CreateLogger<Scenes.Systems.LoadingSceneRendererSystem>()
            );

            // Create loading scene system
            _loadingSceneSystem = new LoadingSceneSystem(
                _world,
                _graphicsDevice,
                _spriteBatch,
                _loadingSceneRendererSystem,
                LoggerFactory.CreateLogger<LoadingSceneSystem>()
            );
            RegisterUpdateSystem(_loadingSceneSystem);

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
                _shaderTemplateSystem = new Rendering.ShaderTemplateSystem(
                    _world,
                    _modManager,
                    LoggerFactory.CreateLogger<Rendering.ShaderTemplateSystem>()
                );
            }

            // Create game systems
            CreateGameSystems();

            // Create render systems
            CreateRenderSystems();

            // Create animation and visibility systems (including PerformanceStatsSystem)
            CreateAnimationAndVisibilitySystems();

            // Create scene-specific systems
            CreateSceneSpecificSystems();

            // Finalize initialization
            FinalizeInitialization();
        }

        /// <summary>
        /// Initializes core services required by systems.
        /// </summary>
        private void InitializeCoreServices()
        {
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
        }

        /// <summary>
        /// Creates scene systems (SceneSystem, LoadingSceneSystem, etc.).
        /// </summary>
        private void CreateSceneSystems()
        {
            if (_spriteBatch == null)
            {
                throw new InvalidOperationException(
                    "SpriteBatch is null. Ensure Initialize() was called with a valid SpriteBatch."
                );
            }

            // Create SceneSystem FIRST
            _sceneSystem = new SceneSystem(_world, LoggerFactory.CreateLogger<SceneSystem>());
            RegisterUpdateSystem(_sceneSystem);

            // Subscribe to scene events to invalidate update blocking cache
            EventBus.Subscribe<SceneCreatedEvent>(OnSceneCreated);
            EventBus.Subscribe<SceneDestroyedEvent>(OnSceneDestroyed);
            EventBus.Subscribe<SceneActivatedEvent>(OnSceneActivated);
            EventBus.Subscribe<SceneDeactivatedEvent>(OnSceneDeactivated);
            EventBus.Subscribe<ScenePausedEvent>(OnScenePaused);
            EventBus.Subscribe<SceneResumedEvent>(OnSceneResumed);

            // Get FontService from Game.Services (needed for scene systems)
            var fontService = _game.Services.GetService<Rendering.FontService>();
            if (fontService == null)
            {
                throw new InvalidOperationException(
                    "FontService is not available in Game.Services. "
                        + "Ensure GameServices.Initialize() was called and mods were loaded successfully."
                );
            }

            // Create loading scene renderer system (needed for LoadingSceneSystem)
            _loadingSceneRendererSystem = new Scenes.Systems.LoadingSceneRendererSystem(
                _world,
                _graphicsDevice,
                _spriteBatch,
                _game,
                LoggerFactory.CreateLogger<Scenes.Systems.LoadingSceneRendererSystem>()
            );

            // Create loading scene system
            _loadingSceneSystem = new LoadingSceneSystem(
                _world,
                _graphicsDevice,
                _spriteBatch,
                _loadingSceneRendererSystem,
                LoggerFactory.CreateLogger<LoadingSceneSystem>()
            );
            RegisterUpdateSystem(_loadingSceneSystem);

            // Create scene input system (needs SceneSystem, so created after it)
            _sceneInputSystem = new Scenes.Systems.SceneInputSystem(
                _world,
                _sceneSystem,
                LoggerFactory.CreateLogger<Scenes.Systems.SceneInputSystem>()
            );
            RegisterUpdateSystem(_sceneInputSystem);
        }

        /// <summary>
        /// Creates game systems (map loading, player, input, movement, etc.).
        /// </summary>
        private void CreateGameSystems()
        {
            // Get FlagVariableService from Game.Services
            var flagVariableService = _game.Services.GetService<Services.IFlagVariableService>();
            if (flagVariableService == null)
            {
                throw new InvalidOperationException(
                    "IFlagVariableService is not available in Game.Services. "
                        + "Ensure GameServices.Initialize() was called."
                );
            }

            // Create shader services and systems
            CreateShaderSystems();

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
            RegisterUpdateSystem(_mapLoaderSystem);

            _mapConnectionSystem = new MapConnectionSystem(
                _world,
                LoggerFactory.CreateLogger<MapConnectionSystem>()
            );
            RegisterUpdateSystem(_mapConnectionSystem);

            // Create active map management system (manages ActiveMapEntity tag component)
            _activeMapManagementSystem = new ActiveMapManagementSystem(
                _world,
                _activeMapFilterService,
                LoggerFactory.CreateLogger<ActiveMapManagementSystem>()
            );
            RegisterUpdateSystem(_activeMapManagementSystem);

            // Create player system
            _playerSystem = new PlayerSystem(
                _world,
                _cameraService,
                _spriteLoader,
                _modManager,
                LoggerFactory.CreateLogger<PlayerSystem>()
            );
            RegisterUpdateSystem(_playerSystem);

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

            // Create input system
            _inputSystem = new InputSystem(
                _world,
                nullInputBlocker,
                inputBuffer,
                _inputBindingService,
                LoggerFactory.CreateLogger<InputSystem>()
            );
            RegisterUpdateSystem(_inputSystem);

            // Create movement system (handles animation state directly, matching oldmonoball architecture)
            _movementSystem = new MovementSystem(
                _world,
                nullCollisionService,
                _activeMapFilterService,
                _modManager,
                LoggerFactory.CreateLogger<MovementSystem>()
            );
            RegisterUpdateSystem(_movementSystem);

            // Create map transition detection system (detects when player crosses map boundaries)
            _mapTransitionDetectionSystem = new MapTransitionDetectionSystem(
                _world,
                _activeMapFilterService,
                LoggerFactory.CreateLogger<MapTransitionDetectionSystem>()
            );
            RegisterUpdateSystem(_mapTransitionDetectionSystem);

            _cameraSystem = new CameraSystem(
                _world,
                _spriteLoader,
                LoggerFactory.CreateLogger<CameraSystem>()
            );
            RegisterUpdateSystem(_cameraSystem);

            _cameraViewportSystem = new CameraViewportSystem(
                _world,
                _graphicsDevice,
                GameConstants.GbaReferenceWidth,
                GameConstants.GbaReferenceHeight,
                LoggerFactory.CreateLogger<CameraViewportSystem>()
            ); // GBA resolution
            RegisterUpdateSystem(_cameraViewportSystem);
        }

        /// <summary>
        /// Creates shader-related systems.
        /// </summary>
        private void CreateShaderSystems()
        {
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
                _shaderTemplateSystem = new Rendering.ShaderTemplateSystem(
                    _world,
                    _modManager,
                    LoggerFactory.CreateLogger<Rendering.ShaderTemplateSystem>()
                );
            }
        }

        /// <summary>
        /// Creates render systems (map, sprite, border renderers).
        /// </summary>
        private void CreateRenderSystems()
        {
            // Get shader service (needed for sprite renderer)
            var shaderService = _game.Services.GetService<Rendering.IShaderService>();

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
            if (_spriteBatch == null)
            {
                throw new InvalidOperationException(
                    "SpriteBatch is null. Ensure Initialize() was called with a valid SpriteBatch."
                );
            }
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
        }

        /// <summary>
        /// Creates animation and visibility systems (animated tiles, sprite animation, sprite sheets, visibility flags, performance stats).
        /// </summary>
        private void CreateAnimationAndVisibilitySystems()
        {
            // Create animated tile system
            _animatedTileSystem = new AnimatedTileSystem(
                _world,
                _tilesetLoader,
                LoggerFactory.CreateLogger<AnimatedTileSystem>()
            );
            RegisterUpdateSystem(_animatedTileSystem);

            // Create sprite animation system
            _spriteAnimationSystem = new SpriteAnimationSystem(
                _world,
                _spriteLoader,
                LoggerFactory.CreateLogger<SpriteAnimationSystem>()
            );
            RegisterUpdateSystem(_spriteAnimationSystem);

            // Create sprite sheet system
            _spriteSheetSystem = new SpriteSheetSystem(
                _world,
                _spriteLoader,
                LoggerFactory.CreateLogger<SpriteSheetSystem>()
            );
            RegisterUpdateSystem(_spriteSheetSystem);

            // Create visibility flag system
            var flagVariableService = _game.Services.GetService<Services.IFlagVariableService>();
            if (flagVariableService == null)
            {
                throw new InvalidOperationException(
                    "IFlagVariableService is not available in Game.Services. "
                        + "Ensure GameServices.Initialize() was called."
                );
            }
            _visibilityFlagSystem = new VisibilityFlagSystem(
                _world,
                flagVariableService,
                LoggerFactory.CreateLogger<VisibilityFlagSystem>()
            );
            RegisterUpdateSystem(_visibilityFlagSystem);

            // Create performance stats system
            var performanceStatsSystem = new PerformanceStatsSystem(
                _world,
                LoggerFactory.CreateLogger<PerformanceStatsSystem>()
            );
            RegisterUpdateSystem(performanceStatsSystem);
        }

        /// <summary>
        /// Creates scene-specific systems (game scene, debug bar, popups).
        /// </summary>
        private void CreateSceneSpecificSystems()
        {
            // Get FontService from Game.Services (needed for scene systems)
            var fontService = _game.Services.GetService<Rendering.FontService>();
            if (fontService == null)
            {
                throw new InvalidOperationException(
                    "FontService is not available in Game.Services. "
                        + "Ensure GameServices.Initialize() was called and mods were loaded successfully."
                );
            }

            // Get performance stats system (needed for debug bar)
            var performanceStatsSystem = _registeredUpdateSystems
                .OfType<PerformanceStatsSystem>()
                .FirstOrDefault();
            if (performanceStatsSystem == null)
            {
                throw new InvalidOperationException(
                    "PerformanceStatsSystem not found. Ensure it was registered before calling CreateSceneSpecificSystems."
                );
            }

            // Create debug bar renderer system (needed for DebugBarSceneSystem)
            if (_spriteBatch == null)
            {
                throw new InvalidOperationException(
                    "SpriteBatch is null. Ensure Initialize() was called with a valid SpriteBatch."
                );
            }
            _debugBarRendererSystem = new Scenes.Systems.DebugBarRendererSystem(
                _world,
                _graphicsDevice,
                fontService,
                performanceStatsSystem,
                _spriteBatch,
                LoggerFactory.CreateLogger<Scenes.Systems.DebugBarRendererSystem>()
            );

            // Create map popup renderer system (needed for MapPopupSystem)
            _mapPopupRendererSystem = new MapPopupRendererSystem(
                _world,
                _graphicsDevice,
                _spriteBatch,
                fontService,
                _modManager,
                LoggerFactory.CreateLogger<MapPopupRendererSystem>()
            );

            // Create scene-specific systems (after all render systems are created)
            _gameSceneSystem = new GameSceneSystem(
                _world,
                _graphicsDevice,
                _spriteBatch,
                _mapRendererSystem,
                _spriteRendererSystem,
                _mapBorderRendererSystem,
                _shaderManagerSystem,
                _shaderRendererSystem,
                _renderTargetManager,
                LoggerFactory.CreateLogger<GameSceneSystem>()
            );
            RegisterUpdateSystem(_gameSceneSystem);

            _debugBarSceneSystem = new DebugBarSceneSystem(
                _world,
                _graphicsDevice,
                _spriteBatch,
                _debugBarRendererSystem,
                LoggerFactory.CreateLogger<DebugBarSceneSystem>()
            );
            RegisterUpdateSystem(_debugBarSceneSystem);

            // Create popup orchestrator system
            _mapPopupOrchestratorSystem = new MapPopupOrchestratorSystem(
                _world,
                _modManager,
                LoggerFactory.CreateLogger<MapPopupOrchestratorSystem>()
            );
            RegisterUpdateSystem(_mapPopupOrchestratorSystem);

            // Create map popup system (needs renderer system)
            _mapPopupSystem = new MapPopupSystem(
                _world,
                _sceneSystem,
                _graphicsDevice,
                _spriteBatch,
                _mapPopupRendererSystem,
                fontService,
                _modManager,
                LoggerFactory.CreateLogger<MapPopupSystem>()
            );
            RegisterUpdateSystem(_mapPopupSystem);

            // Create debug bar toggle system
            var debugBarToggleSystem = new Scenes.Systems.DebugBarToggleSystem(
                _world,
                _sceneSystem,
                _inputBindingService,
                LoggerFactory.CreateLogger<Scenes.Systems.DebugBarToggleSystem>()
            );
            RegisterUpdateSystem(debugBarToggleSystem);

            // Create shader cycle system (for cycling through shader effects with F4)
            if (_shaderManagerSystem != null)
            {
                var shaderCycleSystem = new Scenes.Systems.ShaderCycleSystem(
                    _world,
                    _inputBindingService,
                    _shaderManagerSystem,
                    LoggerFactory.CreateLogger<Scenes.Systems.ShaderCycleSystem>()
                );
                RegisterUpdateSystem(shaderCycleSystem);
            }

            // Create player shader cycle system (for cycling through player shader effects with F5)
            if (_shaderManagerSystem != null)
            {
                var playerShaderCycleSystem = new Scenes.Systems.PlayerShaderCycleSystem(
                    _world,
                    _inputBindingService,
                    _playerSystem,
                    _shaderManagerSystem,
                    LoggerFactory.CreateLogger<Scenes.Systems.PlayerShaderCycleSystem>()
                );
                RegisterUpdateSystem(playerShaderCycleSystem);
            }

            // Register shader parameter animation system if it exists
            if (_shaderParameterAnimationSystem != null)
            {
                RegisterUpdateSystem(_shaderParameterAnimationSystem);
            }

            // Create scene renderer system (coordinator) with references to scene systems
            _sceneRendererSystem = new SceneRendererSystem(
                _world,
                _graphicsDevice,
                _sceneSystem,
                _gameSceneSystem,
                _loadingSceneSystem,
                _debugBarSceneSystem,
                _mapPopupSystem,
                LoggerFactory.CreateLogger<SceneRendererSystem>(),
                _shaderManagerSystem
            );
            _sceneRendererSystem.SetSpriteBatch(_spriteBatch);
        }

        /// <summary>
        /// Finalizes system initialization by sorting systems and creating the update Group.
        /// </summary>
        private void FinalizeInitialization()
        {
            // Sort systems by priority once (before creating Group)
            _registeredUpdateSystems.Sort(
                (a, b) =>
                {
                    var priorityA = ((IPrioritizedSystem)a).Priority;
                    var priorityB = ((IPrioritizedSystem)b).Priority;
                    return priorityA.CompareTo(priorityB);
                }
            );

            // Create Group from registered systems (now sorted by priority)
            _updateSystems = new Group<float>("UpdateSystems", _registeredUpdateSystems.ToArray());
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

            // Check if any scene with BlocksUpdate=true is active
            // If so, only run SceneSystem and LoadingSceneSystem (they need to run to process loading)
            bool isUpdateBlocked = IsUpdateBlocked();

            if (isUpdateBlocked)
            {
                // Only run scene systems when updates are blocked (e.g., during loading)
                // SceneSystem needs to run to manage scene state
                // LoadingSceneSystem needs to run to process progress queue
                _sceneSystem.Update(in deltaTime);
                _loadingSceneSystem.Update(in deltaTime);
            }
            else
            {
                // Normal update: run all systems
                _updateSystems.BeforeUpdate(in deltaTime);
                _updateSystems.Update(in deltaTime);
                _updateSystems.AfterUpdate(in deltaTime);
            }
        }

        /// <summary>
        /// Checks if any active scene has BlocksUpdate=true.
        /// Uses cached result to avoid querying scenes every frame.
        /// </summary>
        /// <returns>True if updates are blocked, false otherwise.</returns>
        private bool IsUpdateBlocked()
        {
            // Return cached value if valid
            if (_isUpdateBlockedCacheValid)
            {
                return _cachedIsUpdateBlocked;
            }

            // Recalculate and cache
            bool isBlocked = false;
            _sceneSystem.IterateScenes(
                (sceneEntity, sceneComponent) =>
                {
                    if (
                        sceneComponent.IsActive
                        && !sceneComponent.IsPaused
                        && sceneComponent.BlocksUpdate
                    )
                    {
                        isBlocked = true;
                        return false; // Stop iterating
                    }
                    return true; // Continue iterating
                }
            );

            _cachedIsUpdateBlocked = isBlocked;
            _isUpdateBlockedCacheValid = true;
            return isBlocked;
        }

        /// <summary>
        /// Invalidates the update blocking cache. Called when scene state changes.
        /// </summary>
        private void InvalidateUpdateBlockedCache()
        {
            _isUpdateBlockedCacheValid = false;
        }

        /// <summary>
        /// Initializes the player entity using PlayerSystem.
        /// </summary>
        /// <param name="cameraEntity">Optional camera entity to use for spawn position.</param>
        public void InitializePlayer(Entity? cameraEntity = null)
        {
            _playerSystem.InitializePlayer(cameraEntity: cameraEntity);
        }

        /// <summary>
        /// Gets the player entity from PlayerSystem.
        /// </summary>
        /// <returns>The player entity, or null if not created yet.</returns>
        public Entity? GetPlayerEntity()
        {
            return _playerSystem.GetPlayerEntity();
        }

        /// <summary>
        /// Loads a map using MapLoaderSystem.
        /// </summary>
        /// <param name="mapId">The map ID to load.</param>
        public void LoadMap(string mapId)
        {
            _mapLoaderSystem.LoadMap(mapId);
        }

        /// <summary>
        /// Creates the game scene using SceneSystem.
        /// </summary>
        public void CreateGameScene()
        {
            var sceneComponent = new Scenes.Components.SceneComponent
            {
                SceneId = "game:main",
                Priority = Scenes.ScenePriorities.GameScene,
                CameraMode = Scenes.SceneCameraMode.GameCamera,
                CameraEntityId = null,
                BlocksUpdate = false,
                BlocksDraw = false,
                BlocksInput = false,
                IsActive = true,
                IsPaused = false,
                BackgroundColor = Color.Black,
            };

            var gameSceneComponent = new Scenes.Components.GameSceneComponent();

            var gameSceneEntity = _sceneSystem.CreateScene(sceneComponent, gameSceneComponent);
            _logger.Information("Game scene created: {EntityId}", gameSceneEntity.Id);
        }

        /// <summary>
        /// Handles SceneCreatedEvent by invalidating update blocking cache.
        /// </summary>
        /// <param name="evt">The scene created event.</param>
        private void OnSceneCreated(ref SceneCreatedEvent evt)
        {
            InvalidateUpdateBlockedCache();
        }

        /// <summary>
        /// Handles SceneDestroyedEvent by invalidating update blocking cache.
        /// </summary>
        /// <param name="evt">The scene destroyed event.</param>
        private void OnSceneDestroyed(ref SceneDestroyedEvent evt)
        {
            InvalidateUpdateBlockedCache();
        }

        /// <summary>
        /// Handles SceneActivatedEvent by invalidating update blocking cache.
        /// </summary>
        /// <param name="evt">The scene activated event.</param>
        private void OnSceneActivated(ref SceneActivatedEvent evt)
        {
            InvalidateUpdateBlockedCache();
        }

        /// <summary>
        /// Handles SceneDeactivatedEvent by invalidating update blocking cache.
        /// </summary>
        /// <param name="evt">The scene deactivated event.</param>
        private void OnSceneDeactivated(ref SceneDeactivatedEvent evt)
        {
            InvalidateUpdateBlockedCache();
        }

        /// <summary>
        /// Handles ScenePausedEvent by invalidating update blocking cache.
        /// </summary>
        /// <param name="evt">The scene paused event.</param>
        private void OnScenePaused(ref ScenePausedEvent evt)
        {
            InvalidateUpdateBlockedCache();
        }

        /// <summary>
        /// Handles SceneResumedEvent by invalidating update blocking cache.
        /// </summary>
        /// <param name="evt">The scene resumed event.</param>
        private void OnSceneResumed(ref SceneResumedEvent evt)
        {
            InvalidateUpdateBlockedCache();
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

            // Unsubscribe from events FIRST (before disposing systems)
            // This prevents memory leaks from event handlers holding references to SystemManager
            EventBus.Unsubscribe<SceneCreatedEvent>(OnSceneCreated);
            EventBus.Unsubscribe<SceneDestroyedEvent>(OnSceneDestroyed);
            EventBus.Unsubscribe<SceneActivatedEvent>(OnSceneActivated);
            EventBus.Unsubscribe<SceneDeactivatedEvent>(OnSceneDeactivated);
            EventBus.Unsubscribe<ScenePausedEvent>(OnScenePaused);
            EventBus.Unsubscribe<SceneResumedEvent>(OnSceneResumed);

            if (_isInitialized)
            {
                _updateSystems.Dispose();
                _sceneSystem?.Cleanup();
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
            _sceneSystem = null!;
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
