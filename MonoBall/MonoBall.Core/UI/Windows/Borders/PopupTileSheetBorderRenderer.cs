using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoBall.Core.Constants;
using MonoBall.Core.Mods;
using Serilog;

namespace MonoBall.Core.UI.Windows.Borders;

/// <summary>
///     Renders window borders using a tile sheet definition (for map popups).
/// </summary>
public class PopupTileSheetBorderRenderer : IBorderRenderer
{
    private readonly IConstantsService _constants;
    private readonly ILogger _logger;
    private readonly PopupOutlineDefinition _outlineDef;
    private readonly int _scaledTileHeight;
    private readonly int _scaledTileWidth;
    private readonly Texture2D _texture;

    // Cached tile lookup dictionary
    private readonly Dictionary<int, PopupTileDefinition> _tileLookup;

    /// <summary>
    ///     Initializes a new instance of the PopupTileSheetBorderRenderer class.
    /// </summary>
    /// <param name="texture">The border texture.</param>
    /// <param name="outlineDef">The outline definition.</param>
    /// <param name="scaledTileWidth">The scaled tile width in pixels.</param>
    /// <param name="scaledTileHeight">The scaled tile height in pixels.</param>
    /// <param name="constants">The constants service.</param>
    /// <param name="logger">The logger.</param>
    /// <exception cref="ArgumentNullException">Thrown when texture, outlineDef, constants, or logger is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when TileUsage is null or Tiles array is empty.</exception>
    public PopupTileSheetBorderRenderer(
        Texture2D texture,
        PopupOutlineDefinition outlineDef,
        int scaledTileWidth,
        int scaledTileHeight,
        IConstantsService constants,
        ILogger logger
    )
    {
        _texture = texture ?? throw new ArgumentNullException(nameof(texture));
        _outlineDef = outlineDef ?? throw new ArgumentNullException(nameof(outlineDef));
        _constants = constants ?? throw new ArgumentNullException(nameof(constants));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _scaledTileWidth = scaledTileWidth;
        _scaledTileHeight = scaledTileHeight;

        if (outlineDef.TileUsage == null)
            throw new InvalidOperationException(
                $"Outline definition '{outlineDef.Id}' has no TileUsage. "
                    + "Cannot render border without tile usage mapping."
            );

        if (outlineDef.Tiles == null || outlineDef.Tiles.Count == 0)
            throw new InvalidOperationException(
                $"Outline definition '{outlineDef.Id}' has no tiles array or tiles array is empty. "
                    + "Cannot render border without tile definitions."
            );

        // Build tile lookup dictionary
        _tileLookup = new Dictionary<int, PopupTileDefinition>();
        foreach (var tile in outlineDef.Tiles)
        {
            if (tile == null)
            {
                _logger.Warning("Null tile found in outline definition {OutlineId}", outlineDef.Id);
                continue;
            }

            if (!_tileLookup.ContainsKey(tile.Index))
                _tileLookup[tile.Index] = tile;
            else
                _logger.Error(
                    "Duplicate tile index {TileIndex} found in outline definition {OutlineId}. "
                        + "This should not happen - check JSON deserialization.",
                    tile.Index,
                    outlineDef.Id
                );
        }

        if (_tileLookup.Count == 0)
            throw new InvalidOperationException(
                $"No valid tiles found in outline definition '{outlineDef.Id}' after processing."
            );
    }

