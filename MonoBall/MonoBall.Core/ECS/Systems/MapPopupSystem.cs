using System;
using Arch.Core;
using Arch.System;
using FontStashSharp;
using Microsoft.Xna.Framework;
using MonoBall.Core.ECS;
using MonoBall.Core.ECS.Components;
using MonoBall.Core.ECS.Events;
using MonoBall.Core.Mods;
using MonoBall.Core.Rendering;
using MonoBall.Core.Scenes;
using MonoBall.Core.Scenes.Components;
using MonoBall.Core.Scenes.Systems;
using Serilog;

namespace MonoBall.Core.ECS.Systems
{
    /// <summary>
    /// System responsible for managing popup lifecycle and animation state machine.
    /// Creates popup entities and scenes, updates animation states, and handles cleanup.
    /// </summary>
    public class MapPopupSystem : BaseSystem<World, float>, IDisposable
    {
        // GBA-accurate constants (pokeemerald dimensions at 1x scale)
        // Use GameConstants for consistency across systems

        private readonly SceneManagerSystem _sceneManagerSystem;
        private readonly FontService _fontService;
        private readonly IModManager _modManager;
        private readonly ILogger _logger;
        private Entity? _currentPopupEntity;
        private Entity? _currentPopupSceneEntity;
        private bool _disposed = false;

        // Cached query description for popup entities
        private readonly QueryDescription _popupQuery = new QueryDescription().WithAll<
            MapPopupComponent,
            PopupAnimationComponent
        >();

        /// <summary>
        /// Initializes a new instance of the MapPopupSystem.
        /// </summary>
        /// <param name="world">The ECS world.</param>
        /// <param name="sceneManagerSystem">The scene manager system for creating/destroying scenes.</param>
        /// <param name="fontService">The font service for text measurement.</param>
        /// <param name="modManager">The mod manager for accessing definitions.</param>
        /// <param name="logger">The logger for logging operations.</param>
        public MapPopupSystem(
            World world,
            SceneManagerSystem sceneManagerSystem,
            FontService fontService,
            IModManager modManager,
            ILogger logger
        )
            : base(world)
        {
            _sceneManagerSystem =
                sceneManagerSystem ?? throw new ArgumentNullException(nameof(sceneManagerSystem));
            _fontService = fontService ?? throw new ArgumentNullException(nameof(fontService));
            _modManager = modManager ?? throw new ArgumentNullException(nameof(modManager));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            // Subscribe to events using RefAction pattern
            EventBus.Subscribe<MapPopupShowEvent>(OnMapPopupShow);
            EventBus.Subscribe<MapPopupHideEvent>(OnMapPopupHide);
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
                _logger.Warning(
                    "PopupTheme definition not found for {ThemeId}",
                    evt.ThemeId
                );
                return;
            }

