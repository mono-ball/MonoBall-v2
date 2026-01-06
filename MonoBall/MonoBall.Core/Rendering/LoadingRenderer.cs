using System;
using FontStashSharp;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoBall.Core.Resources;
using MonoBall.Core.Scenes.Systems;
using Serilog;

namespace MonoBall.Core.Rendering;

/// <summary>
///     Minimal renderer for loading screen - no ECS, no events, no SystemManager.
///     Used during game initialization before the full SystemManager is created.
///     OWNS its own SpriteBatch instance.
/// </summary>
public sealed class LoadingRenderer : ILoadingRenderer
{
    // Layout constants - matching LoadingSceneSystem for consistency
    private const int ProgressBarWidth = 800;
    private const int ProgressBarHeight = 48;
    private const int ProgressBarPadding = 6;
    private const int ProgressBarBorderThickness = 3;
    private const int FontSize = 36;
    private const int FontSizeMedium = 28;
    private const int FontSizeSmall = 22;
    private const int TitleSpacing = 80;
    private const int StepSpacing = 40;
    private const int ErrorSpacing = 30;

    private readonly GraphicsDevice _graphicsDevice;
    private readonly SpriteBatch _spriteBatch; // OWNED - created internally
    private readonly IResourceManager _resourceManager;
    private readonly ILogger _logger;

    private SpriteFontBase? _font;
    private SpriteFontBase? _fontMedium;
    private SpriteFontBase? _fontSmall;
    private FontSystem? _fontSystem;
    private Texture2D? _pixelTexture;

    private float _progress;
    private string _message = "Loading...";
    private string? _errorMessage;
    private bool _isDisposed;

    /// <summary>
    ///     Initializes a new instance of the LoadingRenderer.
    /// </summary>
    /// <param name="graphicsDevice">The graphics device.</param>
    /// <param name="resourceManager">The resource manager for loading fonts.</param>
    /// <param name="logger">The logger for logging operations.</param>
    public LoadingRenderer(
        GraphicsDevice graphicsDevice,
        IResourceManager resourceManager,
        ILogger logger
    )
    {
        _graphicsDevice = graphicsDevice ?? throw new ArgumentNullException(nameof(graphicsDevice));
        _resourceManager =
            resourceManager ?? throw new ArgumentNullException(nameof(resourceManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Create our own SpriteBatch - we own it, we dispose it
        _spriteBatch = new SpriteBatch(graphicsDevice);

        _logger.Debug("LoadingRenderer created with owned SpriteBatch");
    }

    /// <summary>
    ///     Gets the background color for the loading screen.
    /// </summary>
    public static Color BackgroundColor => LoadingScreenTheme.BackgroundColor;

    /// <inheritdoc />
    public void SetProgress(float progress, string message)
    {
        if (_isDisposed)
            return;

        _progress = Math.Clamp(progress, 0.0f, 1.0f);
        _message = message ?? "Loading...";
    }

    /// <summary>
    ///     Sets an error message to display on the loading screen.
    /// </summary>
    /// <param name="errorMessage">The error message, or null to clear.</param>
    public void SetError(string? errorMessage)
    {
        if (_isDisposed)
            return;
        _errorMessage = errorMessage;
    }

    /// <inheritdoc />
    public void Update(GameTime gameTime)
    {
        // Minimal update - nothing needed for static loading screen
    }

    /// <inheritdoc />
    public void Render(GameTime gameTime)
    {
        if (_isDisposed)
            return;

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

            // Clear to background color
            _graphicsDevice.Clear(LoadingScreenTheme.BackgroundColor);

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

                RenderLoadingScreen();
            }
            finally
            {
                _spriteBatch.End();
            }
        }
        finally
        {
            _graphicsDevice.Viewport = savedViewport;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_isDisposed)
            return;
        _isDisposed = true;

        _logger.Debug("Disposing LoadingRenderer");

        // Dispose pixel texture (we own it)
        _pixelTexture?.Dispose();
        _pixelTexture = null;

        // Dispose SpriteBatch (we own it)
        _spriteBatch.Dispose();

        // Do NOT dispose _fontSystem - it's a shared resource from ResourceManager
        _fontSystem = null;
        _font = null;
        _fontMedium = null;
        _fontSmall = null;

        _logger.Debug("LoadingRenderer disposed");
    }

    /// <summary>
    ///     Loads fonts for rendering loading screen text.
    /// </summary>
    private void LoadFonts()
    {
        _logger.Debug("Loading fonts for loading screen via ResourceManager");

        try
        {
            _fontSystem = _resourceManager.LoadFont("base:font:game/pokemon");
        }
        catch (Exception ex)
        {
            _logger.Warning(
                ex,
                "Could not load font 'base:font:game/pokemon', using fallback rendering"
            );
            return;
        }

        _font = _fontSystem.GetFont(FontSize);
        _fontMedium = _fontSystem.GetFont(FontSizeMedium);
        _fontSmall = _fontSystem.GetFont(FontSizeSmall);

        if (_font == null || _fontMedium == null || _fontSmall == null)
        {
            _logger.Warning("Failed to create fonts from FontSystem, using fallback rendering");
            _fontSystem = null;
        }
        else
        {
            _logger.Debug("Successfully loaded fonts for loading screen");
        }
    }

    /// <summary>
    ///     Renders the loading screen UI elements.
    /// </summary>
    private void RenderLoadingScreen()
    {
        var viewport = _graphicsDevice.Viewport;
        var centerX = viewport.Width / 2;
        var centerY = viewport.Height / 2;

        // Calculate positions
        var barX = centerX - ProgressBarWidth / 2;
        var barY = centerY;

        // Draw progress bar shadow
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
        var fillWidth = (int)((ProgressBarWidth - ProgressBarPadding * 2) * _progress);
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

            // Highlight gradient effect
            var highlightHeight = fillHeight / 3;
            if (highlightHeight > 0)
                DrawRectangle(
                    fillX,
                    fillY,
                    fillWidth,
                    highlightHeight,
                    LoadingScreenTheme.ProgressBarFillHighlight
                );
        }

        // Draw percentage text centered on progress bar
        var percentage = (int)(_progress * 100);
        var percentText = $"{percentage}%";
        if (_fontMedium != null)
        {
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
                _logger.Warning(ex, "Failed to render percentage text");
            }
        }

        // Draw step text above progress bar
        var stepText = string.IsNullOrEmpty(_message) ? "Loading..." : _message;
        if (_fontSmall != null)
        {
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
                _logger.Warning(ex, "Failed to render step text");
            }
        }

        // Draw title with shadow for depth
        const string title = "MonoBall";
        if (_font != null)
        {
            try
            {
                var titleSize = _font.MeasureString(title);
                var titleX = centerX - titleSize.X / 2;
                var titleY = barY - TitleSpacing - (int)titleSize.Y;

                // Draw title shadow
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
                _logger.Warning(ex, "Failed to render title text");
            }
        }

        // Draw error message if any
        if (!string.IsNullOrEmpty(_errorMessage) && _fontSmall != null)
        {
            var errorText = $"Error: {_errorMessage}";
            var errorSize = _fontSmall.MeasureString(errorText);
            var errorX = centerX - errorSize.X / 2;
            var errorY = barY + ProgressBarHeight + ErrorSpacing;

            // Draw error background
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
}
