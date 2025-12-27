namespace Porycon3.Services.Sound;

/// <summary>
/// Main orchestrator for extracting and converting pokeemerald sound data.
/// Builds SF2 soundfonts from actual game samples for accurate music playback.
/// </summary>
public class SoundExtractor
{
    private readonly string _pokeemeraldPath;
    private readonly string _outputPath;
    private readonly VoicegroupParser _voicegroupParser;
    private readonly MidiConfigParser _midiConfigParser;
    private readonly PsgSampleGenerator _psgGenerator;

    // Sample cache to avoid loading duplicates
    private readonly Dictionary<string, int> _sampleIndexCache = new();

    public SoundExtractor(string pokeemeraldPath, string outputPath)
    {
        _pokeemeraldPath = pokeemeraldPath;
        _outputPath = outputPath;
        _voicegroupParser = new VoicegroupParser(pokeemeraldPath);
        _midiConfigParser = new MidiConfigParser(pokeemeraldPath);
        _psgGenerator = new PsgSampleGenerator();
    }

    /// <summary>
    /// Extract all sounds and build SF2 soundfont.
    /// </summary>
    public void ExtractAll()
    {
        Console.WriteLine("[SoundExtractor] Starting sound extraction...");

        // Ensure output directories exist
        var soundOutputDir = Path.Combine(_outputPath, "Audio");
        var midiOutputDir = Path.Combine(soundOutputDir, "Music");
        var sfxOutputDir = Path.Combine(soundOutputDir, "SFX");
        Directory.CreateDirectory(midiOutputDir);
        Directory.CreateDirectory(sfxOutputDir);

        // Parse voicegroups and song configs
        Console.WriteLine("[SoundExtractor] Parsing voicegroups...");
        _voicegroupParser.ParseAll();

        Console.WriteLine("[SoundExtractor] Parsing song configurations...");
        _midiConfigParser.ParseAll();

        // Build the master SF2 soundfont
        Console.WriteLine("[SoundExtractor] Building soundfont...");
        var sf2Path = Path.Combine(soundOutputDir, "pokemon_emerald.sf2");
        BuildSoundfont(sf2Path);

        // Convert MIDI files to OGG with loop support
        Console.WriteLine("[SoundExtractor] Converting MIDI files to OGG...");
        ConvertMidiToOgg(midiOutputDir, sf2Path);

        // Generate SFX
        Console.WriteLine("[SoundExtractor] Processing sound effects...");
        ProcessSoundEffects(sfxOutputDir);

        Console.WriteLine($"[SoundExtractor] Complete! Soundfont: {sf2Path}");
    }

    /// <summary>
    /// Build the SF2 soundfont from all voicegroups.
    /// </summary>
    private void BuildSoundfont(string outputPath)
    {
        var sf2 = new Sf2Builder();
        var allVoicegroups = _voicegroupParser.GetAllVoicegroups();

        // First, add all PCM samples from DirectSound voices
        var allSamples = _voicegroupParser.GetAllReferencedSamples();
        Console.WriteLine($"[SoundExtractor] Loading {allSamples.Count} PCM samples...");

        foreach (var sampleName in allSamples)
        {
            LoadAndAddSample(sf2, sampleName);
        }

        // Add PSG samples (square waves, noise)
        Console.WriteLine("[SoundExtractor] Generating PSG samples...");
        AddPsgSamples(sf2);

        // Build a master voicegroup by finding the most common voice for each program
        Console.WriteLine($"[SoundExtractor] Building master voicegroup from {allVoicegroups.Count} voicegroups...");
        var masterVoicegroup = BuildMasterVoicegroup(allVoicegroups);

        // Build instruments from the master voicegroup
        BuildVoicegroupInstruments(sf2, masterVoicegroup, 0);

        // Write the SF2 file
        sf2.Write(outputPath);
        Console.WriteLine($"[SoundExtractor] Wrote soundfont: {outputPath}");
    }

