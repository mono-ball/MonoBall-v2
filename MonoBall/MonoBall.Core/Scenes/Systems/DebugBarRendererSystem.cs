using System;
using Arch.Core;
using Arch.System;
using FontStashSharp;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoBall.Core.ECS.Components;
using MonoBall.Core.ECS.Systems;
using MonoBall.Core.Resources;
using Serilog;

namespace MonoBall.Core.Scenes.Systems
{
    /// <summary>
    /// Render-only system that renders a debug bar with performance statistics at the bottom of the screen.
    /// Queries player and map entities to display player location and map ID.
    /// Called by SceneRendererSystem when rendering DebugBarSceneComponent scenes.
    /// </summary>
    public class DebugBarRendererSystem : BaseSystem<World, float>, IDisposable
    {
        private const int FontSize = 14;
        private const int BarHeight = 24;
        private const int Padding = 8;
        private const int Spacing = 16;
        private readonly GraphicsDevice _graphicsDevice;
        private readonly IResourceManager _resourceManager;
        private readonly PerformanceStatsSystem _performanceStatsSystem;
        private readonly SpriteBatch _spriteBatch;
        private readonly ILogger _logger;
        private FontSystem? _debugFont;
        private Texture2D? _pixelTexture;

        // Cached query descriptions to avoid allocations in hot paths
        private readonly QueryDescription _playerQuery = new QueryDescription().WithAll<
            PlayerComponent,
            PositionComponent
        >();

        private readonly QueryDescription _mapQuery = new QueryDescription().WithAll<
            MapComponent,
            PositionComponent
        >();

        /// <summary>
        /// Initializes a new instance of the DebugBarRendererSystem.
        /// </summary>
        /// <param name="world">The ECS world for querying player and map entities.</param>
        /// <param name="graphicsDevice">The graphics device.</param>
        /// <param name="resourceManager">The resource manager for loading fonts.</param>
        /// <param name="performanceStatsSystem">The performance stats system for reading stats.</param>
        /// <param name="spriteBatch">The sprite batch for rendering.</param>
        /// <param name="logger">The logger for logging operations.</param>
        public DebugBarRendererSystem(
            World world,
            GraphicsDevice graphicsDevice,
            IResourceManager resourceManager,
            PerformanceStatsSystem performanceStatsSystem,
            SpriteBatch spriteBatch,
            ILogger logger
        )
            : base(world)
        {
            _graphicsDevice =
                graphicsDevice ?? throw new ArgumentNullException(nameof(graphicsDevice));
            _resourceManager =
                resourceManager ?? throw new ArgumentNullException(nameof(resourceManager));
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
                try
                {
                    _debugFont = _resourceManager.LoadFont("base:font:debug/mono");
                }
                catch (Exception ex)
                {
                    _logger.Warning(
                        ex,
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
            _performanceStatsSystem.IncrementDrawCalls();

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

            // Get font (with null safety check)
            var font = _debugFont?.GetFont(FontSize);
            if (font == null)
            {
                _logger.Warning(
                    "Failed to get font from FontSystem. Debug bar will not render text."
                );
                return;
            }

            var textColor = Color.White;
            var textY = barY + (BarHeight - FontSize) / 2;

            // Render text (each DrawText call is a draw call)
            int x = Padding;
            font.DrawText(_spriteBatch, fpsText, new Vector2(x, textY), textColor);
            _performanceStatsSystem.IncrementDrawCalls();
            x += (int)font.MeasureString(fpsText).X + Spacing;

            font.DrawText(_spriteBatch, frameTimeText, new Vector2(x, textY), textColor);
            _performanceStatsSystem.IncrementDrawCalls();
            x += (int)font.MeasureString(frameTimeText).X + Spacing;

            font.DrawText(_spriteBatch, entityText, new Vector2(x, textY), textColor);
            _performanceStatsSystem.IncrementDrawCalls();
            x += (int)font.MeasureString(entityText).X + Spacing;

            font.DrawText(_spriteBatch, memoryText, new Vector2(x, textY), textColor);
            _performanceStatsSystem.IncrementDrawCalls();
            x += (int)font.MeasureString(memoryText).X + Spacing;

            font.DrawText(_spriteBatch, drawCallsText, new Vector2(x, textY), textColor);
            _performanceStatsSystem.IncrementDrawCalls();
            x += (int)font.MeasureString(drawCallsText).X + Spacing;

            font.DrawText(_spriteBatch, gcText, new Vector2(x, textY), textColor);
            _performanceStatsSystem.IncrementDrawCalls();
            x += (int)font.MeasureString(gcText).X + Spacing;

            // Get player location and map ID
            var (playerX, playerY, mapId) = GetPlayerLocation();
            var locationText = $"Player: ({playerX}, {playerY})";
            var mapText = $"Map: {mapId}";

            font.DrawText(_spriteBatch, locationText, new Vector2(x, textY), textColor);
            _performanceStatsSystem.IncrementDrawCalls();
            x += (int)font.MeasureString(locationText).X + Spacing;

            font.DrawText(_spriteBatch, mapText, new Vector2(x, textY), textColor);
            _performanceStatsSystem.IncrementDrawCalls();
        }

        /// <summary>
        /// Gets the player's current location (grid coordinates) and map ID.
        /// </summary>
        /// <returns>A tuple containing player X, Y grid coordinates and map ID. Returns (0, 0, "N/A") if player not found.</returns>
        /// <remarks>
        /// If multiple maps overlap and contain the player, returns the first map found.
        /// This is typically not an issue as maps are usually non-overlapping.
        /// </remarks>
        private (int x, int y, string mapId) GetPlayerLocation()
        {
            int playerX = 0;
            int playerY = 0;
            Vector2 playerPixelPos = Vector2.Zero;
            bool foundPlayer = false;

            // Query for player entity
            World.Query(
                in _playerQuery,
                (Entity entity, ref PlayerComponent player, ref PositionComponent position) =>
                {
                    playerX = position.X;
                    playerY = position.Y;
                    playerPixelPos = new Vector2(position.PixelX, position.PixelY);
                    foundPlayer = true;
                }
            );

            if (!foundPlayer)
            {
                return (0, 0, "N/A");
            }

            // Find which map contains the player (return first match if multiple maps overlap)
            string mapId = "N/A";

            World.Query(
                in _mapQuery,
                (Entity entity, ref MapComponent map, ref PositionComponent mapPosition) =>
                {
                    // If we already found a map, skip remaining maps (return first match)
                    if (mapId != "N/A")
                    {
                        return;
                    }

                    // Calculate map bounds in pixels
                    float mapLeft = mapPosition.Position.X;
                    float mapTop = mapPosition.Position.Y;
                    float mapRight = mapLeft + (map.Width * map.TileWidth);
                    float mapBottom = mapTop + (map.Height * map.TileHeight);

                    // Check if player is within map bounds
                    if (
                        playerPixelPos.X >= mapLeft
                        && playerPixelPos.X < mapRight
                        && playerPixelPos.Y >= mapTop
                        && playerPixelPos.Y < mapBottom
                    )
                    {
                        mapId = map.MapId;
                    }
                }
            );

            return (playerX, playerY, mapId);
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
        /// Disposes resources used by the debug bar renderer.
        /// </summary>
        public new void Dispose()
        {
            _pixelTexture?.Dispose();
            _pixelTexture = null;
        }
    }
}
