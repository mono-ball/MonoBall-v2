using System.Text.Json;
using Porycon3.Infrastructure;
using Porycon3.Services.Extraction;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using static Porycon3.Infrastructure.StringUtilities;

namespace Porycon3.Services;

/// <summary>
/// Extracts overworld sprites from pokeemerald-expansion.
/// Handles all sprite categories: people, berry_trees, cushions, dolls, misc.
/// pokemon_old is ignored since improved Pokemon overworld sprites come from species extraction.
/// Outputs Sprite definitions matching porycon2 format.
/// </summary>
public class SpriteExtractor : ExtractorBase
{
    public override string Name => "Overworld Sprites";
    public override string Description => "Extracts overworld sprites and animations";

    private AnimationData? _animationData;

    private readonly string _picsBasePath;
    private readonly string _outputGraphics;
    private readonly string _outputData;

    // Sprite categories with their base folder mappings
    // Note: pokemon_old is intentionally excluded - improved sprites come from species extraction
    private static readonly Dictionary<string, string> CategoryMappings = new()
    {
        { "people", "Characters/Npcs" },
        { "berry_trees", "Objects/BerryTrees" },
        { "cushions", "Objects/Cushions" },
        { "dolls", "Objects/Dolls" },
        { "misc", "Objects/Misc" }
    };

