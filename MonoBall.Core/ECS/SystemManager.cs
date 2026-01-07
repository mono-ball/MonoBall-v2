using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Arch.Core;
using Arch.System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoBall.Core.Audio;
using MonoBall.Core.Constants;
using MonoBall.Core.Diagnostics;
using MonoBall.Core.Diagnostics.Services;
using MonoBall.Core.ECS.Rendering;
using MonoBall.Core.ECS.Services;
using MonoBall.Core.ECS.Systems;
using MonoBall.Core.ECS.Systems.Animation;
using MonoBall.Core.ECS.Systems.Audio;
using MonoBall.Core.Logging;
using MonoBall.Core.Mods;
using MonoBall.Core.Rendering;
using MonoBall.Core.Resources;
using MonoBall.Core.Scenes;
using MonoBall.Core.Scenes.Components;
using MonoBall.Core.Scenes.Events;
using MonoBall.Core.Scenes.Systems;
using MonoBall.Core.Scripting;
using MonoBall.Core.Scripting.Services;
using MonoBall.Core.TextEffects;
using MonoBall.Core.UI.Windows.Animations;
using Serilog;

namespace MonoBall.Core.ECS;

/// <summary>
///     Manages all ECS systems, their initialization, updates, and rendering.
/// </summary>
public class SystemManager : IDisposable
{
    private readonly IConstantsService _constantsService;
    private readonly Game _game;
    private readonly GraphicsDevice _graphicsDevice;
    private readonly ILogger _logger;
    private readonly IModManager _modManager;
    private readonly List<BaseSystem<World, float>> _registeredUpdateSystems = new();
    private readonly IResourceManager _resourceManager;
    private readonly IShaderParameterValidator _shaderParameterValidator;
    private readonly IShaderService _shaderService;
    private readonly List<IDisposable> _subscriptions = new();
    private readonly World _world;
    private IActiveMapFilterService _activeMapFilterService = null!; // Initialized in Initialize()
    private ActiveMapManagementSystem _activeMapManagementSystem = null!; // Initialized in Initialize()
    private IAudioEngine _audioEngine = null!; // Initialized in Initialize()
    private bool _cachedIsUpdateBlocked;
    private ICameraService _cameraService = null!; // Initialized in Initialize()
    private CameraSystem _cameraSystem = null!; // Initialized in Initialize()
    private CameraViewportSystem _cameraViewportSystem = null!; // Initialized in Initialize()
    private readonly IFlagVariableService _flagVariableService; // Injected via constructor

    // Note: Debug overlay is accessed via _sceneSystems.DebugOverlayService
    private InputBindingService _inputBindingService = null!; // Initialized in Initialize()
    private InputSystem _inputSystem = null!; // Initialized in Initialize()
    private volatile bool _isDisposed; // volatile for thread safety in event handlers

    private bool _isInitialized;
    private bool _isUpdateBlockedCacheValid;
    private MapConnectionSystem _mapConnectionSystem = null!; // Initialized in Initialize()
    private MapLoaderSystem _mapLoaderSystem = null!; // Initialized in Initialize()
    private MapTransitionDetectionSystem _mapTransitionDetectionSystem = null!; // Initialized in Initialize()
    private MovementSystem _movementSystem = null!; // Initialized in Initialize()
    private PlayerSystem _playerSystem = null!; // Initialized in Initialize()
    private SceneInputSystem _sceneInputSystem = null!; // Initialized in Initialize()

    // Note: SceneSystem is accessed via _sceneSystems.SceneSystemInternal (internal use only)
    private ScriptApiProvider? _scriptApiProvider; // Initialized in InitializeCoreServices()
    private ScriptCompilerService? _scriptCompilerService; // Initialized in InitializeCoreServices()
    private readonly IScriptCompilationCache _compilationCache; // Injected via constructor
    private ScriptLifecycleSystem? _scriptLifecycleSystem; // Initialized in CreateGameSystems()
    private ScriptLoaderService? _scriptLoaderService; // Initialized in InitializeCoreServices()

    // Shader systems bundle (created by factory)
    private IShaderSystems? _shaderSystems; // Initialized in CreateShaderSystems()

    // Audio systems bundle (created by factory)
    private IAudioSystems? _audioSystems; // Initialized in CreateAudioSystems()

    // Animation systems bundle (created by factory)
    private IAnimationSystems? _animationSystems; // Initialized in CreateAnimationAndVisibilitySystems()

    // Rendering systems bundle (created by factory)
    private IRenderingSystems? _renderingSystems; // Initialized in CreateRenderSystems()

