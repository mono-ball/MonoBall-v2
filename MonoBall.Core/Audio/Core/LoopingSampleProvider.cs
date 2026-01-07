using System;

namespace MonoBall.Core.Audio.Core;

/// <summary>
///     Sample provider that loops audio with optional custom loop points.
/// </summary>
public class LoopingSampleProvider : ISampleProvider
{
    private readonly object _lock = new();
    private readonly long? _loopEndSamples;
    private readonly long? _loopStartSamples;
    private readonly ISeekableSampleProvider _source;

    /// <summary>
    ///     Creates a new looping sample provider.
    /// </summary>
    /// <param name="source">The seekable source sample provider.</param>
    /// <param name="loopStartSamples">Loop start position in samples (interleaved), or null to loop from beginning.</param>
    /// <param name="loopEndSamples">Loop end position in samples (interleaved), or null to loop to end.</param>
    public LoopingSampleProvider(
        ISeekableSampleProvider source,
        long? loopStartSamples = null,
        long? loopEndSamples = null
    )
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _loopStartSamples = loopStartSamples;
        _loopEndSamples = loopEndSamples;
    }

    /// <summary>
    ///     Gets the audio format (same as source).
    /// </summary>
    public AudioFormat Format => _source.Format;

    /// <summary>
    ///     Reads samples with looping support.
    /// </summary>
    public int Read(float[] buffer, int offset, int count)
    {
        lock (_lock)
        {
            var totalSamplesRead = 0;

            while (totalSamplesRead < count)
            {
                var currentPosition = _source.Position;
                var loopEnd = _loopEndSamples ?? _source.TotalSamples;
                var loopStart = _loopStartSamples ?? 0;

                // Check if we've reached the loop end
                if (currentPosition >= loopEnd)
                {
                    // Seek to loop start
                    _source.SeekToSample(loopStart);
                    currentPosition = loopStart;
                }

                // Calculate how many samples we can read before hitting loop end
                var samplesUntilLoopEnd = loopEnd - currentPosition;
                var samplesToRead = (int)Math.Min(samplesUntilLoopEnd, count - totalSamplesRead);

                // Read samples from source
                var samplesRead = _source.Read(buffer, offset + totalSamplesRead, samplesToRead);

                if (samplesRead == 0)
                {
                    // End of stream, seek to loop start
                    _source.SeekToSample(loopStart);
                    continue;
                }

                totalSamplesRead += samplesRead;

                // If we hit the loop end exactly, continue looping
                if (_source.Position >= loopEnd)
                    _source.SeekToSample(loopStart);
            }

            return totalSamplesRead;
        }
    }
}
