using System;
using System.Collections.Generic;
using Arch.Core;
using Arch.System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoBall.Core;
using MonoBall.Core.ECS.Components;
using MonoBall.Core.ECS.Systems;
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
        private SpriteBatch? _spriteBatch;

        private Group<float> _updateSystems = null!; // Initialized in Initialize()
        private MapLoaderSystem _mapLoaderSystem = null!; // Initialized in Initialize()
        private MapConnectionSystem _mapConnectionSystem = null!; // Initialized in Initialize()
        private CameraSystem _cameraSystem = null!; // Initialized in Initialize()
        private CameraViewportSystem _cameraViewportSystem = null!; // Initialized in Initialize()
        private MapRendererSystem _mapRendererSystem = null!; // Initialized in Initialize()
        private SceneManagerSystem _sceneManagerSystem = null!; // Initialized in Initialize()
        private SceneInputSystem _sceneInputSystem = null!; // Initialized in Initialize()
        private SceneRendererSystem _sceneRendererSystem = null!; // Initialized in Initialize()

        private bool _isInitialized;
        private bool _isDisposed;

        /// <summary>
        /// Initializes a new instance of the SystemManager.
        /// </summary>
        /// <param name="world">The ECS world.</param>
        /// <param name="graphicsDevice">The graphics device.</param>
        /// <param name="modManager">The mod manager.</param>
        /// <param name="tilesetLoader">The tileset loader service.</param>
        public SystemManager(
            World world,
            GraphicsDevice graphicsDevice,
            IModManager modManager,
            ITilesetLoaderService tilesetLoader
        )
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _graphicsDevice =
                graphicsDevice ?? throw new ArgumentNullException(nameof(graphicsDevice));
            _modManager = modManager ?? throw new ArgumentNullException(nameof(modManager));
            _tilesetLoader =
                tilesetLoader ?? throw new ArgumentNullException(nameof(tilesetLoader));
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
        /// Initializes all ECS systems. Should be called from LoadContent().
        /// </summary>
        /// <param name="spriteBatch">The sprite batch for rendering.</param>
        public void Initialize(SpriteBatch spriteBatch)
        {
            if (_isInitialized)
            {
                Log.Warning("SystemManager.Initialize: Systems already initialized");
                return;
            }

            if (_isDisposed)
            {
                throw new ObjectDisposedException(nameof(SystemManager));
            }

            _spriteBatch = spriteBatch ?? throw new ArgumentNullException(nameof(spriteBatch));

            Log.Information("SystemManager.Initialize: Initializing ECS systems");

            // Create update systems
            _mapLoaderSystem = new MapLoaderSystem(_world, _modManager.Registry, _tilesetLoader);
            _mapConnectionSystem = new MapConnectionSystem(_world);
            _cameraSystem = new CameraSystem(_world);
            _cameraViewportSystem = new CameraViewportSystem(
                _world,
                _graphicsDevice,
                GameConstants.GbaReferenceWidth,
                GameConstants.GbaReferenceHeight
            ); // GBA resolution

            // Create render systems
            _mapRendererSystem = new MapRendererSystem(_world, _graphicsDevice, _tilesetLoader);
            _mapRendererSystem.SetSpriteBatch(_spriteBatch);

            // Create scene systems
            _sceneManagerSystem = new SceneManagerSystem(_world);
            _sceneInputSystem = new SceneInputSystem(_world, _sceneManagerSystem);
            _sceneRendererSystem = new SceneRendererSystem(
                _world,
                _graphicsDevice,
                _sceneManagerSystem
            );
            _sceneRendererSystem.SetSpriteBatch(_spriteBatch);
            _sceneRendererSystem.SetMapRendererSystem(_mapRendererSystem);

            // Group update systems (including scene systems)
            _updateSystems = new Group<float>(
                "UpdateSystems",
                _mapLoaderSystem,
                _mapConnectionSystem,
                _cameraSystem,
                _cameraViewportSystem,
                _sceneManagerSystem,
                _sceneInputSystem
            );

            _updateSystems.Initialize();

            _isInitialized = true;
            Log.Information("SystemManager.Initialize: ECS systems initialized successfully");
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

            Log.Debug("SystemManager.Dispose: Disposing systems");

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
            _sceneManagerSystem = null!;
            _sceneInputSystem = null!;
            _sceneRendererSystem = null!;

            _isDisposed = true;
        }
    }
}
