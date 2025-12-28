namespace Porycon3.Services.Sound;

/// <summary>
/// Builds SF2 (SoundFont 2) files from GBA sound samples.
/// SF2 is the standard format for software synthesizers and is natively
/// supported by MIDI players, making it ideal for pokeemerald music playback.
/// </summary>
public class Sf2Builder
{
    private readonly List<Sf2Sample> _samples = new();
    private readonly List<Sf2Instrument> _instruments = new();
    private readonly List<Sf2Preset> _presets = new();

    /// <summary>
    /// Add a PCM sample to the soundfont.
    /// </summary>
    /// <param name="name">Sample name (max 20 chars)</param>
    /// <param name="data">8-bit unsigned PCM sample data</param>
    /// <param name="sampleRate">Sample rate in Hz</param>
    /// <param name="rootKey">Root MIDI note (60 = middle C)</param>
    /// <param name="loopStart">Loop start point in samples (-1 for no loop)</param>
    /// <param name="loopEnd">Loop end point in samples (-1 for no loop)</param>
    /// <returns>Sample index for referencing in instruments</returns>
    public int AddSample(string name, byte[] data, int sampleRate, int rootKey = 60,
        int loopStart = -1, int loopEnd = -1)
    {
        var sample = new Sf2Sample
        {
            Name = name.Length > 20 ? name[..20] : name,
            Data = ConvertTo16Bit(data),
            SampleRate = sampleRate,
            RootKey = rootKey,
            LoopStart = loopStart >= 0 ? loopStart : 0,
            LoopEnd = loopEnd >= 0 ? loopEnd : data.Length,
            IsLooped = loopStart >= 0 && loopEnd >= 0
        };

        _samples.Add(sample);
        return _samples.Count - 1;
    }

    /// <summary>
    /// Add a 16-bit sample directly (already in SF2 format).
    /// </summary>
    public int AddSample16Bit(string name, short[] data, int sampleRate, int rootKey = 60,
        int loopStart = -1, int loopEnd = -1)
    {
        var sample = new Sf2Sample
        {
            Name = name.Length > 20 ? name[..20] : name,
            Data = data,
            SampleRate = sampleRate,
            RootKey = rootKey,
            LoopStart = loopStart >= 0 ? loopStart : 0,
            LoopEnd = loopEnd >= 0 ? loopEnd : data.Length,
            IsLooped = loopStart >= 0 && loopEnd >= 0
        };

        _samples.Add(sample);
        return _samples.Count - 1;
    }

    /// <summary>
    /// Create an instrument with a single sample covering all keys.
    /// </summary>
    /// <param name="name">Instrument name</param>
    /// <param name="sampleIndex">Index of the sample to use</param>
    /// <param name="envelope">ADSR envelope parameters</param>
    /// <param name="rootKeyOverride">Override root key from voice definition (-1 to use sample's root key)</param>
    /// <returns>Instrument index for referencing in presets</returns>
    public int AddSimpleInstrument(string name, int sampleIndex, VoiceEnvelope envelope, int rootKeyOverride = -1)
    {
        var inst = new Sf2Instrument
        {
            Name = name.Length > 20 ? name[..20] : name,
            Zones = new List<Sf2Zone>
            {
                new()
                {
                    SampleIndex = sampleIndex,
                    KeyRangeLow = 0,
                    KeyRangeHigh = 127,
                    VelRangeLow = 0,
                    VelRangeHigh = 127,
                    Envelope = envelope,
                    RootKeyOverride = rootKeyOverride
                }
            }
        };

        _instruments.Add(inst);
        return _instruments.Count - 1;
    }

    /// <summary>
    /// Create a keysplit instrument with multiple samples across key ranges.
    /// </summary>
    /// <param name="name">Instrument name</param>
    /// <param name="zones">List of key zones with sample mappings</param>
    /// <returns>Instrument index</returns>
    public int AddKeysplitInstrument(string name, List<Sf2Zone> zones)
    {
        var inst = new Sf2Instrument
        {
            Name = name.Length > 20 ? name[..20] : name,
            Zones = zones
        };

        _instruments.Add(inst);
        return _instruments.Count - 1;
    }

