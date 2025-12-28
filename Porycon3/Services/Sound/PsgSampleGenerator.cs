namespace Porycon3.Services.Sound;

/// <summary>
/// Generates synthetic WAV samples for GBA PSG (Programmable Sound Generator) voices.
/// These are the Game Boy-derived sound channels: square waves, programmable waves, and noise.
/// </summary>
public class PsgSampleGenerator
{
    // Sample rate for generated PSG samples
    // Using 13379 Hz to match authentic GBA hardware sample rate
    // This produces the characteristic "crunchy" GBA sound with proper aliasing
    private const int SampleRate = 13379;

    // One second of audio for looping samples
    private const int SampleLength = SampleRate;

    /// <summary>
    /// Generate a square wave sample with specified duty cycle.
    /// </summary>
    /// <param name="dutyCycle">The duty cycle (12.5%, 25%, 50%, 75%)</param>
    /// <param name="frequency">Base frequency in Hz (default 440 = A4)</param>
    /// <returns>8-bit unsigned PCM sample data</returns>
    // PSG amplitude: ±64 from center (64-192 range)
    // This matches the natural GBA PSG output level (4-bit volume = ~1/4 of 8-bit range)
    // DirectSound samples are normalized to match this level for balanced mixing
    private const byte PsgHigh = 192;
    private const byte PsgLow = 64;

    public byte[] GenerateSquareWave(DutyCycle dutyCycle, double frequency = 440.0)
    {
        var samples = new byte[SampleLength];
        var dutyCycleRatio = GetDutyCycleRatio(dutyCycle);
        var samplesPerCycle = SampleRate / frequency;

        for (var i = 0; i < SampleLength; i++)
        {
            var positionInCycle = (i % samplesPerCycle) / samplesPerCycle;
            // GBA square waves are harsh digital: either high or low
            samples[i] = positionInCycle < dutyCycleRatio ? PsgHigh : PsgLow;
        }

        return samples;
    }

    /// <summary>
    /// Generate a programmable wave sample from 32 4-bit samples.
    /// </summary>
    /// <param name="waveData">32 4-bit samples (0-15 each)</param>
    /// <param name="frequency">Base frequency in Hz</param>
    /// <returns>8-bit unsigned PCM sample data</returns>
    public byte[] GenerateProgrammableWave(byte[] waveData, double frequency = 440.0)
    {
        if (waveData.Length != 32)
        {
            throw new ArgumentException("Programmable wave must have exactly 32 samples", nameof(waveData));
        }

        var samples = new byte[SampleLength];
        var samplesPerCycle = SampleRate / frequency;

        for (var i = 0; i < SampleLength; i++)
        {
            var positionInCycle = (i % samplesPerCycle) / samplesPerCycle;
            var waveIndex = (int)(positionInCycle * 32) % 32;

            // Convert 4-bit (0-15) to 8-bit unsigned (0-255)
            // Center around 128 for proper audio
            var sample4Bit = waveData[waveIndex] & 0x0F;
            samples[i] = (byte)(sample4Bit * 16 + 8);
        }

        return samples;
    }

    /// <summary>
    /// Generate noise sample using Linear Feedback Shift Register (LFSR).
    /// </summary>
    /// <param name="shortPeriod">If true, use 7-bit LFSR (more tonal), else 15-bit (white noise)</param>
    /// <returns>8-bit unsigned PCM sample data</returns>
    public byte[] GenerateNoise(bool shortPeriod = false)
    {
        var samples = new byte[SampleLength];

        // GBA uses a 15-bit or 7-bit LFSR for noise generation
        // Short period (7-bit) produces more tonal/metallic sounds
        // Long period (15-bit) produces white noise
        ushort lfsr = 0x7FFF;
        var lfsrMask = shortPeriod ? 0x7F : 0x7FFF;
        var xorBit = shortPeriod ? 6 : 14;

        // Step LFSR at approximately 262kHz / divider
        // For simplicity, we step every few samples
        var stepInterval = shortPeriod ? 2 : 4;

        for (var i = 0; i < SampleLength; i++)
        {
            // Output based on bit 0 of LFSR
            samples[i] = (lfsr & 1) == 1 ? PsgHigh : PsgLow;

            if (i % stepInterval == 0)
            {
                // LFSR feedback: XOR bits 0 and 1 (or bit at xorBit for short)
                var feedback = ((lfsr >> 0) ^ (lfsr >> 1)) & 1;
                lfsr = (ushort)((lfsr >> 1) | (feedback << xorBit));
                lfsr &= (ushort)lfsrMask;

                // Prevent stuck state
                if (lfsr == 0) lfsr = (ushort)lfsrMask;
            }
        }

        return samples;
    }

