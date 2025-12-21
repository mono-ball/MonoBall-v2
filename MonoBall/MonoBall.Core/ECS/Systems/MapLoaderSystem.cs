using System;
using System.Collections.Generic;
using Arch.Core;
using Arch.System;
using Arch.System.SourceGenerator;
using Microsoft.Xna.Framework;
using MonoBall.Core;
using MonoBall.Core.ECS.Components;
using MonoBall.Core.ECS.Events;
using MonoBall.Core.Maps;
using MonoBall.Core.Maps.Utilities;
using MonoBall.Core.Mods;
using Serilog;

namespace MonoBall.Core.ECS.Systems
{
    /// <summary>
    /// System responsible for loading and unloading maps, creating tile chunks, and managing map connections.
    /// </summary>
    public partial class MapLoaderSystem : BaseSystem<World, float>
    {
        private const int ChunkSize = GameConstants.TileChunkSize; // 16x16 tiles per chunk
        private readonly DefinitionRegistry _registry;
        private readonly ITilesetLoaderService? _tilesetLoader;
        private readonly HashSet<string> _loadedMaps = new HashSet<string>();
        private readonly Dictionary<string, Entity> _mapEntities = new Dictionary<string, Entity>();
        private readonly Dictionary<string, List<Entity>> _mapChunkEntities =
            new Dictionary<string, List<Entity>>();
        private readonly Dictionary<string, List<Entity>> _mapConnectionEntities =
            new Dictionary<string, List<Entity>>();
        private readonly Dictionary<string, Vector2> _mapPositions =
            new Dictionary<string, Vector2>(); // Map positions in tile coordinates

        /// <summary>
        /// Initializes a new instance of the MapLoaderSystem.
        /// </summary>
        /// <param name="world">The ECS world.</param>
        /// <param name="registry">The definition registry.</param>
        /// <param name="tilesetLoader">Optional tileset loader service for preloading tilesets.</param>
        public MapLoaderSystem(
            World world,
            DefinitionRegistry registry,
            ITilesetLoaderService? tilesetLoader = null
        )
            : base(world)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _tilesetLoader = tilesetLoader;
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

            // Fire MapLoadedEvent
            var loadedEvent = new MapLoadedEvent { MapId = mapId, MapEntity = mapEntity };
            // Note: EventBus integration will be added when we integrate with game

            // Proactively load connected maps with proper positioning
            if (mapDefinition.Connections != null)
            {
                foreach (var kvp in mapDefinition.Connections)
                {
                    string directionStr = kvp.Key.ToLowerInvariant();
                    var connection = kvp.Value;

                    if (!_loadedMaps.Contains(connection.MapId))
                    {
                        // Calculate connected map position based on direction and offset
                        Vector2 connectedMapPosition = CalculateConnectedMapPosition(
                            mapTilePosition,
                            mapDefinition.Width,
                            mapDefinition.Height,
                            directionStr,
                            connection.Offset
                        );

                        Log.Debug(
                            "MapLoaderSystem.LoadMap: Loading connected map {ConnectedMapId} at tile position ({TileX}, {TileY}) via {Direction} connection",
                            connection.MapId,
                            connectedMapPosition.X,
                            connectedMapPosition.Y,
                            directionStr
                        );

                        LoadMap(connection.MapId, connectedMapPosition);
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

                // Destroy the map entity
                World.Destroy(mapEntity);
                _mapEntities.Remove(mapId);
            }

            _loadedMaps.Remove(mapId);
            _mapPositions.Remove(mapId); // Clean up position tracking

            // Fire MapUnloadedEvent
            var unloadedEvent = new MapUnloadedEvent { MapId = mapId };
            // Note: EventBus integration will be added when we integrate with game

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
        /// <param name="direction">The connection direction (north, south, east, west).</param>
        /// <param name="offset">The offset in tiles.</param>
        /// <returns>The calculated tile position for the connected map.</returns>
        private Vector2 CalculateConnectedMapPosition(
            Vector2 sourceMapPosition,
            int sourceMapWidth,
            int sourceMapHeight,
            string direction,
            int offset
        )
        {
            return direction.ToLowerInvariant() switch
            {
                "north" => new Vector2(
                    sourceMapPosition.X + offset,
                    sourceMapPosition.Y - sourceMapHeight
                ),
                "south" => new Vector2(
                    sourceMapPosition.X + offset,
                    sourceMapPosition.Y + sourceMapHeight
                ),
                "east" => new Vector2(
                    sourceMapPosition.X + sourceMapWidth,
                    sourceMapPosition.Y + offset
                ),
                "west" => new Vector2(
                    sourceMapPosition.X - sourceMapWidth,
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

                        // Calculate world position for chunk (relative to map's tile position)
                        // Convert chunk tile coordinates to pixel coordinates, offset by map position
                        Vector2 chunkPosition = new Vector2(
                            (mapTilePosition.X + chunkStartX) * mapDefinition.TileWidth,
                            (mapTilePosition.Y + chunkStartY) * mapDefinition.TileHeight
                        );

                        // Create chunk entity
                        var chunkEntity = World.Create(
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
    }
}
