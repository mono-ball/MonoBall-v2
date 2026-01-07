using Arch.Core;
using Microsoft.Xna.Framework.Graphics;
using MonoBall.Core.ECS.Components;

namespace MonoBall.Core.ECS.Rendering;

/// <summary>
///     Base class for renderable items in the elevation-based rendering system.
///     Uses inheritance for type-specific data to avoid memory waste.
/// </summary>
public abstract class RenderableItem
{
    /// <summary>
    ///     Render order for tiles (rendered first within elevation).
    /// </summary>
    public const byte RenderOrderTile = 0;

    /// <summary>
    ///     Render order for sprites (rendered after tiles within elevation).
    /// </summary>
    public const byte RenderOrderSprite = 1;

    /// <summary>
    ///     The entity being rendered.
    /// </summary>
    public required Entity Entity { get; init; }

    /// <summary>
    ///     The elevation (0-15) for sorting.
    /// </summary>
    public required byte Elevation { get; init; }

    /// <summary>
    ///     The render order within the same elevation (tiles=0, sprites=1).
    ///     Tiles render before sprites at each elevation level.
    /// </summary>
    public abstract byte RenderOrder { get; }

    /// <summary>
    ///     The Y position for tertiary sorting within the same elevation and render order.
    /// </summary>
    public required float YPosition { get; init; }

    /// <summary>
    ///     Renders this item using the appropriate helper renderer.
    /// </summary>
    /// <param name="world">The ECS world.</param>
    /// <param name="spriteBatch">The active SpriteBatch.</param>
    /// <param name="tileRenderer">The tile chunk renderer.</param>
    /// <param name="spriteRenderer">The sprite renderer.</param>
    public abstract void Render(
        World world,
        SpriteBatch spriteBatch,
        ITileChunkRenderer tileRenderer,
        ISpriteRenderer spriteRenderer
    );
}

/// <summary>
///     Renderable item for tile chunks.
/// </summary>
public sealed class TileChunkRenderableItem : RenderableItem
{
    /// <inheritdoc />
    public override byte RenderOrder => RenderOrderTile;

    /// <summary>
    ///     The tile chunk component.
    /// </summary>
    public required TileChunkComponent Chunk { get; init; }

    /// <summary>
    ///     The tile data component.
    /// </summary>
    public required TileDataComponent Data { get; init; }

    /// <summary>
    ///     The position component.
    /// </summary>
    public required PositionComponent Position { get; init; }

    /// <summary>
    ///     The renderable component.
    /// </summary>
    public required RenderableComponent Renderable { get; init; }

    /// <inheritdoc />
    public override void Render(
        World world,
        SpriteBatch spriteBatch,
        ITileChunkRenderer tileRenderer,
        ISpriteRenderer spriteRenderer
    )
    {
        tileRenderer.RenderChunk(world, Entity, Chunk, Data, Position, Renderable, spriteBatch);
    }
}

/// <summary>
///     Renderable item for sprites.
/// </summary>
public sealed class SpriteRenderableItem : RenderableItem
{
    /// <inheritdoc />
    public override byte RenderOrder => RenderOrderSprite;

    /// <summary>
    ///     The sprite component.
    /// </summary>
    public required SpriteComponent Sprite { get; init; }

    /// <summary>
    ///     The position component.
    /// </summary>
    public required PositionComponent Position { get; init; }

    /// <summary>
    ///     The renderable component.
    /// </summary>
    public required RenderableComponent Renderable { get; init; }

    /// <inheritdoc />
    public override void Render(
        World world,
        SpriteBatch spriteBatch,
        ITileChunkRenderer tileRenderer,
        ISpriteRenderer spriteRenderer
    )
    {
        spriteRenderer.RenderSprite(world, Entity, Sprite, Position, Renderable, spriteBatch);
    }
}
