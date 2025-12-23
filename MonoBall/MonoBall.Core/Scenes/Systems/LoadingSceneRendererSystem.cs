using System;
using System.IO;
using System.Linq;
using Arch.Core;
using Arch.System;
using FontStashSharp;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoBall.Core.Scenes.Components;
using Serilog;

namespace MonoBall.Core.Scenes.Systems
{
    /// <summary>
    /// Render-only system that renders a loading screen with progress bar and current step text.
    /// Called by SceneRendererSystem when rendering LoadingSceneComponent scenes.
    /// </summary>
    public class LoadingSceneRendererSystem : BaseSystem<World, float>, IDisposable
    {
        // Layout constants - Larger, more prominent design
        private const int ProgressBarWidth = 800;
        private const int ProgressBarHeight = 48;
        private const int ProgressBarPadding = 6;
        private const int ProgressBarBorderThickness = 3;
        private const int FontSize = 36; // Larger title font
        private const int FontSizeMedium = 28; // Progress percentage
        private const int FontSizeSmall = 22; // Step text
        private const int TitleSpacing = 80; // Space between title and progress bar
        private const int StepSpacing = 40; // Space between step text and progress bar
        private const int ErrorSpacing = 30; // Space between progress bar and error message

        // Colors - Use LoadingScreenTheme for consistency

        private readonly GraphicsDevice _graphicsDevice;
        private readonly SpriteBatch _spriteBatch;
        private readonly Game _game;
        private readonly ILogger _logger;
        private Texture2D? _pixelTexture;
        private FontSystem? _fontSystem;
        private SpriteFontBase? _font;
        private SpriteFontBase? _fontMedium;
        private SpriteFontBase? _fontSmall;

        // Cached query descriptions to avoid allocations in hot paths
        private readonly QueryDescription _loadingSceneQuery = new QueryDescription().WithAll<
            LoadingSceneComponent,
            LoadingProgressComponent
        >();

