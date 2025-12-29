using System;
using System.Collections.Generic;
using Arch.Core;
using MonoBall.Core.ECS.Events;
using Serilog;

namespace MonoBall.Core.ECS.Services;

/// <summary>
///     Implementation of IVariableSpriteResolver that resolves variable sprite IDs to actual sprite IDs
///     by reading from game state variables. Variable sprites are wrapped in curly braces and resolve
///     to sprite IDs stored in game state variables.
///     <para>
///         <strong>Runtime Variable Changes:</strong> The resolver automatically invalidates its cache when
///         game state variables change via <see cref="VariableChangedEvent" />. This ensures that if a variable
///         sprite's underlying game state variable changes during gameplay, subsequent resolutions will use
///         the new value. However, note that already-created NPCs will not automatically update their sprites
///         - only new NPCs created after the variable change will use the new resolved sprite ID.
///     </para>
///     <para>
///         <strong>Caching Strategy:</strong> Resolutions are cached per variable sprite ID (shared across all
///         entities using the same variable sprite). This is more efficient than per-entity caching since multiple
///         NPCs may use the same variable sprite. The cache is automatically cleared when any game state variable
///         changes to ensure correctness.
///     </para>
/// </summary>
public class VariableSpriteResolver : IVariableSpriteResolver, IDisposable
{
    private readonly IFlagVariableService _flagVariableService;
    private readonly ILogger _logger;

    // Cache per variable sprite ID (shared across all entities using same variable sprite)
    // More efficient than per-entity caching since multiple NPCs may use same variable sprite
    private readonly Dictionary<string, string> _resolutionCache = new();
    private readonly List<IDisposable> _subscriptions = new();

    private bool _disposed;

    /// <summary>
    ///     Initializes a new instance of the VariableSpriteResolver.
    /// </summary>
    /// <param name="flagVariableService">The flag/variable service for reading game state variables.</param>
    /// <param name="logger">The logger for logging operations.</param>
    /// <exception cref="ArgumentNullException">Thrown if flagVariableService or logger is null.</exception>
    public VariableSpriteResolver(IFlagVariableService flagVariableService, ILogger logger)
    {
        _flagVariableService =
            flagVariableService ?? throw new ArgumentNullException(nameof(flagVariableService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Subscribe to variable change events to invalidate cache
        _subscriptions.Add(EventBus.Subscribe<VariableChangedEvent>(OnVariableChanged));
    }

    /// <summary>
    ///     Resolves a variable sprite ID to an actual sprite ID.
    /// </summary>
    /// <param name="variableSpriteId">The variable sprite ID wrapped in curly braces.</param>
    /// <param name="entity">Optional entity context (not currently used).</param>
    /// <returns>The resolved sprite ID, or null if the format is invalid.</returns>
    /// <exception cref="InvalidOperationException">Thrown if the variable is missing from game state.</exception>
    public string? ResolveVariableSprite(string variableSpriteId, Entity? entity = null)
    {
        if (string.IsNullOrEmpty(variableSpriteId))
            return null;

        if (!IsVariableSprite(variableSpriteId))
            return variableSpriteId; // Not a variable sprite, return as-is

        // Check cache first (shared across all entities using this variable sprite)
        if (_resolutionCache.TryGetValue(variableSpriteId, out var cached))
            return cached;

        // Extract variable sprite ID from within curly braces
        var extractedSpriteId = ExtractVariableSpriteId(variableSpriteId);
        if (string.IsNullOrEmpty(extractedSpriteId))
        {
            _logger.Error(
                "Invalid variable sprite ID format: {VariableSpriteId} (empty after extraction)",
                variableSpriteId
            );
            return null;
        }

        // Use extracted sprite ID directly as the game state variable key
        // Read sprite ID from game state variable
        var resolvedSpriteId = _flagVariableService.GetVariable<string>(extractedSpriteId);

        if (string.IsNullOrEmpty(resolvedSpriteId))
        {
            _logger.Error(
                "Cannot resolve variable sprite '{VariableSpriteId}': Game state variable '{VariableKey}' is not set. Variable must be set before creating NPCs with variable sprites.",
                variableSpriteId,
                extractedSpriteId
            );
            throw new InvalidOperationException(
                $"Cannot resolve variable sprite '{variableSpriteId}': Game state variable '{extractedSpriteId}' is not set. "
                    + "Variable must be set before creating NPCs with variable sprites."
            );
        }

        // Cache the resolution (shared across all entities)
        _resolutionCache[variableSpriteId] = resolvedSpriteId;

        return resolvedSpriteId;
    }

    /// <summary>
    ///     Checks if a sprite ID is a variable sprite (wrapped in curly braces).
    /// </summary>
    /// <param name="spriteId">The sprite ID to check.</param>
    /// <returns>True if the sprite ID is a variable sprite.</returns>
    public bool IsVariableSprite(string spriteId)
    {
        if (string.IsNullOrEmpty(spriteId))
            return false;

        // Check if sprite ID is wrapped in curly braces
        return spriteId.StartsWith("{", StringComparison.Ordinal)
            && spriteId.EndsWith("}", StringComparison.Ordinal);
    }

    /// <summary>
    ///     Clears cached resolution for a specific entity.
    ///     Note: This is a no-op since we cache per variable sprite ID, not per entity.
    ///     This method exists for interface compatibility.
    /// </summary>
    /// <param name="entity">The entity to clear cache for.</param>
    public void ClearEntityCache(Entity entity)
    {
        // No-op: we cache per variable sprite ID, not per entity
        // Cache is cleared via ClearAllCache() when game state variables change
    }

    /// <summary>
    ///     Clears all cached resolutions.
    /// </summary>
    public void ClearAllCache()
    {
        _resolutionCache.Clear();
    }

    /// <summary>
    ///     Disposes of resources and unsubscribes from events.
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
    }

    /// <summary>
    ///     Handles variable change events to invalidate cache.
    ///     When a game state variable changes, we clear the resolution cache since
    ///     variable sprite resolutions depend on game state variables.
    /// </summary>
    /// <param name="variableChangedEvent">The variable changed event.</param>
    private void OnVariableChanged(ref VariableChangedEvent variableChangedEvent)
    {
        // Clear all cached resolutions when any variable changes
        // This is safe but not optimal - we could track which variables affect which sprites
        // For now, clearing all cache ensures correctness
        if (_resolutionCache.Count > 0)
        {
            _resolutionCache.Clear();
            _logger.Debug(
                "Cleared variable sprite resolution cache due to variable change: {VariableKey}",
                variableChangedEvent.Key
            );
        }
    }

    /// <summary>
    ///     Protected dispose method following standard dispose pattern.
    /// </summary>
    /// <param name="disposing">True if called from Dispose(), false if from finalizer.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed && disposing)
            // Unsubscribe from events to prevent memory leaks
            foreach (var subscription in _subscriptions)
                subscription.Dispose();
        _disposed = true;
    }

    /// <summary>
    ///     Extracts the variable sprite ID from within curly braces.
    /// </summary>
    /// <param name="variableSpriteId">The variable sprite ID wrapped in curly braces.</param>
    /// <returns>The extracted sprite ID, or empty string if format is invalid.</returns>
    private static string ExtractVariableSpriteId(string variableSpriteId)
    {
        if (string.IsNullOrEmpty(variableSpriteId))
            return string.Empty;

        // Remove leading '{' and trailing '}'
        if (variableSpriteId.Length < 2)
            return string.Empty;

        return variableSpriteId.Substring(1, variableSpriteId.Length - 2);
    }
}
