using System;
using System.Collections.Generic;
using System.Linq;
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
    ///     Tilesets must not have overlapping ranges - this should be validated before calling this method.
    /// </summary>
    /// <param name="gid">The Global ID of the tile.</param>
    /// <param name="tilesetRefs">List of tileset references sorted by firstGid descending.</param>
    /// <returns>Tuple of (tilesetId, firstGid).</returns>
    /// <exception cref="ArgumentNullException">Thrown when tilesetRefs is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when GID cannot be resolved to any tileset.</exception>
    public static (string TilesetId, int FirstGid) ResolveTilesetForGid(
        int gid,
        IReadOnlyList<TilesetReference> tilesetRefs
    )
    {
        if (tilesetRefs == null)
            throw new ArgumentNullException(nameof(tilesetRefs));

        if (tilesetRefs.Count == 0)
            throw new InvalidOperationException(
                "Cannot resolve GID: no tileset references provided. GID 0 should be handled by caller before calling this method."
            );

        // Tilesets are sorted by firstGid descending, so find the first one where GID >= firstGid
        // This gives us the tileset with the highest firstGid that the GID belongs to
        // Use indexed iteration instead of IndexOf() for O(n) instead of O(n²) performance
        for (int i = 0; i < tilesetRefs.Count; i++)
        {
            var tilesetRef = tilesetRefs[i];
            if (gid >= tilesetRef.FirstGid)
            {
                // Check if there's a tileset with a higher firstGid that this GID also belongs to
                if (i > 0)
                {
                    // There's a tileset with higher firstGid - check if GID belongs to that instead
                    var higherTilesetRef = tilesetRefs[i - 1];
                    if (gid >= higherTilesetRef.FirstGid)
                    {
                        // GID belongs to the higher firstGid tileset, continue to find it
                        continue;
                    }
                }

                // GID is in this tileset's range (either it's the highest firstGid, or GID is below the next higher one)
                return (tilesetRef.TilesetId, tilesetRef.FirstGid);
            }
        }

        // GID is less than all firstGids - fail fast with clear error
        throw new InvalidOperationException(
            $"GID {gid} cannot be resolved to any tileset. GID is less than all firstGid values. "
                + $"Available firstGids: {string.Join(", ", tilesetRefs.Select(t => t.FirstGid))}. "
                + "GID 0 (empty tile) should be handled by caller before calling this method."
        );
    }
}
