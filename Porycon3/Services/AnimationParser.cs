using System.Text.RegularExpressions;

namespace Porycon3.Services;

/// <summary>
/// Parses animation metadata from pokeemerald source code.
/// </summary>
public class AnimationParser
{
    private readonly string _pokeemeraldPath;
    private readonly bool _verbose;

    public AnimationParser(string pokeemeraldPath, bool verbose = false)
    {
        _pokeemeraldPath = pokeemeraldPath;
        _verbose = verbose;
    }

    /// <summary>
    /// Parse all animation data from pokeemerald source.
    /// </summary>
    public AnimationData ParseAnimationData()
    {
        var result = new AnimationData();

        var objectEventsPath = Path.Combine(_pokeemeraldPath, "src", "data", "object_events");
        var graphicsInfoPath = Path.Combine(objectEventsPath, "object_event_graphics_info.h");
        var graphicsPath = Path.Combine(objectEventsPath, "object_event_graphics.h");
        var picTablesPath = Path.Combine(objectEventsPath, "object_event_pic_tables.h");
        var animsPath = Path.Combine(objectEventsPath, "object_event_anims.h");

        if (!Directory.Exists(objectEventsPath))
        {
            if (_verbose)
                Console.WriteLine($"[AnimationParser] Object events path not found: {objectEventsPath}");
            return result;
        }

        // Parse pic name -> file path mapping from object_event_graphics.h
        result.PicToFilePath = ParsePicToFilePath(graphicsPath);
        result.MultiFilePics = new Dictionary<string, List<string>>(MultiFilePics);

        // Parse animation sequences from object_event_anims.h
        result.AnimationSequences = ParseAnimationSequences(animsPath);
        result.AnimationTables = ParseAnimationTables(animsPath);

        // Parse sprite -> animation table mapping from object_event_graphics_info.h
        result.SpriteToAnimTable = ParseGraphicsInfo(graphicsInfoPath);

        // Parse pic table sources (which PNGs belong to which sprite)
        result.PicTableSources = ParsePicTableSources(picTablesPath);

        // Parse frame counts from pic tables
        result.FrameCounts = ParseFrameCounts(picTablesPath);

        // Parse physical frame mappings
        result.FrameMappings = ParseFrameMappings(picTablesPath, result.PicTableSources);

        if (_verbose)
        {
            Console.WriteLine($"[AnimationParser] Parsed {result.PicToFilePath.Count} pic->file mappings");
            Console.WriteLine($"[AnimationParser] Parsed {result.AnimationSequences.Count} animation sequences");
            Console.WriteLine($"[AnimationParser] Parsed {result.AnimationTables.Count} animation tables");
            Console.WriteLine($"[AnimationParser] Parsed {result.PicTableSources.Count} pic table sources");
        }

        return result;
    }

