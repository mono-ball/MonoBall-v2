using System.Text.Json;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Porycon3.Services;

/// <summary>
/// Extracts overworld sprites from pokeemerald-expansion.
/// Handles all sprite categories: people, berry_trees, cushions, dolls, misc, pokemon_old.
/// Outputs Sprite definitions matching porycon2 format.
/// </summary>
public class SpriteExtractor
{
    private readonly string _inputPath;
    private readonly string _outputPath;
    private readonly bool _verbose;
    private readonly AnimationData _animationData;

    private readonly string _picsBasePath;
    private readonly string _outputGraphics;
    private readonly string _outputData;

    // Sprite categories with their base folder mappings
    private static readonly Dictionary<string, string> CategoryMappings = new()
    {
        { "people", "Characters/Npcs" },
        { "berry_trees", "Objects/BerryTrees" },
        { "cushions", "Objects/Cushions" },
        { "dolls", "Objects/Dolls" },
        { "misc", "Objects/Misc" },
        { "pokemon_old", "Characters/Pokemon" }
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

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public SpriteExtractor(string inputPath, string outputPath, bool verbose = false)
    {
        _inputPath = inputPath;
        _outputPath = outputPath;
        _verbose = verbose;

        _picsBasePath = Path.Combine(inputPath, "graphics", "object_events", "pics");
        _outputGraphics = Path.Combine(outputPath, "Graphics");
        _outputData = Path.Combine(outputPath, "Definitions", "Assets");

        // Parse animation data from pokeemerald source
        var parser = new AnimationParser(inputPath, verbose);
        _animationData = parser.ParseAnimationData();
    }

    /// <summary>
    /// Extract all sprites from pokeemerald.
    /// </summary>
    public (int Sprites, int Graphics) ExtractAll()
    {
        if (!Directory.Exists(_picsBasePath))
        {
            if (_verbose)
                Console.WriteLine($"[SpriteExtractor] Pics path not found: {_picsBasePath}");
            return (0, 0);
        }

        Directory.CreateDirectory(_outputGraphics);
        Directory.CreateDirectory(_outputData);

        int spriteCount = 0;
        int graphicsCount = 0;
        var processedFiles = new HashSet<string>();

        // Extract sprites based on sPicTable definitions
        foreach (var (picTableName, sources) in _animationData.PicTableSources)
        {
            // Skip pic tables with no valid sources
            var validSources = sources.Where(s => !string.IsNullOrWhiteSpace(s.FilePath)).ToList();
            if (validSources.Count == 0) continue;

            try
            {
                if (ExtractSpriteFromPicTable(picTableName, validSources))
                {
                    spriteCount++;
                    graphicsCount++;
                    foreach (var source in validSources)
                    {
                        processedFiles.Add(source.FilePath);
                    }
                }
            }
            catch (Exception ex)
            {
                if (_verbose)
                    Console.WriteLine($"[SpriteExtractor] Error processing {picTableName}: {ex.Message}");
            }
        }

        // Mark files that are part of multi-file pics as processed
        // (e.g., walking.png + running.png are combined into BrendanNormalRunning)
        foreach (var (_, files) in _animationData.MultiFilePics)
        {
            foreach (var file in files)
            {
                processedFiles.Add(file);
            }
        }

        // Process each sprite category directory
        foreach (var category in CategoryMappings.Keys)
        {
            var categoryPath = Path.Combine(_picsBasePath, category);
            if (!Directory.Exists(categoryPath)) continue;

            // Extract standalone PNGs not in sPicTables or multi-file pics
            var allPngs = Directory.GetFiles(categoryPath, "*.png", SearchOption.AllDirectories);
            foreach (var pngPath in allPngs)
            {
                // Build relative path from pics base (e.g., "people/may/walking" or "misc/ball_poke")
                var relativePath = Path.GetRelativePath(_picsBasePath, pngPath);
                var pathWithoutExt = Path.ChangeExtension(relativePath, null).Replace('\\', '/');

                if (!processedFiles.Contains(pathWithoutExt))
                {
                    try
                    {
                        if (ExtractStandalonePng(pngPath, category))
                        {
                            spriteCount++;
                            graphicsCount++;
                        }
                    }
                    catch (Exception ex)
                    {
                        if (_verbose)
                            Console.WriteLine($"[SpriteExtractor] Error processing {Path.GetFileName(pngPath)}: {ex.Message}");
                    }
                }
            }
        }

        Console.WriteLine($"[SpriteExtractor] Extracted {graphicsCount} graphics for {spriteCount} sprites");
        return (spriteCount, graphicsCount);
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

        if (_verbose)
            Console.WriteLine($"[SpriteExtractor] Processing: {picTableName} -> {spriteName} (category: {sourceCategory})");

        // Load all source PNGs, handling multi-file pics
        var sourceImages = new List<Image<Rgba32>>();
        var totalWidth = 0;
        var maxHeight = 0;
        var totalPhysicalFrames = 0;
        var allSourceFiles = new List<string>();

        foreach (var source in sources)
        {
            if (string.IsNullOrWhiteSpace(source.PicName)) continue;

            // Check if this pic is a multi-file pic (e.g., walking + running combined)
            if (_animationData.MultiFilePics.TryGetValue(source.PicName, out var multiFiles))
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
            spriteCategory = ToPascalCase(subPath);
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
            spriteCategory = subPath.Length > 0 ? ToPascalCase(subPath) : "";
        }
        else
        {
            baseFolder = "Characters/Npcs";
            spriteCategory = subPath.Length > 0 ? ToPascalCase(subPath) : "Generic";
        }

        var graphicsDir = string.IsNullOrEmpty(spriteCategory)
            ? Path.Combine(_outputGraphics, baseFolder)
            : Path.Combine(_outputGraphics, baseFolder, spriteCategory);
        var dataDir = string.IsNullOrEmpty(spriteCategory)
            ? Path.Combine(_outputData, baseFolder)
            : Path.Combine(_outputData, baseFolder, spriteCategory);
        Directory.CreateDirectory(graphicsDir);
        Directory.CreateDirectory(dataDir);

        // Save combined spritesheet as 32-bit RGBA
        var graphicsPath = Path.Combine(graphicsDir, $"{spriteName}.png");
        SaveAsRgbaPng(combined, graphicsPath);

        // Get physical frame mapping
        var physicalFrameMapping = _animationData.FrameMappings.GetValueOrDefault(picTableName);

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
        var animations = GenerateAnimations(picTableName, frameInfo);

        // Create manifest (IDs are lowercase, file paths preserve case)
        var relativePath = string.IsNullOrEmpty(spriteCategory) ? spriteName : $"{spriteCategory}/{spriteName}";
        var texturePath = $"Graphics/{baseFolder}/{relativePath}.png";
        var idPath = $"{baseFolder}/{relativePath}".ToLowerInvariant();
        var manifest = new SpriteManifest
        {
            Id = $"{IdTransformer.Namespace}:sprite:{idPath}",
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
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, JsonOptions));

        // Cleanup
        foreach (var img in sourceImages)
            img.Dispose();

        return true;
    }

