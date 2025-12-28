using System;
using System.Collections.Generic;
using System.Linq;
using Arch.Core;
using Arch.System;
using Microsoft.Xna.Framework;
using MonoBall.Core;
using MonoBall.Core.Constants;
using MonoBall.Core.ECS;
using MonoBall.Core.ECS.Components;
using MonoBall.Core.ECS.Components.Audio;
using MonoBall.Core.ECS.Events;
using MonoBall.Core.ECS.Utilities;
using MonoBall.Core.Maps;
using MonoBall.Core.Maps.Utilities;
using MonoBall.Core.Mods;
using MonoBall.Core.Mods.Definitions;
using MonoBall.Core.Scripting.Services;
using MonoBall.Core.Scripting.Utilities;
using Serilog;

namespace MonoBall.Core.ECS.Systems
{
    /// <summary>
    /// System responsible for loading and unloading maps, creating tile chunks, and managing map connections.
    /// </summary>
    public class MapLoaderSystem : BaseSystem<World, float>, IPrioritizedSystem
    {
        private readonly int _chunkSize; // 16x16 tiles per chunk (cached from constants service)
        private readonly DefinitionRegistry _registry;
        private readonly ITilesetLoaderService? _tilesetLoader;
        private readonly ISpriteLoaderService? _spriteLoader;
        private readonly Services.IFlagVariableService? _flagVariableService;
        private readonly Services.IVariableSpriteResolver? _variableSpriteResolver;
        private readonly ILogger _logger;
        private readonly IConstantsService _constants;
        private readonly HashSet<string> _loadedMaps = new HashSet<string>();
        private readonly Dictionary<string, Entity> _mapEntities = new Dictionary<string, Entity>();
        private readonly Dictionary<string, List<Entity>> _mapChunkEntities =
            new Dictionary<string, List<Entity>>();
        private readonly Dictionary<string, List<Entity>> _mapConnectionEntities =
            new Dictionary<string, List<Entity>>();
        private readonly Dictionary<string, List<Entity>> _mapNpcEntities =
            new Dictionary<string, List<Entity>>();
        private readonly Dictionary<string, Vector2> _mapPositions =
            new Dictionary<string, Vector2>(); // Map positions in tile coordinates

        /// <summary>
        /// Gets the execution priority for this system.
        /// </summary>
        public int Priority => SystemPriority.MapLoader;

        /// <summary>
        /// Initializes a new instance of the MapLoaderSystem.
        /// </summary>
        /// <param name="world">The ECS world.</param>
        /// <param name="registry">The definition registry.</param>
        /// <param name="tilesetLoader">Optional tileset loader service for preloading tilesets.</param>
        /// <param name="spriteLoader">Optional sprite loader service for loading NPC sprites.</param>
        /// <param name="flagVariableService">Optional flag/variable service for checking NPC visibility flags.</param>
        /// <param name="variableSpriteResolver">Optional variable sprite resolver for resolving variable sprite IDs.</param>
        /// <param name="logger">The logger for logging operations.</param>
        /// <param name="constants">The constants service for accessing game constants. Required.</param>
        public MapLoaderSystem(
            World world,
            DefinitionRegistry registry,
            ITilesetLoaderService? tilesetLoader = null,
            ISpriteLoaderService? spriteLoader = null,
            Services.IFlagVariableService? flagVariableService = null,
            Services.IVariableSpriteResolver? variableSpriteResolver = null,
            ILogger logger = null!,
            IConstantsService? constants = null
        )
            : base(world)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _tilesetLoader = tilesetLoader;
            _spriteLoader = spriteLoader;
            _flagVariableService = flagVariableService;
            _variableSpriteResolver = variableSpriteResolver;
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _constants = constants ?? throw new ArgumentNullException(nameof(constants));

            // Cache chunk size from constants service (performance optimization)
            _chunkSize = _constants.Get<int>("TileChunkSize");
        }

        /// <summary>
        /// Loads a map by its ID. Creates map entity, tile chunks, and connection entities.
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
            Vector2 mapTilePosition = tilePosition ?? Vector2.Zero;
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
            int chunksCreated = CreateTileChunks(mapEntity, mapDefinition, mapTilePosition);
            _logger.Information(
                "Created {ChunkCount} tile chunks for map {MapId}",
                chunksCreated,
                mapId
            );

            // Create connection entities
            CreateConnections(mapEntity, mapDefinition);

