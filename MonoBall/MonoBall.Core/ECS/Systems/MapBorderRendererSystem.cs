using System;
using System.Collections.Generic;
using Arch.Core;
using Arch.System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoBall.Core.ECS.Components;
using MonoBall.Core.ECS.Services;
using MonoBall.Core.Maps;
using Serilog;

namespace MonoBall.Core.ECS.Systems
{
    /// <summary>
    /// System responsible for rendering map borders when the camera extends beyond map bounds.
    /// Uses a 2x2 tiling pattern (Pokemon Emerald style) for infinite border rendering.
    /// </summary>
    public class MapBorderRendererSystem : BaseSystem<World, float>
    {
        private readonly GraphicsDevice _graphicsDevice;
        private readonly ITilesetLoaderService _tilesetLoader;
        private readonly ICameraService _cameraService;
        private readonly IActiveMapFilterService _activeMapFilterService;
        private readonly ILogger _logger;
        private SpriteBatch? _spriteBatch;
        private Viewport _savedViewport;
        private readonly QueryDescription _mapBorderQuery;
        private PerformanceStatsSystem? _performanceStatsSystem;

        /// <summary>
        /// Helper structure for caching map border data.
        /// </summary>
        private struct MapBorderInfo
        {
            /// <summary>
            /// The map ID.
            /// </summary>
            public string MapId;

            /// <summary>
            /// The map origin X coordinate in tile space.
            /// </summary>
            public int MapOriginTileX;

            /// <summary>
            /// The map origin Y coordinate in tile space.
            /// </summary>
            public int MapOriginTileY;

            /// <summary>
            /// The right edge of the map in tile space.
            /// </summary>
            public int MapRightTile;

            /// <summary>
            /// The bottom edge of the map in tile space.
            /// </summary>
            public int MapBottomTile;

            /// <summary>
            /// The tile width in pixels.
            /// </summary>
            public int TileWidth;

            /// <summary>
            /// The tile height in pixels.
            /// </summary>
            public int TileHeight;

            /// <summary>
            /// The border component for this map.
            /// </summary>
            public MapBorderComponent Border;
        }

        /// <summary>
        /// Helper structure for caching map bounds data.
        /// </summary>
        private struct MapBoundsInfo
        {
            /// <summary>
            /// The left edge of the map in tile space.
            /// </summary>
            public int TileX;

            /// <summary>
            /// The top edge of the map in tile space.
            /// </summary>
            public int TileY;

            /// <summary>
            /// The right edge of the map in tile space.
            /// </summary>
            public int TileRight;

            /// <summary>
            /// The bottom edge of the map in tile space.
            /// </summary>
            public int TileBottom;
        }

        // Cached data (recalculated each frame, reused between Render() and RenderTopLayer())
        private readonly List<MapBorderInfo> _cachedMapBorders = new List<MapBorderInfo>();
        private readonly List<MapBoundsInfo> _cachedMapBounds = new List<MapBoundsInfo>();
        private string? _cachedPlayerMapId;
        private Rectangle? _cachedCameraBounds;
        private int _lastMapEntityCount = -1;

