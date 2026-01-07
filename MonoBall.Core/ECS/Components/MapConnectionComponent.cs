namespace MonoBall.Core.ECS.Components;

/// <summary>
///     Component that stores connection information between maps.
/// </summary>
public struct MapConnectionComponent
{
    /// <summary>
    ///     The direction of the connection.
    /// </summary>
    public MapConnectionDirection Direction { get; set; }

    /// <summary>
    ///     The target map ID.
    /// </summary>
    public string TargetMapId { get; set; }

    /// <summary>
    ///     The offset in tiles from the current map.
    /// </summary>
    public int Offset { get; set; }
}

/// <summary>
///     Enumeration of map connection directions.
/// </summary>
public enum MapConnectionDirection
{
    North,
    South,
    East,
    West,
}
