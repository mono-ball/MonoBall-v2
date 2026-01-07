using System;
using Arch.Core;
using MonoBall.Core.Constants;
using MonoBall.Core.ECS.Components;
using MonoBall.Core.Mods;

namespace MonoBall.Core.ECS.Utilities;

/// <summary>
///     Helper utility for getting tile sizes from maps or mod configuration.
/// </summary>
public static class TileSizeHelper
{
    /// <summary>
    ///     Gets the tile size from the first loaded map in the world, or from mod configuration.
    /// </summary>
    /// <remarks>
    ///     This method assumes square tiles (tileWidth == tileHeight).
    ///     For rectangular tiles, use GetTileWidth() and GetTileHeight() separately.
    /// </remarks>
    /// <param name="world">The ECS world to query for MapComponent.</param>
    /// <param name="modManager">Required mod manager for getting default tile size when no maps are loaded.</param>
    /// <returns>The tile size in pixels (uses tile width).</returns>
    /// <exception cref="InvalidOperationException">
    ///     Thrown if no maps are loaded and modManager is null or has no tile size
    ///     configuration.
    /// </exception>
    [Obsolete(
        "This method assumes square tiles. Use GetTileWidth() and GetTileHeight() separately for rectangular tiles."
    )]
    public static int GetTileSize(World world, IConstantsService constantsService)
    {
        // For backward compatibility, return tile width (assumes square tiles)
        return GetTileWidth(world, constantsService);
    }

    /// <summary>
    ///     Gets the tile width from the first loaded map in the world, or from constants service.
    /// </summary>
    /// <param name="world">The ECS world to query for MapComponent.</param>
    /// <param name="constantsService">The constants service to use when no maps are loaded. Must contain "TileWidth" constant.</param>
    /// <returns>The tile width in pixels.</returns>
    /// <exception cref="ArgumentNullException">Thrown if world or constantsService is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown if no maps are loaded and ConstantsService does not contain TileWidth constant.</exception>
    public static int GetTileWidth(World world, IConstantsService constantsService)
    {
        if (world == null)
            throw new ArgumentNullException(nameof(world));
        if (constantsService == null)
            throw new ArgumentNullException(nameof(constantsService));

        // Check for tile width from loaded maps
        var tileWidth = 0;
        var mapQuery = new QueryDescription().WithAll<MapComponent>();
        world.Query(
            in mapQuery,
            (ref MapComponent map) =>
            {
                // Use the first map's tile width
                if (tileWidth == 0 && map.TileWidth > 0)
                    tileWidth = map.TileWidth;
            }
        );

        if (tileWidth > 0)
            return tileWidth;

        // No maps loaded - require ConstantsService to have TileWidth constant
        if (!constantsService.Contains("TileWidth"))
        {
            throw new InvalidOperationException(
                "Cannot determine tile width: No maps are loaded and ConstantsService does not contain 'TileWidth' constant. "
                    + "Either load a map with tileWidth specified, or ensure ConstantsService has TileWidth constant defined."
            );
        }

        return constantsService.Get<int>("TileWidth");
    }

    /// <summary>
    ///     Gets the tile height from the first loaded map in the world, or from constants service.
    /// </summary>
    /// <param name="world">The ECS world to query for MapComponent.</param>
    /// <param name="constantsService">The constants service to use when no maps are loaded. Must contain "TileHeight" constant.</param>
    /// <returns>The tile height in pixels.</returns>
    /// <exception cref="ArgumentNullException">Thrown if world or constantsService is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown if no maps are loaded and ConstantsService does not contain TileHeight constant.</exception>
    public static int GetTileHeight(World world, IConstantsService constantsService)
    {
        if (world == null)
            throw new ArgumentNullException(nameof(world));
        if (constantsService == null)
            throw new ArgumentNullException(nameof(constantsService));

        // Check for tile height from loaded maps
        var tileHeight = 0;
        var mapQuery = new QueryDescription().WithAll<MapComponent>();
        world.Query(
            in mapQuery,
            (ref MapComponent map) =>
            {
                // Use the first map's tile height
                if (tileHeight == 0 && map.TileHeight > 0)
                    tileHeight = map.TileHeight;
            }
        );

        if (tileHeight > 0)
            return tileHeight;

        // No maps loaded - require ConstantsService to have TileHeight constant
        if (!constantsService.Contains("TileHeight"))
        {
            throw new InvalidOperationException(
                "Cannot determine tile height: No maps are loaded and ConstantsService does not contain 'TileHeight' constant. "
                    + "Either load a map with tileHeight specified, or ensure ConstantsService has TileHeight constant defined."
            );
        }

        return constantsService.Get<int>("TileHeight");
    }
}
