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
        private readonly ILogger _logger;
        private ISpriteLoaderService _spriteLoader = null!; // Initialized in Initialize()
        private ICameraService _cameraService = null!; // Initialized in Initialize()
        private SpriteBatch? _spriteBatch;

        private Group<float> _updateSystems = null!; // Initialized in Initialize()
        private MapLoaderSystem _mapLoaderSystem = null!; // Initialized in Initialize()
        private MapConnectionSystem _mapConnectionSystem = null!; // Initialized in Initialize()
        private CameraSystem _cameraSystem = null!; // Initialized in Initialize()
        private CameraViewportSystem _cameraViewportSystem = null!; // Initialized in Initialize()
        private MapRendererSystem _mapRendererSystem = null!; // Initialized in Initialize()
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

        private bool _isInitialized;
        private bool _isDisposed;

        /// <summary>
        /// Initializes a new instance of the SystemManager.
        /// </summary>
        /// <param name="world">The ECS world.</param>
        /// <param name="graphicsDevice">The graphics device.</param>
        /// <param name="modManager">The mod manager.</param>
        /// <param name="tilesetLoader">The tileset loader service.</param>
        /// <param name="logger">The logger for logging operations.</param>
        public SystemManager(
            World world,
            GraphicsDevice graphicsDevice,
            IModManager modManager,
            ITilesetLoaderService tilesetLoader,
            ILogger logger
        )
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _graphicsDevice =
                graphicsDevice ?? throw new ArgumentNullException(nameof(graphicsDevice));
            _modManager = modManager ?? throw new ArgumentNullException(nameof(modManager));
            _tilesetLoader =
                tilesetLoader ?? throw new ArgumentNullException(nameof(tilesetLoader));
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

            // Create services
            _spriteLoader = new SpriteLoaderService(
                _graphicsDevice,
                _modManager,
                LoggerFactory.CreateLogger<SpriteLoaderService>()
            );
            _cameraService = new CameraService(_world, LoggerFactory.CreateLogger<CameraService>());

            // Create update systems
            _mapLoaderSystem = new MapLoaderSystem(
                _world,
                _modManager.Registry,
                _tilesetLoader,
                _spriteLoader,
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
                LoggerFactory.CreateLogger<MapRendererSystem>()
            );
            _mapRendererSystem.SetSpriteBatch(_spriteBatch);
            _spriteRendererSystem = new SpriteRendererSystem(
                _world,
                _graphicsDevice,
                _spriteLoader,
                _cameraService,
                LoggerFactory.CreateLogger<SpriteRendererSystem>()
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
                LoggerFactory.CreateLogger<SceneRendererSystem>()
            );
            _sceneRendererSystem.SetSpriteBatch(_spriteBatch);
            _sceneRendererSystem.SetMapRendererSystem(_mapRendererSystem);
            _sceneRendererSystem.SetSpriteRendererSystem(_spriteRendererSystem);

            // Create font service and performance stats system
            var fontService = new Rendering.FontService(
                _modManager,
                _graphicsDevice,
                LoggerFactory.CreateLogger<Rendering.FontService>()
            );
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

            // Update render systems to track draw calls
            _mapRendererSystem.SetPerformanceStatsSystem(performanceStatsSystem);
            _spriteRendererSystem.SetPerformanceStatsSystem(performanceStatsSystem);

            // Group update systems (including scene systems)
            // SpriteSheetSystem is added early to ensure it's initialized before systems that might publish SpriteSheetChangeRequestEvent
            // InputSystem runs first (Priority 0) to process input and create MovementRequest components
            // MovementSystem runs after InputSystem (Priority 90) to process MovementRequest, update movement, AND handle animation
            _updateSystems = new Group<float>(
                "UpdateSystems",
                _mapLoaderSystem,
                _mapConnectionSystem,
                _playerSystem, // Player initialization only (no per-frame updates)
                _inputSystem, // Priority 0: Process input, create MovementRequest
                _movementSystem, // Priority 90: Process MovementRequest, update movement and animation
                _cameraSystem, // Camera follows player (runs after movement updates)
                _cameraViewportSystem,
                _animatedTileSystem,
                _spriteAnimationSystem, // Animation frame updates (CurrentFrame, FrameTimer)
                _spriteSheetSystem,
                performanceStatsSystem, // Track performance stats each frame
                _sceneManagerSystem,
                _sceneInputSystem,
                debugBarToggleSystem // Handle debug bar toggle input
            );

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

            _isDisposed = true;
        }
    }
}