    /// <summary>
    /// Add a preset (MIDI program) that references an instrument.
    /// </summary>
    /// <param name="name">Preset name</param>
    /// <param name="bank">MIDI bank number (0-127)</param>
    /// <param name="program">MIDI program number (0-127)</param>
    /// <param name="instrumentIndex">Index of the instrument to use</param>
    public void AddPreset(string name, int bank, int program, int instrumentIndex)
    {
        _presets.Add(new Sf2Preset
        {
            Name = name.Length > 20 ? name[..20] : name,
            Bank = bank,
            Program = program,
            InstrumentIndex = instrumentIndex
        });
    }

    /// <summary>
    /// Write the SF2 file to disk.
    /// </summary>
    public void Write(string path)
    {
        using var fs = File.Create(path);
        using var bw = new BinaryWriter(fs);

        // Calculate sizes
        var sampleDataSize = CalculateSampleDataSize();
        var infoSize = CalculateInfoSize();
        var sdtaSize = 8 + sampleDataSize; // smpl sub-chunk
        var pdtaSize = CalculatePdtaSize();

        var totalSize = 4 + // RIFF type
                        8 + infoSize + // INFO chunk
                        8 + sdtaSize + // sdta chunk
                        8 + pdtaSize;  // pdta chunk

        // RIFF header
        bw.Write("RIFF"u8);
        bw.Write(totalSize);
        bw.Write("sfbk"u8);

        // INFO chunk
        WriteInfoChunk(bw);

        // sdta chunk (sample data)
        WriteSdtaChunk(bw);

        // pdta chunk (preset, instrument, and sample headers)
        WritePdtaChunk(bw);
    }

    private void WriteInfoChunk(BinaryWriter bw)
    {
        var infoSize = CalculateInfoSize();

        bw.Write("LIST"u8);
        bw.Write(infoSize);
        bw.Write("INFO"u8);

        // ifil - SoundFont version (2.01)
        bw.Write("ifil"u8);
        bw.Write(4);
        bw.Write((short)2);  // Major version
        bw.Write((short)1);  // Minor version

        // isng - Sound engine
        WriteInfoString(bw, "isng", "EMU8000");

        // INAM - SoundFont name
        WriteInfoString(bw, "INAM", "Pokemon Emerald");

        // ICMT - Comment
        WriteInfoString(bw, "ICMT", "Generated by Porycon3");
    }

    private void WriteInfoString(BinaryWriter bw, string chunkId, string value)
    {
        // Pad to even length with null terminator
        var paddedLength = (value.Length + 2) & ~1;
        var data = new byte[paddedLength];
        System.Text.Encoding.ASCII.GetBytes(value, data);

        bw.Write(System.Text.Encoding.ASCII.GetBytes(chunkId));
        bw.Write(paddedLength);
        bw.Write(data);
    }

    private void WriteSdtaChunk(BinaryWriter bw)
    {
        var sampleDataSize = CalculateSampleDataSize();

        bw.Write("LIST"u8);
        bw.Write(8 + sampleDataSize);
        bw.Write("sdta"u8);

        // smpl sub-chunk
        bw.Write("smpl"u8);
        bw.Write(sampleDataSize);

        // Write all sample data concatenated
        foreach (var sample in _samples)
        {
            foreach (var s in sample.Data)
            {
                bw.Write(s);
            }

            // 46 zero samples as terminator (required by SF2 spec)
            for (var i = 0; i < 46; i++)
            {
                bw.Write((short)0);
            }
        }
    }

