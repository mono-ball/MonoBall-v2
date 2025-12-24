using System.Collections.Generic;
using Arch.Core;

namespace MonoBall.Core.ECS.Services
{
    /// <summary>
    /// Service that provides filtering and querying for entities in active maps.
    /// Active maps are the current map and all connected maps that are currently loaded.
    /// </summary>
    public interface IActiveMapFilterService
    {
        /// <summary>
        /// Gets the set of active map IDs (all currently loaded maps).
        /// </summary>
        /// <returns>A set of active map IDs.</returns>
        HashSet<string> GetActiveMapIds();

        /// <summary>
        /// Checks if an entity is in one of the active maps.
        /// </summary>
        /// <param name="entity">The entity to check.</param>
        /// <returns>True if the entity is in an active map, false otherwise.</returns>
        bool IsEntityInActiveMaps(Entity entity);

        /// <summary>
        /// Gets the map ID for an entity if it has one.
        /// </summary>
        /// <param name="entity">The entity to check.</param>
        /// <returns>The map ID, or null if the entity doesn't have a map association.</returns>
        string? GetEntityMapId(Entity entity);

        /// <summary>
        /// Gets the map ID that the player is currently positioned in.
        /// </summary>
        /// <returns>The map ID containing the player, or null if player not found or not in any map.</returns>
        string? GetPlayerCurrentMapId();

        /// <summary>
        /// Invalidates the cached active map IDs, forcing a recalculation on next access.
        /// Call this when maps are loaded or unloaded.
        /// </summary>
        void InvalidateCache();
    }
}