    /// <summary>
    /// Generate all standard square wave samples.
    /// </summary>
    /// <returns>Dictionary of duty cycle to sample data</returns>
    public Dictionary<DutyCycle, byte[]> GenerateAllSquareWaves()
    {
        return new Dictionary<DutyCycle, byte[]>
        {
            { DutyCycle.Duty12_5, GenerateSquareWave(DutyCycle.Duty12_5) },
            { DutyCycle.Duty25, GenerateSquareWave(DutyCycle.Duty25) },
            { DutyCycle.Duty50, GenerateSquareWave(DutyCycle.Duty50) },
            { DutyCycle.Duty75, GenerateSquareWave(DutyCycle.Duty75) },
        };
    }

    /// <summary>
    /// Generate both noise variants.
    /// </summary>
    /// <returns>Dictionary of period type to sample data</returns>
    public Dictionary<bool, byte[]> GenerateAllNoiseVariants()
    {
        return new Dictionary<bool, byte[]>
        {
            { false, GenerateNoise(false) }, // Long period (white noise)
            { true, GenerateNoise(true) },   // Short period (tonal)
        };
    }

    /// <summary>
    /// Write sample data to a WAV file.
    /// </summary>
    public void WriteWav(string path, byte[] samples, int sampleRate = SampleRate)
    {
        using var fs = File.Create(path);
        using var bw = new BinaryWriter(fs);

        // WAV header
        bw.Write("RIFF"u8);
        bw.Write(36 + samples.Length); // File size - 8
        bw.Write("WAVE"u8);

        // Format chunk
        bw.Write("fmt "u8);
        bw.Write(16);           // Chunk size
        bw.Write((short)1);     // PCM format
        bw.Write((short)1);     // Mono
        bw.Write(sampleRate);   // Sample rate
        bw.Write(sampleRate);   // Byte rate (sample rate * channels * bytes per sample)
        bw.Write((short)1);     // Block align
        bw.Write((short)8);     // Bits per sample

        // Data chunk
        bw.Write("data"u8);
        bw.Write(samples.Length);
        bw.Write(samples);
    }

    /// <summary>
    /// Parse programmable wave data from pokeemerald format.
    /// Format: 16 bytes where each byte contains 2 4-bit samples.
    /// </summary>
    public static byte[] ParseProgrammableWaveData(byte[] rawData)
    {
        if (rawData.Length != 16)
        {
            throw new ArgumentException("Raw wave data must be 16 bytes", nameof(rawData));
        }

        var samples = new byte[32];
        for (var i = 0; i < 16; i++)
        {
            samples[i * 2] = (byte)((rawData[i] >> 4) & 0x0F);
            samples[i * 2 + 1] = (byte)(rawData[i] & 0x0F);
        }

        return samples;
    }

    /// <summary>
    /// Get standard programmable wave patterns used in GBA games.
    /// These are common waveforms found in pokeemerald.
    /// </summary>
    public static Dictionary<string, byte[]> GetStandardWavePatterns()
    {
        return new Dictionary<string, byte[]>
        {
            // Sine-ish wave (smooth)
            ["sine"] = new byte[]
            {
                8, 9, 10, 11, 12, 13, 14, 15,
                15, 14, 13, 12, 11, 10, 9, 8,
                7, 6, 5, 4, 3, 2, 1, 0,
                0, 1, 2, 3, 4, 5, 6, 7
            },

            // Triangle wave
            ["triangle"] = new byte[]
            {
                0, 1, 2, 3, 4, 5, 6, 7,
                8, 9, 10, 11, 12, 13, 14, 15,
                15, 14, 13, 12, 11, 10, 9, 8,
                7, 6, 5, 4, 3, 2, 1, 0
            },

            // Sawtooth wave
            ["sawtooth"] = new byte[]
            {
                0, 0, 1, 1, 2, 2, 3, 3,
                4, 4, 5, 5, 6, 6, 7, 7,
                8, 8, 9, 9, 10, 10, 11, 11,
                12, 12, 13, 13, 14, 14, 15, 15
            },

            // Organ-like wave
            ["organ"] = new byte[]
            {
                8, 11, 13, 14, 15, 14, 13, 11,
                8, 5, 3, 2, 1, 2, 3, 5,
                8, 10, 12, 13, 14, 13, 12, 10,
                8, 6, 4, 3, 2, 3, 4, 6
            },
        };
    }

    private static double GetDutyCycleRatio(DutyCycle dutyCycle)
    {
        return dutyCycle switch
        {
            DutyCycle.Duty12_5 => 0.125,
            DutyCycle.Duty25 => 0.25,
            DutyCycle.Duty50 => 0.5,
            DutyCycle.Duty75 => 0.75,
            _ => 0.5
        };
    }
}
