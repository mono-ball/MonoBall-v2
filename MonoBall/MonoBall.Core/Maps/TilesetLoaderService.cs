using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoBall.Core.Mods;
using MonoBall.Core.Mods.Utilities;
using Serilog;

namespace MonoBall.Core.Maps
{
    /// <summary>
    /// Service for loading and caching tileset textures from the file system.
    /// </summary>
    public class TilesetLoaderService : ITilesetLoaderService
    {
        private readonly GraphicsDevice _graphicsDevice;
        private readonly IModManager _modManager;
        private readonly ILogger _logger;
        private readonly Dictionary<string, Texture2D> _textureCache =
            new Dictionary<string, Texture2D>();
        private readonly Dictionary<string, TilesetDefinition> _definitionCache =
            new Dictionary<string, TilesetDefinition>();
        private readonly Dictionary<
            (string tilesetId, int localTileId),
            IReadOnlyList<TileAnimationFrame>
        > _animationCache = new Dictionary<(string, int), IReadOnlyList<TileAnimationFrame>>();

        /// <summary>
        /// Initializes a new instance of the TilesetLoaderService.
        /// </summary>
        /// <param name="graphicsDevice">The graphics device for loading textures.</param>
        /// <param name="modManager">The mod manager for accessing definitions.</param>
        /// <param name="logger">The logger for logging operations.</param>
        public TilesetLoaderService(
            GraphicsDevice graphicsDevice,
            IModManager modManager,
            ILogger logger
        )
        {
            _graphicsDevice =
                graphicsDevice ?? throw new ArgumentNullException(nameof(graphicsDevice));
            _modManager = modManager ?? throw new ArgumentNullException(nameof(modManager));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Gets a tileset definition by ID.
        /// </summary>
        /// <param name="tilesetId">The tileset ID.</param>
        /// <returns>The tileset definition, or null if not found.</returns>
        public TilesetDefinition? GetTilesetDefinition(string tilesetId)
        {
            if (string.IsNullOrEmpty(tilesetId))
            {
                return null;
            }

            // Check cache first
            if (_definitionCache.TryGetValue(tilesetId, out var cached))
            {
                return cached;
            }

            // Load from registry
            var definition = _modManager.GetDefinition<TilesetDefinition>(tilesetId);
            if (definition != null)
            {
                _definitionCache[tilesetId] = definition;
            }

            return definition;
        }

        /// <summary>
        /// Loads and caches a tileset texture. If already loaded, returns the cached texture.
        /// </summary>
        /// <param name="tilesetId">The tileset ID to load.</param>
        /// <returns>The loaded texture, or null if loading failed.</returns>
        public Texture2D? LoadTileset(string tilesetId)
        {
            if (string.IsNullOrEmpty(tilesetId))
            {
                _logger.Warning("Attempted to load tileset with null or empty ID");
                return null;
            }

            // Check cache first
            if (_textureCache.TryGetValue(tilesetId, out var cachedTexture))
            {
                // Cache hit - no logging needed (this happens every frame during rendering)
                return cachedTexture;
            }

            _logger.Debug("Loading tileset {TilesetId}", tilesetId);

            // Get tileset definition
            var definition = GetTilesetDefinition(tilesetId);
            if (definition == null)
            {
                _logger.Warning("Tileset definition not found: {TilesetId}", tilesetId);
                return null;
            }

            _logger.Debug(
                "Found tileset definition for {TilesetId} (texturePath: {TexturePath})",
                tilesetId,
                definition.TexturePath
            );

            // Get mod directory
            var metadata = _modManager.GetDefinitionMetadata(tilesetId);
            if (metadata == null)
            {
                _logger.Warning("Tileset metadata not found: {TilesetId}", tilesetId);
                return null;
            }

            _logger.Debug(
                "Found metadata for {TilesetId} (originalModId: {ModId})",
                tilesetId,
                metadata.OriginalModId
            );

            // Find mod manifest
            ModManifest? modManifest = null;
            foreach (var mod in _modManager.LoadedMods)
            {
                if (mod.Id == metadata.OriginalModId)
                {
                    modManifest = mod;
                    break;
                }
            }

            if (modManifest == null)
            {
                _logger.Warning(
                    "Mod manifest not found for tileset {TilesetId} (mod: {ModId})",
                    tilesetId,
                    metadata.OriginalModId
                );
                _logger.Debug(
                    "Available mods: {Mods}",
                    string.Join(", ", _modManager.LoadedMods.Select(m => m.Id))
                );
                return null;
            }

            _logger.Debug(
                "Found mod manifest for {ModId} (modDirectory: {ModDirectory})",
                modManifest.Id,
                modManifest.ModDirectory
            );

            // Resolve texture path
            string texturePath = Path.Combine(modManifest.ModDirectory, definition.TexturePath);
            texturePath = Path.GetFullPath(texturePath);

            _logger.Debug("Resolved texture path: {TexturePath}", texturePath);

            if (!File.Exists(texturePath))
            {
                _logger.Warning(
                    "Tileset texture file not found: {TexturePath} (tileset: {TilesetId})",
                    texturePath,
                    tilesetId
                );
                _logger.Debug(
                    "Mod directory exists: {Exists}, TexturePath from definition: {DefPath}",
                    Directory.Exists(modManifest.ModDirectory),
                    definition.TexturePath
                );
                return null;
            }

            try
            {
                // Load texture from file system
                var texture = Texture2D.FromFile(_graphicsDevice, texturePath);
                _textureCache[tilesetId] = texture;
                _logger.Debug(
                    "Loaded tileset texture: {TilesetId} from {TexturePath}",
                    tilesetId,
                    texturePath
                );
                return texture;
            }
            catch (Exception ex)
            {
                _logger.Error(
                    ex,
                    "Failed to load tileset texture: {TilesetId} from {TexturePath}",
                    tilesetId,
                    texturePath
                );
                return null;
            }
        }

        /// <summary>
        /// Gets a tileset texture, loading it if not already cached.
        /// </summary>
        /// <param name="tilesetId">The tileset ID.</param>
        /// <returns>The tileset texture, or null if not found or loading failed.</returns>
        public Texture2D? GetTilesetTexture(string tilesetId)
        {
            return LoadTileset(tilesetId);
        }

        /// <summary>
        /// Calculates the source rectangle for a tile based on its GID (Global ID).
        /// </summary>
        /// <param name="tilesetId">The tileset ID.</param>
        /// <param name="gid">The Global ID of the tile.</param>
        /// <param name="firstGid">The first GID for this tileset.</param>
        /// <returns>The source rectangle, or null if invalid.</returns>
        public Rectangle? CalculateSourceRectangle(string tilesetId, int gid, int firstGid)
        {
            if (string.IsNullOrEmpty(tilesetId))
            {
                return null;
            }

            // Get tileset definition
            var definition = GetTilesetDefinition(tilesetId);
            if (definition == null)
            {
                return null;
            }

            // Calculate local tile ID
            int localTileId = gid - firstGid;

            // Validate local tile ID
            if (localTileId < 0 || localTileId >= definition.TileCount)
            {
                return null;
            }

            // Calculate tile position in tileset
            int column = localTileId % definition.Columns;
            int row = localTileId / definition.Columns;

            // Calculate source rectangle accounting for spacing and margin
            int sourceX = column * (definition.TileWidth + definition.Spacing) + definition.Margin;
            int sourceY = row * (definition.TileHeight + definition.Spacing) + definition.Margin;

            // Validate source rectangle is within image bounds
            if (
                sourceX + definition.TileWidth > definition.ImageWidth
                || sourceY + definition.TileHeight > definition.ImageHeight
            )
            {
                return null;
            }

            return new Rectangle(sourceX, sourceY, definition.TileWidth, definition.TileHeight);
        }

        /// <summary>
        /// Unloads a tileset texture from the cache.
        /// </summary>
        /// <param name="tilesetId">The tileset ID to unload.</param>
        public void UnloadTileset(string tilesetId)
        {
            if (string.IsNullOrEmpty(tilesetId))
            {
                return;
            }

            if (_textureCache.TryGetValue(tilesetId, out var texture))
            {
                texture.Dispose();
                _textureCache.Remove(tilesetId);
                _logger.Debug("Unloaded tileset texture: {TilesetId}", tilesetId);
            }
        }

        /// <summary>
        /// Unloads all tileset textures from the cache.
        /// </summary>
        public void UnloadAll()
        {
            foreach (var kvp in _textureCache)
            {
                kvp.Value.Dispose();
            }
            _textureCache.Clear();
            _definitionCache.Clear();
            _animationCache.Clear();
            _logger.Debug("Unloaded all tileset textures");
        }

        /// <summary>
        /// Gets animation frames for a specific tile, loading and caching if not already cached.
        /// </summary>
        /// <param name="tilesetId">The tileset ID.</param>
        /// <param name="localTileId">The local tile ID within the tileset.</param>
        /// <returns>The animation frames as a readonly list, or null if the tile has no animation or is not found.</returns>
        public IReadOnlyList<TileAnimationFrame>? GetTileAnimation(
            string tilesetId,
            int localTileId
        )
        {
            if (string.IsNullOrEmpty(tilesetId))
            {
                return null;
            }

            var cacheKey = (tilesetId, localTileId);

            // Check cache first
            if (_animationCache.TryGetValue(cacheKey, out var cached))
            {
                return cached;
            }

            // Get tileset definition
            var definition = GetTilesetDefinition(tilesetId);
            if (definition == null)
            {
                return null;
            }

            // Search for the tile in the tiles list
            if (definition.Tiles != null)
            {
                foreach (var tile in definition.Tiles)
                {
                    if (
                        tile.LocalTileId == localTileId
                        && tile.Animation != null
                        && tile.Animation.Count > 0
                    )
                    {
                        // Cache and return as readonly list
                        var readonlyFrames = tile.Animation.AsReadOnly();
                        _animationCache[cacheKey] = readonlyFrames;
                        return readonlyFrames;
                    }
                }
            }

            // No animation found for this tile
            return null;
        }

        /// <summary>
        /// Gets cached animation frames for a specific tile (fast lookup, no definition loading).
        /// </summary>
        /// <param name="tilesetId">The tileset ID.</param>
        /// <param name="localTileId">The local tile ID within the tileset.</param>
        /// <returns>The cached animation frames as a readonly list, or null if not cached.</returns>
        public IReadOnlyList<TileAnimationFrame>? GetCachedAnimation(
            string tilesetId,
            int localTileId
        )
        {
            if (string.IsNullOrEmpty(tilesetId))
            {
                return null;
            }

            var cacheKey = (tilesetId, localTileId);
            if (_animationCache.TryGetValue(cacheKey, out var cached))
            {
                return cached;
            }

            return null;
        }
    }
}
