namespace MonoBall.Core.Audio;

/// <summary>
///     Interface for ambient sound system.
///     Manages looping ambient sounds attached to entities via AmbientSoundComponent.
/// </summary>
/// <remarks>
///     AmbientSoundSystem is update-driven with no public methods.
///     This interface exists for dependency inversion in IAudioSystems.
/// </remarks>
public interface IAmbientSoundSystem
{
    // System is update-driven with no public API
    // Interface exists for dependency inversion in IAudioSystems
}