            // Get outline definition first (needed for dimension calculation)
            var outlineDef = _modManager.GetDefinition<PopupOutlineDefinition>(popupTheme.Outline);
            if (outlineDef == null)
            {
                _logger.Warning(
                    "Outline definition not found for {OutlineId}",
                    popupTheme.Outline
                );
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
            float popupHeight = GameConstants.PopupBackgroundHeight + (tileSize * 2); // Background + border on top and bottom

            // Create popup scene entity first (before popup entity so we can reference it)
            var sceneComponent = new SceneComponent
            {
                SceneId = "MapPopupScene",
                Priority = ScenePriorities.GameScene + 10, // 60
                CameraMode = SceneCameraMode.GameCamera,
                BlocksUpdate = false,
                BlocksDraw = false,
                IsActive = true,
                IsPaused = false,
            };

            var popupSceneComponent = new MapPopupSceneComponent();

            Entity popupSceneEntity;
            try
            {
                popupSceneEntity = _sceneManagerSystem.CreateScene(
                    sceneComponent,
                    popupSceneComponent
                );
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

            var animationComponent = new PopupAnimationComponent
            {
                State = PopupAnimationState.SlidingDown,
                ElapsedTime = 0f, // Start at 0 - will be incremented in first Update() call
                SlideDownDuration = 0.4f, // GBA-accurate slide in duration
                PauseDuration = 2.5f, // GBA-accurate display duration
                SlideUpDuration = 0.4f, // GBA-accurate slide out duration
                PopupHeight = popupHeight,
                CurrentY = -popupHeight, // Start above screen - ensures popup is off-screen when first rendered
            };

            _currentPopupEntity = World.Create(popupComponent, animationComponent);

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
            _logger.Debug(
                "Destroying popup entity {PopupEntityId}",
                popupEntity.Id
            );

            // Get scene entity from popup component before destroying
            Entity? sceneEntityToDestroy = null;
            if (World.IsAlive(popupEntity) && World.Has<MapPopupComponent>(popupEntity))
            {
                ref var popupComponent = ref World.Get<MapPopupComponent>(popupEntity);
                sceneEntityToDestroy = popupComponent.SceneEntity;
            }

            // Destroy popup entity first
            if (World.IsAlive(popupEntity))
            {
                World.Destroy(popupEntity);
            }

            // Destroy popup scene entity
            if (sceneEntityToDestroy.HasValue && World.IsAlive(sceneEntityToDestroy.Value))
            {
                _sceneManagerSystem.DestroyScene(sceneEntityToDestroy.Value);
            }
            else if (
                _currentPopupSceneEntity.HasValue && World.IsAlive(_currentPopupSceneEntity.Value)
            )
            {
                // Fallback: destroy tracked scene entity
                _sceneManagerSystem.DestroyScene(_currentPopupSceneEntity.Value);
            }

            // Clear tracked entities
            if (_currentPopupEntity.HasValue && _currentPopupEntity.Value.Id == popupEntity.Id)
            {
                _currentPopupEntity = null;
            }
            _currentPopupSceneEntity = null;
        }

        /// <summary>
        /// Updates popup animation states.
        /// </summary>
        /// <param name="deltaTime">The elapsed time since last update.</param>
        public override void Update(in float deltaTime)
        {
            // Copy in parameter to local variable for use in lambda (cannot capture in parameters)
            float dt = deltaTime;
            World.Query(
                in _popupQuery,
                (Entity entity, ref PopupAnimationComponent anim) =>
                {
                    // Ensure popup starts off-screen if animation hasn't started yet
                    // This handles the case where popup is created and rendered in the same frame
                    if (anim.State == PopupAnimationState.SlidingDown && anim.ElapsedTime == 0f)
                    {
                        anim.CurrentY = -anim.PopupHeight; // Ensure off-screen position
                    }

                    anim.ElapsedTime += dt;

                    switch (anim.State)
                    {
                        case PopupAnimationState.SlidingDown:
                        {
                            float progress = anim.ElapsedTime / anim.SlideDownDuration;
                            if (progress >= 1.0f)
                            {
                                anim.CurrentY = 0f; // Fully visible
                                anim.State = PopupAnimationState.Paused;
                                anim.ElapsedTime = 0f;
                            }
                            else
                            {
                                // Ease-out interpolation for smooth animation (slides DOWN from top)
                                float easedProgress = 1f - MathF.Pow(1f - progress, 3f); // Cubic ease-out
                                anim.CurrentY = MathHelper.Lerp(
                                    -anim.PopupHeight,
                                    0f,
                                    easedProgress
                                );
                            }
                            break;
                        }

                        case PopupAnimationState.Paused:
                        {
                            if (anim.ElapsedTime >= anim.PauseDuration)
                            {
                                anim.State = PopupAnimationState.SlidingUp;
                                anim.ElapsedTime = 0f;
                            }
                            break;
                        }

                        case PopupAnimationState.SlidingUp:
                        {
                            float progress = anim.ElapsedTime / anim.SlideUpDuration;
                            if (progress >= 1.0f)
                            {
                                anim.CurrentY = -anim.PopupHeight; // Off-screen above
                                // Fire MapPopupHideEvent
                                var hideEvent = new MapPopupHideEvent { PopupEntity = entity };
                                EventBus.Send(ref hideEvent);
                            }
                            else
                            {
                                // Ease-in interpolation for smooth animation (slides UP to top)
                                float easedProgress = progress * progress * progress; // Cubic ease-in
                                anim.CurrentY = MathHelper.Lerp(
                                    0f,
                                    -anim.PopupHeight,
                                    easedProgress
                                );
                            }
                            break;
                        }
                    }
                }
            );
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
                }
                _disposed = true;
            }
        }
    }
}
