using Arch.Core;
using Microsoft.Xna.Framework.Graphics;
using MonoBall.Core.ECS.Components;

namespace MonoBall.Core.ECS.Rendering;

/// <summary>
///     Interface for tile chunk rendering.
///     Implementations render tile chunks within an existing SpriteBatch session.
/// </summary>
public interface ITileChunkRenderer
{
    /// <summary>
    ///     Renders a tile chunk within an existing SpriteBatch session.
    /// </summary>
    /// <param name="world">The ECS world (for accessing AnimatedTileDataComponent).</param>
    /// <param name="chunkEntity">The chunk entity.</param>
    /// <param name="chunk">The tile chunk component.</param>
    /// <param name="data">The tile data component.</param>
    /// <param name="pos">The position component.</param>
    /// <param name="render">The renderable component.</param>
    /// <param name="spriteBatch">The SpriteBatch to draw to (must be in an active Begin/End block).</param>
    /// <returns>The number of tiles rendered.</returns>
    int RenderChunk(
        World world,
        Entity chunkEntity,
        TileChunkComponent chunk,
        TileDataComponent data,
        PositionComponent pos,
        RenderableComponent render,
        SpriteBatch spriteBatch
    );
}
