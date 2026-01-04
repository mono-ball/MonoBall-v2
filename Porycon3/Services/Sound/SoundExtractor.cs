using Porycon3.Services.Extraction;

namespace Porycon3.Services.Sound;

/// <summary>
/// Extracts and converts pokeemerald sound data including music, SFX, and Pokemon cries.
/// Builds SF2 soundfonts from actual game samples for accurate music playback.
/// </summary>
public class SoundExtractor : ExtractorBase
{
    public override string Name => "Sound";
    public override string Description => "Extracts music, SFX, and Pokemon cries";

    private readonly VoicegroupParser _voicegroupParser;
    private readonly MidiConfigParser _midiConfigParser;
    private readonly PsgSampleGenerator _psgGenerator;

    // Sample cache to avoid loading duplicates
    private readonly Dictionary<string, int> _sampleIndexCache = new();

    public SoundExtractor(string inputPath, string outputPath, bool verbose = false)
        : base(inputPath, outputPath, verbose)
    {
        _voicegroupParser = new VoicegroupParser(inputPath);
        _midiConfigParser = new MidiConfigParser(inputPath);
        _psgGenerator = new PsgSampleGenerator();
    }

    protected override int ExecuteExtraction()
    {
        var audioDir = Path.Combine(OutputPath, "Audio");
        var musicOutputDir = Path.Combine(audioDir, "Music");
        EnsureDirectory(audioDir);
        EnsureDirectory(musicOutputDir);

        // Parse voicegroups and song configs (0-15%)
        LogVerbose("Parsing voicegroups...");
        _voicegroupParser.ParseAll();
        SetCount("Voicegroups", _voicegroupParser.GetAllVoicegroups().Count);
        ReportProgress(0.10);

        LogVerbose("Parsing song configurations...");
        _midiConfigParser.ParseAll();
        ReportProgress(0.15);

        // Build the master SF2 soundfont (15-40%)
        LogVerbose("Building soundfont...");
        var sf2Path = Path.Combine(audioDir, "pokemon_emerald.sf2");
        BuildSoundfont(sf2Path);
        ReportProgress(0.40);

        // Convert MIDI files to OGG with loop support (40-80%)
        LogVerbose("Converting MIDI files to OGG...");
        var musicCount = ConvertMidiToOgg(musicOutputDir, sf2Path);
        SetCount("Music Tracks", musicCount);
        ReportProgress(0.80);

        // Generate SFX (cries) (80-100%)
        LogVerbose("Processing sound effects...");
        var sfxCount = ProcessSoundEffects();
        SetCount("Sound Effects", sfxCount);
        ReportProgress(1.0);

        return musicCount + sfxCount;
    }

    /// <summary>
    /// Build the SF2 soundfont from all voicegroups.
    /// </summary>
    private void BuildSoundfont(string outputPath)
    {
        var sf2 = new Sf2Builder();
        var allVoicegroups = _voicegroupParser.GetAllVoicegroups();

        // Load PCM samples
        var allSamples = _voicegroupParser.GetAllReferencedSamples();
        LogVerbose($"Loading {allSamples.Count} PCM samples...");

        foreach (var sampleName in allSamples)
        {
            LoadAndAddSample(sf2, sampleName);
        }

        // Add PSG samples
        LogVerbose("Generating PSG samples...");
        AddPsgSamples(sf2);

        // Build master voicegroup
        LogVerbose($"Building master voicegroup from {allVoicegroups.Count} voicegroups...");
        var masterVoicegroup = BuildMasterVoicegroup(allVoicegroups);

        // Build instruments into bank 0
        BuildVoicegroupInstruments(sf2, masterVoicegroup, 0);

        // Write the SF2 file
        sf2.Write(outputPath);
        SetCount("Samples", _sampleIndexCache.Count);
    }

