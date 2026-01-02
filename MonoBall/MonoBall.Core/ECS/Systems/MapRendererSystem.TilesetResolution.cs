using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoBall.Core.Maps;
using MonoBall.Core.Maps.Utilities;
using MonoBall.Core.Resources;

namespace MonoBall.Core.ECS.Systems;

/// <summary>
///     Helper methods for tileset resolution during rendering (DRY - shared logic for fast and slow paths).
/// </summary>
public partial class MapRendererSystem
{
    /// <summary>
    ///     Resolves tileset resources (texture and definition) for a given GID.
    ///     Returns default resources if GID belongs to default tileset, otherwise loads the resolved tileset.
    /// </summary>
    /// <param name="gid">The Global ID of the tile.</param>
    /// <param name="tilesetRefs">List of tileset references sorted by firstGid descending.</param>
    /// <param name="defaultTilesetId">The default tileset ID from TileDataComponent.</param>
    /// <param name="defaultTexture">The default tileset texture.</param>
    /// <param name="defaultDefinition">The default tileset definition.</param>
    /// <returns>Tuple of (texture, definition, tilesetId, firstGid) or (null, null, empty, 0) if resolution fails.</returns>
    private (
        Texture2D? Texture,
        TilesetDefinition? Definition,
        string TilesetId,
        int FirstGid
    ) ResolveTilesetResources(
        int gid,
        System.Collections.Generic.IReadOnlyList<TilesetReference> tilesetRefs,
        string defaultTilesetId,
        Texture2D defaultTexture,
        TilesetDefinition defaultDefinition
    )
    {
        // Resolve tileset for this GID
        var (resolvedTilesetId, resolvedFirstGid) = TilesetResolver.ResolveTilesetForGid(gid, tilesetRefs);
        if (string.IsNullOrEmpty(resolvedTilesetId))
        {
            _logger.Debug(
                "Failed to resolve tileset for GID {Gid} in map with {TilesetCount} tileset(s)",
                gid,
                tilesetRefs.Count
            );
            return (null, null, string.Empty, 0);
        }

        // Use default resources if tileset matches default
        if (resolvedTilesetId == defaultTilesetId)
            return (defaultTexture, defaultDefinition, resolvedTilesetId, resolvedFirstGid);

        // Load resolved tileset resources
        try
        {
            var texture = _resourceManager.LoadTexture(resolvedTilesetId);
            var definition = _resourceManager.GetTilesetDefinition(resolvedTilesetId);
            return (texture, definition, resolvedTilesetId, resolvedFirstGid);
        }
        catch (Exception ex)
        {
            _logger.Debug(
                ex,
                "Failed to load tileset resources for GID {Gid}, tileset {TilesetId}",
                gid,
                resolvedTilesetId
            );
            return (null, null, string.Empty, 0);
        }
    }
}