            // Create NPC entities
            int npcsCreated = CreateNpcs(mapEntity, mapDefinition, mapTilePosition);
            _logger.Information("Created {NpcCount} NPCs for map {MapId}", npcsCreated, mapId);

            // Fire MapLoadedEvent
            var loadedEvent = new MapLoadedEvent { MapId = mapId, MapEntity = mapEntity };
            EventBus.Send(ref loadedEvent);

            // Note: MapTransitionEvent should be fired by a system that detects when the player
            // actually crosses map boundaries, not when maps are loaded. MapLoaderSystem only
            // loads maps - it doesn't know when the player transitions.

            // Proactively load connected maps with proper positioning
            if (mapDefinition.Connections != null)
            {
                foreach (var kvp in mapDefinition.Connections)
                {
                    string directionStr = kvp.Key.ToLowerInvariant();
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
                    Vector2 connectedMapPosition = CalculateConnectedMapPosition(
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
                            float tolerance = 0.01f;
                            if (
                                Math.Abs(existingPosition.X - connectedMapPosition.X) > tolerance
                                || Math.Abs(existingPosition.Y - connectedMapPosition.Y) > tolerance
                            )
                            {
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
                }
            }

            _logger.Information("Map loaded: {MapId}", mapId);
        }

        /// <summary>
        /// Unloads a map by its ID. Removes all associated entities.
        /// </summary>
        /// <param name="mapId">The map definition ID to unload.</param>
        public void UnloadMap(string mapId)
        {
            if (string.IsNullOrEmpty(mapId))
            {
                return;
            }

            if (!_loadedMaps.Contains(mapId))
            {
                return;
            }

            _logger.Information("Unloading map: {MapId}", mapId);

            if (_mapEntities.TryGetValue(mapId, out var mapEntity))
            {
                // Destroy all chunk entities
                if (_mapChunkEntities.TryGetValue(mapId, out var chunkEntities))
                {
                    foreach (var chunkEntity in chunkEntities)
                    {
                        // Fire EntityDestroyedEvent before destroying
                        var destroyedEvent = new Events.EntityDestroyedEvent
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
                        var destroyedEvent = new Events.EntityDestroyedEvent
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
                            var npcUnloadedEvent = new Events.NpcUnloadedEvent
                            {
                                NpcId = npcComp.NpcId,
                                MapId = mapId,
                            };
                            EventBus.Send(ref npcUnloadedEvent);
                        }
                        // Fire EntityDestroyedEvent before destroying
                        var destroyedEvent = new Events.EntityDestroyedEvent
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
                var mapDestroyedEvent = new Events.EntityDestroyedEvent
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
            if (_tilesetLoader == null)
            {
                _logger.Warning("TilesetLoader is null, cannot preload tilesets");
                return;
            }

            if (mapDefinition.TilesetRefs == null || mapDefinition.TilesetRefs.Count == 0)
            {
                _logger.Warning("Map {MapId} has no tileset references", mapDefinition.Id);
                return;
            }

            _logger.Information(
                "Preloading {Count} tileset(s) for map {MapId}",
                mapDefinition.TilesetRefs.Count,
                mapDefinition.Id
            );
            foreach (var tilesetRef in mapDefinition.TilesetRefs)
            {
                _logger.Debug(
                    "Loading tileset {TilesetId} (firstGid: {FirstGid})",
                    tilesetRef.TilesetId,
                    tilesetRef.FirstGid
                );
                var texture = _tilesetLoader.LoadTileset(tilesetRef.TilesetId);
                if (texture == null)
                {
                    _logger.Warning("Failed to load tileset {TilesetId}", tilesetRef.TilesetId);
                }
                else
                {
                    _logger.Information(
                        "Successfully loaded tileset {TilesetId}",
                        tilesetRef.TilesetId
                    );
                }
            }
        }

        /// <summary>
        /// Calculates the tile position of a connected map based on direction and offset.
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

            int totalChunks = 0;
            int layerIndex = 0;
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
                    _logger.Warning(
                        "Failed to decode tile data for layer {LayerId}",
                        layer.LayerId
                    );
                    layerIndex++;
                    continue;
                }

                _logger.Debug(
                    "Decoded {TileCount} tiles for layer {LayerId}",
                    tileIndices.Length,
                    layer.LayerId
                );

                // Determine tileset for this layer (use first tileset for now)
                string tilesetId = string.Empty;
                int firstGid = 1;
                if (mapDefinition.TilesetRefs != null && mapDefinition.TilesetRefs.Count > 0)
                {
                    tilesetId = mapDefinition.TilesetRefs[0].TilesetId;
                    firstGid = mapDefinition.TilesetRefs[0].FirstGid;
                }

                // Create chunks
                int chunksX = (int)Math.Ceiling((double)layer.Width / _chunkSize);
                int chunksY = (int)Math.Ceiling((double)layer.Height / _chunkSize);

                _logger.Debug(
                    "Creating {ChunksX}x{ChunksY} chunks for layer {LayerId}",
                    chunksX,
                    chunksY,
                    layer.LayerId
                );

                int layerChunks = 0;
                for (int chunkY = 0; chunkY < chunksY; chunkY++)
                {
                    for (int chunkX = 0; chunkX < chunksX; chunkX++)
                    {
                        int chunkStartX = chunkX * _chunkSize;
                        int chunkStartY = chunkY * _chunkSize;
                        int chunkWidth = Math.Min(_chunkSize, layer.Width - chunkStartX);
                        int chunkHeight = Math.Min(_chunkSize, layer.Height - chunkStartY);

                        // Extract tile indices for this chunk
                        int[] chunkTileIndices = new int[chunkWidth * chunkHeight];
                        for (int y = 0; y < chunkHeight; y++)
                        {
                            for (int x = 0; x < chunkWidth; x++)
                            {
                                int tileX = chunkStartX + x;
                                int tileY = chunkStartY + y;
                                int tileIndex = tileY * layer.Width + tileX;
                                chunkTileIndices[y * chunkWidth + x] = tileIndices[tileIndex];
                            }
                        }

                        // Detect animated tiles in this chunk
                        var animatedTiles = new System.Collections.Generic.Dictionary<
                            int,
                            Components.TileAnimationState
                        >();
                        bool hasAnimatedTiles = false;

                        if (_tilesetLoader != null)
                        {
                            for (int i = 0; i < chunkTileIndices.Length; i++)
                            {
                                int gid = chunkTileIndices[i];
                                if (gid > 0)
                                {
                                    int localTileId = gid - firstGid;
                                    if (localTileId >= 0)
                                    {
                                        var animation = _tilesetLoader.GetTileAnimation(
                                            tilesetId,
                                            localTileId
                                        );
                                        if (animation != null && animation.Count > 0)
                                        {
                                            var animState = new Components.TileAnimationState
                                            {
                                                AnimationTilesetId = tilesetId,
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
                        }

                        // Calculate world position for chunk (relative to map's tile position)
                        // Convert chunk tile coordinates to pixel coordinates, offset by map position
                        Vector2 chunkPosition = new Vector2(
                            (mapTilePosition.X + chunkStartX) * mapDefinition.TileWidth,
                            (mapTilePosition.Y + chunkStartY) * mapDefinition.TileHeight
                        );

                        // Create chunk entity with components
                        Entity chunkEntity;
                        if (hasAnimatedTiles)
                        {
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
                                    TilesetId = tilesetId,
                                    TileIndices = chunkTileIndices,
                                    FirstGid = firstGid,
                                    HasAnimatedTiles = hasAnimatedTiles,
                                },
                                new PositionComponent { Position = chunkPosition },
                                new RenderableComponent
                                {
                                    IsVisible = true,
                                    RenderOrder = layerIndex,
                                    Opacity = layer.Opacity,
                                },
                                new Components.AnimatedTileDataComponent
                                {
                                    AnimatedTiles = animatedTiles,
                                }
                            );
                        }
                        else
                        {
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
                                    TilesetId = tilesetId,
                                    TileIndices = chunkTileIndices,
                                    FirstGid = firstGid,
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
                        }

                        // Track chunk entity for unloading
                        _mapChunkEntities[mapDefinition.Id].Add(chunkEntity);
                        layerChunks++;
                    }
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
            {
                return;
            }

            foreach (var kvp in mapDefinition.Connections)
            {
                string directionStr = kvp.Key.ToLowerInvariant();
                var connection = kvp.Value;

                MapConnectionDirection direction = directionStr switch
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
        /// Maps NPC direction string to animation name.
        /// </summary>
        /// <param name="direction">The direction string (null, "up", "down", "left", "right").</param>
        /// <returns>The animation name.</returns>
        private string MapDirectionToAnimation(string? direction)
        {
            return direction?.ToLowerInvariant() switch
            {
                "up" => "face_north",
                "left" => "face_west",
                "right" => "face_east",
                "down" => "face_south",
                _ => "face_south", // Default to face_south for null or unknown directions
            };
        }

        /// <summary>
        /// Parses a direction string from map definition to Direction enum.
        /// Supports: "north"/"up", "south"/"down", "east"/"right", "west"/"left".
        /// </summary>
        /// <param name="direction">The direction string (null, "up", "down", "left", "right", "north", "south", "east", "west").</param>
        /// <param name="defaultDirection">The default direction if parsing fails (default: Direction.South).</param>
        /// <returns>The parsed Direction enum value.</returns>
        private Direction ParseDirection(
            string? direction,
            Direction defaultDirection = Direction.South
        )
        {
            return direction?.ToLowerInvariant() switch
            {
                "north" or "up" => Direction.North,
                "south" or "down" => Direction.South,
                "west" or "left" => Direction.West,
                "east" or "right" => Direction.East,
                _ => defaultDirection,
            };
        }

        /// <summary>
        /// Creates NPC entities for a map.
        /// </summary>
        /// <param name="mapEntity">The map entity.</param>
        /// <param name="mapDefinition">The map definition.</param>
        /// <param name="mapTilePosition">The map position in tile coordinates.</param>
        /// <returns>The number of NPCs created.</returns>
        private int CreateNpcs(
            Entity mapEntity,
            MapDefinition mapDefinition,
            Vector2 mapTilePosition
        )
        {
            if (mapDefinition.Npcs == null || mapDefinition.Npcs.Count == 0)
            {
                return 0;
            }

            if (_spriteLoader == null)
            {
                _logger.Warning(
                    "SpriteLoader is null, cannot create NPCs for map {MapId}",
                    mapDefinition.Id
                );
                return 0;
            }

            int npcsCreated = 0;

            foreach (var npcDef in mapDefinition.Npcs)
            {
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
            }

            return npcsCreated;
        }

        /// <summary>
        /// Creates an NPC entity with all required components.
        /// Validates inputs and throws exceptions if invalid.
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
            {
                throw new ArgumentNullException(nameof(npcDef));
            }

            if (mapDefinition == null)
            {
                throw new ArgumentNullException(nameof(mapDefinition));
            }

            if (_spriteLoader == null)
            {
                throw new InvalidOperationException(
                    "SpriteLoader is required to create NPCs. Ensure SpriteLoader is provided in MapLoaderSystem constructor."
                );
            }

            // CRITICAL: Resolve variable sprite FIRST, before validation
            // Variable sprite IDs like {base:sprite:npcs/generic/var_rival} are not valid sprite definitions
            string actualSpriteId = npcDef.SpriteId;
            if (_variableSpriteResolver?.IsVariableSprite(npcDef.SpriteId) == true)
            {
                try
                {
                    var resolved = _variableSpriteResolver.ResolveVariableSprite(
                        npcDef.SpriteId,
                        Entity.Null
                    );
                    if (resolved == null)
                    {
                        _logger.Error(
                            "Failed to resolve variable sprite '{VariableSpriteId}' for NPC '{NpcId}'. Invalid variable sprite format.",
                            npcDef.SpriteId,
                            npcDef.NpcId
                        );
                        throw new InvalidOperationException(
                            $"Cannot create NPC '{npcDef.NpcId}': variable sprite resolution failed. "
                                + $"Variable sprite ID: '{npcDef.SpriteId}'. Invalid variable sprite format."
                        );
                    }
                    actualSpriteId = resolved;
                }
                catch (InvalidOperationException ex)
                {
                    // Re-throw with context about NPC creation
                    _logger.Error(
                        ex,
                        "Cannot resolve variable sprite '{VariableSpriteId}' for NPC '{NpcId}'. Game state variable is not set.",
                        npcDef.SpriteId,
                        npcDef.NpcId
                    );
                    throw new InvalidOperationException(
                        $"Cannot create NPC '{npcDef.NpcId}': variable sprite resolution failed. "
                            + $"Variable sprite ID: '{npcDef.SpriteId}'. {ex.Message}",
                        ex
                    );
                }
            }

            // CRITICAL: Always validate the RESOLVED sprite ID, never the variable sprite ID
            // Variable sprite IDs like {base:sprite:npcs/generic/var_rival} are not valid sprite definitions
            SpriteValidationHelper.ValidateSpriteDefinition(
                _spriteLoader,
                _logger,
                actualSpriteId, // Always a real sprite ID, never a variable sprite ID
                "NPC",
                npcDef.NpcId,
                throwOnInvalid: true
            );

            // Parse direction from map definition
            Direction facingDirection = ParseDirection(npcDef.Direction, Direction.South);

            // Map direction to animation name
            string animationName = MapDirectionToAnimation(npcDef.Direction);

            // Validate animation exists
            // Note: NPCs use forgiving validation (logs warning, defaults to face_south) for resilience
            // This differs from Player creation which uses strict validation (throws on invalid)
            // CRITICAL: Use actualSpriteId (resolved), not npcDef.SpriteId (may be variable sprite)
            if (!_spriteLoader.ValidateAnimation(actualSpriteId, animationName))
            {
                _logger.Warning(
                    "Animation '{AnimationName}' not found for sprite {SpriteId} (NPC {NpcId}), defaulting to 'face_south'",
                    animationName,
                    actualSpriteId,
                    npcDef.NpcId
                );
                animationName = "face_south";
            }

            // Get sprite definition and animation to determine initial flip state
            // CRITICAL: Use actualSpriteId (resolved), not npcDef.SpriteId (may be variable sprite)
            var spriteDefinition = _spriteLoader.GetSpriteDefinition(actualSpriteId);
            var animation = spriteDefinition?.Animations?.FirstOrDefault(a =>
                a.Name == animationName
            );
            bool flipHorizontal = animation?.FlipHorizontal ?? false;

            // NPC coordinates in JSON are already in pixel coordinates (not tile coordinates)
            // Add map pixel position offset to get world pixel position
            Vector2 mapPixelPosition = new Vector2(
                mapTilePosition.X * mapDefinition.TileWidth,
                mapTilePosition.Y * mapDefinition.TileHeight
            );
            Vector2 npcPixelPosition = new Vector2(
                mapPixelPosition.X + npcDef.X,
                mapPixelPosition.Y + npcDef.Y
            );

            // Determine initial visibility based on flag
            bool isVisible = true;
            if (!string.IsNullOrWhiteSpace(npcDef.VisibilityFlag) && _flagVariableService != null)
            {
                isVisible = _flagVariableService.GetFlag(npcDef.VisibilityFlag);
            }

            // Create NPC entity with all required components
            // All NPCs get GridMovement component (even stationary ones) to store facing direction
            // Default movement speed: 3.75 tiles/second (matches oldmonoball NpcSpawnBuilder default)
            // NPCs created in loaded maps get ActiveMapEntity tag immediately for query-level filtering
            const float defaultNpcMovementSpeed = 3.75f;

            // Create NpcComponent with explicit non-null strings to avoid Arch.Core issues
            var npcComponent = new Components.NpcComponent
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
            };

            var components = new List<object>
            {
                npcComponent,
                new Components.SpriteAnimationComponent
                {
                    CurrentAnimationName = animationName,
                    CurrentFrameIndex = 0,
                    ElapsedTime = 0.0f,
                    FlipHorizontal = flipHorizontal,
                },
                new Components.PositionComponent { Position = npcPixelPosition },
                new Components.RenderableComponent
                {
                    IsVisible = isVisible, // Set based on flag value
                    RenderOrder = npcDef.Elevation,
                    Opacity = 1.0f,
                },
                new Components.GridMovement(defaultNpcMovementSpeed)
                {
                    FacingDirection = facingDirection,
                    MovementDirection = facingDirection,
                },
                new Components.ActiveMapEntity(), // Tag NPCs in loaded maps immediately
            };

            // Add ScriptAttachmentComponent if behavior ID is specified
            // Fail fast - no fallback code
            Dictionary<string, object>? mergedParameters = null;
            Mods.Definitions.ScriptDefinition? scriptDefForVariables = null;

            if (!string.IsNullOrWhiteSpace(npcDef.BehaviorId))
            {
                // Step 1: Look up BehaviorDefinition
                var behaviorDef = _registry.GetById<Mods.Definitions.BehaviorDefinition>(
                    npcDef.BehaviorId
                );

                // Step 2: Fail fast if BehaviorDefinition not found
                if (behaviorDef == null)
                {
                    throw new InvalidOperationException(
                        $"BehaviorDefinition '{npcDef.BehaviorId}' not found for NPC '{npcDef.NpcId}'. "
                            + "Ensure the behavior definition exists and is loaded."
                    );
                }

                // Step 3: Validate scriptId is not empty
                if (string.IsNullOrWhiteSpace(behaviorDef.ScriptId))
                {
                    throw new InvalidOperationException(
                        $"BehaviorDefinition '{npcDef.BehaviorId}' has empty scriptId for NPC '{npcDef.NpcId}'. "
                            + "BehaviorDefinition must reference a valid ScriptDefinition."
                    );
                }

                // Step 4: Get ScriptDefinition from BehaviorDefinition
                var scriptDef = _registry.GetById<Mods.Definitions.ScriptDefinition>(
                    behaviorDef.ScriptId
                );

                if (scriptDef == null)
                {
                    throw new InvalidOperationException(
                        $"ScriptDefinition '{behaviorDef.ScriptId}' not found for BehaviorDefinition '{npcDef.BehaviorId}' on NPC '{npcDef.NpcId}'. "
                            + "Ensure the script definition exists and is loaded."
                    );
                }

                // Step 5: Merge parameters from all layers
                mergedParameters = MergeScriptParameters(scriptDef, behaviorDef, npcDef);
                scriptDefForVariables = scriptDef;

                // Step 6: Create ScriptAttachmentComponent
                // Get mod ID from definition metadata (same approach as ScriptLoaderService)
                var scriptDefMetadata = _registry.GetById(scriptDef.Id);
                if (scriptDefMetadata == null)
                {
                    throw new InvalidOperationException(
                        $"Script definition metadata not found for script definition {scriptDef.Id}. Cannot attach script to NPC {npcDef.NpcId}."
                    );
                }
                var modId = scriptDefMetadata.OriginalModId;

                components.Add(
                    new Components.ScriptAttachmentComponent
                    {
                        ScriptDefinitionId = scriptDef.Id,
                        Priority = scriptDef.Priority,
                        IsActive = true,
                        ModId = modId,
                        IsInitialized = false,
                    }
                );

                _logger.Debug(
                    "Attached script {ScriptId} to NPC {NpcId} via BehaviorDefinition {BehaviorId}",
                    scriptDef.Id,
                    npcDef.NpcId,
                    npcDef.BehaviorId
                );
            }

            // Create entity with all components at once (matching PlayerSystem pattern)
            // Use World.Create() with individual component arguments, not array
            // This matches how PlayerSystem creates entities and avoids Arch.Core array issues
            Entity npcEntity;
            if (components.Count == 7) // Has ScriptAttachmentComponent
            {
                npcEntity = World.Create(
                    npcComponent,
                    (Components.SpriteAnimationComponent)components[1],
                    (Components.PositionComponent)components[2],
                    (Components.RenderableComponent)components[3],
                    (Components.GridMovement)components[4],
                    (Components.ActiveMapEntity)components[5],
                    (Components.ScriptAttachmentComponent)components[6]
                );
            }
            else // No ScriptAttachmentComponent
            {
                npcEntity = World.Create(
                    npcComponent,
                    (Components.SpriteAnimationComponent)components[1],
                    (Components.PositionComponent)components[2],
                    (Components.RenderableComponent)components[3],
                    (Components.GridMovement)components[4],
                    (Components.ActiveMapEntity)components[5]
                );
            }

            // Add EntityVariablesComponent AFTER entity creation (if we have merged parameters)
            // This avoids issues with Arch.Core and struct components containing reference types
            if (
                mergedParameters != null
                && mergedParameters.Count > 0
                && scriptDefForVariables != null
            )
            {
                var variablesComponent = new Components.EntityVariablesComponent
                {
                    Variables = new Dictionary<string, string>(),
                    VariableTypes = new Dictionary<string, string>(),
                };

                foreach (var kvp in mergedParameters)
                {
                    var key = ScriptStateKeys.GetParameterKey(scriptDefForVariables.Id, kvp.Key);
                    variablesComponent.Variables[key] = kvp.Value?.ToString() ?? string.Empty;
                    variablesComponent.VariableTypes[key] =
                        scriptDefForVariables
                            .Parameters?.FirstOrDefault(p => p.Name == kvp.Key)
                            ?.Type
                        ?? "string";
                }

                World.Add(npcEntity, variablesComponent);
            }

            // Preload sprite texture
            _spriteLoader.GetSpriteTexture(actualSpriteId);

            // Fire NpcLoadedEvent
            var loadedEvent = new Events.NpcLoadedEvent
            {
                NpcEntity = npcEntity,
                NpcId = npcDef.NpcId,
                MapId = mapDefinition.Id,
            };
            EventBus.Send(ref loadedEvent);

            return npcEntity;
        }

        /// <summary>
        /// Merges script parameters from ScriptDefinition defaults, BehaviorDefinition overrides, and NPCDefinition overrides.
        /// </summary>
        private Dictionary<string, object> MergeScriptParameters(
            ScriptDefinition scriptDef,
            BehaviorDefinition behaviorDef,
            Maps.NpcDefinition npcDef
        )
        {
            // Step 1: Start with ScriptDefinition defaults
            var mergedParameters = ScriptParameterResolver.GetDefaults(scriptDef);

            // Step 2: Apply BehaviorDefinition.parameterOverrides
            if (behaviorDef.ParameterOverrides != null)
            {
                var behaviorOverrides = new Dictionary<string, object>();
                foreach (var kvp in behaviorDef.ParameterOverrides)
                {
                    behaviorOverrides[kvp.Key] = kvp.Value;
                }
                ScriptParameterResolver.ApplyOverrides(
                    mergedParameters,
                    behaviorOverrides,
                    scriptDef
                );
            }

            // Step 3: Apply NPCDefinition.behaviorParameters
            if (npcDef.BehaviorParameters != null)
            {
                ScriptParameterResolver.ApplyOverrides(
                    mergedParameters,
                    npcDef.BehaviorParameters,
                    scriptDef
                );
            }

            // Step 4: Validate merged parameters against ScriptDefinition
            ScriptParameterResolver.ValidateParameters(mergedParameters, scriptDef);

            return mergedParameters;
        }

        /// <summary>
        /// Validates merged parameters against ScriptDefinition (min/max bounds, etc.).
        /// Throws exceptions for invalid parameters (fail fast).
        /// </summary>
        private void ValidateMergedParameters(
            Dictionary<string, object> mergedParameters,
            Mods.Definitions.ScriptDefinition scriptDef
        )
        {
            if (scriptDef.Parameters == null)
            {
                return;
            }

            foreach (var paramDef in scriptDef.Parameters)
            {
                if (mergedParameters.TryGetValue(paramDef.Name, out var value))
                {
                    // Validate min/max bounds
                    if (paramDef.Min != null || paramDef.Max != null)
                    {
                        double? numericValue = value switch
                        {
                            int i => (double)i,
                            float f => (double)f,
                            double d => d,
                            _ => (double?)null,
                        };

                        if (numericValue != null)
                        {
                            if (paramDef.Min != null && numericValue < paramDef.Min)
                            {
                                throw new ArgumentException(
                                    $"Parameter '{paramDef.Name}' value '{value}' is below minimum '{paramDef.Min}' for script '{scriptDef.Id}'. "
                                        + $"Value must be >= {paramDef.Min}.",
                                    nameof(mergedParameters)
                                );
                            }

                            if (paramDef.Max != null && numericValue > paramDef.Max)
                            {
                                throw new ArgumentException(
                                    $"Parameter '{paramDef.Name}' value '{value}' is above maximum '{paramDef.Max}' for script '{scriptDef.Id}'. "
                                        + $"Value must be <= {paramDef.Max}.",
                                    nameof(mergedParameters)
                                );
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Creates a MapBorderComponent from map definition border data.
        /// Validates border data, gets tileset definition, calculates source rectangles, and creates component.
        /// </summary>
        /// <param name="mapDefinition">The map definition containing border data.</param>
        /// <returns>The created MapBorderComponent, or component with HasBorder=false if validation fails.</returns>
        private MapBorderComponent CreateMapBorderComponent(MapDefinition mapDefinition)
        {
            var border = mapDefinition.Border;
            if (border == null)
            {
                return new MapBorderComponent
                {
                    BottomLayerGids = Array.Empty<int>(),
                    TopLayerGids = Array.Empty<int>(),
                    TilesetId = string.Empty,
                    BottomSourceRects = Array.Empty<Rectangle>(),
                    TopSourceRects = Array.Empty<Rectangle>(),
                };
            }

            // Validate bottom layer has exactly 4 elements
            if (border.BottomLayer == null || border.BottomLayer.Count != 4)
            {
                _logger.Warning(
                    "Map {MapId} has invalid border bottom layer (expected 4 elements, got {Count})",
                    mapDefinition.Id,
                    border.BottomLayer?.Count ?? 0
                );
                return new MapBorderComponent
                {
                    BottomLayerGids = Array.Empty<int>(),
                    TopLayerGids = Array.Empty<int>(),
                    TilesetId = string.Empty,
                    BottomSourceRects = Array.Empty<Rectangle>(),
                    TopSourceRects = Array.Empty<Rectangle>(),
                };
            }

            // Validate tileset ID
            if (string.IsNullOrEmpty(border.TilesetId))
            {
                _logger.Warning("Map {MapId} has border data but no tileset ID", mapDefinition.Id);
                return new MapBorderComponent
                {
                    BottomLayerGids = Array.Empty<int>(),
                    TopLayerGids = Array.Empty<int>(),
                    TilesetId = string.Empty,
                    BottomSourceRects = Array.Empty<Rectangle>(),
                    TopSourceRects = Array.Empty<Rectangle>(),
                };
            }

            // Get tileset definition
            if (_tilesetLoader == null)
            {
                _logger.Warning(
                    "TilesetLoader is null, cannot create border component for map {MapId}",
                    mapDefinition.Id
                );
                return new MapBorderComponent
                {
                    BottomLayerGids = Array.Empty<int>(),
                    TopLayerGids = Array.Empty<int>(),
                    TilesetId = string.Empty,
                    BottomSourceRects = Array.Empty<Rectangle>(),
                    TopSourceRects = Array.Empty<Rectangle>(),
                };
            }

            var tilesetDefinition = _tilesetLoader.GetTilesetDefinition(border.TilesetId);
            if (tilesetDefinition == null)
            {
                _logger.Warning(
                    "Tileset {TilesetId} not found for border in map {MapId}",
                    border.TilesetId,
                    mapDefinition.Id
                );
                return new MapBorderComponent
                {
                    BottomLayerGids = Array.Empty<int>(),
                    TopLayerGids = Array.Empty<int>(),
                    TilesetId = string.Empty,
                    BottomSourceRects = Array.Empty<Rectangle>(),
                    TopSourceRects = Array.Empty<Rectangle>(),
                };
            }

            // Find tileset reference to get firstGid
            int firstGid = 1;
            if (mapDefinition.TilesetRefs != null)
            {
                var tilesetRef = mapDefinition.TilesetRefs.FirstOrDefault(tr =>
                    tr.TilesetId == border.TilesetId
                );
                if (tilesetRef != null)
                {
                    firstGid = tilesetRef.FirstGid;
                }
                else
                {
                    _logger.Warning(
                        "Tileset reference not found for {TilesetId} in map {MapId}, using firstGid=1",
                        border.TilesetId,
                        mapDefinition.Id
                    );
                }
            }

            // Pre-calculate source rectangles for bottom layer
            var bottomSourceRects = new Rectangle[4];
            for (int i = 0; i < 4; i++)
            {
                int localTileId = border.BottomLayer[i];
                if (localTileId > 0)
                {
                    // Convert local tile ID to global GID
                    int globalGid = localTileId + firstGid - 1;
                    var sourceRect = _tilesetLoader.CalculateSourceRectangle(
                        border.TilesetId,
                        globalGid,
                        firstGid
                    );
                    bottomSourceRects[i] = sourceRect ?? Rectangle.Empty;
                }
                else
                {
                    bottomSourceRects[i] = Rectangle.Empty;
                }
            }

            // Pre-calculate source rectangles for top layer
            var topSourceRects = new Rectangle[4];
            if (border.TopLayer != null && border.TopLayer.Count == 4)
            {
                for (int i = 0; i < 4; i++)
                {
                    int localTileId = border.TopLayer[i];
                    if (localTileId > 0)
                    {
                        // Convert local tile ID to global GID
                        int globalGid = localTileId + firstGid - 1;
                        var sourceRect = _tilesetLoader.CalculateSourceRectangle(
                            border.TilesetId,
                            globalGid,
                            firstGid
                        );
                        topSourceRects[i] = sourceRect ?? Rectangle.Empty;
                    }
                    else
                    {
                        topSourceRects[i] = Rectangle.Empty;
                    }
                }
            }
            else
            {
                // No top layer or invalid top layer - all empty
                for (int i = 0; i < 4; i++)
                {
                    topSourceRects[i] = Rectangle.Empty;
                }
            }

            return new MapBorderComponent
            {
                BottomLayerGids = border.BottomLayer.ToArray(),
                TopLayerGids = border.TopLayer?.ToArray() ?? Array.Empty<int>(),
                TilesetId = border.TilesetId,
                BottomSourceRects = bottomSourceRects,
                TopSourceRects = topSourceRects,
            };
        }
    }
}
