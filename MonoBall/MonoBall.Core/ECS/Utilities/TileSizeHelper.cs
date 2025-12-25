using System;
using Arch.Core;
using Arch.Core.Utils;
using MonoBall.Core;
using MonoBall.Core.ECS.Components;
using MonoBall.Core.Mods;

namespace MonoBall.Core.ECS.Utilities
{
    /// <summary>
    /// Helper utility for getting tile sizes from maps or mod configuration.
    /// </summary>
    public static class TileSizeHelper
    {
        // Cached query description to avoid allocations in hot paths
        private static readonly QueryDescription MapQuery =
            new QueryDescription().WithAll<MapComponent>();

        /// <summary>
        /// Gets the tile size from the first loaded map in the world, or from mod configuration.
        /// </summary>
        /// <remarks>
        /// This method assumes square tiles (tileWidth == tileHeight).
        /// For rectangular tiles, use GetTileWidth() and GetTileHeight() separately.
        /// </remarks>
        /// <param name="world">The ECS world to query for MapComponent.</param>
        /// <param name="modManager">Required mod manager for getting default tile size when no maps are loaded.</param>
        /// <returns>The tile size in pixels (uses tile width).</returns>
        /// <exception cref="InvalidOperationException">Thrown if no maps are loaded and modManager is null or has no tile size configuration.</exception>
        [System.Obsolete(
            "This method assumes square tiles. Use GetTileWidth() and GetTileHeight() separately for rectangular tiles."
        )]
        public static int GetTileSize(World world, IModManager? modManager = null)
        {
            // For backward compatibility, return tile width (assumes square tiles)
            return GetTileWidth(world, modManager);
        }

        /// <summary>
        /// Gets the tile width from the first loaded map in the world, or from mod configuration.
        /// </summary>
        /// <param name="world">The ECS world to query for MapComponent.</param>
        /// <param name="modManager">Required mod manager for getting default tile width when no maps are loaded.</param>
        /// <returns>The tile width in pixels.</returns>
        /// <exception cref="InvalidOperationException">Thrown if no maps are loaded and modManager is null or has no tile width configuration.</exception>
        public static int GetTileWidth(World world, IModManager? modManager = null)
        {
            // First, try to get tile width from a loaded map
            int tileWidth = 0;

            world.Query(
                in MapQuery,
                (ref MapComponent map) =>
                {
                    // Use the first map's tile width
                    if (tileWidth == 0 && map.TileWidth > 0)
                    {
                        tileWidth = map.TileWidth;
                    }
                }
            );

            if (tileWidth > 0)
            {
                return tileWidth;
            }

            // Require mod manager for defaults
            if (modManager == null)
            {
                throw new InvalidOperationException(
                    "Cannot determine tile width: No maps are loaded and ModManager is not available. "
                        + "Either load a map with tileWidth specified, or provide a ModManager with tileWidth configured."
                );
            }

            return modManager.GetTileWidth();
        }

        /// <summary>
        /// Gets the tile height from the first loaded map in the world, or from mod configuration.
        /// </summary>
        /// <param name="world">The ECS world to query for MapComponent.</param>
        /// <param name="modManager">Required mod manager for getting default tile height when no maps are loaded.</param>
        /// <returns>The tile height in pixels.</returns>
        /// <exception cref="InvalidOperationException">Thrown if no maps are loaded and modManager is null or has no tile height configuration.</exception>
        public static int GetTileHeight(World world, IModManager? modManager = null)
        {
            // First, try to get tile height from a loaded map
            int tileHeight = 0;

            world.Query(
                in MapQuery,
                (ref MapComponent map) =>
                {
                    // Use the first map's tile height
                    if (tileHeight == 0 && map.TileHeight > 0)
                    {
                        tileHeight = map.TileHeight;
                    }
                }
            );

            if (tileHeight > 0)
            {
                return tileHeight;
            }

            // Require mod manager for defaults
            if (modManager == null)
            {
                throw new InvalidOperationException(
                    "Cannot determine tile height: No maps are loaded and ModManager is not available. "
                        + "Either load a map with tileHeight specified, or provide a ModManager with tileHeight configured."
                );
            }

            return modManager.GetTileHeight();
        }
    }
}
