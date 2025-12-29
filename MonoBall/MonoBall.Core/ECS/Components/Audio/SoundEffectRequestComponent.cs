namespace MonoBall.Core.ECS.Components.Audio;

/// <summary>
///     Component for requesting sound effect playback.
///     System processes this component and removes it after playback starts.
/// </summary>
public struct SoundEffectRequestComponent
{
    /// <summary>
    ///     The audio definition ID for the sound effect.
    /// </summary>
    public string AudioId { get; set; }

    /// <summary>
    ///     Volume override (0-1, or -1 to use definition default).
    /// </summary>
    public float Volume { get; set; }

    /// <summary>
    ///     Pitch adjustment (-1 to 1).
    /// </summary>
    public float Pitch { get; set; }

    /// <summary>
    ///     Pan adjustment (-1 left to 1 right).
    /// </summary>
    public float Pan { get; set; }
}
