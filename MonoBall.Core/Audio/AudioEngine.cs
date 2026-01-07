using System;
using MonoBall.Core.Audio.Core;
using MonoBall.Core.Audio.Internal;
using MonoBall.Core.Mods;
using MonoBall.Core.Resources;
using Serilog;

namespace MonoBall.Core.Audio;

/// <summary>
///     Low-level audio playback implementation using PortAudio and NVorbis.
/// </summary>
/// <remarks>
///     <para>
///         Audio files must be at 44100 Hz sample rate. Files with different sample rates will produce
///         warnings and may play at incorrect speed/pitch. Resampling support is planned for future versions.
///     </para>
///     <para>
///         All audio is mixed and output at 44100 Hz. The mixer requires all sources to have matching
///         sample rates and channel counts.
///     </para>
/// </remarks>
public class AudioEngine : IAudioEngine, IDisposable
{
    /// <summary>
    ///     The target sample rate for all audio playback (44100 Hz).
    ///     Audio files must match this sample rate or warnings will be logged.
    /// </summary>
    private const int TargetSampleRate = 44100;

    private readonly object _lock = new();
    private readonly ILogger _logger;

    private readonly IModManager _modManager;

    // Managers for different responsibilities
    private readonly MusicPlaybackManager _musicManager;
    private readonly IResourceManager _resourceManager;
    private readonly SoundEffectPlaybackManager _soundEffectManager;

    private bool _disposed;

    private float _masterVolume = 1.0f;
    private float _musicVolume = 1.0f;
    private float _soundEffectVolume = 1.0f;

    /// <summary>
    ///     Initializes a new instance of the AudioEngine.
    /// </summary>
    /// <param name="modManager">The mod manager for registry and mod manifest access.</param>
    /// <param name="resourceManager">The resource manager for loading audio readers.</param>
    /// <param name="logger">The logger for logging operations.</param>
    public AudioEngine(IModManager modManager, IResourceManager resourceManager, ILogger logger)
    {
        _modManager = modManager ?? throw new ArgumentNullException(nameof(modManager));
        _resourceManager =
            resourceManager ?? throw new ArgumentNullException(nameof(resourceManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Initialize managers
        _musicManager = new MusicPlaybackManager(logger);
        _soundEffectManager = new SoundEffectPlaybackManager();
    }

    /// <summary>
    ///     Plays a sound effect.
    ///     Throws exceptions on failure (fail fast per .cursorrules).
    /// </summary>
    /// <param name="audioId">The audio definition ID.</param>
    /// <param name="volume">Volume (0.0 - 1.0).</param>
    /// <param name="pitch">Pitch adjustment (-1.0 to 1.0).</param>
    /// <param name="pan">Pan adjustment (-1.0 left to 1.0 right).</param>
    /// <returns>The sound effect instance.</returns>
    /// <exception cref="ObjectDisposedException">Thrown when AudioEngine is disposed.</exception>
    /// <exception cref="ArgumentException">Thrown when audioId is null/empty.</exception>
    /// <exception cref="InvalidOperationException">Thrown when audio definition not found or audio loading fails.</exception>
    /// <exception cref="FileNotFoundException">Thrown when audio file not found.</exception>
    public ISoundEffectInstance PlaySound(string audioId, float volume, float pitch, float pan)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(AudioEngine));

        if (string.IsNullOrEmpty(audioId))
            throw new ArgumentException("Audio ID cannot be null or empty.", nameof(audioId));

        // Load VorbisReader from ResourceManager (handles definition lookup and caching)
        // Fail fast - let exceptions propagate
        var vorbisReader = _resourceManager.LoadAudioReader(audioId);

        // Get definition for volume and other properties
        var definition = _modManager.GetDefinition<AudioDefinition>(audioId);
        if (definition == null)
            throw new InvalidOperationException(
                $"Audio definition not found: {audioId}. "
                    + "Ensure the audio is defined in a loaded mod."
            );

        // Validate sample rate
        if (vorbisReader.Format.SampleRate != TargetSampleRate)
            _logger.Warning(
                "Audio file {AudioId} has sample rate {SampleRate} Hz, expected {TargetSampleRate} Hz. "
                    + "Audio may play at wrong speed/pitch. Resampling not yet implemented.",
                audioId,
                vorbisReader.Format.SampleRate,
                TargetSampleRate
            );

        // Create playback state
        var finalVolume = CalculateSoundEffectVolume(volume);
        var volumeProvider = new VolumeSampleProvider(vorbisReader) { Volume = finalVolume };

        // Create output (each sound effect gets its own output for simplicity)
        // Note: In a more optimized implementation, we could use a mixer for all sound effects
        var output = new PortAudioOutput(volumeProvider);
        output.Play();

        var instance = new SoundEffectInstance(volume, pitch, pan); // Store original values, not final volume
        var playbackState = new SoundEffectPlaybackState
        {
            AudioId = audioId,
            VorbisReader = vorbisReader,
            Output = output,
            Instance = instance,
            VolumeProvider = volumeProvider,
            BaseVolume = volume, // Store original volume before multipliers
        };

        lock (_lock)
        {
            instance.IsPlaying = true;
            _soundEffectManager.AddInstance(instance, playbackState);
        }

        _logger.Debug("Started sound effect: {AudioId}", audioId);
        return instance;
    }

