using Microsoft.Xna.Framework.Graphics;

namespace MonoBall.Core.UI.Windows;

/// <summary>
///     Interface for rendering window borders.
/// </summary>
public interface IBorderRenderer
{
    /// <summary>
    ///     Renders the window border around the specified interior bounds.
    ///     All coordinates are in screen pixels (already scaled by the caller).
    /// </summary>
    /// <param name="spriteBatch">The sprite batch for rendering.</param>
    /// <param name="interiorX">The X position of the interior (content area).</param>
    /// <param name="interiorY">The Y position of the interior (content area).</param>
    /// <param name="interiorWidth">The width of the interior (content area).</param>
    /// <param name="interiorHeight">The height of the interior (content area).</param>
    /// <remarks>
    ///     SpriteBatch.Begin() must be called before this method.
    /// </remarks>
    void RenderBorder(
        SpriteBatch spriteBatch,
        int interiorX,
        int interiorY,
        int interiorWidth,
        int interiorHeight
    );
}
