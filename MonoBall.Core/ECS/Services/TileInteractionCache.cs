using System.Collections.Generic;

namespace MonoBall.Core.ECS.Services;

/// <summary>
///     Cache for tile interaction script IDs.
///     Uses nested dictionaries for O(1) lookup by map and position.
/// </summary>
public sealed class TileInteractionCache : ITileInteractionCache
{
    private readonly Dictionary<string, Dictionary<(int X, int Y, int Elevation), string?>> _cache =
        new();

    /// <inheritdoc />
    public string? GetTileInteractionId(string mapId, int x, int y, int elevation)
    {
        if (!_cache.TryGetValue(mapId, out var mapInteractions))
            return null;

        mapInteractions.TryGetValue((x, y, elevation), out var interactionId);
        return interactionId;
    }

    /// <inheritdoc />
    public void SetTileInteraction(string mapId, int x, int y, int elevation, string? interactionId)
    {
        if (!_cache.TryGetValue(mapId, out var mapInteractions))
        {
            mapInteractions = new Dictionary<(int X, int Y, int Elevation), string?>();
            _cache[mapId] = mapInteractions;
        }

        var key = (x, y, elevation);
        if (interactionId == null)
            mapInteractions.Remove(key);
        else
            mapInteractions[key] = interactionId;
    }

    /// <inheritdoc />
    public void UnloadMap(string mapId)
    {
        _cache.Remove(mapId);
    }
}
