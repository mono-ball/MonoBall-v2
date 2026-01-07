using System;
using MonoBall.Core.Resources;
using Serilog;

namespace MonoBall.Core.ECS.Utilities;

/// <summary>
///     Helper class for validating sprite definitions and animations.
/// </summary>
public static class SpriteValidationHelper
{
    /// <summary>
    ///     Validates that a sprite definition exists and an animation exists within it.
    /// </summary>
    /// <param name="resourceManager">The resource manager.</param>
    /// <param name="logger">The logger instance for logging validation warnings.</param>
    /// <param name="spriteId">The sprite definition ID to validate.</param>
    /// <param name="animationName">The animation name to validate.</param>
    /// <param name="entityType">The type of entity (e.g., "Player", "NPC") for logging purposes.</param>
    /// <param name="entityId">The entity ID for logging purposes.</param>
    /// <param name="throwOnInvalid">
    ///     If true, throws ArgumentException on validation failure. If false, returns false and logs
    ///     warning.
    /// </param>
    /// <returns>True if valid, false if invalid (only when throwOnInvalid is false).</returns>
    /// <exception cref="ArgumentException">Thrown if throwOnInvalid is true and validation fails.</exception>
    public static bool ValidateSpriteAndAnimation(
        IResourceManager resourceManager,
        ILogger logger,
        string spriteId,
        string animationName,
        string entityType,
        string entityId,
        bool throwOnInvalid = true
    )
    {
        if (logger == null)
            throw new ArgumentNullException(nameof(logger));

        // Validate sprite definition exists
        if (!resourceManager.ValidateSpriteDefinition(spriteId))
        {
            var message = $"{entityType} '{entityId}': Sprite definition not found: {spriteId}";
            if (throwOnInvalid)
                throw new ArgumentException(message, nameof(spriteId));

            logger.Warning("{Message}", message);
            return false;
        }

        // Validate animation exists
        if (!resourceManager.ValidateAnimation(spriteId, animationName))
        {
            var message =
                $"{entityType} '{entityId}': Animation '{animationName}' not found in sprite sheet '{spriteId}'";
            if (throwOnInvalid)
                throw new ArgumentException(message, nameof(animationName));

            logger.Warning("{Message}", message);
            return false;
        }

        return true;
    }

    /// <summary>
    ///     Validates that a sprite definition exists.
    /// </summary>
    /// <param name="resourceManager">The resource manager.</param>
    /// <param name="logger">The logger instance for logging validation warnings.</param>
    /// <param name="spriteId">The sprite definition ID to validate.</param>
    /// <param name="entityType">The type of entity (e.g., "Player", "NPC") for logging purposes.</param>
    /// <param name="entityId">The entity ID for logging purposes.</param>
    /// <param name="throwOnInvalid">
    ///     If true, throws ArgumentException on validation failure. If false, returns false and logs
    ///     warning.
    /// </param>
    /// <returns>True if valid, false if invalid (only when throwOnInvalid is false).</returns>
    /// <exception cref="ArgumentException">Thrown if throwOnInvalid is true and validation fails.</exception>
    public static bool ValidateSpriteDefinition(
        IResourceManager resourceManager,
        ILogger logger,
        string spriteId,
        string entityType,
        string entityId,
        bool throwOnInvalid = true
    )
    {
        if (logger == null)
            throw new ArgumentNullException(nameof(logger));

        if (!resourceManager.ValidateSpriteDefinition(spriteId))
        {
            var message = $"{entityType} '{entityId}': Sprite definition not found: {spriteId}";
            if (throwOnInvalid)
                throw new ArgumentException(message, nameof(spriteId));

            logger.Warning("{Message}", message);
            return false;
        }

        return true;
    }
}
