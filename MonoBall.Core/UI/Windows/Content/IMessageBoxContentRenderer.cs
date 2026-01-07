using Microsoft.Xna.Framework.Graphics;
using MonoBall.Core.Scenes.Components;

namespace MonoBall.Core.UI.Windows.Content;

/// <summary>
///     Specialized interface for rendering message box content that requires component state.
/// </summary>
public interface IMessageBoxContentRenderer
{
    /// <summary>
    ///     Renders the message box content with the specified component state.
    ///     All coordinates are in screen pixels (already scaled by the caller).
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
    void RenderContent(
        SpriteBatch spriteBatch,
        ref MessageBoxComponent messageBox,
        int x,
        int y,
        int width,
        int height
    );
}
