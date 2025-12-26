using System;
using Microsoft.Xna.Framework;

namespace MonoBall.Core.Scripting.Utilities
{
    /// <summary>
    /// Utility class for parsing Vector2 values from strings.
    /// Provides consistent, fail-fast parsing with clear error messages.
    /// </summary>
    public static class Vector2Parser
    {
        /// <summary>
        /// Parses a Vector2 from a string (format: "X,Y").
        /// </summary>
        /// <param name="value">The string value to parse (format: "X,Y").</param>
        /// <returns>The parsed Vector2 value.</returns>
        /// <exception cref="ArgumentNullException">Thrown if value is null or empty.</exception>
        /// <exception cref="FormatException">Thrown if the string format is invalid or coordinates cannot be parsed.</exception>
        public static Vector2 Parse(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException(
                    "Value cannot be null or empty. Expected format: 'X,Y' (e.g., '1.5,2.0')",
                    nameof(value)
                );
            }

            var parts = value.Split(',');
            if (parts.Length != 2)
            {
                throw new FormatException(
                    $"Invalid Vector2 format: '{value}'. Expected format: 'X,Y' (e.g., '1.5,2.0')"
                );
            }

            if (!float.TryParse(parts[0].Trim(), out var x))
            {
                throw new FormatException(
                    $"Invalid Vector2 X coordinate: '{parts[0].Trim()}'. Expected a valid float value."
                );
            }

            if (!float.TryParse(parts[1].Trim(), out var y))
            {
                throw new FormatException(
                    $"Invalid Vector2 Y coordinate: '{parts[1].Trim()}'. Expected a valid float value."
                );
            }

            return new Vector2(x, y);
        }

        /// <summary>
        /// Attempts to parse a Vector2 from a string, returning a default value on failure.
        /// </summary>
        /// <param name="value">The string value to parse.</param>
        /// <param name="defaultValue">The default value to return if parsing fails.</param>
        /// <returns>The parsed Vector2, or defaultValue if parsing fails.</returns>
        public static Vector2 ParseOrDefault(string value, Vector2 defaultValue = default)
        {
            try
            {
                return Parse(value);
            }
            catch
            {
                return defaultValue;
            }
        }
    }
}
