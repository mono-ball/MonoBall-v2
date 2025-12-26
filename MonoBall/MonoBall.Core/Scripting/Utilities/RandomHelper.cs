using System;

namespace MonoBall.Core.Scripting.Utilities
{
    /// <summary>
    /// Utility class for random number generation.
    /// Provides convenient methods for generating random numbers in various ranges.
    /// </summary>
    public static class RandomHelper
    {
        /// <summary>
        /// Generates a random float in the specified range [min, max).
        /// </summary>
        /// <param name="min">The minimum value (inclusive).</param>
        /// <param name="max">The maximum value (exclusive).</param>
        /// <returns>A random float in the range [min, max).</returns>
        /// <exception cref="ArgumentException">Thrown if min is greater than or equal to max.</exception>
        public static float RandomFloat(float min, float max)
        {
            if (min >= max)
            {
                throw new ArgumentException("Min must be less than max.", nameof(min));
            }

            return (float)(Random.Shared.NextDouble() * (max - min) + min);
        }

        /// <summary>
        /// Generates a random integer in the specified range [min, max).
        /// </summary>
        /// <param name="min">The minimum value (inclusive).</param>
        /// <param name="max">The maximum value (exclusive).</param>
        /// <returns>A random integer in the range [min, max).</returns>
        public static int RandomInt(int min, int max)
        {
            return Random.Shared.Next(min, max);
        }

        /// <summary>
        /// Generates a random integer in the specified range [min, max] (inclusive).
        /// </summary>
        /// <param name="min">The minimum value (inclusive).</param>
        /// <param name="max">The maximum value (inclusive).</param>
        /// <returns>A random integer in the range [min, max].</returns>
        /// <exception cref="ArgumentException">Thrown if min is greater than max.</exception>
        public static int RandomIntInclusive(int min, int max)
        {
            if (min > max)
            {
                throw new ArgumentException("Min must be less than or equal to max.", nameof(min));
            }

            return Random.Shared.Next(min, max + 1);
        }
    }
}
