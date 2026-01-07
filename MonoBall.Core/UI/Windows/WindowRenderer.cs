using System;
using Microsoft.Xna.Framework.Graphics;
using MonoBall.Core.UI.Windows.Animations;
using Serilog;

namespace MonoBall.Core.UI.Windows;

/// <summary>
///     Renders a UI window using pluggable border, background, and content renderers.
///     Supports animation via WindowAnimationComponent.
/// </summary>
public class WindowRenderer
{
    private readonly IBackgroundRenderer? _backgroundRenderer;
    private readonly IBorderRenderer? _borderRenderer;
    private readonly IContentRenderer? _contentRenderer;
    private readonly ILogger _logger;

    /// <summary>
    ///     Initializes a new instance of the WindowRenderer class.
    /// </summary>
    /// <param name="borderRenderer">Optional border renderer. If null, no border is rendered.</param>
    /// <param name="backgroundRenderer">Optional background renderer. If null, no background is rendered.</param>
    /// <param name="contentRenderer">Optional content renderer. If null, no content is rendered.</param>
    /// <param name="logger">The logger for logging operations.</param>
    /// <exception cref="ArgumentNullException">Thrown when logger is null.</exception>
    public WindowRenderer(
        IBorderRenderer? borderRenderer,
        IBackgroundRenderer? backgroundRenderer,
        IContentRenderer? contentRenderer,
        ILogger logger
    )
    {
        _borderRenderer = borderRenderer;
        _backgroundRenderer = backgroundRenderer;
        _contentRenderer = contentRenderer;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    ///     Renders the complete window (border, background, content) with optional animation.
    /// </summary>
    /// <param name="spriteBatch">The sprite batch for rendering.</param>
    /// <param name="bounds">The window bounds (coordinates already scaled by caller).</param>
    /// <param name="animation">Optional animation component for animated windows.</param>
    /// <remarks>
    ///     SpriteBatch.Begin() must be called before this method.
    ///     Optional renderers (null) are skipped silently - this is by design to allow windows without borders, backgrounds,
    ///     or content.
    ///     If animation is provided, position offset and scale are applied. Opacity transformations are calculated but not
    ///     applied
    ///     (renderer interfaces don't support opacity yet - future enhancement).
    ///     Note: Animation scaling assumes uniform borders - non-uniform borders (like MessageBox) need manual calculation.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when spriteBatch is null.</exception>
    public void Render(
        SpriteBatch spriteBatch,
        WindowBounds bounds,
        WindowAnimationComponent? animation = null
    )
    {
        if (spriteBatch == null)
            throw new ArgumentNullException(nameof(spriteBatch));

        // Apply animation transformations
        var animatedX = bounds.OuterX;
        var animatedY = bounds.OuterY;
        var animatedScale = 1.0f;
        var animatedOpacity = 1.0f;

        if (animation.HasValue)
        {
            var anim = animation.Value;
            animatedX += (int)anim.PositionOffset.X;
            animatedY += (int)anim.PositionOffset.Y;
            animatedScale = anim.Scale;
            animatedOpacity = anim.Opacity;
        }

        // Calculate animated bounds
        // Scale both outer and interior dimensions independently
        // Note: For non-uniform borders (like MessageBox), the caller must calculate
        // animated bounds manually, as border thicknesses differ per side.
        var animatedOuterWidth = (int)(bounds.OuterWidth * animatedScale);
        var animatedOuterHeight = (int)(bounds.OuterHeight * animatedScale);
        var animatedInteriorWidth = (int)(bounds.InteriorWidth * animatedScale);
        var animatedInteriorHeight = (int)(bounds.InteriorHeight * animatedScale);

        // Calculate animated interior position (maintain border offset proportionally)
        // This assumes uniform borders - non-uniform borders need manual calculation
        var borderOffsetX = bounds.InteriorX - bounds.OuterX;
        var borderOffsetY = bounds.InteriorY - bounds.OuterY;
        var animatedInteriorX = animatedX + (int)(borderOffsetX * animatedScale);
        var animatedInteriorY = animatedY + (int)(borderOffsetY * animatedScale);

        var animatedBounds = new WindowBounds(
            animatedX,
            animatedY,
            animatedOuterWidth,
            animatedOuterHeight,
            animatedInteriorX,
            animatedInteriorY,
            animatedInteriorWidth,
            animatedInteriorHeight
        );

        // Note: Opacity transformations are calculated but not applied
        // Renderer interfaces don't currently support opacity - this is a future enhancement
        // Opacity would need to be applied via SpriteBatch.Begin() with BlendState.AlphaBlend
        // and color tinting per draw call, or renderer interfaces need to be updated

        // Render background first (behind border)
        if (_backgroundRenderer != null)
            _backgroundRenderer.RenderBackground(
                spriteBatch,
                animatedBounds.InteriorX,
                animatedBounds.InteriorY,
                animatedBounds.InteriorWidth,
                animatedBounds.InteriorHeight
            );

        // Render border around interior
        if (_borderRenderer != null)
            _borderRenderer.RenderBorder(
                spriteBatch,
                animatedBounds.InteriorX,
                animatedBounds.InteriorY,
                animatedBounds.InteriorWidth,
                animatedBounds.InteriorHeight
            );

        // Render content last (on top)
        if (_contentRenderer != null)
            _contentRenderer.RenderContent(
                spriteBatch,
                animatedBounds.InteriorX,
                animatedBounds.InteriorY,
                animatedBounds.InteriorWidth,
                animatedBounds.InteriorHeight
            );
    }
}
