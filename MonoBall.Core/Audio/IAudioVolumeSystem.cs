namespace MonoBall.Core.Audio;

/// <summary>
///     Interface for audio volume system.
///     Handles volume change events (master, music, sound effect).
/// </summary>
/// <remarks>
///     AudioVolumeSystem is event-driven with no public methods.
///     This interface exists for dependency inversion in IAudioSystems.
/// </remarks>
public interface IAudioVolumeSystem
{
    // System is event-driven with no public API
    // Interface exists for dependency inversion in IAudioSystems
}
