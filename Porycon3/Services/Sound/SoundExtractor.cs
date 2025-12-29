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
    private readonly string _idPrefix;

    // Sample cache to avoid loading duplicates
    private readonly Dictionary<string, int> _sampleIndexCache = new();

    /// <summary>
    /// Create a SoundExtractor with default "base:" ID prefix.
    /// </summary>
    public SoundExtractor(string pokeemeraldPath, string outputPath)
        : this(pokeemeraldPath, outputPath, "base")
    {
    }

    /// <summary>
    /// Create a SoundExtractor with a custom ID prefix for audio definitions.
    /// </summary>
    /// <param name="pokeemeraldPath">Path to pokeemerald decompilation</param>
    /// <param name="outputPath">Base output path</param>
    /// <param name="idPrefix">ID prefix for audio definitions (e.g., "emerald-audio" produces "emerald-audio:audio:...")</param>
    public SoundExtractor(string pokeemeraldPath, string outputPath, string idPrefix)
    {
        _pokeemeraldPath = pokeemeraldPath;
        _outputPath = outputPath;
        _idPrefix = idPrefix + ":";
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

        // All audio goes under Audio/Music/ (music, sfx, fanfares, etc.)
        var musicOutputDir = Path.Combine(_outputPath, "Audio", "Music");
        Directory.CreateDirectory(musicOutputDir);

        // Parse voicegroups and song configs
        Console.WriteLine("[SoundExtractor] Parsing voicegroups...");
        _voicegroupParser.ParseAll();

        Console.WriteLine("[SoundExtractor] Parsing song configurations...");
        _midiConfigParser.ParseAll();

        // Build the master SF2 soundfont
        Console.WriteLine("[SoundExtractor] Building soundfont...");
        var audioDir = Path.Combine(_outputPath, "Audio");
        Directory.CreateDirectory(audioDir);
        var sf2Path = Path.Combine(audioDir, "pokemon_emerald.sf2");
        BuildSoundfont(sf2Path);

        // Convert MIDI files to OGG with loop support
        Console.WriteLine("[SoundExtractor] Converting MIDI files to OGG...");
        ConvertMidiToOgg(musicOutputDir, sf2Path);

        // Generate SFX (also goes under Music/)
        Console.WriteLine("[SoundExtractor] Processing sound effects...");
        ProcessSoundEffects(musicOutputDir);

        Console.WriteLine($"[SoundExtractor] Complete! Soundfont: {sf2Path}");
    }

    /// <summary>
    /// Build the SF2 soundfont from all voicegroups.
    /// Each unique voicegroup gets its own SF2 bank for proper per-song instrument selection.
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
        // This gives us the best single-bank approximation for all songs
        Console.WriteLine($"[SoundExtractor] Building master voicegroup from {allVoicegroups.Count} voicegroups...");
        var masterVoicegroup = BuildMasterVoicegroup(allVoicegroups);

        // Build instruments from the master voicegroup into bank 0
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

        // Normalize DirectSound samples to target amplitude (±48 to match PSG)
        // This ensures balanced volume between sampled instruments and PSG tones
        data = NormalizeSampleVolume(data);

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

    // Sample rate for PSG samples (matches PsgSampleGenerator - authentic GBA rate)
    private const int PsgSampleRate = 13379;

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
            var index = sf2.AddSample(name, data, PsgSampleRate, 60, 0, data.Length);
            _sampleIndexCache[name] = index;
        }

        // Load actual programmable wave samples from pokeemerald PCM files
        var waveSamplesDir = Path.Combine(_pokeemeraldPath, "sound", "programmable_wave_samples");
        for (var i = 1; i <= 25; i++)
        {
            var pcmPath = Path.Combine(waveSamplesDir, $"{i:D2}.pcm");
            if (File.Exists(pcmPath))
            {
                try
                {
                    // PCM files are 16 bytes containing two 4-bit samples per byte (32 samples total)
                    var rawData = File.ReadAllBytes(pcmPath);
                    var waveData = PsgSampleGenerator.ParseProgrammableWaveData(rawData);
                    var sampleData = _psgGenerator.GenerateProgrammableWave(waveData);

                    var name = $"psg_wave_{i}";
                    var index = sf2.AddSample(name, sampleData, PsgSampleRate, 60, 0, sampleData.Length);
                    _sampleIndexCache[name] = index;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[SoundExtractor] Warning: Failed to load wave {i}: {ex.Message}");
                }
            }
        }

        // Add fallback standard wave patterns (sine, triangle, etc.) for compatibility
        foreach (var (waveName, waveData) in PsgSampleGenerator.GetStandardWavePatterns())
        {
            var name = $"psg_wave_{waveName}";
            if (!_sampleIndexCache.ContainsKey(name))
            {
                var data = _psgGenerator.GenerateProgrammableWave(waveData);
                var index = sf2.AddSample(name, data, PsgSampleRate, 60, 0, data.Length);
                _sampleIndexCache[name] = index;
            }
        }

        // Add noise samples
        var noiseShort = _psgGenerator.GenerateNoise(true);
        var noiseLong = _psgGenerator.GenerateNoise(false);
        _sampleIndexCache["psg_noise_short"] = sf2.AddSample("psg_noise_short", noiseShort, PsgSampleRate, 60);
        _sampleIndexCache["psg_noise_long"] = sf2.AddSample("psg_noise_long", noiseLong, PsgSampleRate, 60);
    }

    /// <summary>
    /// Map a ProgrammableWaveVoice WaveName to the corresponding sample cache key.
    /// Handles names like "ProgrammableWaveData_3" -> "psg_wave_3"
    /// </summary>
    private string GetProgrammableWaveSampleName(string waveName)
    {
        // Try to extract the wave number from names like "ProgrammableWaveData_3"
        if (waveName.StartsWith("ProgrammableWaveData_"))
        {
            var numPart = waveName["ProgrammableWaveData_".Length..];
            if (int.TryParse(numPart, out var waveNum) && _sampleIndexCache.ContainsKey($"psg_wave_{waveNum}"))
            {
                return $"psg_wave_{waveNum}";
            }
        }

        // Fall back to sine wave if not found
        return "psg_wave_sine";
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
                    // Use voice's BaseMidiKey for correct pitch mapping
                    return sf2.AddSimpleInstrument(name, sampleIndex, ds.Envelope, ds.BaseMidiKey);
                }
                break;

            case Square1Voice sq1:
                var sq1SampleName = $"psg_square_{sq1.DutyCycle}";
                if (_sampleIndexCache.TryGetValue(sq1SampleName, out var sq1Index))
                {
                    return sf2.AddSimpleInstrument(name, sq1Index, sq1.Envelope, sq1.BaseMidiKey);
                }
                break;

            case Square2Voice sq2:
                var sq2SampleName = $"psg_square_{sq2.DutyCycle}";
                if (_sampleIndexCache.TryGetValue(sq2SampleName, out var sq2Index))
                {
                    return sf2.AddSimpleInstrument(name, sq2Index, sq2.Envelope, sq2.BaseMidiKey);
                }
                break;

            case ProgrammableWaveVoice pw:
                // Look up the actual wave pattern, fall back to sine if not found
                var waveSampleName = GetProgrammableWaveSampleName(pw.WaveName);
                if (_sampleIndexCache.TryGetValue(waveSampleName, out var pwIndex))
                {
                    return sf2.AddSimpleInstrument(name, pwIndex, pw.Envelope, pw.BaseMidiKey);
                }
                break;

            case NoiseVoice nv:
                var noiseSampleName = nv.Period == 1 ? "psg_noise_short" : "psg_noise_long";
                if (_sampleIndexCache.TryGetValue(noiseSampleName, out var noiseIndex))
                {
                    return sf2.AddSimpleInstrument(name, noiseIndex, nv.Envelope, nv.BaseMidiKey);
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
                        Envelope = ds.Envelope,
                        RootKeyOverride = ds.BaseMidiKey
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
                        Envelope = ds.Envelope,
                        RootKeyOverride = ds.BaseMidiKey
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

        var midiFiles = Directory.GetFiles(midiSourceDir, "*.mid");
        Console.WriteLine($"[SoundExtractor] Found {midiFiles.Length} MIDI files to convert");

        var converter = new MidiToOggConverter(sf2Path);
        var converted = 0;
        var failed = 0;

        foreach (var midiFile in midiFiles)
        {
            var baseName = Path.GetFileNameWithoutExtension(midiFile);
            var category = CategorizeMusic(baseName);

            // Strip prefixes for clean filenames (mus_, se_, ph_)
            var cleanName = StripPrefix(baseName);
            var pascalName = ToPascalCase(cleanName);

            // Get song config for volume/reverb/priority settings
            var songConfig = _midiConfigParser.GetSongConfig(baseName);
            var volume = songConfig?.VolumeNormalized ?? 1.0f;

            // Determine output paths based on category
            // SFX and Phonemes go under Audio/SFX/, everything else under Audio/Music/
            var isSfx = category is "SFX" or "Phonemes";
            var audioBaseDir = isSfx
                ? Path.Combine(_outputPath, "Audio", "SFX")
                : outputDir;
            var defsBaseDir = isSfx
                ? Path.Combine(_outputPath, "Definitions", "Assets", "Audio", "SFX")
                : Path.Combine(_outputPath, "Definitions", "Assets", "Audio", "Music");

            // Create category subdirectories
            var categoryOutputDir = Path.Combine(audioBaseDir, category);
            var categoryDefsDir = Path.Combine(defsBaseDir, category);
            Directory.CreateDirectory(categoryOutputDir);
            Directory.CreateDirectory(categoryDefsDir);

            var oggPath = Path.Combine(categoryOutputDir, $"{pascalName}.ogg");
            var result = converter.Convert(midiFile, oggPath);

            if (result.Success)
            {
                converted++;

                // Generate audio definition JSON with parsed volume
                var jsonPath = Path.Combine(categoryDefsDir, $"{pascalName}.json");
                WriteAudioDefinition(jsonPath, cleanName, pascalName, category, isSfx, result, volume);

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
    /// Strip common prefixes from filenames.
    /// </summary>
    private static string StripPrefix(string name)
    {
        if (name.StartsWith("mus_")) return name[4..];
        if (name.StartsWith("se_")) return name[3..];
        if (name.StartsWith("ph_")) return name[3..];
        return name;
    }

    /// <summary>
    /// Categorize a music file based on its name.
    /// </summary>
    private static string CategorizeMusic(string baseName)
    {
        // Sound effects
        if (baseName.StartsWith("se_"))
            return "SFX";

        // Phonemes (voice clips)
        if (baseName.StartsWith("ph_"))
            return "Phonemes";

        // Remove mus_ prefix for pattern matching
        var name = baseName.StartsWith("mus_") ? baseName[4..] : baseName;

        // Battle music (vs_* patterns)
        if (name.StartsWith("vs_") || name.StartsWith("rg_vs_") ||
            name.StartsWith("c_vs_") || // Contest battle
            name.Contains("intro_battle") || name.Contains("intro_fight"))
            return "Battle";

        // Encounter music (trainer approach)
        if (name.StartsWith("encounter_") || name.StartsWith("rg_encounter_"))
            return "Encounter";

        // Victory fanfares and jingles
        if (name.StartsWith("victory_") || name.StartsWith("rg_victory_") ||
            name.StartsWith("caught") || name.StartsWith("rg_caught") ||
            name.StartsWith("obtain_") || name.StartsWith("rg_obtain_") ||
            name is "level_up" or "evolved" or "heal" or "rg_heal" or
            "move_deleted" or "slots_jackpot" or "slots_win")
            return "Fanfares";

        // Route/Overworld travel music
        if (name.StartsWith("route") || name.StartsWith("rg_route") ||
            name.StartsWith("gsc_route") || name.StartsWith("rg_sevii") ||
            name is "cycling" or "rg_cycling" or "surf" or "rg_surf" or
            "sailing" or "follow_me" or "rg_follow_me")
            return "Routes";

        // Battle Frontier facilities
        if (name.StartsWith("b_"))
            return "Facilities";

        // Contest music
        if (name.StartsWith("contest") || name.StartsWith("link_contest"))
            return "Contest";

        // Evolution
        if (name.StartsWith("evolution") || name == "evolved")
            return "Evolution";

        // Minigames and entertainment
        if (name is "game_corner" or "rg_game_corner" or "roulette" or
            "rg_berry_pick" or "rg_poke_jump" or "rg_teachy_tv_menu" or
            "rg_teachy_tv_show" or "trick_house" or "safari_zone")
            return "Minigames";

        // Dungeons, caves, and mysterious locations
        if (name.Contains("cave") || name.Contains("tunnel") ||
            name.Contains("hideout") || name.Contains("mt_") ||
            name.Contains("tower") || name.Contains("mansion") ||
            name.Contains("sealed") || name.Contains("forest") ||
            name.Contains("dungeon") || name.Contains("victory_road") ||
            name is "underwater" or "petalburg_woods" or "rg_viridian_forest" or
            "rg_silph" or "rg_trainer_tower")
            return "Dungeons";

        // Special/Cutscene/Event music
        if (name is "credits" or "rg_credits" or "end" or "intro" or
            "title" or "rg_title" or "hall_of_fame" or "rg_hall_of_fame" or
            "hall_of_fame_room" or "new_game_intro" or "rg_new_game_intro" or
            "rg_new_game_exit" or "rg_new_game_instruct" or
            "awaken_legend" or "abnormal_weather" or "cable_car" or
            "rayquaza_appears" or "weather_groudon" or "help" or
            "register_match_call" or "too_bad" or "dummy" or
            "rg_oak" or "rg_oak_lab" or "rg_rival_exit" or
            "rg_dex_rating" or "rg_mystery_gift" or "rg_photo" or
            "rg_jigglypuff" or "rg_poke_flute" or "rg_slow_pallet" or
            "rg_game_freak" or "c_comm_center")
            return "Special";

        // Known towns/cities (Hoenn)
        if (name is "littleroot" or "littleroot_test" or "oldale" or
            "petalburg" or "rustboro" or "dewford" or "slateport" or
            "mauville" or "verdanturf" or "fallarbor" or "lavaridge" or
            "fortree" or "lilycove" or "mossdeep" or "sootopolis" or
            "pacifidlog" or "ever_grande")
            return "Towns";

        // Known towns/cities (Kanto/FRLG)
        if (name is "rg_pallet" or "rg_viridian" or "rg_pewter" or
            "rg_cerulean" or "rg_vermillion" or "rg_lavender" or
            "rg_celadon" or "rg_fuchsia" or "rg_saffron" or "rg_cinnabar" or
            "gsc_pewter")
            return "Towns";

        // Buildings and indoor locations
        if (name.Contains("center") || name.Contains("mart") ||
            name.Contains("lab") || name.Contains("gym") ||
            name.Contains("school") || name.Contains("museum") ||
            name.Contains("ship") || name.Contains("ss_anne") ||
            name.Contains("union_room") || name.Contains("net_center"))
            return "Locations";

        // Default category
        return "Other";
    }

    /// <summary>
    /// Write an AudioDefinition JSON file for a converted track.
    /// </summary>
    private void WriteAudioDefinition(string jsonPath, string cleanName, string pascalName,
        string category, bool isSfx, MidiToOggConverter.ConversionResult result, float volume = 1.0f)
    {
        var hasLoop = result.LoopStartSamples > 0 || result.LoopLengthSamples > 0;

        // ID format: {prefix}audio:{music|sfx}/{category}/{kebab-name}
        var kebabName = cleanName.Replace('_', '-');
        var categoryKebab = category.ToLowerInvariant();
        var audioType = isSfx ? "sfx" : "music";
        var id = $"{_idPrefix}audio:{audioType}/{categoryKebab}/{kebabName}";

        // Audio path: Audio/{Music|SFX}/{Category}/{PascalName}.ogg
        var audioDir = isSfx ? "SFX" : "Music";
        var audioPath = $"Audio/{audioDir}/{category}/{pascalName}.ogg";

        // SFX typically don't loop
        var shouldLoop = !isSfx && hasLoop;
        var loopSection = shouldLoop
            ? $",\n  \"loopStartSamples\": {result.LoopStartSamples},\n  \"loopLengthSamples\": {result.LoopLengthSamples}"
            : "";

        // Format volume with 2 decimal places (e.g., 0.63 for V080)
        var volumeStr = volume.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);

        var json = $$"""
{
  "id": "{{id}}",
  "name": "{{FormatSongName(cleanName)}}",
  "audioPath": "{{audioPath}}",
  "volume": {{volumeStr}},
  "loop": {{(shouldLoop ? "true" : "false")}}{{loopSection}}
}
""";

        File.WriteAllText(jsonPath, json);
    }

    /// <summary>
    /// Format a song filename into a readable name.
    /// </summary>
    private static string FormatSongName(string cleanName)
    {
        // Name is already stripped of prefix, just convert underscores to spaces
        var name = cleanName;

        // Convert underscores to spaces and title case
        return System.Globalization.CultureInfo.CurrentCulture.TextInfo
            .ToTitleCase(name.Replace('_', ' '));
    }

    /// <summary>
    /// Convert snake_case to PascalCase.
    /// </summary>
    private static string ToPascalCase(string snakeCase)
    {
        if (string.IsNullOrEmpty(snakeCase))
            return snakeCase ?? "";

        return string.Concat(
            snakeCase.Split('_', StringSplitOptions.RemoveEmptyEntries)
                .Where(part => part.Length > 0)
                .Select(part => char.ToUpperInvariant(part[0]) + (part.Length > 1 ? part[1..].ToLowerInvariant() : ""))
        );
    }

    /// <summary>
    /// Process sound effects (cries, etc).
    /// </summary>
    private void ProcessSoundEffects(string outputDir)
    {
        var criesDir = Path.Combine(_pokeemeraldPath, "sound", "direct_sound_samples", "cries");
        if (!Directory.Exists(criesDir)) return;

        // Cries go under Audio/SFX/Cries/
        var criesOutputDir = Path.Combine(_outputPath, "Audio", "SFX", "Cries");
        var criesDefsDir = Path.Combine(_outputPath, "Definitions", "Assets", "Audio", "SFX", "Cries");
        Directory.CreateDirectory(criesOutputDir);
        Directory.CreateDirectory(criesDefsDir);

        var wavFiles = Directory.GetFiles(criesDir, "*.wav");
        Console.WriteLine($"[SoundExtractor] Found {wavFiles.Length} cry files");

        foreach (var wavFile in wavFiles)
        {
            var baseName = Path.GetFileNameWithoutExtension(wavFile);
            var pascalName = ToPascalCase(baseName);

            // Copy with PascalCase name
            var destPath = Path.Combine(criesOutputDir, $"{pascalName}.wav");
            File.Copy(wavFile, destPath, overwrite: true);

            // Generate definition
            var jsonPath = Path.Combine(criesDefsDir, $"{pascalName}.json");
            WriteCryDefinition(jsonPath, baseName, pascalName);
        }

        Console.WriteLine($"[SoundExtractor] Processed {wavFiles.Length} cries");
    }

    /// <summary>
    /// Write a cry definition JSON file.
    /// </summary>
    private void WriteCryDefinition(string jsonPath, string baseName, string pascalName)
    {
        var kebabName = baseName.Replace('_', '-');
        var id = $"{_idPrefix}audio:sfx/cries/{kebabName}";

        var json = $$"""
{
  "id": "{{id}}",
  "name": "{{FormatSongName(baseName)}}",
  "audioPath": "Audio/SFX/Cries/{{pascalName}}.wav",
  "volume": 1.0,
  "loop": false
}
""";

        File.WriteAllText(jsonPath, json);
    }

    // Minimum amplitude for normalization: ±64 from center (matching PSG samples)
    // Samples quieter than this get boosted; louder samples are left alone
    private const int MinAmplitude = 64;

    /// <summary>
    /// Normalize quiet 8-bit samples by boosting them to minimum amplitude.
    /// This ensures quiet DirectSound samples can be heard alongside PSG tones.
    /// Samples already at or above the minimum amplitude are left unchanged
    /// to preserve their original character and dynamics.
    /// </summary>
    private static byte[] NormalizeSampleVolume(byte[] data)
    {
        if (data.Length == 0)
            return data;

        // Find the peak deviation from center (128)
        var maxDeviation = 0;
        foreach (var sample in data)
        {
            var deviation = Math.Abs(sample - 128);
            if (deviation > maxDeviation)
                maxDeviation = deviation;
        }

        // If sample is very quiet (near silence), leave it alone
        if (maxDeviation < 4)
            return data;

        // Only boost quiet samples - don't reduce loud ones
        // This preserves the original character of well-mastered samples
        if (maxDeviation >= MinAmplitude)
            return data;

        // Calculate boost factor to reach minimum amplitude (±64)
        var boostFactor = (double)MinAmplitude / maxDeviation;

        // Cap boost to avoid extreme amplification (max 4x)
        boostFactor = Math.Min(boostFactor, 4.0);

        var result = new byte[data.Length];
        for (var i = 0; i < data.Length; i++)
        {
            var centered = data[i] - 128;
            var scaled = (int)(centered * boostFactor);
            result[i] = (byte)Math.Clamp(scaled + 128, 0, 255);
        }

        return result;
    }
}
