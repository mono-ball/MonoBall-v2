using System.Text.RegularExpressions;

namespace Porycon3.Services.Sound;

/// <summary>
/// Parses voicegroup .inc files from pokeemerald.
/// </summary>
public class VoicegroupParser
{
    private readonly string _soundPath;
    private readonly Dictionary<string, Voicegroup> _voicegroups = new();
    private readonly Dictionary<string, KeysplitTable> _keysplitTables = new();

    // Regex patterns for voice definitions
    private static readonly Regex VoiceGroupStartRegex = new(
        @"^voice_group\s+(\w+)",
        RegexOptions.Compiled);

    private static readonly Regex DirectSoundRegex = new(
        @"voice_directsound(?:_no_resample|_alt)?\s+(\d+),\s*(\d+),\s*(\w+),\s*(\d+),\s*(\d+),\s*(\d+),\s*(\d+)",
        RegexOptions.Compiled);

    private static readonly Regex Square1Regex = new(
        @"voice_square_1(?:_alt)?\s+(\d+),\s*(\d+),\s*(\d+),\s*(\d+),\s*(\d+),\s*(\d+),\s*(\d+),\s*(\d+)",
        RegexOptions.Compiled);

    private static readonly Regex Square2Regex = new(
        @"voice_square_2(?:_alt)?\s+(\d+),\s*(\d+),\s*(\d+),\s*(\d+),\s*(\d+),\s*(\d+),\s*(\d+)",
        RegexOptions.Compiled);

    private static readonly Regex ProgrammableWaveRegex = new(
        @"voice_programmable_wave(?:_alt)?\s+(\d+),\s*(\d+),\s*(\w+),\s*(\d+),\s*(\d+),\s*(\d+),\s*(\d+)",
        RegexOptions.Compiled);

    private static readonly Regex NoiseRegex = new(
        @"voice_noise(?:_alt)?\s+(\d+),\s*(\d+),\s*(\d+),\s*(\d+),\s*(\d+),\s*(\d+),\s*(\d+)",
        RegexOptions.Compiled);

    private static readonly Regex KeysplitRegex = new(
        @"voice_keysplit\s+(\w+),\s*(\w+)",
        RegexOptions.Compiled);

    private static readonly Regex KeysplitAllRegex = new(
        @"voice_keysplit_all\s+(\w+)",
        RegexOptions.Compiled);

    // Keysplit table patterns
    private static readonly Regex KeysplitTableStartRegex = new(
        @"^keysplit\s+(\w+),\s*(\d+)",
        RegexOptions.Compiled);

    private static readonly Regex KeysplitSplitRegex = new(
        @"split\s+(\d+),\s*(\d+)",
        RegexOptions.Compiled);

    public VoicegroupParser(string pokeemeraldPath)
    {
        _soundPath = Path.Combine(pokeemeraldPath, "sound");
    }

    /// <summary>
    /// Parse all voicegroups and keysplit tables.
    /// </summary>
    public void ParseAll()
    {
        // First parse keysplit tables
        ParseKeysplitTables();

        var voicegroupsDir = Path.Combine(_soundPath, "voicegroups");
        if (Directory.Exists(voicegroupsDir))
        {
            // Parse shared voicegroups first (keysplits, drumsets)
            var keysplitsDir = Path.Combine(voicegroupsDir, "keysplits");
            if (Directory.Exists(keysplitsDir))
            {
                foreach (var file in Directory.GetFiles(keysplitsDir, "*.inc"))
                {
                    ParseVoicegroupFile(file);
                }
            }

            var drumsetsDir = Path.Combine(voicegroupsDir, "drumsets");
            if (Directory.Exists(drumsetsDir))
            {
                foreach (var file in Directory.GetFiles(drumsetsDir, "*.inc"))
                {
                    ParseVoicegroupFile(file);
                }
            }

            // Then parse song-specific voicegroup files
            foreach (var file in Directory.GetFiles(voicegroupsDir, "*.inc"))
            {
                ParseVoicegroupFile(file);
            }
        }

        // Also parse the main voice_groups.inc if it exists
        var mainFile = Path.Combine(_soundPath, "voice_groups.inc");
        if (File.Exists(mainFile))
        {
            // This file includes other files, parse it for structure
            ParseVoiceGroupsInc(mainFile);
        }

        // Metrics tracked by SoundExtractor via SetCount
    }