    private void WritePdtaChunk(BinaryWriter bw)
    {
        var pdtaSize = CalculatePdtaSize();

        bw.Write("LIST"u8);
        bw.Write(pdtaSize);
        bw.Write("pdta"u8);

        // phdr - Preset headers
        WritePresetHeaders(bw);

        // pbag - Preset zones
        WritePresetBags(bw);

        // pmod - Preset modulators (empty)
        bw.Write("pmod"u8);
        bw.Write(10);
        bw.Write(new byte[10]); // Terminal record

        // pgen - Preset generators
        WritePresetGenerators(bw);

        // inst - Instrument headers
        WriteInstrumentHeaders(bw);

        // ibag - Instrument zones
        WriteInstrumentBags(bw);

        // imod - Instrument modulators (empty)
        bw.Write("imod"u8);
        bw.Write(10);
        bw.Write(new byte[10]); // Terminal record

        // igen - Instrument generators
        WriteInstrumentGenerators(bw);

        // shdr - Sample headers
        WriteSampleHeaders(bw);
    }

    private void WritePresetHeaders(BinaryWriter bw)
    {
        var size = (_presets.Count + 1) * 38;
        bw.Write("phdr"u8);
        bw.Write(size);

        var bagIndex = 0;
        foreach (var preset in _presets)
        {
            WritePresetHeader(bw, preset.Name, preset.Program, preset.Bank, bagIndex);
            bagIndex++;
        }

        // Terminal record
        WritePresetHeader(bw, "EOP", 0, 0, bagIndex);
    }

    private void WritePresetHeader(BinaryWriter bw, string name, int preset, int bank, int bagIndex)
    {
        var nameBytes = new byte[20];
        System.Text.Encoding.ASCII.GetBytes(name, nameBytes.AsSpan());
        bw.Write(nameBytes);
        bw.Write((short)preset);
        bw.Write((short)bank);
        bw.Write((short)bagIndex);
        bw.Write(0); // Library
        bw.Write(0); // Genre
        bw.Write(0); // Morphology
    }

    private void WritePresetBags(BinaryWriter bw)
    {
        var size = (_presets.Count + 1) * 4;
        bw.Write("pbag"u8);
        bw.Write(size);

        var genIndex = 0;
        foreach (var _ in _presets)
        {
            bw.Write((short)genIndex);
            bw.Write((short)0); // Mod index
            genIndex += 1; // One generator per preset (instrument reference)
        }

        // Terminal
        bw.Write((short)genIndex);
        bw.Write((short)0);
    }

    private void WritePresetGenerators(BinaryWriter bw)
    {
        var size = (_presets.Count + 1) * 4;
        bw.Write("pgen"u8);
        bw.Write(size);

        foreach (var preset in _presets)
        {
            // Generator: instrument (41)
            bw.Write((short)41);
            bw.Write((short)preset.InstrumentIndex);
        }

        // Terminal
        bw.Write((short)0);
        bw.Write((short)0);
    }

    private void WriteInstrumentHeaders(BinaryWriter bw)
    {
        var size = (_instruments.Count + 1) * 22;
        bw.Write("inst"u8);
        bw.Write(size);

        var bagIndex = 0;
        foreach (var inst in _instruments)
        {
            var nameBytes = new byte[20];
            System.Text.Encoding.ASCII.GetBytes(inst.Name, nameBytes.AsSpan());
            bw.Write(nameBytes);
            bw.Write((short)bagIndex);
            bagIndex += inst.Zones.Count;
        }

        // Terminal
        var termName = new byte[20];
        System.Text.Encoding.ASCII.GetBytes("EOI", termName.AsSpan());
        bw.Write(termName);
        bw.Write((short)bagIndex);
    }

    private void WriteInstrumentBags(BinaryWriter bw)
    {
        var totalZones = _instruments.Sum(i => i.Zones.Count);
        var size = (totalZones + 1) * 4;
        bw.Write("ibag"u8);
        bw.Write(size);

        var genIndex = 0;
        foreach (var inst in _instruments)
        {
            foreach (var _ in inst.Zones)
            {
                bw.Write((short)genIndex);
                bw.Write((short)0); // Mod index
                genIndex += GeneratorsPerZone; // All generators including ADSR, attenuation, pan
            }
        }

        // Terminal
        bw.Write((short)genIndex);
        bw.Write((short)0);
    }