    /// <summary>
    /// Build a master voicegroup by finding the most common voice definition for each program.
    /// This ensures most songs will have the correct instruments.
    /// </summary>
    private Voicegroup BuildMasterVoicegroup(IReadOnlyDictionary<string, Voicegroup> allVoicegroups)
    {
        var master = new Voicegroup { Name = "master" };

        // Get only song voicegroups (exclude keysplit/drumset voicegroups)
        var songVoicegroups = allVoicegroups.Values
            .Where(vg => !vg.Name.Contains("keysplit") && !vg.Name.Contains("drumset"))
            .ToList();

        for (var program = 0; program < 128; program++)
        {
            // Count occurrences of each voice definition
            var voiceCounts = new Dictionary<string, (int count, VoiceDefinition voice)>();

            foreach (var vg in songVoicegroups)
            {
                var voice = vg.GetVoice(program);
                if (voice == null) continue;

                // Create a key that identifies this voice type
                var key = GetVoiceKey(voice);
                if (!voiceCounts.ContainsKey(key))
                {
                    voiceCounts[key] = (0, voice);
                }
                voiceCounts[key] = (voiceCounts[key].count + 1, voice);
            }

            // Use the most common voice for this program
            if (voiceCounts.Count > 0)
            {
                var mostCommon = voiceCounts.MaxBy(kv => kv.Value.count);
                master.Voices[program] = mostCommon.Value.voice;
            }
        }

        return master;
    }

    /// <summary>
    /// Get a unique key for a voice definition (for deduplication).
    /// </summary>
    private static string GetVoiceKey(VoiceDefinition voice)
    {
        return voice switch
        {
            DirectSoundVoice ds => $"ds:{ds.SampleName}",
            Square1Voice sq1 => $"sq1:{sq1.DutyCycle}",
            Square2Voice sq2 => $"sq2:{sq2.DutyCycle}",
            ProgrammableWaveVoice pw => $"pw:{pw.WaveName}",
            NoiseVoice nv => $"nv:{nv.Period}",
            KeysplitVoice ks => $"ks:{ks.VoicegroupName}:{ks.KeysplitTableName}",
            KeysplitAllVoice ksa => $"ksa:{ksa.VoicegroupName}",
            _ => $"unknown:{voice.GetType().Name}"
        };
    }

    /// <summary>
    /// Load a PCM sample from the sound directory and add it to the SF2.
    /// </summary>
    private void LoadAndAddSample(Sf2Builder sf2, string sampleName)
    {
        if (_sampleIndexCache.ContainsKey(sampleName))
            return;

        // Strip the DirectSoundWaveData_ prefix if present
        var baseName = sampleName;
        if (baseName.StartsWith("DirectSoundWaveData_"))
        {
            baseName = baseName["DirectSoundWaveData_".Length..];
        }

        var samplesDir = Path.Combine(_pokeemeraldPath, "sound", "direct_sound_samples");

        // Handle phonemes specially (Phoneme_1 -> phonemes/01.wav)
        if (baseName.StartsWith("Phoneme_"))
        {
            var numStr = baseName["Phoneme_".Length..];
            if (int.TryParse(numStr, out var num))
            {
                var phonemePath = Path.Combine(samplesDir, "phonemes", $"{num:D2}.wav");
                if (TryLoadSample(sf2, sampleName, phonemePath))
                    return;
            }
        }

        // Look for the sample in various locations with different naming conventions
        var samplePaths = new[]
        {
            // Direct match with base name
            Path.Combine(samplesDir, $"{baseName}.wav"),
            // In cries subdirectory
            Path.Combine(samplesDir, "cries", $"{baseName}.wav"),
            // In phonemes subdirectory
            Path.Combine(samplesDir, "phonemes", $"{baseName}.wav"),
            // Try with original name (in case it's already correct)
            Path.Combine(samplesDir, $"{sampleName}.wav"),
        };

        foreach (var samplePath in samplePaths)
        {
            if (TryLoadSample(sf2, sampleName, samplePath))
                return;
        }

        // Sample not found - add a placeholder silently (reduce noise)
        var placeholderData = new byte[1000];
        Array.Fill(placeholderData, (byte)128); // Silence
        var placeholderIndex = sf2.AddSample(sampleName, placeholderData, 22050, 60);
        _sampleIndexCache[sampleName] = placeholderIndex;
    }

