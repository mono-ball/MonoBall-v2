namespace MonoBall.Core.ECS.Events;

/// <summary>
///     Event fired when the player has fully entered the game (initial load complete).
///     This event is fired once when the player is first detected in a map after game initialization.
/// </summary>
public struct GameEnteredEvent
{
    /// <summary>
    ///     Gets or sets the map ID that the player entered on initial game load.
    /// </summary>
    public string InitialMapId { get; set; }
}
