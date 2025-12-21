using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace MonoBall.Core.Maps
{
    /// <summary>
    /// Service interface for loading and caching sprite definitions and textures.
    /// </summary>
    public interface ISpriteLoaderService
    {
        /// <summary>
        /// Gets a sprite definition by ID.
        /// </summary>
        /// <param name="spriteId">The sprite ID.</param>
        /// <returns>The sprite definition, or null if not found.</returns>
        SpriteDefinition? GetSpriteDefinition(string spriteId);

        /// <summary>
        /// Gets a sprite texture, loading it if not already cached.
        /// </summary>
        /// <param name="spriteId">The sprite ID.</param>
        /// <returns>The sprite texture, or null if not found or loading failed.</returns>
        Texture2D? GetSpriteTexture(string spriteId);

        /// <summary>
        /// Gets the cached animation frame sequence for a sprite animation.
        /// </summary>
        /// <param name="spriteId">The sprite ID.</param>
        /// <param name="animationName">The animation name.</param>
        /// <returns>The list of animation frames, or null if not found.</returns>
        IReadOnlyList<SpriteAnimationFrame>? GetAnimationFrames(
            string spriteId,
            string animationName
        );

        /// <summary>
        /// Gets the source rectangle for a specific frame in an animation.
        /// </summary>
        /// <param name="spriteId">The sprite ID.</param>
        /// <param name="animationName">The animation name.</param>
        /// <param name="frameIndex">The frame index within the animation sequence.</param>
        /// <returns>The source rectangle, or null if not found.</returns>
        Rectangle? GetAnimationFrameRectangle(
            string spriteId,
            string animationName,
            int frameIndex
        );

        /// <summary>
        /// Validates that a sprite definition exists.
        /// </summary>
        /// <param name="spriteId">The sprite ID to validate.</param>
        /// <returns>True if the sprite definition exists, false otherwise.</returns>
        bool ValidateSpriteDefinition(string spriteId);

        /// <summary>
        /// Validates that an animation exists for a sprite.
        /// </summary>
        /// <param name="spriteId">The sprite ID.</param>
        /// <param name="animationName">The animation name to validate.</param>
        /// <returns>True if the animation exists, false otherwise.</returns>
        bool ValidateAnimation(string spriteId, string animationName);
    }
}
