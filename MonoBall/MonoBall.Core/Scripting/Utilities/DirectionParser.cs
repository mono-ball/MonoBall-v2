using System;
using System.Collections.Generic;
using System.Linq;
using MonoBall.Core.ECS.Components;

namespace MonoBall.Core.Scripting.Utilities
{
    /// <summary>
    /// Utility class for parsing direction strings to Direction enum values.
    /// </summary>
    public static class DirectionParser
    {
        /// <summary>
        /// Parses a direction string to a Direction enum value.
        /// Supports multiple formats: "up"/"north", "down"/"south", "left"/"west", "right"/"east".
        /// </summary>
        /// <param name="directionStr">The direction string to parse.</param>
        /// <param name="defaultDirection">The default direction to return if parsing fails (default: South).</param>
        /// <returns>The parsed Direction value, or defaultDirection if parsing fails.</returns>
        public static Direction Parse(
            string directionStr,
            Direction defaultDirection = Direction.South
        )
        {
            if (string.IsNullOrWhiteSpace(directionStr))
            {
                return defaultDirection;
            }

            return directionStr.ToLowerInvariant().Trim() switch
            {
                "up" or "north" => Direction.North,
                "down" or "south" => Direction.South,
                "left" or "west" => Direction.West,
                "right" or "east" => Direction.East,
                _ => defaultDirection,
            };
        }

        /// <summary>
        /// Parses a comma-separated list of direction strings to an array of Direction values.
        /// </summary>
        /// <param name="directionsStr">Comma-separated direction strings (e.g., "up,down,left,right").</param>
        /// <returns>Array of parsed Direction values, or empty array if parsing fails.</returns>
        public static Direction[] ParseList(string directionsStr)
        {
            if (string.IsNullOrWhiteSpace(directionsStr))
            {
                return Array.Empty<Direction>();
            }

            var directionNames = directionsStr
                .Split(',')
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToArray();

            var parsedDirections = new List<Direction>();
            foreach (var name in directionNames)
            {
                var dir = Parse(name, Direction.None);
                if (dir != Direction.None)
                {
                    parsedDirections.Add(dir);
                }
            }

            return parsedDirections.ToArray();
        }
    }
}