    /// <summary>
    ///     Renders the window border around the specified interior bounds.
    /// </summary>
    /// <param name="spriteBatch">The sprite batch for rendering.</param>
    /// <param name="interiorX">The X position of the interior (content area).</param>
    /// <param name="interiorY">The Y position of the interior (content area).</param>
    /// <param name="interiorWidth">The width of the interior (content area).</param>
    /// <param name="interiorHeight">The height of the interior (content area).</param>
    /// <remarks>
    ///     SpriteBatch.Begin() must be called before this method.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when spriteBatch is null.</exception>
    public void RenderBorder(
        SpriteBatch spriteBatch,
        int interiorX,
        int interiorY,
        int interiorWidth,
        int interiorHeight
    )
    {
        // Helper to draw a single tile by its Index property
        void DrawTile(int tileIndex, int destX, int destY)
        {
            if (!_tileLookup.TryGetValue(tileIndex, out var tile))
            {
                _logger.Debug(
                    "Tile index {TileIndex} not found in outline definition {OutlineId}",
                    tileIndex,
                    _outlineDef.Id
                );
                return;
            }

            var srcRect = new Rectangle(tile.X, tile.Y, tile.Width, tile.Height);
            var destRect = new Rectangle(destX, destY, _scaledTileWidth, _scaledTileHeight);
            spriteBatch.Draw(_texture, destRect, srcRect, Color.White);
        }

        var usage = _outlineDef.TileUsage!;

        // Pokeemerald draws the window interior at (interiorX, interiorY) with size (interiorWidth, interiorHeight)
        // The frame is drawn AROUND it
        // For 80x24 background: deltaX = 10 tiles, deltaY = 3 tiles

        // Draw top edge (12 tiles from x-1 to x+10)
        // This includes the top-left and top-right corner tiles
        for (var i = 0; i < usage.TopEdge.Count && i < 12; i++)
        {
            var tileIndex = usage.TopEdge[i];
            var tileX = interiorX + (i - 1) * _scaledTileWidth; // i-1 means first tile is at x-1 (pokeemerald: i - 1 + x)
            var tileY = interiorY - _scaledTileHeight; // y-1 in tile units (pokeemerald: y - 1)
            DrawTile(tileIndex, tileX, tileY);
        }

        // Draw left edge (3 tiles at x-1, for y+0, y+1, y+2)
        // Use LeftEdge array if available, otherwise fall back to LeftMiddle
        if (usage.LeftEdge != null && usage.LeftEdge.Count > 0)
        {
            var popupInteriorTilesYLeft = _constants.Get<int>("PopupInteriorTilesY");
            for (var i = 0; i < usage.LeftEdge.Count && i < popupInteriorTilesYLeft; i++)
            {
                var tileIndex = usage.LeftEdge[i];
                DrawTile(
                    tileIndex,
                    interiorX - _scaledTileWidth,
                    interiorY + i * _scaledTileHeight
                );
            }
        }
        else if (usage.LeftMiddle != 0)
        {
            // Fallback to LeftMiddle for backwards compatibility
            var popupInteriorTilesYLeftMiddle = _constants.Get<int>("PopupInteriorTilesY");
            for (var i = 0; i < popupInteriorTilesYLeftMiddle; i++)
                DrawTile(
                    usage.LeftMiddle,
                    interiorX - _scaledTileWidth,
                    interiorY + i * _scaledTileHeight
                );
        }

        // Draw right edge (3 tiles at deltaX+x, for y+0, y+1, y+2)
        // Use RightEdge array if available, otherwise fall back to RightMiddle
        if (usage.RightEdge != null && usage.RightEdge.Count > 0)
        {
            var popupInteriorTilesYRight = _constants.Get<int>("PopupInteriorTilesY");
            for (var i = 0; i < usage.RightEdge.Count && i < popupInteriorTilesYRight; i++)
            {
                var tileIndex = usage.RightEdge[i];
                DrawTile(
                    tileIndex,
                    interiorX + _constants.Get<int>("PopupInteriorTilesX") * _scaledTileWidth,
                    interiorY + i * _scaledTileHeight
                );
            }
        }
        else if (usage.RightMiddle != 0)
        {
            // Fallback to RightMiddle for backwards compatibility
            var popupInteriorTilesY = _constants.Get<int>("PopupInteriorTilesY");
            for (var i = 0; i < popupInteriorTilesY; i++)
                DrawTile(
                    usage.RightMiddle,
                    interiorX + _constants.Get<int>("PopupInteriorTilesX") * _scaledTileWidth,
                    interiorY + i * _scaledTileHeight
                );
        }

        // Draw bottom edge (12 tiles from x-1 to x+10)
        // This includes the bottom-left and bottom-right corner tiles
        for (var i = 0; i < usage.BottomEdge.Count && i < 12; i++)
        {
            var tileIndex = usage.BottomEdge[i];
            var tileX = interiorX + (i - 1) * _scaledTileWidth;
            var tileY = interiorY + _constants.Get<int>("PopupInteriorTilesY") * _scaledTileHeight;
            DrawTile(tileIndex, tileX, tileY);
        }
    }
}
