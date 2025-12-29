using System;
using FontStashSharp;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoBall.Core.Constants;
using MonoBall.Core.Mods;
using MonoBall.Core.Resources;
using MonoBall.Core.Scenes.Components;
using MonoBall.Core.TextEffects;
using Serilog;

namespace MonoBall.Core.UI.Windows.Content;

/// <summary>
///     Renders message box text content with scrolling, wrapping, and control codes.
/// </summary>
public class MessageBoxContentRenderer : IMessageBoxContentRenderer
{
    private readonly IConstantsService _constants;
    private readonly ILogger _logger;
    private readonly int _maxVisibleLines;
    private readonly IModManager? _modManager;
    private readonly IResourceManager _resourceManager;
    private readonly int _scale;
    private readonly int _scaledFontSize;
    private readonly ITextEffectCalculator? _textEffectCalculator;

    /// <summary>
    ///     Initializes a new instance of the MessageBoxContentRenderer class.
    /// </summary>
    /// <param name="resourceManager">The resource manager for loading fonts.</param>
    /// <param name="scaledFontSize">The scaled font size in pixels.</param>
    /// <param name="scale">The viewport scale factor (needed for padding calculations).</param>
    /// <param name="constants">The constants service.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="textEffectCalculator">Optional text effect calculator for animated effects.</param>
    /// <param name="modManager">Optional mod manager for loading effect definitions.</param>
    /// <exception cref="ArgumentNullException">Thrown when resourceManager, constants, or logger is null.</exception>
    public MessageBoxContentRenderer(
        IResourceManager resourceManager,
        int scaledFontSize,
        int scale,
        IConstantsService constants,
        ILogger logger,
        ITextEffectCalculator? textEffectCalculator = null,
        IModManager? modManager = null
    )
    {
        _resourceManager =
            resourceManager ?? throw new ArgumentNullException(nameof(resourceManager));
        _scaledFontSize = scaledFontSize;
        _scale = scale;
        _constants = constants ?? throw new ArgumentNullException(nameof(constants));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _maxVisibleLines = constants.Get<int>("MaxVisibleLines");
        _textEffectCalculator = textEffectCalculator;
        _modManager = modManager;
    }

    /// <summary>
    ///     Gets whether text effects can be rendered (both calculator and mod manager are available).
    /// </summary>
    private bool CanRenderEffects => _textEffectCalculator != null && _modManager != null;

    /// <summary>
    ///     Renders the message box content with the specified component state.
    /// </summary>
    /// <param name="spriteBatch">The sprite batch for rendering.</param>
    /// <param name="messageBox">The message box component (passed by reference for performance).</param>
    /// <param name="x">The X position of the content area.</param>
    /// <param name="y">The Y position of the content area.</param>
    /// <param name="width">The width of the content area.</param>
    /// <param name="height">The height of the content area.</param>
    /// <remarks>
    ///     SpriteBatch.Begin() must be called before this method.
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
        FontSystem fontSystem;
        try
        {
            fontSystem = _resourceManager.LoadFont(messageBox.FontId);
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Font '{FontId}' not found, cannot render text", messageBox.FontId);
            return;
        }

        var font = fontSystem.GetFont(_scaledFontSize);
        if (font == null)
        {
            _logger.Warning("Failed to get scaled font, cannot render text");
            return;
        }

        // Text padding from frame edges (Pokemon uses 1px top, 0px horizontal)
        var textPaddingX = _constants.Get<int>("TextPaddingX") * _scale;
        var textPaddingY = _constants.Get<int>("TextPaddingTop") * _scale;
        var textStartX = x + textPaddingX;
        var textStartY = y + textPaddingY;

        // Render wrapped lines up to CurrentCharIndex, starting from PageStartLine
        if (messageBox.WrappedLines == null || messageBox.WrappedLines.Count == 0)
            return;

        // Calculate scroll offset in scaled pixels (applied during scroll animation)
        var scrollOffsetPixels = (int)(messageBox.ScrollOffset * _scale);

        var lineY = textStartY - scrollOffsetPixels; // Apply scroll offset
        // Line advance = extra spacing + font height (fontSize is already scaled)
        var lineSpacing = messageBox.LineSpacing * _scale + _scaledFontSize;
        var lineIndex = 0; // Track which line number we're on
        var linesRendered = 0; // Track how many lines we've rendered on this page

