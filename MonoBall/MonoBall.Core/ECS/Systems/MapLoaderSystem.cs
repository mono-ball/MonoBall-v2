using System;
using System.Collections.Generic;
using System.Linq;
using Arch.Core;
using Arch.System;
using Microsoft.Xna.Framework;
using MonoBall.Core.Constants;
using MonoBall.Core.ECS.Components;
using MonoBall.Core.ECS.Components.Audio;
using MonoBall.Core.ECS.Events;
using MonoBall.Core.ECS.Services;
using MonoBall.Core.ECS.Utilities;
using MonoBall.Core.Maps;
using MonoBall.Core.Maps.Utilities;
using MonoBall.Core.Mods;
using MonoBall.Core.Mods.Definitions;
using MonoBall.Core.Resources;
using MonoBall.Core.Scripting.Services;
using MonoBall.Core.Scripting.Utilities;
using Serilog;

namespace MonoBall.Core.ECS.Systems;

/// <summary>
///     System responsible for loading and unloading maps, creating tile chunks, and managing map connections.
/// </summary>
public class MapLoaderSystem : BaseSystem<World, float>, IPrioritizedSystem
{
    private readonly int _chunkSize; // 16x16 tiles per chunk (cached from constants service)
    private readonly IConstantsService _constants;
    private readonly IFlagVariableService? _flagVariableService;
    private readonly HashSet<string> _loadedMaps = new();
    private readonly ILogger _logger;

    private readonly Dictionary<string, List<Entity>> _mapChunkEntities = new();

    private readonly Dictionary<string, List<Entity>> _mapConnectionEntities = new();

    private readonly Dictionary<string, Entity> _mapEntities = new();

    private readonly Dictionary<string, List<Entity>> _mapNpcEntities = new();

    private readonly Dictionary<string, Vector2> _mapPositions = new(); // Map positions in tile coordinates

    private readonly Dictionary<string, List<TilesetReference>> _mapTilesetRefs = new(); // Map tileset references for GID resolution

    private readonly DefinitionRegistry _registry;
    private readonly IResourceManager _resourceManager;
    private readonly IVariableSpriteResolver? _variableSpriteResolver;

    /// <summary>
    ///     Initializes a new instance of the MapLoaderSystem.
    /// </summary>
    /// <param name="world">The ECS world.</param>
    /// <param name="registry">The definition registry.</param>
    /// <param name="resourceManager">The resource manager for loading textures and validating sprites.</param>
    /// <param name="flagVariableService">Optional flag/variable service for checking NPC visibility flags.</param>
    /// <param name="variableSpriteResolver">Optional variable sprite resolver for resolving variable sprite IDs.</param>
    /// <param name="logger">The logger for logging operations.</param>
    /// <param name="constants">The constants service for accessing game constants. Required.</param>
    public MapLoaderSystem(
        World world,
        DefinitionRegistry registry,
        IResourceManager resourceManager,
        IFlagVariableService? flagVariableService = null,
        IVariableSpriteResolver? variableSpriteResolver = null,
        ILogger logger = null!,
        IConstantsService? constants = null
    )
        : base(world)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _resourceManager =
            resourceManager ?? throw new ArgumentNullException(nameof(resourceManager));
        _flagVariableService = flagVariableService;
        _variableSpriteResolver = variableSpriteResolver;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _constants = constants ?? throw new ArgumentNullException(nameof(constants));

