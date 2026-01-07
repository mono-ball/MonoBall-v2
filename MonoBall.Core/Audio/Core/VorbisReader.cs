using System;
using System.IO;

namespace MonoBall.Core.Audio.Core;

/// <summary>
///     OGG Vorbis audio file reader using NVorbis.
///     Provides streaming audio data from OGG files in float format.
///     Thread-safe for concurrent Read() calls.
/// </summary>
public class VorbisReader : ISeekableSampleProvider, IDisposable
{
    private readonly NVorbis.VorbisReader _reader;
    private readonly object _readLock = new();
    private bool _disposed;

    /// <summary>
    ///     Creates a new Vorbis reader for the specified file.
    /// </summary>
    /// <param name="filePath">Path to the OGG Vorbis file.</param>
    public VorbisReader(string filePath)
    {
        if (string.IsNullOrEmpty(filePath))
            throw new ArgumentNullException(nameof(filePath));

        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Audio file not found: {filePath}", filePath);

        _reader = new NVorbis.VorbisReader(filePath);
        Format = new AudioFormat(_reader.SampleRate, _reader.Channels);
    }

    /// <summary>
    ///     Creates a new Vorbis reader from a stream.
    /// </summary>
    /// <param name="stream">Stream containing OGG Vorbis data.</param>
    /// <param name="closeOnDispose">Whether to close the stream when this reader is disposed.</param>
    public VorbisReader(Stream stream, bool closeOnDispose = true)
    {
        ArgumentNullException.ThrowIfNull(stream);

        _reader = new NVorbis.VorbisReader(stream, closeOnDispose);
        Format = new AudioFormat(_reader.SampleRate, _reader.Channels);
    }

    /// <summary>
    ///     Gets the total duration of the audio.
    /// </summary>
    public TimeSpan TotalTime => _reader.TotalTime;

    /// <summary>
    ///     Gets the current time position.
    /// </summary>
    public TimeSpan TimePosition
    {
        get
        {
            lock (_readLock)
            {
                return _disposed ? TimeSpan.Zero : _reader.TimePosition;
            }
        }
    }

    /// <summary>
    ///     Disposes the reader and releases resources.
    /// </summary>
    public void Dispose()
    {
        lock (_readLock)
        {
            if (_disposed)
                return;

            _disposed = true;
            _reader?.Dispose();
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>
    ///     Gets the audio format.
    /// </summary>
    public AudioFormat Format { get; }

    /// <summary>
    ///     Gets the total number of samples (interleaved).
    /// </summary>
    public long TotalSamples => _reader.TotalSamples * Format.Channels;

    /// <summary>
    ///     Gets the current position in samples (interleaved).
    /// </summary>
    public long Position
    {
        get
        {
            lock (_readLock)
            {
                if (_disposed)
                    return 0;

                return _reader.SamplePosition * Format.Channels;
            }
        }
    }

    /// <summary>
    ///     Reads samples from the Vorbis stream.
    ///     Thread-safe.
    /// </summary>
    /// <param name="buffer">Destination buffer for float samples.</param>
    /// <param name="offset">Offset into the buffer.</param>
    /// <param name="count">Number of samples to read.</param>
    /// <returns>Number of samples actually read.</returns>
    public int Read(float[] buffer, int offset, int count)
    {
        lock (_readLock)
        {
            if (_disposed)
                return 0;

            try
            {
                return _reader.ReadSamples(buffer, offset, count);
            }
            catch (ObjectDisposedException)
            {
                return 0;
            }
        }
    }

    /// <summary>
    ///     Seeks to a specific sample position.
    ///     Thread-safe.
    /// </summary>
    /// <param name="samplePosition">Target position in samples (interleaved).</param>
    public void SeekToSample(long samplePosition)
    {
        lock (_readLock)
        {
            if (_disposed)
                return;

            // Convert interleaved sample position to per-channel sample position
            var perChannelPosition = samplePosition / Format.Channels;

            // Clamp to valid range
            perChannelPosition = Math.Max(0, Math.Min(perChannelPosition, _reader.TotalSamples));

            _reader.SamplePosition = perChannelPosition;
        }
    }

    /// <summary>
    ///     Seeks to a specific time position.
    ///     Thread-safe.
    /// </summary>
    /// <param name="time">Target time position.</param>
    public void SeekToTime(TimeSpan time)
    {
        lock (_readLock)
        {
            if (_disposed)
                return;

            // Clamp to valid range
            if (time < TimeSpan.Zero)
                time = TimeSpan.Zero;
            else if (time > _reader.TotalTime)
                time = _reader.TotalTime;

            _reader.TimePosition = time;
        }
    }

    /// <summary>
    ///     Resets the reader to the beginning.
    /// </summary>
    public void Reset()
    {
        SeekToSample(0);
    }
}
