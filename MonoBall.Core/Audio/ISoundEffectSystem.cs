namespace MonoBall.Core.Audio;

/// <summary>
///     Interface for sound effect system.
///     Processes SoundEffectRequestComponent entities and plays one-shot sounds.
/// </summary>
/// <remarks>
///     SoundEffectSystem is update-driven with no public methods.
///     This interface exists for dependency inversion in IAudioSystems.
/// </remarks>
public interface ISoundEffectSystem
{
    // System is update-driven with no public API
    // Interface exists for dependency inversion in IAudioSystems
}
