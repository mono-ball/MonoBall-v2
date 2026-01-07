using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoBall.Core.Constants;
using MonoBall.Core.Mods;
using Serilog;

namespace MonoBall.Core.UI.Windows.Borders;

/// <summary>
///     Renders message box dialogue frame using a custom tile pattern.
/// </summary>
public class MessageBoxDialogueFrameBorderRenderer : IBorderRenderer
{
    private readonly IConstantsService _constants;
    private readonly ILogger _logger;
    private readonly int _scaledTileSize;
    private readonly Texture2D _texture;

    // Cached tile lookup
    private readonly Dictionary<int, PopupTileDefinition> _tileLookup;
    private readonly PopupOutlineDefinition _tilesheetDef;

    /// <summary>
    ///     Initializes a new instance of the MessageBoxDialogueFrameBorderRenderer class.
    /// </summary>
    /// <param name="texture">The tilesheet texture.</param>
    /// <param name="tilesheetDef">The tilesheet definition.</param>
    /// <param name="scaledTileSize">The scaled tile size in pixels.</param>
    /// <param name="constants">The constants service.</param>
    /// <param name="logger">The logger.</param>
    /// <exception cref="ArgumentNullException">Thrown when texture, tilesheetDef, constants, or logger is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when Tiles array is null or empty.</exception>
    public MessageBoxDialogueFrameBorderRenderer(
        Texture2D texture,
        PopupOutlineDefinition tilesheetDef,
        int scaledTileSize,
        IConstantsService constants,
        ILogger logger
    )
    {
        _texture = texture ?? throw new ArgumentNullException(nameof(texture));
        _tilesheetDef = tilesheetDef ?? throw new ArgumentNullException(nameof(tilesheetDef));
        _constants = constants ?? throw new ArgumentNullException(nameof(constants));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _scaledTileSize = scaledTileSize;

        if (_tilesheetDef.Tiles == null || _tilesheetDef.Tiles.Count == 0)
            throw new InvalidOperationException(
                $"Tilesheet definition '{_tilesheetDef.Id}' has no tiles array or tiles array is empty. "
                    + "Cannot render border without tile definitions."
            );

        // Build tile lookup by index
        _tileLookup = new Dictionary<int, PopupTileDefinition>();
        foreach (var tile in _tilesheetDef.Tiles)
            if (tile != null && !_tileLookup.ContainsKey(tile.Index))
                _tileLookup[tile.Index] = tile;
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
        // Helper to get source rectangle for a tile by index
        Rectangle GetTileSourceRect(int tileIndex)
        {
            if (_tileLookup.TryGetValue(tileIndex, out var tile))
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

        // Helper to draw a tile by index
        void DrawTile(int tileIndex, int destX, int destY)
        {
            var srcRect = GetTileSourceRect(tileIndex);
            var destRect = new Rectangle(destX, destY, _scaledTileSize, _scaledTileSize);
            spriteBatch.Draw(_texture, destRect, srcRect, Color.White);
        }

        // Helper to draw a tile flipped vertically (for bottom row)
        // Note: MonoGame SpriteBatch doesn't support negative height, so we use SpriteEffects
        void DrawTileFlippedV(int tileIndex, int destX, int destY)
        {
            var srcRect = GetTileSourceRect(tileIndex);
            var destRect = new Rectangle(destX, destY, _scaledTileSize, _scaledTileSize);
            spriteBatch.Draw(
                _texture,
                destRect,
                srcRect,
                Color.White,
                0f,
                Vector2.Zero,
                SpriteEffects.FlipVertically,
                0f
            );
        }

        // Match pokeemerald-expansion's DrawDialogueFrame pattern exactly
        // Frame extends outside the interior bounds (x-2*tileSize, x-tileSize, x+width, etc.)
        // Top row: tiles 1, 3, 4 (repeated), 5, 6
        // Middle: tiles 7, 9 (repeated), 10
        // Bottom row: tiles 1, 3, 4 (repeated), 5, 6 (vertically flipped)

        // Top row (at y - tileSize)
        var topY = interiorY - _scaledTileSize;
        DrawTile(1, interiorX - 2 * _scaledTileSize, topY); // Left decorative corner
        DrawTile(3, interiorX - _scaledTileSize, topY); // Left edge
        // Top edge tile 4 repeated for width-1
        for (var i = 0; i < interiorWidth - _scaledTileSize; i += _scaledTileSize)
            DrawTile(4, interiorX + i, topY);
        DrawTile(5, interiorX + interiorWidth - _scaledTileSize, topY); // Right edge
        DrawTile(6, interiorX + interiorWidth, topY); // Right decorative corner

        // Middle section (at y, height matches interior height)
        // Note: In pokeemerald-expansion, the middle section is drawn with height=5 in FillBgTilemapBufferRect,
        // but the actual visible height should match the interior (4 tiles) because the bottom row
        // is drawn at tilemapTop + height (4 tiles), which overwrites the 5th tile.
        // To avoid rendering issues, we draw the middle section only as tall as the interior,
        // then draw the bottom row immediately after.
        var middleHeight = interiorHeight; // Match interior height (4 tiles), not 5
        // Left edge tile 7
        for (var i = 0; i < middleHeight; i += _scaledTileSize)
            DrawTile(7, interiorX - 2 * _scaledTileSize, interiorY + i);
        // Center fill tile 9 repeated
        for (var i = 0; i < interiorWidth + _scaledTileSize; i += _scaledTileSize)
        for (var j = 0; j < middleHeight; j += _scaledTileSize)
            DrawTile(9, interiorX - _scaledTileSize + i, interiorY + j);

        // Right edge tile 10
        for (var i = 0; i < middleHeight; i += _scaledTileSize)
            DrawTile(10, interiorX + interiorWidth, interiorY + i);

        // Bottom row (at tilemapTop + height, where height=4 is the interior height)
        // This matches pokeemerald-expansion exactly: bottom row is drawn at tilemapTop + height (4 tiles)
        var bottomY = interiorY + interiorHeight;
        DrawTileFlippedV(1, interiorX - 2 * _scaledTileSize, bottomY); // Left decorative corner (flipped)
        DrawTileFlippedV(3, interiorX - _scaledTileSize, bottomY); // Left edge (flipped)
        // Bottom edge tile 4 repeated for width-1 (flipped)
        for (var i = 0; i < interiorWidth - _scaledTileSize; i += _scaledTileSize)
            DrawTileFlippedV(4, interiorX + i, bottomY);
        DrawTileFlippedV(5, interiorX + interiorWidth - _scaledTileSize, bottomY); // Right edge (flipped)
        DrawTileFlippedV(6, interiorX + interiorWidth, bottomY); // Right decorative corner (flipped)
    }
}
