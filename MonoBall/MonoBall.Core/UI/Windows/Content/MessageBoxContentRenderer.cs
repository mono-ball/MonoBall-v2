using System;
using FontStashSharp;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoBall.Core.Constants;
using MonoBall.Core.Rendering;
using MonoBall.Core.Scenes.Components;
using Serilog;

namespace MonoBall.Core.UI.Windows.Content
{
    /// <summary>
    /// Renders message box text content with scrolling, wrapping, and control codes.
    /// </summary>
    public class MessageBoxContentRenderer : IMessageBoxContentRenderer
    {
        private readonly FontService _fontService;
        private readonly int _scaledFontSize;
        private readonly int _scale;
        private readonly IConstantsService _constants;
        private readonly ILogger _logger;
        private readonly int _maxVisibleLines;

        /// <summary>
        /// Initializes a new instance of the MessageBoxContentRenderer class.
        /// </summary>
        /// <param name="fontService">The font service for loading fonts.</param>
        /// <param name="scaledFontSize">The scaled font size in pixels.</param>
        /// <param name="scale">The viewport scale factor (needed for padding calculations).</param>
        /// <param name="constants">The constants service.</param>
        /// <param name="logger">The logger.</param>
        /// <exception cref="ArgumentNullException">Thrown when fontService, constants, or logger is null.</exception>
        public MessageBoxContentRenderer(
            FontService fontService,
            int scaledFontSize,
            int scale,
            IConstantsService constants,
            ILogger logger
        )
        {
            _fontService = fontService ?? throw new ArgumentNullException(nameof(fontService));
            _scaledFontSize = scaledFontSize;
            _scale = scale;
            _constants = constants ?? throw new ArgumentNullException(nameof(constants));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _maxVisibleLines = constants.Get<int>("MaxVisibleLines");
        }

