using System;
using System.Collections.Generic;
using System.IO;
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
using MonoBall.Core.UI.Windows.Animations;
using Serilog;

namespace MonoBall.Core.Scenes.Systems
{
    /// <summary>
    /// System that handles lifecycle, updates, and rendering for MapPopupScene entities.
    /// Queries for MapPopupSceneComponent entities and processes them.
    /// Consolidates popup lifecycle, animation updates, and rendering.
    /// </summary>
    public class MapPopupSceneSystem
        : BaseSystem<World, float>,
            IPrioritizedSystem,
            IDisposable,
            ISceneSystem
    {
        // GBA-accurate constants (pokeemerald dimensions at 1x scale)
        // Use GameConstants for consistency across systems

        private readonly ISceneManager _sceneManager;
        private readonly FontService _fontService;
        private readonly IModManager _modManager;
        private readonly ILogger _logger;
        private readonly GraphicsDevice _graphicsDevice;
        private readonly SpriteBatch _spriteBatch;
        private readonly IConstantsService _constants;
        private Entity? _currentPopupEntity;
        private Entity? _currentPopupSceneEntity;
        private bool _disposed = false;

        // Texture cache keyed by definition ID
        private readonly Dictionary<string, Texture2D> _textureCache =
            new Dictionary<string, Texture2D>();

        /// <summary>
        /// Gets the execution priority for this system.
        /// </summary>
        public int Priority => SystemPriority.MapPopupScene;

        // Cached query descriptions to avoid allocations in hot paths
        private readonly QueryDescription _popupScenesQuery = new QueryDescription().WithAll<
            SceneComponent,
            MapPopupSceneComponent
        >();

        private readonly QueryDescription _popupQuery = new QueryDescription().WithAll<
            MapPopupComponent,
            WindowAnimationComponent
        >();

        private readonly QueryDescription _cameraQuery =
            new QueryDescription().WithAll<CameraComponent>();

        /// <summary>
        /// Initializes a new instance of the MapPopupSceneSystem.
        /// </summary>
        /// <param name="world">The ECS world.</param>
        /// <param name="sceneManager">The scene manager for creating/destroying scenes.</param>
        /// <param name="graphicsDevice">The graphics device.</param>
        /// <param name="spriteBatch">The sprite batch for rendering.</param>
        /// <param name="fontService">The font service for text measurement.</param>
        /// <param name="modManager">The mod manager for accessing definitions.</param>
        /// <param name="logger">The logger for logging operations.</param>
        /// <param name="constants">The constants service for accessing game constants. Required.</param>
        public MapPopupSceneSystem(
            World world,
            ISceneManager sceneManager,
            GraphicsDevice graphicsDevice,
            SpriteBatch spriteBatch,
            FontService fontService,
            IModManager modManager,
            ILogger logger,
            IConstantsService constants
        )
            : base(world)
        {
            _sceneManager = sceneManager ?? throw new ArgumentNullException(nameof(sceneManager));
            _graphicsDevice =
                graphicsDevice ?? throw new ArgumentNullException(nameof(graphicsDevice));
            _spriteBatch = spriteBatch ?? throw new ArgumentNullException(nameof(spriteBatch));
            _fontService = fontService ?? throw new ArgumentNullException(nameof(fontService));
            _modManager = modManager ?? throw new ArgumentNullException(nameof(modManager));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _constants = constants ?? throw new ArgumentNullException(nameof(constants));

            // NOTE: This system only handles rendering. MapPopupSystem handles popup lifecycle.
            // No event subscriptions needed - popups are created/destroyed by MapPopupSystem.
        }

        // Note: Animation updates are now handled by WindowAnimationSystem.
        // This system only handles rendering and scene lifecycle.

        /// <summary>
        /// Updates a specific map popup scene entity.
        /// Implements ISceneSystem interface.
        /// </summary>
        /// <param name="sceneEntity">The scene entity to update.</param>
        /// <param name="deltaTime">The elapsed time since last update.</param>
        public void Update(Entity sceneEntity, float deltaTime)
        {
            // MapPopupSceneSystem updates popup entities (not scene entities) in ProcessInternal()
            // Per-scene updates are not needed - the popup animation updates handle all popups
            // This method exists to satisfy ISceneSystem interface
        }

        /// <summary>
        /// Performs internal processing for map popup scenes.
        /// Updates popup animation states by querying for popup entities.
        /// Implements ISceneSystem interface.
        /// </summary>
        /// <param name="deltaTime">The elapsed time since last update.</param>
        public void ProcessInternal(float deltaTime)
        {
            // Delegate to the internal Update method that processes popup animations
            Update(in deltaTime);
        }

        /// <summary>
        /// DEPRECATED: MapPopupSystem now handles popup creation. This method should not be called.
        /// </summary>
        /// <param name="evt">The map popup show event.</param>
        [Obsolete("MapPopupSystem now handles popup creation. This method should not be called.")]
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
            // Use unique scene ID to avoid collisions if duplicate events fire
            var sceneComponent = new SceneComponent
            {
                SceneId = $"map:popup:{Guid.NewGuid()}",
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
                popupSceneEntity = _sceneManager.CreateScene(sceneComponent, popupSceneComponent);
            }
            catch (Exception ex)
            {
                _logger.Error(
                    ex,
                    "Failed to create popup scene for {MapSectionName}",
                    evt.MapSectionName
                );
                // Note: At this point, _currentPopupEntity has NOT been assigned yet
                // (it's assigned at line 324 after scene creation succeeds).
                // The old popup entity was already destroyed at line 231 if it existed.
                // Nothing to clean up here - just return.
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
        /// DEPRECATED: MapPopupSystem now handles popup destruction. This method should not be called.
        /// </summary>
        /// <param name="evt">The map popup hide event.</param>
        [Obsolete(
            "MapPopupSystem now handles popup destruction. This method should not be called."
        )]
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
            _sceneManager.DestroyScene(sceneEntityToDestroy);

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
        /// Renders a single map popup scene. Called by SceneSystem (coordinator) for a single scene.
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

                // Render popups (query for popup entities and render them)
                RenderPopups(sceneEntity, camera, gameTime);

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
        /// Renders popups for the specified scene entity.
        /// </summary>
        /// <param name="sceneEntity">The popup scene entity.</param>
        /// <param name="camera">The camera component to use for positioning.</param>
        /// <param name="gameTime">The game time.</param>
        private void RenderPopups(Entity sceneEntity, CameraComponent camera, GameTime gameTime)
        {
            // Query for popup entities
            int popupCount = 0;
            World.Query(
                in _popupQuery,
                (Entity entity, ref MapPopupComponent popup, ref WindowAnimationComponent anim) =>
                {
                    // Only render popups that belong to this scene
                    if (popup.SceneEntity.Id == sceneEntity.Id)
                    {
                        // Don't render if animation is completed (like old implementation)
                        // This prevents rendering leftover pixels when animation finishes
                        if (anim.State == WindowAnimationState.Completed)
                        {
                            return;
                        }

                        popupCount++;
                        _logger.Debug(
                            "Rendering popup entity {EntityId} for '{MapSectionName}', State={State}",
                            entity.Id,
                            popup.MapSectionName,
                            anim.State
                        );
                        RenderPopup(entity, ref popup, ref anim, camera);
                    }
                }
            );

            if (popupCount == 0)
            {
                _logger.Debug(
                    "No popup entities found to render for scene {SceneEntityId}",
                    sceneEntity.Id
                );
            }
        }

        /// <summary>
        /// Renders a single popup in screen space (GBA-accurate pokeemerald style).
        /// </summary>
        private void RenderPopup(
            Entity entity,
            ref MapPopupComponent popup,
            ref WindowAnimationComponent anim,
            CameraComponent camera
        )
        {
            // Load textures (cached, so fast on subsequent calls)
            var backgroundTexture = LoadBackgroundTexture(popup.BackgroundId);
            var outlineTexture = LoadOutlineTexture(popup.OutlineId);

            if (backgroundTexture == null || outlineTexture == null)
            {
                _logger.Warning(
                    "Failed to load textures for popup (background: {BackgroundId}, outline: {OutlineId})",
                    popup.BackgroundId,
                    popup.OutlineId
                );
                return;
            }

            // Get definitions for dimensions
            var backgroundDef = _modManager.GetDefinition<PopupBackgroundDefinition>(
                popup.BackgroundId
            );
            var outlineDef = _modManager.GetDefinition<PopupOutlineDefinition>(popup.OutlineId);

            if (backgroundDef == null || outlineDef == null)
            {
                _logger.Warning(
                    "Failed to get definitions for popup (background: {BackgroundId}, outline: {OutlineId})",
                    popup.BackgroundId,
                    popup.OutlineId
                );
                return;
            }

            // Calculate viewport scale factor (from reference resolution)
            // Use shared utility to ensure consistency with other systems
            int currentScale = CameraTransformUtility.GetViewportScale(
                camera,
                _constants.Get<int>("ReferenceWidth")
            );

            // Calculate scaled dimensions
            int tileSize = outlineDef.IsTileSheet ? outlineDef.TileWidth : 8;
            int borderThickness = tileSize * currentScale;
            int bgWidth = _constants.Get<int>("PopupBackgroundWidth") * currentScale;
            int bgHeight = _constants.Get<int>("PopupBackgroundHeight") * currentScale;

            // Calculate popup position in SCREEN SPACE (top-left corner)
            // Calculate base position without animation offset - apply PositionOffset from animation
            // PositionOffset is in world space, so scale it to screen space
            int scaledPadding = _constants.Get<int>("PopupScreenPadding") * currentScale;
            int popupX = scaledPadding; // Top-left corner
            int scaledPositionOffsetY = (int)MathF.Round(anim.PositionOffset.Y * currentScale);
            int popupY = scaledPositionOffsetY; // Base Y position (0) + scaled PositionOffset

            // Calculate total popup height (border on top + background + border on bottom)
            int totalPopupHeight = (borderThickness * 2) + bgHeight;

            // Skip rendering if popup is completely off-screen
            // Check if bottom edge is above screen (completely off-screen above)
            if (popupY + totalPopupHeight < 0)
            {
                return; // Popup is completely above the screen, don't render
            }

            // Check if top edge is below screen (completely off-screen below)
            int viewportHeight = camera.Viewport.Height;
            if (popupY > viewportHeight)
            {
                return; // Popup is completely below the screen, don't render
            }

            // Background position (inside the border frame)
            int bgX = popupX + borderThickness;
            int bgY = popupY + borderThickness;

            // Draw background texture (fixed size, scaled)
            _spriteBatch.Draw(
                backgroundTexture,
                new Rectangle(bgX, bgY, bgWidth, bgHeight),
                new Rectangle(0, 0, backgroundDef.Width, backgroundDef.Height),
                Color.White
            );

            // Draw outline border AROUND the background (not at same position)
            if (outlineDef.IsTileSheet && outlineDef.TileUsage != null)
            {
                DrawTileSheetBorder(
                    outlineTexture,
                    outlineDef,
                    bgX,
                    bgY,
                    bgWidth,
                    bgHeight,
                    currentScale
                );
            }
            else if (outlineDef.CornerWidth.HasValue && outlineDef.CornerHeight.HasValue)
            {
                DrawLegacyNineSliceBorder(
                    outlineTexture,
                    outlineDef,
                    bgX,
                    bgY,
                    bgWidth,
                    bgHeight,
                    currentScale
                );
            }

            // Load font and render text
            var fontSystem = _fontService.GetFontSystem("base:font:game/pokemon");
            if (fontSystem == null)
            {
                _logger.Warning("Font 'base:font:game/pokemon' not found, cannot render text");
                return;
            }

            // Use scaled font
            int scaledFontSize = _constants.Get<int>("PopupBaseFontSize") * currentScale;
            var font = fontSystem.GetFont(scaledFontSize);
            if (font == null)
            {
                _logger.Warning("Failed to get scaled font, cannot render text");
                return;
            }

            // Truncate text to fit within background width
            string displayText = TruncateTextToFit(
                font,
                popup.MapSectionName,
                bgWidth - (_constants.Get<int>("PopupTextPadding") * 2 * currentScale)
            );

            // Calculate text position (centered horizontally, Y offset from top)
            Vector2 textSize = font.MeasureString(displayText);
            int textOffsetY = _constants.Get<int>("PopupTextOffsetY") * currentScale;
            int shadowOffset = _constants.Get<int>("PopupShadowOffsetX") * currentScale;
            float textX = bgX + ((bgWidth - textSize.X) / 2f); // Center horizontally
            float textY = bgY + textOffsetY;

            // Round to integer positions for crisp pixel-perfect rendering
            int intTextX = (int)Math.Round(textX);
            int intTextY = (int)Math.Round(textY);

            // Draw text shadow first (pokeemerald uses DARK_GRAY shadow)
            int shadowOffsetY = _constants.Get<int>("PopupShadowOffsetY") * currentScale;
            font.DrawText(
                _spriteBatch,
                displayText,
                new Vector2(intTextX + shadowOffset, intTextY + shadowOffsetY),
                new Color(72, 72, 80, 255) // Dark gray shadow, fully opaque
            );

            // Draw main text on top (pokeemerald uses WHITE text)
            font.DrawText(
                _spriteBatch,
                displayText,
                new Vector2(intTextX, intTextY),
                new Color(255, 255, 255, 255) // White text, fully opaque
            );
        }

        /// <summary>
        /// Truncates text to fit within the specified width using binary search.
        /// </summary>
        private string TruncateTextToFit(DynamicSpriteFont font, string text, int maxWidth)
        {
            Vector2 fullSize = font.MeasureString(text);
            if (fullSize.X <= maxWidth)
            {
                return text;
            }

            // Binary search for best fit
            int left = 0;
            int right = text.Length;
            int bestFit = 0;

            while (left <= right)
            {
                int mid = (left + right) / 2;
                string testText = text[..mid];
                Vector2 testSize = font.MeasureString(testText);

                if (testSize.X <= maxWidth)
                {
                    bestFit = mid;
                    left = mid + 1;
                }
                else
                {
                    right = mid - 1;
                }
            }

            return bestFit > 0 ? text[..bestFit] : text;
        }

        /// <summary>
        /// Draws a tile-based border (GBA-accurate pokeemerald style).
        /// Draws the frame AROUND the interior (background) position.
        /// </summary>
        /// <param name="outlineTexture">The outline texture.</param>
        /// <param name="outlineDef">The outline definition.</param>
        /// <param name="interiorX">The X position of the interior (background).</param>
        /// <param name="interiorY">The Y position of the interior (background).</param>
        /// <param name="interiorWidth">The width of the interior (background).</param>
        /// <param name="interiorHeight">The height of the interior (background).</param>
        /// <param name="scale">The viewport scale factor.</param>
        private void DrawTileSheetBorder(
            Texture2D outlineTexture,
            PopupOutlineDefinition outlineDef,
            int interiorX,
            int interiorY,
            int interiorWidth,
            int interiorHeight,
            int scale
        )
        {
            if (
                outlineDef.TileUsage == null
                || outlineDef.Tiles == null
                || outlineDef.Tiles.Count == 0
            )
            {
                return;
            }

            // Validate tiles array
            if (outlineDef.Tiles == null || outlineDef.Tiles.Count == 0)
            {
                _logger.Warning(
                    "Outline definition {OutlineId} has no tiles array or tiles array is empty",
                    outlineDef.Id
                );
                return;
            }

            // Scale tile dimensions to match viewport scaling
            int tileW = outlineDef.TileWidth * scale;
            int tileH = outlineDef.TileHeight * scale;

            // Build a lookup dictionary for tiles by their Index property
            var tileLookup = new Dictionary<int, PopupTileDefinition>();
            foreach (var tile in outlineDef.Tiles)
            {
                if (tile == null)
                {
                    _logger.Warning(
                        "Null tile found in outline definition {OutlineId}",
                        outlineDef.Id
                    );
                    continue;
                }

                if (!tileLookup.ContainsKey(tile.Index))
                {
                    tileLookup[tile.Index] = tile;
                }
                else
                {
                    _logger.Error(
                        "Duplicate tile index {TileIndex} found in outline definition {OutlineId}. "
                            + "This should not happen - check JSON deserialization.",
                        tile.Index,
                        outlineDef.Id
                    );
                }
            }

            if (tileLookup.Count == 0)
            {
                _logger.Error(
                    "No valid tiles found in outline definition {OutlineId} after processing",
                    outlineDef.Id
                );
                return;
            }

            // Helper to draw a single tile by its Index property
            void DrawTile(int tileIndex, int destX, int destY)
            {
                if (!tileLookup.TryGetValue(tileIndex, out var tile))
                {
                    _logger.Debug(
                        "Tile index {TileIndex} not found in outline definition {OutlineId}",
                        tileIndex,
                        outlineDef.Id
                    );
                    return;
                }

                var srcRect = new Rectangle(tile.X, tile.Y, tile.Width, tile.Height);
                var destRect = new Rectangle(destX, destY, tileW, tileH);
                _spriteBatch.Draw(outlineTexture, destRect, srcRect, Color.White);
            }

            var usage = outlineDef.TileUsage;

            // Pokeemerald draws the window interior at (interiorX, interiorY) with size (interiorWidth, interiorHeight)
            // The frame is drawn AROUND it
            // For 80x24 background: deltaX = 10 tiles, deltaY = 3 tiles

            // Draw top edge (12 tiles from x-1 to x+10)
            // This includes the top-left and top-right corner tiles
            for (int i = 0; i < usage.TopEdge.Count && i < 12; i++)
            {
                int tileIndex = usage.TopEdge[i];
                int tileX = interiorX + ((i - 1) * tileW); // i-1 means first tile is at x-1 (pokeemerald: i - 1 + x)
                int tileY = interiorY - tileH; // y-1 in tile units (pokeemerald: y - 1)
                DrawTile(tileIndex, tileX, tileY);
            }

            // Draw left edge (3 tiles at x-1, for y+0, y+1, y+2)
            // Use LeftEdge array if available, otherwise fall back to LeftMiddle
            if (usage.LeftEdge != null && usage.LeftEdge.Count > 0)
            {
                int popupInteriorTilesYLeftEdge = _constants.Get<int>("PopupInteriorTilesY");
                for (int i = 0; i < usage.LeftEdge.Count && i < popupInteriorTilesYLeftEdge; i++)
                {
                    int tileIndex = usage.LeftEdge[i];
                    DrawTile(tileIndex, interiorX - tileW, interiorY + (i * tileH));
                }
            }
            else if (usage.LeftMiddle != 0)
            {
                // Fallback to LeftMiddle for backwards compatibility
                int popupInteriorTilesYLeft = _constants.Get<int>("PopupInteriorTilesY");
                for (int i = 0; i < popupInteriorTilesYLeft; i++)
                {
                    DrawTile(usage.LeftMiddle, interiorX - tileW, interiorY + (i * tileH));
                }
            }

            // Draw right edge (3 tiles at deltaX+x, for y+0, y+1, y+2)
            // Use RightEdge array if available, otherwise fall back to RightMiddle
            if (usage.RightEdge != null && usage.RightEdge.Count > 0)
            {
                int popupInteriorTilesYRight = _constants.Get<int>("PopupInteriorTilesY");
                int popupInteriorTilesX = _constants.Get<int>("PopupInteriorTilesX");
                for (int i = 0; i < usage.RightEdge.Count && i < popupInteriorTilesYRight; i++)
                {
                    int tileIndex = usage.RightEdge[i];
                    DrawTile(
                        tileIndex,
                        interiorX + (popupInteriorTilesX * tileW),
                        interiorY + (i * tileH)
                    );
                }
            }
            else if (usage.RightMiddle != 0)
            {
                // Fallback to RightMiddle for backwards compatibility
                int popupInteriorTilesY = _constants.Get<int>("PopupInteriorTilesY");
                for (int i = 0; i < popupInteriorTilesY; i++)
                {
                    DrawTile(
                        usage.RightMiddle,
                        interiorX + (_constants.Get<int>("PopupInteriorTilesX") * tileW),
                        interiorY + (i * tileH)
                    );
                }
            }

            // Draw bottom edge (12 tiles from x-1 to x+10)
            // This includes the bottom-left and bottom-right corner tiles
            for (int i = 0; i < usage.BottomEdge.Count && i < 12; i++)
            {
                int tileIndex = usage.BottomEdge[i];
                int tileX = interiorX + ((i - 1) * tileW);
                int tileY = interiorY + (_constants.Get<int>("PopupInteriorTilesY") * tileH);
                DrawTile(tileIndex, tileX, tileY);
            }
        }

        /// <summary>
        /// Draws a 9-slice border (legacy rendering for backwards compatibility).
        /// Draws the frame AROUND the interior (background) position.
        /// </summary>
        private void DrawLegacyNineSliceBorder(
            Texture2D outlineTexture,
            PopupOutlineDefinition outlineDef,
            int interiorX,
            int interiorY,
            int interiorWidth,
            int interiorHeight,
            int scale
        )
        {
            if (outlineTexture == null || _spriteBatch == null)
            {
                return;
            }

            // Original texture dimensions (unscaled)
            int srcCornerW = outlineDef.CornerWidth ?? 8;
            int srcCornerH = outlineDef.CornerHeight ?? 8;
            int texWidth = outlineTexture.Width;
            int texHeight = outlineTexture.Height;

            // Scaled destination dimensions
            int destCornerW = srcCornerW * scale;
            int destCornerH = srcCornerH * scale;

            // Calculate source rectangles for 9-slice regions (unscaled texture coordinates)
            var srcTopLeft = new Rectangle(0, 0, srcCornerW, srcCornerH);
            var srcTopRight = new Rectangle(texWidth - srcCornerW, 0, srcCornerW, srcCornerH);
            var srcBottomLeft = new Rectangle(0, texHeight - srcCornerH, srcCornerW, srcCornerH);
            var srcBottomRight = new Rectangle(
                texWidth - srcCornerW,
                texHeight - srcCornerH,
                srcCornerW,
                srcCornerH
            );

            var srcTop = new Rectangle(srcCornerW, 0, texWidth - (srcCornerW * 2), srcCornerH);
            var srcBottom = new Rectangle(
                srcCornerW,
                texHeight - srcCornerH,
                texWidth - (srcCornerW * 2),
                srcCornerH
            );
            var srcLeft = new Rectangle(0, srcCornerH, srcCornerW, texHeight - (srcCornerH * 2));
            var srcRight = new Rectangle(
                texWidth - srcCornerW,
                srcCornerH,
                srcCornerW,
                texHeight - (srcCornerH * 2)
            );

            // Draw corners (scaled destinations, unscaled sources)
            _spriteBatch.Draw(
                outlineTexture,
                new Rectangle(
                    interiorX - destCornerW,
                    interiorY - destCornerH,
                    destCornerW,
                    destCornerH
                ),
                srcTopLeft,
                Color.White
            );
            _spriteBatch.Draw(
                outlineTexture,
                new Rectangle(
                    interiorX + interiorWidth,
                    interiorY - destCornerH,
                    destCornerW,
                    destCornerH
                ),
                srcTopRight,
                Color.White
            );
            _spriteBatch.Draw(
                outlineTexture,
                new Rectangle(
                    interiorX - destCornerW,
                    interiorY + interiorHeight,
                    destCornerW,
                    destCornerH
                ),
                srcBottomLeft,
                Color.White
            );
            _spriteBatch.Draw(
                outlineTexture,
                new Rectangle(
                    interiorX + interiorWidth,
                    interiorY + interiorHeight,
                    destCornerW,
                    destCornerH
                ),
                srcBottomRight,
                Color.White
            );

            // Draw edges (stretched to fill space between corners)
            _spriteBatch.Draw(
                outlineTexture,
                new Rectangle(interiorX, interiorY - destCornerH, interiorWidth, destCornerH),
                srcTop,
                Color.White
            );
            _spriteBatch.Draw(
                outlineTexture,
                new Rectangle(interiorX, interiorY + interiorHeight, interiorWidth, destCornerH),
                srcBottom,
                Color.White
            );
            _spriteBatch.Draw(
                outlineTexture,
                new Rectangle(interiorX - destCornerW, interiorY, destCornerW, interiorHeight),
                srcLeft,
                Color.White
            );
            _spriteBatch.Draw(
                outlineTexture,
                new Rectangle(interiorX + interiorWidth, interiorY, destCornerW, interiorHeight),
                srcRight,
                Color.White
            );
        }

        /// <summary>
        /// Loads a background texture by definition ID, caching it for future use.
        /// </summary>
        /// <param name="definitionId">The background definition ID.</param>
        /// <returns>The loaded texture, or null if loading failed.</returns>
        private Texture2D? LoadBackgroundTexture(string definitionId)
        {
            if (string.IsNullOrEmpty(definitionId))
            {
                return null;
            }

            // Check cache first
            if (_textureCache.TryGetValue(definitionId, out var cachedTexture))
            {
                return cachedTexture;
            }

            _logger.Debug("Loading background texture for definition {DefinitionId}", definitionId);

            // Get definition
            var definition = _modManager.GetDefinition<PopupBackgroundDefinition>(definitionId);
            if (definition == null)
            {
                _logger.Warning("Background definition not found: {DefinitionId}", definitionId);
                return null;
            }

            return LoadTextureFromDefinition(definitionId, definition.TexturePath);
        }

        /// <summary>
        /// Loads an outline texture by definition ID, caching it for future use.
        /// </summary>
        /// <param name="definitionId">The outline definition ID.</param>
        /// <returns>The loaded texture, or null if loading failed.</returns>
        private Texture2D? LoadOutlineTexture(string definitionId)
        {
            if (string.IsNullOrEmpty(definitionId))
            {
                return null;
            }

            // Check cache first
            if (_textureCache.TryGetValue(definitionId, out var cachedTexture))
            {
                return cachedTexture;
            }

            _logger.Debug("Loading outline texture for definition {DefinitionId}", definitionId);

            // Get definition
            var definition = _modManager.GetDefinition<PopupOutlineDefinition>(definitionId);
            if (definition == null)
            {
                _logger.Warning("Outline definition not found: {DefinitionId}", definitionId);
                return null;
            }

            return LoadTextureFromDefinition(definitionId, definition.TexturePath);
        }

        /// <summary>
        /// Loads a texture from a texture path, resolving it through mod manifests.
        /// </summary>
        /// <param name="definitionId">The definition ID (for logging and caching).</param>
        /// <param name="texturePath">The texture path relative to mod root.</param>
        /// <returns>The loaded texture, or null if loading failed.</returns>
        private Texture2D? LoadTextureFromDefinition(string definitionId, string texturePath)
        {
            if (string.IsNullOrEmpty(texturePath))
            {
                _logger.Warning("Definition {DefinitionId} has no TexturePath", definitionId);
                return null;
            }

            // Get metadata
            var metadata = _modManager.GetDefinitionMetadata(definitionId);
            if (metadata == null)
            {
                _logger.Warning("Metadata not found for {DefinitionId}", definitionId);
                return null;
            }

            // Get mod manifest
            var modManifest = _modManager.GetModManifest(metadata.OriginalModId);
            if (modManifest == null)
            {
                _logger.Warning(
                    "Mod manifest not found for {DefinitionId} (mod: {ModId})",
                    definitionId,
                    metadata.OriginalModId
                );
                return null;
            }

            // Resolve texture path
            string fullTexturePath = Path.Combine(modManifest.ModDirectory, texturePath);
            fullTexturePath = Path.GetFullPath(fullTexturePath);

            if (!File.Exists(fullTexturePath))
            {
                _logger.Warning(
                    "Texture file not found: {TexturePath} (definition: {DefinitionId})",
                    fullTexturePath,
                    definitionId
                );
                return null;
            }

            try
            {
                // Load texture from file system
                var texture = Texture2D.FromFile(_graphicsDevice, fullTexturePath);
                _textureCache[definitionId] = texture;
                _logger.Debug(
                    "Loaded texture: {DefinitionId} from {TexturePath}",
                    definitionId,
                    fullTexturePath
                );
                return texture;
            }
            catch (Exception ex)
            {
                _logger.Error(
                    ex,
                    "Failed to load texture: {DefinitionId} from {TexturePath}",
                    definitionId,
                    fullTexturePath
                );
                return null;
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
                    // No event subscriptions to unsubscribe (MapPopupSystem handles lifecycle)
                }
                _disposed = true;
            }
        }
    }
}
