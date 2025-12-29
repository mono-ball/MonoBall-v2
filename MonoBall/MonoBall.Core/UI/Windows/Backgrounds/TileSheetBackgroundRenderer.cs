using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoBall.Core.Constants;
using MonoBall.Core.Mods;

namespace MonoBall.Core.UI.Windows.Backgrounds;

/// <summary>
///     Renders window backgrounds using a tile sheet (for message boxes).
/// </summary>
public class TileSheetBackgroundRenderer : IBackgroundRenderer
{
    private readonly int _backgroundTileIndex;
    private readonly IConstantsService _constants;
    private readonly int _scaledTileSize;
    private readonly Texture2D _texture;
    private readonly PopupOutlineDefinition _tilesheetDef;

    /// <summary>
    ///     Initializes a new instance of the TileSheetBackgroundRenderer class.
    /// </summary>
    /// <param name="texture">The tilesheet texture.</param>
    /// <param name="tilesheetDef">The tilesheet definition.</param>
    /// <param name="scaledTileSize">The scaled tile size in pixels.</param>
    /// <param name="backgroundTileIndex">The tile index to use for background fill.</param>
    /// <param name="constants">The constants service.</param>
    /// <exception cref="ArgumentNullException">Thrown when texture, tilesheetDef, or constants is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when Tiles array is null.</exception>
    public TileSheetBackgroundRenderer(
        Texture2D texture,
        PopupOutlineDefinition tilesheetDef,
        int scaledTileSize,
        int backgroundTileIndex,
        IConstantsService constants
    )
    {
        _texture = texture ?? throw new ArgumentNullException(nameof(texture));
        _tilesheetDef = tilesheetDef ?? throw new ArgumentNullException(nameof(tilesheetDef));
        _scaledTileSize = scaledTileSize;
        _backgroundTileIndex = backgroundTileIndex;
        _constants = constants ?? throw new ArgumentNullException(nameof(constants));

        if (_tilesheetDef.Tiles == null)
            throw new InvalidOperationException(
                $"Tilesheet definition '{_tilesheetDef.Id}' has no Tiles array. "
                    + "Cannot render background without tile definitions."
            );
    }

    /// <summary>
    ///     Renders the window background within the specified bounds.
    /// </summary>
    /// <param name="spriteBatch">The sprite batch for rendering.</param>
    /// <param name="x">The X position of the background.</param>
    /// <param name="y">The Y position of the background.</param>
    /// <param name="width">The width of the background.</param>
    /// <param name="height">The height of the background.</param>
    /// <remarks>
    ///     SpriteBatch.Begin() must be called before this method.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when spriteBatch is null.</exception>
    public void RenderBackground(SpriteBatch spriteBatch, int x, int y, int width, int height)
    {
        // Helper to get source rectangle for a tile by index
        Rectangle GetTileSourceRect(int tileIndex)
        {
            if (_tilesheetDef.Tiles != null)
                foreach (var tile in _tilesheetDef.Tiles)
                    if (tile != null && tile.Index == tileIndex)
                        return new Rectangle(tile.X, tile.Y, tile.Width, tile.Height);

            // Calculate from grid position if not in Tiles array
            var tileWidth = _tilesheetDef.TileWidth;
            var tileHeight = _tilesheetDef.TileHeight;
            var columns =
                _tilesheetDef.TileCount > 0
                    ? (int)Math.Sqrt(_tilesheetDef.TileCount)
                    : _constants.Get<int>("DefaultTilesheetColumns");
            var row = tileIndex / columns;
            var col = tileIndex % columns;
            var tileX = col * tileWidth;
            var tileY = row * tileHeight;
            return new Rectangle(tileX, tileY, tileWidth, tileHeight);
        }

        // Fill interior with background tile (tile 0 for message box)
        // This fills the entire interior area where text will be rendered
        for (var i = 0; i < width; i += _scaledTileSize)
        for (var j = 0; j < height; j += _scaledTileSize)
        {
            var srcRect = GetTileSourceRect(_backgroundTileIndex);
            var destRect = new Rectangle(x + i, y + j, _scaledTileSize, _scaledTileSize);
            spriteBatch.Draw(_texture, destRect, srcRect, Color.White);
        }
    }
}