    /// <summary>
    /// Number of generators per instrument zone.
    /// keyRange, velRange, overridingRootKey, sampleModes, sampleID = 5
    /// Plus ADSR envelope: attackVolEnv, decayVolEnv, sustainVolEnv, releaseVolEnv = 4
    /// Total = 9 generators per zone
    /// </summary>
    private const int GeneratorsPerZone = 9;

    private void WriteInstrumentGenerators(BinaryWriter bw)
    {
        var totalZones = _instruments.Sum(i => i.Zones.Count);
        var size = (totalZones * GeneratorsPerZone + 1) * 4;
        bw.Write("igen"u8);
        bw.Write(size);

        foreach (var inst in _instruments)
        {
            foreach (var zone in inst.Zones)
            {
                // Key range (43)
                bw.Write((short)43);
                bw.Write((byte)zone.KeyRangeLow);
                bw.Write((byte)zone.KeyRangeHigh);

                // Velocity range (44)
                bw.Write((short)44);
                bw.Write((byte)zone.VelRangeLow);
                bw.Write((byte)zone.VelRangeHigh);

                // ADSR Envelope generators
                // Attack (34) - attackVolEnv in timecents
                bw.Write((short)34);
                bw.Write(GbaAttackToTimecents(zone.Envelope.Attack));

                // Decay (36) - decayVolEnv in timecents
                bw.Write((short)36);
                bw.Write(GbaDecayToTimecents(zone.Envelope.Decay));

                // Sustain (37) - sustainVolEnv in centibels (0 = full, 1000 = -100dB)
                bw.Write((short)37);
                bw.Write(GbaSustainToCentibels(zone.Envelope.Sustain));

                // Release (38) - releaseVolEnv in timecents
                bw.Write((short)38);
                bw.Write(GbaReleaseToTimecents(zone.Envelope.Release));

                // Overriding root key (58)
                // Use zone's RootKeyOverride if set, otherwise fall back to sample's root key
                int rootKey;
                if (zone.RootKeyOverride >= 0)
                {
                    rootKey = zone.RootKeyOverride;
                }
                else if (_samples.Count > zone.SampleIndex)
                {
                    rootKey = _samples[zone.SampleIndex].RootKey;
                }
                else
                {
                    rootKey = 60;
                }
                bw.Write((short)58);
                bw.Write((short)rootKey);

                // Sample modes (54) - 1 = loop continuously
                bw.Write((short)54);
                bw.Write((short)(_samples.Count > zone.SampleIndex && _samples[zone.SampleIndex].IsLooped ? 1 : 0));

                // Sample ID (53) - must be last generator
                bw.Write((short)53);
                bw.Write((short)zone.SampleIndex);
            }
        }

        // Terminal
        bw.Write((short)0);
        bw.Write((short)0);
    }

    /// <summary>
    /// Convert GBA attack value to SF2 timecents.
    /// Using simple fixed values for predictable results - GBA envelope semantics
    /// don't map cleanly to SF2, so we use sensible defaults.
    /// </summary>
    private static short GbaAttackToTimecents(int gbaAttack)
    {
        // Fast attack for all instruments - notes start immediately
        // -12000 = instant, -8000 = very fast, 0 = 1 second
        return -10000; // Very fast attack (~0.01s)
    }

    /// <summary>
    /// Convert GBA decay value to SF2 timecents.
    /// </summary>
    private static short GbaDecayToTimecents(int gbaDecay)
    {
        // No decay - go straight to sustain level
        // This prevents unwanted volume drops after note start
        return -12000; // Instant (no decay phase)
    }

