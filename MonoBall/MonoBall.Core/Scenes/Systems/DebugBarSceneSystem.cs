using System;
using Arch.Core;
using Arch.System;
using FontStashSharp;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoBall.Core.ECS;
using MonoBall.Core.ECS.Components;
using MonoBall.Core.ECS.Systems;
using MonoBall.Core.Resources;
using MonoBall.Core.Scenes.Components;
using Serilog;

namespace MonoBall.Core.Scenes.Systems;

/// <summary>
///     System that handles update and rendering for DebugBarScene entities.
///     Queries for DebugBarSceneComponent entities and processes them.
///     Consolidates debug bar lifecycle, updates, and rendering.
/// </summary>
public class DebugBarSceneSystem
    : BaseSystem<World, float>,
        IPrioritizedSystem,
        IDisposable,
        ISceneSystem
{
    private const int FontSize = 14;
    private const int BarHeight = 24;
    private const int Padding = 8;
    private const int Spacing = 16;

    // Cached query descriptions to avoid allocations in hot paths
    private readonly QueryDescription _debugBarScenesQuery = new QueryDescription().WithAll<
        SceneComponent,
        DebugBarSceneComponent
    >();

    private readonly GraphicsDevice _graphicsDevice;
    private readonly ILogger _logger;

    private readonly QueryDescription _mapQuery = new QueryDescription().WithAll<
        MapComponent,
        PositionComponent
    >();

    private readonly PerformanceStatsSystem _performanceStatsSystem;

    private readonly QueryDescription _playerQuery = new QueryDescription().WithAll<
        PlayerComponent,
        PositionComponent
    >();

    private readonly IResourceManager _resourceManager;
    private readonly SpriteBatch _spriteBatch;

    // Rendering resources
    private FontSystem? _debugFont;
    private bool _disposed;
    private Texture2D? _pixelTexture;

    /// <summary>
    ///     Initializes a new instance of the DebugBarSceneSystem.
    /// </summary>
    /// <param name="world">The ECS world.</param>
    /// <param name="graphicsDevice">The graphics device.</param>
    /// <param name="spriteBatch">The sprite batch for rendering.</param>
    /// <param name="resourceManager">The resource manager for loading fonts.</param>
    /// <param name="performanceStatsSystem">The performance stats system for reading stats.</param>
    /// <param name="logger">The logger for logging operations.</param>
    public DebugBarSceneSystem(
        World world,
        GraphicsDevice graphicsDevice,
        SpriteBatch spriteBatch,
        IResourceManager resourceManager,
        PerformanceStatsSystem performanceStatsSystem,
        ILogger logger
    )
        : base(world)
    {
        _graphicsDevice = graphicsDevice ?? throw new ArgumentNullException(nameof(graphicsDevice));
        _spriteBatch = spriteBatch ?? throw new ArgumentNullException(nameof(spriteBatch));
        _resourceManager =
            resourceManager ?? throw new ArgumentNullException(nameof(resourceManager));
        _performanceStatsSystem =
            performanceStatsSystem
            ?? throw new ArgumentNullException(nameof(performanceStatsSystem));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    ///     Disposes resources used by the debug bar system.
    /// </summary>
    public new void Dispose()
    {
        Dispose(true);
    }

    /// <summary>
    ///     Gets the execution priority for this system.
    /// </summary>
    public int Priority => SystemPriority.DebugBarScene;

    /// <summary>
    ///     Updates a specific debug bar scene entity.
    ///     Implements ISceneSystem interface.
    /// </summary>
    /// <param name="sceneEntity">The scene entity to update.</param>
    /// <param name="deltaTime">The elapsed time since last update.</param>
    public void Update(Entity sceneEntity, float deltaTime)
    {
        // Debug bar scenes typically don't need per-scene updates
        // This method exists to satisfy ISceneSystem interface
    }

    /// <summary>
    ///     Performs internal processing for debug bar scenes.
    ///     Implements ISceneSystem interface.
    /// </summary>
    /// <param name="deltaTime">The elapsed time since last update.</param>
    public void ProcessInternal(float deltaTime)
    {
        // Debug bar scenes don't need internal processing
    }

    /// <summary>
    ///     Renders a single debug bar scene. Called by SceneRendererSystem (coordinator) for a single scene.
    /// </summary>
    /// <param name="sceneEntity">The scene entity to render.</param>
    /// <param name="gameTime">The game time.</param>
    public void RenderScene(Entity sceneEntity, GameTime gameTime)
    {
        // Verify this is actually a debug bar scene
        if (!World.Has<DebugBarSceneComponent>(sceneEntity))
            return;

        ref var scene = ref World.Get<SceneComponent>(sceneEntity);
        if (!scene.IsActive)
            return;

        // DebugBarScene only supports screen-space rendering
        if (scene.CameraMode != SceneCameraMode.ScreenCamera)
        {
            _logger.Warning(
                "DebugBarScene '{SceneId}' does not support camera-based rendering. Use ScreenCamera mode.",
                scene.SceneId
            );
            return;
        }

        // Render the debug bar scene
        RenderDebugBarScene(sceneEntity, ref scene, gameTime);
    }

    /// <summary>
    ///     Updates active, unpaused debug bar scenes.
    /// </summary>
    /// <param name="deltaTime">The elapsed time since last update.</param>
    public override void Update(in float deltaTime)
    {
        // Query for active, unpaused debug bar scenes
        World.Query(
            in _debugBarScenesQuery,
            (Entity e, ref SceneComponent scene) =>
            {
                if (scene.IsActive && !scene.IsPaused && !scene.BlocksUpdate)
                {
                    // Debug bar scenes typically don't need per-frame updates
                    // But if they do, add logic here
                }
            }
        );
    }

    /// <summary>
    ///     Renders the debug bar scene in screen space.
    /// </summary>
    /// <param name="sceneEntity">The scene entity.</param>
    /// <param name="scene">The scene component.</param>
    /// <param name="gameTime">The game time.</param>
    private void RenderDebugBarScene(
        Entity sceneEntity,
        ref SceneComponent scene,
        GameTime gameTime
    )
    {
        // Save original viewport
        var savedViewport = _graphicsDevice.Viewport;

        try
        {
            // Set viewport to full window
            _graphicsDevice.Viewport = new Viewport(
                0,
                0,
                _graphicsDevice.Viewport.Width,
                _graphicsDevice.Viewport.Height
            );

            // Begin SpriteBatch with identity matrix for screen-space rendering
            _spriteBatch.Begin(
                SpriteSortMode.Deferred,
                BlendState.AlphaBlend,
                SamplerState.PointClamp,
                DepthStencilState.None,
                RasterizerState.CullCounterClockwise,
                null,
                Matrix.Identity
            );

            try
            {
                // Render debug bar
                RenderDebugBar(gameTime);
            }
            finally
            {
                // Always End SpriteBatch, even if Render() throws an exception
                _spriteBatch.End();
            }
        }
        finally
        {
            // Always restore viewport, even if rendering fails
            _graphicsDevice.Viewport = savedViewport;
        }
    }

    /// <summary>
    ///     Renders the debug bar at the bottom of the screen.
    /// </summary>
    /// <param name="gameTime">The game time.</param>
    private void RenderDebugBar(GameTime gameTime)
    {
        // Load font if not already loaded
        if (_debugFont == null)
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
            _logger.Warning("Failed to get font from FontSystem. Debug bar will not render text.");
            return;
        }

        var textColor = Color.White;
        var textY = barY + (BarHeight - FontSize) / 2;

        // Render text (each DrawText call is a draw call)
        var x = Padding;
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
    ///     Gets the player's current location (grid coordinates) and map ID.
    /// </summary>
    /// <returns>A tuple containing player X, Y grid coordinates and map ID. Returns (0, 0, "N/A") if player not found.</returns>
    /// <remarks>
    ///     If multiple maps overlap and contain the player, returns the first map found.
    ///     This is typically not an issue as maps are usually non-overlapping.
    /// </remarks>
    private (int x, int y, string mapId) GetPlayerLocation()
    {
        var playerX = 0;
        var playerY = 0;
        var playerPixelPos = Vector2.Zero;
        var foundPlayer = false;

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
            return (0, 0, "N/A");

        // Find which map contains the player (return first match if multiple maps overlap)
        var mapId = "N/A";

        World.Query(
            in _mapQuery,
            (Entity entity, ref MapComponent map, ref PositionComponent mapPosition) =>
            {
                // If we already found a map, skip remaining maps (return first match)
                if (mapId != "N/A")
                    return;

                // Calculate map bounds in pixels
                var mapLeft = mapPosition.Position.X;
                var mapTop = mapPosition.Position.Y;
                var mapRight = mapLeft + map.Width * map.TileWidth;
                var mapBottom = mapTop + map.Height * map.TileHeight;

                // Check if player is within map bounds
                if (
                    playerPixelPos.X >= mapLeft
                    && playerPixelPos.X < mapRight
                    && playerPixelPos.Y >= mapTop
                    && playerPixelPos.Y < mapBottom
                )
                    mapId = map.MapId;
            }
        );

        return (playerX, playerY, mapId);
    }

    /// <summary>
    ///     Disposes resources used by the debug bar system.
    /// </summary>
    /// <param name="disposing">True if called from Dispose(), false if from finalizer.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed && disposing)
        {
            _pixelTexture?.Dispose();
            _pixelTexture = null;
            // Do NOT dispose _debugFont - it's a shared resource from FontService
            _debugFont = null;
            _disposed = true;
        }
    }
}