    /// <summary>
    ///     Plays a looping sound effect.
    ///     Throws exceptions on failure (fail fast per .cursorrules).
    /// </summary>
    /// <param name="audioId">The audio definition ID.</param>
    /// <param name="volume">Volume (0.0 - 1.0).</param>
    /// <returns>The sound effect instance.</returns>
    /// <exception cref="ObjectDisposedException">Thrown when AudioEngine is disposed.</exception>
    /// <exception cref="ArgumentException">Thrown when audioId is null/empty.</exception>
    /// <exception cref="InvalidOperationException">Thrown when audio definition not found or audio loading fails.</exception>
    /// <exception cref="FileNotFoundException">Thrown when audio file not found.</exception>
    public ISoundEffectInstance PlayLoopingSound(string audioId, float volume)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(AudioEngine));

        if (string.IsNullOrEmpty(audioId))
            throw new ArgumentException("Audio ID cannot be null or empty.", nameof(audioId));

        // Load VorbisReader from ResourceManager (handles definition lookup and caching)
        // Fail fast - let exceptions propagate
        var vorbisReader = _resourceManager.LoadAudioReader(audioId);

        // Get definition for volume and other properties
        var definition = _modManager.GetDefinition<AudioDefinition>(audioId);
        if (definition == null)
            throw new InvalidOperationException(
                $"Audio definition not found: {audioId}. "
                    + "Ensure the audio is defined in a loaded mod."
            );

        // Validate sample rate
        if (vorbisReader.Format.SampleRate != TargetSampleRate)
            _logger.Warning(
                "Audio file {AudioId} has sample rate {SampleRate} Hz, expected {TargetSampleRate} Hz. "
                    + "Audio may play at wrong speed/pitch. Resampling not yet implemented.",
                audioId,
                vorbisReader.Format.SampleRate,
                TargetSampleRate
            );

        // Create looping provider
        var (loopStart, loopEnd) = CalculateLoopPoints(definition);
        var loopingProvider = new LoopingSampleProvider(vorbisReader, loopStart, loopEnd);

        // Create playback state
        var finalVolume = CalculateSoundEffectVolume(volume);
        var volumeProvider = new VolumeSampleProvider(loopingProvider) { Volume = finalVolume };

        // Create output
        var output = new PortAudioOutput(volumeProvider);
        output.Play();

        var instance = new SoundEffectInstance(volume); // Store original values
        var playbackState = new SoundEffectPlaybackState
        {
            AudioId = audioId,
            VorbisReader = vorbisReader,
            Output = output,
            Instance = instance,
            VolumeProvider = volumeProvider,
            BaseVolume = volume, // Store original volume before multipliers
            IsLooping = true,
        };

        lock (_lock)
        {
            instance.IsPlaying = true;
            _soundEffectManager.AddInstance(instance, playbackState);
        }

        _logger.Debug("Started looping sound effect: {AudioId}", audioId);
        return instance;
    }

    /// <summary>
    ///     Stops a sound effect instance.
    /// </summary>
    /// <param name="instance">The sound effect instance to stop.</param>
    public void StopSound(ISoundEffectInstance instance)
    {
        if (_disposed || instance == null)
            return;

        lock (_lock)
        {
            if (
                !_soundEffectManager.TryGetState(instance, out var playbackState)
                || playbackState == null
            )
                return;

            try
            {
                playbackState.Output?.Stop();
                playbackState.Output?.Dispose();
                playbackState.VorbisReader?.Dispose();
                playbackState.Instance.IsPlaying = false;
                _soundEffectManager.RemoveInstance(instance);
                _logger.Debug("Stopped sound effect: {AudioId}", playbackState.AudioId);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error stopping sound effect");
            }
        }
    }

    /// <summary>
    ///     Pauses a sound effect instance.
    /// </summary>
    /// <param name="instance">The sound effect instance to pause.</param>
    public void PauseSound(ISoundEffectInstance instance)
    {
        if (_disposed || instance == null)
            return;

        lock (_lock)
        {
            if (
                !_soundEffectManager.TryGetState(instance, out var playbackState)
                || playbackState == null
            )
                return;

            try
            {
                playbackState.Output?.Pause();
                playbackState.Instance.Pause();
                _logger.Debug("Paused sound effect: {AudioId}", playbackState.AudioId);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error pausing sound effect");
            }
        }
    }

    /// <summary>
    ///     Resumes a sound effect instance.
    /// </summary>
    /// <param name="instance">The sound effect instance to resume.</param>
    public void ResumeSound(ISoundEffectInstance instance)
    {
        if (_disposed || instance == null)
            return;

        lock (_lock)
        {
            if (
                !_soundEffectManager.TryGetState(instance, out var playbackState)
                || playbackState == null
            )
                return;

            try
            {
                playbackState.Output?.Play();
                playbackState.Instance.Resume();
                _logger.Debug("Resumed sound effect: {AudioId}", playbackState.AudioId);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error resuming sound effect");
            }
        }
    }

    /// <summary>
    ///     Updates the volume of a sound effect instance.
    /// </summary>
    /// <param name="instance">The sound effect instance.</param>
    /// <param name="volume">The new volume (0.0 - 1.0).</param>
    public void SetSoundEffectVolume(ISoundEffectInstance instance, float volume)
    {
        if (_disposed || instance == null)
            return;

        lock (_lock)
        {
            if (
                !_soundEffectManager.TryGetState(instance, out var playbackState)
                || playbackState == null
            )
                return;

            try
            {
                var clampedVolume = Math.Clamp(volume, 0f, 1f);
                playbackState.BaseVolume = clampedVolume;
                var finalVolume = CalculateSoundEffectVolume(clampedVolume);
                if (playbackState.VolumeProvider != null)
                    playbackState.VolumeProvider.Volume = finalVolume;
                playbackState.Instance.Volume = finalVolume;
                _logger.Debug(
                    "Updated sound effect volume: {AudioId} to {Volume}",
                    playbackState.AudioId,
                    finalVolume
                );
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error updating sound effect volume");
            }
        }
    }

    /// <summary>
    ///     Updates the pitch of a sound effect instance.
    ///     Note: Pitch adjustment is not yet implemented - this method stores the value for future use.
    /// </summary>
    /// <param name="instance">The sound effect instance.</param>
    /// <param name="pitch">The new pitch adjustment (-1.0 to 1.0).</param>
    public void SetSoundEffectPitch(ISoundEffectInstance instance, float pitch)
    {
        if (_disposed || instance == null)
            return;

        lock (_lock)
        {
            if (
                !_soundEffectManager.TryGetState(instance, out var playbackState)
                || playbackState == null
            )
                return;

            try
            {
                playbackState.Instance.Pitch = Math.Clamp(pitch, -1f, 1f);
                // TODO: Implement pitch adjustment in audio pipeline
                _logger.Debug(
                    "Updated sound effect pitch: {AudioId} to {Pitch} (not yet implemented)",
                    playbackState.AudioId,
                    pitch
                );
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error updating sound effect pitch");
            }
        }
    }

    /// <summary>
    ///     Updates the pan of a sound effect instance.
    ///     Note: Pan adjustment is not yet implemented - this method stores the value for future use.
    /// </summary>
    /// <param name="instance">The sound effect instance.</param>
    /// <param name="pan">The new pan adjustment (-1.0 left to 1.0 right).</param>
    public void SetSoundEffectPan(ISoundEffectInstance instance, float pan)
    {
        if (_disposed || instance == null)
            return;

        lock (_lock)
        {
            if (
                !_soundEffectManager.TryGetState(instance, out var playbackState)
                || playbackState == null
            )
                return;

            try
            {
                playbackState.Instance.Pan = Math.Clamp(pan, -1f, 1f);
                // TODO: Implement pan adjustment in audio pipeline
                _logger.Debug(
                    "Updated sound effect pan: {AudioId} to {Pan} (not yet implemented)",
                    playbackState.AudioId,
                    pan
                );
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error updating sound effect pan");
            }
        }
    }

    /// <summary>
    ///     Plays background music.
    ///     Throws exceptions on failure (fail fast per .cursorrules).
    /// </summary>
    /// <param name="audioId">The audio definition ID.</param>
    /// <param name="loop">Whether the music should loop.</param>
    /// <param name="fadeInDuration">Fade-in duration in seconds (0 = instant).</param>
    /// <exception cref="ObjectDisposedException">Thrown when AudioEngine is disposed.</exception>
    /// <exception cref="ArgumentException">Thrown when audioId is null/empty.</exception>
    /// <exception cref="InvalidOperationException">Thrown when audio definition not found or audio loading fails.</exception>
    /// <exception cref="FileNotFoundException">Thrown when audio file not found.</exception>
    public void PlayMusic(string audioId, bool loop, float fadeInDuration)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(AudioEngine));

        if (string.IsNullOrEmpty(audioId))
            throw new ArgumentException("Audio ID cannot be null or empty.", nameof(audioId));

        // Load VorbisReader from ResourceManager (handles definition lookup and caching)
        // Fail fast - let exceptions propagate
        var vorbisReader = _resourceManager.LoadAudioReader(audioId);

        // Get definition for volume and other properties
        var definition = _modManager.GetDefinition<AudioDefinition>(audioId);
        if (definition == null)
            throw new InvalidOperationException(
                $"Audio definition not found: {audioId}. "
                    + "Ensure the audio is defined in a loaded mod."
            );

        // Validate sample rate
        if (vorbisReader.Format.SampleRate != TargetSampleRate)
            _logger.Warning(
                "Audio file {AudioId} has sample rate {SampleRate} Hz, expected {TargetSampleRate} Hz. "
                    + "Audio may play at wrong speed/pitch. Resampling not yet implemented.",
                audioId,
                vorbisReader.Format.SampleRate,
                TargetSampleRate
            );

        // Create sample provider chain
        ISampleProvider provider = vorbisReader;

        // Add looping if needed
        if (loop)
        {
            var (loopStart, loopEnd) = CalculateLoopPoints(definition);
            provider = new LoopingSampleProvider(vorbisReader, loopStart, loopEnd);
        }

        // Apply volume
        var finalVolume = CalculateMusicVolume(definition.Volume);
        var volumeProvider = new VolumeSampleProvider(provider) { Volume = finalVolume };

        // Create playback state
        var musicState = new MusicPlaybackState
        {
            AudioId = audioId,
            VorbisReader = vorbisReader,
            VolumeProvider = volumeProvider,
            BaseVolume = definition.Volume, // Store original volume before multipliers
            TargetVolume = finalVolume,
            CurrentVolume = fadeInDuration > 0 ? 0f : finalVolume,
            FadeInDuration = fadeInDuration,
            FadeInTimer = 0f,
            FadeOutDuration = definition.FadeOut, // Store fade-out from definition
        };

        lock (_lock)
        {
            var currentMusic = _musicManager.CurrentMusic;
            // If music is already playing and we have a fade duration, do sequential fade
            // (fade out old track, then fade in new track - like oldmonoball's FadeOutAndPlay)
            if (
                currentMusic != null
                && _musicManager.PlaybackState == PlaybackState.Playing
                && fadeInDuration > 0f
            )
            {
                // Store pending track info
                _musicManager.SetPendingMusic(musicState);
                musicState.FadeInDuration = fadeInDuration; // Use fade-in duration for new track

                // Get fade-out duration from current music (use its stored FadeOutDuration, or fallback to fade-in)
                var fadeOutDuration =
                    currentMusic.FadeOutDuration > 0
                        ? currentMusic.FadeOutDuration
                        : fadeInDuration; // Fallback to fade-in duration if no fade-out specified
                currentMusic.FadeOutDuration = fadeOutDuration;
                currentMusic.FadeOutTimer = 0f;
                currentMusic.IsFadingOut = true;
                currentMusic.StartVolume = currentMusic.CurrentVolume;

                _logger.Debug(
                    "Starting sequential fade: fade out {OldAudioId} ({FadeOut}s), then fade in {NewAudioId} ({FadeIn}s)",
                    currentMusic.AudioId,
                    fadeOutDuration,
                    audioId,
                    fadeInDuration
                );
            }
            else
            {
                // Stop current music immediately (or if no music playing)
                StopMusicInternal(0f);

                // Initialize mixer and output if needed
                _musicManager.EnsureMixerAndOutput(volumeProvider.Format);

                // Add to mixer
                _musicManager.AddToMixer(musicState, musicState.CurrentVolume);
                _musicManager.SetCurrentMusic(musicState);

                _logger.Debug(
                    "Started music: {AudioId} (Loop: {Loop}, FadeIn: {FadeIn}s)",
                    audioId,
                    loop,
                    fadeInDuration
                );
            }
        }
    }

    /// <summary>
    ///     Stops background music.
    /// </summary>
    /// <param name="fadeOutDuration">Fade-out duration in seconds (0 = instant).</param>
    public void StopMusic(float fadeOutDuration)
    {
        lock (_lock)
        {
            StopMusicInternal(fadeOutDuration);
        }
    }

    /// <summary>
    ///     Pauses background music.
    /// </summary>
    public void PauseMusic()
    {
        lock (_lock)
        {
            if (_disposed || _musicManager.PlaybackState != PlaybackState.Playing)
                return;

            try
            {
                _musicManager.Pause();
                _logger.Debug("Paused music");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error pausing music");
            }
        }
    }

    /// <summary>
    ///     Resumes background music.
    /// </summary>
    public void ResumeMusic()
    {
        lock (_lock)
        {
            if (_disposed || _musicManager.PlaybackState != PlaybackState.Paused)
                return;

            try
            {
                _musicManager.Resume();
                _logger.Debug("Resumed music");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error resuming music");
            }
        }
    }

    /// <summary>
    ///     Crossfades from current music to new music.
    /// </summary>
    /// <param name="newAudioId">The audio definition ID of the new music track.</param>
    /// <param name="crossfadeDuration">Crossfade duration in seconds.</param>
    /// <param name="loop">Whether the new music should loop.</param>
    public void CrossfadeMusic(string newAudioId, float crossfadeDuration, bool loop)
    {
        if (_disposed || string.IsNullOrEmpty(newAudioId))
            return;

        lock (_lock)
        {
            // If no current music, just play normally
            var currentMusic = _musicManager.CurrentMusic;
            if (currentMusic == null || _musicManager.PlaybackState != PlaybackState.Playing)
            {
                PlayMusic(newAudioId, loop, crossfadeDuration);
                return;
            }

            // Start crossfade
            // Load VorbisReader from ResourceManager (handles definition lookup and caching)
            // Fail fast - let exceptions propagate
            var vorbisReader = _resourceManager.LoadAudioReader(newAudioId);

            // Get definition for volume and other properties
            var definition = _modManager.GetDefinition<AudioDefinition>(newAudioId);
            if (definition == null)
                throw new InvalidOperationException(
                    $"Audio definition not found: {newAudioId}. "
                        + "Ensure the audio is defined in a loaded mod."
                );

            // Validate sample rate
            if (vorbisReader.Format.SampleRate != TargetSampleRate)
                _logger.Warning(
                    "Audio file {AudioId} has sample rate {SampleRate} Hz, expected {TargetSampleRate} Hz. "
                        + "Audio may play at wrong speed/pitch. Resampling not yet implemented.",
                    newAudioId,
                    vorbisReader.Format.SampleRate,
                    TargetSampleRate
                );

            // Create sample provider chain for new track
            ISampleProvider provider = vorbisReader;
            if (loop)
            {
                var (loopStart, loopEnd) = CalculateLoopPoints(definition);
                provider = new LoopingSampleProvider(vorbisReader, loopStart, loopEnd);
            }

            var finalVolume = CalculateMusicVolume(definition.Volume);
            var volumeProvider = new VolumeSampleProvider(provider) { Volume = 0f }; // Start at 0

            // Create crossfade state
            var crossfadeState = new MusicPlaybackState
            {
                AudioId = newAudioId,
                VorbisReader = vorbisReader,
                VolumeProvider = volumeProvider,
                BaseVolume = definition.Volume, // Store original volume before multipliers
                TargetVolume = finalVolume,
                CurrentVolume = 0f,
                FadeInDuration = crossfadeDuration,
                FadeInTimer = 0f,
                IsCrossfading = true,
            };

            // Start fading out current music
            currentMusic.FadeOutDuration = crossfadeDuration;
            currentMusic.FadeOutTimer = 0f;
            currentMusic.IsFadingOut = true;
            currentMusic.StartVolume = currentMusic.CurrentVolume;

            // Ensure mixer exists
            _musicManager.EnsureMixerAndOutput(volumeProvider.Format);

            // Add crossfade track to mixer
            _musicManager.AddToMixer(crossfadeState, 0f);
            _musicManager.SetCrossfadeMusic(crossfadeState);

            _logger.Debug(
                "Started crossfade from {OldAudioId} to {NewAudioId} (duration: {Duration}s)",
                currentMusic.AudioId,
                newAudioId,
                crossfadeDuration
            );
        }
    }

    /// <summary>
    ///     Gets or sets the master volume (0.0 - 1.0).
    /// </summary>
    public float MasterVolume
    {
        get => _masterVolume;
        set
        {
            _masterVolume = Math.Clamp(value, 0f, 1f);
            UpdateActivePlaybackVolumes();
        }
    }

    /// <summary>
    ///     Gets or sets the music volume (0.0 - 1.0).
    /// </summary>
    public float MusicVolume
    {
        get => _musicVolume;
        set
        {
            _musicVolume = Math.Clamp(value, 0f, 1f);
            UpdateActivePlaybackVolumes();
        }
    }

    /// <summary>
    ///     Gets or sets the sound effect volume (0.0 - 1.0).
    /// </summary>
    public float SoundEffectVolume
    {
        get => _soundEffectVolume;
        set
        {
            _soundEffectVolume = Math.Clamp(value, 0f, 1f);
            UpdateActivePlaybackVolumes();
        }
    }

    /// <summary>
    ///     Updates the audio engine. Should be called every frame.
    /// </summary>
    /// <param name="deltaTime">Time elapsed since last update in seconds.</param>
    public void Update(float deltaTime)
    {
        if (_disposed)
            return;

        lock (_lock)
        {
            // Update music fade in/out
            var currentMusic = _musicManager.CurrentMusic;
            if (currentMusic != null)
            {
                AudioFadeManager.UpdateFadeIn(currentMusic, deltaTime);
                if (AudioFadeManager.UpdateFadeOut(currentMusic, deltaTime))
                {
                    // Fade-out complete
                    var format = currentMusic.VolumeProvider?.Format;
                    if (format != null)
                        _musicManager.CompleteFadeOut(currentMusic, format);
                }
            }

            // Update crossfade
            var crossfadeMusic = _musicManager.CrossfadeMusic;
            if (crossfadeMusic != null)
            {
                AudioFadeManager.UpdateFadeIn(crossfadeMusic, deltaTime);
                AudioFadeManager.UpdateFadeOut(crossfadeMusic, deltaTime);

                // If crossfade is complete, promote crossfade track to current
                // Check both volume and fade-in timer to ensure fade-in is actually complete
                if (
                    crossfadeMusic.FadeInTimer >= crossfadeMusic.FadeInDuration
                    && crossfadeMusic.CurrentVolume >= crossfadeMusic.TargetVolume
                    && currentMusic != null
                )
                {
                    // Remove old track
                    _musicManager.RemoveFromMixer(currentMusic);
                    currentMusic.VorbisReader?.Dispose();

                    // Promote crossfade track
                    crossfadeMusic.IsCrossfading = false;
                    _musicManager.SetCurrentMusic(crossfadeMusic);
                    _musicManager.SetCrossfadeMusic(null);

                    _logger.Debug("Crossfade completed to: {AudioId}", crossfadeMusic.AudioId);
                }
            }

            // Clean up stopped sound effects
            var stoppedInstances = _soundEffectManager.GetStoppedInstances();
            foreach (var instance in stoppedInstances)
                if (_soundEffectManager.TryGetState(instance, out var state) && state != null)
                {
                    state.Output?.Dispose();
                    state.VorbisReader?.Dispose();
                    _soundEffectManager.RemoveInstance(instance);
                }
        }
    }

    /// <summary>
    ///     Disposes the audio engine and releases all resources.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        lock (_lock)
        {
            _disposed = true;

            // Stop all sound effects
            _soundEffectManager.DisposeAll();

            // Stop music
            _musicManager.Dispose();

            _logger.Debug("AudioEngine disposed");
        }

        GC.SuppressFinalize(this);
    }

    private void StopMusicInternal(float fadeOutDuration)
    {
        var currentMusic = _musicManager.CurrentMusic;
        if (currentMusic == null)
            return;

        if (fadeOutDuration > 0f && _musicManager.PlaybackState == PlaybackState.Playing)
        {
            // Start fade out
            currentMusic.FadeOutDuration = fadeOutDuration;
            currentMusic.FadeOutTimer = 0f;
            currentMusic.IsFadingOut = true;
        }
        else
        {
            // Immediate stop
            _musicManager.RemoveFromMixer(currentMusic);
            currentMusic.VorbisReader?.Dispose();
            _musicManager.SetCurrentMusic(null);
        }
    }

    // GetAudioDefinitionAndManifest removed - ResourceManager handles definition lookup internally

    /// <summary>
    ///     Calculates loop points from an audio definition.
    /// </summary>
    /// <param name="definition">The audio definition.</param>
    /// <returns>A tuple containing loop start and loop end sample positions.</returns>
    private static (long? loopStart, long? loopEnd) CalculateLoopPoints(AudioDefinition definition)
    {
        long? loopStart = definition.LoopStartSamples;
        var loopEnd =
            loopStart.HasValue && definition.LoopLengthSamples.HasValue
                ? loopStart + definition.LoopLengthSamples
                : null;
        return (loopStart, loopEnd);
    }

    /// <summary>
    ///     Calculates the final volume for a sound effect.
    /// </summary>
    /// <param name="baseVolume">The base volume (0.0 - 1.0).</param>
    /// <returns>The final volume after applying SoundEffectVolume and MasterVolume multipliers.</returns>
    private float CalculateSoundEffectVolume(float baseVolume)
    {
        return baseVolume * SoundEffectVolume * MasterVolume;
    }

    /// <summary>
    ///     Calculates the final volume for music.
    /// </summary>
    /// <param name="definitionVolume">The volume from the audio definition (0.0 - 1.0).</param>
    /// <returns>The final volume after applying MusicVolume and MasterVolume multipliers.</returns>
    private float CalculateMusicVolume(float definitionVolume)
    {
        return definitionVolume * MusicVolume * MasterVolume;
    }

    /// <summary>
    ///     Updates volumes for all active playback (music and sound effects).
    ///     Called when MasterVolume, MusicVolume, or SoundEffectVolume changes.
    /// </summary>
    private void UpdateActivePlaybackVolumes()
    {
        lock (_lock)
        {
            // Update music volumes
            var currentMusic = _musicManager.CurrentMusic;
            if (currentMusic != null && currentMusic.VolumeProvider != null)
            {
                var newTargetVolume = CalculateMusicVolume(currentMusic.BaseVolume);
                currentMusic.TargetVolume = newTargetVolume;
                // Update current volume if not fading
                if (currentMusic.FadeInDuration == 0f && !currentMusic.IsFadingOut)
                {
                    currentMusic.CurrentVolume = newTargetVolume;
                    currentMusic.VolumeProvider.Volume = newTargetVolume;
                    if (currentMusic.MixerInput != null)
                        currentMusic.MixerInput.Volume = newTargetVolume;
                }
            }

            var crossfadeMusic = _musicManager.CrossfadeMusic;
            if (crossfadeMusic != null && crossfadeMusic.VolumeProvider != null)
            {
                var newTargetVolume = CalculateMusicVolume(crossfadeMusic.BaseVolume);
                crossfadeMusic.TargetVolume = newTargetVolume;
                // Update current volume if not fading
                if (crossfadeMusic.FadeInDuration == 0f && !crossfadeMusic.IsFadingOut)
                {
                    crossfadeMusic.CurrentVolume = newTargetVolume;
                    crossfadeMusic.VolumeProvider.Volume = newTargetVolume;
                    if (crossfadeMusic.MixerInput != null)
                        crossfadeMusic.MixerInput.Volume = newTargetVolume;
                }
            }

            var pendingMusic = _musicManager.PendingMusic;
            if (pendingMusic != null && pendingMusic.VolumeProvider != null)
            {
                var newTargetVolume = CalculateMusicVolume(pendingMusic.BaseVolume);
                pendingMusic.TargetVolume = newTargetVolume;
                // Update current volume if not fading
                if (pendingMusic.FadeInDuration == 0f && !pendingMusic.IsFadingOut)
                {
                    pendingMusic.CurrentVolume = newTargetVolume;
                    pendingMusic.VolumeProvider.Volume = newTargetVolume;
                }
            }

            // Update sound effect volumes
            foreach (var kvp in _soundEffectManager.GetAllInstances())
            {
                var state = kvp.Value;
                if (state.VolumeProvider != null)
                {
                    var newVolume = CalculateSoundEffectVolume(state.BaseVolume);
                    state.VolumeProvider.Volume = newVolume;
                    // Update instance volume to reflect final volume (after multipliers)
                    state.Instance.Volume = newVolume;
                }
            }
        }
    }
}
