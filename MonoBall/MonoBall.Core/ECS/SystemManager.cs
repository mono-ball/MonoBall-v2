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
using MonoBall.Core.ECS.Services;
using MonoBall.Core.ECS.Systems;
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
    private readonly Game _game;
    private readonly GraphicsDevice _graphicsDevice;
    private readonly ILogger _logger;
    private readonly IModManager _modManager;
    private readonly List<BaseSystem<World, float>> _registeredUpdateSystems = new();
    private readonly IResourceManager _resourceManager;
    private readonly List<IDisposable> _subscriptions = new();
    private readonly World _world;
    private IActiveMapFilterService _activeMapFilterService = null!; // Initialized in Initialize()
    private ActiveMapManagementSystem _activeMapManagementSystem = null!; // Initialized in Initialize()
    private AmbientSoundSystem _ambientSoundSystem = null!; // Initialized in Initialize()
    private AnimatedTileSystem _animatedTileSystem = null!; // Initialized in Initialize()
    private IAudioEngine _audioEngine = null!; // Initialized in Initialize()
    private AudioVolumeSystem _audioVolumeSystem = null!; // Initialized in Initialize()
    private bool _cachedIsUpdateBlocked;
    private ICameraService _cameraService = null!; // Initialized in Initialize()
    private CameraSystem _cameraSystem = null!; // Initialized in Initialize()
    private CameraViewportSystem _cameraViewportSystem = null!; // Initialized in Initialize()
    private IFlagVariableService _flagVariableService = null!; // Initialized in InitializeCoreServices()

    // Scene-specific systems (owned by SceneSystem, not registered separately)
    private IDebugOverlayService? _debugOverlayService; // Initialized in CreateSceneSystems()
    private InputBindingService _inputBindingService = null!; // Initialized in Initialize()
    private InputSystem _inputSystem = null!; // Initialized in Initialize()
    private bool _isDisposed;

    private bool _isInitialized;
    private bool _isUpdateBlockedCacheValid;
    private MapBorderRendererSystem _mapBorderRendererSystem = null!; // Initialized in Initialize()
    private MapConnectionSystem _mapConnectionSystem = null!; // Initialized in Initialize()
    private MapLoaderSystem _mapLoaderSystem = null!; // Initialized in Initialize()
    private MapMusicSystem _mapMusicSystem = null!; // Initialized in Initialize()
    private MapRendererSystem _mapRendererSystem = null!; // Initialized in Initialize()
    private MapTransitionDetectionSystem _mapTransitionDetectionSystem = null!; // Initialized in Initialize()
    private MovementSystem _movementSystem = null!; // Initialized in Initialize()
    private MusicPlaybackSystem _musicPlaybackSystem = null!; // Initialized in Initialize()
    private PlayerSystem _playerSystem = null!; // Initialized in Initialize()
    private RenderTargetManager? _renderTargetManager; // Initialized in Initialize(), may be null
    private SceneInputSystem _sceneInputSystem = null!; // Initialized in Initialize()
    private SceneSystem _sceneSystem = null!; // Initialized in Initialize()
    private ScriptApiProvider? _scriptApiProvider; // Initialized in InitializeCoreServices()
    private ScriptCompilerService? _scriptCompilerService; // Initialized in InitializeCoreServices()
    private ScriptLifecycleSystem? _scriptLifecycleSystem; // Initialized in CreateGameSystems()
    private ScriptLoaderService? _scriptLoaderService; // Initialized in InitializeCoreServices()
    private ShaderAnimationChainSystem? _shaderChainSystem; // Initialized in Initialize(), may be null
    private ShaderManagerSystem? _shaderManagerSystem; // Initialized in Initialize(), may be null
    private ShaderMultiParameterAnimationSystem? _shaderMultiAnimSystem; // Initialized in Initialize(), may be null
    private ShaderParameterAnimationSystem? _shaderParameterAnimationSystem; // Initialized in Initialize(), may be null
    private IShaderPresetService? _shaderPresetService; // Initialized in Initialize(), may be null
    private ShaderRegionDetectionSystem? _shaderRegionSystem; // Initialized in Initialize(), may be null
    private ShaderRendererSystem? _shaderRendererSystem; // Initialized in Initialize(), may be null
    private ShaderTemplateSystem? _shaderTemplateSystem; // Initialized in Initialize(), may be null
    private ShaderTransitionSystem? _shaderTransitionSystem; // Initialized in Initialize(), may be null
    private SoundEffectSystem _soundEffectSystem = null!; // Initialized in Initialize()
    private SpriteAnimationSystem _spriteAnimationSystem = null!; // Initialized in Initialize()
    private SpriteBatch? _spriteBatch;
    private SpriteRendererSystem _spriteRendererSystem = null!; // Initialized in Initialize()
    private SpriteSheetSystem _spriteSheetSystem = null!; // Initialized in Initialize()

    private Group<float> _updateSystems = null!; // Initialized in Initialize()
    private IVariableSpriteResolver? _variableSpriteResolver; // Initialized in Initialize()
    private VisibilityFlagSystem _visibilityFlagSystem = null!; // Initialized in Initialize()

    /// <summary>
    ///     Initializes a new instance of the SystemManager.
    /// </summary>
    /// <param name="world">The ECS world.</param>
    /// <param name="graphicsDevice">The graphics device.</param>
    /// <param name="modManager">The mod manager.</param>
    /// <param name="resourceManager">The resource manager service.</param>
    /// <param name="game">The game instance for accessing services.</param>
    /// <param name="logger">The logger for logging operations.</param>
    public SystemManager(
        World world,
        GraphicsDevice graphicsDevice,
        IModManager modManager,
        IResourceManager resourceManager,
        Game game,
        ILogger logger
    )
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _graphicsDevice = graphicsDevice ?? throw new ArgumentNullException(nameof(graphicsDevice));
        _modManager = modManager ?? throw new ArgumentNullException(nameof(modManager));
        _resourceManager =
            resourceManager ?? throw new ArgumentNullException(nameof(resourceManager));
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
    ///     Gets the map renderer system.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown if systems are not initialized.</exception>
    public MapRendererSystem MapRendererSystem
    {
        get
        {
            if (!_isInitialized)
                throw new InvalidOperationException(
                    "Systems are not initialized. Call Initialize() first."
                );
            return _mapRendererSystem;
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
    ///     Gets the scene system.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown if systems are not initialized.</exception>
    public SceneSystem SceneSystem
    {
        get
        {
            if (!_isInitialized)
                throw new InvalidOperationException(
                    "Systems are not initialized. Call Initialize() first."
                );
            return _sceneSystem;
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
            return _sceneSystem.LoadingSceneSystem;
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

        _logger.Debug("Disposing systems");

        // Unsubscribe from events FIRST (before disposing systems)
        // This prevents memory leaks from event handlers holding references to SystemManager
        foreach (var subscription in _subscriptions)
            subscription.Dispose();

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
        _mapTransitionDetectionSystem = null!;
        _sceneSystem = null!;
        _sceneInputSystem = null!;

        // Dispose shader systems
        _shaderParameterAnimationSystem?.Dispose();
        _shaderParameterAnimationSystem = null;
        _shaderTransitionSystem?.Dispose();
        _shaderTransitionSystem = null;
        _shaderMultiAnimSystem?.Dispose();
        _shaderMultiAnimSystem = null;
        _shaderChainSystem?.Dispose();
        _shaderChainSystem = null;
        _shaderRegionSystem?.Dispose();
        _shaderRegionSystem = null;
        _shaderPresetService = null; // Service doesn't implement IDisposable
        _renderTargetManager?.Dispose();
        _renderTargetManager = null;
        // ShaderManagerSystem doesn't need disposal (no managed resources)

        // Dispose debug overlay service
        _debugOverlayService?.Dispose();
        _debugOverlayService = null;

        // Dispose audio systems
        _mapMusicSystem?.Dispose();
        _mapMusicSystem = null!;
        _musicPlaybackSystem?.Dispose();
        _musicPlaybackSystem = null!;
        _ambientSoundSystem?.Dispose();
        _ambientSoundSystem = null!;
        _audioVolumeSystem?.Dispose();
        _audioVolumeSystem = null!;
        // SoundEffectSystem doesn't need disposal (no event subscriptions)

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

        _isDisposed = true;
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

        // Get ConstantsService from Game.Services (needed for scene systems)
        // Use helper method for consistency
        var constantsService = GetConstantsService();

        // LoadingSceneSystem no longer needs LoadingSceneRendererSystem - it handles rendering internally

        // Create shader services and systems
        var shaderService = _game.Services.GetService<IShaderService>();
        var shaderParameterValidator = _game.Services.GetService<IShaderParameterValidator>();

        if (shaderService != null && shaderParameterValidator != null)
        {
            _renderTargetManager = new RenderTargetManager(
                _graphicsDevice,
                LoggerFactory.CreateLogger<RenderTargetManager>()
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
            _shaderTemplateSystem = new ShaderTemplateSystem(
                _world,
                _modManager,
                LoggerFactory.CreateLogger<ShaderTemplateSystem>()
            );
        }

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

        // Get FlagVariableService from Game.Services
        var flagVariableService = _game.Services.GetService<IFlagVariableService>();
        if (flagVariableService == null)
            throw new InvalidOperationException(
                "IFlagVariableService is not available in Game.Services. "
                    + "Ensure GameServices.Initialize() was called."
            );

        // Assign to field for use by other systems
        _flagVariableService = flagVariableService;

        // Create VariableSpriteResolver
        _variableSpriteResolver = new VariableSpriteResolver(
            _flagVariableService,
            LoggerFactory.CreateLogger<VariableSpriteResolver>()
        );

        // Get ResourceManager from Game.Services (should already be registered)
        var resourceManager = _game.Services.GetService<IResourceManager>();
        if (resourceManager == null)
            throw new InvalidOperationException(
                "IResourceManager is not available in Game.Services. "
                    + "Ensure ResourceManager was created and registered before SystemManager initialization."
            );

        // Note: ResourceManager is passed via constructor, but we also need it from Game.Services
        // for systems that access it directly. The constructor parameter is the primary source.

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
            flagVariableService,
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
    ///     Creates scene input system (needs SceneSystem, so created after SceneSystem is created).
    /// </summary>
    private void CreateSceneInputSystem()
    {
        // Create scene input system (needs SceneSystem, so created after SceneSystem)
        _sceneInputSystem = new SceneInputSystem(
            _world,
            _sceneSystem,
            LoggerFactory.CreateLogger<SceneInputSystem>()
        );
        RegisterUpdateSystem(_sceneInputSystem);
    }

    /// <summary>
    ///     Gets the ConstantsService from Game.Services, throwing if not available.
    /// </summary>
    /// <returns>The ConstantsService instance.</returns>
    /// <exception cref="InvalidOperationException">Thrown if ConstantsService is not available.</exception>
    private ConstantsService GetConstantsService()
    {
        var service = _game.Services.GetService<ConstantsService>();
        if (service == null)
            throw new InvalidOperationException(
                "ConstantsService is not available in Game.Services. "
                    + "Ensure ConstantsService was registered after mods were loaded."
            );
        return service;
    }

    /// <summary>
    ///     Creates game systems (map loading, player, input, movement, etc.).
    /// </summary>
    private void CreateGameSystems()
    {
        // Get FlagVariableService from Game.Services
        var flagVariableService = _game.Services.GetService<IFlagVariableService>();
        if (flagVariableService == null)
            throw new InvalidOperationException(
                "IFlagVariableService is not available in Game.Services. "
                    + "Ensure GameServices.Initialize() was called."
            );

        // Create shader services and systems
        CreateShaderSystems();

        // Get ConstantsService from Game.Services (needed for multiple systems)
        var constantsService = GetConstantsService();

        // Create update systems
        _mapLoaderSystem = new MapLoaderSystem(
            _world,
            _modManager.Registry,
            _resourceManager,
            _flagVariableService,
            _variableSpriteResolver,
            LoggerFactory.CreateLogger<MapLoaderSystem>(),
            constantsService // Pass ConstantsService for accessing constants
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
            constantsService // Pass ConstantsService for accessing constants
        );
        RegisterUpdateSystem(_playerSystem);

        // Create input and movement services
        var inputBuffer = new InputBuffer(
            LoggerFactory.CreateLogger<InputBuffer>(),
            constantsService.Get<int>("InputBufferMaxSize"),
            constantsService.Get<float>("InputBufferTimeoutSeconds")
        );
        _inputBindingService = new InputBindingService(
            LoggerFactory.CreateLogger<InputBindingService>()
        );

        // Create interaction system (after player system and input binding service)
        var interactionSystem = new InteractionSystem(
            _world,
            _inputBindingService,
            _modManager.Registry,
            constantsService,
            LoggerFactory.CreateLogger<InteractionSystem>()
        );
        RegisterUpdateSystem(interactionSystem);
        // Create scene-based input blocker (checks if any scene has BlocksInput=true)
        // Use a lambda to get _sceneSystem lazily since it's created later in CreateSceneSpecificSystems()
        var sceneInputBlocker = new SceneInputBlocker(() => _sceneSystem);
        var nullCollisionService = new NullCollisionService();

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
            nullCollisionService,
            _activeMapFilterService,
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

        var constantsServiceForCamera = GetConstantsService();
        _cameraViewportSystem = new CameraViewportSystem(
            _world,
            _graphicsDevice,
            constantsServiceForCamera.Get<int>("ReferenceWidth"),
            constantsServiceForCamera.Get<int>("ReferenceHeight"),
            LoggerFactory.CreateLogger<CameraViewportSystem>()
        );
        RegisterUpdateSystem(_cameraViewportSystem);
    }

    /// <summary>
    ///     Creates shader-related systems.
    /// </summary>
    private void CreateShaderSystems()
    {
        // Create shader services and systems
        var shaderService = _game.Services.GetService<IShaderService>();
        var shaderParameterValidator = _game.Services.GetService<IShaderParameterValidator>();

        if (shaderService != null && shaderParameterValidator != null)
        {
            _renderTargetManager = new RenderTargetManager(
                _graphicsDevice,
                LoggerFactory.CreateLogger<RenderTargetManager>()
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
            _shaderTemplateSystem = new ShaderTemplateSystem(
                _world,
                _modManager,
                LoggerFactory.CreateLogger<ShaderTemplateSystem>()
            );

            // Create shader transition system
            _shaderTransitionSystem = new ShaderTransitionSystem(
                _world,
                _shaderManagerSystem,
                LoggerFactory.CreateLogger<ShaderTransitionSystem>()
            );
            RegisterUpdateSystem(_shaderTransitionSystem);

            // Create multi-parameter animation system
            _shaderMultiAnimSystem = new ShaderMultiParameterAnimationSystem(
                _world,
                _shaderManagerSystem,
                LoggerFactory.CreateLogger<ShaderMultiParameterAnimationSystem>()
            );
            RegisterUpdateSystem(_shaderMultiAnimSystem);

            // Create animation chain system
            _shaderChainSystem = new ShaderAnimationChainSystem(
                _world,
                _shaderManagerSystem,
                LoggerFactory.CreateLogger<ShaderAnimationChainSystem>()
            );
            RegisterUpdateSystem(_shaderChainSystem);

            // Create shader region detection system
            _shaderRegionSystem = new ShaderRegionDetectionSystem(
                _world,
                _shaderTransitionSystem,
                LoggerFactory.CreateLogger<ShaderRegionDetectionSystem>()
            );
            RegisterUpdateSystem(_shaderRegionSystem);

            // Create shader preset service
            _shaderPresetService = new ShaderPresetService(
                _modManager.Registry,
                LoggerFactory.CreateLogger<ShaderPresetService>()
            );
        }
    }

    /// <summary>
    ///     Creates render systems (map, sprite, border renderers).
    /// </summary>
    private void CreateRenderSystems()
    {
        // Get shader service (needed for sprite renderer)
        var shaderService = _game.Services.GetService<IShaderService>();

        // Create render systems
        _mapRendererSystem = new MapRendererSystem(
            _world,
            _graphicsDevice,
            _resourceManager,
            _cameraService,
            LoggerFactory.CreateLogger<MapRendererSystem>(),
            _shaderManagerSystem,
            _shaderRendererSystem,
            _renderTargetManager
        );
        if (_spriteBatch == null)
            throw new InvalidOperationException(
                "SpriteBatch is null. Ensure Initialize() was called with a valid SpriteBatch."
            );
        _mapRendererSystem.SetSpriteBatch(_spriteBatch);
        _mapBorderRendererSystem = new MapBorderRendererSystem(
            _world,
            _graphicsDevice,
            _resourceManager,
            _cameraService,
            _activeMapFilterService,
            LoggerFactory.CreateLogger<MapBorderRendererSystem>()
        );
        _mapBorderRendererSystem.SetSpriteBatch(_spriteBatch);
        _spriteRendererSystem = new SpriteRendererSystem(
            _world,
            _graphicsDevice,
            _resourceManager,
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
    ///     Creates animation and visibility systems (animated tiles, sprite animation, sprite sheets, visibility flags,
    ///     performance stats).
    /// </summary>
    private void CreateAnimationAndVisibilitySystems()
    {
        // Create animated tile system
        _animatedTileSystem = new AnimatedTileSystem(
            _world,
            _resourceManager,
            LoggerFactory.CreateLogger<AnimatedTileSystem>()
        );
        RegisterUpdateSystem(_animatedTileSystem);

        // Create sprite animation system
        _spriteAnimationSystem = new SpriteAnimationSystem(
            _world,
            _resourceManager,
            LoggerFactory.CreateLogger<SpriteAnimationSystem>()
        );
        RegisterUpdateSystem(_spriteAnimationSystem);

        // Create sprite sheet system
        _spriteSheetSystem = new SpriteSheetSystem(
            _world,
            _resourceManager,
            LoggerFactory.CreateLogger<SpriteSheetSystem>()
        );
        RegisterUpdateSystem(_spriteSheetSystem);

        // Create window animation system
        // Pass function to get SceneSystem (may be null initially, but will be set before first update)
        var windowAnimationSystem = new WindowAnimationSystem(
            _world,
            LoggerFactory.CreateLogger<WindowAnimationSystem>(),
            () => _sceneSystem // Function to get SceneSystem (null-safe)
        );
        RegisterUpdateSystem(windowAnimationSystem);

        // Create visibility flag system
        var flagVariableService = _game.Services.GetService<IFlagVariableService>();
        if (flagVariableService == null)
            throw new InvalidOperationException(
                "IFlagVariableService is not available in Game.Services. "
                    + "Ensure GameServices.Initialize() was called."
            );
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
    ///     Creates audio systems.
    /// </summary>
    private void CreateAudioSystems()
    {
        // Create audio systems
        _mapMusicSystem = new MapMusicSystem(
            _world,
            _sceneSystem, // Pass SceneSystem as ISceneManager
            LoggerFactory.CreateLogger<MapMusicSystem>()
        );
        RegisterUpdateSystem(_mapMusicSystem);

        _musicPlaybackSystem = new MusicPlaybackSystem(
            _world,
            _modManager.Registry,
            _audioEngine,
            LoggerFactory.CreateLogger<MusicPlaybackSystem>()
        );
        RegisterUpdateSystem(_musicPlaybackSystem);

        _soundEffectSystem = new SoundEffectSystem(
            _world,
            _modManager.Registry,
            _audioEngine,
            LoggerFactory.CreateLogger<SoundEffectSystem>()
        );
        RegisterUpdateSystem(_soundEffectSystem);

        _ambientSoundSystem = new AmbientSoundSystem(
            _world,
            _modManager.Registry,
            _audioEngine,
            LoggerFactory.CreateLogger<AmbientSoundSystem>()
        );
        RegisterUpdateSystem(_ambientSoundSystem);

        _audioVolumeSystem = new AudioVolumeSystem(
            _world,
            _audioEngine,
            LoggerFactory.CreateLogger<AudioVolumeSystem>()
        );
        RegisterUpdateSystem(_audioVolumeSystem);
    }

    /// <summary>
    ///     Creates scene-specific systems (game scene, debug bar, popups).
    /// </summary>
    private void CreateSceneSpecificSystems()
    {
        // Get ConstantsService from Game.Services (needed for scene systems)
        var constantsService = GetConstantsService();

        // ResourceManager is already available from constructor (no need to get FontService)

        // Get performance stats system (needed for debug bar)
        var performanceStatsSystem = _registeredUpdateSystems
            .OfType<PerformanceStatsSystem>()
            .FirstOrDefault();
        if (performanceStatsSystem == null)
            throw new InvalidOperationException(
                "PerformanceStatsSystem not found. Ensure it was registered before calling CreateSceneSpecificSystems."
            );

        // Create scene-specific systems first
        if (_spriteBatch == null)
            throw new InvalidOperationException(
                "SpriteBatch must be initialized before creating scene-specific systems."
            );
        var loadingSceneSystem = new LoadingSceneSystem(
            _world,
            _graphicsDevice,
            _spriteBatch,
            _game,
            LoggerFactory.CreateLogger<LoadingSceneSystem>()
        );

        var gameSceneSystem = new GameSceneSystem(
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

        var debugBarSceneSystem = new DebugBarSceneSystem(
            _world,
            _graphicsDevice,
            _spriteBatch,
            _resourceManager,
            performanceStatsSystem,
            LoggerFactory.CreateLogger<DebugBarSceneSystem>()
        );

        // Create SceneSystem with scene-specific systems (except MapPopupSceneSystem which needs SceneSystem)
        _sceneSystem = new SceneSystem(
            _world,
            LoggerFactory.CreateLogger<SceneSystem>(),
            _graphicsDevice,
            _shaderManagerSystem,
            gameSceneSystem, // ISceneSystem
            loadingSceneSystem, // ISceneSystem
            debugBarSceneSystem // MapPopupSceneSystem will be created after SceneSystem
        );

        // Create MapPopupSceneSystem (needs ISceneManager, which SceneSystem implements)
        var mapPopupSceneSystem = new MapPopupSceneSystem(
            _world,
            _sceneSystem, // Pass SceneSystem as ISceneManager
            _graphicsDevice,
            _spriteBatch,
            _modManager,
            LoggerFactory.CreateLogger<MapPopupSceneSystem>(),
            constantsService, // Pass ConstantsService for accessing constants
            _resourceManager // Pass ResourceManager for loading textures and fonts
        );

        // Register MapPopupSceneSystem with SceneSystem (as ISceneSystem)
        _sceneSystem.SetMapPopupSceneSystem(mapPopupSceneSystem);

        // Create TextEffectCalculator for animated text effects
        var textEffectCalculator = new TextEffectCalculator();

        // Create MessageBoxSceneSystem (needs ISceneManager, which SceneSystem implements)
        var messageBoxSceneSystem = new MessageBoxSceneSystem(
            _world,
            _sceneSystem, // Pass SceneSystem as ISceneManager
            _modManager,
            _inputBindingService,
            _flagVariableService,
            _cameraService, // Pass CameraService for camera queries
            _graphicsDevice,
            _spriteBatch,
            LoggerFactory.CreateLogger<MessageBoxSceneSystem>(),
            constantsService, // Pass ConstantsService for accessing constants
            textEffectCalculator, // Pass TextEffectCalculator for text effects
            _resourceManager // Pass ResourceManager for loading textures and fonts
        );

        // Register MessageBoxSceneSystem with SceneSystem (as ISceneSystem)
        _sceneSystem.SetMessageBoxSceneSystem(messageBoxSceneSystem);

        // Register MessageBoxSceneSystem with update systems (it implements IPrioritizedSystem)
        RegisterUpdateSystem(messageBoxSceneSystem);

        // Only register SceneSystem (not scene-specific systems - they're owned by SceneSystem)
        RegisterUpdateSystem(_sceneSystem);

        // Create scene input system (needs SceneSystem, so created after it)
        CreateSceneInputSystem();

        // Create map popup system (handles popup lifecycle based on map transitions)
        var mapPopupSystem = new MapPopupSystem(
            _world,
            _sceneSystem, // Pass SceneSystem as ISceneManager
            _modManager,
            LoggerFactory.CreateLogger<MapPopupSystem>(),
            constantsService // Pass ConstantsService for accessing constants
        );
        RegisterUpdateSystem(mapPopupSystem);

        // Create debug bar toggle system
        var debugBarToggleSystem = new DebugBarToggleSystem(
            _world,
            _sceneSystem,
            _inputBindingService,
            LoggerFactory.CreateLogger<DebugBarToggleSystem>()
        );
        RegisterUpdateSystem(debugBarToggleSystem);

        // Create ImGui debug overlay service and scene system
        _debugOverlayService = new DebugOverlayService(_world);
        _debugOverlayService.Initialize(_game, _resourceManager, _sceneSystem);

        var debugMenuSceneSystem = new DebugMenuSceneSystem(
            _world,
            _sceneSystem, // Pass SceneSystem as ISceneManager
            _inputBindingService,
            _debugOverlayService
        );

        // Register DebugMenuSceneSystem with SceneSystem (as ISceneSystem)
        _sceneSystem.SetDebugMenuSceneSystem(debugMenuSceneSystem);

        // Register DebugMenuSceneSystem with update systems (it implements IPrioritizedSystem)
        RegisterUpdateSystem(debugMenuSceneSystem);

        // Create shader cycle system (for cycling through shader effects with F4 and F5)
        if (_shaderManagerSystem != null)
        {
            var shaderCycleSystem = new ShaderCycleSystem(
                _world,
                _inputBindingService,
                _shaderManagerSystem,
                _playerSystem, // Pass PlayerSystem for F5 player shader cycling
                LoggerFactory.CreateLogger<ShaderCycleSystem>()
            );
            RegisterUpdateSystem(shaderCycleSystem);
        }

        // Register shader parameter animation system if it exists
        if (_shaderParameterAnimationSystem != null)
            RegisterUpdateSystem(_shaderParameterAnimationSystem);
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

            // Update shader system references
            _scriptApiProvider.UpdateShaderSystems(
                _shaderManagerSystem,
                _shaderTransitionSystem,
                _shaderMultiAnimSystem,
                _shaderChainSystem,
                _shaderPresetService
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
        var timeControl = _debugOverlayService?.TimeControl;
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

        // SceneSystem coordinates rendering for all scene-specific systems
        _sceneSystem.Render(gameTime);
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
    ///     Creates the game scene using SceneSystem.
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

        var gameSceneEntity = _sceneSystem.CreateScene(sceneComponent, gameSceneComponent);
        _logger.Information("Game scene created: {EntityId}", gameSceneEntity.Id);
    }

    /// <summary>
    ///     Handles SceneCreatedEvent by invalidating update blocking cache.
    /// </summary>
    /// <param name="evt">The scene created event.</param>
    private void OnSceneCreated(ref SceneCreatedEvent evt)
    {
        InvalidateUpdateBlockedCache();
    }

    /// <summary>
    ///     Handles SceneDestroyedEvent by invalidating update blocking cache.
    /// </summary>
    /// <param name="evt">The scene destroyed event.</param>
    private void OnSceneDestroyed(ref SceneDestroyedEvent evt)
    {
        InvalidateUpdateBlockedCache();
    }

    /// <summary>
    ///     Handles SceneActivatedEvent by invalidating update blocking cache.
    /// </summary>
    /// <param name="evt">The scene activated event.</param>
    private void OnSceneActivated(ref SceneActivatedEvent evt)
    {
        InvalidateUpdateBlockedCache();
    }

    /// <summary>
    ///     Handles SceneDeactivatedEvent by invalidating update blocking cache.
    /// </summary>
    /// <param name="evt">The scene deactivated event.</param>
    private void OnSceneDeactivated(ref SceneDeactivatedEvent evt)
    {
        InvalidateUpdateBlockedCache();
    }

    /// <summary>
    ///     Handles ScenePausedEvent by invalidating update blocking cache.
    /// </summary>
    /// <param name="evt">The scene paused event.</param>
    private void OnScenePaused(ref ScenePausedEvent evt)
    {
        InvalidateUpdateBlockedCache();
    }

    /// <summary>
    ///     Handles SceneResumedEvent by invalidating update blocking cache.
    /// </summary>
    /// <param name="evt">The scene resumed event.</param>
    private void OnSceneResumed(ref SceneResumedEvent evt)
    {
        InvalidateUpdateBlockedCache();
    }
}
