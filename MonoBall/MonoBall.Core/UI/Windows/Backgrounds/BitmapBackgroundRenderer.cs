using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoBall.Core.Mods;

namespace MonoBall.Core.UI.Windows.Backgrounds
{
    /// <summary>
    /// Renders window backgrounds using a bitmap texture (for map popups).
    /// </summary>
    public class BitmapBackgroundRenderer : IBackgroundRenderer
    {
        private readonly Texture2D _texture;
        private readonly PopupBackgroundDefinition _backgroundDef;

        /// <summary>
        /// Initializes a new instance of the BitmapBackgroundRenderer class.
        /// </summary>
        /// <param name="texture">The background texture.</param>
        /// <param name="backgroundDef">The background definition.</param>
        /// <exception cref="ArgumentNullException">Thrown when texture or backgroundDef is null.</exception>
        public BitmapBackgroundRenderer(Texture2D texture, PopupBackgroundDefinition backgroundDef)
        {
            _texture = texture ?? throw new ArgumentNullException(nameof(texture));
            _backgroundDef =
                backgroundDef ?? throw new ArgumentNullException(nameof(backgroundDef));
        }

        /// <summary>
        /// Renders the window background within the specified bounds.
        /// </summary>
        /// <param name="spriteBatch">The sprite batch for rendering.</param>
        /// <param name="x">The X position of the background.</param>
        /// <param name="y">The Y position of the background.</param>
        /// <param name="width">The width of the background.</param>
        /// <param name="height">The height of the background.</param>
        /// <remarks>
        /// SpriteBatch.Begin() must be called before this method.
        /// </remarks>
        /// <exception cref="ArgumentNullException">Thrown when spriteBatch is null.</exception>
        public void RenderBackground(SpriteBatch spriteBatch, int x, int y, int width, int height)
        {
            // Draw bitmap texture (coordinates already scaled by caller)
            spriteBatch.Draw(
                _texture,
                new Rectangle(x, y, width, height),
                new Rectangle(0, 0, _backgroundDef.Width, _backgroundDef.Height),
                Color.White
            );
        }
    }
}
