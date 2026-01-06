namespace MonoBall.Core.Audio;

/// <summary>
///     Interface for map-based music management.
///     Manages background music based on map transitions.
/// </summary>
/// <remarks>
///     MapMusicSystem responds to events (MapTransitionEvent, GameEnteredEvent)
///     and has no public methods besides its constructor.
///     This interface exists for DIP compliance in bundle interfaces.
/// </remarks>
public interface IMapMusicSystem
{
    // MapMusicSystem is event-driven with no public API
    // Interface exists for dependency inversion in IAudioSystems
}
