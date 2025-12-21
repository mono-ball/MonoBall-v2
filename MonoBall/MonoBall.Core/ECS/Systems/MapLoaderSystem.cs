using System;
using System.Collections.Generic;
using System.Linq;
using Arch.Core;
using Arch.System;
using Microsoft.Xna.Framework;
using MonoBall.Core;
using MonoBall.Core.ECS;
using MonoBall.Core.ECS.Components;
using MonoBall.Core.ECS.Events;
using MonoBall.Core.ECS.Utilities;
using MonoBall.Core.Maps;
using MonoBall.Core.Maps.Utilities;
using MonoBall.Core.Mods;
using Serilog;

namespace MonoBall.Core.ECS.Systems
{
    /// <summary>
    /// System responsible for loading and unloading maps, creating tile chunks, and managing map connections.
    /// </summary>
    public class MapLoaderSystem : BaseSystem<World, float>
    {
        private const int ChunkSize = GameConstants.TileChunkSize; // 16x16 tiles per chunk
        private readonly DefinitionRegistry _registry;
        private readonly ITilesetLoaderService? _tilesetLoader;
        private readonly ISpriteLoaderService? _spriteLoader;
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
        /// Initializes a new instance of the MapLoaderSystem.
        /// </summary>
        /// <param name="world">The ECS world.</param>
        /// <param name="registry">The definition registry.</param>
        /// <param name="tilesetLoader">Optional tileset loader service for preloading tilesets.</param>
        /// <param name="spriteLoader">Optional sprite loader service for loading NPC sprites.</param>
        public MapLoaderSystem(
            World world,
            DefinitionRegistry registry,
            ITilesetLoaderService? tilesetLoader = null,
            ISpriteLoaderService? spriteLoader = null
        )
            : base(world)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _tilesetLoader = tilesetLoader;
            _spriteLoader = spriteLoader;
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
                Log.Warning("Attempted to load map with null or empty ID");
                return;
            }

            if (_loadedMaps.Contains(mapId))
            {
                Log.Debug("Map {MapId} is already loaded", mapId);
                return;
            }

            var mapDefinition = _registry.GetById<MapDefinition>(mapId);
            if (mapDefinition == null)
            {
                Log.Warning("Map definition not found: {MapId}", mapId);
                return;
            }

            Log.Information("Loading map: {MapId} ({Name})", mapId, mapDefinition.Name);

            // Determine map position (default to 0,0 for initial map, or use provided position)
            Vector2 mapTilePosition = tilePosition ?? Vector2.Zero;
            _mapPositions[mapId] = mapTilePosition;

