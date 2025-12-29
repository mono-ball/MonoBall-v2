using System;
using System.Collections.Concurrent;
using Arch.Core;
using Arch.System;
using FontStashSharp;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoBall.Core.ECS;
using MonoBall.Core.Resources;
using MonoBall.Core.Scenes.Components;
using MonoBall.Core.Scenes.Events;
using Serilog;

namespace MonoBall.Core.Scenes.Systems;

/// <summary>
///     System that handles update and rendering for LoadingScene entities.
///     Queries for LoadingSceneComponent entities and processes them.
///     Consolidates loading scene lifecycle, updates, and rendering.
/// </summary>
public class LoadingSceneSystem
    : BaseSystem<World, float>,
        IPrioritizedSystem,
        IDisposable,
        ISceneSystem
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
    private readonly Game _game;

    private readonly GraphicsDevice _graphicsDevice;

    private readonly QueryDescription _loadingProgressQuery = new QueryDescription().WithAll<
        LoadingSceneComponent,
        LoadingProgressComponent
    >();

    // Cached query descriptions to avoid allocations in hot paths
    private readonly QueryDescription _loadingScenesQuery = new QueryDescription().WithAll<
        SceneComponent,
        LoadingSceneComponent
    >();

    private readonly ILogger _logger;

    // Thread-safe queue for progress updates from async initialization tasks
    private readonly ConcurrentQueue<(float progress, string step)> _progressQueue = new();
    private readonly SpriteBatch _spriteBatch;
    private bool _disposed;
    private SpriteFontBase? _font;
    private SpriteFontBase? _fontMedium;
    private SpriteFontBase? _fontSmall;
    private FontSystem? _fontSystem;

    // Rendering resources
    private Texture2D? _pixelTexture;

    /// <summary>
    ///     Initializes a new instance of the LoadingSceneSystem.
    /// </summary>
    /// <param name="world">The ECS world.</param>
    /// <param name="graphicsDevice">The graphics device.</param>
    /// <param name="spriteBatch">The sprite batch for rendering.</param>
    /// <param name="game">The game instance for accessing services.</param>
    /// <param name="logger">The logger for logging operations.</param>
    public LoadingSceneSystem(
        World world,
        GraphicsDevice graphicsDevice,
        SpriteBatch spriteBatch,
        Game game,
        ILogger logger
    )
        : base(world)
    {
        _graphicsDevice = graphicsDevice ?? throw new ArgumentNullException(nameof(graphicsDevice));
        _spriteBatch = spriteBatch ?? throw new ArgumentNullException(nameof(spriteBatch));
        _game = game ?? throw new ArgumentNullException(nameof(game));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    ///     Disposes of the system and clears resources.
    /// </summary>
    public new void Dispose()
    {
        Dispose(true);
    }

    /// <summary>
    ///     Gets the execution priority for this system.
    /// </summary>
    public int Priority => SystemPriority.LoadingScene;

    /// <summary>
    ///     Updates a specific loading scene entity.
    ///     Implements ISceneSystem interface.
    /// </summary>
    /// <param name="sceneEntity">The scene entity to update.</param>
    /// <param name="deltaTime">The elapsed time since last update.</param>
    public void Update(Entity sceneEntity, float deltaTime)
    {
        // LoadingSceneSystem processes progress queue in ProcessInternal()
        // Per-scene updates are not needed - the queue processing handles all scenes
        // This method exists to satisfy ISceneSystem interface
    }

    /// <summary>
    ///     Performs internal processing for loading scenes.
    ///     Processes the progress queue from async initialization tasks.
    ///     Implements ISceneSystem interface.
    /// </summary>
    /// <param name="deltaTime">The elapsed time since last update.</param>
    public void ProcessInternal(float deltaTime)
    {
        // Delegate to the internal Update method that processes the queue
        Update(in deltaTime);
    }

    /// <summary>
    ///     Renders a single loading scene. Called by SceneRendererSystem (coordinator) for a single scene.
    /// </summary>
    /// <param name="sceneEntity">The scene entity to render.</param>
    /// <param name="gameTime">The game time.</param>
    public void RenderScene(Entity sceneEntity, GameTime gameTime)
    {
        if (_disposed)
            return;

        // Verify this is actually a loading scene
        if (!World.Has<LoadingSceneComponent>(sceneEntity))
            return;

        ref var scene = ref World.Get<SceneComponent>(sceneEntity);
        if (!scene.IsActive)
            return;

        // LoadingScene only supports screen-space rendering
        // Note: LoadingSceneSystem.RenderScene() should render even when BlocksDraw=true,
        // because BlocksDraw prevents OTHER scenes from rendering, not the loading scene itself
        if (scene.CameraMode != SceneCameraMode.ScreenCamera)
        {
            _logger.Warning(
                "LoadingScene '{SceneId}' does not support camera-based rendering. Use ScreenCamera mode.",
                scene.SceneId
            );
            return;
        }

        // Render the loading scene
        RenderLoadingScene(sceneEntity, ref scene, gameTime);
    }

    /// <summary>
    ///     Gets the background color for the loading screen.
    /// </summary>
    /// <returns>The background color (light gray theme).</returns>
    public static Color GetBackgroundColor()
    {
        return LoadingScreenTheme.BackgroundColor;
    }

    /// <summary>
    ///     Enqueues a progress update from an async initialization task.
    ///     Thread-safe method that can be called from background threads.
    /// </summary>
    /// <param name="progress">Progress value between 0.0 and 1.0.</param>
    /// <param name="step">Current step description.</param>
    public void EnqueueProgress(float progress, string step)
    {
        if (_disposed)
            return;

        _progressQueue.Enqueue((progress, step ?? "Loading..."));
    }

    /// <summary>
    ///     Updates active, unpaused loading scenes.
    ///     Processes queued progress updates and updates loading progress components.
    /// </summary>
    /// <param name="deltaTime">The elapsed time since last update.</param>
    public override void Update(in float deltaTime)
    {
        if (_disposed)
            return;

        // Query for active, unpaused loading scenes
        // Note: LoadingSceneSystem.Update() should ALWAYS run to process progress queue,
        // even when BlocksUpdate=true, because it needs to update the loading progress component
        World.Query(
            in _loadingScenesQuery,
            (Entity e, ref SceneComponent scene) =>
            {
                if (scene.IsActive && !scene.IsPaused)
                {
                    // Check if loading is already complete - if so, don't process queue
                    // (MarkComplete() may have been called, setting Progress = 1.0f)
                    var isComplete = false;
                    if (World.Has<LoadingProgressComponent>(e))
                    {
                        ref var progressComponent = ref World.Get<LoadingProgressComponent>(e);
                        isComplete = progressComponent.IsComplete;
                    }

                    // Process queued progress updates from async initialization task
                    // This must run even when BlocksUpdate=true, as it's the loading scene's own update
                    // But skip if already complete to avoid overwriting final progress
                    while (!isComplete && _progressQueue.TryDequeue(out var update))
                        if (World.Has<LoadingProgressComponent>(e))
                        {
                            ref var progressComponent = ref World.Get<LoadingProgressComponent>(e);

                            // Double-check IsComplete after dequeuing (MarkComplete may have been called)
                            if (progressComponent.IsComplete)
                            {
                                isComplete = true;
                                // Discard remaining queued updates
                                while (_progressQueue.TryDequeue(out _))
                                {
                                    // Drain queue
                                }

                                break;
                            }

                            progressComponent.Progress = Math.Clamp(update.progress, 0.0f, 1.0f);
                            progressComponent.CurrentStep = update.step ?? "Loading...";

                            // Fire loading progress event for extensibility
                            var progressEvent = new LoadingProgressUpdatedEvent
                            {
                                Progress = progressComponent.Progress,
                                CurrentStep = progressComponent.CurrentStep,
                            };
                            EventBus.Send(ref progressEvent);
                        }
                }
            }
        );
    }

    /// <summary>
    ///     Renders the loading scene in screen space.
    /// </summary>
    /// <param name="sceneEntity">The scene entity.</param>
    /// <param name="scene">The scene component.</param>
    /// <param name="gameTime">The game time.</param>
    private void RenderLoadingScene(Entity sceneEntity, ref SceneComponent scene, GameTime gameTime)
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
                // Load fonts if not already loaded
                if (_fontSystem == null)
                    LoadFonts();

                // Create pixel texture if not already created
                if (_pixelTexture == null)
                {
                    _pixelTexture = new Texture2D(_graphicsDevice, 1, 1);
                    _pixelTexture.SetData(new[] { Color.White });
                }

                // Query for loading progress component
                var foundProgress = false;
                World.Query(
                    in _loadingProgressQuery,
                    (
                        Entity entity,
                        ref LoadingSceneComponent _,
                        ref LoadingProgressComponent progress
                    ) =>
                    {
                        if (foundProgress)
                            return; // Only render first loading scene found

                        foundProgress = true;
                        RenderLoadingScreen(ref progress);
                    }
                );
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
    ///     Loads fonts for rendering loading screen text.
    ///     Requires FontService to be available and core mod to be loaded.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown if FontService is not available or font cannot be loaded.</exception>
    private void LoadFonts()
    {
        var resourceManager = _game.Services.GetService<IResourceManager>();
        if (resourceManager == null)
            throw new InvalidOperationException(
                "IResourceManager is not available. Cannot load fonts for loading screen. "
                    + "Ensure GameServices.LoadContent() was called and ResourceManager is registered in Game.Services."
            );

        _logger.Debug("Loading fonts for loading screen via ResourceManager");

        try
        {
            _fontSystem = resourceManager.LoadFont("base:font:game/pokemon");
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "ResourceManager could not load font 'base:font:game/pokemon'. "
                    + "Ensure core mod (slot 0 in mod.manifest) is loaded and contains the font definition.",
                ex
            );
        }

        _font = _fontSystem.GetFont(FontSize);
        _fontMedium = _fontSystem.GetFont(FontSizeMedium);
        _fontSmall = _fontSystem.GetFont(FontSizeSmall);

        if (_font == null || _fontMedium == null || _fontSmall == null)
            throw new InvalidOperationException(
                $"Failed to create fonts from FontSystem. FontSize: {FontSize}, FontSizeMedium: {FontSizeMedium}, FontSizeSmall: {FontSizeSmall}. "
                    + "FontSystem may not have a font source loaded."
            );

        _logger.Debug("Successfully loaded fonts for loading screen");
    }

    /// <summary>
    ///     Renders the loading screen UI elements.
    /// </summary>
    /// <param name="progress">The loading progress component.</param>
    private void RenderLoadingScreen(ref LoadingProgressComponent progress)
    {
        var viewport = _graphicsDevice.Viewport;
        var centerX = viewport.Width / 2;
        var centerY = viewport.Height / 2;

        // Calculate positions
        var barX = centerX - ProgressBarWidth / 2;
        var barY = centerY;

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
        var fillWidth = (int)((ProgressBarWidth - ProgressBarPadding * 2) * progress.Progress);
        if (fillWidth > 0)
        {
            var fillX = barX + ProgressBarPadding;
            var fillY = barY + ProgressBarPadding;
            var fillHeight = ProgressBarHeight - ProgressBarPadding * 2;

            // Main fill
            DrawRectangle(
                fillX,
                fillY,
                fillWidth,
                fillHeight,
                LoadingScreenTheme.ProgressBarFillColor
            );

            // Highlight gradient effect (top portion lighter)
            var highlightHeight = fillHeight / 3;
            if (highlightHeight > 0 && fillWidth > 0)
                DrawRectangle(
                    fillX,
                    fillY,
                    fillWidth,
                    highlightHeight,
                    LoadingScreenTheme.ProgressBarFillHighlight
                );
        }

        // Draw percentage text centered on progress bar (larger, bold)
        var percentage = (int)(progress.Progress * 100);
        var percentText = $"{percentage}%";
        if (_fontMedium != null)
            try
            {
                var textSize = _fontMedium.MeasureString(percentText);
                var textX = centerX - textSize.X / 2;
                var textY = barY + (ProgressBarHeight - textSize.Y) / 2;
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

        // Draw step text above progress bar
        var stepText = string.IsNullOrEmpty(progress.CurrentStep)
            ? "Loading..."
            : progress.CurrentStep;
        if (_fontSmall != null)
            try
            {
                var stepSize = _fontSmall.MeasureString(stepText);
                var stepX = centerX - stepSize.X / 2;
                var stepY = barY - StepSpacing - stepSize.Y;
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

        // Draw title with shadow for depth
        const string title = "MonoBall";
        if (_font != null)
            try
            {
                var titleSize = _font.MeasureString(title);
                var titleX = centerX - titleSize.X / 2;
                var titleY = barY - TitleSpacing - (int)titleSize.Y;

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

        // Draw error message if any
        if (!string.IsNullOrEmpty(progress.ErrorMessage) && _fontSmall != null)
        {
            var errorText = $"Error: {progress.ErrorMessage}";
            var errorSize = _fontSmall.MeasureString(errorText);
            var errorX = centerX - errorSize.X / 2;
            var errorY = barY + ProgressBarHeight + ErrorSpacing;

            // Draw error background with padding
            var padding = 15;
            DrawRectangle(
                (int)errorX - padding,
                errorY - padding / 2,
                (int)errorSize.X + padding * 2,
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
    }

    /// <summary>
    ///     Draws a filled rectangle.
    /// </summary>
    private void DrawRectangle(int x, int y, int width, int height, Color color)
    {
        if (_pixelTexture == null)
            return;

        _spriteBatch.Draw(_pixelTexture, new Rectangle(x, y, width, height), color);
    }

    /// <summary>
    ///     Draws a rectangle outline.
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
            return;

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
    ///     Disposes of the system and clears resources.
    /// </summary>
    /// <param name="disposing">True if called from Dispose(), false if from finalizer.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed && disposing)
        {
            // Clear progress queue
            while (_progressQueue.TryDequeue(out _))
            {
                // Drain queue
            }

            // Dispose rendering resources
            _pixelTexture?.Dispose();
            _pixelTexture = null;
            // Do NOT dispose _fontSystem - it's a shared resource from FontService
            // FontService manages FontSystem lifecycle and caches it
            _fontSystem = null;
            _font = null;
            _fontMedium = null;
            _fontSmall = null;

            _disposed = true;
        }
    }
}