    /// <summary>
    /// Parse keysplit_tables.inc for note range mappings.
    /// </summary>
    private void ParseKeysplitTables()
    {
        var filePath = Path.Combine(_soundPath, "keysplit_tables.inc");
        if (!File.Exists(filePath)) return;

        var lines = File.ReadAllLines(filePath);
        KeysplitTable? currentTable = null;

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("@")) continue;

            var tableMatch = KeysplitTableStartRegex.Match(trimmed);
            if (tableMatch.Success)
            {
                if (currentTable != null)
                {
                    _keysplitTables[currentTable.Name] = currentTable;
                }
                currentTable = new KeysplitTable
                {
                    Name = $"keysplit_{tableMatch.Groups[1].Value}",
                    StartKey = int.Parse(tableMatch.Groups[2].Value)
                };
                continue;
            }

            if (currentTable != null)
            {
                var splitMatch = KeysplitSplitRegex.Match(trimmed);
                if (splitMatch.Success)
                {
                    currentTable.Entries.Add(new KeysplitEntry(
                        int.Parse(splitMatch.Groups[1].Value),
                        int.Parse(splitMatch.Groups[2].Value)
                    ));
                }
            }
        }

        if (currentTable != null)
        {
            _keysplitTables[currentTable.Name] = currentTable;
        }
    }

    /// <summary>
    /// Parse a single voicegroup .inc file.
    /// </summary>
    private void ParseVoicegroupFile(string filePath)
    {
        var lines = File.ReadAllLines(filePath);
        Voicegroup? currentGroup = null;
        var voiceIndex = 0;

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("@")) continue;

            // Check for voicegroup start
            var groupMatch = VoiceGroupStartRegex.Match(trimmed);
            if (groupMatch.Success)
            {
                if (currentGroup != null)
                {
                    _voicegroups[currentGroup.Name] = currentGroup;
                }
                currentGroup = new Voicegroup { Name = $"voicegroup_{groupMatch.Groups[1].Value}" };
                voiceIndex = 0;
                continue;
            }

            if (currentGroup == null) continue;

            // Parse voice definitions
            var voice = ParseVoiceLine(trimmed);
            if (voice != null && voiceIndex < 128)
            {
                currentGroup.Voices[voiceIndex] = voice;
                voiceIndex++;
            }
        }

        if (currentGroup != null)
        {
            _voicegroups[currentGroup.Name] = currentGroup;
        }
    }

    /// <summary>
    /// Parse the main voice_groups.inc file for .include directives.
    /// </summary>
    private void ParseVoiceGroupsInc(string filePath)
    {
        // This file contains .include statements pointing to other files
        // Already handled by parsing the voicegroups directory
    }

    /// <summary>
    /// Parse a single voice definition line.
    /// </summary>
    private VoiceDefinition? ParseVoiceLine(string line)
    {
        // DirectSound
        var match = DirectSoundRegex.Match(line);
        if (match.Success)
        {
            return new DirectSoundVoice
            {
                Type = VoiceType.DirectSound,
                BaseMidiKey = int.Parse(match.Groups[1].Value),
                Pan = int.Parse(match.Groups[2].Value),
                SampleName = match.Groups[3].Value,
                Envelope = new VoiceEnvelope(
                    int.Parse(match.Groups[4].Value),
                    int.Parse(match.Groups[5].Value),
                    int.Parse(match.Groups[6].Value),
                    int.Parse(match.Groups[7].Value)
                )
            };
        }

        // Square1
        match = Square1Regex.Match(line);
        if (match.Success)
        {
            return new Square1Voice
            {
                Type = VoiceType.Square1,
                BaseMidiKey = int.Parse(match.Groups[1].Value),
                Pan = int.Parse(match.Groups[2].Value),
                Sweep = int.Parse(match.Groups[3].Value),
                DutyCycle = (DutyCycle)int.Parse(match.Groups[4].Value),
                Envelope = new VoiceEnvelope(
                    int.Parse(match.Groups[5].Value),
                    int.Parse(match.Groups[6].Value),
                    int.Parse(match.Groups[7].Value),
                    int.Parse(match.Groups[8].Value)
                )
            };
        }

        // Square2
        match = Square2Regex.Match(line);
        if (match.Success)
        {
            return new Square2Voice
            {
                Type = VoiceType.Square2,
                BaseMidiKey = int.Parse(match.Groups[1].Value),
                Pan = int.Parse(match.Groups[2].Value),
                DutyCycle = (DutyCycle)int.Parse(match.Groups[3].Value),
                Envelope = new VoiceEnvelope(
                    int.Parse(match.Groups[4].Value),
                    int.Parse(match.Groups[5].Value),
                    int.Parse(match.Groups[6].Value),
                    int.Parse(match.Groups[7].Value)
                )
            };
        }

        // Programmable Wave
        match = ProgrammableWaveRegex.Match(line);
        if (match.Success)
        {
            return new ProgrammableWaveVoice
            {
                Type = VoiceType.ProgrammableWave,
                BaseMidiKey = int.Parse(match.Groups[1].Value),
                Pan = int.Parse(match.Groups[2].Value),
                WaveName = match.Groups[3].Value,
                Envelope = new VoiceEnvelope(
                    int.Parse(match.Groups[4].Value),
                    int.Parse(match.Groups[5].Value),
                    int.Parse(match.Groups[6].Value),
                    int.Parse(match.Groups[7].Value)
                )
            };
        }

        // Noise
        match = NoiseRegex.Match(line);
        if (match.Success)
        {
            return new NoiseVoice
            {
                Type = VoiceType.Noise,
                BaseMidiKey = int.Parse(match.Groups[1].Value),
                Pan = int.Parse(match.Groups[2].Value),
                Period = int.Parse(match.Groups[3].Value),
                Envelope = new VoiceEnvelope(
                    int.Parse(match.Groups[4].Value),
                    int.Parse(match.Groups[5].Value),
                    int.Parse(match.Groups[6].Value),
                    int.Parse(match.Groups[7].Value)
                )
            };
        }

        // Keysplit
        match = KeysplitRegex.Match(line);
        if (match.Success)
        {
            return new KeysplitVoice
            {
                Type = VoiceType.Keysplit,
                BaseMidiKey = 60,
                Pan = 0,
                VoicegroupName = match.Groups[1].Value,
                KeysplitTableName = match.Groups[2].Value,
                Envelope = new VoiceEnvelope(0, 0, 0, 0)
            };
        }

        // Keysplit All
        match = KeysplitAllRegex.Match(line);
        if (match.Success)
        {
            return new KeysplitAllVoice
            {
                Type = VoiceType.KeysplitAll,
                BaseMidiKey = 60,
                Pan = 0,
                VoicegroupName = match.Groups[1].Value,
                Envelope = new VoiceEnvelope(0, 0, 0, 0)
            };
        }

        return null;
    }

    /// <summary>
    /// Get a parsed voicegroup by name.
    /// </summary>
    public Voicegroup? GetVoicegroup(string name)
    {
        // Normalize name
        var normalized = name.StartsWith("voicegroup_") ? name : $"voicegroup_{name}";
        return _voicegroups.TryGetValue(normalized, out var vg) ? vg : null;
    }

    /// <summary>
    /// Get a keysplit table by name.
    /// </summary>
    public KeysplitTable? GetKeysplitTable(string name)
    {
        var normalized = name.StartsWith("keysplit_") ? name : $"keysplit_{name}";
        return _keysplitTables.TryGetValue(normalized, out var table) ? table : null;
    }

    /// <summary>
    /// Get all parsed voicegroups.
    /// </summary>
    public IReadOnlyDictionary<string, Voicegroup> GetAllVoicegroups() => _voicegroups;

    /// <summary>
    /// Get all keysplit tables.
    /// </summary>
    public IReadOnlyDictionary<string, KeysplitTable> GetAllKeysplitTables() => _keysplitTables;

    /// <summary>
    /// Get all unique sample names referenced across all voicegroups.
    /// </summary>
    public HashSet<string> GetAllReferencedSamples()
    {
        var samples = new HashSet<string>();

        foreach (var vg in _voicegroups.Values)
        {
            foreach (var voice in vg.Voices)
            {
                if (voice is DirectSoundVoice ds)
                {
                    samples.Add(ds.SampleName);
                }
            }
        }

        return samples;
    }
}