    // Pattern-based category overrides (filename -> folder)
    // Note: Pattern matching is case-insensitive
    // Only match actual pokeball sprite names, not cushions
    private static readonly HashSet<string> PokeballNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "ball_poke", "ball_great", "ball_ultra", "ball_master",
        "ball_safari", "ball_net", "ball_dive", "ball_nest",
        "ball_repeat", "ball_timer", "ball_luxury", "ball_premier",
        "ball_dusk", "ball_heal", "ball_quick", "ball_cherish",
        "ball_park", "ball_dream", "ball_fast", "ball_level",
        "ball_lure", "ball_heavy", "ball_love", "ball_friend",
        "ball_moon", "ball_sport", "ball_beast", "ball_strange",
        // PascalCase versions from pic tables
        "pokeball", "ballpoke", "ballgreat", "ballultra", "ballmaster",
        "ballsafari", "ballnet", "balldive", "ballnest",
        "ballrepeat", "balltimer", "ballluxury", "ballpremier",
        "balldusk", "ballheal", "ballquick", "ballcherish",
        "ballpark", "balldream", "ballfast", "balllevel",
        "balllure", "ballheavy", "balllove", "ballfriend",
        "ballmoon", "ballsport", "ballbeast", "ballstrange"
    };

    public SpriteExtractor(string inputPath, string outputPath, bool verbose = false)
        : base(inputPath, outputPath, verbose)
    {
        _picsBasePath = Path.Combine(inputPath, "graphics", "object_events", "pics");
        _outputGraphics = Path.Combine(outputPath, "Graphics");
        _outputData = Path.Combine(outputPath, "Definitions", "Assets");
    }

    private AnimationData GetAnimationData()
    {
        if (_animationData != null)
            return _animationData;

        // Parse animation data lazily (deferred from constructor)
        var parser = new AnimationParser(InputPath, Verbose);
        _animationData = parser.ParseAnimationData();

        LogVerbose($"Parsed {_animationData.PicToFilePath.Count} pic->file mappings");
        LogVerbose($"Parsed {_animationData.AnimationSequences.Count} animation sequences");
        LogVerbose($"Parsed {_animationData.AnimationTables.Count} animation tables");
        LogVerbose($"Parsed {_animationData.PicTableSources.Count} pic table sources");

        return _animationData;
    }

    protected override int ExecuteExtraction()
    {
        if (!Directory.Exists(_picsBasePath))
        {
            LogWarning($"Pics path not found: {_picsBasePath}");
            return 0;
        }

        EnsureDirectory(_outputGraphics);
        EnsureDirectory(_outputData);

        // Thread-safe counters and collection
        int spriteCount = 0;
        int graphicsCount = 0;
        var processedFiles = new System.Collections.Concurrent.ConcurrentDictionary<string, byte>();

        // Extract sprites based on sPicTable definitions (parallel)
        var animationData = GetAnimationData();
        var picTableList = animationData.PicTableSources
            .Where(kvp => kvp.Value.Any(s => !string.IsNullOrWhiteSpace(s.FilePath)))
            .ToList();

        if (picTableList.Count > 0)
        {
            WithParallelProgress("Extracting pic table sprites", picTableList, kvp =>
            {
                var (picTableName, sources) = kvp;
                var validSources = sources.Where(s => !string.IsNullOrWhiteSpace(s.FilePath)).ToList();
                if (validSources.Count == 0) return;

                if (ExtractSpriteFromPicTable(picTableName, validSources))
                {
                    Interlocked.Increment(ref spriteCount);
                    Interlocked.Increment(ref graphicsCount);
                    foreach (var source in validSources)
                    {
                        processedFiles.TryAdd(source.FilePath, 0);
                    }
                }
            });
        }

        // Mark files that are part of multi-file pics as processed
        foreach (var (_, files) in animationData.MultiFilePics)
        {
            foreach (var file in files)
            {
                processedFiles.TryAdd(file, 0);
            }
        }

        // Process each sprite category directory for standalone PNGs (parallel)
        foreach (var category in CategoryMappings.Keys)
        {
            var categoryPath = Path.Combine(_picsBasePath, category);
            if (!Directory.Exists(categoryPath)) continue;

            // Extract standalone PNGs not in sPicTables or multi-file pics
            var allPngs = Directory.GetFiles(categoryPath, "*.png", SearchOption.AllDirectories)
                .Where(pngPath =>
                {
                    var relativePath = Path.GetRelativePath(_picsBasePath, pngPath);
                    var pathWithoutExt = Path.ChangeExtension(relativePath, null).Replace('\\', '/');
                    return !processedFiles.ContainsKey(pathWithoutExt);
                })
                .ToList();

            if (allPngs.Count > 0)
            {
                WithParallelProgress($"Extracting {category} sprites", allPngs, pngPath =>
                {
                    if (ExtractStandalonePng(pngPath, category))
                    {
                        Interlocked.Increment(ref spriteCount);
                        Interlocked.Increment(ref graphicsCount);
                    }
                });
            }
        }

        SetCount("Graphics", graphicsCount);
        return spriteCount;
    }

    private bool ExtractSpriteFromPicTable(string picTableName, List<SpriteSourceInfo> sources)
    {
        if (sources.Count == 0) return false;

        var firstFilePath = sources[0].FilePath;
        // FilePath now includes category prefix like "people/may/walking" or "misc/ball_poke"
        var pathParts = firstFilePath.Replace('\\', '/').Split('/');
        var sourceCategory = pathParts.Length > 0 ? pathParts[0] : "people";
        var subPath = pathParts.Length > 1 ? string.Join("/", pathParts.Skip(1).Take(pathParts.Length - 2)) : "";

        var isPlayerSprite = sourceCategory == "people" &&
                             (subPath.StartsWith("may", StringComparison.OrdinalIgnoreCase) ||
                              subPath.StartsWith("brendan", StringComparison.OrdinalIgnoreCase));

        var spriteName = ConvertPicTableNameToSpriteName(picTableName, subPath);

        LogVerbose($"Processing: {picTableName} -> {spriteName} (category: {sourceCategory})");

        // Load all source PNGs, handling multi-file pics
        var sourceImages = new List<Image<Rgba32>>();
        try
        {
            var totalWidth = 0;
            var maxHeight = 0;
            var totalPhysicalFrames = 0;
            var allSourceFiles = new List<string>();

            foreach (var source in sources)
        {
            if (string.IsNullOrWhiteSpace(source.PicName)) continue;

            // Check if this pic is a multi-file pic (e.g., walking + running combined)
            if (GetAnimationData().MultiFilePics.TryGetValue(source.PicName, out var multiFiles))
            {
                allSourceFiles.AddRange(multiFiles);
            }
            else if (!string.IsNullOrWhiteSpace(source.FilePath))
            {
                allSourceFiles.Add(source.FilePath);
            }
        }

        foreach (var filePath in allSourceFiles)
        {
            var pngPath = Path.Combine(_picsBasePath, $"{filePath}.png");
            if (!File.Exists(pngPath)) continue;

            // Load with proper index 0 transparency
            var img = LoadWithIndex0Transparency(pngPath);
            sourceImages.Add(img);
            totalWidth += img.Width;
            maxHeight = Math.Max(maxHeight, img.Height);

            var srcFrameInfo = AnalyzeSpriteSheet(img);
            totalPhysicalFrames += srcFrameInfo.FrameCount;
        }

        if (sourceImages.Count == 0) return false;

        // Combine images horizontally (transparency already applied)
        var physicalFramePositions = new List<(int Index, int X)>();
        using var combined = new Image<Rgba32>(totalWidth, maxHeight);
        var currentX = 0;

        for (var i = 0; i < sourceImages.Count; i++)
        {
            var img = sourceImages[i];
            var srcFrameInfo = AnalyzeSpriteSheet(img);

            // Paste into combined image
            combined.Mutate(ctx => ctx.DrawImage(img, new Point(currentX, 0), 1f));

            // Track frame positions
            for (var f = 0; f < srcFrameInfo.FrameCount; f++)
            {
                var frameX = currentX + (f * srcFrameInfo.FrameWidth);
                physicalFramePositions.Add((physicalFramePositions.Count, frameX));
            }

            currentX += img.Width;
        }

        // Determine frame layout from first source
        var frameInfo = AnalyzeSpriteSheet(sourceImages[0]);

        // Create output directories (PascalCase for Porycon3)
        string baseFolder;
        string spriteCategory;

        // Check for pattern-based overrides (e.g., ball_* -> Pokeballs)
        var patternOverride = GetPatternOverrideFolder(spriteName);

        // Transform pokeball names (ball_fast -> FastBall)
        if (patternOverride != null)
        {
            spriteName = TransformPokeballName(spriteName);
        }

        if (isPlayerSprite)
        {
            baseFolder = "Characters/Players";
            spriteCategory = PathToPascalCase(subPath);
        }
        else if (patternOverride != null)
        {
            // Pattern override takes precedence (e.g., misc/ball_poke -> Objects/Pokeballs)
            baseFolder = patternOverride;
            spriteCategory = "";
        }
        else if (CategoryMappings.TryGetValue(sourceCategory, out var mappedFolder))
        {
            baseFolder = mappedFolder;
            spriteCategory = subPath.Length > 0 ? PathToPascalCase(subPath) : "";
        }
        else
        {
            baseFolder = "Characters/Npcs";
            spriteCategory = subPath.Length > 0 ? PathToPascalCase(subPath) : "Generic";
        }

        var graphicsDir = string.IsNullOrEmpty(spriteCategory)
            ? Path.Combine(_outputGraphics, baseFolder)
            : Path.Combine(_outputGraphics, baseFolder, spriteCategory);
        var dataDir = string.IsNullOrEmpty(spriteCategory)
            ? Path.Combine(_outputData, baseFolder)
            : Path.Combine(_outputData, baseFolder, spriteCategory);
        EnsureDirectory(graphicsDir);
        EnsureDirectory(dataDir);

        // Save combined spritesheet as 32-bit RGBA
        var graphicsPath = Path.Combine(graphicsDir, $"{spriteName}.png");
        IndexedPngLoader.SaveAsRgbaPng(combined, graphicsPath);

        // Get physical frame mapping
        var physicalFrameMapping = GetAnimationData().FrameMappings.GetValueOrDefault(picTableName);

        // Build frame definitions
        var physicalToX = physicalFramePositions.ToDictionary(p => p.Index, p => p.X);
        var frames = new List<FrameDefinition>();

        if (physicalFrameMapping != null)
        {
            for (var logical = 0; logical < physicalFrameMapping.Count; logical++)
            {
                var physical = physicalFrameMapping[logical];
                var frameX = physicalToX.GetValueOrDefault(physical, physical * frameInfo.FrameWidth);
                frames.Add(new FrameDefinition
                {
                    Index = logical,
                    X = frameX,
                    Y = 0,
                    Width = frameInfo.FrameWidth,
                    Height = frameInfo.FrameHeight
                });
            }
        }
        else
        {
            foreach (var (index, x) in physicalFramePositions)
            {
                frames.Add(new FrameDefinition
                {
                    Index = index,
                    X = x,
                    Y = 0,
                    Width = frameInfo.FrameWidth,
                    Height = frameInfo.FrameHeight
                });
            }
        }

        // Generate animations
        // Use actual frame count from combined image, not just first source
        var combinedFrameInfo = new SpriteSheetInfo
        {
            FrameWidth = frameInfo.FrameWidth,
            FrameHeight = frameInfo.FrameHeight,
            FrameCount = frames.Count
        };
        var animations = GenerateAnimations(picTableName, combinedFrameInfo, sourceCategory);

        // Create manifest (IDs are lowercase, file paths preserve case)
        var relativePath = string.IsNullOrEmpty(spriteCategory) ? spriteName : $"{spriteCategory}/{spriteName}";
        var texturePath = $"Graphics/{baseFolder}/{relativePath}.png";
        
        // Generate sprite ID using IdTransformer.SpriteId() to match map definitions
        // Maps use IdTransformer.SpriteId(graphicsId), so we must use the same method
        // Reconstruct graphics ID from pic table name and category information
        string graphicsIdInput;
        if (isPlayerSprite && !string.IsNullOrEmpty(subPath))
        {
            // Player sprites: reconstruct as "may_normal" format
            var playerName = subPath.Split('/')[0].ToLowerInvariant();
            var picTableLower = picTableName.ToLowerInvariant();
            if (!picTableLower.StartsWith(playerName + "_"))
            {
                var variant = picTableLower.StartsWith(playerName) 
                    ? picTableLower.Substring(playerName.Length) 
                    : picTableLower;
                graphicsIdInput = $"{playerName}_{variant}";
            }
            else
            {
                graphicsIdInput = picTableName;
            }
        }
        else
        {
            graphicsIdInput = picTableName;
        }
        var graphicsId = ReconstructGraphicsId(graphicsIdInput, sourceCategory, spriteName);
        var spriteId = IdTransformer.SpriteId(graphicsId);
        var manifest = new SpriteManifest
        {
            Id = spriteId,
            Name = FormatDisplayName(spriteName),
            Type = "Sprite",
            TexturePath = texturePath,
            FrameWidth = frameInfo.FrameWidth,
            FrameHeight = frameInfo.FrameHeight,
            FrameCount = frames.Count,
            Frames = frames,
            Animations = animations
        };

            var manifestPath = Path.Combine(dataDir, $"{spriteName}.json");
            File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, JsonOptions.Default));

            return true;
        }
        finally
        {
            // Ensure all loaded images are disposed even on exception
            foreach (var img in sourceImages)
                img.Dispose();
        }
    }

    private bool ExtractStandalonePng(string pngPath, string sourceCategory)
    {
        // Get path relative to the category directory
        var categoryPath = Path.Combine(_picsBasePath, sourceCategory);
        var relativePath = Path.GetRelativePath(categoryPath, pngPath);
        var originalFileName = Path.GetFileNameWithoutExtension(pngPath);
        var spriteName = ToPascalCase(originalFileName);
        var subDirectory = Path.GetDirectoryName(relativePath)?.Replace('\\', '/') ?? "";

        var isPlayerSprite = sourceCategory == "people" &&
                             (subDirectory.StartsWith("may", StringComparison.OrdinalIgnoreCase) ||
                              subDirectory.StartsWith("brendan", StringComparison.OrdinalIgnoreCase));

        // Load with proper index 0 transparency
        using var image = LoadWithIndex0Transparency(pngPath);
        var frameInfo = AnalyzeSpriteSheet(image);

        // Create output directories (PascalCase for Porycon3)
        string baseFolder;
        string spriteCategory;

        // Check for pattern-based overrides (e.g., ball_* -> Pokeballs)
        var patternOverride = GetPatternOverrideFolder(Path.GetFileNameWithoutExtension(pngPath));

        // Transform pokeball names (ball_fast -> FastBall)
        if (patternOverride != null)
        {
            spriteName = TransformPokeballName(spriteName);
        }

        if (isPlayerSprite)
        {
            baseFolder = "Characters/Players";
            spriteCategory = PathToPascalCase(subDirectory);
        }
        else if (patternOverride != null)
        {
            // Pattern override takes precedence (e.g., misc/ball_poke -> Objects/Pokeballs)
            baseFolder = patternOverride;
            spriteCategory = "";
        }
        else if (CategoryMappings.TryGetValue(sourceCategory, out var mappedFolder))
        {
            baseFolder = mappedFolder;
            spriteCategory = subDirectory.Length > 0 ? PathToPascalCase(subDirectory) : "";
        }
        else
        {
            baseFolder = "Characters/Npcs";
            spriteCategory = subDirectory.Length > 0 ? PathToPascalCase(subDirectory) : "Generic";
        }

        var graphicsDir = string.IsNullOrEmpty(spriteCategory)
            ? Path.Combine(_outputGraphics, baseFolder)
            : Path.Combine(_outputGraphics, baseFolder, spriteCategory);
        var dataDir = string.IsNullOrEmpty(spriteCategory)
            ? Path.Combine(_outputData, baseFolder)
            : Path.Combine(_outputData, baseFolder, spriteCategory);
        EnsureDirectory(graphicsDir);
        EnsureDirectory(dataDir);

        // Save sprite sheet as 32-bit RGBA
        var graphicsPath = Path.Combine(graphicsDir, $"{spriteName}.png");
        IndexedPngLoader.SaveAsRgbaPng(image, graphicsPath);

        // Build frames
        var frames = new List<FrameDefinition>();
        for (var i = 0; i < frameInfo.FrameCount; i++)
        {
            frames.Add(new FrameDefinition
            {
                Index = i,
                X = i * frameInfo.FrameWidth,
                Y = 0,
                Width = frameInfo.FrameWidth,
                Height = frameInfo.FrameHeight
            });
        }

        // Generate animations (may not have any for standalone)
        var animations = GenerateAnimations(spriteName, frameInfo, sourceCategory);

        // Create manifest (IDs are lowercase, file paths preserve case)
        var outputRelativePath = string.IsNullOrEmpty(spriteCategory) ? spriteName : $"{spriteCategory}/{spriteName}";
        var texturePath = $"Graphics/{baseFolder}/{outputRelativePath}.png";
        
        // Generate sprite ID using IdTransformer.SpriteId() to match map definitions
        // Maps use IdTransformer.SpriteId(graphicsId), so we must use the same method
        // Reconstruct graphics ID from original file name (before PascalCase conversion)
        var graphicsId = isPlayerSprite && !string.IsNullOrEmpty(subDirectory)
            ? ReconstructGraphicsId($"{subDirectory.ToLowerInvariant()}_{originalFileName.ToLowerInvariant()}", sourceCategory, spriteName)
            : ReconstructGraphicsId(originalFileName, sourceCategory, spriteName);
        var spriteId = IdTransformer.SpriteId(graphicsId);
        var manifest = new SpriteManifest
        {
            Id = spriteId,
            Name = FormatDisplayName(spriteName),
            Type = "Sprite",
            TexturePath = texturePath,
            FrameWidth = frameInfo.FrameWidth,
            FrameHeight = frameInfo.FrameHeight,
            FrameCount = frames.Count,
            Frames = frames,
            Animations = animations
        };

        var manifestPath = Path.Combine(dataDir, $"{spriteName}.json");
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, JsonOptions.Default));

        return true;
    }

    private SpriteSheetInfo AnalyzeSpriteSheet(Image image)
    {
        var width = image.Width;
        var height = image.Height;

        // Detect frame size based on sprite sheet dimensions
        int frameWidth, frameHeight, frameCount;

        if (height == 64 && width % 64 == 0)
        {
            // 64x64 sprites (large)
            frameWidth = 64;
            frameHeight = 64;
            frameCount = width / 64;
        }
        else if (height == 64 && width % 32 == 0)
        {
            // 32x64 sprites (tall)
            frameWidth = 32;
            frameHeight = 64;
            frameCount = width / 32;
        }
        else if (height == 32 && width % 32 == 0 && width != height)
        {
            // Could be 32x32 or 16x32
            // Check if 16x32 makes more sense (more common for NPCs)
            if (width % 16 == 0 && width / 16 > width / 32)
            {
                frameWidth = 16;
                frameHeight = 32;
                frameCount = width / 16;
            }
            else
            {
                frameWidth = 32;
                frameHeight = 32;
                frameCount = width / 32;
            }
        }
        else if (height == 32 && width % 16 == 0)
        {
            // 16x32 sprites (most NPCs)
            frameWidth = 16;
            frameHeight = 32;
            frameCount = width / 16;
        }
        else if (height == 16 && width % 16 == 0)
        {
            // 16x16 sprites (small NPCs)
            frameWidth = 16;
            frameHeight = 16;
            frameCount = width / 16;
        }
        else
        {
            // Default: assume square frames based on height
            frameWidth = height;
            frameHeight = height;
            frameCount = frameWidth > 0 ? width / frameWidth : 1;
        }

        return new SpriteSheetInfo
        {
            FrameWidth = frameWidth,
            FrameHeight = frameHeight,
            FrameCount = Math.Max(1, frameCount)
        };
    }

    private List<SpriteAnimation> GenerateAnimations(string spriteName, SpriteSheetInfo info, string sourceCategory)
    {
        var animations = new List<SpriteAnimation>();

        // Special handling for berry trees (stage-based animations)
        if (sourceCategory == "berry_trees")
        {
            animations.AddRange(GenerateBerryTreeAnimations(info.FrameCount));
            return animations;
        }

        // Try to find animation table for this sprite
        var possibleNames = new[]
        {
            spriteName,
            StripCommonSuffixes(spriteName),
            $"May{ToPascalCase(spriteName)}",
            $"Brendan{ToPascalCase(spriteName)}"
        };

        string? animTableName = null;
        var animData = GetAnimationData();
        foreach (var name in possibleNames)
        {
            if (animData.SpriteToAnimTable.TryGetValue(name, out var tableName))
            {
                animTableName = tableName;
                break;
            }
        }

        if (animTableName != null && animData.AnimationTables.TryGetValue(animTableName, out var animDefs))
        {
            var frameCount = animData.FrameCounts.GetValueOrDefault(spriteName, info.FrameCount);

            foreach (var animDef in animDefs)
            {
                var frameIndices = animDef.Frames.Select(f => f.FrameIndex).ToList();

                // Skip animations with invalid frame indices
                if (frameIndices.Any(idx => idx >= frameCount))
                    continue;

                var usesFlip = animDef.Frames.Any(f => f.FlipHorizontal);
                var frameDurations = animDef.Frames.Select(f => f.Duration / 60.0).ToList();

                animations.Add(new SpriteAnimation
                {
                    Name = animDef.Name,
                    Loop = true,
                    FrameIndices = frameIndices,
                    FrameDurations = frameDurations,
                    FlipHorizontal = usesFlip
                });
            }
        }
        else if (info.FrameCount >= 9)
        {
            // Generate default animations for standalone sprites
            // Standard layout: 3 columns (south, north, west) x 3 rows (idle, walk1, walk2)
            var lowerName = spriteName.ToLowerInvariant();
            var isRunning = lowerName.Contains("running") || lowerName.Contains("run");

            animations.AddRange(GenerateDefaultAnimations(info.FrameCount, isRunning));
        }

        return animations;
    }

    /// <summary>
    /// Generates berry tree stage-based animations.
    /// Based on pokeemerald's sAnimTable_BerryTree with 5 growth stage animations.
    /// </summary>
    private List<SpriteAnimation> GenerateBerryTreeAnimations(int frameCount)
    {
        var animations = new List<SpriteAnimation>();

        // Berry tree animations from pokeemerald's object_event_anims.h:
        // - Stage0 (PLANTED): frame 0, 32 ticks
        // - Stage1 (SPROUTED): frames 1, 2, 32 ticks each
        // - Stage2 (TALLER/TRUNK/BUDDING): frames 3, 4, 48 ticks each
        // - Stage3 (FLOWERING): frames 5, 5, 6, 6, 32 ticks each (frame 6 may not exist)
        // - Stage4 (BERRIES): frames 7, 7, 8, 8, 48 ticks each (frames 7-8 may not exist)

        // Animation durations (converted from ticks to seconds)
        var stage0Duration = 32 / 60.0; // ~0.533s
        var stage1Duration = 32 / 60.0; // ~0.533s
        var stage2Duration = 48 / 60.0; // ~0.8s
        var stage3Duration = 32 / 60.0; // ~0.533s
        var stage4Duration = 48 / 60.0; // ~0.8s

        // Stage0 (PLANTED): frame 0
        if (frameCount > 0)
        {
            animations.Add(new SpriteAnimation
            {
                Name = "stage0",
                Loop = true,
                FrameIndices = new List<int> { 0 },
                FrameDurations = new List<double> { stage0Duration },
                FlipHorizontal = false
            });
        }

        // Stage1 (SPROUTED): frames 1, 2
        if (frameCount > 2)
        {
            animations.Add(new SpriteAnimation
            {
                Name = "stage1",
                Loop = true,
                FrameIndices = new List<int> { 1, 2 },
                FrameDurations = new List<double> { stage1Duration, stage1Duration },
                FlipHorizontal = false
            });
        }

        // Stage2 (TALLER/TRUNK/BUDDING): frames 3, 4
        if (frameCount > 4)
        {
            animations.Add(new SpriteAnimation
            {
                Name = "stage2",
                Loop = true,
                FrameIndices = new List<int> { 3, 4 },
                FrameDurations = new List<double> { stage2Duration, stage2Duration },
                FlipHorizontal = false
            });
        }

        // Stage3 (FLOWERING): frames 5, 5, 6, 6 (or just frame 5 if frame 6 doesn't exist)
        if (frameCount > 5)
        {
            if (frameCount > 6)
            {
                // Full animation with frames 5 and 6
                animations.Add(new SpriteAnimation
                {
                    Name = "stage3",
                    Loop = true,
                    FrameIndices = new List<int> { 5, 5, 6, 6 },
                    FrameDurations = new List<double> { stage3Duration, stage3Duration, stage3Duration, stage3Duration },
                    FlipHorizontal = false
                });
            }
            else
            {
                // Simplified animation with just frame 5
                animations.Add(new SpriteAnimation
                {
                    Name = "stage3",
                    Loop = true,
                    FrameIndices = new List<int> { 5 },
                    FrameDurations = new List<double> { stage3Duration },
                    FlipHorizontal = false
                });
            }
        }

        // Stage4 (BERRIES): frames 7, 7, 8, 8 (or frame 5 as fallback if frames 7-8 don't exist)
        if (frameCount > 7)
        {
            // Full animation with frames 7 and 8
            animations.Add(new SpriteAnimation
            {
                Name = "stage4",
                Loop = true,
                FrameIndices = new List<int> { 7, 7, 8, 8 },
                FrameDurations = new List<double> { stage4Duration, stage4Duration, stage4Duration, stage4Duration },
                FlipHorizontal = false
            });
        }
        else if (frameCount > 5)
        {
            // Fallback: use frame 5 if frames 7-8 don't exist
            animations.Add(new SpriteAnimation
            {
                Name = "stage4",
                Loop = true,
                FrameIndices = new List<int> { 5 },
                FrameDurations = new List<double> { stage4Duration },
                FlipHorizontal = false
            });
        }

        return animations;
    }

    private List<SpriteAnimation> GenerateDefaultAnimations(int frameCount, bool isRunning)
    {
        var animations = new List<SpriteAnimation>();

        // Standard 9-frame layout: 3 directions (S, N, W) x 3 frames (idle, walk1, walk2)
        // Frame indices: 0=S_idle, 1=N_idle, 2=W_idle, 3=S_walk1, 4=N_walk1, 5=W_walk1, 6=S_walk2, 7=N_walk2, 8=W_walk2

        // Timing based on whether this is walking or running
        var faceDuration = 16 / 60.0; // ~0.267s
        var moveDuration = isRunning ? 4 / 60.0 : 8 / 60.0; // faster for running

        // Face animations (single frame)
        animations.Add(new SpriteAnimation { Name = "face_south", Loop = true, FrameIndices = new List<int> { 0 }, FrameDurations = new List<double> { faceDuration }, FlipHorizontal = false });
        animations.Add(new SpriteAnimation { Name = "face_north", Loop = true, FrameIndices = new List<int> { 1 }, FrameDurations = new List<double> { faceDuration }, FlipHorizontal = false });
        animations.Add(new SpriteAnimation { Name = "face_west", Loop = true, FrameIndices = new List<int> { 2 }, FrameDurations = new List<double> { faceDuration }, FlipHorizontal = false });
        animations.Add(new SpriteAnimation { Name = "face_east", Loop = true, FrameIndices = new List<int> { 2 }, FrameDurations = new List<double> { faceDuration }, FlipHorizontal = true });

        // Walk/run cycle: idle -> walk1 -> idle -> walk2 (4 frames per cycle)
        var prefix = isRunning ? "go_fast" : "go";
        animations.Add(new SpriteAnimation { Name = $"{prefix}_south", Loop = true, FrameIndices = new List<int> { 3, 0, 6, 0 }, FrameDurations = new List<double> { moveDuration, moveDuration, moveDuration, moveDuration }, FlipHorizontal = false });
        animations.Add(new SpriteAnimation { Name = $"{prefix}_north", Loop = true, FrameIndices = new List<int> { 4, 1, 7, 1 }, FrameDurations = new List<double> { moveDuration, moveDuration, moveDuration, moveDuration }, FlipHorizontal = false });
        animations.Add(new SpriteAnimation { Name = $"{prefix}_west", Loop = true, FrameIndices = new List<int> { 5, 2, 8, 2 }, FrameDurations = new List<double> { moveDuration, moveDuration, moveDuration, moveDuration }, FlipHorizontal = false });
        animations.Add(new SpriteAnimation { Name = $"{prefix}_east", Loop = true, FrameIndices = new List<int> { 5, 2, 8, 2 }, FrameDurations = new List<double> { moveDuration, moveDuration, moveDuration, moveDuration }, FlipHorizontal = true });

        return animations;
    }

    /// <summary>
    /// Load an indexed PNG and convert to RGBA with palette index 0 as transparent.
    /// This is how GBA/pokeemerald handles sprite transparency.
    /// </summary>
    private static Image<Rgba32> LoadWithIndex0Transparency(string pngPath)
    {
        // Read raw PNG bytes to extract palette and pixel indices
        var bytes = File.ReadAllBytes(pngPath);

        // Load as generic image first to check format
        using var tempImage = Image.Load(pngPath);

        if (tempImage is Image<Rgba32> rgbaImage)
        {
            // Already RGBA - check if it has transparency via alpha channel
            // If not, fall back to first pixel method
            var hasTransparency = false;
            rgbaImage.ProcessPixelRows(accessor =>
            {
                for (var y = 0; y < accessor.Height && !hasTransparency; y++)
                {
                    var row = accessor.GetRowSpan(y);
                    for (var x = 0; x < row.Length; x++)
                    {
                        if (row[x].A < 255)
                        {
                            hasTransparency = true;
                            break;
                        }
                    }
                }
            });

            if (hasTransparency)
            {
                return rgbaImage.Clone();
            }

            // No transparency - use first pixel as background
            var bgColor = rgbaImage[0, 0];
            var result = rgbaImage.Clone();
            result.ProcessPixelRows(accessor =>
            {
                for (var y = 0; y < accessor.Height; y++)
                {
                    var row = accessor.GetRowSpan(y);
                    for (var x = 0; x < row.Length; x++)
                    {
                        if (row[x].R == bgColor.R && row[x].G == bgColor.G && row[x].B == bgColor.B)
                        {
                            row[x] = new Rgba32(0, 0, 0, 0);
                        }
                    }
                }
            });
            return result;
        }

        // For indexed images, we need to extract palette and apply index 0 transparency
        // Parse PNG to get palette
        var palette = ExtractPngPalette(bytes);
        if (palette == null || palette.Length == 0)
        {
            // Fallback: load as RGBA and use first pixel
            var img = Image.Load<Rgba32>(pngPath);
            var bgColor = img[0, 0];
            img.ProcessPixelRows(accessor =>
            {
                for (var y = 0; y < accessor.Height; y++)
                {
                    var row = accessor.GetRowSpan(y);
                    for (var x = 0; x < row.Length; x++)
                    {
                        if (row[x].R == bgColor.R && row[x].G == bgColor.G && row[x].B == bgColor.B)
                        {
                            row[x] = new Rgba32(0, 0, 0, 0);
                        }
                    }
                }
            });
            return img;
        }

        // Extract raw palette indices from PNG IDAT chunk
        var (width, height, bitDepth, indices) = ExtractPngIndices(bytes);

        if (indices == null || indices.Length == 0)
        {
            // Fallback: load as RGBA and use first pixel as transparent
            var img = Image.Load<Rgba32>(pngPath);
            var bgColor = img[0, 0];
            img.ProcessPixelRows(accessor =>
            {
                for (var y = 0; y < accessor.Height; y++)
                {
                    var row = accessor.GetRowSpan(y);
                    for (var x = 0; x < row.Length; x++)
                    {
                        if (row[x].R == bgColor.R && row[x].G == bgColor.G && row[x].B == bgColor.B)
                        {
                            row[x] = new Rgba32(0, 0, 0, 0);
                        }
                    }
                }
            });
            return img;
        }

        var output = new Image<Rgba32>(width, height);

        output.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < width; x++)
                {
                    var paletteIndex = indices[y * width + x];

                    if (paletteIndex == 0)
                    {
                        // Index 0 is transparent in GBA
                        row[x] = new Rgba32(0, 0, 0, 0);
                    }
                    else if (paletteIndex < palette.Length)
                    {
                        row[x] = palette[paletteIndex];
                    }
                    else
                    {
                        // Fallback - shouldn't happen with valid PNG
                        row[x] = new Rgba32(255, 0, 255, 255); // Magenta for debug
                    }
                }
            }
        });

        return output;
    }

    /// <summary>
    /// Extract RGB palette from PNG PLTE chunk.
    /// </summary>
    private static Rgba32[]? ExtractPngPalette(byte[] pngData)
    {
        // Find PLTE chunk
        // PNG structure: 8-byte signature, then chunks (4-byte length, 4-byte type, data, 4-byte CRC)
        var pos = 8; // Skip PNG signature

        while (pos < pngData.Length - 12)
        {
            var length = (pngData[pos] << 24) | (pngData[pos + 1] << 16) |
                         (pngData[pos + 2] << 8) | pngData[pos + 3];
            var type = System.Text.Encoding.ASCII.GetString(pngData, pos + 4, 4);

            if (type == "PLTE")
            {
                var colorCount = length / 3;
                var palette = new Rgba32[colorCount];

                for (var i = 0; i < colorCount; i++)
                {
                    var offset = pos + 8 + i * 3;
                    palette[i] = new Rgba32(pngData[offset], pngData[offset + 1], pngData[offset + 2], 255);
                }

                return palette;
            }

            pos += 12 + length; // 4 length + 4 type + data + 4 CRC
        }

        return null;
    }

    /// <summary>
    /// Extract raw palette indices from PNG IDAT chunks.
    /// Handles PNG decompression and filtering to get actual palette indices.
    /// </summary>
    private static (int Width, int Height, int BitDepth, byte[]? Indices) ExtractPngIndices(byte[] pngData)
    {
        int width = 0, height = 0, bitDepth = 0, colorType = 0;
        var idatChunks = new List<byte[]>();
        var pos = 8; // Skip PNG signature

        // Parse chunks to get IHDR and IDAT data
        while (pos < pngData.Length - 12)
        {
            var length = (pngData[pos] << 24) | (pngData[pos + 1] << 16) |
                         (pngData[pos + 2] << 8) | pngData[pos + 3];
            var type = System.Text.Encoding.ASCII.GetString(pngData, pos + 4, 4);

            if (type == "IHDR")
            {
                var dataStart = pos + 8;
                width = (pngData[dataStart] << 24) | (pngData[dataStart + 1] << 16) |
                        (pngData[dataStart + 2] << 8) | pngData[dataStart + 3];
                height = (pngData[dataStart + 4] << 24) | (pngData[dataStart + 5] << 16) |
                         (pngData[dataStart + 6] << 8) | pngData[dataStart + 7];
                bitDepth = pngData[dataStart + 8];
                colorType = pngData[dataStart + 9];
            }
            else if (type == "IDAT")
            {
                var chunk = new byte[length];
                Array.Copy(pngData, pos + 8, chunk, 0, length);
                idatChunks.Add(chunk);
            }
            else if (type == "IEND")
            {
                break;
            }

            pos += 12 + length;
        }

        // Only handle indexed color (colorType 3)
        if (colorType != 3 || width == 0 || height == 0)
        {
            return (width, height, bitDepth, null);
        }

        // Combine all IDAT chunks
        var totalLength = idatChunks.Sum(c => c.Length);
        var compressedData = new byte[totalLength];
        var offset = 0;
        foreach (var chunk in idatChunks)
        {
            Array.Copy(chunk, 0, compressedData, offset, chunk.Length);
            offset += chunk.Length;
        }

        // Decompress zlib data (skip first 2 bytes - zlib header)
        byte[] decompressedData;
        try
        {
            using var compressedStream = new MemoryStream(compressedData, 2, compressedData.Length - 2);
            using var deflateStream = new System.IO.Compression.DeflateStream(compressedStream, System.IO.Compression.CompressionMode.Decompress);
            using var decompressedStream = new MemoryStream();
            deflateStream.CopyTo(decompressedStream);
            decompressedData = decompressedStream.ToArray();
        }
        catch
        {
            return (width, height, bitDepth, null);
        }

        // Calculate bytes per row (including filter byte)
        var pixelsPerByte = 8 / bitDepth;
        var bytesPerRow = (width + pixelsPerByte - 1) / pixelsPerByte;
        var rowSize = bytesPerRow + 1; // +1 for filter byte

        // Un-filter and extract palette indices
        var indices = new byte[width * height];
        var prevRow = new byte[bytesPerRow];

        for (var y = 0; y < height; y++)
        {
            var rowStart = y * rowSize;
            if (rowStart >= decompressedData.Length) break;

            var filterType = decompressedData[rowStart];
            var currentRow = new byte[bytesPerRow];

            // Copy row data
            var dataStart = rowStart + 1;
            var copyLen = Math.Min(bytesPerRow, decompressedData.Length - dataStart);
            if (copyLen > 0)
            {
                Array.Copy(decompressedData, dataStart, currentRow, 0, copyLen);
            }

            // Apply PNG filter
            switch (filterType)
            {
                case 0: // None
                    break;
                case 1: // Sub
                    for (var i = 1; i < bytesPerRow; i++)
                        currentRow[i] = (byte)(currentRow[i] + currentRow[i - 1]);
                    break;
                case 2: // Up
                    for (var i = 0; i < bytesPerRow; i++)
                        currentRow[i] = (byte)(currentRow[i] + prevRow[i]);
                    break;
                case 3: // Average
                    for (var i = 0; i < bytesPerRow; i++)
                    {
                        var left = i > 0 ? currentRow[i - 1] : 0;
                        currentRow[i] = (byte)(currentRow[i] + (left + prevRow[i]) / 2);
                    }
                    break;
                case 4: // Paeth
                    for (var i = 0; i < bytesPerRow; i++)
                    {
                        var a = i > 0 ? currentRow[i - 1] : 0;
                        var b = prevRow[i];
                        var c = i > 0 ? prevRow[i - 1] : 0;
                        currentRow[i] = (byte)(currentRow[i] + IndexedPngLoader.PaethPredictor(a, b, c));
                    }
                    break;
            }

            // Extract palette indices from row
            for (var x = 0; x < width; x++)
            {
                int index;
                if (bitDepth == 8)
                {
                    index = x < currentRow.Length ? currentRow[x] : 0;
                }
                else if (bitDepth == 4)
                {
                    var byteIndex = x / 2;
                    var nibble = x % 2;
                    if (byteIndex < currentRow.Length)
                    {
                        index = nibble == 0
                            ? (currentRow[byteIndex] >> 4) & 0x0F
                            : currentRow[byteIndex] & 0x0F;
                    }
                    else
                    {
                        index = 0;
                    }
                }
                else if (bitDepth == 2)
                {
                    var byteIndex = x / 4;
                    var shift = 6 - (x % 4) * 2;
                    index = byteIndex < currentRow.Length ? (currentRow[byteIndex] >> shift) & 0x03 : 0;
                }
                else if (bitDepth == 1)
                {
                    var byteIndex = x / 8;
                    var shift = 7 - (x % 8);
                    index = byteIndex < currentRow.Length ? (currentRow[byteIndex] >> shift) & 0x01 : 0;
                }
                else
                {
                    index = 0;
                }

                indices[y * width + x] = (byte)index;
            }

            Array.Copy(currentRow, prevRow, bytesPerRow);
        }

        return (width, height, bitDepth, indices);
    }

    private string ConvertPicTableNameToSpriteName(string picTableName, string category)
    {
        // Remove category prefixes, keep PascalCase for Porycon3
        var prefixes = new[] { "Brendan", "May", "RubySapphireBrendan", "RubySapphireMay" };
        foreach (var prefix in prefixes)
        {
            if (picTableName.StartsWith(prefix))
            {
                var suffix = picTableName[prefix.Length..];
                return string.IsNullOrEmpty(suffix) ? picTableName : suffix;
            }
        }

        return picTableName;
    }

    private static string PascalToSnakeCase(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;

        var result = new List<char>();
        for (var i = 0; i < input.Length; i++)
        {
            var c = input[i];
            if (i > 0 && (char.IsUpper(c) || (char.IsDigit(c) && i > 0 && !char.IsDigit(input[i - 1]))))
            {
                result.Add('_');
            }
            result.Add(char.ToLowerInvariant(c));
        }
        return new string(result.ToArray());
    }

    /// <summary>
    /// Path-aware version of ToPascalCase that handles paths with slashes.
    /// </summary>
    private static string PathToPascalCase(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;

        // Handle paths with slashes - convert each segment
        if (input.Contains('/'))
        {
            var segments = input.Split('/');
            return string.Join("/", segments.Select(ToPascalCase));
        }

        return ToPascalCase(input);
    }

    private static string StripCommonSuffixes(string name)
    {
        if (name.EndsWith("Normal")) return name[..^6];
        if (name.EndsWith("Running")) return name[..^7];
        return name;
    }

    /// <summary>
    /// Reconstruct graphics ID from pic table name, source category, and sprite name.
    /// Ensures berry trees and other objects have the correct prefix to match actual OBJ_EVENT_GFX names.
    /// </summary>
    private static string ReconstructGraphicsId(string picTableName, string sourceCategory, string spriteName)
    {
        // For berry trees, the graphics ID should be BERRY_TREE_{NAME}
        if (sourceCategory == "berry_trees")
        {
            // Convert sprite name to uppercase with underscores (e.g., "Cheri" -> "CHERI")
            var normalizedName = picTableName.ToUpperInvariant().Replace(" ", "_");
            return $"OBJ_EVENT_GFX_BERRY_TREE_{normalizedName}";
        }

        // Handle pokeball names - convert to ITEM_BALL or POKE_BALL format
        // PokeBall, Pokeball, Poke_Ball -> POKE_BALL
        // FastBall, GreatBall, etc. -> ITEM_BALL (generic item ball)
        var picTableUpper = picTableName.ToUpperInvariant().Replace("_", "").Replace(" ", "");
        if (picTableUpper == "POKEBALL")
        {
            return "OBJ_EVENT_GFX_POKE_BALL";
        }
        if (picTableUpper.EndsWith("BALL") && picTableUpper != "POKEBALL")
        {
            // All other pokeballs (FastBall, GreatBall, etc.) use ITEM_BALL
            return "OBJ_EVENT_GFX_ITEM_BALL";
        }

        // Normalize all names to snake_case to match OBJ_EVENT_GFX format
        // This ensures PascalCase names (e.g., AquaMemberM) become snake_case (aqua_member_m)
        // which matches what IdTransformer.SpriteId() expects
        var normalizedSnakeCase = IdTransformer.Normalize(picTableName);
        
        // Handle misc objects that need special handling
        var miscObjects = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "apricorn_tree", "birth_island_stone", "breakable_rock", "cable_car",
            "cuttable_tree", "fossil", "light", "mart_light", "moving_box",
            "mr_brineys_boat", "poke_center_light", "pushable_boulder",
            "ss_tidal", "statue", "submarine_shadow", "truck"
        };
        
        // For all names, use normalized snake_case format to match OBJ_EVENT_GFX constants
        // e.g., "AquaMemberM" -> "aqua_member_m" -> "OBJ_EVENT_GFX_AQUA_MEMBER_M"
        return $"OBJ_EVENT_GFX_{normalizedSnakeCase.ToUpperInvariant()}";
    }

    /// <summary>
    /// Check if a filename matches a pattern that should override the category folder.
    /// Returns the override folder if matched, null otherwise.
    /// </summary>
    private static string? GetPatternOverrideFolder(string filename)
    {
        // Remove any extension and check if it's a known pokeball name
        var nameWithoutExt = Path.GetFileNameWithoutExtension(filename);
        if (PokeballNames.Contains(nameWithoutExt))
        {
            return "Objects/Pokeballs";
        }
        return null;
    }

    /// <summary>
    /// Transform pokeball names from "ball_fast" to "FastBall" format.
    /// </summary>
    private static string TransformPokeballName(string name)
    {
        var lower = name.ToLowerInvariant();

        // Handle "ball_X" pattern -> "XBall"
        if (lower.StartsWith("ball_") || lower.StartsWith("ball"))
        {
            var suffix = lower.StartsWith("ball_") ? name[5..] : name[4..];
            if (!string.IsNullOrEmpty(suffix))
            {
                var pascalSuffix = char.ToUpperInvariant(suffix[0]) + suffix[1..].ToLowerInvariant();
                return $"{pascalSuffix}Ball";
            }
        }

        // Handle "BallX" pattern (from PascalCase) -> "XBall"
        if (name.StartsWith("Ball") && name.Length > 4 && char.IsUpper(name[4]))
        {
            var suffix = name[4..];
            return $"{suffix}Ball";
        }

        return name;
    }
}

// Data classes for sprite definitions

public class SpriteSheetInfo
{
    public int FrameWidth { get; set; }
    public int FrameHeight { get; set; }
    public int FrameCount { get; set; }
}

public class FrameDefinition
{
    public int Index { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
}

public class SpriteAnimation
{
    public string Name { get; set; } = "";
    public bool Loop { get; set; }
    public List<int> FrameIndices { get; set; } = new();
    public List<double> FrameDurations { get; set; } = new();
    public bool FlipHorizontal { get; set; }
}

public class SpriteManifest
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Type { get; set; } = "Sprite";
    public string TexturePath { get; set; } = "";
    public int FrameWidth { get; set; }
    public int FrameHeight { get; set; }
    public int FrameCount { get; set; }
    public List<FrameDefinition> Frames { get; set; } = new();
    public List<SpriteAnimation> Animations { get; set; } = new();
}
