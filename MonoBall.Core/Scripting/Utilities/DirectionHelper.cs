using System;
using MonoBall.Core.ECS.Components;

namespace MonoBall.Core.Scripting.Utilities;

/// <summary>
///     Utility class for direction manipulation operations.
///     Provides pure functions for rotating, inverting, and calculating directions.
/// </summary>
public static class DirectionHelper
{
    /// <summary>
    ///     Rotates a direction 90 degrees clockwise or counter-clockwise.
    /// </summary>
    /// <param name="dir">The direction to rotate.</param>
    /// <param name="clockwise">True for clockwise rotation, false for counter-clockwise.</param>
    /// <returns>The rotated direction.</returns>
    public static Direction Rotate(Direction dir, bool clockwise)
    {
        return clockwise ? RotateClockwise(dir) : RotateCounterClockwise(dir);
    }

    /// <summary>
    ///     Rotates a direction 90 degrees clockwise.
    ///     North -> East -> South -> West -> North
    /// </summary>
    /// <param name="dir">The direction to rotate.</param>
    /// <returns>The rotated direction.</returns>
    public static Direction RotateClockwise(Direction dir)
    {
        return dir switch
        {
            Direction.North => Direction.East,
            Direction.East => Direction.South,
            Direction.South => Direction.West,
            Direction.West => Direction.North,
            _ => dir,
        };
    }

    /// <summary>
    ///     Rotates a direction 90 degrees counter-clockwise.
    ///     North -> West -> South -> East -> North
    /// </summary>
    /// <param name="dir">The direction to rotate.</param>
    /// <returns>The rotated direction.</returns>
    public static Direction RotateCounterClockwise(Direction dir)
    {
        return dir switch
        {
            Direction.North => Direction.West,
            Direction.West => Direction.South,
            Direction.South => Direction.East,
            Direction.East => Direction.North,
            _ => dir,
        };
    }

    /// <summary>
    ///     Gets the opposite direction.
    ///     North <-> South, East <-> West
    /// </summary>
    /// <param name="dir">The direction.</param>
    /// <returns>The opposite direction.</returns>
    public static Direction GetOpposite(Direction dir)
    {
        return dir switch
        {
            Direction.North => Direction.South,
            Direction.South => Direction.North,
            Direction.East => Direction.West,
            Direction.West => Direction.East,
            _ => dir,
        };
    }

    /// <summary>
    ///     Calculates the primary direction from one point to another.
    ///     Uses the axis with the larger delta.
    /// </summary>
    /// <param name="fromX">Source X coordinate.</param>
    /// <param name="fromY">Source Y coordinate.</param>
    /// <param name="toX">Target X coordinate.</param>
    /// <param name="toY">Target Y coordinate.</param>
    /// <returns>The primary direction from source to target.</returns>
    public static Direction GetDirectionTo(int fromX, int fromY, int toX, int toY)
    {
        var dx = toX - fromX;
        var dy = toY - fromY;

        // Determine primary axis (larger delta)
        if (Math.Abs(dx) > Math.Abs(dy))
            return dx > 0 ? Direction.East : Direction.West;

        return dy > 0 ? Direction.South : Direction.North;
    }

    /// <summary>
    ///     Gets a random direction from the four cardinal directions.
    /// </summary>
    /// <returns>A random cardinal direction.</returns>
    public static Direction GetRandomDirection()
    {
        var directions = new[] { Direction.North, Direction.South, Direction.East, Direction.West };
        return directions[Random.Shared.Next(directions.Length)];
    }

    /// <summary>
    ///     Gets a random direction from the allowed directions.
    /// </summary>
    /// <param name="allowed">Array of allowed directions.</param>
    /// <returns>A random direction from the allowed array.</returns>
    /// <exception cref="ArgumentException">Thrown if allowed is null or empty.</exception>
    public static Direction GetRandomDirection(Direction[] allowed)
    {
        if (allowed == null || allowed.Length == 0)
            throw new ArgumentException(
                "Allowed directions cannot be null or empty.",
                nameof(allowed)
            );

        return allowed[Random.Shared.Next(allowed.Length)];
    }
}
