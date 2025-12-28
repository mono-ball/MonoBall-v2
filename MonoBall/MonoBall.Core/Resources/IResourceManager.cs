using System.Collections.Generic;
using FontStashSharp;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoBall.Core.Audio.Core;
using MonoBall.Core.Maps;

namespace MonoBall.Core.Resources
{
    /// <summary>
    /// Unified resource manager for loading and caching all game resources.
    /// </summary>
    public interface IResourceManager
    {
        // Texture Loading (from SpriteDefinition or TilesetDefinition)
        /// <summary>
        /// Loads a texture for the specified resource ID (sprite or tileset).
        /// </summary>
        /// <param name="resourceId">The resource ID.</param>
        /// <returns>The loaded texture.</returns>
        /// <exception cref="ArgumentException">Thrown when resourceId is null/empty.</exception>
        /// <exception cref="InvalidOperationException">Thrown when definition not found or texture path missing.</exception>
        /// <exception cref="FileNotFoundException">Thrown when texture file not found.</exception>
        Texture2D LoadTexture(string resourceId);

        /// <summary>
        /// Gets a cached texture if available, without loading.
        /// </summary>
        /// <param name="resourceId">The resource ID.</param>
        /// <returns>The cached texture, or null if not cached.</returns>
        Texture2D? GetCachedTexture(string resourceId);

        /// <summary>
        /// Checks if a texture is cached.
        /// </summary>
        /// <param name="resourceId">The resource ID.</param>
        /// <returns>True if cached, false otherwise.</returns>
        bool HasTexture(string resourceId);

        // Font Loading (from FontDefinition)
        /// <summary>
        /// Loads a font system for the specified font resource ID.
        /// </summary>
        /// <param name="resourceId">The font resource ID.</param>
        /// <returns>The loaded font system.</returns>
        /// <exception cref="ArgumentException">Thrown when resourceId is null/empty.</exception>
        /// <exception cref="InvalidOperationException">Thrown when definition not found, font path missing, or font is invalid.</exception>
        /// <exception cref="FileNotFoundException">Thrown when font file not found.</exception>
        FontSystem LoadFont(string resourceId);

        /// <summary>
        /// Gets a cached font system if available, without loading.
        /// </summary>
        /// <param name="resourceId">The font resource ID.</param>
        /// <returns>The cached font system, or null if not cached.</returns>
        FontSystem? GetCachedFont(string resourceId);

        /// <summary>
        /// Checks if a font is cached.
        /// </summary>
        /// <param name="resourceId">The font resource ID.</param>
        /// <returns>True if cached, false otherwise.</returns>
        bool HasFont(string resourceId);

        // Audio Loading (from AudioDefinition)
        /// <summary>
        /// Creates a new audio reader for the specified audio resource.
        /// Each call returns a new VorbisReader instance - readers are NOT cached because they are stateful.
        /// </summary>
        /// <param name="resourceId">The audio resource ID.</param>
        /// <returns>A new VorbisReader instance positioned at the start of the audio file.</returns>
        /// <remarks>
        /// <para>
        /// VorbisReader instances are stateful (maintain playback position) and cannot be safely shared
        /// between concurrent audio playbacks. Each audio playback should use its own reader instance.
        /// Callers are responsible for disposing the returned reader when done.
        /// </para>
        /// </remarks>
        /// <exception cref="ArgumentException">Thrown when resourceId is null/empty.</exception>
        /// <exception cref="InvalidOperationException">Thrown when definition not found or audio path missing.</exception>
        /// <exception cref="FileNotFoundException">Thrown when audio file not found.</exception>
        VorbisReader LoadAudioReader(string resourceId);

        /// <summary>
        /// Gets a cached audio reader if available, without loading.
        /// Always returns null - audio readers are not cached.
        /// </summary>
        /// <param name="resourceId">The audio resource ID.</param>
        /// <returns>Always returns null - audio readers are not cached.</returns>
        VorbisReader? GetCachedAudioReader(string resourceId);

