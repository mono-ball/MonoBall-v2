using System;
using System.Collections.Generic;
using System.Linq;
using Arch.Core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoBall.Core.ECS.Components;
using MonoBall.Core.Maps;
using MonoBall.Core.Maps.Utilities;
using MonoBall.Core.Mods;
using MonoBall.Core.Resources;
using Serilog;

namespace MonoBall.Core.ECS.Rendering;

/// <summary>
///     Implementation of ITileChunkRenderer that renders tile chunks.
///     Extracted from MapRendererSystem for use with ElevationRendererSystem.
/// </summary>
internal sealed class TileChunkRenderer : ITileChunkRenderer
{
    private readonly DefinitionRegistry _definitionRegistry;
    private readonly ILogger _logger;
    private readonly IResourceManager _resourceManager;

    // Cache tileset refs per map to avoid repeated lookups
    private readonly Dictionary<string, List<TilesetReference>> _mapTilesetRefsCache = new();

    /// <summary>
    ///     Initializes a new instance of the TileChunkRenderer.
    /// </summary>
    /// <param name="resourceManager">The resource manager for loading textures and definitions.</param>
    /// <param name="definitionRegistry">The definition registry for map definitions.</param>
    /// <param name="logger">The logger.</param>
    public TileChunkRenderer(
        IResourceManager resourceManager,
        DefinitionRegistry definitionRegistry,
        ILogger logger
    )
    {
        _resourceManager =
            resourceManager ?? throw new ArgumentNullException(nameof(resourceManager));
        _definitionRegistry =
            definitionRegistry ?? throw new ArgumentNullException(nameof(definitionRegistry));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public int RenderChunk(
        World world,
        Entity chunkEntity,
        TileChunkComponent chunk,
        TileDataComponent data,
        PositionComponent pos,
        RenderableComponent render,
        SpriteBatch spriteBatch
    )
    {
        if (!render.IsVisible)
            return 0;

        if (data.TileIndices == null)
        {
            _logger.Warning(
                "TileChunkRenderer.RenderChunk: Chunk at ({X}, {Y}) has null TileIndices",
                pos.Position.X,
                pos.Position.Y
            );
            return 0;
        }

        // Get map ID directly from chunk component (O(1) lookup)
        // MapId is stored in TileChunkComponent to avoid expensive reverse relationship queries
        if (string.IsNullOrEmpty(chunk.MapId))
        {
            _logger.Warning(
                "TileChunkRenderer.RenderChunk: Chunk at ({X}, {Y}) has no MapId",
                pos.Position.X,
                pos.Position.Y
            );
            return 0;
        }

        var mapId = chunk.MapId;

        // Get tileset references for this map (cached)
        var tilesetRefs = GetTilesetRefsForMap(mapId);
        if (tilesetRefs.Count == 0)
        {
            _logger.Warning(
                "TileChunkRenderer.RenderChunk: Map {MapId} has no tileset references",
                mapId
            );
            return 0;
        }

        // Get default tileset texture
        Texture2D defaultTilesetTexture;
        try
        {
            defaultTilesetTexture = _resourceManager.LoadTexture(data.TilesetId);
        }
        catch (Exception ex)
        {
            _logger.Warning(
                ex,
                "TileChunkRenderer.RenderChunk: Failed to get tileset texture for {TilesetId}",
                data.TilesetId
            );
            return 0;
        }

        // Get default tileset definition for tile dimensions
        TilesetDefinition defaultTilesetDefinition;
        try
        {
            defaultTilesetDefinition = _resourceManager.GetTilesetDefinition(data.TilesetId);
        }
        catch (Exception ex)
        {
            _logger.Warning(
                ex,
                "TileChunkRenderer.RenderChunk: Failed to get tileset definition for {TilesetId}",
                data.TilesetId
            );
            return 0;
        }

        // Calculate color with opacity
        var color = Color.White * render.Opacity;

        var tilesRendered = 0;

        // Fast path: if no animated tiles, render all tiles normally
        if (!data.HasAnimatedTiles)
        {
            tilesRendered = RenderStaticTiles(
                chunk,
                data,
                pos,
                tilesetRefs,
                defaultTilesetTexture,
                defaultTilesetDefinition,
                color,
                spriteBatch
            );
        }
        else
        {
            tilesRendered = RenderAnimatedTiles(
                world,
                chunkEntity,
                chunk,
                data,
                pos,
                tilesetRefs,
                defaultTilesetTexture,
                defaultTilesetDefinition,
                color,
                spriteBatch
            );
        }

        return tilesRendered;
    }

    private int RenderStaticTiles(
        TileChunkComponent chunk,
        TileDataComponent data,
        PositionComponent pos,
        List<TilesetReference> tilesetRefs,
        Texture2D defaultTilesetTexture,
        TilesetDefinition defaultTilesetDefinition,
        Color color,
        SpriteBatch spriteBatch
    )
    {
        var tilesRendered = 0;

        for (var y = 0; y < chunk.ChunkHeight; y++)
        for (var x = 0; x < chunk.ChunkWidth; x++)
        {
            var tileIndex = y * chunk.ChunkWidth + x;
            if (tileIndex >= data.TileIndices.Length)
                continue;

            var rawGidWithFlags = data.TileIndices[tileIndex];
            var gid = TileConstants.GetRawGid(rawGidWithFlags);

            if (gid <= 0)
                continue;

            // Extract flip flags
            var flipH = TileConstants.IsFlippedHorizontally(rawGidWithFlags);
            var flipV = TileConstants.IsFlippedVertically(rawGidWithFlags);
            var spriteEffects = SpriteEffects.None;
            if (flipH)
                spriteEffects |= SpriteEffects.FlipHorizontally;
            if (flipV)
                spriteEffects |= SpriteEffects.FlipVertically;

            // Resolve tileset resources for this GID
            var (tilesetTexture, tilesetDefinition, resolvedTilesetId, resolvedFirstGid) =
                ResolveTilesetResources(
                    gid,
                    tilesetRefs,
                    data.TilesetId,
                    defaultTilesetTexture,
                    defaultTilesetDefinition
                );

            if (tilesetTexture == null || tilesetDefinition == null)
                continue;

            // Calculate source rectangle for this tile
            Rectangle sourceRect;
            try
            {
                sourceRect = _resourceManager.CalculateTilesetSourceRectangle(
                    resolvedTilesetId,
                    gid,
                    resolvedFirstGid
                );
            }
            catch (Exception ex)
            {
                _logger.Debug(
                    ex,
                    "Failed to calculate source rectangle for GID {Gid} in tileset {TilesetId}",
                    gid,
                    resolvedTilesetId
                );
                continue;
            }

            // Calculate world position for this tile
            var tilePosition = new Vector2(
                pos.Position.X + x * tilesetDefinition.TileWidth,
                pos.Position.Y + y * tilesetDefinition.TileHeight
            );

            // Draw the tile with flip effects
            spriteBatch.Draw(
                tilesetTexture,
                tilePosition,
                sourceRect,
                color,
                0f,
                Vector2.Zero,
                1f,
                spriteEffects,
                0f
            );

            tilesRendered++;
        }

        return tilesRendered;
    }

    private int RenderAnimatedTiles(
        World world,
        Entity chunkEntity,
        TileChunkComponent chunk,
        TileDataComponent data,
        PositionComponent pos,
        List<TilesetReference> tilesetRefs,
        Texture2D defaultTilesetTexture,
        TilesetDefinition defaultTilesetDefinition,
        Color color,
        SpriteBatch spriteBatch
    )
    {
        if (!world.IsAlive(chunkEntity))
            return 0;

        if (!world.Has<AnimatedTileDataComponent>(chunkEntity))
        {
            throw new InvalidOperationException(
                $"Chunk entity {chunkEntity.Id} has TileDataComponent.HasAnimatedTiles=true but is missing AnimatedTileDataComponent."
            );
        }

        ref var animData = ref world.Get<AnimatedTileDataComponent>(chunkEntity);
        var tilesRendered = 0;

        for (var y = 0; y < chunk.ChunkHeight; y++)
        for (var x = 0; x < chunk.ChunkWidth; x++)
        {
            var tileIndex = y * chunk.ChunkWidth + x;
            if (tileIndex >= data.TileIndices.Length)
                continue;

            var rawGidWithFlags = data.TileIndices[tileIndex];
            var gid = TileConstants.GetRawGid(rawGidWithFlags);

            if (gid <= 0)
                continue;

            // Extract flip flags
            var flipH = TileConstants.IsFlippedHorizontally(rawGidWithFlags);
            var flipV = TileConstants.IsFlippedVertically(rawGidWithFlags);
            var spriteEffects = SpriteEffects.None;
            if (flipH)
                spriteEffects |= SpriteEffects.FlipHorizontally;
            if (flipV)
                spriteEffects |= SpriteEffects.FlipVertically;

            // Resolve tileset for this GID
            string resolvedTilesetId;
            int resolvedFirstGid;
            try
            {
                (resolvedTilesetId, resolvedFirstGid) = TilesetResolver.ResolveTilesetForGid(
                    gid,
                    tilesetRefs
                );
            }
            catch (InvalidOperationException)
            {
                continue;
            }

            // Determine render GID (use animated frame if this tile is animated)
            var renderGid = gid;
            if (
                animData.AnimatedTiles != null
                && animData.AnimatedTiles.TryGetValue(tileIndex, out var animState)
            )
            {
                var frames = _resourceManager.GetCachedTileAnimation(
                    animState.AnimationTilesetId,
                    animState.AnimationLocalTileId
                );

                if (
                    frames != null
                    && frames.Count > 0
                    && animState.CurrentFrameIndex < frames.Count
                )
                {
                    var currentFrame = frames[animState.CurrentFrameIndex];
                    renderGid = currentFrame.TileId + resolvedFirstGid;
                }
            }

            // Resolve tileset resources for render GID
            var (animTilesetTexture, animTilesetDefinition, _, _) = ResolveTilesetResources(
                renderGid,
                tilesetRefs,
                data.TilesetId,
                defaultTilesetTexture,
                defaultTilesetDefinition
            );

            if (animTilesetTexture == null || animTilesetDefinition == null)
                continue;

            // Calculate source rectangle for this tile
            Rectangle sourceRect;
            try
            {
                sourceRect = _resourceManager.CalculateTilesetSourceRectangle(
                    resolvedTilesetId,
                    renderGid,
                    resolvedFirstGid
                );
            }
            catch (Exception ex)
            {
                _logger.Debug(
                    ex,
                    "Failed to calculate source rectangle for animated GID {Gid} in tileset {TilesetId}",
                    renderGid,
                    resolvedTilesetId
                );
                continue;
            }

            // Calculate world position for this tile
            var tilePosition = new Vector2(
                pos.Position.X + x * animTilesetDefinition.TileWidth,
                pos.Position.Y + y * animTilesetDefinition.TileHeight
            );

            // Draw the tile with flip effects
            spriteBatch.Draw(
                animTilesetTexture,
                tilePosition,
                sourceRect,
                color,
                0f,
                Vector2.Zero,
                1f,
                spriteEffects,
                0f
            );

            tilesRendered++;
        }

        return tilesRendered;
    }

    private (
        Texture2D? Texture,
        TilesetDefinition? Definition,
        string TilesetId,
        int FirstGid
    ) ResolveTilesetResources(
        int gid,
        IReadOnlyList<TilesetReference> tilesetRefs,
        string defaultTilesetId,
        Texture2D defaultTexture,
        TilesetDefinition defaultDefinition
    )
    {
        string resolvedTilesetId;
        int resolvedFirstGid;
        try
        {
            (resolvedTilesetId, resolvedFirstGid) = TilesetResolver.ResolveTilesetForGid(
                gid,
                tilesetRefs
            );
        }
        catch (InvalidOperationException)
        {
            return (null, null, string.Empty, 0);
        }

        if (resolvedTilesetId == defaultTilesetId)
            return (defaultTexture, defaultDefinition, resolvedTilesetId, resolvedFirstGid);

        try
        {
            var texture = _resourceManager.LoadTexture(resolvedTilesetId);
            var definition = _resourceManager.GetTilesetDefinition(resolvedTilesetId);
            return (texture, definition, resolvedTilesetId, resolvedFirstGid);
        }
        catch (Exception ex)
        {
            _logger.Debug(
                ex,
                "Failed to resolve tileset resources for tileset {TilesetId}",
                resolvedTilesetId
            );
            return (null, null, string.Empty, 0);
        }
    }

    private List<TilesetReference> GetTilesetRefsForMap(string mapId)
    {
        if (_mapTilesetRefsCache.TryGetValue(mapId, out var cached))
            return cached;

        var mapDefinition = _definitionRegistry.GetById<MapDefinition>(mapId);
        if (mapDefinition?.Tilesets == null || mapDefinition.Tilesets.Count == 0)
        {
            _mapTilesetRefsCache[mapId] = new List<TilesetReference>();
            return _mapTilesetRefsCache[mapId];
        }

        var sortedRefs = mapDefinition.Tilesets.OrderByDescending(t => t.FirstGid).ToList();
        _mapTilesetRefsCache[mapId] = sortedRefs;
        return sortedRefs;
    }
}
