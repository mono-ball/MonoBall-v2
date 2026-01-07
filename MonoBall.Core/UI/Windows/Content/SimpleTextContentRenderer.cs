using System;
using FontStashSharp;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoBall.Core.Constants;
using MonoBall.Core.Resources;
using Serilog;

namespace MonoBall.Core.UI.Windows.Content;

/// <summary>
///     Renders simple centered text content (for map popups).
/// </summary>
public class SimpleTextContentRenderer : IContentRenderer
{
    private readonly IConstantsService _constants;
    private readonly string _fontId;
    private readonly ILogger _logger;
    private readonly IResourceManager _resourceManager;
    private readonly int _scale;
    private readonly int _scaledFontSize;
    private readonly Color _shadowColor;
    private readonly string _text;
    private readonly Color _textColor;

    /// <summary>
    ///     Initializes a new instance of the SimpleTextContentRenderer class.
    /// </summary>
    /// <param name="resourceManager">The resource manager for loading fonts.</param>
    /// <param name="fontId">The font identifier.</param>
    /// <param name="text">The text to render.</param>
    /// <param name="textColor">The text color.</param>
    /// <param name="shadowColor">The shadow color.</param>
    /// <param name="scaledFontSize">The scaled font size in pixels.</param>
    /// <param name="scale">The viewport scale factor (needed for padding calculations).</param>
    /// <param name="constants">The constants service.</param>
    /// <param name="logger">The logger.</param>
    /// <exception cref="ArgumentNullException">Thrown when resourceManager, fontId, constants, or logger is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when font is not found.</exception>
    public SimpleTextContentRenderer(
        IResourceManager resourceManager,
        string fontId,
        string text,
        Color textColor,
        Color shadowColor,
        int scaledFontSize,
        int scale,
        IConstantsService constants,
        ILogger logger
    )
    {
        _resourceManager =
            resourceManager ?? throw new ArgumentNullException(nameof(resourceManager));
        _fontId = fontId ?? throw new ArgumentNullException(nameof(fontId));
        _text = text ?? throw new ArgumentNullException(nameof(text));
        _textColor = textColor;
        _shadowColor = shadowColor;
        _scaledFontSize = scaledFontSize;
        _scale = scale;
        _constants = constants ?? throw new ArgumentNullException(nameof(constants));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Validate font exists
        try
        {
            var fontSystem = _resourceManager.LoadFont(_fontId);
            // Font loaded successfully
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Font '{_fontId}' not found. Cannot create content renderer without valid font.",
                ex
            );
        }
    }

    /// <summary>
    ///     Renders the window content within the specified bounds.
    /// </summary>
    /// <param name="spriteBatch">The sprite batch for rendering.</param>
    /// <param name="x">The X position of the content area.</param>
    /// <param name="y">The Y position of the content area.</param>
    /// <param name="width">The width of the content area.</param>
    /// <param name="height">The height of the content area.</param>
    /// <remarks>
    ///     SpriteBatch.Begin() must be called before this method.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when spriteBatch is null.</exception>
    public void RenderContent(SpriteBatch spriteBatch, int x, int y, int width, int height)
    {
        // Get font (validated in constructor, but font system may change at runtime)
        FontSystem fontSystem;
        try
        {
            fontSystem = _resourceManager.LoadFont(_fontId);
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Font '{FontId}' not found, cannot render text", _fontId);
            return;
        }

        var font = fontSystem.GetFont(_scaledFontSize);
        if (font == null)
        {
            _logger.Warning("Failed to get scaled font, cannot render text");
            return;
        }

        // Truncate text to fit within content width
        var textPadding = _constants.Get<int>("PopupTextPadding") * _scale;
        var displayText = TruncateTextToFit(font, _text, width - textPadding * 2);

        // Calculate text position (centered horizontally, Y offset from top)
        var textSize = font.MeasureString(displayText);
        var textOffsetY = _constants.Get<int>("PopupTextOffsetY") * _scale;
        var shadowOffset = _constants.Get<int>("PopupShadowOffsetX") * _scale;
        var textX = x + (width - textSize.X) / 2f; // Center horizontally
        float textY = y + textOffsetY;

        // Round to integer positions for crisp pixel-perfect rendering
        var intTextX = (int)Math.Round(textX);
        var intTextY = (int)Math.Round(textY);

        // Draw text shadow first (pokeemerald uses DARK_GRAY shadow)
        var shadowOffsetY = _constants.Get<int>("PopupShadowOffsetY") * _scale;
        font.DrawText(
            spriteBatch,
            displayText,
            new Vector2(intTextX + shadowOffset, intTextY + shadowOffsetY),
            _shadowColor
        );

        // Draw main text on top (pokeemerald uses WHITE text)
        font.DrawText(spriteBatch, displayText, new Vector2(intTextX, intTextY), _textColor);
    }

    /// <summary>
    ///     Truncates text to fit within the specified width using binary search.
    /// </summary>
    /// <param name="font">The font to use for measurement.</param>
    /// <param name="text">The text to truncate.</param>
    /// <param name="maxWidth">The maximum width in pixels.</param>
    /// <returns>The truncated text.</returns>
    private string TruncateTextToFit(DynamicSpriteFont font, string text, int maxWidth)
    {
        var fullSize = font.MeasureString(text);
        if (fullSize.X <= maxWidth)
            return text;

        // Binary search for best fit
        var left = 0;
        var right = text.Length;
        var bestFit = 0;

        while (left <= right)
        {
            var mid = (left + right) / 2;
            var testText = text[..mid];
            var testSize = font.MeasureString(testText);

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
}
