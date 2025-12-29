namespace MonoBall.Core.ECS.Components;

/// <summary>
///     Represents the four cardinal directions for movement and facing.
///     Uses Pokemon Emerald's naming convention (North/South/East/West).
///     Matches MonoBall's Direction enum structure.
/// </summary>
public enum Direction
{
    /// <summary>
    ///     No direction / neutral.
    /// </summary>
    None = -1,

    /// <summary>
    ///     Facing south (down on screen).
    /// </summary>
    South = 0,

    /// <summary>
    ///     Facing west (left on screen).
    /// </summary>
    West = 1,

    /// <summary>
    ///     Facing east (right on screen).
    /// </summary>
    East = 2,

    /// <summary>
    ///     Facing north (up on screen).
    /// </summary>
    North = 3,
}

/// <summary>
///     Extension methods for Direction enum.
///     Matches MonoBall's DirectionExtensions.
/// </summary>
public static class DirectionExtensions
{
    /// <summary>
    ///     Converts a direction to a movement delta in tile coordinates.
    /// </summary>
    /// <param name="direction">The direction.</param>
    /// <returns>A tuple (deltaX, deltaY) representing the movement in tiles.</returns>
    public static (int deltaX, int deltaY) ToTileDelta(this Direction direction)
    {
        return direction switch
        {
            Direction.South => (0, 1),
            Direction.West => (-1, 0),
            Direction.East => (1, 0),
            Direction.North => (0, -1),
            _ => (0, 0),
        };
    }

    /// <summary>
    ///     Converts a direction to its lowercase string representation for animation names.
    ///     (e.g., Direction.South -> "south")
    /// </summary>
    /// <param name="direction">The direction.</param>
    /// <returns>The lowercase direction name.</returns>
    public static string ToAnimationSuffix(this Direction direction)
    {
        return direction switch
        {
            Direction.South => "south",
            Direction.North => "north",
            Direction.West => "west",
            Direction.East => "east",
            _ => "south",
        };
    }

    /// <summary>
    ///     Gets the animation name for walking in this direction.
    ///     Uses Pokemon Emerald's "go_*" naming convention.
    /// </summary>
    /// <param name="direction">The direction.</param>
    /// <returns>The walk animation name (e.g., "go_south").</returns>
    public static string ToWalkAnimation(this Direction direction)
    {
        return $"go_{direction.ToAnimationSuffix()}";
    }

    /// <summary>
    ///     Gets the animation name for idling/facing in this direction.
    ///     Uses Pokemon Emerald's "face_*" naming convention.
    /// </summary>
    /// <param name="direction">The direction.</param>
    /// <returns>The idle animation name (e.g., "face_south").</returns>
    public static string ToIdleAnimation(this Direction direction)
    {
        return $"face_{direction.ToAnimationSuffix()}";
    }

    /// <summary>
    ///     Gets the animation name for turning in place in this direction.
    ///     Pokemon Emerald uses WALK_IN_PLACE_FAST which uses GetMoveDirectionFastAnimNum()
    ///     (ANIM_STD_GO_FAST_*) and plays for 8 frames at 60fps = ~133ms.
    ///     We use "go_fast_*" to match the same animation variant, played with PlayOnce.
    /// </summary>
    /// <param name="direction">The direction.</param>
    /// <returns>The turn animation name (e.g., "go_fast_south").</returns>
    public static string ToTurnAnimation(this Direction direction)
    {
        return $"go_fast_{direction.ToAnimationSuffix()}";
    }

    /// <summary>
    ///     Gets the opposite direction.
    /// </summary>
    /// <param name="direction">The direction.</param>
    /// <returns>The opposite direction.</returns>
    public static Direction Opposite(this Direction direction)
    {
        return direction switch
        {
            Direction.South => Direction.North,
            Direction.West => Direction.East,
            Direction.East => Direction.West,
            Direction.North => Direction.South,
            _ => direction,
        };
    }
}