    private bool ExtractStandalonePng(string pngPath, string sourceCategory)
    {
        // Get path relative to the category directory
        var categoryPath = Path.Combine(_picsBasePath, sourceCategory);
        var relativePath = Path.GetRelativePath(categoryPath, pngPath);
        var spriteName = ToPascalCase(Path.GetFileNameWithoutExtension(pngPath));
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
            spriteCategory = ToPascalCase(subDirectory);
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
            spriteCategory = subDirectory.Length > 0 ? ToPascalCase(subDirectory) : "";
        }
        else
        {
            baseFolder = "Characters/Npcs";
            spriteCategory = subDirectory.Length > 0 ? ToPascalCase(subDirectory) : "Generic";
        }

        var graphicsDir = string.IsNullOrEmpty(spriteCategory)
            ? Path.Combine(_outputGraphics, baseFolder)
            : Path.Combine(_outputGraphics, baseFolder, spriteCategory);
        var dataDir = string.IsNullOrEmpty(spriteCategory)
            ? Path.Combine(_outputData, baseFolder)
            : Path.Combine(_outputData, baseFolder, spriteCategory);
        Directory.CreateDirectory(graphicsDir);
        Directory.CreateDirectory(dataDir);

        // Save sprite sheet as 32-bit RGBA
        var graphicsPath = Path.Combine(graphicsDir, $"{spriteName}.png");
        SaveAsRgbaPng(image, graphicsPath);

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
        var animations = GenerateAnimations(spriteName, frameInfo);