    // Scene systems bundle (created by factory)
    private ISceneSystems? _sceneSystems; // Initialized in CreateSceneSpecificSystems()

    private SpriteBatch? _spriteBatch;

    private Group<float> _updateSystems = null!; // Initialized in Initialize()
    private IVariableSpriteResolver? _variableSpriteResolver; // Initialized in Initialize()

    // Collision services (initialized in CreateGameSystems)
    private ICollisionLayerCache _collisionLayerCache = null!;
    private IEntityElevationService _entityElevationService = null!;
    private IEntityQueryService _entityQueryService = null!;
    private ITileInteractionCache _tileInteractionCache = null!;
    private ITileInteractionDispatcher _tileInteractionDispatcher = null!;
    private ICollisionService _collisionService = null!;
    private SpatialHashSystem _spatialHashSystem = null!;

    /// <summary>
    ///     Initializes a new instance of the SystemManager.
    /// </summary>
    /// <param name="world">The ECS world.</param>
    /// <param name="graphicsDevice">The graphics device.</param>
    /// <param name="modManager">The mod manager.</param>
    /// <param name="resourceManager">The resource manager service.</param>
    /// <param name="shaderService">The shader service for loading and managing shaders.</param>
    /// <param name="shaderParameterValidator">The shader parameter validator.</param>
    /// <param name="flagVariableService">The flag/variable service for game state.</param>
    /// <param name="scriptCompilationCache">The shared script compilation cache.</param>
    /// <param name="constantsService">The constants service for game configuration.</param>
    /// <param name="game">The game instance for Content and Window access.</param>
    /// <param name="logger">The logger for logging operations.</param>
    public SystemManager(
        World world,
        GraphicsDevice graphicsDevice,
        IModManager modManager,
        IResourceManager resourceManager,
        IShaderService shaderService,
        IShaderParameterValidator shaderParameterValidator,
        IFlagVariableService flagVariableService,
        IScriptCompilationCache scriptCompilationCache,
        IConstantsService constantsService,
        Game game,
        ILogger logger
    )
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _graphicsDevice = graphicsDevice ?? throw new ArgumentNullException(nameof(graphicsDevice));
        _modManager = modManager ?? throw new ArgumentNullException(nameof(modManager));
        _resourceManager =
            resourceManager ?? throw new ArgumentNullException(nameof(resourceManager));
        _shaderService = shaderService ?? throw new ArgumentNullException(nameof(shaderService));
        _shaderParameterValidator =
            shaderParameterValidator
            ?? throw new ArgumentNullException(nameof(shaderParameterValidator));
        _flagVariableService =
            flagVariableService ?? throw new ArgumentNullException(nameof(flagVariableService));
        _compilationCache =
            scriptCompilationCache
            ?? throw new ArgumentNullException(nameof(scriptCompilationCache));
        _constantsService =
            constantsService ?? throw new ArgumentNullException(nameof(constantsService));
        _game = game ?? throw new ArgumentNullException(nameof(game));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    ///     Gets the map loader system.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown if systems are not initialized.</exception>
    public MapLoaderSystem MapLoaderSystem
    {
        get
        {
            if (!_isInitialized)
                throw new InvalidOperationException(
                    "Systems are not initialized. Call Initialize() first."
                );
            return _mapLoaderSystem;
        }
    }

    /// <summary>
    ///     Gets the elevation renderer system.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown if systems are not initialized.</exception>
    public ElevationRendererSystem? ElevationRendererSystem
    {
        get
        {
            if (!_isInitialized)
                throw new InvalidOperationException(
                    "Systems are not initialized. Call Initialize() first."
                );
            return _renderingSystems?.ElevationRendererSystem;
        }
    }

    /// <summary>
    ///     Gets the camera system.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown if systems are not initialized.</exception>
    public CameraSystem CameraSystem
    {
        get
        {
            if (!_isInitialized)
                throw new InvalidOperationException(
                    "Systems are not initialized. Call Initialize() first."
                );
            return _cameraSystem;
        }
    }

    /// <summary>
    ///     Gets the scene systems bundle.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown if systems are not initialized.</exception>
    public ISceneSystems SceneSystems
    {
        get
        {
            if (!_isInitialized)
                throw new InvalidOperationException(
                    "Systems are not initialized. Call Initialize() first."
                );
            return _sceneSystems!;
        }
    }

    /// <summary>
    ///     Gets the loading scene system (for progress updates during initialization).
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown if systems are not initialized.</exception>
    public LoadingSceneSystem? LoadingSceneSystem
    {
        get
        {
            if (!_isInitialized)
                throw new InvalidOperationException(
                    "Systems are not initialized. Call Initialize() first."
                );
            return _sceneSystems?.LoadingSceneSystem as LoadingSceneSystem;
        }
    }

