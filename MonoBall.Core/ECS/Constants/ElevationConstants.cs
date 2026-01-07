namespace MonoBall.Core.ECS.Constants;

/// <summary>
///     Constants for elevation-based rendering.
/// </summary>
public static class ElevationConstants
{
    /// <summary>
    ///     Elevation at which bottom border layer is rendered.
    /// </summary>
    public const byte BorderBottomElevation = 3;

    /// <summary>
    ///     Elevation at which top border layer is rendered.
    /// </summary>
    public const byte BorderTopElevation = 9;

    /// <summary>
    ///     Default elevation for entities without explicit elevation.
    /// </summary>
    public const byte DefaultElevation = 3;
}
