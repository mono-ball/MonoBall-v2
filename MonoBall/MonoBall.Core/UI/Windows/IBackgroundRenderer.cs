using Microsoft.Xna.Framework.Graphics;

namespace MonoBall.Core.UI.Windows;

/// <summary>
///     Interface for rendering window backgrounds.
/// </summary>
public interface IBackgroundRenderer
{
    /// <summary>
    ///     Renders the window background within the specified bounds.
    ///     All coordinates are in screen pixels (already scaled by the caller).
    /// </summary>
    /// <param name="spriteBatch">The sprite batch for rendering.</param>
    /// <param name="x">The X position of the background.</param>
    /// <param name="y">The Y position of the background.</param>
    /// <param name="width">The width of the background.</param>
    /// <param name="height">The height of the background.</param>
    /// <remarks>
    ///     SpriteBatch.Begin() must be called before this method.
    /// </remarks>
    void RenderBackground(SpriteBatch spriteBatch, int x, int y, int width, int height);
}