    /// <summary>
    /// Convert GBA sustain to SF2 centibels attenuation.
    /// </summary>
    private static short GbaSustainToCentibels(int gbaSustain)
    {
        // Full sustain - notes stay at full volume while held
        // 0 = full volume, 1000 = -100dB (silent)
        return 0; // Full volume sustain
    }

    /// <summary>
    /// Convert GBA release value to SF2 timecents.
    /// </summary>
    private static short GbaReleaseToTimecents(int gbaRelease)
    {
        // Gentle release so notes fade out naturally when key is released
        // -12000 = instant, 0 = 1 second, 1200 = 2 seconds
        return -3000; // ~0.15 second release for natural fade
    }

    /// <summary>
    /// Calculate SF2 initial attenuation from GBA envelope.
    /// GBA sustain: 0-255 (DirectSound) or 0-15 (PSG) where max = full volume
    /// SF2 attenuation: 0 = full volume, 1000 = -100dB (centibels, 10 cB = 1 dB)
    /// </summary>
    private static short CalculateAttenuation(VoiceEnvelope envelope)
    {
        var sustain = envelope.Sustain;

        // Check if this looks like a PSG envelope (sustain 0-15)
        // PSG uses 4-bit values, so max is 15
        if (sustain <= 15 && envelope.Attack <= 15 && envelope.Decay <= 15 && envelope.Release <= 15)
        {
            // Scale PSG sustain (0-15) to 0-255 range
            sustain = sustain * 255 / 15;
        }

        // Sustain 255 = full volume (0 attenuation)
        // Sustain 0 = silent (max attenuation, but cap at 600 cB = 60dB for usable range)
        if (sustain >= 250)
            return 0; // Full volume

        if (sustain <= 5)
            return 600; // Very quiet but not silent

        // Linear interpolation: sustain 255->0 maps to attenuation 0->600
        // This gives a reasonable dynamic range without complete silence
        var attenuation = (255 - sustain) * 600 / 255;
        return (short)Math.Clamp(attenuation, 0, 600);
    }

    private void WriteSampleHeaders(BinaryWriter bw)
    {
        var size = (_samples.Count + 1) * 46;
        bw.Write("shdr"u8);
        bw.Write(size);

        var sampleStart = 0;
        foreach (var sample in _samples)
        {
            var nameBytes = new byte[20];
            System.Text.Encoding.ASCII.GetBytes(sample.Name, nameBytes.AsSpan());
            bw.Write(nameBytes);

            var sampleEnd = sampleStart + sample.Data.Length;

            // Loop positions: SF2 spec says loopEnd points to first sample AFTER the loop
            // So for a loop from 0-99, loopEnd should be 100, not 99
            var loopStart = Math.Clamp(sample.LoopStart, 0, sample.Data.Length - 1);
            // loopEnd in SF2 is exclusive (one past the last sample), clamp to data length
            var loopEnd = Math.Clamp(sample.LoopEnd, loopStart + 1, sample.Data.Length);

            bw.Write(sampleStart);                      // Start
            bw.Write(sampleEnd);                        // End (points to first terminator)
            bw.Write(sampleStart + loopStart);          // Loop start (first sample of loop)
            bw.Write(sampleStart + loopEnd);            // Loop end (first sample AFTER loop)
            bw.Write(sample.SampleRate);              // Sample rate
            bw.Write((byte)sample.RootKey);           // Original pitch
            bw.Write((sbyte)0);                       // Pitch correction
            bw.Write((short)0);                       // Sample link
            bw.Write((short)1);                       // Sample type (mono)

            sampleStart = sampleEnd + 46;
        }

        // Terminal sample header
        var termName = new byte[20];
        System.Text.Encoding.ASCII.GetBytes("EOS", termName.AsSpan());
        bw.Write(termName);
        bw.Write(sampleStart);
        bw.Write(sampleStart);
        bw.Write(0);
        bw.Write(0);
        bw.Write(0);
        bw.Write((byte)0);
        bw.Write((sbyte)0);
        bw.Write((short)0);
        bw.Write((short)0);
    }

