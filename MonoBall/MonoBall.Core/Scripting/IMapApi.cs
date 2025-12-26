using System.Collections.Generic;
using Arch.Core;
using Microsoft.Xna.Framework;

namespace MonoBall.Core.Scripting
{
    /// <summary>
    /// API for map-related operations.
    /// </summary>
    public interface IMapApi
    {
        /// <summary>
        /// Loads a map by its ID.
        /// </summary>
        /// <param name="mapId">The map definition ID.</param>
        /// <param name="tilePosition">Optional tile position for map placement.</param>
        void LoadMap(string mapId, Vector2? tilePosition = null);

        /// <summary>
        /// Unloads a map by its ID.
        /// </summary>
        /// <param name="mapId">The map ID to unload.</param>
        void UnloadMap(string mapId);

        /// <summary>
        /// Checks if a map is loaded.
        /// </summary>
        /// <param name="mapId">The map ID to check.</param>
        /// <returns>True if the map is loaded, false otherwise.</returns>
        bool IsMapLoaded(string mapId);

        /// <summary>
        /// Gets the map entity for a given map ID.
        /// </summary>
        /// <param name="mapId">The map ID.</param>
        /// <returns>The map entity, or null if map not loaded.</returns>
        Entity? GetMapEntity(string mapId);

        /// <summary>
        /// Gets all loaded map IDs.
        /// </summary>
        /// <returns>Collection of loaded map IDs.</returns>
        IEnumerable<string> GetLoadedMapIds();

        /// <summary>
        /// Gets the active map IDs (current map and connected maps).
        /// </summary>
        /// <returns>Collection of active map IDs.</returns>
        IEnumerable<string> GetActiveMapIds();
    }
}