        /// <summary>
        /// Initializes a new instance of the MapBorderRendererSystem.
        /// </summary>
        /// <param name="world">The ECS world.</param>
        /// <param name="graphicsDevice">The graphics device.</param>
        /// <param name="tilesetLoader">The tileset loader service.</param>
        /// <param name="cameraService">The camera service.</param>
        /// <param name="activeMapFilterService">The active map filter service.</param>
        /// <param name="logger">The logger for logging operations.</param>
        public MapBorderRendererSystem(
            World world,
            GraphicsDevice graphicsDevice,
            ITilesetLoaderService tilesetLoader,
            ICameraService cameraService,
            IActiveMapFilterService activeMapFilterService,
            ILogger logger
        )
            : base(world)
        {
            _graphicsDevice =
                graphicsDevice ?? throw new ArgumentNullException(nameof(graphicsDevice));
            _tilesetLoader =
                tilesetLoader ?? throw new ArgumentNullException(nameof(tilesetLoader));
            _cameraService =
                cameraService ?? throw new ArgumentNullException(nameof(cameraService));
            _activeMapFilterService =
                activeMapFilterService
                ?? throw new ArgumentNullException(nameof(activeMapFilterService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _mapBorderQuery = new QueryDescription().WithAll<
                MapComponent,
                PositionComponent,
                MapBorderComponent
            >();
        }

        /// <summary>
        /// Sets the SpriteBatch instance to use for rendering.
        /// </summary>
        /// <param name="spriteBatch">The SpriteBatch instance.</param>
        public void SetSpriteBatch(SpriteBatch spriteBatch)
        {
            _spriteBatch = spriteBatch ?? throw new ArgumentNullException(nameof(spriteBatch));
        }

        /// <summary>
        /// Sets the PerformanceStatsSystem instance for tracking draw calls.
        /// </summary>
        /// <param name="performanceStatsSystem">The PerformanceStatsSystem instance.</param>
        public void SetPerformanceStatsSystem(PerformanceStatsSystem performanceStatsSystem)
        {
            _performanceStatsSystem =
                performanceStatsSystem
                ?? throw new ArgumentNullException(nameof(performanceStatsSystem));
        }

        /// <summary>
        /// Renders the bottom layer of border tiles when camera extends beyond map bounds.
        /// </summary>
        /// <param name="gameTime">The game time.</param>
        public void Render(GameTime gameTime)
        {
            RenderBorderLayer(gameTime, useTopLayer: false);
        }

        /// <summary>
        /// Renders the top layer of border tiles when camera extends beyond map bounds.
        /// Called after SpriteRendererSystem to render borders on top of sprites.
        /// </summary>
        /// <param name="gameTime">The game time.</param>
        public void RenderTopLayer(GameTime gameTime)
        {
            RenderBorderLayer(gameTime, useTopLayer: true);
        }

        /// <summary>
        /// Shared rendering logic for both bottom and top border layers.
        /// </summary>
        /// <param name="gameTime">The game time.</param>
        /// <param name="useTopLayer">If true, renders top layer; otherwise renders bottom layer.</param>
        private void RenderBorderLayer(GameTime gameTime, bool useTopLayer)
        {
            if (_spriteBatch == null)
            {
                _logger.Warning(
                    "MapBorderRendererSystem.RenderBorderLayer called but SpriteBatch is null"
                );
                return;
            }

            // Get active camera
            CameraComponent? activeCamera = _cameraService.GetActiveCamera();
            if (!activeCamera.HasValue)
            {
                return;
            }

            var camera = activeCamera.Value;

            // Cache phase: update cached data if needed (reuse between Render() and RenderTopLayer())
            UpdateCachedData(camera);

            // Get player map ID
            string? playerMapId = _cachedPlayerMapId;
            if (playerMapId == null)
            {
                return; // No player map - no borders to render
            }

            // Find player's map border
            MapBorderInfo? playerMapBorder = null;
            foreach (var borderInfo in _cachedMapBorders)
            {
                if (borderInfo.MapId == playerMapId)
                {
                    playerMapBorder = borderInfo;
                    break;
                }
            }

            if (!playerMapBorder.HasValue)
            {
                return; // Player's map has no border
            }

            var border = playerMapBorder.Value;

            // For top layer, check if border has top layer data
            if (useTopLayer && !border.Border.HasTopLayer)
            {
                return; // No top layer to render
            }

            // Validate border component arrays
            Rectangle[] sourceRects = useTopLayer
                ? border.Border.TopSourceRects
                : border.Border.BottomSourceRects;

            if (sourceRects == null || sourceRects.Length != 4)
            {
                _logger.Warning(
                    "Map {MapId} has invalid border source rectangles array",
                    border.MapId
                );
                return;
            }

            // Get camera bounds (use cached if available)
            Rectangle expandedTileBounds = _cachedCameraBounds ?? camera.GetTileViewBounds();
            if (!_cachedCameraBounds.HasValue)
            {
                // Expand bounds by 1 tile margin
                expandedTileBounds = new Rectangle(
                    expandedTileBounds.X - 1,
                    expandedTileBounds.Y - 1,
                    expandedTileBounds.Width + 2,
                    expandedTileBounds.Height + 2
                );
                _cachedCameraBounds = expandedTileBounds;
            }

            // Check if camera extends beyond map bounds
            bool cameraExtendsBeyondBounds =
                expandedTileBounds.X < border.MapOriginTileX
                || expandedTileBounds.Y < border.MapOriginTileY
                || expandedTileBounds.Right > border.MapRightTile
                || expandedTileBounds.Bottom > border.MapBottomTile;

            if (!cameraExtendsBeyondBounds)
            {
                return; // Camera entirely within bounds - no borders to render
            }

            // Get tileset texture
            Texture2D? tilesetTexture = _tilesetLoader.GetTilesetTexture(border.Border.TilesetId);
            if (tilesetTexture == null)
            {
                _logger.Warning(
                    "Tileset texture not found for border: {TilesetId}",
                    border.Border.TilesetId
                );
                return;
            }

            // Begin SpriteBatch
            _savedViewport = _graphicsDevice.Viewport;

            try
            {
                // Set viewport to camera.VirtualViewport if not empty
                if (camera.VirtualViewport != Rectangle.Empty)
                {
                    _graphicsDevice.Viewport = new Viewport(camera.VirtualViewport);
                }

                // Get camera transform matrix
                Matrix transform = camera.GetTransformMatrix();

                // Begin SpriteBatch with transform
                _spriteBatch.Begin(
                    SpriteSortMode.Deferred,
                    BlendState.AlphaBlend,
                    SamplerState.PointClamp,
                    DepthStencilState.None,
                    RasterizerState.CullCounterClockwise,
                    null,
                    transform
                );

                // Render border tiles
                for (int tileY = expandedTileBounds.Y; tileY < expandedTileBounds.Bottom; tileY++)
                {
                    for (
                        int tileX = expandedTileBounds.X;
                        tileX < expandedTileBounds.Right;
                        tileX++
                    )
                    {
                        // Check if this tile is outside all map bounds
                        if (IsTileInsideAnyMap(tileX, tileY))
                        {
                            continue; // Inside a map - don't render border
                        }

                        // Calculate relative position to map origin
                        int relativeX = tileX - border.MapOriginTileX;
                        int relativeY = tileY - border.MapOriginTileY;

                        // Get border tile index using 2x2 tiling pattern
                        int borderTileIndex = MapBorderComponent.GetBorderTileIndex(
                            relativeX,
                            relativeY
                        );

                        // Validate index bounds
                        if (borderTileIndex < 0 || borderTileIndex >= sourceRects.Length)
                        {
                            continue;
                        }

                        // Get source rectangle from appropriate layer
                        Rectangle sourceRect = sourceRects[borderTileIndex];
                        if (sourceRect == Rectangle.Empty)
                        {
                            continue; // No tile for this position
                        }

                        // Calculate pixel position (use separate TileWidth and TileHeight for non-square tiles)
                        Vector2 tilePixelPosition = new Vector2(
                            tileX * border.TileWidth,
                            tileY * border.TileHeight
                        );

                        // Draw the tile
                        _spriteBatch.Draw(
                            tilesetTexture,
                            tilePixelPosition,
                            sourceRect,
                            Color.White
                        );
                    }
                }

                _spriteBatch.End();

                // Increment draw calls if PerformanceStatsSystem available
                _performanceStatsSystem?.IncrementDrawCalls();
            }
            finally
            {
                // Always restore viewport, even if rendering fails
                _graphicsDevice.Viewport = _savedViewport;
            }
        }

        /// <summary>
        /// Updates cached map border and bounds data. Only recalculates if map entity count changed.
        /// </summary>
        /// <param name="camera">The active camera component.</param>
        private void UpdateCachedData(CameraComponent camera)
        {
            // Check if we need to recalculate (map entity count changed)
            int currentMapCount = World.CountEntities(in _mapBorderQuery);
            if (
                _cachedMapBorders.Count > 0
                && _lastMapEntityCount == currentMapCount
                && _cachedPlayerMapId != null
            )
            {
                // Cache is still valid, just update camera bounds
                Rectangle currentTileViewBounds = camera.GetTileViewBounds();
                _cachedCameraBounds = new Rectangle(
                    currentTileViewBounds.X - 1,
                    currentTileViewBounds.Y - 1,
                    currentTileViewBounds.Width + 2,
                    currentTileViewBounds.Height + 2
                );
                return;
            }

            // Recalculate cache
            _cachedMapBorders.Clear();
            _cachedMapBounds.Clear();
            _cachedCameraBounds = null;

            // Query all maps with MapBorderComponent, populate cached lists
            World.Query(
                in _mapBorderQuery,
                (
                    Entity entity,
                    ref MapComponent map,
                    ref PositionComponent mapPosition,
                    ref MapBorderComponent border
                ) =>
                {
                    if (!border.HasBorder)
                    {
                        return;
                    }

                    // Validate border component arrays
                    if (
                        border.BottomSourceRects == null
                        || border.BottomSourceRects.Length != 4
                        || border.TopSourceRects == null
                        || border.TopSourceRects.Length != 4
                    )
                    {
                        _logger.Warning(
                            "Map {MapId} has invalid border component arrays",
                            map.MapId
                        );
                        return;
                    }

                    // Calculate map bounds in tile coordinates
                    int mapOriginTileX = (int)(mapPosition.Position.X / map.TileWidth);
                    int mapOriginTileY = (int)(mapPosition.Position.Y / map.TileHeight);
                    int mapRightTile = mapOriginTileX + map.Width;
                    int mapBottomTile = mapOriginTileY + map.Height;

                    _cachedMapBorders.Add(
                        new MapBorderInfo
                        {
                            MapId = map.MapId,
                            MapOriginTileX = mapOriginTileX,
                            MapOriginTileY = mapOriginTileY,
                            MapRightTile = mapRightTile,
                            MapBottomTile = mapBottomTile,
                            TileWidth = map.TileWidth,
                            TileHeight = map.TileHeight,
                            Border = border,
                        }
                    );

                    _cachedMapBounds.Add(
                        new MapBoundsInfo
                        {
                            TileX = mapOriginTileX,
                            TileY = mapOriginTileY,
                            TileRight = mapRightTile,
                            TileBottom = mapBottomTile,
                        }
                    );
                }
            );

            // Cache player map ID
            _cachedPlayerMapId = _activeMapFilterService.GetPlayerCurrentMapId();

            // Cache camera bounds
            Rectangle newTileViewBounds = camera.GetTileViewBounds();
            _cachedCameraBounds = new Rectangle(
                newTileViewBounds.X - 1,
                newTileViewBounds.Y - 1,
                newTileViewBounds.Width + 2,
                newTileViewBounds.Height + 2
            );

            // Update map entity count for next frame comparison
            _lastMapEntityCount = currentMapCount;
        }

        /// <summary>
        /// Checks if a tile position is inside any map bounds.
        /// </summary>
        /// <param name="tileX">The tile X coordinate.</param>
        /// <param name="tileY">The tile Y coordinate.</param>
        /// <returns>True if the tile is inside any map, false otherwise.</returns>
        private bool IsTileInsideAnyMap(int tileX, int tileY)
        {
            foreach (var bounds in _cachedMapBounds)
            {
                if (
                    tileX >= bounds.TileX
                    && tileX < bounds.TileRight
                    && tileY >= bounds.TileY
                    && tileY < bounds.TileBottom
                )
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Update method required by BaseSystem, but rendering is done via Render() and RenderTopLayer().
        /// </summary>
        /// <param name="deltaTime">The elapsed time since last update.</param>
        public override void Update(in float deltaTime)
        {
            // Rendering is done via Render() and RenderTopLayer() methods called from Game.Draw()
            // This Update() method is a no-op for renderer system
        }
    }
}
