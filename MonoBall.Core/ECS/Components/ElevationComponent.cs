namespace MonoBall.Core.ECS.Components;

/// <summary>
///     Component that stores the elevation (z-order) of an entity.
///     Used for rendering order and collision detection.
/// </summary>
public struct ElevationComponent
{
    /// <summary>
    ///     Ground level elevation (water, ground tiles).
    /// </summary>
    public const byte Ground = 0;

    /// <summary>
    ///     Default elevation for most entities.
    /// </summary>
    public const byte Default = 3;

    /// <summary>
    ///     Bridge level elevation (above ground, below overhead).
    /// </summary>
    public const byte Bridge = 6;

    /// <summary>
    ///     Overhead elevation (treetops, roofs).
    /// </summary>
    public const byte Overhead = 9;

    /// <summary>
    ///     Maximum elevation value.
    /// </summary>
    public const byte Max = 15;

    private byte _value;

    /// <summary>
    ///     The elevation value (0-15). Values above 15 are clamped.
    /// </summary>
    public byte Value
    {
        get => _value;
        set => _value = value > Max ? Max : value;
    }

    /// <summary>
    ///     Returns true if this entity is at ground level.
    /// </summary>
    public bool IsGroundLevel => _value == Ground;

    /// <summary>
    ///     Returns true if this entity is at bridge level or higher.
    /// </summary>
    public bool IsBridgeOrHigher => _value >= Bridge;

    /// <summary>
    ///     Returns true if this entity is at overhead level or higher.
    /// </summary>
    public bool IsOverhead => _value >= Overhead;

    /// <summary>
    ///     Implicit conversion from byte to ElevationComponent.
    /// </summary>
    public static implicit operator ElevationComponent(byte value) => new() { Value = value };

    /// <summary>
    ///     Implicit conversion from ElevationComponent to byte.
    /// </summary>
    public static implicit operator byte(ElevationComponent elevation) => elevation.Value;
}
