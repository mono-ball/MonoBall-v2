namespace MonoBall.Core.Audio.Core;

/// <summary>
///     Interface for providing audio samples in floating-point format.
///     Cross-platform audio sample provider for PortAudio-based playback.
/// </summary>
public interface ISampleProvider
{
    /// <summary>
    ///     Gets the audio format of the samples.
    /// </summary>
    AudioFormat Format { get; }

    /// <summary>
    ///     Reads samples from this provider into the buffer.
    /// </summary>
    /// <param name="buffer">Destination buffer for float samples.</param>
    /// <param name="offset">Offset into the buffer to start writing.</param>
    /// <param name="count">Number of samples to read.</param>
    /// <returns>Number of samples actually read (may be less than count at end of stream).</returns>
    int Read(float[] buffer, int offset, int count);
}

/// <summary>
///     Extension of ISampleProvider that supports seeking.
/// </summary>
public interface ISeekableSampleProvider : ISampleProvider
{
    /// <summary>
    ///     Gets the total number of samples (interleaved) in the stream.
    /// </summary>
    long TotalSamples { get; }

    /// <summary>
    ///     Gets the current position in samples (interleaved).
    /// </summary>
    long Position { get; }

    /// <summary>
    ///     Seeks to a specific sample position.
    /// </summary>
    /// <param name="samplePosition">Target position in samples (interleaved).</param>
    void SeekToSample(long samplePosition);
}