    private Voicegroup BuildMasterVoicegroup(IReadOnlyDictionary<string, Voicegroup> allVoicegroups)
    {
        var master = new Voicegroup { Name = "master" };

        var songVoicegroups = allVoicegroups.Values
            .Where(vg => !vg.Name.Contains("keysplit") && !vg.Name.Contains("drumset"))
            .ToList();

        for (var program = 0; program < 128; program++)
        {
            var voiceCounts = new Dictionary<string, (int count, VoiceDefinition voice)>();

            foreach (var vg in songVoicegroups)
            {
                var voice = vg.GetVoice(program);
                if (voice == null) continue;

                var key = GetVoiceKey(voice);
                if (!voiceCounts.ContainsKey(key))
                    voiceCounts[key] = (0, voice);
                voiceCounts[key] = (voiceCounts[key].count + 1, voice);
            }

            if (voiceCounts.Count > 0)
            {
                var mostCommon = voiceCounts.MaxBy(kv => kv.Value.count);
                master.Voices[program] = mostCommon.Value.voice;
            }
        }

        return master;
    }

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

    private void LoadAndAddSample(Sf2Builder sf2, string sampleName)
    {
        if (_sampleIndexCache.ContainsKey(sampleName))
            return;

        var baseName = sampleName;
        if (baseName.StartsWith("DirectSoundWaveData_"))
            baseName = baseName["DirectSoundWaveData_".Length..];

        var samplesDir = Path.Combine(InputPath, "sound", "direct_sound_samples");

        // Handle phonemes
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

        var samplePaths = new[]
        {
            Path.Combine(samplesDir, $"{baseName}.wav"),
            Path.Combine(samplesDir, "cries", $"{baseName}.wav"),
            Path.Combine(samplesDir, "phonemes", $"{baseName}.wav"),
            Path.Combine(samplesDir, $"{sampleName}.wav"),
        };

        foreach (var samplePath in samplePaths)
        {
            if (TryLoadSample(sf2, sampleName, samplePath))
                return;
        }

        // Add placeholder for missing samples
        var placeholderData = new byte[1000];
        Array.Fill(placeholderData, (byte)128);
        var placeholderIndex = sf2.AddSample(sampleName, placeholderData, 22050, 60);
        _sampleIndexCache[sampleName] = placeholderIndex;
    }

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
            LogWarning($"Failed to load {samplePath}: {ex.Message}");
            return false;
        }
    }

    private (byte[] data, int sampleRate, int rootKey, int loopStart, int loopEnd) LoadWavFile(string path)
    {
        using var fs = File.OpenRead(path);
        using var br = new BinaryReader(fs);

        var riff = new string(br.ReadChars(4));
        if (riff != "RIFF")
            throw new InvalidDataException($"Not a valid WAV file: {path}");

        br.ReadInt32();
        var wave = new string(br.ReadChars(4));
        if (wave != "WAVE")
            throw new InvalidDataException($"Not a valid WAV file: {path}");

        int sampleRate = 22050;
        short bitsPerSample = 8;
        short channels = 1;
        byte[]? data = null;
        int loopStart = -1;
        int loopEnd = -1;

        while (fs.Position < fs.Length)
        {
            var chunkId = new string(br.ReadChars(4));
            var chunkSize = br.ReadInt32();

            switch (chunkId)
            {
                case "fmt ":
                    br.ReadInt16();
                    channels = br.ReadInt16();
                    sampleRate = br.ReadInt32();
                    br.ReadInt32();
                    br.ReadInt16();
                    bitsPerSample = br.ReadInt16();
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
                        var samples16 = new short[chunkSize / 2];
                        for (var i = 0; i < samples16.Length; i++)
                            samples16[i] = br.ReadInt16();

                        data = new byte[samples16.Length];
                        for (var i = 0; i < samples16.Length; i++)
                            data[i] = (byte)((samples16[i] / 256) + 128);
                    }
                    else
                    {
                        throw new NotSupportedException($"Unsupported bit depth: {bitsPerSample}");
                    }
                    break;

                case "smpl":
                    var smplChunkEnd = fs.Position + chunkSize;
                    if (chunkSize >= 36)
                    {
                        br.ReadBytes(28);
                        var loopCount = br.ReadInt32();
                        br.ReadInt32();

                        if (loopCount > 0 && chunkSize >= 60)
                        {
                            br.ReadInt32();
                            br.ReadInt32();
                            loopStart = br.ReadInt32();
                            loopEnd = br.ReadInt32();
                        }
                    }
                    if (fs.Position < smplChunkEnd)
                        br.ReadBytes((int)(smplChunkEnd - fs.Position));
                    break;

                default:
                    if (chunkSize > 0 && fs.Position + chunkSize <= fs.Length)
                        br.ReadBytes(chunkSize);
                    break;
            }
        }

        if (data == null)
            throw new InvalidDataException($"No data chunk found in WAV file: {path}");

        data = NormalizeSampleVolume(data);

        if (channels == 2)
        {
            var monoData = new byte[data.Length / 2];
            for (var i = 0; i < monoData.Length; i++)
                monoData[i] = (byte)((data[i * 2] + data[i * 2 + 1]) / 2);
            data = monoData;
        }

        return (data, sampleRate, 60, loopStart, loopEnd);
    }

    private const int PsgSampleRate = 13379;

    private void AddPsgSamples(Sf2Builder sf2)
    {
        foreach (DutyCycle duty in Enum.GetValues<DutyCycle>())
        {
            var name = $"psg_square_{duty}";
            var data = _psgGenerator.GenerateSquareWave(duty);
            var index = sf2.AddSample(name, data, PsgSampleRate, 60, 0, data.Length);
            _sampleIndexCache[name] = index;
        }

        var waveSamplesDir = Path.Combine(InputPath, "sound", "programmable_wave_samples");
        for (var i = 1; i <= 25; i++)
        {
            var pcmPath = Path.Combine(waveSamplesDir, $"{i:D2}.pcm");
            if (File.Exists(pcmPath))
            {
                try
                {
                    var rawData = File.ReadAllBytes(pcmPath);
                    var waveData = PsgSampleGenerator.ParseProgrammableWaveData(rawData);
                    var sampleData = _psgGenerator.GenerateProgrammableWave(waveData);

                    var name = $"psg_wave_{i}";
                    var index = sf2.AddSample(name, sampleData, PsgSampleRate, 60, 0, sampleData.Length);
                    _sampleIndexCache[name] = index;
                }
                catch (Exception ex)
                {
                    LogWarning($"Failed to load wave {i}: {ex.Message}");
                }
            }
        }

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

        var noiseShort = _psgGenerator.GenerateNoise(true);
        var noiseLong = _psgGenerator.GenerateNoise(false);
        _sampleIndexCache["psg_noise_short"] = sf2.AddSample("psg_noise_short", noiseShort, PsgSampleRate, 60);
        _sampleIndexCache["psg_noise_long"] = sf2.AddSample("psg_noise_long", noiseLong, PsgSampleRate, 60);
    }

    private string GetProgrammableWaveSampleName(string waveName)
    {
        if (waveName.StartsWith("ProgrammableWaveData_"))
        {
            var numPart = waveName["ProgrammableWaveData_".Length..];
            if (int.TryParse(numPart, out var waveNum) && _sampleIndexCache.ContainsKey($"psg_wave_{waveNum}"))
                return $"psg_wave_{waveNum}";
        }
        return "psg_wave_sine";
    }

    private void BuildVoicegroupInstruments(Sf2Builder sf2, Voicegroup voicegroup, int bank)
    {
        for (var program = 0; program < 128; program++)
        {
            var voice = voicegroup.GetVoice(program);
            if (voice == null) continue;

            var instrumentIndex = BuildVoiceInstrument(sf2, voice, $"prog_{program:D3}");
            if (instrumentIndex >= 0)
                sf2.AddPreset($"Program {program}", bank, program, instrumentIndex);
        }
    }

    private int BuildVoiceInstrument(Sf2Builder sf2, VoiceDefinition voice, string name)
    {
        switch (voice)
        {
            case DirectSoundVoice ds:
                if (_sampleIndexCache.TryGetValue(ds.SampleName, out var sampleIndex))
                    return sf2.AddSimpleInstrument(name, sampleIndex, ds.Envelope, ds.BaseMidiKey);
                break;

            case Square1Voice sq1:
                var sq1SampleName = $"psg_square_{sq1.DutyCycle}";
                if (_sampleIndexCache.TryGetValue(sq1SampleName, out var sq1Index))
                    return sf2.AddSimpleInstrument(name, sq1Index, sq1.Envelope, sq1.BaseMidiKey);
                break;

            case Square2Voice sq2:
                var sq2SampleName = $"psg_square_{sq2.DutyCycle}";
                if (_sampleIndexCache.TryGetValue(sq2SampleName, out var sq2Index))
                    return sf2.AddSimpleInstrument(name, sq2Index, sq2.Envelope, sq2.BaseMidiKey);
                break;

            case ProgrammableWaveVoice pw:
                var waveSampleName = GetProgrammableWaveSampleName(pw.WaveName);
                if (_sampleIndexCache.TryGetValue(waveSampleName, out var pwIndex))
                    return sf2.AddSimpleInstrument(name, pwIndex, pw.Envelope, pw.BaseMidiKey);
                break;

            case NoiseVoice nv:
                var noiseSampleName = nv.Period == 1 ? "psg_noise_short" : "psg_noise_long";
                if (_sampleIndexCache.TryGetValue(noiseSampleName, out var noiseIndex))
                    return sf2.AddSimpleInstrument(name, noiseIndex, nv.Envelope, nv.BaseMidiKey);
                break;

            case KeysplitVoice ks:
                var keysplitVg = _voicegroupParser.GetVoicegroup(ks.VoicegroupName);
                var keysplitTable = _voicegroupParser.GetKeysplitTable(ks.KeysplitTableName);
                if (keysplitVg != null && keysplitTable != null)
                    return BuildKeysplitInstrument(sf2, keysplitVg, keysplitTable, name);
                break;

            case KeysplitAllVoice ksa:
                var drumVg = _voicegroupParser.GetVoicegroup(ksa.VoicegroupName);
                if (drumVg != null)
                    return BuildDrumkitInstrument(sf2, drumVg, name);
                break;
        }

        return -1;
    }

    private int BuildKeysplitInstrument(Sf2Builder sf2, Voicegroup voicegroup, KeysplitTable table, string name)
    {
        var zones = new List<Sf2Zone>();
        var lowKey = table.StartKey;

        foreach (var entry in table.Entries)
        {
            var voice = voicegroup.GetVoice(entry.VoiceIndex);
            if (voice is DirectSoundVoice ds && _sampleIndexCache.TryGetValue(ds.SampleName, out var sampleIndex))
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
            lowKey = entry.HighKey + 1;
        }

        return zones.Count > 0 ? sf2.AddKeysplitInstrument(name, zones) : -1;
    }

    private int BuildDrumkitInstrument(Sf2Builder sf2, Voicegroup voicegroup, string name)
    {
        var zones = new List<Sf2Zone>();

        for (var note = 0; note < 128; note++)
        {
            var voice = voicegroup.GetVoice(note);
            if (voice is DirectSoundVoice ds && _sampleIndexCache.TryGetValue(ds.SampleName, out var sampleIndex))
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

        return zones.Count > 0 ? sf2.AddKeysplitInstrument(name, zones) : -1;
    }

    private int ConvertMidiToOgg(string outputDir, string sf2Path)
    {
        var midiSourceDir = Path.Combine(InputPath, "sound", "songs", "midi");
        if (!Directory.Exists(midiSourceDir)) return 0;

        var midiFiles = Directory.GetFiles(midiSourceDir, "*.mid");
        LogVerbose($"Found {midiFiles.Length} MIDI files to convert");

        var converter = new MidiToOggConverter(sf2Path);
        var converted = 0;

        foreach (var midiFile in midiFiles)
        {
            var baseName = Path.GetFileNameWithoutExtension(midiFile);
            var category = CategorizeMusic(baseName);
            var cleanName = StripPrefix(baseName);
            var pascalName = ToPascalCase(cleanName);

            var songConfig = _midiConfigParser.GetSongConfig(baseName);
            var volume = songConfig?.VolumeNormalized ?? 1.0f;

            var isSfx = category is "SFX" or "Phonemes";
            var audioBaseDir = isSfx
                ? Path.Combine(OutputPath, "Audio", "SFX")
                : outputDir;
            var defsBaseDir = isSfx
                ? Path.Combine(OutputPath, "Definitions", "Assets", "Audio", "SFX")
                : Path.Combine(OutputPath, "Definitions", "Assets", "Audio", "Music");

            var categoryOutputDir = Path.Combine(audioBaseDir, category);
            var categoryDefsDir = Path.Combine(defsBaseDir, category);
            EnsureDirectory(categoryOutputDir);
            EnsureDirectory(categoryDefsDir);

            var oggPath = Path.Combine(categoryOutputDir, $"{pascalName}.ogg");
            var result = converter.Convert(midiFile, oggPath);

            if (result.Success)
            {
                converted++;
                var jsonPath = Path.Combine(categoryDefsDir, $"{pascalName}.json");
                WriteAudioDefinition(jsonPath, cleanName, pascalName, category, isSfx, result, volume);
            }
            else
            {
                AddError(baseName, "MIDI conversion failed");
            }
        }

        return converted;
    }

    private static string StripPrefix(string name)
    {
        if (name.StartsWith("mus_")) return name[4..];
        if (name.StartsWith("se_")) return name[3..];
        if (name.StartsWith("ph_")) return name[3..];
        return name;
    }

    private static string CategorizeMusic(string baseName)
    {
        if (baseName.StartsWith("se_")) return "SFX";
        if (baseName.StartsWith("ph_")) return "Phonemes";

        var name = baseName.StartsWith("mus_") ? baseName[4..] : baseName;

        if (name.StartsWith("vs_") || name.StartsWith("rg_vs_") ||
            name.StartsWith("c_vs_") || name.Contains("intro_battle") || name.Contains("intro_fight"))
            return "Battle";

        if (name.StartsWith("encounter_") || name.StartsWith("rg_encounter_"))
            return "Encounter";

        if (name.StartsWith("victory_") || name.StartsWith("rg_victory_") ||
            name.StartsWith("caught") || name.StartsWith("rg_caught") ||
            name.StartsWith("obtain_") || name.StartsWith("rg_obtain_") ||
            name is "level_up" or "evolved" or "heal" or "rg_heal" or
            "move_deleted" or "slots_jackpot" or "slots_win")
            return "Fanfares";

        if (name.StartsWith("route") || name.StartsWith("rg_route") ||
            name.StartsWith("gsc_route") || name.StartsWith("rg_sevii") ||
            name is "cycling" or "rg_cycling" or "surf" or "rg_surf" or
            "sailing" or "follow_me" or "rg_follow_me")
            return "Routes";

        if (name.StartsWith("b_")) return "Facilities";
        if (name.StartsWith("contest") || name.StartsWith("link_contest")) return "Contest";
        if (name.StartsWith("evolution") || name == "evolved") return "Evolution";

        if (name is "game_corner" or "rg_game_corner" or "roulette" or
            "rg_berry_pick" or "rg_poke_jump" or "rg_teachy_tv_menu" or
            "rg_teachy_tv_show" or "trick_house" or "safari_zone")
            return "Minigames";

        if (name.Contains("cave") || name.Contains("tunnel") ||
            name.Contains("hideout") || name.Contains("mt_") ||
            name.Contains("tower") || name.Contains("mansion") ||
            name.Contains("sealed") || name.Contains("forest") ||
            name.Contains("dungeon") || name.Contains("victory_road") ||
            name is "underwater" or "petalburg_woods" or "rg_viridian_forest" or
            "rg_silph" or "rg_trainer_tower")
            return "Dungeons";

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

        if (name is "littleroot" or "littleroot_test" or "oldale" or
            "petalburg" or "rustboro" or "dewford" or "slateport" or
            "mauville" or "verdanturf" or "fallarbor" or "lavaridge" or
            "fortree" or "lilycove" or "mossdeep" or "sootopolis" or
            "pacifidlog" or "ever_grande" or
            "rg_pallet" or "rg_viridian" or "rg_pewter" or
            "rg_cerulean" or "rg_vermillion" or "rg_lavender" or
            "rg_celadon" or "rg_fuchsia" or "rg_saffron" or "rg_cinnabar" or
            "gsc_pewter")
            return "Towns";

        if (name.Contains("center") || name.Contains("mart") ||
            name.Contains("lab") || name.Contains("gym") ||
            name.Contains("school") || name.Contains("museum") ||
            name.Contains("ship") || name.Contains("ss_anne") ||
            name.Contains("union_room") || name.Contains("net_center"))
            return "Locations";

        return "Other";
    }

    private void WriteAudioDefinition(string jsonPath, string cleanName, string pascalName,
        string category, bool isSfx, MidiToOggConverter.ConversionResult result, float volume = 1.0f)
    {
        var hasLoop = result.LoopStartSamples > 0 || result.LoopLengthSamples > 0;
        var kebabName = cleanName.Replace('_', '-').ToLowerInvariant();
        var categoryKebab = category.ToLowerInvariant();
        var audioType = isSfx ? "sfx" : "music";
        var id = $"{IdTransformer.Namespace}:audio:{audioType}/{categoryKebab}/{kebabName}";

        var audioDir = isSfx ? "SFX" : "Music";
        var audioPath = $"Audio/{audioDir}/{category}/{pascalName}.ogg";

        var shouldLoop = !isSfx && hasLoop;
        var loopSection = shouldLoop
            ? $",\n  \"loopStartSamples\": {result.LoopStartSamples},\n  \"loopLengthSamples\": {result.LoopLengthSamples}"
            : "";

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

    private static string FormatSongName(string cleanName)
    {
        return System.Globalization.CultureInfo.CurrentCulture.TextInfo
            .ToTitleCase(cleanName.Replace('_', ' '));
    }

    private static string ToPascalCase(string snakeCase) => IdTransformer.ToPascalCase(snakeCase);

    private int ProcessSoundEffects()
    {
        var criesDir = Path.Combine(InputPath, "sound", "direct_sound_samples", "cries");
        if (!Directory.Exists(criesDir)) return 0;

        var criesOutputDir = Path.Combine(OutputPath, "Audio", "SFX", "Cries");
        var criesDefsDir = Path.Combine(OutputPath, "Definitions", "Assets", "Audio", "SFX", "Cries");
        EnsureDirectory(criesOutputDir);
        EnsureDirectory(criesDefsDir);

        var wavFiles = Directory.GetFiles(criesDir, "*.wav");
        LogVerbose($"Found {wavFiles.Length} cry files");

        foreach (var wavFile in wavFiles)
        {
            var baseName = Path.GetFileNameWithoutExtension(wavFile);
            var pascalName = ToPascalCase(baseName);

            var destPath = Path.Combine(criesOutputDir, $"{pascalName}.wav");
            File.Copy(wavFile, destPath, overwrite: true);

            var jsonPath = Path.Combine(criesDefsDir, $"{pascalName}.json");
            WriteCryDefinition(jsonPath, baseName, pascalName);
        }

        return wavFiles.Length;
    }

    private void WriteCryDefinition(string jsonPath, string baseName, string pascalName)
    {
        var kebabName = baseName.Replace('_', '-').ToLowerInvariant();
        var id = $"{IdTransformer.Namespace}:audio:sfx/cries/{kebabName}";

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

    private const int MinAmplitude = 64;

    private static byte[] NormalizeSampleVolume(byte[] data)
    {
        if (data.Length == 0)
            return data;

        var maxDeviation = 0;
        foreach (var sample in data)
        {
            var deviation = Math.Abs(sample - 128);
            if (deviation > maxDeviation)
                maxDeviation = deviation;
        }

        if (maxDeviation < 4 || maxDeviation >= MinAmplitude)
            return data;

        var boostFactor = Math.Min((double)MinAmplitude / maxDeviation, 4.0);

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
