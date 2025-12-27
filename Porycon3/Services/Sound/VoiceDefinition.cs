namespace Porycon3.Services.Sound;

/// <summary>
/// Types of voices in the GBA m4a sound engine.
/// </summary>
public enum VoiceType
{
    DirectSound = 0x00,      // PCM sample playback
    Square1 = 0x01,          // Square wave with sweep
    Square2 = 0x02,          // Square wave without sweep
    ProgrammableWave = 0x03, // Custom 4-bit waveform
    Noise = 0x04,            // LFSR noise generator
    Keysplit = 0x40,         // Routes note ranges to different voices
    KeysplitAll = 0x80,      // Each note maps to different voice (drums)
}

/// <summary>
/// Square wave duty cycle options.
/// </summary>
public enum DutyCycle
{
    Duty12_5 = 0,  // 12.5%
    Duty25 = 1,    // 25%
    Duty50 = 2,    // 50%
    Duty75 = 3,    // 75%
}

/// <summary>
/// ADSR envelope parameters for a voice.
/// GBA uses 0-255 scale for attack/decay/sustain/release.
/// </summary>
public record VoiceEnvelope(
    int Attack,   // 0-255, lower = slower attack
    int Decay,    // 0-255, decay rate
    int Sustain,  // 0-255, sustain level
    int Release   // 0-255, release rate
);

/// <summary>
/// Base voice definition from voicegroup files.
/// </summary>
public abstract record VoiceDefinition
{
    public required VoiceType Type { get; init; }
    public required int BaseMidiKey { get; init; } // Root pitch (usually 60 = C3)
    public required int Pan { get; init; }         // 0-127, 64 = center
    public required VoiceEnvelope Envelope { get; init; }
}

/// <summary>
/// DirectSound voice - plays PCM samples.
/// </summary>
public record DirectSoundVoice : VoiceDefinition
{
    public required string SampleName { get; init; } // e.g., "DirectSoundWaveData_sc88pro_fretless_bass"
}

/// <summary>
/// Square wave voice (channel 1 with sweep).
/// </summary>
public record Square1Voice : VoiceDefinition
{
    public required int Sweep { get; init; }      // 0-7, frequency sweep
    public required DutyCycle DutyCycle { get; init; }
}

/// <summary>
/// Square wave voice (channel 2 without sweep).
/// </summary>
public record Square2Voice : VoiceDefinition
{
    public required DutyCycle DutyCycle { get; init; }
}

/// <summary>
/// Programmable wave voice - custom 4-bit waveform.
/// </summary>
public record ProgrammableWaveVoice : VoiceDefinition
{
    public required string WaveName { get; init; } // e.g., "ProgrammableWaveData_3"
}

/// <summary>
/// Noise voice - LFSR-based noise generator.
/// </summary>
public record NoiseVoice : VoiceDefinition
{
    public required int Period { get; init; } // 0 = long period, 1 = short period (more tonal)
}

/// <summary>
/// Keysplit voice - routes different note ranges to different samples.
/// </summary>
public record KeysplitVoice : VoiceDefinition
{
    public required string VoicegroupName { get; init; }  // e.g., "voicegroup_piano_keysplit"
    public required string KeysplitTableName { get; init; } // e.g., "keysplit_piano"
}

/// <summary>
/// Keysplit all voice - each note maps to a different voice (drums).
/// </summary>
public record KeysplitAllVoice : VoiceDefinition
{
    public required string VoicegroupName { get; init; } // e.g., "voicegroup_rs_drumset"
}

/// <summary>
/// A voicegroup definition containing 128 voice slots (MIDI programs 0-127).
/// </summary>
public class Voicegroup
{
    public required string Name { get; init; }
    public VoiceDefinition?[] Voices { get; } = new VoiceDefinition?[128];

    /// <summary>
    /// Get the voice for a MIDI program number.
    /// </summary>
    public VoiceDefinition? GetVoice(int program)
    {
        if (program < 0 || program >= 128) return null;
        return Voices[program];
    }
}

/// <summary>
/// Keysplit table entry defining note range to sample mapping.
/// </summary>
public record KeysplitEntry(
    int VoiceIndex,  // Index into the keysplit voicegroup
    int HighKey      // Highest note for this range
);

/// <summary>
/// Keysplit table defining how notes map to samples.
/// </summary>
public class KeysplitTable
{
    public required string Name { get; init; }
    public required int StartKey { get; init; } // First valid note
    public List<KeysplitEntry> Entries { get; } = new();
}
