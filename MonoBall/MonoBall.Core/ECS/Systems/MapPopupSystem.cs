using System;
using Arch.Core;
using Arch.System;
using FontStashSharp;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoBall.Core.Constants;
using MonoBall.Core.ECS;
using MonoBall.Core.ECS.Components;
using MonoBall.Core.ECS.Events;
using MonoBall.Core.Mods;
using MonoBall.Core.Rendering;
using MonoBall.Core.Scenes;
using MonoBall.Core.Scenes.Components;
using MonoBall.Core.Scenes.Systems;
using MonoBall.Core.UI.Windows.Animations;
using MonoBall.Core.UI.Windows.Animations.Events;
using Serilog;

namespace MonoBall.Core.ECS.Systems
{
    /// <summary>
    /// System responsible for managing popup lifecycle and animation state machine.
    /// Creates popup entities and scenes, updates animation states, and handles cleanup.
    /// </summary>
    public class MapPopupSystem : BaseSystem<World, float>, IPrioritizedSystem, IDisposable
    {
        // GBA-accurate constants (pokeemerald dimensions at 1x scale)
        // Use GameConstants for consistency across systems

        private readonly SceneSystem _sceneSystem;
        private readonly FontService _fontService;
        private readonly IModManager _modManager;
        private readonly ILogger _logger;
        private readonly GraphicsDevice _graphicsDevice;
        private readonly SpriteBatch _spriteBatch;
        private readonly MapPopupRendererSystem _mapPopupRendererSystem;
        private readonly IConstantsService _constants;
        private Entity? _currentPopupEntity;
        private Entity? _currentPopupSceneEntity;
        private bool _disposed = false;

        /// <summary>
        /// Gets the execution priority for this system.
        /// </summary>
        public int Priority => SystemPriority.MapPopup;

        // Cached query descriptions to avoid allocations in hot paths
        private readonly QueryDescription _mapPopupScenesQuery = new QueryDescription().WithAll<
            SceneComponent,
            MapPopupSceneComponent
        >();

        private readonly QueryDescription _cameraQuery =
            new QueryDescription().WithAll<CameraComponent>();

        /// <summary>
        /// Initializes a new instance of the MapPopupSystem.
        /// </summary>
        /// <param name="world">The ECS world.</param>
        /// <param name="sceneSystem">The scene system for creating/destroying scenes.</param>
        /// <param name="graphicsDevice">The graphics device.</param>
        /// <param name="spriteBatch">The sprite batch for rendering.</param>
        /// <param name="mapPopupRendererSystem">The map popup renderer system.</param>
        /// <param name="fontService">The font service for text measurement.</param>
        /// <param name="modManager">The mod manager for accessing definitions.</param>
        /// <param name="logger">The logger for logging operations.</param>
        /// <param name="constants">The constants service for accessing game constants. Required.</param>
        public MapPopupSystem(
            World world,
            SceneSystem sceneSystem,
            GraphicsDevice graphicsDevice,
            SpriteBatch spriteBatch,
            MapPopupRendererSystem mapPopupRendererSystem,
            FontService fontService,
            IModManager modManager,
            ILogger logger,
            IConstantsService constants
        )
            : base(world)
        {
            _sceneSystem = sceneSystem ?? throw new ArgumentNullException(nameof(sceneSystem));
            _graphicsDevice =
                graphicsDevice ?? throw new ArgumentNullException(nameof(graphicsDevice));
            _spriteBatch = spriteBatch ?? throw new ArgumentNullException(nameof(spriteBatch));
            _mapPopupRendererSystem =
                mapPopupRendererSystem
                ?? throw new ArgumentNullException(nameof(mapPopupRendererSystem));
            _fontService = fontService ?? throw new ArgumentNullException(nameof(fontService));
            _modManager = modManager ?? throw new ArgumentNullException(nameof(modManager));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _constants = constants ?? throw new ArgumentNullException(nameof(constants));

            // Subscribe to events using RefAction pattern
            EventBus.Subscribe<MapPopupShowEvent>(OnMapPopupShow);
            EventBus.Subscribe<MapPopupHideEvent>(OnMapPopupHide);
            EventBus.Subscribe<WindowAnimationCompletedEvent>(OnWindowAnimationCompleted);
            EventBus.Subscribe<WindowAnimationDestroyEvent>(OnWindowAnimationDestroy);
        }