        /// <summary>
        /// Initializes a new instance of the LoadingSceneRendererSystem.
        /// </summary>
        /// <param name="world">The ECS world for querying loading scene entities.</param>
        /// <param name="graphicsDevice">The graphics device.</param>
        /// <param name="spriteBatch">The sprite batch for rendering.</param>
        /// <param name="game">The game instance for accessing services.</param>
        /// <param name="logger">The logger for logging operations.</param>
        public LoadingSceneRendererSystem(
            World world,
            GraphicsDevice graphicsDevice,
            SpriteBatch spriteBatch,
            Game game,
            ILogger logger
        )
            : base(world)
        {
            _graphicsDevice =
                graphicsDevice ?? throw new ArgumentNullException(nameof(graphicsDevice));
            _spriteBatch = spriteBatch ?? throw new ArgumentNullException(nameof(spriteBatch));
            _game = game ?? throw new ArgumentNullException(nameof(game));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Gets the background color for the loading screen.
        /// </summary>
        /// <returns>The background color (light gray theme).</returns>
        public static Color GetBackgroundColor()
        {
            return LoadingScreenTheme.BackgroundColor;
        }

        /// <summary>
        /// Renders the loading screen with progress bar and current step text.
        /// </summary>
        /// <param name="gameTime">The game time.</param>
        /// <remarks>
        /// Note: SpriteBatch.Begin() must be called before this method.
        /// SpriteBatch.End() must be called after this method.
        /// </remarks>
        public void Render(GameTime gameTime)
        {
            // Load fonts if not already loaded (must happen before rendering)
            // This will throw if FontService is not available
            if (_fontSystem == null)
            {
                LoadFonts();
            }

            // Create pixel texture if not already created
            if (_pixelTexture == null)
            {
                _pixelTexture = new Texture2D(_graphicsDevice, 1, 1);
                _pixelTexture.SetData(new[] { Color.White });
            }

            // Query for loading scene entity
            bool foundScene = false;
            World.Query(
                in _loadingSceneQuery,
                (
                    Entity entity,
                    ref LoadingSceneComponent _,
                    ref LoadingProgressComponent progress
                ) =>
                {
                    if (foundScene)
                    {
                        return; // Only render first loading scene found
                    }

                    foundScene = true;
                    RenderLoadingScreen(ref progress);
                }
            );
        }

        /// <summary>
        /// Loads fonts for rendering loading screen text.
        /// Requires FontService to be available and core mod to be loaded.
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown if FontService is not available or font cannot be loaded.</exception>
        private void LoadFonts()
        {
            var fontService = _game.Services.GetService<MonoBall.Core.Rendering.FontService>();
            if (fontService == null)
            {
                throw new InvalidOperationException(
                    "FontService is not available. Cannot load fonts for loading screen. "
                        + "Ensure core mod (slot 0 in mod.manifest) is loaded and FontService is registered in Game.Services."
                );
            }

            _logger.Debug("Loading fonts for loading screen via FontService");

            _fontSystem = fontService.GetFontSystem("base:font:game/pokemon");
            if (_fontSystem == null)
            {
                throw new InvalidOperationException(
                    "FontService could not load font 'base:font:game/pokemon'. "
                        + "Ensure core mod (slot 0 in mod.manifest) is loaded and contains the font definition."
                );
            }

            _font = _fontSystem.GetFont(FontSize);
            _fontMedium = _fontSystem.GetFont(FontSizeMedium);
            _fontSmall = _fontSystem.GetFont(FontSizeSmall);

            if (_font == null || _fontMedium == null || _fontSmall == null)
            {
                throw new InvalidOperationException(
                    $"Failed to create fonts from FontSystem. FontSize: {FontSize}, FontSizeMedium: {FontSizeMedium}, FontSizeSmall: {FontSizeSmall}. "
                        + "FontSystem may not have a font source loaded."
                );
            }

            _logger.Debug("Successfully loaded fonts for loading screen");
        }

        /// <summary>
        /// Renders the loading screen UI elements.
        /// Note: SpriteBatch.Begin() must be called before this method.
        /// SpriteBatch.End() must be called after this method.
        /// </summary>
        /// <param name="progress">The loading progress component.</param>
        private void RenderLoadingScreen(ref LoadingProgressComponent progress)
        {
            var viewport = _graphicsDevice.Viewport;
            int centerX = viewport.Width / 2;
            int centerY = viewport.Height / 2;

            // Note: GraphicsDevice.Clear() is called by MonoBallGame.Draw() before rendering
            // Note: SpriteBatch.Begin() is called by SceneRendererSystem before calling Render()
            // We don't call Begin()/End() here to avoid double Begin() errors

            // Calculate positions
            int barX = centerX - (ProgressBarWidth / 2);
            int barY = centerY;

            // Draw progress bar shadow (offset slightly down and right)
            DrawRectangle(
                barX + 4,
                barY + 4,
                ProgressBarWidth,
                ProgressBarHeight,
                LoadingScreenTheme.ProgressBarShadowColor
            );

            // Draw progress bar background
            DrawRectangle(
                barX,
                barY,
                ProgressBarWidth,
                ProgressBarHeight,
                LoadingScreenTheme.ProgressBarBackgroundColor
            );

            // Draw progress bar border
            DrawRectangleOutline(
                barX,
                barY,
                ProgressBarWidth,
                ProgressBarHeight,
                LoadingScreenTheme.ProgressBarBorderColor,
                ProgressBarBorderThickness
            );

            // Draw progress bar fill with gradient effect
            int fillWidth = (int)(
                (ProgressBarWidth - (ProgressBarPadding * 2)) * progress.Progress
            );
            if (fillWidth > 0)
            {
                int fillX = barX + ProgressBarPadding;
                int fillY = barY + ProgressBarPadding;
                int fillHeight = ProgressBarHeight - (ProgressBarPadding * 2);

                // Main fill
                DrawRectangle(
                    fillX,
                    fillY,
                    fillWidth,
                    fillHeight,
                    LoadingScreenTheme.ProgressBarFillColor
                );

                // Highlight gradient effect (top portion lighter)
                int highlightHeight = fillHeight / 3;
                if (highlightHeight > 0 && fillWidth > 0)
                {
                    DrawRectangle(
                        fillX,
                        fillY,
                        fillWidth,
                        highlightHeight,
                        LoadingScreenTheme.ProgressBarFillHighlight
                    );
                }
            }

            // Draw percentage text centered on progress bar (larger, bold)
            int percentage = (int)(progress.Progress * 100);
            string percentText = $"{percentage}%";
            if (_fontMedium != null)
            {
                try
                {
                    Vector2 textSize = _fontMedium.MeasureString(percentText);
                    float textX = centerX - (textSize.X / 2);
                    float textY = barY + ((ProgressBarHeight - textSize.Y) / 2);
                    _fontMedium.DrawText(
                        _spriteBatch,
                        percentText,
                        new Vector2(textX, textY),
                        LoadingScreenTheme.TextColor
                    );
                }
                catch (Exception ex)
                {
                    _logger.Warning(ex, "Failed to render percentage text: {Error}", ex.Message);
                }
            }

            // Draw step text above progress bar
            string stepText = string.IsNullOrEmpty(progress.CurrentStep)
                ? "Loading..."
                : progress.CurrentStep;
            if (_fontSmall != null)
            {
                try
                {
                    Vector2 stepSize = _fontSmall.MeasureString(stepText);
                    float stepX = centerX - (stepSize.X / 2);
                    float stepY = barY - StepSpacing - stepSize.Y;
                    _fontSmall.DrawText(
                        _spriteBatch,
                        stepText,
                        new Vector2(stepX, stepY),
                        LoadingScreenTheme.TextSecondaryColor
                    );
                }
                catch (Exception ex)
                {
                    _logger.Warning(ex, "Failed to render step text: {Error}", ex.Message);
                }
            }

            // Draw title with shadow for depth
            const string title = "MonoBall";
            if (_font != null)
            {
                try
                {
                    Vector2 titleSize = _font.MeasureString(title);
                    float titleX = centerX - (titleSize.X / 2);
                    int titleY = barY - TitleSpacing - (int)titleSize.Y;

                    // Draw title shadow (offset down and right)
                    _font.DrawText(
                        _spriteBatch,
                        title,
                        new Vector2(titleX + 3, titleY + 3),
                        LoadingScreenTheme.TitleShadowColor
                    );

                    // Draw title
                    _font.DrawText(
                        _spriteBatch,
                        title,
                        new Vector2(titleX, titleY),
                        LoadingScreenTheme.TextColor
                    );
                }
                catch (Exception ex)
                {
                    _logger.Warning(ex, "Failed to render title text: {Error}", ex.Message);
                }
            }

            // Draw error message if any
            if (!string.IsNullOrEmpty(progress.ErrorMessage) && _fontSmall != null)
            {
                string errorText = $"Error: {progress.ErrorMessage}";
                Vector2 errorSize = _fontSmall.MeasureString(errorText);
                float errorX = centerX - (errorSize.X / 2);
                int errorY = barY + ProgressBarHeight + ErrorSpacing;

                // Draw error background with padding
                int padding = 15;
                DrawRectangle(
                    (int)errorX - padding,
                    errorY - padding / 2,
                    (int)errorSize.X + (padding * 2),
                    (int)errorSize.Y + padding,
                    LoadingScreenTheme.ErrorBackgroundColor
                );

                // Draw error text
                _fontSmall.DrawText(
                    _spriteBatch,
                    errorText,
                    new Vector2(errorX, errorY),
                    LoadingScreenTheme.ErrorColor
                );
            }

            // Note: SpriteBatch.End() is called by SceneRendererSystem after Render() completes
            // We don't call End() here to avoid double End() errors
        }

        /// <summary>
        /// Draws a filled rectangle.
        /// </summary>
        private void DrawRectangle(int x, int y, int width, int height, Color color)
        {
            if (_pixelTexture == null)
            {
                return;
            }

            _spriteBatch.Draw(_pixelTexture, new Rectangle(x, y, width, height), color);
        }

        /// <summary>
        /// Draws a rectangle outline.
        /// </summary>
        private void DrawRectangleOutline(
            int x,
            int y,
            int width,
            int height,
            Color color,
            int thickness
        )
        {
            if (_pixelTexture == null)
            {
                return;
            }

            // Top
            _spriteBatch.Draw(_pixelTexture, new Rectangle(x, y, width, thickness), color);
            // Bottom
            _spriteBatch.Draw(
                _pixelTexture,
                new Rectangle(x, y + height - thickness, width, thickness),
                color
            );
            // Left
            _spriteBatch.Draw(_pixelTexture, new Rectangle(x, y, thickness, height), color);
            // Right
            _spriteBatch.Draw(
                _pixelTexture,
                new Rectangle(x + width - thickness, y, thickness, height),
                color
            );
        }

        /// <summary>
        /// Update method required by BaseSystem, but rendering is done via Render().
        /// </summary>
        /// <param name="deltaTime">The elapsed time since last update.</param>
        public override void Update(in float deltaTime)
        {
            // Rendering is done via Render() method called from SceneRendererSystem
            // This Update() method is a no-op for renderer system
        }

        /// <summary>
        /// Disposes resources used by the loading scene renderer.
        /// Note: FontSystem is obtained from FontService and is shared/cached, so we do NOT dispose it here.
        /// FontService owns FontSystem lifecycle. Only dispose resources we create ourselves.
        /// </summary>
        public new void Dispose()
        {
            _pixelTexture?.Dispose();
            _pixelTexture = null;
            // Do NOT dispose _fontSystem - it's a shared resource from FontService
            // FontService manages FontSystem lifecycle and caches it
            _fontSystem = null;
            _font = null;
            _fontMedium = null;
            _fontSmall = null;
        }
    }
}
