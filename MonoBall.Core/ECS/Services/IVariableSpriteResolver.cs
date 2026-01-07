using System;
using Arch.Core;

namespace MonoBall.Core.ECS.Services;

/// <summary>
///     Service for resolving variable sprite IDs to actual sprite IDs based on game state.
///     Variable sprites are sprite IDs wrapped in curly braces (e.g., "{base:sprite:npcs/generic/var_rival}")
///     that resolve to actual sprite IDs by reading from game state variables.
/// </summary>
public interface IVariableSpriteResolver : IDisposable
{
    /// <summary>
    ///     Resolves a variable sprite ID to an actual sprite ID.
    /// </summary>
    /// <param name="variableSpriteId">
    ///     The variable sprite ID wrapped in curly braces (e.g.,
    ///     "{base:sprite:npcs/generic/var_rival}").
    /// </param>
    /// <param name="entity">
    ///     Optional entity context for caching resolved values. Not currently used but reserved for future
    ///     use.
    /// </param>
    /// <returns>The resolved sprite ID, or null if resolution fails due to invalid format.</returns>
    /// <exception cref="InvalidOperationException">Thrown if the variable is missing from game state (fail fast, no fallback).</exception>
    string? ResolveVariableSprite(string variableSpriteId, Entity? entity = null);

    /// <summary>
    ///     Checks if a sprite ID is a variable sprite (wrapped in curly braces).
    /// </summary>
    /// <param name="spriteId">The sprite ID to check.</param>
    /// <returns>True if the sprite ID is a variable sprite.</returns>
    bool IsVariableSprite(string spriteId);

    /// <summary>
    ///     Clears cached resolution for a specific entity.
    ///     Note: Implementation may cache per variable sprite ID rather than per entity,
    ///     in which case this method may be a no-op. Use ClearAllCache() to clear all cache.
    /// </summary>
    /// <param name="entity">The entity to clear cache for.</param>
    void ClearEntityCache(Entity entity);

    /// <summary>
    ///     Clears all cached resolutions.
    /// </summary>
    void ClearAllCache();
}