    /// <summary>
    ///     Gets the scene input system.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown if systems are not initialized.</exception>
    public SceneInputSystem SceneInputSystem
    {
        get
        {
            if (!_isInitialized)
                throw new InvalidOperationException(
                    "Systems are not initialized. Call Initialize() first."
                );
            return _sceneInputSystem;
        }
    }

    /// <summary>
    ///     Gets the player system.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown if systems are not initialized.</exception>
    public PlayerSystem PlayerSystem
    {
        get
        {
            if (!_isInitialized)
                throw new InvalidOperationException(
                    "Systems are not initialized. Call Initialize() first."
                );
            return _playerSystem;
        }
    }

    /// <summary>
    ///     Disposes of all systems and resources.
    /// </summary>
    public void Dispose()
    {
        if (_isDisposed)
            return;

        // Set disposed flag FIRST to prevent event handlers from running during disposal
        _isDisposed = true;

        _logger.Debug("Disposing systems");

        // Unsubscribe from events (after setting flag, before disposing systems)
        // This prevents memory leaks from event handlers holding references to SystemManager
        foreach (var subscription in _subscriptions)
            subscription.Dispose();

        if (_isInitialized)
        {
            _updateSystems.Dispose();
            _sceneSystems?.Cleanup();
        }

        // Reset to null after disposal (systems are no longer valid)
        _updateSystems = null!;
        _mapLoaderSystem = null!;
        _mapConnectionSystem = null!;
        _cameraSystem = null!;
        _cameraViewportSystem = null!;
        _playerSystem = null!;
        _inputSystem = null!;
        _movementSystem = null!;
        _mapTransitionDetectionSystem = null!;
        _sceneInputSystem = null!;

        // Dispose shader systems bundle (handles all shader system disposal)
        _shaderSystems?.Dispose();
        _shaderSystems = null;

        // Dispose animation systems bundle (handles all animation system disposal)
        _animationSystems?.Dispose();
        _animationSystems = null;

        // Dispose rendering systems bundle (handles all rendering system disposal)
        _renderingSystems?.Dispose();
        _renderingSystems = null;

        // Dispose scene systems bundle (handles SceneSystem and DebugOverlayService disposal)
        _sceneSystems?.Dispose();
        _sceneSystems = null;

        // Dispose audio systems bundle (handles all audio system disposal)
        _audioSystems?.Dispose();
        _audioSystems = null;

        // Dispose audio engine
        if (_audioEngine is IDisposable audioEngineDisposable)
            audioEngineDisposable.Dispose();
        _audioEngine = null!;

        // Dispose services
        _variableSpriteResolver?.Dispose();
        _variableSpriteResolver = null;

        // Dispose script services
        _scriptLoaderService?.Dispose();
        _scriptLoaderService = null;
        _scriptLifecycleSystem?.Dispose();
        _scriptLifecycleSystem = null;

        // NOTE: Do NOT dispose or clear the compilation cache (_compilationCache)
        // It's a shared singleton that persists across SystemManager instances
        // The cache is disposed by Game.Services when the game exits
    }

