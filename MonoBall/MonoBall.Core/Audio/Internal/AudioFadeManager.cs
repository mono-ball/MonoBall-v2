using System;
using MonoBall.Core.Audio.Core;

namespace MonoBall.Core.Audio.Internal
{
    /// <summary>
    /// Manages fade calculations and updates for audio playback.
    /// Handles fade-in and fade-out logic.
    /// </summary>
    internal class AudioFadeManager
    {
        /// <summary>
        /// Updates fade-in progress for a music playback state.
        /// </summary>
        /// <param name="state">The music playback state to update.</param>
        /// <param name="deltaTime">Time elapsed since last update in seconds.</param>
        public static void UpdateFadeIn(MusicPlaybackState state, float deltaTime)
        {
            if (state.FadeInDuration <= 0f || state.FadeInTimer >= state.FadeInDuration)
            {
                return;
            }

            state.FadeInTimer += deltaTime;
            float progress = Math.Min(state.FadeInTimer / state.FadeInDuration, 1.0f);
            state.CurrentVolume = progress * state.TargetVolume;
            ApplyVolumeToState(state);
        }

        /// <summary>
        /// Updates fade-out progress for a music playback state.
        /// </summary>
        /// <param name="state">The music playback state to update.</param>
        /// <param name="deltaTime">Time elapsed since last update in seconds.</param>
        /// <returns>True if fade-out is complete, false otherwise.</returns>
        public static bool UpdateFadeOut(MusicPlaybackState state, float deltaTime)
        {
            if (!state.IsFadingOut || state.FadeOutDuration <= 0f)
            {
                return false;
            }

            state.FadeOutTimer += deltaTime;
            float progress = Math.Min(state.FadeOutTimer / state.FadeOutDuration, 1.0f);
            float startVol = state.StartVolume > 0 ? state.StartVolume : state.TargetVolume;
            state.CurrentVolume = startVol * (1.0f - progress);
            ApplyVolumeToState(state);

            return progress >= 1.0f;
        }

        /// <summary>
        /// Applies the current volume from state to volume provider and mixer input.
        /// </summary>
        /// <param name="state">The music playback state.</param>
        private static void ApplyVolumeToState(MusicPlaybackState state)
        {
            if (state.VolumeProvider != null)
            {
                state.VolumeProvider.Volume = state.CurrentVolume;
            }
            if (state.MixerInput != null)
            {
                state.MixerInput.Volume = state.CurrentVolume;
            }
        }
    }

    /// <summary>
    /// Internal class representing the playback state of a music track.
    /// </summary>
    internal class MusicPlaybackState
    {
        /// <summary>
        /// Gets or sets the audio definition ID.
        /// </summary>
        public string AudioId { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the Vorbis reader for the audio file.
        /// </summary>
        public VorbisReader? VorbisReader { get; set; }

        /// <summary>
        /// Gets or sets the volume sample provider.
        /// </summary>
        public VolumeSampleProvider? VolumeProvider { get; set; }

        /// <summary>
        /// Gets or sets the mixer input handle.
        /// </summary>
        public AudioMixer.MixerInput? MixerInput { get; set; }

        /// <summary>
        /// Gets or sets the base volume from definition (before MusicVolume/MasterVolume multipliers).
        /// </summary>
        public float BaseVolume { get; set; }

        /// <summary>
        /// Gets or sets the target volume after all multipliers.
        /// </summary>
        public float TargetVolume { get; set; }

        /// <summary>
        /// Gets or sets the current volume (may be fading in/out).
        /// </summary>
        public float CurrentVolume { get; set; }

        /// <summary>
        /// Gets or sets the fade-in duration in seconds.
        /// </summary>
        public float FadeInDuration { get; set; }

        /// <summary>
        /// Gets or sets the fade-in timer in seconds.
        /// </summary>
        public float FadeInTimer { get; set; }

        /// <summary>
        /// Gets or sets the fade-out duration in seconds.
        /// </summary>
        public float FadeOutDuration { get; set; }

        /// <summary>
        /// Gets or sets the fade-out timer in seconds.
        /// </summary>
        public float FadeOutTimer { get; set; }

        /// <summary>
        /// Gets or sets whether the track is currently fading out.
        /// </summary>
        public bool IsFadingOut { get; set; }

        /// <summary>
        /// Gets or sets the volume at the start of fade-out.
        /// </summary>
        public float StartVolume { get; set; }

        /// <summary>
        /// Gets or sets whether this track is crossfading (being promoted from crossfade to current).
        /// </summary>
        public bool IsCrossfading { get; set; }
    }
}
