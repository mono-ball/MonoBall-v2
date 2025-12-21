using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace MonoBall.Core.Maps
{
    /// <summary>
    /// Interface for tileset loading and caching functionality.
    /// Provides access to tileset textures and definitions.
    /// </summary>
    public interface ITilesetLoaderService
    {
        /// <summary>
        /// Gets a tileset definition by ID.
        /// </summary>
        /// <param name="tilesetId">The tileset ID.</param>
        /// <returns>The tileset definition, or null if not found.</returns>
        TilesetDefinition? GetTilesetDefinition(string tilesetId);

        /// <summary>
        /// Loads and caches a tileset texture. If already loaded, returns the cached texture.
        /// </summary>
        /// <param name="tilesetId">The tileset ID to load.</param>
        /// <returns>The loaded texture, or null if loading failed.</returns>
        Texture2D? LoadTileset(string tilesetId);

        /// <summary>
        /// Gets a tileset texture, loading it if not already cached.
        /// </summary>
        /// <param name="tilesetId">The tileset ID.</param>
        /// <returns>The tileset texture, or null if not found or loading failed.</returns>
        Texture2D? GetTilesetTexture(string tilesetId);

        /// <summary>
        /// Calculates the source rectangle for a tile based on its GID (Global ID).
        /// </summary>
        /// <param name="tilesetId">The tileset ID.</param>
        /// <param name="gid">The Global ID of the tile.</param>
        /// <param name="firstGid">The first GID for this tileset.</param>
        /// <returns>The source rectangle, or null if invalid.</returns>
        Rectangle? CalculateSourceRectangle(string tilesetId, int gid, int firstGid);

        /// <summary>
        /// Unloads a tileset texture from the cache.
        /// </summary>
        /// <param name="tilesetId">The tileset ID to unload.</param>
        void UnloadTileset(string tilesetId);

        /// <summary>
        /// Unloads all tileset textures from the cache.
        /// </summary>
        void UnloadAll();

        /// <summary>
        /// Gets animation frames for a specific tile, loading and caching if not already cached.
        /// </summary>
        /// <param name="tilesetId">The tileset ID.</param>
        /// <param name="localTileId">The local tile ID within the tileset.</param>
        /// <returns>The animation frames as a readonly list, or null if the tile has no animation or is not found.</returns>
        System.Collections.Generic.IReadOnlyList<TileAnimationFrame>? GetTileAnimation(
            string tilesetId,
            int localTileId
        );

        /// <summary>
        /// Gets cached animation frames for a specific tile (fast lookup, no definition loading).
        /// </summary>
        /// <param name="tilesetId">The tileset ID.</param>
        /// <param name="localTileId">The local tile ID within the tileset.</param>
        /// <returns>The cached animation frames as a readonly list, or null if not cached.</returns>
        System.Collections.Generic.IReadOnlyList<TileAnimationFrame>? GetCachedAnimation(
            string tilesetId,
            int localTileId
        );
    }
}