    /// <summary>
    ///     Registers an update system with priority-based sorting.
    /// </summary>
    /// <param name="system">The system to register.</param>
    /// <exception cref="ArgumentNullException">Thrown if system is null.</exception>
    /// <exception cref="ArgumentException">Thrown if system does not implement IPrioritizedSystem.</exception>
    private void RegisterUpdateSystem(BaseSystem<World, float> system)
    {
        if (system == null)
            throw new ArgumentNullException(nameof(system));

        if (system is not IPrioritizedSystem prioritizedSystem)
            throw new ArgumentException(
                $"System {system.GetType().Name} does not implement IPrioritizedSystem.",
                nameof(system)
            );

        var priority = prioritizedSystem.Priority;
        if (priority < 0)
            _logger.Warning(
                "System {SystemName} has negative priority {Priority}",
                system.GetType().Name,
                priority
            );

        // Check for duplicate priorities (warn but allow)
        var existingSystem = _registeredUpdateSystems.FirstOrDefault(s =>
            s is IPrioritizedSystem ps && ps.Priority == priority
        );
        if (existingSystem != null)
            _logger.Warning(
                "Duplicate priority {Priority} found: {SystemName} and {ExistingSystemName}",
                priority,
                system.GetType().Name,
                existingSystem.GetType().Name
            );

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
    ///     Initializes all ECS systems. Should be called from LoadContent().
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
            throw new ObjectDisposedException(nameof(SystemManager));

        _spriteBatch = spriteBatch ?? throw new ArgumentNullException(nameof(spriteBatch));

        _logger.Information("Initializing ECS systems");

        // Initialize core services
        InitializeCoreServices();

        // Subscribe to scene events to invalidate update blocking cache
        _subscriptions.Add(EventBus.Subscribe<SceneCreatedEvent>(OnSceneCreated));
        _subscriptions.Add(EventBus.Subscribe<SceneDestroyedEvent>(OnSceneDestroyed));
        _subscriptions.Add(EventBus.Subscribe<SceneActivatedEvent>(OnSceneActivated));
        _subscriptions.Add(EventBus.Subscribe<SceneDeactivatedEvent>(OnSceneDeactivated));
        _subscriptions.Add(EventBus.Subscribe<ScenePausedEvent>(OnScenePaused));
        _subscriptions.Add(EventBus.Subscribe<SceneResumedEvent>(OnSceneResumed));

        // ResourceManager is already available from constructor (no need to get FontService)

        // LoadingSceneSystem no longer needs LoadingSceneRendererSystem - it handles rendering internally

        // Create game systems
        CreateGameSystems();

        // Create render systems
        CreateRenderSystems();

        // Create animation and visibility systems (including PerformanceStatsSystem)
        CreateAnimationAndVisibilitySystems();

        // Create scene-specific systems (must be before audio systems since MapMusicSystem needs ISceneManager)
        CreateSceneSpecificSystems();

        // Create audio systems (after scene systems since MapMusicSystem needs ISceneManager)
        CreateAudioSystems();

        // Finalize initialization
        FinalizeInitialization();
    }

    /// <summary>
    ///     Initializes core services required by systems.
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

        // Create VariableSpriteResolver using injected FlagVariableService
        _variableSpriteResolver = new VariableSpriteResolver(
            _flagVariableService,
            LoggerFactory.CreateLogger<VariableSpriteResolver>()
        );

        _cameraService = new CameraService(_world, LoggerFactory.CreateLogger<CameraService>());

        // Create active map filter service (used by multiple systems for filtering entities by active maps)
        // Must be created before render systems that depend on it
        _activeMapFilterService = new ActiveMapFilterService(_world);

        // Create script services (after mods are loaded)
        _scriptCompilerService = new ScriptCompilerService(
            LoggerFactory.CreateLogger<ScriptCompilerService>()
        );
        _scriptLoaderService = new ScriptLoaderService(
            _scriptCompilerService,
            _modManager.Registry,
            (ModManager)_modManager, // Cast to concrete type as ModManager is internal
            _resourceManager,
            _compilationCache,
            LoggerFactory.CreateLogger<ScriptLoaderService>()
        );

        // Preload all scripts (compiles but doesn't initialize plugin scripts)
        _scriptLoaderService.PreloadAllScripts();

        // Create script API provider (stub for now, will be fully initialized after systems are created)
        _scriptApiProvider = new ScriptApiProvider(
            _world,
            null, // PlayerSystem - will be set later
            null, // MapLoaderSystem - will be set later
            null, // MovementSystem - will be set later
            _cameraService,
            _flagVariableService,
            _modManager.Registry
        );

