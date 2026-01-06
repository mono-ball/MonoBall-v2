namespace MonoBall.Core.Audio;

/// <summary>
///     Interface for music playback system.
///     Handles PlayMusicEvent and StopMusicEvent for background music control.
/// </summary>
/// <remarks>
///     MusicPlaybackSystem is event-driven with no public methods.
///     This interface exists for dependency inversion in IAudioSystems.
/// </remarks>
public interface IMusicPlaybackSystem
{
    // System is event-driven with no public API
    // Interface exists for dependency inversion in IAudioSystems
}
