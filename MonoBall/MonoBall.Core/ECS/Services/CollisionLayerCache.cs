using System;
using System.Collections.Generic;

namespace MonoBall.Core.ECS.Services;

/// <summary>
///     Cache for per-elevation tile collision data.
///     Stores collision arrays per (map, elevation) for O(1) lookups.
/// </summary>
public sealed class CollisionLayerCache : ICollisionLayerCache
{
    private readonly Dictionary<string, MapCollisionData> _mapData = new();

    /// <inheritdoc />
    public byte? GetCollisionValue(string mapId, int x, int y, int elevation)
    {
        if (!_mapData.TryGetValue(mapId, out var data))
            return null;

        if (x < 0 || x >= data.Width || y < 0 || y >= data.Height)
            return null;

        // Check for collision layer at this elevation
        if (data.CollisionLayers.TryGetValue(elevation, out var layer))
        {
            // Apply layer offset to get local coordinates
            var localX = x - layer.OffsetX;
            var localY = y - layer.OffsetY;

            // Check if position is within this layer's bounds
            if (localX < 0 || localX >= layer.Width || localY < 0 || localY >= layer.Height)
                return 0; // Outside layer bounds = passable

            return layer.Data[localY * layer.Width + localX];
        }

        // No collision layer for this elevation means passable
        return 0;
    }

    /// <inheritdoc />
    public bool IsInBounds(string mapId, int x, int y)
    {
        if (!_mapData.TryGetValue(mapId, out var data))
            return false;

        return x >= 0 && x < data.Width && y >= 0 && y < data.Height;
    }

    /// <inheritdoc />
    public bool HasMapData(string mapId)
    {
        return _mapData.ContainsKey(mapId);
    }

    /// <inheritdoc />
    public void LoadCollisionLayer(
        string mapId,
        int elevation,
        byte[] collisionData,
        int width,
        int height,
        int offsetX = 0,
        int offsetY = 0
    )
    {
        ArgumentNullException.ThrowIfNull(mapId);
        ArgumentNullException.ThrowIfNull(collisionData);

        if (width <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "Width must be positive.");
        if (height <= 0)
            throw new ArgumentOutOfRangeException(nameof(height), "Height must be positive.");

        var expectedSize = width * height;
        if (collisionData.Length < expectedSize)
            throw new ArgumentException(
                $"Collision data size ({collisionData.Length}) is smaller than expected ({expectedSize}).",
                nameof(collisionData)
            );

        // Get or create map data
        if (!_mapData.TryGetValue(mapId, out var data))
        {
            data = new MapCollisionData
            {
                Width = width,
                Height = height,
                CollisionLayers = new Dictionary<int, CollisionLayerData>(),
            };
            _mapData[mapId] = data;
        }

        // Store collision layer for this elevation with offset information
        data.CollisionLayers[elevation] = new CollisionLayerData
        {
            Data = collisionData,
            Width = width,
            Height = height,
            OffsetX = offsetX,
            OffsetY = offsetY,
        };
    }

    /// <inheritdoc />
    public void SetMapDimensions(string mapId, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(mapId);

        if (width <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "Width must be positive.");
        if (height <= 0)
            throw new ArgumentOutOfRangeException(nameof(height), "Height must be positive.");

        if (!_mapData.TryGetValue(mapId, out var data))
        {
            data = new MapCollisionData
            {
                Width = width,
                Height = height,
                CollisionLayers = new Dictionary<int, CollisionLayerData>(),
            };
            _mapData[mapId] = data;
        }
        else
        {
            data.Width = width;
            data.Height = height;
        }
    }

    /// <inheritdoc />
    public void UnloadMap(string mapId)
    {
        _mapData.Remove(mapId);
    }

    /// <summary>
    ///     Internal structure to hold per-map collision data.
    /// </summary>
    private sealed class MapCollisionData
    {
        public required int Width { get; set; }
        public required int Height { get; set; }
        public required Dictionary<int, CollisionLayerData> CollisionLayers { get; init; }
    }

    /// <summary>
    ///     Internal structure to hold per-layer collision data with offset.
    /// </summary>
    private sealed class CollisionLayerData
    {
        public required byte[] Data { get; init; }
        public required int Width { get; init; }
        public required int Height { get; init; }
        public required int OffsetX { get; init; }
        public required int OffsetY { get; init; }
    }
}