        // Calculate visible bounds for clipping during scroll animation
        var visibleTop = y;
        var visibleBottom = y + height;

        // During scroll animation, render one extra line that's scrolling into view
        var maxLinesToRender = _maxVisibleLines;
        if (messageBox.State == MessageBoxRenderState.Scrolling)
            maxLinesToRender++; // Render one extra line scrolling in from bottom

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
                break;

            // Rendering logic:
            // - Lines that end with newline have EndIndex = last char index + 1 (includes newline)
            // - The last line (no trailing newline) has EndIndex = last char index
            // - CurrentCharIndex advances for both Char and Newline tokens

            if (messageBox.CurrentCharIndex >= line.EndIndex)
            {
                // Render complete line (processed all visible characters in this line)
                // Only render if line is within visible bounds (for scroll clipping)
                var lineVisible = lineY >= visibleTop && lineY < visibleBottom;
                if (!string.IsNullOrEmpty(line.Text) && lineVisible)
                {
                    // Check if line has effects (pre-calculated during parsing) and we have effect support
                    var canRenderEffects =
                        line.HasEffects && line.CharacterData != null && CanRenderEffects;

                    if (canRenderEffects)
                        // Per-character rendering with effects
                        RenderTextLineWithEffects(
                            font,
                            spriteBatch,
                            line,
                            textStartX,
                            lineY,
                            ref messageBox
                        );
                    else
                        // Fast path: render entire line at once
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
                var lineVisible = lineY >= visibleTop && lineY < visibleBottom;
                var substringLength = messageBox.CurrentCharIndex - line.StartIndex;
                substringLength = Math.Min(substringLength, line.Text.Length);
                if (substringLength > 0 && lineVisible)
                {
                    // Check if line has effects (pre-calculated during parsing) and we have effect support
                    var canRenderEffects =
                        line.HasEffects && line.CharacterData != null && CanRenderEffects;

                    if (canRenderEffects)
                    {
                        // Per-character rendering with effects (partial line)
                        RenderTextLineWithEffects(
                            font,
                            spriteBatch,
                            line,
                            textStartX,
                            lineY,
                            ref messageBox,
                            substringLength
                        );
                    }
                    else
                    {
                        // Fast path: render partial line at once
                        var partialText = line.Text.Substring(0, substringLength);
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
            }
            else
            {
                // Line hasn't been reached yet - stop rendering
                break;
            }
        }
    }

    /// <summary>
    ///     Renders a single line of text with shadow.
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
        var shadowOffset = _constants.Get<int>("PopupShadowOffsetY") * _scale;
        font.DrawText(
            spriteBatch,
            text,
            new Vector2(x + shadowOffset, y + shadowOffset),
            shadowColor
        );

        // Render main text on top
        font.DrawText(spriteBatch, text, new Vector2(x, y), textColor);
    }