        // Cache chunk size from constants service (performance optimization)
        _chunkSize = _constants.Get<int>("TileChunkSize");
    }

    /// <summary>
    ///     Gets the execution priority for this system.
    /// </summary>
    public int Priority => SystemPriority.MapLoader;

    /// <summary>
    ///     Loads a map by its ID. Creates map entity, tile chunks, and connection entities.
    /// </summary>
    /// <param name="mapId">The map definition ID to load.</param>
    /// <param name="tilePosition">The tile position where this map should be placed. Defaults to (0,0) for initial map.</param>
    public void LoadMap(string mapId, Vector2? tilePosition = null)
    {
        if (string.IsNullOrEmpty(mapId))
        {
            _logger.Warning("Attempted to load map with null or empty ID");
            return;
        }

        if (_loadedMaps.Contains(mapId))
        {
            _logger.Debug("Map {MapId} is already loaded", mapId);
            return;
        }

        var mapDefinition = _registry.GetById<MapDefinition>(mapId);
        if (mapDefinition == null)
        {
            _logger.Warning("Map definition not found: {MapId}", mapId);
            return;
        }

        _logger.Information("Loading map: {MapId} ({Name})", mapId, mapDefinition.Name);

        // Determine map position (default to 0,0 for initial map, or use provided position)
        var mapTilePosition = tilePosition ?? Vector2.Zero;
        _mapPositions[mapId] = mapTilePosition;

        _logger.Debug(
            "Positioning map {MapId} at tile position ({TileX}, {TileY})",
            mapId,
            mapTilePosition.X,
            mapTilePosition.Y
        );

        // Create map entity with position component
        var mapEntity = World.Create(
            new MapComponent
            {
                MapId = mapId,
                Width = mapDefinition.Width,
                Height = mapDefinition.Height,
                TileWidth = mapDefinition.TileWidth,
                TileHeight = mapDefinition.TileHeight,
            },
            new PositionComponent
            {
                Position = new Vector2(
                    mapTilePosition.X * mapDefinition.TileWidth,
                    mapTilePosition.Y * mapDefinition.TileHeight
                ),
            }
        );

        _mapEntities[mapId] = mapEntity;
        _loadedMaps.Add(mapId);
        _mapChunkEntities[mapId] = new List<Entity>();
        _mapConnectionEntities[mapId] = new List<Entity>();
        _mapNpcEntities[mapId] = new List<Entity>();

        // Store tileset references for GID resolution (sorted by firstGid descending)
        if (mapDefinition.Tilesets != null && mapDefinition.Tilesets.Count > 0)
        {
            // Validate tileset ranges don't overlap incorrectly
            ValidateTilesetRanges(mapId, mapDefinition.Tilesets);

            _mapTilesetRefs[mapId] = mapDefinition
                .Tilesets.OrderByDescending(t => t.FirstGid)
                .ToList();
        }
        else
        {
            _mapTilesetRefs[mapId] = new List<TilesetReference>();
        }

        // Preload tilesets referenced by this map
        PreloadTilesets(mapDefinition);

        // Create border component if border data exists
        if (mapDefinition.Border != null)
        {
            var borderComponent = CreateMapBorderComponent(mapDefinition);
            if (borderComponent.HasBorder)
            {
                World.Add(mapEntity, borderComponent);
                _logger.Debug("Added MapBorderComponent to map {MapId}", mapId);
            }
        }

        // Create music component if music ID exists
        if (!string.IsNullOrEmpty(mapDefinition.MusicId))
        {
            var musicComponent = new MusicComponent
            {
                AudioId = mapDefinition.MusicId,
                FadeInOnTransition = true,
                FadeDuration = 0f, // Use default fade duration from audio definition
            };
            World.Add(mapEntity, musicComponent);
            _logger.Debug(
                "Added MusicComponent to map {MapId} with music {MusicId}",
                mapId,
                mapDefinition.MusicId
            );
        }

        // Create map section component if map section ID exists
        if (!string.IsNullOrEmpty(mapDefinition.MapSectionId))
        {
            // Resolve MapSectionDefinition to get popup theme
            var mapSectionDefinition = _registry.GetById<MapSectionDefinition>(
                mapDefinition.MapSectionId
            );
            if (
                mapSectionDefinition != null
                && !string.IsNullOrEmpty(mapSectionDefinition.PopupTheme)
            )
            {
                var mapSectionComponent = new MapSectionComponent
                {
                    MapSectionId = mapDefinition.MapSectionId,
                    PopupThemeId = mapSectionDefinition.PopupTheme,
                };
                World.Add(mapEntity, mapSectionComponent);
                _logger.Debug(
                    "Added MapSectionComponent to map {MapId} with section {MapSectionId} and theme {PopupThemeId}",
                    mapId,
                    mapDefinition.MapSectionId,
                    mapSectionDefinition.PopupTheme
                );
            }
            else
            {
                _logger.Warning(
                    "Map {MapId} has MapSectionId {MapSectionId} but MapSectionDefinition not found or has no PopupTheme",
                    mapId,
                    mapDefinition.MapSectionId
                );
            }
        }

        // Create tile chunks for each layer (positioned relative to map position)
        var chunksCreated = CreateTileChunks(mapEntity, mapDefinition, mapTilePosition);
        _logger.Information(
            "Created {ChunkCount} tile chunks for map {MapId}",
            chunksCreated,
            mapId
        );

        // Create connection entities
        CreateConnections(mapEntity, mapDefinition);

        // Create NPC entities
        var npcsCreated = CreateNpcs(mapEntity, mapDefinition, mapTilePosition);
        _logger.Information("Created {NpcCount} NPCs for map {MapId}", npcsCreated, mapId);

        // Fire MapLoadedEvent
        var loadedEvent = new MapLoadedEvent { MapId = mapId, MapEntity = mapEntity };
        EventBus.Send(ref loadedEvent);

        // Note: MapTransitionEvent should be fired by a system that detects when the player
        // actually crosses map boundaries, not when maps are loaded. MapLoaderSystem only
        // loads maps - it doesn't know when the player transitions.

        // Proactively load connected maps with proper positioning
        if (mapDefinition.Connections != null)
            foreach (var kvp in mapDefinition.Connections)
            {
                var directionStr = kvp.Key.ToLowerInvariant();
                var connection = kvp.Value;

                // Get target map definition to calculate correct position
                var targetMapDefinition = _registry.GetById<MapDefinition>(connection.MapId);
                if (targetMapDefinition == null)
                {
                    _logger.Warning(
                        "Target map definition not found for connection: {MapId}",
                        connection.MapId
                    );
                    continue;
                }

                // Calculate connected map position based on direction and offset
                var connectedMapPosition = CalculateConnectedMapPosition(
                    mapTilePosition,
                    mapDefinition.Width,
                    mapDefinition.Height,
                    targetMapDefinition.Width,
                    targetMapDefinition.Height,
                    directionStr,
                    connection.Offset
                );

                if (!_loadedMaps.Contains(connection.MapId))
                {
                    _logger.Debug(
                        "Loading connected map {ConnectedMapId} at tile position ({TileX}, {TileY}) via {Direction} connection",
                        connection.MapId,
                        connectedMapPosition.X,
                        connectedMapPosition.Y,
                        directionStr
                    );

                    LoadMap(connection.MapId, connectedMapPosition);
                }
                else
                {
                    // Map is already loaded - verify position matches expected position
                    if (_mapPositions.TryGetValue(connection.MapId, out var existingPosition))
                    {
                        // Allow small floating point differences
                        var tolerance = 0.01f;
                        if (
                            Math.Abs(existingPosition.X - connectedMapPosition.X) > tolerance
                            || Math.Abs(existingPosition.Y - connectedMapPosition.Y) > tolerance
                        )
                            _logger.Warning(
                                "Position conflict for map {MapId}. Existing: ({ExistingX}, {ExistingY}), Expected: ({ExpectedX}, {ExpectedY}) via {Direction} connection from {SourceMapId}",
                                connection.MapId,
                                existingPosition.X,
                                existingPosition.Y,
                                connectedMapPosition.X,
                                connectedMapPosition.Y,
                                directionStr,
                                mapId
                            );
                    }
                }
            }

        _logger.Information("Map loaded: {MapId}", mapId);
    }

    /// <summary>
    ///     Unloads a map by its ID. Removes all associated entities.
    /// </summary>
    /// <param name="mapId">The map definition ID to unload.</param>
    public void UnloadMap(string mapId)
    {
        if (string.IsNullOrEmpty(mapId))
            return;

        if (!_loadedMaps.Contains(mapId))
            return;

        _logger.Information("Unloading map: {MapId}", mapId);

        if (_mapEntities.TryGetValue(mapId, out var mapEntity))
        {
            // Destroy all chunk entities
            if (_mapChunkEntities.TryGetValue(mapId, out var chunkEntities))
            {
                foreach (var chunkEntity in chunkEntities)
                {
                    // Fire EntityDestroyedEvent before destroying
                    var destroyedEvent = new EntityDestroyedEvent
                    {
                        Entity = chunkEntity,
                        DestroyedAt = DateTime.UtcNow,
                    };
                    EventBus.Send(ref destroyedEvent);
                    World.Destroy(chunkEntity);
                }

                _mapChunkEntities.Remove(mapId);
            }

            // Destroy all connection entities
            if (_mapConnectionEntities.TryGetValue(mapId, out var connectionEntities))
            {
                foreach (var connectionEntity in connectionEntities)
                {
                    // Fire EntityDestroyedEvent before destroying
                    var destroyedEvent = new EntityDestroyedEvent
                    {
                        Entity = connectionEntity,
                        DestroyedAt = DateTime.UtcNow,
                    };
                    EventBus.Send(ref destroyedEvent);
                    World.Destroy(connectionEntity);
                }

                _mapConnectionEntities.Remove(mapId);
            }

            // Destroy all NPC entities
            if (_mapNpcEntities.TryGetValue(mapId, out var npcEntities))
            {
                foreach (var npcEntity in npcEntities)
                {
                    // Fire NpcUnloadedEvent before destroying
                    if (World.Has<NpcComponent>(npcEntity))
                    {
                        ref var npcComp = ref World.Get<NpcComponent>(npcEntity);
                        var npcUnloadedEvent = new NpcUnloadedEvent
                        {
                            NpcId = npcComp.NpcId,
                            MapId = mapId,
                        };
                        EventBus.Send(ref npcUnloadedEvent);
                    }

                    // Fire EntityDestroyedEvent before destroying
                    var destroyedEvent = new EntityDestroyedEvent
                    {
                        Entity = npcEntity,
                        DestroyedAt = DateTime.UtcNow,
                    };
                    EventBus.Send(ref destroyedEvent);
                    World.Destroy(npcEntity);
                }

                _mapNpcEntities.Remove(mapId);
            }

            // Destroy the map entity
            var mapDestroyedEvent = new EntityDestroyedEvent
            {
                Entity = mapEntity,
                DestroyedAt = DateTime.UtcNow,
            };
            EventBus.Send(ref mapDestroyedEvent);
            World.Destroy(mapEntity);
            _mapEntities.Remove(mapId);
        }

        _loadedMaps.Remove(mapId);
        _mapPositions.Remove(mapId); // Clean up position tracking

        // Fire MapUnloadedEvent
        var unloadedEvent = new MapUnloadedEvent { MapId = mapId };
        EventBus.Send(ref unloadedEvent);
        _logger.Information("Map unloaded: {MapId}", mapId);
    }

    private void PreloadTilesets(MapDefinition mapDefinition)
    {
        if (mapDefinition.Tilesets == null || mapDefinition.Tilesets.Count == 0)
        {
            _logger.Warning("Map {MapId} has no tileset references", mapDefinition.Id);
            return;
        }

        _logger.Information(
            "Preloading {Count} tileset(s) for map {MapId}",
            mapDefinition.Tilesets.Count,
            mapDefinition.Id
        );
        foreach (var tilesetRef in mapDefinition.Tilesets)
        {
            _logger.Debug(
                "Loading tileset {TilesetId} (firstGid: {FirstGid})",
                tilesetRef.TilesetId,
                tilesetRef.FirstGid
            );
            try
            {
                var texture = _resourceManager.LoadTexture(tilesetRef.TilesetId);
                _logger.Information(
                    "Successfully loaded tileset {TilesetId}",
                    tilesetRef.TilesetId
                );
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Failed to load tileset {TilesetId}", tilesetRef.TilesetId);
            }
        }
    }

    /// <summary>
    ///     Calculates the tile position of a connected map based on direction and offset.
    /// </summary>
    /// <param name="sourceMapPosition">The source map's tile position.</param>
    /// <param name="sourceMapWidth">The source map's width in tiles.</param>
    /// <param name="sourceMapHeight">The source map's height in tiles.</param>
    /// <param name="targetMapWidth">The target map's width in tiles.</param>
    /// <param name="targetMapHeight">The target map's height in tiles.</param>
    /// <param name="direction">The connection direction (north, south, east, west).</param>
    /// <param name="offset">The offset in tiles.</param>
    /// <returns>The calculated tile position for the connected map.</returns>
    private Vector2 CalculateConnectedMapPosition(
        Vector2 sourceMapPosition,
        int sourceMapWidth,
        int sourceMapHeight,
        int targetMapWidth,
        int targetMapHeight,
        string direction,
        int offset
    )
    {
        return direction.ToLowerInvariant() switch
        {
            // North: Target map's south edge aligns with source map's north edge
            // Target map position = source position - target map height
            "north" => new Vector2(
                sourceMapPosition.X + offset,
                sourceMapPosition.Y - targetMapHeight
            ),
            // South: Target map's north edge aligns with source map's south edge
            // Target map position = source position + source map height
            "south" => new Vector2(
                sourceMapPosition.X + offset,
                sourceMapPosition.Y + sourceMapHeight
            ),
            // East: Target map's west edge aligns with source map's east edge
            // Target map position = source position + source map width
            "east" => new Vector2(
                sourceMapPosition.X + sourceMapWidth,
                sourceMapPosition.Y + offset
            ),
            // West: Target map's east edge aligns with source map's west edge
            // Target map position = source position - target map width
            "west" => new Vector2(
                sourceMapPosition.X - targetMapWidth,
                sourceMapPosition.Y + offset
            ),
            _ => sourceMapPosition, // Default to same position if direction is invalid
        };
    }

    private int CreateTileChunks(
        Entity mapEntity,
        MapDefinition mapDefinition,
        Vector2 mapTilePosition
    )
    {
        if (mapDefinition.Layers == null || mapDefinition.Layers.Count == 0)
        {
            _logger.Warning("Map {MapId} has no layers", mapDefinition.Id);
            return 0;
        }

        _logger.Debug(
            "Creating chunks for {LayerCount} layers in map {MapId}",
            mapDefinition.Layers.Count,
            mapDefinition.Id
        );

        var totalChunks = 0;
        var layerIndex = 0;
        foreach (var layer in mapDefinition.Layers)
        {
            if (!layer.Visible)
            {
                _logger.Debug(
                    "Skipping invisible layer {LayerId} (index: {Index})",
                    layer.LayerId,
                    layerIndex
                );
                layerIndex++;
                continue;
            }

            if (string.IsNullOrEmpty(layer.TileData))
            {
                _logger.Warning(
                    "Layer {LayerId} (index: {Index}) has no tile data",
                    layer.LayerId,
                    layerIndex
                );
                layerIndex++;
                continue;
            }

            _logger.Debug(
                "Processing layer {LayerId} (index: {Index}, size: {Width}x{Height})",
                layer.LayerId,
                layerIndex,
                layer.Width,
                layer.Height
            );

            // Decode tile data
            var tileIndices = TileDataDecoder.Decode(layer.TileData, layer.Width, layer.Height);
            if (tileIndices == null)
            {
                _logger.Warning("Failed to decode tile data for layer {LayerId}", layer.LayerId);
                layerIndex++;
                continue;
            }

            _logger.Debug(
                "Decoded {TileCount} tiles for layer {LayerId}",
                tileIndices.Length,
                layer.LayerId
            );

            // Get tileset references for this map (sorted by firstGid descending)
            var tilesetRefs = _mapTilesetRefs.TryGetValue(mapDefinition.Id, out var refs)
                ? refs
                : new List<TilesetReference>();

            // Determine default tileset (first one, for backward compatibility with TileDataComponent)
            var defaultTilesetId = string.Empty;
            var defaultFirstGid = 1;
            if (tilesetRefs.Count > 0)
            {
                defaultTilesetId = tilesetRefs[tilesetRefs.Count - 1].TilesetId; // Last = lowest firstGid
                defaultFirstGid = tilesetRefs[tilesetRefs.Count - 1].FirstGid;
            }

            // Create chunks
            var chunksX = (int)Math.Ceiling((double)layer.Width / _chunkSize);
            var chunksY = (int)Math.Ceiling((double)layer.Height / _chunkSize);

            _logger.Debug(
                "Creating {ChunksX}x{ChunksY} chunks for layer {LayerId}",
                chunksX,
                chunksY,
                layer.LayerId
            );

            var layerChunks = 0;
            for (var chunkY = 0; chunkY < chunksY; chunkY++)
            for (var chunkX = 0; chunkX < chunksX; chunkX++)
            {
                var chunkStartX = chunkX * _chunkSize;
                var chunkStartY = chunkY * _chunkSize;
                var chunkWidth = Math.Min(_chunkSize, layer.Width - chunkStartX);
                var chunkHeight = Math.Min(_chunkSize, layer.Height - chunkStartY);

                // Extract tile indices for this chunk
                var chunkTileIndices = new int[chunkWidth * chunkHeight];
                for (var y = 0; y < chunkHeight; y++)
                for (var x = 0; x < chunkWidth; x++)
                {
                    var tileX = chunkStartX + x;
                    var tileY = chunkStartY + y;
                    var tileIndex = tileY * layer.Width + tileX;
                    chunkTileIndices[y * chunkWidth + x] = tileIndices[tileIndex];
                }

                // Detect animated tiles in this chunk
                var animatedTiles = new Dictionary<int, TileAnimationState>();
                var hasAnimatedTiles = false;

                // Check for animated tiles using ResourceManager
                {
                    for (var i = 0; i < chunkTileIndices.Length; i++)
                    {
                        var rawGidWithFlags = chunkTileIndices[i];

                        // Extract raw GID (strips flip flags from high bits)
                        // CRITICAL: Must mask flip flags before resolving tileset, as TilesetResolver
                        // compares GIDs and a GID with 0x80000000 set will be negative when treated as signed int
                        var gid = TileConstants.GetRawGid(rawGidWithFlags);
                        if (gid > 0)
                        {
                            // Resolve tileset for this GID (using raw GID without flip flags)
                            // Fail fast - no fallback code
                            try
                            {
                                var (resolvedTilesetId, resolvedFirstGid) =
                                    TilesetResolver.ResolveTilesetForGid(gid, tilesetRefs);

                                // Calculate local tile ID using raw GID (without flip flags)
                                var localTileId = gid - resolvedFirstGid;

                                if (localTileId >= 0)
                                {
                                    // Load tileset definition to check for animations (fail fast if not found)
                                    var tilesetDefinition = _resourceManager.GetTilesetDefinition(
                                        resolvedTilesetId
                                    );

                                    // Check tileset definition's Tiles list directly for this tile's animation
                                    // This avoids exceptions for non-animated tiles (no fallback code)
                                    if (tilesetDefinition.Tiles != null)
                                    {
                                        var tile = tilesetDefinition.Tiles.FirstOrDefault(t =>
                                            t.LocalTileId == localTileId
                                        );
                                        if (tile?.Animation != null && tile.Animation.Count > 0)
                                        {
                                            // Populate animation cache by calling GetTileAnimation
                                            // This is safe because we've already verified the animation exists
                                            // This ensures AnimatedTileSystem can find the cached animation
                                            _resourceManager.GetTileAnimation(
                                                resolvedTilesetId,
                                                localTileId
                                            );

                                            var animState = new TileAnimationState
                                            {
                                                AnimationTilesetId = resolvedTilesetId,
                                                AnimationLocalTileId = localTileId,
                                                CurrentFrameIndex = 0,
                                                ElapsedTime = 0.0f,
                                            };
                                            animatedTiles[i] = animState;
                                            hasAnimatedTiles = true;
                                        }
                                    }
                                }
                            }
                            catch (InvalidOperationException)
                            {
                                // GID cannot be resolved - this indicates a bug in map data or tileset configuration
                                // Log and continue (tile will render as empty/invalid)
                                _logger.Warning(
                                    "Failed to resolve tileset for GID {Gid} in map {MapId}. Tile will be skipped.",
                                    gid,
                                    mapDefinition.Id
                                );
                            }
                        }
                    }
                }

                // Calculate world position for chunk (relative to map's tile position)
                // Convert chunk tile coordinates to pixel coordinates, offset by map position
                var chunkPosition = new Vector2(
                    (mapTilePosition.X + chunkStartX) * mapDefinition.TileWidth,
                    (mapTilePosition.Y + chunkStartY) * mapDefinition.TileHeight
                );

                // Create chunk entity with components
                Entity chunkEntity;
                if (hasAnimatedTiles)
                    // Create with AnimatedTileDataComponent
                    chunkEntity = World.Create(
                        new MapComponent { MapId = mapDefinition.Id },
                        new TileChunkComponent
                        {
                            ChunkX = chunkX,
                            ChunkY = chunkY,
                            ChunkWidth = chunkWidth,
                            ChunkHeight = chunkHeight,
                            LayerId = layer.LayerId,
                            LayerIndex = layerIndex,
                        },
                        new TileDataComponent
                        {
                            TilesetId = defaultTilesetId, // Default tileset (resolved per-tile in renderer)
                            TileIndices = chunkTileIndices,
                            FirstGid = defaultFirstGid, // Default firstGid (resolved per-tile in renderer)
                            HasAnimatedTiles = hasAnimatedTiles,
                        },
                        new PositionComponent { Position = chunkPosition },
                        new RenderableComponent
                        {
                            IsVisible = true,
                            RenderOrder = layerIndex,
                            Opacity = layer.Opacity,
                        },
                        new AnimatedTileDataComponent { AnimatedTiles = animatedTiles }
                    );
                else
                    // Create without AnimatedTileDataComponent
                    chunkEntity = World.Create(
                        new MapComponent { MapId = mapDefinition.Id },
                        new TileChunkComponent
                        {
                            ChunkX = chunkX,
                            ChunkY = chunkY,
                            ChunkWidth = chunkWidth,
                            ChunkHeight = chunkHeight,
                            LayerId = layer.LayerId,
                            LayerIndex = layerIndex,
                        },
                        new TileDataComponent
                        {
                            TilesetId = defaultTilesetId, // Default tileset (resolved per-tile in renderer)
                            TileIndices = chunkTileIndices,
                            FirstGid = defaultFirstGid, // Default firstGid (resolved per-tile in renderer)
                            HasAnimatedTiles = hasAnimatedTiles,
                        },
                        new PositionComponent { Position = chunkPosition },
                        new RenderableComponent
                        {
                            IsVisible = true,
                            RenderOrder = layerIndex,
                            Opacity = layer.Opacity,
                        }
                    );

                // Track chunk entity for unloading
                _mapChunkEntities[mapDefinition.Id].Add(chunkEntity);
                layerChunks++;
            }

            _logger.Debug(
                "Created {ChunkCount} chunks for layer {LayerId}",
                layerChunks,
                layer.LayerId
            );
            totalChunks += layerChunks;
            layerIndex++;
        }

        return totalChunks;
    }

    private void CreateConnections(Entity mapEntity, MapDefinition mapDefinition)
    {
        if (mapDefinition.Connections == null)
            return;

        foreach (var kvp in mapDefinition.Connections)
        {
            var directionStr = kvp.Key.ToLowerInvariant();
            var connection = kvp.Value;

            var direction = directionStr switch
            {
                "north" => MapConnectionDirection.North,
                "south" => MapConnectionDirection.South,
                "east" => MapConnectionDirection.East,
                "west" => MapConnectionDirection.West,
                _ => MapConnectionDirection.North,
            };

            var connectionEntity = World.Create(
                new MapComponent { MapId = mapDefinition.Id },
                new MapConnectionComponent
                {
                    Direction = direction,
                    TargetMapId = connection.MapId,
                    Offset = connection.Offset,
                }
            );

            // Track connection entity for unloading
            _mapConnectionEntities[mapDefinition.Id].Add(connectionEntity);
        }
    }

    /// <summary>
    ///     Creates NPC entities for a map.
    /// </summary>
    /// <param name="mapEntity">The map entity.</param>
    /// <param name="mapDefinition">The map definition.</param>
    /// <param name="mapTilePosition">The map position in tile coordinates.</param>
    /// <returns>The number of NPCs created.</returns>
    private int CreateNpcs(Entity mapEntity, MapDefinition mapDefinition, Vector2 mapTilePosition)
    {
        if (mapDefinition.Npcs == null || mapDefinition.Npcs.Count == 0)
            return 0;

        // ResourceManager is always available (required parameter)

        var npcsCreated = 0;

        foreach (var npcDef in mapDefinition.Npcs)
            try
            {
                var npcEntity = CreateNpcEntity(npcDef, mapDefinition, mapTilePosition);

                // Track NPC entity for unloading
                _mapNpcEntities[mapDefinition.Id].Add(npcEntity);
                npcsCreated++;
            }
            catch (Exception ex)
            {
                _logger.Error(
                    ex,
                    "Failed to create NPC {NpcId} for map {MapId}",
                    npcDef.NpcId,
                    mapDefinition.Id
                );
                // Continue with next NPC
            }

        return npcsCreated;
    }

    /// <summary>
    ///     Creates an NPC entity with all required components.
    ///     Validates inputs and throws exceptions if invalid.
    /// </summary>
    /// <param name="npcDef">The NPC definition.</param>
    /// <param name="mapDefinition">The map definition.</param>
    /// <param name="mapTilePosition">The map position in tile coordinates.</param>
    /// <returns>The created NPC entity.</returns>
    /// <exception cref="ArgumentNullException">Thrown if npcDef or mapDefinition is null.</exception>
    /// <exception cref="ArgumentException">Thrown if sprite definition or animation is invalid.</exception>
    /// <exception cref="InvalidOperationException">Thrown if sprite loader is not available.</exception>
    private Entity CreateNpcEntity(
        NpcDefinition npcDef,
        MapDefinition mapDefinition,
        Vector2 mapTilePosition
    )
    {
        if (npcDef == null)
            throw new ArgumentNullException(nameof(npcDef));

        if (mapDefinition == null)
            throw new ArgumentNullException(nameof(mapDefinition));

        // ResourceManager is always available (required parameter)

        // CRITICAL: Resolve variable sprite FIRST, before validation
        // Variable sprite IDs like {base:sprite:npcs/generic/var_rival} are not valid sprite definitions
        var actualSpriteId = ResolveNpcSpriteId(npcDef);

        // CRITICAL: Always validate the RESOLVED sprite ID, never the variable sprite ID
        // Variable sprite IDs like {base:sprite:npcs/generic/var_rival} are not valid sprite definitions
        SpriteValidationHelper.ValidateSpriteDefinition(
            _resourceManager,
            _logger,
            actualSpriteId, // Always a real sprite ID, never a variable sprite ID
            "NPC",
            npcDef.NpcId
        );

        // Get sprite definition to determine initial flip state
        // CRITICAL: Use actualSpriteId (resolved), not npcDef.SpriteId (may be variable sprite)
        // Behavior scripts will handle animation selection
        var spriteDefinition = _resourceManager.GetSpriteDefinition(actualSpriteId);
        var flipHorizontal = false; // Default - behavior scripts will set animation and flip state

        // NPC coordinates in JSON are already in pixel coordinates (not tile coordinates)
        // Add map pixel position offset to get world pixel position
        var mapPixelPosition = new Vector2(
            mapTilePosition.X * mapDefinition.TileWidth,
            mapTilePosition.Y * mapDefinition.TileHeight
        );
        var npcPixelPosition = new Vector2(
            mapPixelPosition.X + npcDef.X,
            mapPixelPosition.Y + npcDef.Y
        );

        // Determine initial visibility based on flag
        var isVisible = true;
        if (!string.IsNullOrWhiteSpace(npcDef.VisibilityFlag) && _flagVariableService != null)
            isVisible = _flagVariableService.GetFlag(npcDef.VisibilityFlag);

        // Create NPC entity with all required components
        // All NPCs get GridMovement component (even stationary ones) to store facing direction
        // Default movement speed: 3.75 tiles/second (matches oldmonoball NpcSpawnBuilder default)
        // NPCs created in loaded maps get ActiveMapEntity tag immediately for query-level filtering
        const float defaultNpcMovementSpeed = 3.75f;

        // Create NpcComponent with explicit non-null strings to avoid Arch.Core issues
        var npcComponent = new NpcComponent
        {
            NpcId =
                npcDef.NpcId
                ?? throw new InvalidOperationException(
                    $"NPC definition {npcDef.NpcId} has null NpcId"
                ),
            Name = npcDef.Name ?? string.Empty,
            SpriteId =
                actualSpriteId
                ?? throw new InvalidOperationException($"NPC {npcDef.NpcId} has null SpriteId"),
            MapId =
                mapDefinition.Id
                ?? throw new InvalidOperationException(
                    $"Map definition {mapDefinition.Id} has null Id"
                ),
            Elevation = npcDef.Elevation,
            VisibilityFlag = npcDef.VisibilityFlag,
            BehaviorId = npcDef.BehaviorId,
        };

        var components = new List<object>
        {
            npcComponent,
            new SpriteComponent
            {
                SpriteId = actualSpriteId,
                FrameIndex = 0,
                FlipHorizontal = flipHorizontal,
                FlipVertical = false, // Will be updated by SpriteAnimationSystem
            },
            new PositionComponent { Position = npcPixelPosition },
            new RenderableComponent
            {
                IsVisible = isVisible, // Set based on flag value
                RenderOrder = npcDef.Elevation,
                Opacity = 1.0f,
            },
            new GridMovement(defaultNpcMovementSpeed)
            {
                FacingDirection = Direction.South, // Default - behavior scripts will set actual direction
                MovementDirection = Direction.South,
            },
            new ActiveMapEntity(), // Tag NPCs in loaded maps immediately
        };

        // Add ScriptAttachmentComponent if behavior ID (ScriptDefinition ID) is specified
        // Fail fast - no fallback code
        Dictionary<string, object>? mergedParameters = null;
        ScriptDefinition? scriptDefForVariables = null;

        if (!string.IsNullOrWhiteSpace(npcDef.BehaviorId))
        {
            // Look up ScriptDefinition directly (BehaviorId now references ScriptDefinition ID directly)
            var scriptDef = _registry.GetById<ScriptDefinition>(npcDef.BehaviorId);

            if (scriptDef == null)
                throw new InvalidOperationException(
                    $"ScriptDefinition '{npcDef.BehaviorId}' not found for NPC '{npcDef.NpcId}'. "
                        + "Ensure the script definition exists and is loaded."
                );

            // Merge parameters from ScriptDefinition defaults and NPCDefinition overrides
            mergedParameters = MergeScriptParameters(scriptDef, npcDef);
            scriptDefForVariables = scriptDef;

            _logger.Debug(
                "Prepared behavior script {ScriptId} for NPC {NpcId}",
                scriptDef.Id,
                npcDef.NpcId
            );
        }

        // Store behavior script ID for later use in entity creation
        string? behaviorScriptId = null;
        if (mergedParameters != null && scriptDefForVariables != null)
            behaviorScriptId = scriptDefForVariables.Id;

        // Prepare interaction script data if it exists (before entity creation)
        ScriptAttachmentData? interactionScriptData = null;
        InteractionComponent? interactionComponent = null;

        if (!string.IsNullOrWhiteSpace(npcDef.InteractionId))
        {
            // Validate ScriptDefinition exists (fail fast)
            var interactionScriptDef = _registry.GetById<ScriptDefinition>(npcDef.InteractionId);
            if (interactionScriptDef == null)
                throw new InvalidOperationException(
                    $"ScriptDefinition '{npcDef.InteractionId}' not found for NPC '{npcDef.NpcId}' interaction. "
                        + "Ensure the script definition exists and is loaded."
                );

            // Create InteractionComponent
            interactionComponent = new InteractionComponent
            {
                InteractionId = npcDef.NpcId,
                ScriptDefinitionId = interactionScriptDef.Id,
                Width = 1, // 1 tile
                Height = 1, // 1 tile
                Elevation = npcDef.Elevation,
                RequiredFacing = null, // No facing requirement by default
                IsEnabled = true,
            };

            // Create ScriptAttachmentData for interaction script
            interactionScriptData = CreateScriptAttachmentData(
                interactionScriptDef.Id,
                npcDef.NpcId,
                "interaction"
            );
        }

        // Create ScriptAttachmentComponent with collection of scripts
        var hasBehaviorScript = !string.IsNullOrEmpty(behaviorScriptId);
        var hasInteractionScript = interactionScriptData.HasValue;

        // Create entity with all components at once (matching PlayerSystem pattern)
        // Use World.Create() with individual component arguments, not array
        // This matches how PlayerSystem creates entities and avoids Arch.Core array issues
        // Note: ScriptAttachmentComponent is now created separately and added if scripts exist
        Entity npcEntity;
        if (hasBehaviorScript && hasInteractionScript)
        {
            // Has both behavior and interaction scripts - create component with both
            var scriptComp = new ScriptAttachmentComponent();
            scriptComp.Scripts = new Dictionary<string, ScriptAttachmentData>();

            // Add behavior script
            var behaviorData = CreateScriptAttachmentData(
                behaviorScriptId!,
                npcDef.NpcId,
                "behavior"
            );
            scriptComp.Scripts[behaviorScriptId!] = behaviorData;

            // Add interaction script
            scriptComp.Scripts[interactionScriptData!.Value.ScriptDefinitionId] =
                interactionScriptData.Value;

            npcEntity = World.Create(
                npcComponent,
                (SpriteComponent)components[1],
                (PositionComponent)components[2],
                (RenderableComponent)components[3],
                (GridMovement)components[4],
                (ActiveMapEntity)components[5],
                scriptComp,
                interactionComponent!.Value
            );
        }
        else if (hasBehaviorScript) // Has only behavior script
        {
            var scriptComp = new ScriptAttachmentComponent();
            scriptComp.Scripts = new Dictionary<string, ScriptAttachmentData>();

            // Add behavior script
            var behaviorData = CreateScriptAttachmentData(
                behaviorScriptId!,
                npcDef.NpcId,
                "behavior"
            );
            scriptComp.Scripts[behaviorScriptId!] = behaviorData;

            npcEntity = World.Create(
                npcComponent,
                (SpriteComponent)components[1],
                (PositionComponent)components[2],
                (RenderableComponent)components[3],
                (GridMovement)components[4],
                (ActiveMapEntity)components[5],
                scriptComp
            );
        }
        else if (hasInteractionScript) // Has only interaction script
        {
            var scriptComp = new ScriptAttachmentComponent();
            scriptComp.Scripts = new Dictionary<string, ScriptAttachmentData>();
            scriptComp.Scripts[interactionScriptData!.Value.ScriptDefinitionId] =
                interactionScriptData.Value;

            npcEntity = World.Create(
                npcComponent,
                (SpriteComponent)components[1],
                (PositionComponent)components[2],
                (RenderableComponent)components[3],
                (GridMovement)components[4],
                (ActiveMapEntity)components[5],
                scriptComp,
                interactionComponent!.Value
            );
        }
        else // No ScriptAttachmentComponent
        {
            npcEntity = World.Create(
                npcComponent,
                (SpriteComponent)components[1],
                (PositionComponent)components[2],
                (RenderableComponent)components[3],
                (GridMovement)components[4],
                (ActiveMapEntity)components[5]
            );
        }

        // Mark dirty to notify ScriptLifecycleSystem that scripts were attached (consolidated from all branches)
        if (hasBehaviorScript || hasInteractionScript)
            ScriptChangeTracker.MarkDirty();

        // Add EntityVariablesComponent AFTER entity creation (if we have merged parameters)
        // This avoids issues with Arch.Core and struct components containing reference types
        if (mergedParameters != null && mergedParameters.Count > 0 && scriptDefForVariables != null)
        {
            var variablesComponent = new EntityVariablesComponent
            {
                Variables = new Dictionary<string, string>(),
                VariableTypes = new Dictionary<string, string>(),
            };

            foreach (var kvp in mergedParameters)
            {
                var key = ScriptStateKeys.GetParameterKey(scriptDefForVariables.Id, kvp.Key);
                variablesComponent.Variables[key] = kvp.Value?.ToString() ?? string.Empty;
                variablesComponent.VariableTypes[key] =
                    scriptDefForVariables.Parameters?.FirstOrDefault(p => p.Name == kvp.Key)?.Type
                    ?? "string";
            }

            World.Add(npcEntity, variablesComponent);
        }

        // Log interaction script attachment (components were added during entity creation)
        if (interactionScriptData.HasValue)
            _logger.Debug(
                "Attached interaction script {ScriptId} to NPC {NpcId} (entity {EntityId}) during entity creation, IsActive={IsActive}",
                interactionScriptData.Value.ScriptDefinitionId,
                npcDef.NpcId,
                npcEntity.Id,
                interactionScriptData.Value.IsActive
            );

        // Preload sprite texture
        _resourceManager.LoadTexture(actualSpriteId);

        // Fire NpcLoadedEvent
        var loadedEvent = new NpcLoadedEvent
        {
            NpcEntity = npcEntity,
            NpcId = npcDef.NpcId,
            MapId = mapDefinition.Id,
        };
        EventBus.Send(ref loadedEvent);

        return npcEntity;
    }

    /// <summary>
    ///     Creates a ScriptAttachmentData for a script definition.
    ///     Validates that the script definition exists and gets mod ID from metadata.
    /// </summary>
    /// <param name="scriptDefinitionId">The script definition ID to attach.</param>
    /// <param name="npcId">The NPC ID (for error messages).</param>
    /// <param name="context">The context string ("behavior" or "interaction") for error messages.</param>
    /// <returns>The created ScriptAttachmentData.</returns>
    /// <exception cref="InvalidOperationException">Thrown if script definition or metadata not found.</exception>
    private ScriptAttachmentData CreateScriptAttachmentData(
        string scriptDefinitionId,
        string npcId,
        string context
    )
    {
        // Get script definition to access priority
        var scriptDef = _registry.GetById<ScriptDefinition>(scriptDefinitionId);
        if (scriptDef == null)
            throw new InvalidOperationException(
                $"ScriptDefinition '{scriptDefinitionId}' not found for NPC '{npcId}' {context}. "
                    + "Ensure the script definition exists and is loaded."
            );

        // Get mod ID from definition metadata (same approach as ScriptLoaderService)
        var scriptDefMetadata = _registry.GetById(scriptDefinitionId);
        if (scriptDefMetadata == null)
            throw new InvalidOperationException(
                $"Script definition metadata not found for {context} script definition {scriptDefinitionId}. "
                    + $"Cannot attach {context} script to NPC {npcId}."
            );
        var modId = scriptDefMetadata.OriginalModId;

        return new ScriptAttachmentData
        {
            ScriptDefinitionId = scriptDefinitionId,
            Priority = scriptDef.Priority,
            IsActive = true,
            ModId = modId,
            IsInitialized = false,
        };
    }

    /// <summary>
    ///     Merges script parameters from ScriptDefinition defaults and NPCDefinition overrides.
    /// </summary>
    private Dictionary<string, object> MergeScriptParameters(
        ScriptDefinition scriptDef,
        NpcDefinition npcDef
    )
    {
        // Step 1: Start with ScriptDefinition defaults
        var mergedParameters = ScriptParameterResolver.GetDefaults(scriptDef);

        // Step 2: Apply NPCDefinition.behaviorParameters
        if (npcDef.BehaviorParameters != null)
            ScriptParameterResolver.ApplyOverrides(
                mergedParameters,
                npcDef.BehaviorParameters,
                scriptDef
            );

        // Step 3: Validate merged parameters against ScriptDefinition
        ScriptParameterResolver.ValidateParameters(mergedParameters, scriptDef);

        return mergedParameters;
    }

    /// <summary>
    ///     Validates merged parameters against ScriptDefinition (min/max bounds, etc.).
    ///     Throws exceptions for invalid parameters (fail fast).
    /// </summary>
    private void ValidateMergedParameters(
        Dictionary<string, object> mergedParameters,
        ScriptDefinition scriptDef
    )
    {
        if (scriptDef.Parameters == null)
            return;

        foreach (var paramDef in scriptDef.Parameters)
            if (mergedParameters.TryGetValue(paramDef.Name, out var value))
                // Validate min/max bounds
                if (paramDef.Min != null || paramDef.Max != null)
                {
                    var numericValue = value switch
                    {
                        int i => (double)i,
                        float f => (double)f,
                        double d => d,
                        _ => (double?)null,
                    };

                    if (numericValue != null)
                    {
                        if (paramDef.Min != null && numericValue < paramDef.Min)
                            throw new ArgumentException(
                                $"Parameter '{paramDef.Name}' value '{value}' is below minimum '{paramDef.Min}' for script '{scriptDef.Id}'. "
                                    + $"Value must be >= {paramDef.Min}.",
                                nameof(mergedParameters)
                            );

                        if (paramDef.Max != null && numericValue > paramDef.Max)
                            throw new ArgumentException(
                                $"Parameter '{paramDef.Name}' value '{value}' is above maximum '{paramDef.Max}' for script '{scriptDef.Id}'. "
                                    + $"Value must be <= {paramDef.Max}.",
                                nameof(mergedParameters)
                            );
                    }
                }
    }

    /// <summary>
    ///     Creates a MapBorderComponent from map definition border data.
    ///     Validates border data, gets tileset definition, calculates source rectangles, and creates component.
    /// </summary>
    /// <param name="mapDefinition">The map definition containing border data.</param>
    /// <returns>The created MapBorderComponent, or component with HasBorder=false if validation fails.</returns>
    private MapBorderComponent CreateMapBorderComponent(MapDefinition mapDefinition)
    {
        var border = mapDefinition.Border;
        if (border == null)
            return new MapBorderComponent
            {
                BottomLayerGids = Array.Empty<int>(),
                TopLayerGids = Array.Empty<int>(),
                TilesetId = string.Empty,
                BottomSourceRects = Array.Empty<Rectangle>(),
                TopSourceRects = Array.Empty<Rectangle>(),
            };

        // Validate bottom layer has exactly 4 elements (fail fast)
        if (border.BottomLayer == null || border.BottomLayer.Count != 4)
        {
            throw new InvalidOperationException(
                $"Map '{mapDefinition.Id}' has invalid border bottom layer (expected 4 elements, got {border.BottomLayer?.Count ?? 0}). "
                    + "Border bottom layer must have exactly 4 tile IDs: [TopLeft, TopRight, BottomLeft, BottomRight]."
            );
        }

        // Validate tileset ID (fail fast)
        if (string.IsNullOrEmpty(border.TilesetId))
        {
            throw new InvalidOperationException(
                $"Map '{mapDefinition.Id}' has border data but no tileset ID. "
                    + "Border definition must include a valid TilesetId."
            );
        }

        // Get tileset definition (fail fast - no fallback code)
        var tilesetDefinition = _resourceManager.GetTilesetDefinition(border.TilesetId);

        // Find tileset reference to get firstGid
        var firstGid = 1;
        if (mapDefinition.Tilesets != null)
        {
            var tilesetRef = mapDefinition.Tilesets.FirstOrDefault(tr =>
                tr.TilesetId == border.TilesetId
            );
            if (tilesetRef != null)
                firstGid = tilesetRef.FirstGid;
            else
                _logger.Warning(
                    "Tileset reference not found for {TilesetId} in map {MapId}, using firstGid=1",
                    border.TilesetId,
                    mapDefinition.Id
                );
        }

        // Pre-calculate source rectangles for bottom layer
        var bottomSourceRects = new Rectangle[4];
        for (var i = 0; i < 4; i++)
        {
            var localTileId = border.BottomLayer[i];
            if (localTileId > 0)
            {
                // Convert local tile ID to global GID
                var globalGid = localTileId + firstGid - 1;
                try
                {
                    var sourceRect = _resourceManager.CalculateTilesetSourceRectangle(
                        border.TilesetId,
                        globalGid,
                        firstGid
                    );
                    bottomSourceRects[i] = sourceRect;
                }
                catch (Exception)
                {
                    // Invalid source rectangle - use empty
                    bottomSourceRects[i] = Rectangle.Empty;
                }
            }
            else
            {
                bottomSourceRects[i] = Rectangle.Empty;
            }
        }

        // Pre-calculate source rectangles for top layer
        var topSourceRects = new Rectangle[4];
        if (border.TopLayer != null && border.TopLayer.Count == 4)
            for (var i = 0; i < 4; i++)
            {
                var localTileId = border.TopLayer[i];
                if (localTileId > 0)
                {
                    // Convert local tile ID to global GID
                    var globalGid = localTileId + firstGid - 1;
                    try
                    {
                        var sourceRect = _resourceManager.CalculateTilesetSourceRectangle(
                            border.TilesetId,
                            globalGid,
                            firstGid
                        );
                        topSourceRects[i] = sourceRect;
                    }
                    catch (Exception)
                    {
                        // Invalid source rectangle - use empty
                        topSourceRects[i] = Rectangle.Empty;
                    }
                }
                else
                {
                    topSourceRects[i] = Rectangle.Empty;
                }
            }
        else
            // No top layer or invalid top layer - all empty
            for (var i = 0; i < 4; i++)
                topSourceRects[i] = Rectangle.Empty;

        return new MapBorderComponent
        {
            BottomLayerGids = border.BottomLayer.ToArray(),
            TopLayerGids = border.TopLayer?.ToArray() ?? Array.Empty<int>(),
            TilesetId = border.TilesetId,
            BottomSourceRects = bottomSourceRects,
            TopSourceRects = topSourceRects,
        };
    }

    /// <summary>
    ///     Validates that tileset firstGid ranges don't overlap.
    ///     Throws exception if ranges overlap - fail fast, no fallback code.
    /// </summary>
    /// <param name="mapId">The map ID for error messages.</param>
    /// <param name="tilesets">The tileset references to validate.</param>
    /// <exception cref="InvalidOperationException">Thrown when tileset ranges overlap.</exception>
    private void ValidateTilesetRanges(string mapId, List<TilesetReference> tilesets)
    {
        if (tilesets == null || tilesets.Count <= 1)
            return;

        // Get tileset definitions to calculate range end (firstGid + tileCount - 1)
        var tilesetRanges = new List<(string TilesetId, int FirstGid, int LastGid)>();
        foreach (var tilesetRef in tilesets)
        {
            try
            {
                var definition = _resourceManager.GetTilesetDefinition(tilesetRef.TilesetId);
                var lastGid = tilesetRef.FirstGid + definition.TileCount - 1;
                tilesetRanges.Add((tilesetRef.TilesetId, tilesetRef.FirstGid, lastGid));
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Cannot validate tileset ranges for map '{mapId}': failed to load tileset definition '{tilesetRef.TilesetId}'. "
                        + $"Ensure the tileset definition exists and is valid.",
                    ex
                );
            }
        }

        // Check for overlapping ranges
        for (int i = 0; i < tilesetRanges.Count; i++)
        {
            for (int j = i + 1; j < tilesetRanges.Count; j++)
            {
                var range1 = tilesetRanges[i];
                var range2 = tilesetRanges[j];

                // Check if ranges overlap: range1 overlaps range2 if range1.FirstGid <= range2.LastGid && range1.LastGid >= range2.FirstGid
                if (range1.FirstGid <= range2.LastGid && range1.LastGid >= range2.FirstGid)
                {
                    throw new InvalidOperationException(
                        $"Map '{mapId}' has overlapping tileset ranges: "
                            + $"'{range1.TilesetId}' (firstGid: {range1.FirstGid}, lastGid: {range1.LastGid}) and "
                            + $"'{range2.TilesetId}' (firstGid: {range2.FirstGid}, lastGid: {range2.LastGid}). "
                            + "Tileset ranges must not overlap. This is likely a bug in Porycon3 map conversion."
                    );
                }
            }
        }
    }

    /// <summary>
    ///     Resolves variable sprite ID to actual sprite ID for an NPC.
    ///     Throws exception if resolution fails (fail fast, no fallback code).
    /// </summary>
    /// <param name="npcDef">The NPC definition containing the sprite ID.</param>
    /// <returns>The resolved sprite ID (always a real sprite ID, never a variable sprite ID).</returns>
    /// <exception cref="InvalidOperationException">Thrown if variable sprite resolution fails.</exception>
    private string ResolveNpcSpriteId(NpcDefinition npcDef)
    {
        var spriteId = npcDef.SpriteId;

        // If not a variable sprite, return as-is
        if (_variableSpriteResolver?.IsVariableSprite(spriteId) != true)
            return spriteId;

        // Attempt to resolve variable sprite - fail fast if cannot resolve
        try
        {
            var resolved = _variableSpriteResolver.ResolveVariableSprite(spriteId, Entity.Null);
            if (resolved == null)
            {
                _logger.Error(
                    "Failed to resolve variable sprite '{VariableSpriteId}' for NPC '{NpcId}'. Invalid variable sprite format.",
                    spriteId,
                    npcDef.NpcId
                );
                throw new InvalidOperationException(
                    $"Cannot create NPC '{npcDef.NpcId}': variable sprite resolution failed. "
                        + $"Variable sprite ID: '{spriteId}'. Invalid variable sprite format."
                );
            }

            return resolved;
        }
        catch (InvalidOperationException ex)
        {
            // Re-throw with context about NPC creation
            _logger.Error(
                ex,
                "Cannot resolve variable sprite '{VariableSpriteId}' for NPC '{NpcId}'. Game state variable is not set.",
                spriteId,
                npcDef.NpcId
            );
            throw new InvalidOperationException(
                $"Cannot create NPC '{npcDef.NpcId}': variable sprite resolution failed. "
                    + $"Variable sprite ID: '{spriteId}'. {ex.Message}",
                ex
            );
        }
    }
}
