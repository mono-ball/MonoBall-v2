using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoBall.Core.Mods;
using Serilog;

namespace MonoBall.Core.Maps
{
    /// <summary>
    /// Service for loading and caching sprite definitions, textures, and animation frames.
    /// </summary>
    public class SpriteLoaderService : ISpriteLoaderService
    {
        /// <summary>
        /// Threshold for determining if frame duration is in milliseconds.
        /// Durations greater than this value are assumed to be in milliseconds and converted to seconds.
        /// This heuristic is used when the data format doesn't specify units.
        /// </summary>
        private const double MillisecondsThreshold = 100.0;

        private readonly GraphicsDevice _graphicsDevice;
        private readonly IModManager _modManager;
        private readonly ILogger _logger;
        private readonly Dictionary<string, Texture2D> _textureCache =
            new Dictionary<string, Texture2D>();
        private readonly Dictionary<string, SpriteDefinition> _definitionCache =
            new Dictionary<string, SpriteDefinition>();
        private readonly Dictionary<
            (string spriteId, string animationName),
            List<SpriteAnimationFrame>
        > _animationCache = new Dictionary<(string, string), List<SpriteAnimationFrame>>();
        private Texture2D? _placeholderTexture;

        /// <summary>
        /// Initializes a new instance of the SpriteLoaderService.
        /// </summary>
        /// <param name="graphicsDevice">The graphics device for loading textures.</param>
        /// <param name="modManager">The mod manager for accessing definitions.</param>
        /// <param name="logger">The logger for logging operations.</param>
        public SpriteLoaderService(
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
        /// Gets a sprite definition by ID.
        /// </summary>
        /// <param name="spriteId">The sprite ID.</param>
        /// <returns>The sprite definition, or null if not found.</returns>
        public SpriteDefinition? GetSpriteDefinition(string spriteId)
        {
            if (string.IsNullOrEmpty(spriteId))
            {
                return null;
            }

            // Check cache first
            if (_definitionCache.TryGetValue(spriteId, out var cached))
            {
                return cached;
            }

            // Load from registry
            var definition = _modManager.GetDefinition<SpriteDefinition>(spriteId);
            if (definition != null)
            {
                _definitionCache[spriteId] = definition;
                // Pre-compute animation frames when definition is loaded
                PrecomputeAnimationFrames(spriteId, definition);
            }

            return definition;
        }

        /// <summary>
        /// Gets a sprite texture, loading it if not already cached.
        /// Returns a placeholder texture if the sprite texture cannot be loaded.
        /// </summary>
        /// <param name="spriteId">The sprite ID.</param>
        /// <returns>The sprite texture, or a placeholder texture if not found or loading failed.</returns>
        public Texture2D? GetSpriteTexture(string spriteId)
        {
            if (string.IsNullOrEmpty(spriteId))
            {
                _logger.Warning("Attempted to load sprite texture with null or empty ID");
                return null;
            }

            // Check cache first
            if (_textureCache.TryGetValue(spriteId, out var cachedTexture))
            {
                // Cache hit - no logging needed (this happens every frame during rendering)
                return cachedTexture;
            }

            _logger.Debug("Loading sprite texture {SpriteId}", spriteId);

            // Get sprite definition
            var definition = GetSpriteDefinition(spriteId);
            if (definition == null)
            {
                _logger.Warning("Sprite definition not found: {SpriteId}", spriteId);
                return null;
            }

            _logger.Debug(
                "Found sprite definition for {SpriteId} (texturePath: {TexturePath})",
                spriteId,
                definition.TexturePath
            );

            // Get mod directory
            var metadata = _modManager.GetDefinitionMetadata(spriteId);
            if (metadata == null)
            {
                _logger.Warning("Sprite metadata not found: {SpriteId}", spriteId);
                return null;
            }

            _logger.Debug(
                "Found metadata for {SpriteId} (originalModId: {ModId})",
                spriteId,
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
                    "Mod manifest not found for sprite {SpriteId} (mod: {ModId})",
                    spriteId,
                    metadata.OriginalModId
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
                    "Sprite texture file not found: {TexturePath} (sprite: {SpriteId})",
                    texturePath,
                    spriteId
                );
                return null;
            }

            try
            {
                // Load texture from file system
                var texture = Texture2D.FromFile(_graphicsDevice, texturePath);
                _textureCache[spriteId] = texture;
                _logger.Debug(
                    "Loaded sprite texture: {SpriteId} from {TexturePath}",
                    spriteId,
                    texturePath
                );
                return texture;
            }
            catch (Exception ex)
            {
                _logger.Error(
                    ex,
                    "Failed to load sprite texture: {SpriteId} from {TexturePath}, using placeholder",
                    spriteId,
                    texturePath
                );
                return GetPlaceholderTexture();
            }
        }

