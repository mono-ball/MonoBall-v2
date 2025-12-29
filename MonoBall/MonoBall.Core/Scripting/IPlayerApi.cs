using Arch.Core;
using MonoBall.Core.ECS.Components;

namespace MonoBall.Core.Scripting;

/// <summary>
///     API for player-related operations.
/// </summary>
public interface IPlayerApi
{
    /// <summary>
    ///     Gets the player entity.
    /// </summary>
    /// <returns>The player entity, or null if player not initialized.</returns>
    Entity? GetPlayerEntity();

    /// <summary>
    ///     Gets the player's position component.
    /// </summary>
    /// <returns>The position component, or null if player not found.</returns>
    PositionComponent? GetPlayerPosition();

    /// <summary>
    ///     Gets the player's current map ID.
    /// </summary>
    /// <returns>The map ID, or null if player not in any map.</returns>
    string? GetPlayerMapId();

    /// <summary>
    ///     Checks if the player exists and is initialized.
    /// </summary>
    /// <returns>True if player exists, false otherwise.</returns>
    bool PlayerExists();
}