        /// <summary>
        /// Renders the message box content with the specified component state.
        /// </summary>
        /// <param name="spriteBatch">The sprite batch for rendering.</param>
        /// <param name="messageBox">The message box component (passed by reference for performance).</param>
        /// <param name="x">The X position of the content area.</param>
        /// <param name="y">The Y position of the content area.</param>
        /// <param name="width">The width of the content area.</param>
        /// <param name="height">The height of the content area.</param>
        /// <remarks>
        /// SpriteBatch.Begin() must be called before this method.
        /// </remarks>
        /// <exception cref="ArgumentNullException">Thrown when spriteBatch is null.</exception>
        public void RenderContent(
            SpriteBatch spriteBatch,
            ref MessageBoxComponent messageBox,
            int x,
            int y,
            int width,
            int height
        )
        {
            // Get font
            var fontSystem = _fontService.GetFontSystem(messageBox.FontId);
            if (fontSystem == null)
            {
                _logger.Warning("Font '{FontId}' not found, cannot render text", messageBox.FontId);
                return;
            }

            var font = fontSystem.GetFont(_scaledFontSize);
            if (font == null)
            {
                _logger.Warning("Failed to get scaled font, cannot render text");
                return;
            }

            // Text padding from frame edges (Pokemon uses 1px top, 0px horizontal)
            int textPaddingX = _constants.Get<int>("TextPaddingX") * _scale;
            int textPaddingY = _constants.Get<int>("TextPaddingTop") * _scale;
            int textStartX = x + textPaddingX;
            int textStartY = y + textPaddingY;

            // Render wrapped lines up to CurrentCharIndex, starting from PageStartLine
            if (messageBox.WrappedLines == null || messageBox.WrappedLines.Count == 0)
            {
                return;
            }

            // Calculate scroll offset in scaled pixels (applied during scroll animation)
            int scrollOffsetPixels = (int)(messageBox.ScrollOffset * _scale);

            int lineY = textStartY - scrollOffsetPixels; // Apply scroll offset
            // Line advance = extra spacing + font height (fontSize is already scaled)
            int lineSpacing = (messageBox.LineSpacing * _scale) + _scaledFontSize;
            int lineIndex = 0; // Track which line number we're on
            int linesRendered = 0; // Track how many lines we've rendered on this page

            // Calculate visible bounds for clipping during scroll animation
            int visibleTop = y;
            int visibleBottom = y + height;

            // During scroll animation, render one extra line that's scrolling into view
            int maxLinesToRender = _maxVisibleLines;
            if (messageBox.State == MessageBoxRenderState.Scrolling)
            {
                maxLinesToRender++; // Render one extra line scrolling in from bottom
            }

            foreach (var line in messageBox.WrappedLines)
            {
                // Skip lines before PageStartLine (previous pages)
                if (lineIndex < messageBox.PageStartLine)
                {
                    lineIndex++;
                    continue;
                }

                // Stop if we've rendered max lines for current state
                if (linesRendered >= maxLinesToRender)
                {
                    break;
                }

                // Rendering logic:
                // - Lines that end with newline have EndIndex = last char index + 1 (includes newline)
                // - The last line (no trailing newline) has EndIndex = last char index
                // - CurrentCharIndex advances for both Char and Newline tokens

                if (messageBox.CurrentCharIndex >= line.EndIndex)
                {
                    // Render complete line (processed all visible characters in this line)
                    // Only render if line is within visible bounds (for scroll clipping)
                    bool lineVisible = lineY >= visibleTop && lineY < visibleBottom;
                    if (!string.IsNullOrEmpty(line.Text) && lineVisible)
                    {
                        RenderTextLine(
                            font,
                            spriteBatch,
                            line.Text,
                            textStartX,
                            lineY,
                            messageBox.TextColor,
                            messageBox.ShadowColor
                        );
                    }
                    // Move to next line position (even for empty/clipped lines - preserves spacing)
                    lineY += lineSpacing;
                    lineIndex++;
                    linesRendered++;
                }
                else if (messageBox.CurrentCharIndex > line.StartIndex)
                {
                    // Render partial line (currently being printed)
                    // Only render if line is within visible bounds (for scroll clipping)
                    bool lineVisible = lineY >= visibleTop && lineY < visibleBottom;
                    int substringLength = messageBox.CurrentCharIndex - line.StartIndex;
                    substringLength = Math.Min(substringLength, line.Text.Length);
                    if (substringLength > 0 && lineVisible)
                    {
                        string partialText = line.Text.Substring(0, substringLength);
                        RenderTextLine(
                            font,
                            spriteBatch,
                            partialText,
                            textStartX,
                            lineY,
                            messageBox.TextColor,
                            messageBox.ShadowColor
                        );
                    }
                    // Don't render lines after this one (they haven't been reached yet)
                    break;
                }
                else if (
                    messageBox.CurrentCharIndex == line.StartIndex
                    && string.IsNullOrEmpty(line.Text)
                )
                {
                    // Empty line at exact current position (consecutive newlines)
                    lineY += lineSpacing;
                    lineIndex++;
                    linesRendered++;
                    continue;
                }
                else
                {
                    // Line hasn't been reached yet - stop rendering
                    break;
                }
            }
        }

        /// <summary>
        /// Renders a single line of text with shadow.
        /// </summary>
        /// <param name="font">The font to use.</param>
        /// <param name="spriteBatch">The sprite batch for rendering.</param>
        /// <param name="text">The text to render.</param>
        /// <param name="x">The X position.</param>
        /// <param name="y">The Y position.</param>
        /// <param name="textColor">The text color.</param>
        /// <param name="shadowColor">The shadow color.</param>
        private void RenderTextLine(
            DynamicSpriteFont font,
            SpriteBatch spriteBatch,
            string text,
            int x,
            int y,
            Color textColor,
            Color shadowColor
        )
        {
            // Render shadow first (offset scales with viewport, matching map popup style)
            int shadowOffset = _constants.Get<int>("PopupShadowOffsetY") * _scale;
            font.DrawText(
                spriteBatch,
                text,
                new Vector2(x + shadowOffset, y + shadowOffset),
                shadowColor
            );

            // Render main text on top
            font.DrawText(spriteBatch, text, new Vector2(x, y), textColor);
        }
    }
}
