using System;
using System.Collections.Generic;
using System.Linq;
using MonoBall.Core.Audio;
using MonoBall.Core.Audio.Core;

namespace MonoBall.Core.Audio.Internal
{
    /// <summary>
    /// Manages sound effect playback instances and their lifecycle.
    /// </summary>
    internal class SoundEffectPlaybackManager
    {
        private readonly Dictionary<ISoundEffectInstance, SoundEffectPlaybackState> _instances =
            new();
        private readonly object _lock = new();

        /// <summary>
        /// Adds a sound effect instance to the manager.
        /// </summary>
        /// <param name="instance">The sound effect instance.</param>
        /// <param name="state">The playback state.</param>
        public void AddInstance(ISoundEffectInstance instance, SoundEffectPlaybackState state)
        {
            lock (_lock)
            {
                _instances[instance] = state;
            }
        }

        /// <summary>
        /// Removes a sound effect instance from the manager.
        /// </summary>
        /// <param name="instance">The sound effect instance.</param>
        /// <returns>True if the instance was found and removed, false otherwise.</returns>
        public bool RemoveInstance(ISoundEffectInstance instance)
        {
            lock (_lock)
            {
                return _instances.Remove(instance);
            }
        }

        /// <summary>
        /// Gets the playback state for a sound effect instance.
        /// </summary>
        /// <param name="instance">The sound effect instance.</param>
        /// <param name="state">The playback state, or null if not found.</param>
        /// <returns>True if the instance was found, false otherwise.</returns>
        public bool TryGetState(ISoundEffectInstance instance, out SoundEffectPlaybackState? state)
        {
            lock (_lock)
            {
                return _instances.TryGetValue(instance, out state);
            }
        }

        /// <summary>
        /// Gets all sound effect instances for volume updates.
        /// </summary>
        /// <returns>All sound effect instances and their states.</returns>
        public IEnumerable<
            KeyValuePair<ISoundEffectInstance, SoundEffectPlaybackState>
        > GetAllInstances()
        {
            lock (_lock)
            {
                return _instances.ToList();
            }
        }

        /// <summary>
        /// Gets stopped instances that should be cleaned up.
        /// </summary>
        /// <returns>List of stopped instances.</returns>
        public List<ISoundEffectInstance> GetStoppedInstances()
        {
            lock (_lock)
            {
                return _instances
                    .Where(kvp =>
                        !kvp.Value.Instance.IsPlaying
                        || kvp.Value.Output?.PlaybackState != PlaybackState.Playing
                    )
                    .Select(kvp => kvp.Key)
                    .ToList();
            }
        }

        /// <summary>
        /// Disposes all sound effect instances and clears the manager.
        /// </summary>
        public void DisposeAll()
        {
            lock (_lock)
            {
                foreach (var kvp in _instances.ToList())
                {
                    kvp.Value.Output?.Stop();
                    kvp.Value.Output?.Dispose();
                    kvp.Value.VorbisReader?.Dispose();
                }
                _instances.Clear();
            }
        }
    }

    /// <summary>
    /// Internal class representing the playback state of a sound effect instance.
    /// </summary>
    internal class SoundEffectPlaybackState
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
        /// Gets or sets the PortAudio output for playback.
        /// </summary>
        public PortAudioOutput? Output { get; set; }

        /// <summary>
        /// Gets or sets the sound effect instance.
        /// </summary>
        public SoundEffectInstance Instance { get; set; } = null!;

        /// <summary>
        /// Gets or sets the volume sample provider.
        /// </summary>
        public VolumeSampleProvider? VolumeProvider { get; set; }

        /// <summary>
        /// Gets or sets the base volume (before SoundEffectVolume/MasterVolume multipliers).
        /// </summary>
        public float BaseVolume { get; set; }

        /// <summary>
        /// Gets or sets whether the sound effect is looping.
        /// </summary>
        public bool IsLooping { get; set; }
    }
}
