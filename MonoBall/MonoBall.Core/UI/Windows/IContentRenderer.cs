using Microsoft.Xna.Framework.Graphics;

namespace MonoBall.Core.UI.Windows
{
    /// <summary>
    /// Interface for rendering window content.
    /// </summary>
    public interface IContentRenderer
    {
        /// <summary>
        /// Renders the window content within the specified bounds.
        /// All coordinates are in screen pixels (already scaled by the caller).
        /// </summary>
        /// <param name="spriteBatch">The sprite batch for rendering.</param>
        /// <param name="x">The X position of the content area.</param>
        /// <param name="y">The Y position of the content area.</param>
        /// <param name="width">The width of the content area.</param>
        /// <param name="height">The height of the content area.</param>
        /// <remarks>
        /// SpriteBatch.Begin() must be called before this method.
        /// </remarks>
        void RenderContent(SpriteBatch spriteBatch, int x, int y, int width, int height);
    }
}