        /// <summary>
        /// Checks if an audio resource is cached.
        /// Always returns false - audio readers are not cached.
        /// </summary>
        /// <param name="resourceId">The audio resource ID.</param>
        /// <returns>Always returns false - audio readers are not cached.</returns>
        bool HasAudio(string resourceId);

        // Shader Loading (from ShaderDefinition)
        /// <summary>
        /// Loads a shader effect for the specified shader resource ID.
        /// </summary>
        /// <param name="resourceId">The shader resource ID.</param>
        /// <returns>The loaded shader effect.</returns>
        /// <exception cref="ArgumentException">Thrown when resourceId is null/empty.</exception>
        /// <exception cref="InvalidOperationException">Thrown when definition not found, source file missing, or shader bytecode invalid.</exception>
        /// <exception cref="FileNotFoundException">Thrown when shader file not found.</exception>
        Effect LoadShader(string resourceId);

        /// <summary>
        /// Gets a cached shader effect if available, without loading.
        /// </summary>
        /// <param name="resourceId">The shader resource ID.</param>
        /// <returns>The cached shader effect, or null if not cached.</returns>
        Effect? GetCachedShader(string resourceId);

        /// <summary>
        /// Checks if a shader is cached.
        /// </summary>
        /// <param name="resourceId">The shader resource ID.</param>
        /// <returns>True if cached, false otherwise.</returns>
        bool HasShader(string resourceId);

        // Sprite Definition Access
        /// <summary>
        /// Gets a sprite definition by ID, caching it and precomputing animation frames.
        /// </summary>
        /// <param name="spriteId">The sprite ID.</param>
        /// <returns>The sprite definition.</returns>
        /// <exception cref="ArgumentException">Thrown when spriteId is null/empty.</exception>
        /// <exception cref="InvalidOperationException">Thrown when sprite definition not found.</exception>
        SpriteDefinition GetSpriteDefinition(string spriteId);

        // Tileset Definition Access
        /// <summary>
        /// Gets a tileset definition by ID, caching it.
        /// </summary>
        /// <param name="tilesetId">The tileset ID.</param>
        /// <returns>The tileset definition.</returns>
        /// <exception cref="ArgumentException">Thrown when tilesetId is null/empty.</exception>
        /// <exception cref="InvalidOperationException">Thrown when tileset definition not found.</exception>
        TilesetDefinition GetTilesetDefinition(string tilesetId);

        // Tileset Animation Access
        /// <summary>
        /// Gets animation frames for a specific tile, loading and caching if not already cached.
        /// </summary>
        /// <param name="tilesetId">The tileset ID.</param>
        /// <param name="localTileId">The local tile ID within the tileset.</param>
        /// <returns>The animation frames as a readonly list.</returns>
        /// <exception cref="ArgumentException">Thrown when tilesetId is null/empty.</exception>
        /// <exception cref="InvalidOperationException">Thrown when tileset not found or tile has no animation.</exception>
        IReadOnlyList<TileAnimationFrame> GetTileAnimation(string tilesetId, int localTileId);

        /// <summary>
        /// Gets cached animation frames for a specific tile (fast lookup, no definition loading).
        /// </summary>
        /// <param name="tilesetId">The tileset ID.</param>
        /// <param name="localTileId">The local tile ID within the tileset.</param>
        /// <returns>The cached animation frames as a readonly list, or null if not cached.</returns>
        IReadOnlyList<TileAnimationFrame>? GetCachedTileAnimation(
            string tilesetId,
            int localTileId
        );

        /// <summary>
        /// Calculates the source rectangle for a tile based on its GID (Global ID).
        /// </summary>
        /// <param name="tilesetId">The tileset ID.</param>
        /// <param name="gid">The Global ID of the tile.</param>
        /// <param name="firstGid">The first GID for this tileset.</param>
        /// <returns>The source rectangle.</returns>
        /// <exception cref="ArgumentException">Thrown when tilesetId is null/empty or gid/localTileId is invalid.</exception>
        /// <exception cref="InvalidOperationException">Thrown when tileset not found or calculated rectangle is out of bounds.</exception>
        Rectangle CalculateTilesetSourceRectangle(string tilesetId, int gid, int firstGid);