        /// <summary>
        /// Gets the cached animation frame sequence for a sprite animation.
        /// </summary>
        /// <param name="spriteId">The sprite ID.</param>
        /// <param name="animationName">The animation name.</param>
        /// <returns>The list of animation frames, or null if not found.</returns>
        public IReadOnlyList<SpriteAnimationFrame>? GetAnimationFrames(
            string spriteId,
            string animationName
        )
        {
            if (string.IsNullOrEmpty(spriteId) || string.IsNullOrEmpty(animationName))
            {
                return null;
            }

            var key = (spriteId, animationName);
            if (_animationCache.TryGetValue(key, out var frames))
            {
                return frames;
            }

            // Try to load sprite definition if not already loaded
            var definition = GetSpriteDefinition(spriteId);
            if (definition == null)
            {
                return null;
            }

            // Check cache again (PrecomputeAnimationFrames should have populated it)
            if (_animationCache.TryGetValue(key, out frames))
            {
                return frames;
            }

            return null;
        }

        /// <summary>
        /// Gets the source rectangle for a specific frame in an animation.
        /// </summary>
        /// <param name="spriteId">The sprite ID.</param>
        /// <param name="animationName">The animation name.</param>
        /// <param name="frameIndex">The frame index within the animation sequence.</param>
        /// <returns>The source rectangle, or null if not found.</returns>
        public Rectangle? GetAnimationFrameRectangle(
            string spriteId,
            string animationName,
            int frameIndex
        )
        {
            var frames = GetAnimationFrames(spriteId, animationName);
            if (frames == null || frameIndex < 0 || frameIndex >= frames.Count)
            {
                return null;
            }

            return frames[frameIndex].SourceRectangle;
        }

        /// <summary>
        /// Pre-computes animation frame data for all animations in a sprite definition.
        /// </summary>
        /// <param name="spriteId">The sprite ID.</param>
        /// <param name="definition">The sprite definition.</param>
        private void PrecomputeAnimationFrames(string spriteId, SpriteDefinition definition)
        {
            if (definition.Animations == null || definition.Frames == null)
            {
                return;
            }

            foreach (var animation in definition.Animations)
            {
                var frameList = new List<SpriteAnimationFrame>();

                if (animation.FrameIndices == null || animation.FrameDurations == null)
                {
                    continue;
                }

                for (int i = 0; i < animation.FrameIndices.Count; i++)
                {
                    int frameIndex = animation.FrameIndices[i];
                    double frameDuration =
                        i < animation.FrameDurations.Count ? animation.FrameDurations[i] : 0.0;

                    // Handle frame duration unit conversion
                    // If duration exceeds threshold, assume it's in milliseconds and convert to seconds
                    // This is a heuristic for backward compatibility when units aren't specified
                    float durationSeconds = (float)frameDuration;
                    if (frameDuration > MillisecondsThreshold)
                    {
                        durationSeconds = (float)(frameDuration / 1000.0);
                    }

                    // Find the frame definition
                    var frameDef = definition.Frames.FirstOrDefault(f => f.Index == frameIndex);
                    if (frameDef != null)
                    {
                        var animationFrame = new SpriteAnimationFrame
                        {
                            SourceRectangle = new Rectangle(
                                frameDef.X,
                                frameDef.Y,
                                frameDef.Width,
                                frameDef.Height
                            ),
                            DurationSeconds = durationSeconds,
                        };
                        frameList.Add(animationFrame);
                    }
                    else
                    {
                        _logger.Warning(
                            "Frame index {FrameIndex} not found in sprite {SpriteId}",
                            frameIndex,
                            spriteId
                        );
                    }
                }

                if (frameList.Count > 0)
                {
                    var key = (spriteId, animation.Name);
                    _animationCache[key] = frameList;
                }
            }
        }