        /// <summary>
        /// Handles MapPopupShowEvent by creating popup entity and scene.
        /// </summary>
        /// <param name="evt">The map popup show event.</param>
        private void OnMapPopupShow(ref MapPopupShowEvent evt)
        {
            if (string.IsNullOrEmpty(evt.MapSectionName))
            {
                _logger.Warning("MapPopupShowEvent has empty MapSectionName, skipping popup");
                return;
            }

            _logger.Debug(
                "Showing popup for {MapSectionName} with theme {ThemeId}",
                evt.MapSectionName,
                evt.ThemeId
            );

            // Cancel existing popup if present
            if (_currentPopupEntity.HasValue && World.IsAlive(_currentPopupEntity.Value))
            {
                _logger.Debug("Cancelling existing popup");
                DestroyPopup(_currentPopupEntity.Value);
            }

            // Look up PopupThemeDefinition to get background and outline IDs
            var popupTheme = _modManager.GetDefinition<PopupThemeDefinition>(evt.ThemeId);
            if (popupTheme == null)
            {
                _logger.Warning("PopupTheme definition not found for {ThemeId}", evt.ThemeId);
                return;
            }

            // Get outline definition first (needed for dimension calculation)
            var outlineDef = _modManager.GetDefinition<PopupOutlineDefinition>(popupTheme.Outline);
            if (outlineDef == null)
            {
                _logger.Warning("Outline definition not found for {OutlineId}", popupTheme.Outline);
                return;
            }

            // Validate font system (needed for text measurement, but not critical for popup creation)
            var fontSystem = _fontService.GetFontSystem("base:font:game/pokemon");
            if (fontSystem == null)
            {
                _logger.Warning(
                    "Font 'base:font:game/pokemon' not found, popup will be created but text may not render correctly"
                );
                // Continue anyway - popup can still be created without font
            }

            // Calculate popup dimensions (fixed size like pokeemerald)
            // Background is always 80x24 at 1x scale, plus border tiles
            int tileSize = outlineDef.IsTileSheet ? outlineDef.TileWidth : 8;
            float popupHeight = _constants.Get<int>("PopupBackgroundHeight") + (tileSize * 2); // Background + border on top and bottom

            // Create popup scene entity first (before popup entity so we can reference it)
            var sceneComponent = new SceneComponent
            {
                SceneId = "map:popup",
                Priority = ScenePriorities.GameScene + 10, // 60
                CameraMode = SceneCameraMode.GameCamera,
                BlocksUpdate = false,
                BlocksDraw = false,
                IsActive = true,
                IsPaused = false,
                BackgroundColor = Color.Transparent, // Map popup is transparent overlay
            };

            var popupSceneComponent = new MapPopupSceneComponent();

            Entity popupSceneEntity;
            try
            {
                popupSceneEntity = _sceneSystem.CreateScene(sceneComponent, popupSceneComponent);
            }
            catch (Exception ex)
            {
                _logger.Error(
                    ex,
                    "Failed to create popup scene for {MapSectionName}",
                    evt.MapSectionName
                );
                // Clean up popup entity if scene creation failed
                if (_currentPopupEntity.HasValue)
                {
                    World.Destroy(_currentPopupEntity.Value);
                    _currentPopupEntity = null;
                }
                return;
            }

            _currentPopupSceneEntity = popupSceneEntity;

            // Create popup entity
            var popupComponent = new MapPopupComponent
            {
                MapSectionName = evt.MapSectionName,
                ThemeId = evt.ThemeId,
                BackgroundId = popupTheme.Background,
                OutlineId = popupTheme.Outline,
                SceneEntity = popupSceneEntity, // Store scene entity reference
            };

            // Create window animation component using helper
            var animationConfig = WindowAnimationHelper.CreateSlideDownUpAnimation(
                slideDownDuration: 0.4f, // GBA-accurate slide in duration
                pauseDuration: 2.5f, // GBA-accurate display duration
                slideUpDuration: 0.4f, // GBA-accurate slide out duration
                windowHeight: popupHeight,
                destroyOnComplete: true
            );

            // Create entity first, then set WindowEntity reference
            _currentPopupEntity = World.Create(popupComponent);

            // Add explicit scene ownership component for queryable scene membership
            World.Add(
                _currentPopupEntity.Value,
                new SceneOwnershipComponent { SceneEntity = popupSceneEntity }
            );

            var windowAnim = new WindowAnimationComponent
            {
                State = WindowAnimationState.NotStarted,
                ElapsedTime = 0f,
                Config = animationConfig,
                PositionOffset = new Vector2(0, -popupHeight), // Start off-screen
                Scale = 1.0f,
                Opacity = 1.0f,
                WindowEntity = _currentPopupEntity.Value, // Set to popup entity itself
            };

            // Add animation component to the entity
            World.Add(_currentPopupEntity.Value, windowAnim);

            _logger.Information(
                "Created popup entity {PopupEntityId} and scene entity {SceneEntityId} for {MapSectionName} (height: {Height})",
                _currentPopupEntity.Value.Id,
                popupSceneEntity.Id,
                evt.MapSectionName,
                popupHeight
            );
        }