        // Sprite Animation Access
        /// <summary>
        /// Gets the cached animation frame sequence for a sprite animation.
        /// </summary>
        /// <param name="spriteId">The sprite ID.</param>
        /// <param name="animationName">The animation name.</param>
        /// <returns>The list of animation frames.</returns>
        /// <exception cref="ArgumentException">Thrown when spriteId or animationName is null/empty.</exception>
        /// <exception cref="InvalidOperationException">Thrown when sprite or animation not found.</exception>
        IReadOnlyList<SpriteAnimationFrame> GetAnimationFrames(
            string spriteId,
            string animationName
        );

        /// <summary>
        /// Gets the source rectangle for a specific frame in an animation.
        /// </summary>
        /// <param name="spriteId">The sprite ID.</param>
        /// <param name="animationName">The animation name.</param>
        /// <param name="frameIndex">The frame index within the animation sequence.</param>
        /// <returns>The source rectangle.</returns>
        /// <exception cref="ArgumentException">Thrown when spriteId or animationName is null/empty, or frameIndex is out of range.</exception>
        /// <exception cref="InvalidOperationException">Thrown when sprite or animation not found.</exception>
        Rectangle GetAnimationFrameRectangle(string spriteId, string animationName, int frameIndex);

        // Sprite Validation
        /// <summary>
        /// Validates that a sprite definition exists.
        /// </summary>
        /// <param name="spriteId">The sprite ID to validate.</param>
        /// <returns>True if the sprite definition exists, false otherwise.</returns>
        bool ValidateSpriteDefinition(string spriteId);

        /// <summary>
        /// Validates that an animation exists for a sprite.
        /// </summary>
        /// <param name="spriteId">The sprite ID.</param>
        /// <param name="animationName">The animation name to validate.</param>
        /// <returns>True if the animation exists, false otherwise.</returns>
        bool ValidateAnimation(string spriteId, string animationName);

        /// <summary>
        /// Gets whether an animation should loop.
        /// </summary>
        /// <param name="spriteId">The sprite ID.</param>
        /// <param name="animationName">The animation name.</param>
        /// <returns>True if the animation loops, false otherwise. Returns true if animation not found (default behavior).</returns>
        bool GetAnimationLoops(string spriteId, string animationName);

        /// <summary>
        /// Gets whether an animation should be horizontally flipped.
        /// </summary>
        /// <param name="spriteId">The sprite ID.</param>
        /// <param name="animationName">The animation name.</param>
        /// <returns>True if the animation should be flipped, false otherwise.</returns>
        bool GetAnimationFlipHorizontal(string spriteId, string animationName);

        // Generic Definition Access
        /// <summary>
        /// Gets a definition of the specified type by resource ID.
        /// </summary>
        /// <typeparam name="T">The definition type.</typeparam>
        /// <param name="resourceId">The resource ID.</param>
        /// <returns>The definition, or null if not found.</returns>
        T? GetDefinition<T>(string resourceId)
            where T : class;

        // Cache Management
        /// <summary>
        /// Unloads a specific resource from cache.
        /// </summary>
        /// <param name="resourceId">The resource ID.</param>
        /// <param name="type">The resource type.</param>
        void UnloadResource(string resourceId, ResourceType type);

        /// <summary>
        /// Unloads all resources of the specified type, or all resources if type is null.
        /// </summary>
        /// <param name="type">The resource type to unload, or null for all types.</param>
        void UnloadAll(ResourceType? type = null);

        /// <summary>
        /// Clears the cache for the specified resource type(s). Equivalent to UnloadAll().
        /// </summary>
        /// <param name="type">The resource type to clear, or null for all types.</param>
        void ClearCache(ResourceType? type = null);
    }
}
