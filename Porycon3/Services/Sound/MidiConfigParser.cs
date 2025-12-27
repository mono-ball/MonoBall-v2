using System.Text.RegularExpressions;

namespace Porycon3.Services.Sound;

/// <summary>
/// Parses sound/songs.mk and related configuration files from pokeemerald
/// to determine which voicegroups are used by each song.
/// </summary>
public class MidiConfigParser
{
    private readonly string _soundPath;
    private readonly Dictionary<string, SongConfig> _songs = new();

    // Pattern for song definitions in songs.mk
    // Format: $(Pokemon Emerald)/mus_dp_battle.s: INTERP=none VOICEGROUP=voicegroup000
    private static readonly Regex SongDefRegex = new(
        @"\$\(([^)]+)\)/(\w+)\.s:\s+(.+)",
        RegexOptions.Compiled);

    // Pattern for mid2agb arguments
    private static readonly Regex ParamRegex = new(
        @"(\w+)=(\w+)",
        RegexOptions.Compiled);

    // Pattern for voicegroup assignments in mid2agb invocations
    private static readonly Regex VoicegroupAssignRegex = new(
        @"VOICEGROUP=(\w+)",
        RegexOptions.Compiled);

    public MidiConfigParser(string pokeemeraldPath)
    {
        _soundPath = Path.Combine(pokeemeraldPath, "sound");
    }

    /// <summary>
    /// Parse all song configurations.
    /// </summary>
    public void ParseAll()
    {
        // Parse songs.mk which contains all song configurations
        var songsMk = Path.Combine(_soundPath, "songs.mk");
        if (File.Exists(songsMk))
        {
            ParseSongsMk(songsMk);
        }

        // Also scan the songs directory for individual .s files
        var songsDir = Path.Combine(_soundPath, "songs");
        if (Directory.Exists(songsDir))
        {
            foreach (var dir in Directory.GetDirectories(songsDir))
            {
                var songName = Path.GetFileName(dir);
                if (!_songs.ContainsKey(songName))
                {
                    // Default to voicegroup000 if not specified
                    _songs[songName] = new SongConfig
                    {
                        Name = songName,
                        VoicegroupName = "voicegroup000",
                        AssemblyPath = Path.Combine(dir, $"{songName}.s")
                    };
                }
            }
        }

        Console.WriteLine($"[MidiConfigParser] Found {_songs.Count} songs");
    }

    /// <summary>
    /// Parse the songs.mk makefile for song configurations.
    /// </summary>
    private void ParseSongsMk(string filePath)
    {
        var content = File.ReadAllText(filePath);
        var lines = content.Split('\n');

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#"))
                continue;

            // Look for song definitions
            var match = SongDefRegex.Match(trimmed);
            if (match.Success)
            {
                var gameDir = match.Groups[1].Value;
                var songName = match.Groups[2].Value;
                var params_ = match.Groups[3].Value;

                var config = new SongConfig
                {
                    Name = songName,
                    VoicegroupName = "voicegroup000", // Default
                    AssemblyPath = Path.Combine(_soundPath, "songs", songName, $"{songName}.s")
                };

                // Parse parameters
                var paramMatches = ParamRegex.Matches(params_);
                foreach (Match pm in paramMatches)
                {
                    var key = pm.Groups[1].Value;
                    var value = pm.Groups[2].Value;

                    switch (key.ToUpperInvariant())
                    {
                        case "VOICEGROUP":
                            config.VoicegroupName = value;
                            break;
                        case "INTERP":
                            config.Interpolation = value;
                            break;
                        case "MODT":
                            config.ModType = value;
                            break;
                        case "REVERB":
                            config.Reverb = int.TryParse(value, out var r) ? r : 0;
                            break;
                    }
                }

                _songs[songName] = config;
            }
        }
    }

    /// <summary>
    /// Get configuration for a specific song.
    /// </summary>
    public SongConfig? GetSongConfig(string songName)
    {
        return _songs.TryGetValue(songName, out var config) ? config : null;
    }

    /// <summary>
    /// Get all song configurations.
    /// </summary>
    public IReadOnlyDictionary<string, SongConfig> GetAllSongs() => _songs;

    /// <summary>
    /// Get all unique voicegroups used by songs.
    /// </summary>
    public HashSet<string> GetUsedVoicegroups()
    {
        return _songs.Values
            .Select(s => s.VoicegroupName)
            .ToHashSet();
    }

    /// <summary>
    /// Get all songs using a specific voicegroup.
    /// </summary>
    public List<SongConfig> GetSongsByVoicegroup(string voicegroupName)
    {
        return _songs.Values
            .Where(s => s.VoicegroupName.Equals(voicegroupName, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }
}

/// <summary>
/// Configuration for a single song.
/// </summary>
public class SongConfig
{
    public required string Name { get; init; }
    public required string VoicegroupName { get; set; }
    public string? AssemblyPath { get; set; }
    public string? Interpolation { get; set; }
    public string? ModType { get; set; }
    public int Reverb { get; set; }
}