        /// <summary>
        /// Validates that a sprite definition exists.
        /// </summary>
        /// <param name="spriteId">The sprite ID to validate.</param>
        /// <returns>True if the sprite definition exists, false otherwise.</returns>
        public bool ValidateSpriteDefinition(string spriteId)
        {
            if (string.IsNullOrEmpty(spriteId))
            {
                return false;
            }

            return GetSpriteDefinition(spriteId) != null;
        }

        /// <summary>
        /// Validates that an animation exists for a sprite.
        /// </summary>
        /// <param name="spriteId">The sprite ID.</param>
        /// <param name="animationName">The animation name to validate.</param>
        /// <returns>True if the animation exists, false otherwise.</returns>
        public bool ValidateAnimation(string spriteId, string animationName)
        {
            if (string.IsNullOrEmpty(spriteId) || string.IsNullOrEmpty(animationName))
            {
                return false;
            }

            var definition = GetSpriteDefinition(spriteId);
            if (definition == null)
            {
                return false;
            }

            return definition.Animations?.Any(a => a.Name == animationName) ?? false;
        }

        /// <summary>
        /// Gets whether an animation should loop.
        /// </summary>
        /// <param name="spriteId">The sprite ID.</param>
        /// <param name="animationName">The animation name.</param>
        /// <returns>True if the animation loops, false otherwise. Returns true if animation not found (default behavior).</returns>
        public bool GetAnimationLoops(string spriteId, string animationName)
        {
            if (string.IsNullOrEmpty(spriteId) || string.IsNullOrEmpty(animationName))
            {
                return true;
            }

            var definition = GetSpriteDefinition(spriteId);
            if (definition == null)
            {
                return true;
            }

            var animation = definition.Animations?.FirstOrDefault(a => a.Name == animationName);
            if (animation == null)
            {
                return true;
            }

            return animation.Loop;
        }

        /// <summary>
        /// Gets whether an animation should be horizontally flipped.
        /// </summary>
        /// <param name="spriteId">The sprite ID.</param>
        /// <param name="animationName">The animation name.</param>
        /// <returns>True if the animation should be flipped, false otherwise.</returns>
        public bool GetAnimationFlipHorizontal(string spriteId, string animationName)
        {
            if (string.IsNullOrEmpty(spriteId) || string.IsNullOrEmpty(animationName))
            {
                return false;
            }

            var definition = GetSpriteDefinition(spriteId);
            if (definition == null)
            {
                return false;
            }

            var animation = definition.Animations?.FirstOrDefault(a => a.Name == animationName);
            if (animation == null)
            {
                return false;
            }

            return animation.FlipHorizontal;
        }

        /// <summary>
        /// Gets or creates a placeholder texture for missing sprites.
        /// </summary>
        /// <returns>A placeholder texture (magenta square).</returns>
        private Texture2D GetPlaceholderTexture()
        {
            if (_placeholderTexture != null)
            {
                return _placeholderTexture;
            }

            // Create a simple 32x32 magenta placeholder texture
            const int size = 32;
            var texture = new Texture2D(_graphicsDevice, size, size);
            var colorData = new Color[size * size];

            // Fill with magenta (easily visible missing texture indicator)
            for (int i = 0; i < colorData.Length; i++)
            {
                colorData[i] = Color.Magenta;
            }

            // Add a black border
            for (int x = 0; x < size; x++)
            {
                colorData[x] = Color.Black; // Top border
                colorData[(size - 1) * size + x] = Color.Black; // Bottom border
            }
            for (int y = 0; y < size; y++)
            {
                colorData[y * size] = Color.Black; // Left border
                colorData[y * size + (size - 1)] = Color.Black; // Right border
            }

            texture.SetData(colorData);
            _placeholderTexture = texture;

            return _placeholderTexture;
        }
    }
}