        // Create audio engine (AudioEngine needs ResourceManager)
        // Use the ResourceManager passed to constructor
        _audioEngine = new AudioEngine(
            _modManager,
            _resourceManager,
            LoggerFactory.CreateLogger<AudioEngine>()
        );
    }

    /// <summary>
    ///     Creates scene input system (needs SceneSystems, so created after scene systems are created).
    /// </summary>
    private void CreateSceneInputSystem()
    {
        // Create scene input system (needs SceneSystems, so created after scene systems)
        _sceneInputSystem = new SceneInputSystem(
            _world,
            _sceneSystems!,
            LoggerFactory.CreateLogger<SceneInputSystem>()
        );
        RegisterUpdateSystem(_sceneInputSystem);
    }

    /// <summary>
    ///     Creates game systems (map loading, player, input, movement, etc.).
    /// </summary>
    private void CreateGameSystems()
    {
        // Create collision services (needed before MapLoaderSystem for loading collision data)
        _collisionLayerCache = new CollisionLayerCache();
        _entityElevationService = new EntityElevationService(_world);
        _entityQueryService = new EntityQueryService(
            _world,
            LoggerFactory.CreateLogger<EntityQueryService>()
        );
        _tileInteractionCache = new TileInteractionCache();
        _tileInteractionDispatcher = new TileInteractionDispatcher(
            _modManager.Registry,
            _scriptLoaderService,
            LoggerFactory.CreateLogger<TileInteractionDispatcher>()
        );

        // Create update systems
        _mapLoaderSystem = new MapLoaderSystem(
            _world,
            _modManager.Registry,
            _resourceManager,
            _flagVariableService,
            _variableSpriteResolver,
            _collisionLayerCache,
            LoggerFactory.CreateLogger<MapLoaderSystem>(),
            _constantsService // Pass ConstantsService for accessing constants
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

        // Create script lifecycle system (runs after map management, before player)
        if (_scriptLoaderService != null && _scriptApiProvider != null)
        {
            _scriptLifecycleSystem = new ScriptLifecycleSystem(
                _world,
                _scriptLoaderService,
                _scriptApiProvider,
                _modManager.Registry,
                LoggerFactory.CreateLogger<ScriptLifecycleSystem>()
            );
            RegisterUpdateSystem(_scriptLifecycleSystem);

            // Create script timer system (runs after script lifecycle)
            var scriptTimerSystem = new ScriptTimerSystem(
                _world,
                LoggerFactory.CreateLogger<ScriptTimerSystem>()
            );
            RegisterUpdateSystem(scriptTimerSystem);
        }

        // Create player system
        _playerSystem = new PlayerSystem(
            _world,
            _cameraService,
            _resourceManager,
            _modManager,
            LoggerFactory.CreateLogger<PlayerSystem>(),
            _constantsService // Pass ConstantsService for accessing constants
        );
        RegisterUpdateSystem(_playerSystem);

        // Create input and movement services
        var inputBuffer = new InputBuffer(
            LoggerFactory.CreateLogger<InputBuffer>(),
            _constantsService.Get<int>("InputBufferMaxSize"),
            _constantsService.Get<float>("InputBufferTimeoutSeconds")
        );
        _inputBindingService = new InputBindingService(
            LoggerFactory.CreateLogger<InputBindingService>()
        );

        // Create interaction system (after player system and input binding service)
        var interactionSystem = new InteractionSystem(
            _world,
            _inputBindingService,
            _modManager.Registry,
            _constantsService,
            LoggerFactory.CreateLogger<InteractionSystem>()
        );
        RegisterUpdateSystem(interactionSystem);
        // Create scene-based input blocker (checks if any scene has BlocksInput=true)
        // Use a lambda to get _sceneSystems lazily since it's created later in CreateSceneSpecificSystems()
        var sceneInputBlocker = new SceneInputBlocker(() => _sceneSystems);

        // Create SpatialHashSystem for entity position queries (implements IEntityPositionService)
        // Must run early to rebuild spatial hash before collision queries
        _spatialHashSystem = new SpatialHashSystem(
            _world,
            _activeMapFilterService,
            LoggerFactory.CreateLogger<SpatialHashSystem>()
        );
        RegisterUpdateSystem(_spatialHashSystem);

        // Create CollisionService with all dependencies
        _collisionService = new CollisionService(
            _world,
            _collisionLayerCache,
            _entityElevationService,
            _spatialHashSystem, // Implements IEntityPositionService
            _entityQueryService,
            _tileInteractionCache,
            _tileInteractionDispatcher,
            LoggerFactory.CreateLogger<CollisionService>()
        );

        // Create input system
        _inputSystem = new InputSystem(
            _world,
            sceneInputBlocker,
            inputBuffer,
            _inputBindingService,
            LoggerFactory.CreateLogger<InputSystem>()
        );
        RegisterUpdateSystem(_inputSystem);

        // Create movement system (handles animation state directly, matching oldmonoball architecture)
        _movementSystem = new MovementSystem(
            _world,
            _collisionService,
            _activeMapFilterService,
            _constantsService,
            _modManager,
            LoggerFactory.CreateLogger<MovementSystem>()
        );
        RegisterUpdateSystem(_movementSystem);

        // Create map transition detection system (detects when player walks across map boundaries)
        // Note: GameEnteredEvent is NOT fired here - it's handled by GameInitializationService
        _mapTransitionDetectionSystem = new MapTransitionDetectionSystem(
            _world,
            _activeMapFilterService,
            LoggerFactory.CreateLogger<MapTransitionDetectionSystem>()
        );
        RegisterUpdateSystem(_mapTransitionDetectionSystem);

        _cameraSystem = new CameraSystem(
            _world,
            _resourceManager,
            LoggerFactory.CreateLogger<CameraSystem>()
        );
        RegisterUpdateSystem(_cameraSystem);

        _cameraViewportSystem = new CameraViewportSystem(
            _world,
            _graphicsDevice,
            _constantsService.Get<int>("ReferenceWidth"),
            _constantsService.Get<int>("ReferenceHeight"),
            LoggerFactory.CreateLogger<CameraViewportSystem>()
        );
        RegisterUpdateSystem(_cameraViewportSystem);

        // Create shader services and systems (after _inputBindingService and _playerSystem are created)
        // ShaderCycleSystem needs these for F4/F5 key handling
        CreateShaderSystems();
    }

    /// <summary>
    ///     Creates shader-related systems using the factory.
    /// </summary>
    private void CreateShaderSystems()
    {
        // Create shared context for system creation
        var context = new SystemCreationContext(
            _world,
            _graphicsDevice,
            _spriteBatch!,
            _modManager,
            _resourceManager,
            _shaderService,
            _shaderParameterValidator,
            _constantsService,
            _game,
            _logger
        );

        // Use factory to create shader systems bundle
        // Pass _inputBindingService and _playerSystem for ShaderCycleSystem (F4/F5 key handling)
        var factory = new ShaderSystemFactory();
        _shaderSystems = factory.Create(
            context,
            _inputBindingService,
            _playerSystem,
            _registeredUpdateSystems
        );
    }

    /// <summary>
    ///     Creates render systems (elevation-based unified renderer) using the factory.
    /// </summary>
    private void CreateRenderSystems()
    {
        // Create shared context for system creation
        var context = new SystemCreationContext(
            _world,
            _graphicsDevice,
            _spriteBatch!,
            _modManager,
            _resourceManager,
            _shaderService,
            _shaderParameterValidator,
            _constantsService,
            _game,
            _logger
        );

        // Use factory to create rendering systems bundle
        var factory = new RenderingSystemFactory();
        _renderingSystems = factory.Create(
            context,
            _cameraService,
            _activeMapFilterService,
            _shaderSystems
        );
    }

    /// <summary>
    ///     Creates animation and visibility systems using the factory.
    /// </summary>
    private void CreateAnimationAndVisibilitySystems()
    {
        // Create shared context for system creation
        var context = new SystemCreationContext(
            _world,
            _graphicsDevice,
            _spriteBatch!,
            _modManager,
            _resourceManager,
            _shaderService,
            _shaderParameterValidator,
            _constantsService,
            _game,
            _logger
        );

        // Use factory to create animation systems bundle
        var factory = new AnimationSystemFactory();
        _animationSystems = factory.Create(
            context,
            _flagVariableService,
            () => _sceneSystems, // Function to get SceneSystems (null-safe)
            _registeredUpdateSystems
        );
    }

    /// <summary>
    ///     Creates audio systems using the factory.
    /// </summary>
    private void CreateAudioSystems()
    {
        // Create shared context for system creation
        var context = new SystemCreationContext(
            _world,
            _graphicsDevice,
            _spriteBatch!,
            _modManager,
            _resourceManager,
            _shaderService,
            _shaderParameterValidator,
            _constantsService,
            _game,
            _logger
        );

        // Use factory to create audio systems bundle
        var factory = new AudioSystemFactory();
        _audioSystems = factory.Create(
            context,
            _sceneSystems?.SceneManager!,
            _audioEngine,
            _registeredUpdateSystems
        );
    }

    /// <summary>
    ///     Creates scene-specific systems (game scene, debug bar, popups) using the factory.
    /// </summary>
    private void CreateSceneSpecificSystems()
    {
        if (_spriteBatch == null)
            throw new InvalidOperationException(
                "SpriteBatch must be initialized before creating scene-specific systems."
            );

        // Create context with all dependencies needed by scene factory
        var context = new SceneSystemCreationContext(
            _world,
            _graphicsDevice,
            _spriteBatch,
            _game,
            _modManager,
            _resourceManager,
            _constantsService,
            _inputBindingService,
            _cameraService,
            _flagVariableService,
            _shaderSystems,
            _renderingSystems,
            _animationSystems,
            _playerSystem,
            _shaderService,
            _logger
        );

        // Use factory to create scene systems bundle
        var factory = new SceneSystemFactory();
        _sceneSystems = factory.Create(context, _registeredUpdateSystems);

        // Create scene input system (needs SceneManager, so created after factory)
        CreateSceneInputSystem();

        // Create map popup system (handles popup lifecycle based on map transitions)
        // Note: This is an update system, not a scene system - it triggers popups
        if (_sceneSystems.SceneManager != null)
        {
            var mapPopupSystem = new MapPopupSystem(
                _world,
                _sceneSystems.SceneManager,
                _modManager,
                LoggerFactory.CreateLogger<MapPopupSystem>(),
                _constantsService
            );
            RegisterUpdateSystem(mapPopupSystem);

            // Create debug bar toggle system
            var debugBarToggleSystem = new DebugBarToggleSystem(
                _world,
                _sceneSystems.SceneManager,
                _inputBindingService,
                LoggerFactory.CreateLogger<DebugBarToggleSystem>()
            );
            RegisterUpdateSystem(debugBarToggleSystem);
        }

        // Note: ShaderCycleSystem is now created by ShaderSystemFactory in CreateShaderSystems()
    }

    /// <summary>
    ///     Finalizes system initialization by sorting systems and creating the update Group.
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

        // Update script API provider with actual system references
        if (_scriptApiProvider != null && _scriptLoaderService != null)
        {
            // Update API provider with actual system references
            _scriptApiProvider.UpdateSystems(_playerSystem, _mapLoaderSystem, _movementSystem);

            // Update shader system references from bundle
            _scriptApiProvider.UpdateShaderSystems(
                _shaderSystems?.ShaderManager,
                _shaderSystems?.TransitionSystem,
                _shaderSystems?.MultiParameterAnimation,
                _shaderSystems?.AnimationChain,
                _shaderSystems?.PresetService
            );

            // Initialize plugin scripts (after all systems are ready)
            _scriptLoaderService.InitializePluginScripts(
                _scriptApiProvider,
                _world,
                LoggerFactory.CreateLogger<ScriptLoaderService>()
            );
        }

        _isInitialized = true;
        _logger.Information("ECS systems initialized successfully");
    }

    /// <summary>
    ///     Updates all ECS systems. Should be called from Game.Update().
    /// </summary>
    /// <param name="gameTime">The game time.</param>
    public void Update(GameTime gameTime)
    {
        if (!_isInitialized || _isDisposed)
            return;

        var deltaTime = (float)gameTime.ElapsedGameTime.TotalSeconds;

        // Check time control for pause/step/scale
        var timeControl = _sceneSystems?.DebugOverlayService?.TimeControl;
        if (timeControl != null)
        {
            // Handle pause state - set deltaTime to 0 so systems still run but gameplay freezes
            if (timeControl.IsPaused)
            {
                // Check for step frames
                if (timeControl.ConsumeStepFrame())
                {
                    // Allow this frame to proceed with normal deltaTime (stepping)
                }
                else
                {
                    // Paused - freeze gameplay by zeroing deltaTime
                    deltaTime = 0f;
                }
            }
            else
            {
                // Apply time scale only when not paused
                deltaTime *= timeControl.TimeScale;
            }
        }

        // Check if profiling is enabled (any listeners subscribed to timing hooks)
        var profilingEnabled = SystemTimingHook.HasSubscribers;

        if (profilingEnabled)
        {
            // Profile each system individually
            UpdateWithProfiling(deltaTime);
        }
        else
        {
            // Normal update: run all systems via Group (slightly faster, no profiling overhead)
            _updateSystems.BeforeUpdate(in deltaTime);
            _updateSystems.Update(in deltaTime);
            _updateSystems.AfterUpdate(in deltaTime);
        }

        // Update audio engine
        _audioEngine.Update(deltaTime);
    }

    /// <summary>
    ///     Updates all systems with per-system timing for profiling.
    ///     Maintains the same lifecycle order as Group: all BeforeUpdate, then all Update, then all AfterUpdate.
    /// </summary>
    private void UpdateWithProfiling(float deltaTime)
    {
        var stopwatch = new Stopwatch();

        // Phase 1: Call BeforeUpdate on all systems (matches Group behavior)
        foreach (var system in _registeredUpdateSystems)
        {
            system.BeforeUpdate(in deltaTime);
        }

        // Phase 2: Call Update on all systems with timing
        foreach (var system in _registeredUpdateSystems)
        {
            stopwatch.Restart();
            system.Update(in deltaTime);
            stopwatch.Stop();
            SystemProfiler.RecordTiming(system.GetType().Name, stopwatch.Elapsed.TotalMilliseconds);
        }

        // Phase 3: Call AfterUpdate on all systems (matches Group behavior)
        foreach (var system in _registeredUpdateSystems)
        {
            system.AfterUpdate(in deltaTime);
        }
    }

    /// <summary>
    ///     Checks if any active scene has BlocksUpdate=true.
    ///     Uses cached result to avoid querying scenes every frame.
    /// </summary>
    /// <returns>True if updates are blocked, false otherwise.</returns>
    private bool IsUpdateBlocked()
    {
        // Return cached value if valid
        if (_isUpdateBlockedCacheValid)
            return _cachedIsUpdateBlocked;

        // Recalculate and cache
        var isBlocked = false;
        _sceneSystems?.IterateScenes(
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
    ///     Invalidates the update blocking cache. Called when scene state changes.
    /// </summary>
    private void InvalidateUpdateBlockedCache()
    {
        _isUpdateBlockedCacheValid = false;
    }

    /// <summary>
    ///     Renders all scenes. Should be called from Game.Draw().
    /// </summary>
    /// <param name="gameTime">The game time.</param>
    public void Render(GameTime gameTime)
    {
        if (!_isInitialized || _isDisposed)
            return;

        // SceneSystems coordinates rendering for all scene-specific systems
        _sceneSystems?.Render(gameTime);
    }

    /// <summary>
    ///     Initializes the player entity using PlayerSystem.
    /// </summary>
    /// <param name="cameraEntity">Optional camera entity to use for spawn position.</param>
    public void InitializePlayer(Entity? cameraEntity = null)
    {
        _playerSystem.InitializePlayer(cameraEntity: cameraEntity);
    }

    /// <summary>
    ///     Gets the player entity from PlayerSystem.
    /// </summary>
    /// <returns>The player entity, or null if not created yet.</returns>
    public Entity? GetPlayerEntity()
    {
        return _playerSystem.GetPlayerEntity();
    }

    /// <summary>
    ///     Loads a map using MapLoaderSystem.
    /// </summary>
    /// <param name="mapId">The map ID to load.</param>
    public void LoadMap(string mapId)
    {
        _mapLoaderSystem.LoadMap(mapId);
    }

    /// <summary>
    ///     Creates the game scene using SceneManager.
    /// </summary>
    public void CreateGameScene()
    {
        var sceneComponent = new SceneComponent
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
            BackgroundColor = Color.Black,
        };

        var gameSceneComponent = new GameSceneComponent();

        var sceneManager =
            _sceneSystems?.SceneManager
            ?? throw new InvalidOperationException("SceneManager is not available");
        var gameSceneEntity = sceneManager.CreateScene(sceneComponent, gameSceneComponent);
        _logger.Information("Game scene created: {EntityId}", gameSceneEntity.Id);
    }

    /// <summary>
    ///     Handles SceneCreatedEvent by invalidating update blocking cache.
    /// </summary>
    /// <param name="evt">The scene created event.</param>
    private void OnSceneCreated(ref SceneCreatedEvent evt)
    {
        if (_isDisposed)
            return; // Guard against events during/after disposal
        InvalidateUpdateBlockedCache();
    }

    /// <summary>
    ///     Handles SceneDestroyedEvent by invalidating update blocking cache.
    /// </summary>
    /// <param name="evt">The scene destroyed event.</param>
    private void OnSceneDestroyed(ref SceneDestroyedEvent evt)
    {
        if (_isDisposed)
            return; // Guard against events during/after disposal
        InvalidateUpdateBlockedCache();
    }

    /// <summary>
    ///     Handles SceneActivatedEvent by invalidating update blocking cache.
    /// </summary>
    /// <param name="evt">The scene activated event.</param>
    private void OnSceneActivated(ref SceneActivatedEvent evt)
    {
        if (_isDisposed)
            return; // Guard against events during/after disposal
        InvalidateUpdateBlockedCache();
    }

    /// <summary>
    ///     Handles SceneDeactivatedEvent by invalidating update blocking cache.
    /// </summary>
    /// <param name="evt">The scene deactivated event.</param>
    private void OnSceneDeactivated(ref SceneDeactivatedEvent evt)
    {
        if (_isDisposed)
            return; // Guard against events during/after disposal
        InvalidateUpdateBlockedCache();
    }

    /// <summary>
    ///     Handles ScenePausedEvent by invalidating update blocking cache.
    /// </summary>
    /// <param name="evt">The scene paused event.</param>
    private void OnScenePaused(ref ScenePausedEvent evt)
    {
        if (_isDisposed)
            return; // Guard against events during/after disposal
        InvalidateUpdateBlockedCache();
    }

    /// <summary>
    ///     Handles SceneResumedEvent by invalidating update blocking cache.
    /// </summary>
    /// <param name="evt">The scene resumed event.</param>
    private void OnSceneResumed(ref SceneResumedEvent evt)
    {
        if (_isDisposed)
            return; // Guard against events during/after disposal
        InvalidateUpdateBlockedCache();
    }
}
