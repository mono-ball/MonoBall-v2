using System;
using FontStashSharp;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoBall.Core.ECS.Systems;
using MonoBall.Core.Rendering;
using Serilog;

namespace MonoBall.Core.Scenes.Systems
{
    /// <summary>
    /// Render-only system that renders a debug bar with performance statistics at the bottom of the screen.
    /// This is not a BaseSystem because it doesn't query entities from the ECS World.
    /// It only reads pre-computed statistics from PerformanceStatsSystem and renders them.
    /// Called by SceneRendererSystem when rendering DebugBarSceneComponent scenes.
    /// </summary>
    public class DebugBarRendererSystem : IDisposable
    {
        private const int FontSize = 14;
        private const int BarHeight = 24;
        private const int Padding = 8;
        private const int Spacing = 16;

        private readonly GraphicsDevice _graphicsDevice;
        private readonly FontService _fontService;
        private readonly PerformanceStatsSystem _performanceStatsSystem;
        private readonly SpriteBatch _spriteBatch;
        private readonly ILogger _logger;
        private FontSystem? _debugFont;
        private Texture2D? _pixelTexture;

        /// <summary>
        /// Initializes a new instance of the DebugBarRendererSystem.
        /// </summary>
        /// <param name="graphicsDevice">The graphics device.</param>
        /// <param name="fontService">The font service for loading fonts.</param>
        /// <param name="performanceStatsSystem">The performance stats system for reading stats.</param>
        /// <param name="spriteBatch">The sprite batch for rendering.</param>
        /// <param name="logger">The logger for logging operations.</param>
        public DebugBarRendererSystem(
            GraphicsDevice graphicsDevice,
            FontService fontService,
            PerformanceStatsSystem performanceStatsSystem,
            SpriteBatch spriteBatch,
            ILogger logger
        )
        {
            _graphicsDevice =
                graphicsDevice ?? throw new ArgumentNullException(nameof(graphicsDevice));
            _fontService = fontService ?? throw new ArgumentNullException(nameof(fontService));
            _performanceStatsSystem =
                performanceStatsSystem
                ?? throw new ArgumentNullException(nameof(performanceStatsSystem));
            _spriteBatch = spriteBatch ?? throw new ArgumentNullException(nameof(spriteBatch));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Renders the debug bar at the bottom of the screen.
        /// </summary>
        /// <param name="gameTime">The game time.</param>
        public void Render(GameTime gameTime)
        {
            // Load font if not already loaded
            if (_debugFont == null)
            {
                _debugFont = _fontService.GetFontSystem("base:font:debug/mono");
                if (_debugFont == null)
                {
                    _logger.Warning(
                        "Debug font 'base:font:debug/mono' not found. Debug bar will not render."
                    );
                    return;
                }
            }

            var screenWidth = _graphicsDevice.Viewport.Width;
            var screenHeight = _graphicsDevice.Viewport.Height;
            var barY = screenHeight - BarHeight;

            // Create pixel texture if not already created
            if (_pixelTexture == null)
            {
                _pixelTexture = new Texture2D(_graphicsDevice, 1, 1);
                _pixelTexture.SetData(new[] { Color.White });
            }

            // Render background rectangle
            var backgroundRect = new Rectangle(0, barY, screenWidth, BarHeight);
            _spriteBatch.Draw(
                _pixelTexture,
                backgroundRect,
                new Color(0, 0, 0, 200) // Semi-transparent black
            );

            // Get stats
            var fps = _performanceStatsSystem.Fps;
            var frameTime = _performanceStatsSystem.FrameTimeMs;
            var entityCount = _performanceStatsSystem.EntityCount;
            var memoryMb = _performanceStatsSystem.MemoryBytes / (1024.0 * 1024.0);
            var drawCalls = _performanceStatsSystem.DrawCalls;
            var gcGen0 = _performanceStatsSystem.GcGen0;
            var gcGen1 = _performanceStatsSystem.GcGen1;
            var gcGen2 = _performanceStatsSystem.GcGen2;

            // Format stats strings
            var fpsText = $"FPS: {fps:F1}";
            var frameTimeText = $"Frame: {frameTime:F2}ms";
            var entityText = $"Entities: {entityCount}";
            var memoryText = $"Memory: {memoryMb:F2}MB";
            var drawCallsText = $"Draws: {drawCalls}";
            var gcText = $"GC: {gcGen0}/{gcGen1}/{gcGen2}";

            // Get font
            var font = _debugFont.GetFont(FontSize);
            var textColor = Color.White;
            var textY = barY + (BarHeight - FontSize) / 2;

            // Render text
            int x = Padding;
            font.DrawText(_spriteBatch, fpsText, new Vector2(x, textY), textColor);
            x += (int)font.MeasureString(fpsText).X + Spacing;

            font.DrawText(_spriteBatch, frameTimeText, new Vector2(x, textY), textColor);
            x += (int)font.MeasureString(frameTimeText).X + Spacing;

            font.DrawText(_spriteBatch, entityText, new Vector2(x, textY), textColor);
            x += (int)font.MeasureString(entityText).X + Spacing;

            font.DrawText(_spriteBatch, memoryText, new Vector2(x, textY), textColor);
            x += (int)font.MeasureString(memoryText).X + Spacing;

            font.DrawText(_spriteBatch, drawCallsText, new Vector2(x, textY), textColor);
            x += (int)font.MeasureString(drawCallsText).X + Spacing;

            font.DrawText(_spriteBatch, gcText, new Vector2(x, textY), textColor);
        }

        /// <summary>
        /// Disposes resources used by the debug bar renderer.
        /// </summary>
        public void Dispose()
        {
            _pixelTexture?.Dispose();
            _pixelTexture = null;
        }
    }
}
