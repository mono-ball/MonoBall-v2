using System;
using MonoBall.Core.Audio.Core;
using Serilog;

namespace MonoBall.Core.Audio.Internal
{
    /// <summary>
    /// Manages music playback state, crossfades, and sequential fades.
    /// Handles mixer and output management for music.
    /// </summary>
    internal class MusicPlaybackManager
    {
        private readonly ILogger _logger;
        private readonly object _lock = new();

        private AudioMixer? _mixer;
        private PortAudioOutput? _output;
        private MusicPlaybackState? _currentMusic;
        private MusicPlaybackState? _crossfadeMusic;
        private MusicPlaybackState? _pendingMusic;

        /// <summary>
        /// Initializes a new instance of the MusicPlaybackManager.
        /// </summary>
        /// <param name="logger">The logger for logging operations.</param>
        public MusicPlaybackManager(ILogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Gets the current music playback state.
        /// </summary>
        public MusicPlaybackState? CurrentMusic
        {
            get
            {
                lock (_lock)
                {
                    return _currentMusic;
                }
            }
        }

        /// <summary>
        /// Gets the crossfade music playback state.
        /// </summary>
        public MusicPlaybackState? CrossfadeMusic
        {
            get
            {
                lock (_lock)
                {
                    return _crossfadeMusic;
                }
            }
        }

        /// <summary>
        /// Gets the pending music playback state.
        /// </summary>
        public MusicPlaybackState? PendingMusic
        {
            get
            {
                lock (_lock)
                {
                    return _pendingMusic;
                }
            }
        }

        /// <summary>
        /// Gets the music mixer.
        /// </summary>
        public AudioMixer? Mixer
        {
            get
            {
                lock (_lock)
                {
                    return _mixer;
                }
            }
        }

        /// <summary>
        /// Gets the music output.
        /// </summary>
        public PortAudioOutput? Output
        {
            get
            {
                lock (_lock)
                {
                    return _output;
                }
            }
        }

        /// <summary>
        /// Gets the playback state of the music output.
        /// </summary>
        public PlaybackState PlaybackState
        {
            get
            {
                lock (_lock)
                {
                    return _output?.PlaybackState ?? PlaybackState.Stopped;
                }
            }
        }

        /// <summary>
        /// Sets the current music playback state.
        /// </summary>
        /// <param name="state">The music playback state.</param>
        public void SetCurrentMusic(MusicPlaybackState? state)
        {
            lock (_lock)
            {
                _currentMusic = state;
            }
        }

        /// <summary>
        /// Sets the crossfade music playback state.
        /// </summary>
        /// <param name="state">The music playback state.</param>
        public void SetCrossfadeMusic(MusicPlaybackState? state)
        {
            lock (_lock)
            {
                _crossfadeMusic = state;
            }
        }

        /// <summary>
        /// Sets the pending music playback state.
        /// </summary>
        /// <param name="state">The music playback state.</param>
        public void SetPendingMusic(MusicPlaybackState? state)
        {
            lock (_lock)
            {
                _pendingMusic = state;
            }
        }

        /// <summary>
        /// Initializes the mixer and output if needed.
        /// </summary>
        /// <param name="format">The audio format for the mixer.</param>
        public void EnsureMixerAndOutput(AudioFormat format)
        {
            lock (_lock)
            {
                if (_mixer == null || _output == null)
                {
                    _mixer = new AudioMixer(format);
                    _output = new PortAudioOutput(_mixer);
                    _output.Play();
                }
            }
        }

        /// <summary>
        /// Adds a music track to the mixer.
        /// </summary>
        /// <param name="state">The music playback state.</param>
        /// <param name="volume">The initial volume.</param>
        public void AddToMixer(MusicPlaybackState state, float volume)
        {
            lock (_lock)
            {
                if (_mixer == null || state.VolumeProvider == null)
                {
                    return;
                }

                state.MixerInput = _mixer.AddSource(state.VolumeProvider, volume);
            }
        }

        /// <summary>
        /// Removes a music track from the mixer.
        /// </summary>
        /// <param name="state">The music playback state.</param>
        public void RemoveFromMixer(MusicPlaybackState state)
        {
            lock (_lock)
            {
                if (state.MixerInput != null && _mixer != null)
                {
                    _mixer.RemoveSource(state.MixerInput);
                }
            }
        }

        /// <summary>
        /// Starts pending music after fade-out completes (for sequential fade).
        /// </summary>
        /// <param name="format">The audio format for the mixer (if needed).</param>
        public void StartPendingMusic(AudioFormat format)
        {
            lock (_lock)
            {
                if (_pendingMusic == null)
                {
                    return;
                }

                var pending = _pendingMusic;
                _pendingMusic = null;

                // Initialize mixer and output if needed
                if (_mixer == null || _output == null)
                {
                    _mixer = new AudioMixer(format);
                    _output = new PortAudioOutput(_mixer);
                    _output.Play();
                }

                // Add pending track to mixer
                if (pending.VolumeProvider != null)
                {
                    pending.MixerInput = _mixer.AddSource(
                        pending.VolumeProvider,
                        pending.CurrentVolume
                    );
                }
                _currentMusic = pending;

                _logger.Debug(
                    "Started pending music after fade-out: {AudioId} (FadeIn: {FadeIn}s)",
                    pending.AudioId,
                    pending.FadeInDuration
                );
            }
        }

        /// <summary>
        /// Handles completion of fade-out for a music track.
        /// </summary>
        /// <param name="state">The music playback state that completed fade-out.</param>
        /// <param name="format">The audio format for starting pending music (if needed).</param>
        /// <returns>True if the state was the current music and was removed, false otherwise.</returns>
        public bool CompleteFadeOut(MusicPlaybackState state, AudioFormat? format = null)
        {
            lock (_lock)
            {
                RemoveFromMixer(state);
                state.VorbisReader?.Dispose();

                if (state == _currentMusic)
                {
                    _currentMusic = null;
                    if (_pendingMusic != null && format != null)
                    {
                        StartPendingMusic(format);
                    }
                    return true;
                }

                return false;
            }
        }

        /// <summary>
        /// Stops all music playback immediately.
        /// </summary>
        public void StopAll()
        {
            lock (_lock)
            {
                if (_currentMusic != null)
                {
                    RemoveFromMixer(_currentMusic);
                    _currentMusic.VorbisReader?.Dispose();
                    _currentMusic = null;
                }

                if (_crossfadeMusic != null)
                {
                    RemoveFromMixer(_crossfadeMusic);
                    _crossfadeMusic.VorbisReader?.Dispose();
                    _crossfadeMusic = null;
                }

                if (_pendingMusic != null)
                {
                    RemoveFromMixer(_pendingMusic);
                    _pendingMusic.VorbisReader?.Dispose();
                    _pendingMusic = null;
                }
            }
        }

        /// <summary>
        /// Pauses music playback.
        /// </summary>
        public void Pause()
        {
            lock (_lock)
            {
                _output?.Pause();
            }
        }

        /// <summary>
        /// Resumes music playback.
        /// </summary>
        public void Resume()
        {
            lock (_lock)
            {
                _output?.Play();
            }
        }

        /// <summary>
        /// Disposes the music playback manager and releases all resources.
        /// </summary>
        public void Dispose()
        {
            lock (_lock)
            {
                StopAll();

                _output?.Stop();
                _output?.Dispose();
                _output = null;
                _mixer?.Dispose();
                _mixer = null;
            }
        }
    }
}
