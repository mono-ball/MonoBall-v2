using System;

namespace MonoBall.Core.Audio.Core;

/// <summary>
///     Sample provider that applies volume scaling to audio samples.
///     Thread-safe volume adjustments during playback.
/// </summary>
public class VolumeSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private volatile float _volume = 1.0f;

    /// <summary>
    ///     Creates a new volume sample provider.
    /// </summary>
    /// <param name="source">The source sample provider.</param>
    public VolumeSampleProvider(ISampleProvider source)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
    }

    /// <summary>
    ///     Gets or sets the volume multiplier (0.0 to 1.0).
    ///     Thread-safe; changes take effect immediately on the next read.
    /// </summary>
    public float Volume
    {
        get => _volume;
        set => _volume = Math.Clamp(value, 0f, 1f);
    }

    /// <summary>
    ///     Gets the audio format (same as source).
    /// </summary>
    public AudioFormat Format => _source.Format;

    /// <summary>
    ///     Reads samples from the source and applies volume scaling.
    /// </summary>
    public int Read(float[] buffer, int offset, int count)
    {
        var samplesRead = _source.Read(buffer, offset, count);

        var vol = _volume;
        if (Math.Abs(vol - 1.0f) > 0.0001f) // Skip if volume is ~1.0
            for (var i = 0; i < samplesRead; i++)
                buffer[offset + i] *= vol;

        return samplesRead;
    }
}