    /// <summary>
    ///     Renders a line of text with per-character effects.
    /// </summary>
    /// <param name="font">The font to use.</param>
    /// <param name="spriteBatch">The sprite batch for rendering.</param>
    /// <param name="line">The wrapped line with character data.</param>
    /// <param name="lineX">The X position of the line start.</param>
    /// <param name="lineY">The Y position of the line.</param>
    /// <param name="messageBox">The message box component (for effect state).</param>
    /// <param name="charLimit">Optional limit on characters to render (for partial lines).</param>
    private void RenderTextLineWithEffects(
        DynamicSpriteFont font,
        SpriteBatch spriteBatch,
        WrappedLine line,
        int lineX,
        int lineY,
        ref MessageBoxComponent messageBox,
        int charLimit = -1
    )
    {
        if (line.CharacterData == null || _textEffectCalculator == null || _modManager == null)
            return;

        var shadowOffset = _constants.Get<int>("PopupShadowOffsetY") * _scale;
        var charsToRender =
            charLimit > 0
                ? Math.Min(charLimit, line.CharacterData.Count)
                : line.CharacterData.Count;

        // Cache for effect definitions (avoid repeated lookups for same effect ID)
        string? lastEffectId = null;
        TextEffectDefinition? effectDef = null;
        ColorPaletteDefinition? palette = null;

        // Render each character with effects
        for (var i = 0; i < charsToRender; i++)
        {
            var charData = line.CharacterData[i];
            var charStr = charData.Character.ToString();

            // Look up effect definition for this character (cached if same as previous)
            if (charData.EffectId != lastEffectId)
            {
                lastEffectId = charData.EffectId;
                effectDef = null;
                palette = null;

                if (!string.IsNullOrEmpty(charData.EffectId))
                {
                    effectDef = _modManager.GetDefinition<TextEffectDefinition>(charData.EffectId);
                    if (effectDef?.ColorPaletteId != null)
                        palette = _modManager.GetDefinition<ColorPaletteDefinition>(
                            effectDef.ColorPaletteId
                        );
                }
            }

            // Calculate base position (scaled)
            var baseX = lineX + charData.BaseX * _scale;
            float baseY = lineY;

            // Calculate position offset from effects
            var positionOffset = Vector2.Zero;
            if (effectDef != null)
            {
                // Get shake offset for this character
                var shakeOffset = Vector2.Zero;
                if (
                    messageBox.ShakeOffsets != null
                    && messageBox.ShakeOffsets.TryGetValue(charData.CharIndex, out var shake)
                )
                    shakeOffset = shake * _scale; // Scale shake offset

                positionOffset =
                    _textEffectCalculator.CalculatePositionOffset(
                        effectDef,
                        charData.CharIndex,
                        messageBox.EffectTime,
                        shakeOffset
                    ) * _scale; // Scale effect offsets
            }

            // Apply spacing adjustments from effect
            var letterSpacingOffset = effectDef?.LetterSpacingOffset ?? 0f;
            var verticalOffset = effectDef?.VerticalOffset ?? 0f;

            // Calculate final position with spacing adjustments
            var finalX = baseX + positionOffset.X + i * letterSpacingOffset * _scale;
            var finalY = baseY + positionOffset.Y + verticalOffset * _scale;

            // Calculate rotation, scale, and opacity for effects
            var rotation = 0f;
            var scale = 1f;
            var opacity = 1f;
            var glowOpacity = 0f;
            if (effectDef != null)
            {
                rotation = _textEffectCalculator.CalculateRotation(
                    effectDef,
                    charData.CharIndex,
                    messageBox.EffectTime
                );
                scale = _textEffectCalculator.CalculateScale(
                    effectDef,
                    charData.CharIndex,
                    messageBox.EffectTime
                );
                opacity = _textEffectCalculator.CalculateOpacity(
                    effectDef,
                    charData.CharIndex,
                    messageBox.EffectTime
                );
                glowOpacity = _textEffectCalculator.CalculateGlowOpacity(
                    effectDef,
                    charData.CharIndex,
                    messageBox.EffectTime
                );
            }

            // Determine text color (effect color cycling or manual/default)
            var textColor = charData.TextColor;
            var shadowColor = charData.ShadowColor;

            // Pre-calculate cycle color if color cycling is active (avoid repeated calculations)
            Color? cycleColor = null;
            if (
                effectDef != null
                && palette != null
                && effectDef.EffectTypes.HasFlag(TextEffectType.ColorCycle)
            )
            {
                cycleColor = _textEffectCalculator.CalculateCycleColor(
                    palette,
                    effectDef,
                    charData.CharIndex,
                    messageBox.EffectTime,
                    effectDef.ColorCycleSpeed
                );

                // Apply color cycling based on mode
                switch (effectDef.ColorMode)
                {
                    case ColorEffectMode.Override:
                        // Always use effect color
                        textColor = cycleColor.Value;
                        break;

                    case ColorEffectMode.Tint:
                        // Blend effect color with current color
                        textColor = Color.Lerp(charData.TextColor, cycleColor.Value, 0.5f);
                        break;

                    case ColorEffectMode.Preserve:
                        // Only apply if no manual color set
                        if (!charData.HasManualColor)
                            textColor = cycleColor.Value;
                        break;
                }

                // Apply shadow mode
                switch (effectDef.ShadowMode)
                {
                    case ShadowEffectMode.Derive:
                        // Derive shadow from text color
                        shadowColor = new Color(
                            (byte)(textColor.R * effectDef.ShadowDeriveMultiplier),
                            (byte)(textColor.G * effectDef.ShadowDeriveMultiplier),
                            (byte)(textColor.B * effectDef.ShadowDeriveMultiplier),
                            textColor.A
                        );
                        break;

                    case ShadowEffectMode.Preserve:
                        // Keep original shadow (already set from charData)
                        break;
                }
            }

            // Apply opacity to colors
            if (opacity < 1f)
            {
                textColor = new Color(
                    textColor.R,
                    textColor.G,
                    textColor.B,
                    (byte)(textColor.A * opacity)
                );
                shadowColor = new Color(
                    shadowColor.R,
                    shadowColor.G,
                    shadowColor.B,
                    (byte)(shadowColor.A * opacity)
                );
            }

            // Calculate origin for rotation based on wobble origin setting
            var charBounds = font.MeasureString(charStr);
            var origin = Vector2.Zero;
            if (rotation != 0f || scale != 1f)
            {
                var originX = charBounds.X / 2f;
                var originY = charBounds.Y / 2f; // Default: center

                if (effectDef != null)
                    switch (effectDef.WobbleOrigin)
                    {
                        case WobbleOrigin.Top:
                            originY = 0f;
                            break;
                        case WobbleOrigin.Bottom:
                            originY = charBounds.Y;
                            break;
                        case WobbleOrigin.Center:
                        default:
                            originY = charBounds.Y / 2f;
                            break;
                    }

                origin = new Vector2(originX, originY);
            }

            // Adjust position to account for origin offset when rotating/scaling
            var adjustedPos =
                rotation != 0f || scale != 1f
                    ? new Vector2(finalX + origin.X, finalY + origin.Y)
                    : new Vector2(finalX, finalY);

            var scaleVec = new Vector2(scale, scale);

            // Render glow effect (multiple offset passes)
            if (glowOpacity > 0f && effectDef != null)
            {
                Color glowColor;
                if (effectDef.GlowColor != null)
                    glowColor = effectDef.GlowColor.ToColor();
                else
                    // Derive glow color from text color (brighter)
                    glowColor = new Color(
                        Math.Min(255, textColor.R + 50),
                        Math.Min(255, textColor.G + 50),
                        Math.Min(255, textColor.B + 50),
                        (byte)(255 * glowOpacity)
                    );
                glowColor = new Color(
                    glowColor.R,
                    glowColor.G,
                    glowColor.B,
                    (byte)(glowColor.A * glowOpacity)
                );

                var glowRadius = effectDef.GlowRadius * _scale;
                // Render glow in 8 directions
                for (var dx = -glowRadius; dx <= glowRadius; dx += glowRadius)
                for (var dy = -glowRadius; dy <= glowRadius; dy += glowRadius)
                {
                    if (dx == 0 && dy == 0)
                        continue;
                    var glowPos = adjustedPos + new Vector2(dx, dy);
                    if (rotation != 0f || scale != 1f)
                        font.DrawText(
                            spriteBatch,
                            charStr,
                            glowPos,
                            glowColor,
                            rotation,
                            origin,
                            scaleVec
                        );
                    else
                        font.DrawText(
                            spriteBatch,
                            charStr,
                            new Vector2(finalX + dx, finalY + dy),
                            glowColor
                        );
                }
            }

            // Render shadow first
            if (rotation != 0f || scale != 1f)
            {
                font.DrawText(
                    spriteBatch,
                    charStr,
                    adjustedPos + new Vector2(shadowOffset, shadowOffset),
                    shadowColor,
                    rotation,
                    origin,
                    scaleVec
                );
                // Render main character
                font.DrawText(
                    spriteBatch,
                    charStr,
                    adjustedPos,
                    textColor,
                    rotation,
                    origin,
                    scaleVec
                );
            }
            else
            {
                // Fast path: no rotation/scale
                font.DrawText(
                    spriteBatch,
                    charStr,
                    new Vector2(finalX + shadowOffset, finalY + shadowOffset),
                    shadowColor
                );
                font.DrawText(spriteBatch, charStr, new Vector2(finalX, finalY), textColor);
            }
        }
    }
}
