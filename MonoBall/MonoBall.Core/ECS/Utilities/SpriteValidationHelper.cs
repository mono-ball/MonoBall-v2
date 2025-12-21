using System;
using MonoBall.Core.Maps;
using Serilog;

namespace MonoBall.Core.ECS.Utilities
{
    /// <summary>
    /// Helper class for validating sprite definitions and animations.
    /// </summary>
    public static class SpriteValidationHelper
    {
        /// <summary>
        /// Validates that a sprite definition exists and an animation exists within it.
        /// </summary>
        /// <param name="spriteLoader">The sprite loader service.</param>
        /// <param name="spriteId">The sprite definition ID to validate.</param>
        /// <param name="animationName">The animation name to validate.</param>
        /// <param name="entityType">The type of entity (e.g., "Player", "NPC") for logging purposes.</param>
        /// <param name="entityId">The entity ID for logging purposes.</param>
        /// <param name="throwOnInvalid">If true, throws ArgumentException on validation failure. If false, returns false and logs warning.</param>
        /// <returns>True if valid, false if invalid (only when throwOnInvalid is false).</returns>
        /// <exception cref="ArgumentException">Thrown if throwOnInvalid is true and validation fails.</exception>
        public static bool ValidateSpriteAndAnimation(
            ISpriteLoaderService spriteLoader,
            string spriteId,
            string animationName,
            string entityType,
            string entityId,
            bool throwOnInvalid = true
        )
        {
            // Validate sprite definition exists
            if (!spriteLoader.ValidateSpriteDefinition(spriteId))
            {
                string message =
                    $"{entityType} '{entityId}': Sprite definition not found: {spriteId}";
                if (throwOnInvalid)
                {
                    throw new ArgumentException(message, nameof(spriteId));
                }

                Log.Warning("SpriteValidationHelper: {Message}", message);
                return false;
            }

            // Validate animation exists
            if (!spriteLoader.ValidateAnimation(spriteId, animationName))
            {
                string message =
                    $"{entityType} '{entityId}': Animation '{animationName}' not found in sprite sheet '{spriteId}'";
                if (throwOnInvalid)
                {
                    throw new ArgumentException(message, nameof(animationName));
                }

                Log.Warning("SpriteValidationHelper: {Message}", message);
                return false;
            }

            return true;
        }

        /// <summary>
        /// Validates that a sprite definition exists.
        /// </summary>
        /// <param name="spriteLoader">The sprite loader service.</param>
        /// <param name="spriteId">The sprite definition ID to validate.</param>
        /// <param name="entityType">The type of entity (e.g., "Player", "NPC") for logging purposes.</param>
        /// <param name="entityId">The entity ID for logging purposes.</param>
        /// <param name="throwOnInvalid">If true, throws ArgumentException on validation failure. If false, returns false and logs warning.</param>
        /// <returns>True if valid, false if invalid (only when throwOnInvalid is false).</returns>
        /// <exception cref="ArgumentException">Thrown if throwOnInvalid is true and validation fails.</exception>
        public static bool ValidateSpriteDefinition(
            ISpriteLoaderService spriteLoader,
            string spriteId,
            string entityType,
            string entityId,
            bool throwOnInvalid = true
        )
        {
            if (!spriteLoader.ValidateSpriteDefinition(spriteId))
            {
                string message =
                    $"{entityType} '{entityId}': Sprite definition not found: {spriteId}";
                if (throwOnInvalid)
                {
                    throw new ArgumentException(message, nameof(spriteId));
                }

                Log.Warning("SpriteValidationHelper: {Message}", message);
                return false;
            }

            return true;
        }
    }
}
