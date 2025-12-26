using System;
using System.Collections.Generic;

namespace MonoBall.Core.Audio.Core
{
    /// <summary>
    /// Mixes multiple audio sources into a single output stream.
    /// Thread-safe for adding/removing sources during playback.
    /// </summary>
    public class AudioMixer : ISampleProvider, IDisposable
    {
        private readonly object _sourceLock = new();
        private readonly List<MixerInput> _sources = [];
        private bool _disposed;
        private float[]? _mixBuffer;

        /// <summary>
        /// Initializes a new instance of the AudioMixer class.
        /// </summary>
        /// <param name="format">The audio format for the mixer output.</param>
        public AudioMixer(AudioFormat format)
        {
            Format = format ?? throw new ArgumentNullException(nameof(format));
        }

        /// <summary>
        /// Gets the number of active sources currently being mixed.
        /// </summary>
        public int SourceCount
        {
            get
            {
                lock (_sourceLock)
                {
                    return _sources.Count;
                }
            }
        }

        /// <summary>
        /// Disposes the mixer and releases resources.
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            lock (_sourceLock)
            {
                _sources.Clear();
            }

            _mixBuffer = null;

            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Gets the audio format of the mixer output.
        /// </summary>
        public AudioFormat Format { get; }

        /// <summary>
        /// Reads mixed audio samples from all active sources.
        /// </summary>
        /// <param name="buffer">The buffer to fill with mixed samples.</param>
        /// <param name="offset">The offset in the buffer to start writing.</param>
        /// <param name="count">The number of samples to read.</param>
        /// <returns>The number of samples read.</returns>
        public int Read(float[] buffer, int offset, int count)
        {
            if (_disposed)
            {
                return 0;
            }

            ArgumentNullException.ThrowIfNull(buffer);
            ArgumentOutOfRangeException.ThrowIfNegative(offset);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(offset, buffer.Length);
            ArgumentOutOfRangeException.ThrowIfNegative(count);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(offset + count, buffer.Length);

            // Clear output buffer
            Array.Clear(buffer, offset, count);

            // Ensure mix buffer is large enough
            if (_mixBuffer == null || _mixBuffer.Length < count)
            {
                _mixBuffer = new float[count];
            }

            List<MixerInput> sourcesToRemove = [];
            int samplesRead = 0;

            lock (_sourceLock)
            {
                foreach (MixerInput input in _sources)
                {
                    // Clear mix buffer for this source
                    Array.Clear(_mixBuffer, 0, count);

                    // Read from source
                    int sourceSamples = input.Source.Read(_mixBuffer, 0, count);

                    if (sourceSamples > 0)
                    {
                        // Mix into output buffer with volume adjustment
                        float volume = input.Volume;
                        for (int i = 0; i < sourceSamples; i++)
                        {
                            buffer[offset + i] += _mixBuffer[i] * volume;
                        }

                        samplesRead = Math.Max(samplesRead, sourceSamples);
                    }
                    else
                    {
                        // Source has finished, mark for removal
                        sourcesToRemove.Add(input);
                    }
                }

                // Remove finished sources
                foreach (MixerInput input in sourcesToRemove)
                {
                    _sources.Remove(input);
                }
            }

            // Apply soft clipping to prevent hard clipping artifacts
            for (int i = offset; i < offset + samplesRead; i++)
            {
                if (buffer[i] > 1.0f)
                {
                    buffer[i] = 1.0f;
                }
                else if (buffer[i] < -1.0f)
                {
                    buffer[i] = -1.0f;
                }
            }

            return samplesRead;
        }

        /// <summary>
        /// Adds an audio source to the mixer.
        /// </summary>
        /// <param name="source">The sample provider to mix.</param>
        /// <param name="volume">The volume level (0.0 to 1.0).</param>
        /// <returns>A handle that can be used to remove or adjust the source.</returns>
        /// <exception cref="ArgumentNullException">Thrown when source is null.</exception>
        /// <exception cref="ArgumentException">Thrown when source format doesn't match mixer format.</exception>
        /// <exception cref="ObjectDisposedException">Thrown when mixer is disposed.</exception>
        public MixerInput AddSource(ISampleProvider source, float volume = 1.0f)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            ArgumentNullException.ThrowIfNull(source);

            if (!source.Format.Equals(Format))
            {
                throw new ArgumentException(
                    $"Source format ({source.Format}) does not match mixer format ({Format})",
                    nameof(source)
                );
            }

            var input = new MixerInput(source, volume);

            lock (_sourceLock)
            {
                _sources.Add(input);
            }

            return input;
        }

        /// <summary>
        /// Removes a source from the mixer.
        /// </summary>
        /// <param name="input">The mixer input to remove.</param>
        /// <returns>True if the source was removed; false if it wasn't found.</returns>
        public bool RemoveSource(MixerInput input)
        {
            if (input == null)
            {
                return false;
            }

            lock (_sourceLock)
            {
                return _sources.Remove(input);
            }
        }

        /// <summary>
        /// Removes all sources from the mixer.
        /// </summary>
        public void ClearSources()
        {
            lock (_sourceLock)
            {
                _sources.Clear();
            }
        }

        /// <summary>
        /// Represents an input source in the mixer with volume control.
        /// </summary>
        public class MixerInput
        {
            private float _volume;

            internal MixerInput(ISampleProvider source, float volume)
            {
                Source = source;
                _volume = Math.Clamp(volume, 0.0f, 1.0f);
            }

            /// <summary>
            /// Gets the underlying sample provider.
            /// </summary>
            public ISampleProvider Source { get; }

            /// <summary>
            /// Gets or sets the volume level (0.0 to 1.0).
            /// </summary>
            public float Volume
            {
                get => _volume;
                set => _volume = Math.Clamp(value, 0.0f, 1.0f);
            }
        }
    }
}
