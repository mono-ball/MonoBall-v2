using System;
using Arch.Core;

namespace MonoBall.Core.ECS.Services;

/// <summary>
///     Service for querying entity positions in the spatial hash.
///     Provides O(1) lookups for entities at specific tile positions.
/// </summary>
/// <remarks>
///     <para>
///         CRITICAL: The ReadOnlySpan returned by these methods is only valid until the next call
///         to any method on this interface. The span is backed by a pooled list that may be reused.
///     </para>
///     <para>
///         DO NOT store the span. Iterate immediately and copy any entities you need to keep.
///     </para>
///     <example>
///         <code>
///         // CORRECT: Iterate immediately
///         foreach (var entity in positionService.GetEntitiesAt(mapId, x, y))
///         {
///             // Process entity
///         }
///
///         // INCORRECT: Storing span for later
///         var entities = positionService.GetEntitiesAt(mapId, x, y);
///         DoSomethingElse(); // Span may now be invalid!
///         foreach (var entity in entities) { } // UNDEFINED BEHAVIOR
///         </code>
///     </example>
/// </remarks>
public interface IEntityPositionService
{
    /// <summary>
    ///     Gets all entities at a specific tile position and elevation.
    /// </summary>
    /// <param name="mapId">The map identifier.</param>
    /// <param name="x">The X coordinate in tile space.</param>
    /// <param name="y">The Y coordinate in tile space.</param>
    /// <param name="elevation">The elevation to filter by.</param>
    /// <returns>
    ///     A span of entities at the position. ONLY VALID UNTIL NEXT CALL.
    ///     Returns empty span if no entities found.
    /// </returns>
    ReadOnlySpan<Entity> GetEntitiesAt(string mapId, int x, int y, int elevation);

    /// <summary>
    ///     Gets all entities at a specific tile position (any elevation).
    /// </summary>
    /// <param name="mapId">The map identifier.</param>
    /// <param name="x">The X coordinate in tile space.</param>
    /// <param name="y">The Y coordinate in tile space.</param>
    /// <returns>
    ///     A span of entities at the position. ONLY VALID UNTIL NEXT CALL.
    ///     Returns empty span if no entities found.
    /// </returns>
    ReadOnlySpan<Entity> GetEntitiesAt(string mapId, int x, int y);

    /// <summary>
    ///     Gets all entities within a range of a center position.
    /// </summary>
    /// <param name="mapId">The map identifier.</param>
    /// <param name="centerX">The center X coordinate in tile space.</param>
    /// <param name="centerY">The center Y coordinate in tile space.</param>
    /// <param name="range">The range in tiles (Manhattan distance).</param>
    /// <param name="elevation">The elevation to filter by.</param>
    /// <returns>
    ///     A span of entities in range. ONLY VALID UNTIL NEXT CALL.
    ///     Returns empty span if no entities found.
    /// </returns>
    ReadOnlySpan<Entity> GetEntitiesInRange(
        string mapId,
        int centerX,
        int centerY,
        int range,
        int elevation
    );
}
