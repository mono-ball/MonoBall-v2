namespace MonoBall.Core.ECS.Components;

/// <summary>
///     Component that stores player identity and configuration data.
/// </summary>
public struct PlayerComponent
{
    /// <summary>
    ///     The unique player ID.
    /// </summary>
    public string PlayerId { get; set; }

    /// <summary>
    ///     The name of the player.
    /// </summary>
    public string Name { get; set; }
}