    /// <summary>
    /// Try to load a sample from a specific path.
    /// </summary>
    private bool TryLoadSample(Sf2Builder sf2, string sampleName, string samplePath)
    {
        if (!File.Exists(samplePath))
            return false;

        try
        {
            var (data, sampleRate, rootKey, loopStart, loopEnd) = LoadWavFile(samplePath);
            var index = sf2.AddSample(sampleName, data, sampleRate, rootKey, loopStart, loopEnd);
            _sampleIndexCache[sampleName] = index;
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SoundExtractor] Warning: Failed to load {samplePath}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Load a WAV file and extract its data.
    /// </summary>
    private (byte[] data, int sampleRate, int rootKey, int loopStart, int loopEnd) LoadWavFile(string path)
    {
        using var fs = File.OpenRead(path);
        using var br = new BinaryReader(fs);

        // Read RIFF header
        var riff = new string(br.ReadChars(4));
        if (riff != "RIFF")
            throw new InvalidDataException($"Not a valid WAV file: {path}");

        br.ReadInt32(); // File size
        var wave = new string(br.ReadChars(4));
        if (wave != "WAVE")
            throw new InvalidDataException($"Not a valid WAV file: {path}");

        int sampleRate = 22050;
        short bitsPerSample = 8;
        short channels = 1;
        byte[]? data = null;
        int loopStart = -1;
        int loopEnd = -1;

        // Read chunks
        while (fs.Position < fs.Length)
        {
            var chunkId = new string(br.ReadChars(4));
            var chunkSize = br.ReadInt32();

            switch (chunkId)
            {
                case "fmt ":
                    br.ReadInt16(); // Audio format (1 = PCM)
                    channels = br.ReadInt16();
                    sampleRate = br.ReadInt32();
                    br.ReadInt32(); // Byte rate
                    br.ReadInt16(); // Block align
                    bitsPerSample = br.ReadInt16();

                    // Skip any extra format bytes
                    if (chunkSize > 16)
                        br.ReadBytes(chunkSize - 16);
                    break;

                case "data":
                    if (bitsPerSample == 8)
                    {
                        data = br.ReadBytes(chunkSize);
                    }
                    else if (bitsPerSample == 16)
                    {
                        // Convert 16-bit to 8-bit
                        var samples16 = new short[chunkSize / 2];
                        for (var i = 0; i < samples16.Length; i++)
                        {
                            samples16[i] = br.ReadInt16();
                        }

                        data = new byte[samples16.Length];
                        for (var i = 0; i < samples16.Length; i++)
                        {
                            data[i] = (byte)((samples16[i] / 256) + 128);
                        }
                    }
                    else
                    {
                        throw new NotSupportedException($"Unsupported bit depth: {bitsPerSample}");
                    }
                    break;

                case "smpl":
                    // Sample chunk - read loop points if present
                    // Structure: 36 bytes header + 24 bytes per loop
                    var smplChunkEnd = fs.Position + chunkSize;
                    if (chunkSize >= 36)
                    {
                        br.ReadBytes(28); // Skip manufacturer, product, sample period, etc.
                        var loopCount = br.ReadInt32();
                        br.ReadInt32(); // Sampler data size

                        if (loopCount > 0 && chunkSize >= 60)
                        {
                            br.ReadInt32(); // Cue point ID
                            br.ReadInt32(); // Type
                            loopStart = br.ReadInt32();
                            loopEnd = br.ReadInt32();
                            // Skip fraction and play count
                        }
                    }
                    // Skip to end of chunk
                    if (fs.Position < smplChunkEnd)
                        br.ReadBytes((int)(smplChunkEnd - fs.Position));
                    break;

                default:
                    // Skip unknown chunks (including agbp)
                    if (chunkSize > 0 && fs.Position + chunkSize <= fs.Length)
                        br.ReadBytes(chunkSize);
                    break;
            }
        }

        if (data == null)
            throw new InvalidDataException($"No data chunk found in WAV file: {path}");

        // If stereo, convert to mono
        if (channels == 2)
        {
            var monoData = new byte[data.Length / 2];
            for (var i = 0; i < monoData.Length; i++)
            {
                monoData[i] = (byte)((data[i * 2] + data[i * 2 + 1]) / 2);
            }
            data = monoData;
        }

        // Default root key is 60 (middle C)
        return (data, sampleRate, 60, loopStart, loopEnd);
    }

    /// <summary>
    /// Add PSG (square wave, programmable wave, noise) samples.
    /// </summary>
    private void AddPsgSamples(Sf2Builder sf2)
    {
        // Add square wave samples for each duty cycle
        foreach (DutyCycle duty in Enum.GetValues<DutyCycle>())
        {
            var name = $"psg_square_{duty}";
            var data = _psgGenerator.GenerateSquareWave(duty);
            var index = sf2.AddSample(name, data, 22050, 60, 0, data.Length);
            _sampleIndexCache[name] = index;
        }

        // Add standard programmable wave samples
        foreach (var (waveName, waveData) in PsgSampleGenerator.GetStandardWavePatterns())
        {
            var name = $"psg_wave_{waveName}";
            var data = _psgGenerator.GenerateProgrammableWave(waveData);
            var index = sf2.AddSample(name, data, 22050, 60, 0, data.Length);
            _sampleIndexCache[name] = index;
        }

        // Add noise samples
        var noiseShort = _psgGenerator.GenerateNoise(true);
        var noiseLong = _psgGenerator.GenerateNoise(false);
        _sampleIndexCache["psg_noise_short"] = sf2.AddSample("psg_noise_short", noiseShort, 22050, 60);
        _sampleIndexCache["psg_noise_long"] = sf2.AddSample("psg_noise_long", noiseLong, 22050, 60);
    }

    /// <summary>
    /// Build instruments from a voicegroup's voice definitions.
    /// </summary>
    private void BuildVoicegroupInstruments(Sf2Builder sf2, Voicegroup voicegroup, int bank)
    {
        for (var program = 0; program < 128; program++)
        {
            var voice = voicegroup.GetVoice(program);
            if (voice == null) continue;

            var instrumentIndex = BuildVoiceInstrument(sf2, voice, $"prog_{program:D3}");
            if (instrumentIndex >= 0)
            {
                sf2.AddPreset($"Program {program}", bank, program, instrumentIndex);
            }
        }
    }

    /// <summary>
    /// Build an instrument from a voice definition.
    /// </summary>
    private int BuildVoiceInstrument(Sf2Builder sf2, VoiceDefinition voice, string name)
    {
        switch (voice)
        {
            case DirectSoundVoice ds:
                if (_sampleIndexCache.TryGetValue(ds.SampleName, out var sampleIndex))
                {
                    return sf2.AddSimpleInstrument(name, sampleIndex, ds.Envelope);
                }
                break;

            case Square1Voice sq1:
                var sq1SampleName = $"psg_square_{sq1.DutyCycle}";
                if (_sampleIndexCache.TryGetValue(sq1SampleName, out var sq1Index))
                {
                    return sf2.AddSimpleInstrument(name, sq1Index, sq1.Envelope);
                }
                break;

            case Square2Voice sq2:
                var sq2SampleName = $"psg_square_{sq2.DutyCycle}";
                if (_sampleIndexCache.TryGetValue(sq2SampleName, out var sq2Index))
                {
                    return sf2.AddSimpleInstrument(name, sq2Index, sq2.Envelope);
                }
                break;

            case ProgrammableWaveVoice pw:
                // Use sine wave as default for now
                if (_sampleIndexCache.TryGetValue("psg_wave_sine", out var pwIndex))
                {
                    return sf2.AddSimpleInstrument(name, pwIndex, pw.Envelope);
                }
                break;

            case NoiseVoice nv:
                var noiseSampleName = nv.Period == 1 ? "psg_noise_short" : "psg_noise_long";
                if (_sampleIndexCache.TryGetValue(noiseSampleName, out var noiseIndex))
                {
                    return sf2.AddSimpleInstrument(name, noiseIndex, nv.Envelope);
                }
                break;

            case KeysplitVoice ks:
                // Handle keysplit - need to look up the referenced voicegroup
                var keysplitVg = _voicegroupParser.GetVoicegroup(ks.VoicegroupName);
                var keysplitTable = _voicegroupParser.GetKeysplitTable(ks.KeysplitTableName);
                if (keysplitVg != null && keysplitTable != null)
                {
                    return BuildKeysplitInstrument(sf2, keysplitVg, keysplitTable, name);
                }
                break;

            case KeysplitAllVoice ksa:
                // Each MIDI note maps to a different sample (drums)
                var drumVg = _voicegroupParser.GetVoicegroup(ksa.VoicegroupName);
                if (drumVg != null)
                {
                    return BuildDrumkitInstrument(sf2, drumVg, name);
                }
                break;
        }

        return -1;
    }

    /// <summary>
    /// Build a keysplit instrument with multiple samples across key ranges.
    /// </summary>
    private int BuildKeysplitInstrument(Sf2Builder sf2, Voicegroup voicegroup,
        KeysplitTable table, string name)
    {
        var zones = new List<Sf2Zone>();
        var lowKey = table.StartKey;

        foreach (var entry in table.Entries)
        {
            var voice = voicegroup.GetVoice(entry.VoiceIndex);
            if (voice is DirectSoundVoice ds)
            {
                if (_sampleIndexCache.TryGetValue(ds.SampleName, out var sampleIndex))
                {
                    zones.Add(new Sf2Zone
                    {
                        SampleIndex = sampleIndex,
                        KeyRangeLow = lowKey,
                        KeyRangeHigh = entry.HighKey,
                        Envelope = ds.Envelope
                    });
                }
            }

            lowKey = entry.HighKey + 1;
        }

        return zones.Count > 0 ? sf2.AddKeysplitInstrument(name, zones) : -1;
    }

    /// <summary>
    /// Build a drumkit instrument where each note is a different sample.
    /// </summary>
    private int BuildDrumkitInstrument(Sf2Builder sf2, Voicegroup voicegroup, string name)
    {
        var zones = new List<Sf2Zone>();

        for (var note = 0; note < 128; note++)
        {
            var voice = voicegroup.GetVoice(note);
            if (voice is DirectSoundVoice ds)
            {
                if (_sampleIndexCache.TryGetValue(ds.SampleName, out var sampleIndex))
                {
                    zones.Add(new Sf2Zone
                    {
                        SampleIndex = sampleIndex,
                        KeyRangeLow = note,
                        KeyRangeHigh = note,
                        Envelope = ds.Envelope
                    });
                }
            }
        }

        return zones.Count > 0 ? sf2.AddKeysplitInstrument(name, zones) : -1;
    }

    /// <summary>
    /// Convert MIDI files to OGG using the SF2 soundfont.
    /// </summary>
    private void ConvertMidiToOgg(string outputDir, string sf2Path)
    {
        var midiSourceDir = Path.Combine(_pokeemeraldPath, "sound", "songs", "midi");
        if (!Directory.Exists(midiSourceDir)) return;

        // Create definitions directory for audio JSON files
        var definitionsDir = Path.Combine(_outputPath, "Definitions", "Audio");
        Directory.CreateDirectory(definitionsDir);

        var midiFiles = Directory.GetFiles(midiSourceDir, "*.mid");
        Console.WriteLine($"[SoundExtractor] Found {midiFiles.Length} MIDI files to convert");

        var converter = new MidiToOggConverter(sf2Path);
        var converted = 0;
        var failed = 0;

        foreach (var midiFile in midiFiles)
        {
            var baseName = Path.GetFileNameWithoutExtension(midiFile);
            var oggPath = Path.Combine(outputDir, $"{baseName}.ogg");

            var result = converter.Convert(midiFile, oggPath);
            if (result.Success)
            {
                converted++;

                // Generate audio definition JSON
                var audioId = baseName.StartsWith("mus_") ? baseName : $"mus_{baseName}";
                var jsonPath = Path.Combine(definitionsDir, $"{audioId}.json");
                WriteAudioDefinition(jsonPath, audioId, baseName, result);

                if (converted % 50 == 0)
                    Console.WriteLine($"[SoundExtractor] Converted {converted}/{midiFiles.Length} songs...");
            }
            else
            {
                failed++;
            }
        }

        Console.WriteLine($"[SoundExtractor] Converted {converted} songs, {failed} failed");
    }

    /// <summary>
    /// Write an AudioDefinition JSON file for a converted track.
    /// </summary>
    private static void WriteAudioDefinition(string jsonPath, string audioId, string baseName,
        MidiToOggConverter.ConversionResult result)
    {
        var hasLoop = result.LoopStartSamples > 0 || result.LoopLengthSamples > 0;

        var json = $$"""
            {
                "id": "{{audioId}}",
                "name": "{{FormatSongName(baseName)}}",
                "audioPath": "Audio/Music/{{baseName}}.ogg",
                "volume": 1.0,
                "loop": true,
                "fadeIn": 0.5,
                "fadeOut": 0.5{{(hasLoop ? $",\n    \"loopStartSamples\": {result.LoopStartSamples},\n    \"loopLengthSamples\": {result.LoopLengthSamples}" : "")}}
            }
            """;

        File.WriteAllText(jsonPath, json);
    }

    /// <summary>
    /// Format a song filename into a readable name.
    /// </summary>
    private static string FormatSongName(string baseName)
    {
        // Remove mus_ prefix and convert underscores to spaces
        var name = baseName;
        if (name.StartsWith("mus_"))
            name = name[4..];

        // Convert underscores to spaces and title case
        return System.Globalization.CultureInfo.CurrentCulture.TextInfo
            .ToTitleCase(name.Replace('_', ' '));
    }

    /// <summary>
    /// Copy MIDI files to the output directory (fallback if OGG conversion not wanted).
    /// </summary>
    private void CopyMidiFiles(string outputDir)
    {
        // MIDI files are in sound/songs/midi/ directory
        var midiSourceDir = Path.Combine(_pokeemeraldPath, "sound", "songs", "midi");
        if (!Directory.Exists(midiSourceDir)) return;

        var midiFiles = Directory.GetFiles(midiSourceDir, "*.mid");
        Console.WriteLine($"[SoundExtractor] Found {midiFiles.Length} MIDI files");

        foreach (var midiFile in midiFiles)
        {
            var destPath = Path.Combine(outputDir, Path.GetFileName(midiFile));
            File.Copy(midiFile, destPath, overwrite: true);
        }
    }

    /// <summary>
    /// Process sound effects (cries, etc).
    /// </summary>
    private void ProcessSoundEffects(string outputDir)
    {
        var criesDir = Path.Combine(_pokeemeraldPath, "sound", "direct_sound_samples", "cries");
        if (!Directory.Exists(criesDir)) return;

        // Copy cry WAV files
        foreach (var wavFile in Directory.GetFiles(criesDir, "*.wav"))
        {
            var destPath = Path.Combine(outputDir, Path.GetFileName(wavFile));
            File.Copy(wavFile, destPath, overwrite: true);
        }
    }
}