    private int CalculateSampleDataSize()
    {
        // Each sample + 46 terminator samples (16-bit = 2 bytes each)
        return _samples.Sum(s => (s.Data.Length + 46) * 2);
    }

    private int CalculateInfoSize()
    {
        // INFO type + ifil + isng + INAM + ICMT (with proper padding calculation)
        var ifilSize = 4 + 4 + 4; // chunk id + size + version data
        var isngSize = 4 + 4 + GetPaddedStringLength("EMU8000");
        var inamSize = 4 + 4 + GetPaddedStringLength("Pokemon Emerald");
        var icmtSize = 4 + 4 + GetPaddedStringLength("Generated by Porycon3");
        return 4 + ifilSize + isngSize + inamSize + icmtSize; // 4 = "INFO"
    }

    private static int GetPaddedStringLength(string value)
    {
        // Pad to even length with null terminator
        return (value.Length + 2) & ~1;
    }

    private int CalculatePdtaSize()
    {
        var phdrSize = (_presets.Count + 1) * 38 + 8;
        var pbagSize = (_presets.Count + 1) * 4 + 8;
        var pmodSize = 10 + 8;
        var pgenSize = (_presets.Count + 1) * 4 + 8;

        var totalZones = _instruments.Sum(i => i.Zones.Count);
        var instSize = (_instruments.Count + 1) * 22 + 8;
        var ibagSize = (totalZones + 1) * 4 + 8;
        var imodSize = 10 + 8;
        var igenSize = (totalZones * GeneratorsPerZone + 1) * 4 + 8;

        var shdrSize = (_samples.Count + 1) * 46 + 8;

        return 4 + phdrSize + pbagSize + pmodSize + pgenSize +
               instSize + ibagSize + imodSize + igenSize + shdrSize;
    }

    private static short[] ConvertTo16Bit(byte[] data8Bit)
    {
        var result = new short[data8Bit.Length];
        for (var i = 0; i < data8Bit.Length; i++)
        {
            // Convert unsigned 8-bit (0-255) to signed 16-bit (-32768 to 32767)
            // Use 256 to avoid overflow (max becomes 32512, close enough)
            result[i] = (short)((data8Bit[i] - 128) * 256);
        }

        return result;
    }
}

/// <summary>
/// Internal sample representation for SF2 building.
/// </summary>
internal class Sf2Sample
{
    public required string Name { get; init; }
    public required short[] Data { get; init; }
    public required int SampleRate { get; init; }
    public required int RootKey { get; init; }
    public int LoopStart { get; init; }
    public int LoopEnd { get; init; }
    public bool IsLooped { get; init; }
}

/// <summary>
/// Internal instrument representation.
/// </summary>
internal class Sf2Instrument
{
    public required string Name { get; init; }
    public required List<Sf2Zone> Zones { get; init; }
}

/// <summary>
/// A zone within an instrument mapping a key/velocity range to a sample.
/// </summary>
public class Sf2Zone
{
    public required int SampleIndex { get; init; }
    public int KeyRangeLow { get; init; } = 0;
    public int KeyRangeHigh { get; init; } = 127;
    public int VelRangeLow { get; init; } = 0;
    public int VelRangeHigh { get; init; } = 127;
    public VoiceEnvelope Envelope { get; init; } = new(0, 0, 255, 0);
    /// <summary>
    /// Override root key from the voice definition's BaseMidiKey.
    /// If set (>= 0), uses this instead of the sample's root key.
    /// </summary>
    public int RootKeyOverride { get; init; } = -1;
}

/// <summary>
/// Internal preset representation.
/// </summary>
internal class Sf2Preset
{
    public required string Name { get; init; }
    public required int Bank { get; init; }
    public required int Program { get; init; }
    public required int InstrumentIndex { get; init; }
}