        /// <summary>
        /// Handles MapPopupHideEvent by destroying popup entity and scene.
        /// </summary>
        /// <param name="evt">The map popup hide event.</param>
        private void OnMapPopupHide(ref MapPopupHideEvent evt)
        {
            if (!World.IsAlive(evt.PopupEntity))
            {
                _logger.Debug("Popup entity is not alive, skipping cleanup");
                return;
            }

            _logger.Debug("Hiding popup");
            DestroyPopup(evt.PopupEntity);
        }

        /// <summary>
        /// Destroys a popup entity and its associated scene entity.
        /// </summary>
        /// <param name="popupEntity">The popup entity to destroy.</param>
        private void DestroyPopup(Entity popupEntity)
        {
            _logger.Debug("Destroying popup entity {PopupEntityId}", popupEntity.Id);

            // Get scene entity from popup component before destroying
            // Fail fast if popup entity doesn't exist or doesn't have required component
            if (!World.IsAlive(popupEntity))
            {
                throw new InvalidOperationException(
                    $"Cannot destroy popup entity {popupEntity.Id}: Entity is not alive."
                );
            }

            if (!World.Has<MapPopupComponent>(popupEntity))
            {
                throw new InvalidOperationException(
                    $"Cannot destroy popup entity {popupEntity.Id}: Entity does not have MapPopupComponent. "
                        + "Cannot determine scene entity to destroy."
                );
            }

            ref var popupComponent = ref World.Get<MapPopupComponent>(popupEntity);
            Entity sceneEntityToDestroy = popupComponent.SceneEntity;

            // Validate scene entity exists and is alive
            if (!World.IsAlive(sceneEntityToDestroy))
            {
                throw new InvalidOperationException(
                    $"Cannot destroy popup scene entity for popup {popupEntity.Id}: "
                        + $"Scene entity {sceneEntityToDestroy.Id} is not alive. "
                        + "This indicates the scene entity was already destroyed or never created properly."
                );
            }

            // Destroy popup scene entity first (before destroying popup entity)
            _sceneSystem.DestroyScene(sceneEntityToDestroy);

            // Destroy popup entity
            World.Destroy(popupEntity);

            // Clear tracked entities
            if (_currentPopupEntity.HasValue && _currentPopupEntity.Value.Id == popupEntity.Id)
            {
                _currentPopupEntity = null;
            }
            if (
                _currentPopupSceneEntity.HasValue
                && _currentPopupSceneEntity.Value.Id == sceneEntityToDestroy.Id
            )
            {
                _currentPopupSceneEntity = null;
            }
        }

        /// <summary>
        /// Handles WindowAnimationCompletedEvent.
        /// Animation logic is handled by WindowAnimationSystem, so this handler can be empty or log.
        /// </summary>
        /// <param name="evt">The window animation completed event.</param>
        private void OnWindowAnimationCompleted(ref WindowAnimationCompletedEvent evt)
        {
            // Animation completed - WindowAnimationSystem handles the animation logic
            // This handler is here for potential future use (logging, etc.)
        }

        /// <summary>
        /// Handles WindowAnimationDestroyEvent by destroying the popup.
        /// </summary>
        /// <param name="evt">The window animation destroy event.</param>
        private void OnWindowAnimationDestroy(ref WindowAnimationDestroyEvent evt)
        {
            if (!World.IsAlive(evt.WindowEntity))
            {
                _logger.Debug("Window entity is not alive, skipping cleanup");
                return;
            }

            _logger.Debug("Destroying popup from animation destroy event");
            DestroyPopup(evt.WindowEntity);
        }

