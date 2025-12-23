using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Arch.Core;
using Arch.System;
using FontStashSharp;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoBall.Core.ECS.Components;
using MonoBall.Core.Mods;
using MonoBall.Core.Rendering;
using Serilog;

namespace MonoBall.Core.ECS.Systems
{
    /// <summary>
    /// System responsible for rendering map popups (background, outline, text).
    /// Called by SceneRendererSystem when rendering popup scenes.
    /// </summary>
    public class MapPopupRendererSystem : BaseSystem<World, float>
    {
        // GBA-accurate constants (pokeemerald dimensions at 1x scale)
        // Use GameConstants for consistency across systems

        private readonly GraphicsDevice _graphicsDevice;
        private readonly SpriteBatch _spriteBatch;
        private readonly FontService _fontService;
        private readonly IModManager _modManager;
        private readonly ILogger _logger;

        // Texture cache keyed by definition ID
        private readonly Dictionary<string, Texture2D> _textureCache =
            new Dictionary<string, Texture2D>();

        // Cached query description for popup entities
        private readonly QueryDescription _popupQuery = new QueryDescription().WithAll<
            MapPopupComponent,
            PopupAnimationComponent
        >();

        /// <summary>
        /// Initializes a new instance of the MapPopupRendererSystem.
        /// </summary>
        /// <param name="world">The ECS world.</param>
        /// <param name="graphicsDevice">The graphics device.</param>
        /// <param name="spriteBatch">The sprite batch for rendering.</param>
        /// <param name="fontService">The font service for loading fonts.</param>
        /// <param name="modManager">The mod manager for accessing definitions.</param>
        /// <param name="logger">The logger for logging operations.</param>
        public MapPopupRendererSystem(
            World world,
            GraphicsDevice graphicsDevice,
            SpriteBatch spriteBatch,
            FontService fontService,
            IModManager modManager,
            ILogger logger
        )
            : base(world)
        {
            _graphicsDevice =
                graphicsDevice ?? throw new ArgumentNullException(nameof(graphicsDevice));
            _spriteBatch = spriteBatch ?? throw new ArgumentNullException(nameof(spriteBatch));
            _fontService = fontService ?? throw new ArgumentNullException(nameof(fontService));
            _modManager = modManager ?? throw new ArgumentNullException(nameof(modManager));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Renders popups for the specified scene entity.
        /// </summary>
        /// <param name="sceneEntity">The popup scene entity.</param>
        /// <param name="camera">The camera component to use for positioning.</param>
        /// <param name="gameTime">The game time.</param>
        public void Render(Entity sceneEntity, CameraComponent camera, GameTime gameTime)
        {
            // Query for popup entities
            int popupCount = 0;
            World.Query(
                in _popupQuery,
                (Entity entity, ref MapPopupComponent popup, ref PopupAnimationComponent anim) =>
                {
                    popupCount++;
                    _logger.Debug(
                        "Rendering popup entity {EntityId} for '{MapSectionName}' at Y={CurrentY}, State={State}",
                        entity.Id,
                        popup.MapSectionName,
                        anim.CurrentY,
                        anim.State
                    );
                    RenderPopup(entity, ref popup, ref anim, camera);
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
            ref PopupAnimationComponent anim,
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

            // Calculate viewport scale factor (from GBA reference resolution)
            // Use shared utility to ensure consistency with other systems
            int currentScale = CameraTransformUtility.GetViewportScale(
                camera,
                GameConstants.GbaReferenceWidth
            );

            // Calculate scaled dimensions
            int tileSize = outlineDef.IsTileSheet ? outlineDef.TileWidth : 8;
            int borderThickness = tileSize * currentScale;
            int bgWidth = GameConstants.PopupBackgroundWidth * currentScale;
            int bgHeight = GameConstants.PopupBackgroundHeight * currentScale;

            // Calculate popup position in SCREEN SPACE (top-left corner)
            // CurrentY is in screen space (0 = top of screen, negative = above screen)
            int scaledPadding = GameConstants.PopupScreenPadding * currentScale;
            int popupX = scaledPadding; // Top-left corner
            int scaledAnimationY = (int)MathF.Round(anim.CurrentY * currentScale);
            int popupY = scaledAnimationY;

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
            int scaledFontSize = GameConstants.PopupBaseFontSize * currentScale;
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
                bgWidth - (GameConstants.PopupTextPadding * 2 * currentScale)
            );

            // Calculate text position (centered horizontally, Y offset from top)
            Vector2 textSize = font.MeasureString(displayText);
            int textOffsetY = GameConstants.PopupTextOffsetY * currentScale;
            int shadowOffset = GameConstants.PopupShadowOffsetX * currentScale;
            float textX = bgX + ((bgWidth - textSize.X) / 2f); // Center horizontally
            float textY = bgY + textOffsetY;

            // Round to integer positions for crisp pixel-perfect rendering
            int intTextX = (int)Math.Round(textX);
            int intTextY = (int)Math.Round(textY);

            // Draw text shadow first (pokeemerald uses DARK_GRAY shadow)
            int shadowOffsetY = GameConstants.PopupShadowOffsetY * currentScale;
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
                for (
                    int i = 0;
                    i < usage.LeftEdge.Count && i < GameConstants.PopupInteriorTilesY;
                    i++
                )
                {
                    int tileIndex = usage.LeftEdge[i];
                    DrawTile(tileIndex, interiorX - tileW, interiorY + (i * tileH));
                }
            }
            else if (usage.LeftMiddle != 0)
            {
                // Fallback to LeftMiddle for backwards compatibility
                for (int i = 0; i < GameConstants.PopupInteriorTilesY; i++)
                {
                    DrawTile(usage.LeftMiddle, interiorX - tileW, interiorY + (i * tileH));
                }
            }

            // Draw right edge (3 tiles at deltaX+x, for y+0, y+1, y+2)
            // Use RightEdge array if available, otherwise fall back to RightMiddle
            if (usage.RightEdge != null && usage.RightEdge.Count > 0)
            {
                for (
                    int i = 0;
                    i < usage.RightEdge.Count && i < GameConstants.PopupInteriorTilesY;
                    i++
                )
                {
                    int tileIndex = usage.RightEdge[i];
                    DrawTile(
                        tileIndex,
                        interiorX + (GameConstants.PopupInteriorTilesX * tileW),
                        interiorY + (i * tileH)
                    );
                }
            }
            else if (usage.RightMiddle != 0)
            {
                // Fallback to RightMiddle for backwards compatibility
                for (int i = 0; i < GameConstants.PopupInteriorTilesY; i++)
                {
                    DrawTile(
                        usage.RightMiddle,
                        interiorX + (GameConstants.PopupInteriorTilesX * tileW),
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
                int tileY = interiorY + (GameConstants.PopupInteriorTilesY * tileH);
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

            // Find mod manifest
            ModManifest? modManifest = null;
            foreach (var mod in _modManager.LoadedMods)
            {
                if (mod.Id == metadata.OriginalModId)
                {
                    modManifest = mod;
                    break;
                }
            }

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
    }
}
