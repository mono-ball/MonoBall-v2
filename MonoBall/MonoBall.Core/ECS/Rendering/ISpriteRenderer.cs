using Arch.Core;
using Microsoft.Xna.Framework.Graphics;
using MonoBall.Core.ECS.Components;

namespace MonoBall.Core.ECS.Rendering;

/// <summary>
///     Interface for sprite rendering.
///     Implementations render sprites within an existing SpriteBatch session.
/// </summary>
public interface ISpriteRenderer
{
    /// <summary>
    ///     Renders a sprite within an existing SpriteBatch session.
    /// </summary>
    /// <param name="world">The ECS world (for accessing additional components if needed).</param>
    /// <param name="entity">The sprite entity.</param>
    /// <param name="sprite">The sprite component.</param>
    /// <param name="pos">The position component.</param>
    /// <param name="render">The renderable component.</param>
    /// <param name="spriteBatch">The SpriteBatch to draw to (must be in an active Begin/End block).</param>
    void RenderSprite(
        World world,
        Entity entity,
        SpriteComponent sprite,
        PositionComponent pos,
        RenderableComponent render,
        SpriteBatch spriteBatch
    );

    /// <summary>
    ///     Gets the per-entity shader for a sprite if one is configured.
    /// </summary>
    /// <param name="world">The ECS world.</param>
    /// <param name="entity">The sprite entity.</param>
    /// <returns>The shader effect, or null if no per-entity shader.</returns>
    Effect? GetEntityShader(World world, Entity entity);
}
