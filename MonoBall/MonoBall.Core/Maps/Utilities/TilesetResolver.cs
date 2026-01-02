using System;
using System.Collections.Generic;
using MonoBall.Core.Maps;

namespace MonoBall.Core.Maps.Utilities;

/// <summary>
///     Utility class for resolving tilesets from GIDs based on firstGid ranges.
///     Follows DRY principle - shared logic for tileset resolution.
/// </summary>
public static class TilesetResolver
{
    /// <summary>
    ///     Resolves which tileset a GID belongs to based on firstGid ranges.
    ///     Tilesets must be sorted by firstGid descending for correct resolution.
    /// </summary>
    /// <param name="gid">The Global ID of the tile.</param>
    /// <param name="tilesetRefs">List of tileset references sorted by firstGid descending.</param>
    /// <returns>Tuple of (tilesetId, firstGid) or (empty string, 0) if not found.</returns>
    /// <exception cref="ArgumentNullException">Thrown when tilesetRefs is null.</exception>
    public static (string TilesetId, int FirstGid) ResolveTilesetForGid(
        int gid,
        IReadOnlyList<TilesetReference> tilesetRefs
    )
    {
        if (tilesetRefs == null)
            throw new ArgumentNullException(nameof(tilesetRefs));

        if (tilesetRefs.Count == 0)
            return (string.Empty, 0);

        // Tilesets are sorted by firstGid descending, so find the first one where GID >= firstGid
        // This gives us the tileset with the highest firstGid that the GID belongs to
        // Use indexed iteration instead of IndexOf() for O(n) instead of O(n²) performance
        for (int i = 0; i < tilesetRefs.Count; i++)
        {
            var tilesetRef = tilesetRefs[i];
            if (gid >= tilesetRef.FirstGid)
            {
                // Check if there's a next tileset with a higher firstGid
                if (i > 0)
                {
                    // There's a tileset with higher firstGid - check if GID is below that
                    var nextTilesetRef = tilesetRefs[i - 1];
                    if (gid < nextTilesetRef.FirstGid)
                    {
                        // GID is in this tileset's range
                        return (tilesetRef.TilesetId, tilesetRef.FirstGid);
                    }
                }
                else
                {
                    // This is the tileset with highest firstGid, GID belongs to it
                    return (tilesetRef.TilesetId, tilesetRef.FirstGid);
                }
            }
        }

        // Fallback to tileset with lowest firstGid if GID is less than all firstGids
        return (
            tilesetRefs[tilesetRefs.Count - 1].TilesetId,
            tilesetRefs[tilesetRefs.Count - 1].FirstGid
        );
    }
}