            Log.Debug(
                "MapLoaderSystem.LoadMap: Positioning map {MapId} at tile position ({TileX}, {TileY})",
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

            // Create tile chunks for each layer (positioned relative to map position)
            int chunksCreated = CreateTileChunks(mapEntity, mapDefinition, mapTilePosition);
            Log.Information(
                "MapLoaderSystem.LoadMap: Created {ChunkCount} tile chunks for map {MapId}",
                chunksCreated,
                mapId
            );

            // Create connection entities
            CreateConnections(mapEntity, mapDefinition);

            // Create NPC entities
            int npcsCreated = CreateNpcs(mapEntity, mapDefinition, mapTilePosition);
            Log.Information(
                "MapLoaderSystem.LoadMap: Created {NpcCount} NPCs for map {MapId}",
                npcsCreated,
                mapId
            );

            // Fire MapLoadedEvent
            var loadedEvent = new MapLoadedEvent { MapId = mapId, MapEntity = mapEntity };
            EventBus.Send(ref loadedEvent);

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
                        Log.Warning(
                            "MapLoaderSystem.LoadMap: Target map definition not found for connection: {MapId}",
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
                        Log.Debug(
                            "MapLoaderSystem.LoadMap: Loading connected map {ConnectedMapId} at tile position ({TileX}, {TileY}) via {Direction} connection",
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
                                Log.Warning(
                                    "MapLoaderSystem.LoadMap: Position conflict for map {MapId}. Existing: ({ExistingX}, {ExistingY}), Expected: ({ExpectedX}, {ExpectedY}) via {Direction} connection from {SourceMapId}",
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

            Log.Information("Map loaded: {MapId}", mapId);
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

            Log.Information("Unloading map: {MapId}", mapId);

            if (_mapEntities.TryGetValue(mapId, out var mapEntity))
            {
                // Destroy all chunk entities
                if (_mapChunkEntities.TryGetValue(mapId, out var chunkEntities))
                {
                    foreach (var chunkEntity in chunkEntities)
                    {
                        World.Destroy(chunkEntity);
                    }
                    _mapChunkEntities.Remove(mapId);
                }

                // Destroy all connection entities
                if (_mapConnectionEntities.TryGetValue(mapId, out var connectionEntities))
                {
                    foreach (var connectionEntity in connectionEntities)
                    {
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
                        World.Destroy(npcEntity);
                    }
                    _mapNpcEntities.Remove(mapId);
                }

                // Destroy the map entity
                World.Destroy(mapEntity);
                _mapEntities.Remove(mapId);
            }

            _loadedMaps.Remove(mapId);
            _mapPositions.Remove(mapId); // Clean up position tracking

            // Fire MapUnloadedEvent
            var unloadedEvent = new MapUnloadedEvent { MapId = mapId };
            EventBus.Send(ref unloadedEvent);
            Log.Information("Map unloaded: {MapId}", mapId);
        }

        private void PreloadTilesets(MapDefinition mapDefinition)
        {
            if (_tilesetLoader == null)
            {
                Log.Warning(
                    "MapLoaderSystem.PreloadTilesets: TilesetLoader is null, cannot preload tilesets"
                );
                return;
            }

            if (mapDefinition.TilesetRefs == null || mapDefinition.TilesetRefs.Count == 0)
            {
                Log.Warning(
                    "MapLoaderSystem.PreloadTilesets: Map {MapId} has no tileset references",
                    mapDefinition.Id
                );
                return;
            }

            Log.Information(
                "MapLoaderSystem.PreloadTilesets: Preloading {Count} tileset(s) for map {MapId}",
                mapDefinition.TilesetRefs.Count,
                mapDefinition.Id
            );
            foreach (var tilesetRef in mapDefinition.TilesetRefs)
            {
                Log.Debug(
                    "MapLoaderSystem.PreloadTilesets: Loading tileset {TilesetId} (firstGid: {FirstGid})",
                    tilesetRef.TilesetId,
                    tilesetRef.FirstGid
                );
                var texture = _tilesetLoader.LoadTileset(tilesetRef.TilesetId);
                if (texture == null)
                {
                    Log.Warning(
                        "MapLoaderSystem.PreloadTilesets: Failed to load tileset {TilesetId}",
                        tilesetRef.TilesetId
                    );
                }
                else
                {
                    Log.Information(
                        "MapLoaderSystem.PreloadTilesets: Successfully loaded tileset {TilesetId}",
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
                Log.Warning(
                    "MapLoaderSystem.CreateTileChunks: Map {MapId} has no layers",
                    mapDefinition.Id
                );
                return 0;
            }

            Log.Debug(
                "MapLoaderSystem.CreateTileChunks: Creating chunks for {LayerCount} layers in map {MapId}",
                mapDefinition.Layers.Count,
                mapDefinition.Id
            );

            int totalChunks = 0;
            int layerIndex = 0;
            foreach (var layer in mapDefinition.Layers)
            {
                if (!layer.Visible)
                {
                    Log.Debug(
                        "MapLoaderSystem.CreateTileChunks: Skipping invisible layer {LayerId} (index: {Index})",
                        layer.LayerId,
                        layerIndex
                    );
                    layerIndex++;
                    continue;
                }

                if (string.IsNullOrEmpty(layer.TileData))
                {
                    Log.Warning(
                        "MapLoaderSystem.CreateTileChunks: Layer {LayerId} (index: {Index}) has no tile data",
                        layer.LayerId,
                        layerIndex
                    );
                    layerIndex++;
                    continue;
                }

                Log.Debug(
                    "MapLoaderSystem.CreateTileChunks: Processing layer {LayerId} (index: {Index}, size: {Width}x{Height})",
                    layer.LayerId,
                    layerIndex,
                    layer.Width,
                    layer.Height
                );

                // Decode tile data
                var tileIndices = TileDataDecoder.Decode(layer.TileData, layer.Width, layer.Height);
                if (tileIndices == null)
                {
                    Log.Warning(
                        "MapLoaderSystem.CreateTileChunks: Failed to decode tile data for layer {LayerId}",
                        layer.LayerId
                    );
                    layerIndex++;
                    continue;
                }

                Log.Debug(
                    "MapLoaderSystem.CreateTileChunks: Decoded {TileCount} tiles for layer {LayerId}",
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
                int chunksX = (int)Math.Ceiling((double)layer.Width / ChunkSize);
                int chunksY = (int)Math.Ceiling((double)layer.Height / ChunkSize);

                Log.Debug(
                    "MapLoaderSystem.CreateTileChunks: Creating {ChunksX}x{ChunksY} chunks for layer {LayerId}",
                    chunksX,
                    chunksY,
                    layer.LayerId
                );

                int layerChunks = 0;
                for (int chunkY = 0; chunkY < chunksY; chunkY++)
                {
                    for (int chunkX = 0; chunkX < chunksX; chunkX++)
                    {
                        int chunkStartX = chunkX * ChunkSize;
                        int chunkStartY = chunkY * ChunkSize;
                        int chunkWidth = Math.Min(ChunkSize, layer.Width - chunkStartX);
                        int chunkHeight = Math.Min(ChunkSize, layer.Height - chunkStartY);

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

                Log.Debug(
                    "MapLoaderSystem.CreateTileChunks: Created {ChunkCount} chunks for layer {LayerId}",
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
                Log.Warning(
                    "MapLoaderSystem.CreateNpcs: SpriteLoader is null, cannot create NPCs for map {MapId}",
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
                    Log.Error(
                        ex,
                        "MapLoaderSystem.CreateNpcs: Failed to create NPC {NpcId} for map {MapId}",
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

            // Validate sprite definition exists (strict validation - throw on invalid)
            SpriteValidationHelper.ValidateSpriteDefinition(
                _spriteLoader,
                npcDef.SpriteId,
                "NPC",
                npcDef.NpcId,
                throwOnInvalid: true
            );

            // Map direction to animation name
            string animationName = MapDirectionToAnimation(npcDef.Direction);

            // Validate animation exists
            // Note: NPCs use forgiving validation (logs warning, defaults to face_south) for resilience
            // This differs from Player creation which uses strict validation (throws on invalid)
            if (!_spriteLoader.ValidateAnimation(npcDef.SpriteId, animationName))
            {
                Log.Warning(
                    "MapLoaderSystem.CreateNpcEntity: Animation '{AnimationName}' not found for sprite {SpriteId} (NPC {NpcId}), defaulting to 'face_south'",
                    animationName,
                    npcDef.SpriteId,
                    npcDef.NpcId
                );
                animationName = "face_south";
            }

            // Get sprite definition and animation to determine initial flip state
            var spriteDefinition = _spriteLoader.GetSpriteDefinition(npcDef.SpriteId);
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

            // Create NPC entity with all required components
            var npcEntity = World.Create(
                new Components.NpcComponent
                {
                    NpcId = npcDef.NpcId,
                    Name = npcDef.Name,
                    SpriteId = npcDef.SpriteId,
                    MapId = mapDefinition.Id,
                    Elevation = npcDef.Elevation,
                    VisibilityFlag = npcDef.VisibilityFlag,
                },
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
                    IsVisible = true,
                    RenderOrder = npcDef.Elevation,
                    Opacity = 1.0f,
                }
            );

            // Preload sprite texture
            _spriteLoader.GetSpriteTexture(npcDef.SpriteId);

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
    }
}