        /// <summary>
        /// Renders a single map popup scene. Called by SceneRendererSystem (coordinator) for a single scene.
        /// </summary>
        /// <param name="sceneEntity">The scene entity to render.</param>
        /// <param name="gameTime">The game time.</param>
        public void RenderScene(Entity sceneEntity, GameTime gameTime)
        {
            // Verify this is actually a map popup scene
            if (!World.Has<MapPopupSceneComponent>(sceneEntity))
            {
                return;
            }

            ref var scene = ref World.Get<SceneComponent>(sceneEntity);
            if (!scene.IsActive)
            {
                return;
            }

            // Determine camera based on CameraMode
            CameraComponent? camera = null;

            switch (scene.CameraMode)
            {
                case SceneCameraMode.GameCamera:
                    camera = GetActiveGameCamera();
                    break;

                case SceneCameraMode.SceneCamera:
                    if (scene.CameraEntityId.HasValue)
                    {
                        // Query for camera entity by ID
                        int cameraEntityId = scene.CameraEntityId.Value;
                        bool foundCamera = false;
                        World.Query(
                            in _cameraQuery,
                            (Entity entity, ref CameraComponent cam) =>
                            {
                                if (entity.Id == cameraEntityId)
                                {
                                    camera = cam;
                                    foundCamera = true;
                                }
                            }
                        );

                        if (!foundCamera)
                        {
                            _logger.Warning(
                                "MapPopupScene '{SceneId}' specified SceneCamera mode but camera entity {CameraEntityId} is not found or doesn't have CameraComponent",
                                scene.SceneId,
                                cameraEntityId
                            );
                            return;
                        }
                    }
                    else
                    {
                        _logger.Warning(
                            "MapPopupScene '{SceneId}' specified SceneCamera mode but CameraEntityId is null",
                            scene.SceneId
                        );
                        return;
                    }
                    break;

                case SceneCameraMode.ScreenCamera:
                    // MapPopupScene requires a camera for viewport
                    _logger.Warning(
                        "MapPopupScene '{SceneId}' requires a camera for viewport. Use GameCamera or SceneCamera mode.",
                        scene.SceneId
                    );
                    return;
            }

            if (!camera.HasValue)
            {
                _logger.Warning(
                    "MapPopupScene '{SceneId}' requires camera but none was found. Scene will not render.",
                    scene.SceneId
                );
                return;
            }

            // Render the map popup scene
            RenderMapPopupScene(sceneEntity, ref scene, gameTime, camera.Value);
        }

        /// <summary>
        /// Renders the map popup scene using the specified camera (popups render in screen space within camera viewport).
        /// </summary>
        /// <param name="sceneEntity">The scene entity.</param>
        /// <param name="scene">The scene component.</param>
        /// <param name="gameTime">The game time.</param>
        /// <param name="camera">The camera component.</param>
        private void RenderMapPopupScene(
            Entity sceneEntity,
            ref SceneComponent scene,
            GameTime gameTime,
            CameraComponent camera
        )
        {
            // Save original viewport
            var savedViewport = _graphicsDevice.Viewport;

            try
            {
                // Set viewport to camera's virtual viewport (if available) or regular viewport
                // Popups render in screen space within this viewport
                if (camera.VirtualViewport != Rectangle.Empty)
                {
                    _graphicsDevice.Viewport = new Viewport(camera.VirtualViewport);
                }

                // Render popups in SCREEN SPACE (not world space) - use Matrix.Identity
                // Map popups are UI overlays that should stay fixed on screen
                _spriteBatch.Begin(
                    SpriteSortMode.Deferred,
                    BlendState.AlphaBlend,
                    SamplerState.PointClamp,
                    DepthStencilState.None,
                    RasterizerState.CullCounterClockwise,
                    null,
                    Matrix.Identity // Screen space - no camera transform
                );

                // Render popup (renderer handles popup rendering in screen space)
                _mapPopupRendererSystem.Render(sceneEntity, camera, gameTime);

                // End SpriteBatch
                _spriteBatch.End();
            }
            finally
            {
                // Always restore viewport, even if rendering fails
                _graphicsDevice.Viewport = savedViewport;
            }
        }

        /// <summary>
        /// Gets the active game camera (CameraComponent.IsActive == true).
        /// </summary>
        /// <returns>The active camera component, or null if none found.</returns>
        private CameraComponent? GetActiveGameCamera()
        {
            CameraComponent? activeCamera = null;

            World.Query(
                in _cameraQuery,
                (Entity entity, ref CameraComponent camera) =>
                {
                    if (camera.IsActive)
                    {
                        activeCamera = camera;
                    }
                }
            );

            return activeCamera;
        }

        /// <summary>
        /// Disposes the system and unsubscribes from events.
        /// </summary>
        /// <remarks>
        /// Implements IDisposable to properly clean up event subscriptions.
        /// Uses standard dispose pattern without finalizer since only managed resources are disposed.
        /// Uses 'new' keyword because BaseSystem may have a Dispose() method with different signature.
        /// </remarks>
        public new void Dispose()
        {
            Dispose(true);
        }

        /// <summary>
        /// Disposes the system and unsubscribes from events.
        /// </summary>
        /// <param name="disposing">Whether managed resources should be disposed.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // Unsubscribe from events using RefAction pattern
                    EventBus.Unsubscribe<MapPopupShowEvent>(OnMapPopupShow);
                    EventBus.Unsubscribe<MapPopupHideEvent>(OnMapPopupHide);
                    EventBus.Unsubscribe<WindowAnimationCompletedEvent>(OnWindowAnimationCompleted);
                    EventBus.Unsubscribe<WindowAnimationDestroyEvent>(OnWindowAnimationDestroy);
                }
                _disposed = true;
            }
        }
    }
}