    /// <summary>
    /// Parse gObjectEventPic_* -> file path mapping.
    /// Handles both single and multi-file INCBIN_U32.
    /// </summary>
    private Dictionary<string, string> ParsePicToFilePath(string filePath)
    {
        var result = new Dictionary<string, string>();
        if (!File.Exists(filePath)) return result;

        var content = File.ReadAllText(filePath);

        // Match: gObjectEventPic_<Name>[] = INCBIN_U32("...", "...", ...);
        // Captures the pic name and the entire INCBIN content
        var pattern = new Regex(
            @"gObjectEventPic_(\w+)\[\]\s*=\s*INCBIN_U32\(([^)]+)\)",
            RegexOptions.Compiled);

        // Pattern to extract individual file paths from INCBIN content
        // Matches all subdirectories: people, berry_trees, cushions, dolls, misc, pokemon_old
        var filePattern = new Regex(
            @"""graphics/object_events/pics/([^""]+)\.4bpp""",
            RegexOptions.Compiled);

        foreach (Match match in pattern.Matches(content))
        {
            var picName = match.Groups[1].Value;
            var incbinContent = match.Groups[2].Value;

            // Extract all file paths from this INCBIN
            var fileMatches = filePattern.Matches(incbinContent);
            if (fileMatches.Count > 0)
            {
                // Store first file as primary (for single-file compat)
                result[picName] = fileMatches[0].Groups[1].Value;

                // Store multi-file mapping separately
                if (fileMatches.Count > 1)
                {
                    var allFiles = fileMatches.Select(m => m.Groups[1].Value).ToList();
                    MultiFilePics[picName] = allFiles;
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Pics that combine multiple source files.
    /// </summary>
    public Dictionary<string, List<string>> MultiFilePics { get; } = new();

    /// <summary>
    /// Parse individual animation sequences (sAnim_*).
    /// </summary>
    private Dictionary<string, List<AnimFrame>> ParseAnimationSequences(string filePath)
    {
        var result = new Dictionary<string, List<AnimFrame>>();
        if (!File.Exists(filePath)) return result;

        var content = File.ReadAllText(filePath);

        // Match: sAnim_<Name>[] = { ... }
        var seqPattern = new Regex(
            @"sAnim_(\w+)\[\]\s*=\s*\{([^}]+)\}",
            RegexOptions.Compiled | RegexOptions.Singleline);

        // Match: ANIMCMD_FRAME(frameIndex, duration, .hFlip = TRUE/FALSE)
        var framePattern = new Regex(
            @"ANIMCMD_FRAME\((\d+),\s*(\d+)(?:,\s*\.hFlip\s*=\s*(TRUE|FALSE))?\)",
            RegexOptions.Compiled);

        foreach (Match seqMatch in seqPattern.Matches(content))
        {
            var animName = $"sAnim_{seqMatch.Groups[1].Value}";
            var animContent = seqMatch.Groups[2].Value;
            var frames = new List<AnimFrame>();

            foreach (Match frameMatch in framePattern.Matches(animContent))
            {
                frames.Add(new AnimFrame
                {
                    FrameIndex = int.Parse(frameMatch.Groups[1].Value),
                    Duration = int.Parse(frameMatch.Groups[2].Value),
                    FlipHorizontal = frameMatch.Groups[3].Value == "TRUE"
                });
            }

            if (frames.Count > 0)
            {
                result[animName] = frames;
            }
        }

        return result;
    }

    /// <summary>
    /// Parse animation tables (sAnimTable_*) that reference sequences.
    /// </summary>
    private Dictionary<string, List<SpriteAnimationDefinition>> ParseAnimationTables(string filePath)
    {
        var result = new Dictionary<string, List<SpriteAnimationDefinition>>();
        if (!File.Exists(filePath)) return result;

        var content = File.ReadAllText(filePath);
        var sequences = ParseAnimationSequences(filePath);

        // Match: sAnimTable_<Name>[] = { ... }
        var tablePattern = new Regex(
            @"sAnimTable_(\w+)\[\]\s*=\s*\{([^}]+)\}",
            RegexOptions.Compiled | RegexOptions.Singleline);

        // Match: [ANIM_*] = sAnim_<Name>,
        var entryPattern = new Regex(
            @"\[ANIM_(?:STD_)?([A-Z_]+)\]\s*=\s*sAnim_(\w+)",
            RegexOptions.Compiled);

        foreach (Match tableMatch in tablePattern.Matches(content))
        {
            var tableName = tableMatch.Groups[1].Value;
            var tableContent = tableMatch.Groups[2].Value;
            var animations = new List<SpriteAnimationDefinition>();

            foreach (Match entryMatch in entryPattern.Matches(tableContent))
            {
                var animType = entryMatch.Groups[1].Value.ToLowerInvariant();
                var seqName = $"sAnim_{entryMatch.Groups[2].Value}";

                if (sequences.TryGetValue(seqName, out var frames))
                {
                    animations.Add(new SpriteAnimationDefinition
                    {
                        Name = animType,
                        Frames = frames
                    });
                }
            }

            if (animations.Count > 0)
            {
                result[tableName] = animations;
            }
        }

        return result;
    }

    /// <summary>
    /// Parse sprite -> animation table mapping from graphics_info.h
    /// </summary>
    private Dictionary<string, string> ParseGraphicsInfo(string filePath)
    {
        var result = new Dictionary<string, string>();
        if (!File.Exists(filePath)) return result;

        var content = File.ReadAllText(filePath);

        // Match: gObjectEventGraphicsInfo_<Name> = { ... .anims = sAnimTable_<AnimTable>, ... }
        var pattern = new Regex(
            @"gObjectEventGraphicsInfo_(\w+)\s*=\s*\{[^}]*\.anims\s*=\s*sAnimTable_(\w+)",
            RegexOptions.Compiled | RegexOptions.Singleline);

        foreach (Match match in pattern.Matches(content))
        {
            result[match.Groups[1].Value] = match.Groups[2].Value;
        }

        return result;
    }

    /// <summary>
    /// Parse which PNGs belong to which sPicTable.
    /// </summary>
    private Dictionary<string, List<SpriteSourceInfo>> ParsePicTableSources(string filePath)
    {
        var result = new Dictionary<string, List<SpriteSourceInfo>>();
        if (!File.Exists(filePath)) return result;

        var content = File.ReadAllText(filePath);
        var graphicsPath = Path.Combine(Path.GetDirectoryName(filePath)!, "object_event_graphics.h");
        var picToFile = ParsePicToFilePath(graphicsPath);

        // Match: sPicTable_<Name>[] = { ... }
        var tablePattern = new Regex(
            @"sPicTable_(\w+)\[\]\s*=\s*\{([^}]+)\}",
            RegexOptions.Compiled | RegexOptions.Singleline);

        // Match: overworld_frame, overworld_ascending_frames, or obj_frame_tiles references
        var framePattern = new Regex(
            @"(?:overworld_frame|overworld_ascending_frames|obj_frame_tiles)\(gObjectEventPic_(\w+)",
            RegexOptions.Compiled);

        foreach (Match tableMatch in tablePattern.Matches(content))
        {
            var tableName = tableMatch.Groups[1].Value;
            var tableContent = tableMatch.Groups[2].Value;
            var sources = new List<SpriteSourceInfo>();
            var seenPics = new Dictionary<string, SpriteSourceInfo>();

            foreach (Match frameMatch in framePattern.Matches(tableContent))
            {
                var picName = frameMatch.Groups[1].Value;

                if (!seenPics.ContainsKey(picName))
                {
                    var sourceInfo = new SpriteSourceInfo
                    {
                        PicName = picName,
                        FilePath = picToFile.GetValueOrDefault(picName, ""),
                        StartFrame = seenPics.Values.Sum(s => s.FrameCount),
                        FrameCount = 0
                    };
                    seenPics[picName] = sourceInfo;
                    sources.Add(sourceInfo);
                }

                seenPics[picName].FrameCount++;
            }

            if (sources.Count > 0)
            {
                result[tableName] = sources;
            }
        }

        return result;
    }

    /// <summary>
    /// Parse frame counts from pic tables.
    /// </summary>
    private Dictionary<string, int> ParseFrameCounts(string filePath)
    {
        var result = new Dictionary<string, int>();
        if (!File.Exists(filePath)) return result;

        var content = File.ReadAllText(filePath);

        // Match: sPicTable_<Name>[] = { ... }
        var pattern = new Regex(
            @"sPicTable_(\w+)\[\]\s*=\s*\{([^}]+)\}",
            RegexOptions.Compiled | RegexOptions.Singleline);

        foreach (Match match in pattern.Matches(content))
        {
            var spriteName = match.Groups[1].Value;
            var tableContent = match.Groups[2].Value;

            // Count individual frames
            var frameMatches = Regex.Matches(tableContent, @"overworld_frame|obj_frame_tiles");
            var frameCount = frameMatches.Count;

            // Handle overworld_ascending_frames - these generate 9 or 18 frames
            // based on whether it's a normal (9) or combined walk+run sprite (18)
            var ascendingMatches = Regex.Matches(tableContent, @"overworld_ascending_frames\(gObjectEventPic_(\w+)");
            foreach (Match ascMatch in ascendingMatches)
            {
                var picName = ascMatch.Groups[1].Value;
                // Check if this is a multi-file pic (walking + running = 18 frames)
                if (MultiFilePics.ContainsKey(picName))
                {
                    frameCount += 18; // 9 walking + 9 running frames
                }
                else
                {
                    frameCount += 9; // Standard 9 frames
                }
            }

            result[spriteName] = frameCount;
        }

        return result;
    }

    /// <summary>
    /// Parse physical frame mappings from pic tables.
    /// </summary>
    private Dictionary<string, List<int>> ParseFrameMappings(
        string filePath,
        Dictionary<string, List<SpriteSourceInfo>> picTableSources)
    {
        var result = new Dictionary<string, List<int>>();
        if (!File.Exists(filePath)) return result;

        var content = File.ReadAllText(filePath);

        // Match: sPicTable_<Name>[] = { ... }
        var tablePattern = new Regex(
            @"sPicTable_(\w+)\[\]\s*=\s*\{([^}]+)\}",
            RegexOptions.Compiled | RegexOptions.Singleline);

        // Match: overworld_frame(gObjectEventPic_<PicName>, width, height, frame_index)
        var framePattern = new Regex(
            @"overworld_frame\(gObjectEventPic_(\w+),\s*\d+,\s*\d+,\s*(\d+)\)",
            RegexOptions.Compiled);

        foreach (Match tableMatch in tablePattern.Matches(content))
        {
            var spriteName = tableMatch.Groups[1].Value;
            var tableContent = tableMatch.Groups[2].Value;

            // Build pic name -> offset mapping
            var picToOffset = new Dictionary<string, int>();
            if (picTableSources.TryGetValue(spriteName, out var sources))
            {
                foreach (var source in sources)
                {
                    picToOffset[source.PicName] = source.StartFrame;
                }
            }

            var frameMapping = new List<int>();
            foreach (Match frameMatch in framePattern.Matches(tableContent))
            {
                var picName = frameMatch.Groups[1].Value;
                var frameIndexInPng = int.Parse(frameMatch.Groups[2].Value);

                // Adjust frame index based on which PNG this came from
                var offset = picToOffset.GetValueOrDefault(picName, 0);
                frameMapping.Add(offset + frameIndexInPng);
            }

            // Handle obj_frame_tiles (single frame sprites)
            if (frameMapping.Count == 0 && tableContent.Contains("obj_frame_tiles"))
            {
                frameMapping.Add(0);
            }

            if (frameMapping.Count > 0)
            {
                result[spriteName] = frameMapping;
            }
        }

        return result;
    }
}

/// <summary>
/// Parsed animation data from pokeemerald source.
/// </summary>
public class AnimationData
{
    public Dictionary<string, string> PicToFilePath { get; set; } = new();
    public Dictionary<string, List<string>> MultiFilePics { get; set; } = new();
    public Dictionary<string, List<AnimFrame>> AnimationSequences { get; set; } = new();
    public Dictionary<string, List<SpriteAnimationDefinition>> AnimationTables { get; set; } = new();
    public Dictionary<string, string> SpriteToAnimTable { get; set; } = new();
    public Dictionary<string, List<SpriteSourceInfo>> PicTableSources { get; set; } = new();
    public Dictionary<string, int> FrameCounts { get; set; } = new();
    public Dictionary<string, List<int>> FrameMappings { get; set; } = new();
}

/// <summary>
/// Single frame in an animation sequence.
/// </summary>
public class AnimFrame
{
    public int FrameIndex { get; set; }
    public int Duration { get; set; } // In GBA ticks (~60fps)
    public bool FlipHorizontal { get; set; }
}

/// <summary>
/// Animation definition with name and frames.
/// </summary>
public class SpriteAnimationDefinition
{
    public string Name { get; set; } = "";
    public List<AnimFrame> Frames { get; set; } = new();
}

/// <summary>
/// Information about a sprite source file.
/// </summary>
public class SpriteSourceInfo
{
    public string PicName { get; set; } = "";
    public string FilePath { get; set; } = "";
    public int StartFrame { get; set; }
    public int FrameCount { get; set; }
}