        // Create manifest (IDs are lowercase, file paths preserve case)
        var outputRelativePath = string.IsNullOrEmpty(spriteCategory) ? spriteName : $"{spriteCategory}/{spriteName}";
        var texturePath = $"Graphics/{baseFolder}/{outputRelativePath}.png";
        var idPath = $"{baseFolder}/{outputRelativePath}".ToLowerInvariant();
        var manifest = new SpriteManifest
        {
            Id = $"{IdTransformer.Namespace}:sprite:{idPath}",
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
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, JsonOptions));

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

    private List<SpriteAnimation> GenerateAnimations(string spriteName, SpriteSheetInfo info)
    {
        var animations = new List<SpriteAnimation>();

        // Try to find animation table for this sprite
        var possibleNames = new[]
        {
            spriteName,
            StripCommonSuffixes(spriteName),
            $"May{ToPascalCase(spriteName)}",
            $"Brendan{ToPascalCase(spriteName)}"
        };

        string? animTableName = null;
        foreach (var name in possibleNames)
        {
            if (_animationData.SpriteToAnimTable.TryGetValue(name, out var tableName))
            {
                animTableName = tableName;
                break;
            }
        }

        if (animTableName != null && _animationData.AnimationTables.TryGetValue(animTableName, out var animDefs))
        {
            var frameCount = _animationData.FrameCounts.GetValueOrDefault(spriteName, info.FrameCount);

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

        // Load indexed image and convert with index 0 = transparent
        using var indexedStream = new MemoryStream(bytes);
        using var indexed = Image.Load<L8>(indexedStream);

        var output = new Image<Rgba32>(indexed.Width, indexed.Height);

        indexed.ProcessPixelRows(output, (srcAccessor, dstAccessor) =>
        {
            for (var y = 0; y < srcAccessor.Height; y++)
            {
                var srcRow = srcAccessor.GetRowSpan(y);
                var dstRow = dstAccessor.GetRowSpan(y);

                for (var x = 0; x < srcRow.Length; x++)
                {
                    // For 4bpp indexed, the L8 value represents the palette index
                    // In grayscale representation: white (255) = index 0, black (0) = index 15
                    var grayValue = srcRow[x].PackedValue;
                    var paletteIndex = 15 - (grayValue + 8) / 17;

                    if (paletteIndex == 0)
                    {
                        // Index 0 is transparent in GBA
                        dstRow[x] = new Rgba32(0, 0, 0, 0);
                    }
                    else if (paletteIndex < palette.Length)
                    {
                        dstRow[x] = palette[paletteIndex];
                    }
                    else
                    {
                        // Fallback
                        dstRow[x] = new Rgba32(grayValue, grayValue, grayValue, 255);
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
    /// Save image as 32-bit RGBA PNG (not indexed).
    /// </summary>
    private static void SaveAsRgbaPng(Image<Rgba32> image, string path)
    {
        var encoder = new PngEncoder
        {
            ColorType = PngColorType.RgbWithAlpha,
            BitDepth = PngBitDepth.Bit8,
            CompressionLevel = PngCompressionLevel.BestCompression
        };
        image.SaveAsPng(path, encoder);
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

    private static string ToPascalCase(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;

        // Handle paths with slashes - convert each segment
        if (input.Contains('/'))
        {
            var segments = input.Split('/');
            return string.Join("/", segments.Select(ToPascalCase));
        }

        // Handle underscores
        var parts = input.Split('_');
        return string.Concat(parts.Select(p =>
            p.Length > 0 ? char.ToUpperInvariant(p[0]) + p[1..].ToLowerInvariant() : ""));
    }

    private static string StripCommonSuffixes(string name)
    {
        if (name.EndsWith("Normal")) return name[..^6];
        if (name.EndsWith("Running")) return name[..^7];
        return name;
    }

    private static string FormatDisplayName(string name)
    {
        // Handle PascalCase by inserting spaces before uppercase letters
        var result = new System.Text.StringBuilder();
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (i > 0 && char.IsUpper(c) && !char.IsUpper(name[i - 1]))
            {
                result.Append(' ');
            }
            result.Append(c);
        }
        return result.ToString();
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
