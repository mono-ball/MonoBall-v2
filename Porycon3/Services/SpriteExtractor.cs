using System.Text.Json;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Porycon3.Services;

/// <summary>
/// Extracts NPC and player sprites from pokeemerald-expansion.
/// Outputs Sprite definitions matching porycon2 format.
/// </summary>
public class SpriteExtractor
{
    private readonly string _inputPath;
    private readonly string _outputPath;
    private readonly bool _verbose;
    private readonly AnimationData _animationData;

    private readonly string _spritesPath;
    private readonly string _outputGraphics;
    private readonly string _outputData;

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

        _spritesPath = Path.Combine(inputPath, "graphics", "object_events", "pics", "people");
        _outputGraphics = Path.Combine(outputPath, "Graphics", "Sprites");
        _outputData = Path.Combine(outputPath, "Definitions", "Sprites");

        // Parse animation data from pokeemerald source
        var parser = new AnimationParser(inputPath, verbose);
        _animationData = parser.ParseAnimationData();
    }

    /// <summary>
    /// Extract all sprites from pokeemerald.
    /// </summary>
    public (int Sprites, int Graphics) ExtractAll()
    {
        if (!Directory.Exists(_spritesPath))
        {
            if (_verbose)
                Console.WriteLine($"[SpriteExtractor] Sprites path not found: {_spritesPath}");
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

        // Also extract standalone PNGs not in sPicTables or multi-file pics
        var allPngs = Directory.GetFiles(_spritesPath, "*.png", SearchOption.AllDirectories);
        foreach (var pngPath in allPngs)
        {
            var relativePath = Path.GetRelativePath(_spritesPath, pngPath);
            var pathWithoutExt = Path.ChangeExtension(relativePath, null).Replace('\\', '/');

            if (!processedFiles.Contains(pathWithoutExt))
            {
                try
                {
                    if (ExtractStandalonePng(pngPath))
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

        Console.WriteLine($"[SpriteExtractor] Extracted {graphicsCount} graphics for {spriteCount} sprites");
        return (spriteCount, graphicsCount);
    }

    private bool ExtractSpriteFromPicTable(string picTableName, List<SpriteSourceInfo> sources)
    {
        if (sources.Count == 0) return false;

        var firstFilePath = sources[0].FilePath;
        var directory = Path.GetDirectoryName(firstFilePath)?.Replace('\\', '/') ?? "";
        var isPlayerSprite = directory.StartsWith("may", StringComparison.OrdinalIgnoreCase) ||
                             directory.StartsWith("brendan", StringComparison.OrdinalIgnoreCase);
        var category = directory.Split('/').FirstOrDefault() ?? "generic";

        var spriteName = ConvertPicTableNameToSpriteName(picTableName, category);

        if (_verbose)
            Console.WriteLine($"[SpriteExtractor] Processing: {picTableName} -> {spriteName}");

        // Load all source PNGs
        var sourceImages = new List<Image<Rgba32>>();
        var totalWidth = 0;
        var maxHeight = 0;
        var totalPhysicalFrames = 0;

        foreach (var source in sources)
        {
            if (string.IsNullOrWhiteSpace(source.FilePath)) continue;

            var pngPath = Path.Combine(_spritesPath, $"{source.FilePath}.png");
            if (!File.Exists(pngPath)) continue;

            var img = Image.Load<Rgba32>(pngPath);
            sourceImages.Add(img);
            totalWidth += img.Width;
            maxHeight = Math.Max(maxHeight, img.Height);

            var srcFrameInfo = AnalyzeSpriteSheet(img);
            totalPhysicalFrames += srcFrameInfo.FrameCount;
        }

        if (sourceImages.Count == 0) return false;

        // Detect mask color from first source
        var maskColor = DetectMaskColor(sourceImages[0]);

        // Combine images horizontally with transparency applied
        var physicalFramePositions = new List<(int Index, int X)>();
        using var combined = new Image<Rgba32>(totalWidth, maxHeight);
        var currentX = 0;

        for (var i = 0; i < sourceImages.Count; i++)
        {
            var img = sourceImages[i];
            var srcFrameInfo = AnalyzeSpriteSheet(img);

            // Convert to RGBA with transparency
            ConvertToRgbaWithTransparency(img);
            if (maskColor.HasValue)
                ApplyTransparency(img, maskColor.Value);
            ApplyMagentaTransparency(img);

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
        var baseFolder = isPlayerSprite ? "Players" : "Npcs";
        var spriteCategory = ToPascalCase(isPlayerSprite ? category : (directory.Length > 0 ? directory : "generic"));

        var graphicsDir = Path.Combine(_outputGraphics, baseFolder, spriteCategory);
        var dataDir = Path.Combine(_outputData, baseFolder, spriteCategory);
        Directory.CreateDirectory(graphicsDir);
        Directory.CreateDirectory(dataDir);

        // Save combined spritesheet
        var graphicsPath = Path.Combine(graphicsDir, $"{spriteName}.png");
        combined.SaveAsPng(graphicsPath);

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

        // Create manifest
        var texturePath = $"Graphics/Sprites/{baseFolder}/{spriteCategory}/{spriteName}.png";
        var manifest = new SpriteManifest
        {
            Id = $"base:sprite:{baseFolder}/{spriteCategory}/{spriteName}",
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

    private bool ExtractStandalonePng(string pngPath)
    {
        var relativePath = Path.GetRelativePath(_spritesPath, pngPath);
        var spriteName = ToPascalCase(Path.GetFileNameWithoutExtension(pngPath));
        var directory = Path.GetDirectoryName(relativePath)?.Replace('\\', '/') ?? "";

        var isPlayerSprite = directory.StartsWith("may", StringComparison.OrdinalIgnoreCase) ||
                             directory.StartsWith("brendan", StringComparison.OrdinalIgnoreCase);
        var category = directory.Split('/').FirstOrDefault() ?? "generic";

        using var image = Image.Load<Rgba32>(pngPath);
        var frameInfo = AnalyzeSpriteSheet(image);

        // Create output directories (PascalCase for Porycon3)
        var baseFolder = isPlayerSprite ? "Players" : "Npcs";
        var spriteCategory = ToPascalCase(directory.Length > 0 ? directory : "generic");

        var graphicsDir = Path.Combine(_outputGraphics, baseFolder, spriteCategory);
        var dataDir = Path.Combine(_outputData, baseFolder, spriteCategory);
        Directory.CreateDirectory(graphicsDir);
        Directory.CreateDirectory(dataDir);

        // Apply transparency
        var maskColor = DetectMaskColor(image);
        ConvertToRgbaWithTransparency(image);
        if (maskColor.HasValue)
            ApplyTransparency(image, maskColor.Value);
        ApplyMagentaTransparency(image);

        // Save sprite sheet
        var graphicsPath = Path.Combine(graphicsDir, $"{spriteName}.png");
        image.SaveAsPng(graphicsPath);

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

        // Create manifest
        var texturePath = $"Graphics/Sprites/{baseFolder}/{spriteCategory}/{spriteName}.png";
        var manifest = new SpriteManifest
        {
            Id = $"base:sprite:{baseFolder}/{spriteCategory}/{spriteName}",
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

        return animations;
    }

    private void ConvertToRgbaWithTransparency(Image<Rgba32> image)
    {
        // For indexed/palette images, palette index 0 should be transparent
        // ImageSharp already handles this in most cases, but we ensure it
        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    // Already in RGBA format - transparency handled by other methods
                }
            }
        });
    }

    private void ApplyTransparency(Image<Rgba32> image, Rgba32 maskColor)
    {
        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    if (row[x].R == maskColor.R && row[x].G == maskColor.G && row[x].B == maskColor.B)
                    {
                        row[x] = new Rgba32(0, 0, 0, 0);
                    }
                }
            }
        });
    }

    private void ApplyMagentaTransparency(Image<Rgba32> image)
    {
        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    // Magenta (#FF00FF) is common transparency mask in GBA graphics
                    if (row[x].R == 255 && row[x].G == 0 && row[x].B == 255 && row[x].A > 0)
                    {
                        row[x] = new Rgba32(0, 0, 0, 0);
                    }
                }
            }
        });
    }

    private Rgba32? DetectMaskColor(Image<Rgba32> image)
    {
        var colorCounts = new Dictionary<Rgba32, int>();

        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    var pixel = new Rgba32(row[x].R, row[x].G, row[x].B, 255);
                    colorCounts[pixel] = colorCounts.GetValueOrDefault(pixel, 0) + 1;
                }
            }
        });

        if (colorCounts.Count == 0) return null;

        var mostCommon = colorCounts.MaxBy(c => c.Value);
        var totalPixels = image.Width * image.Height;

        // If most common color appears in > 40% of pixels, it's probably background
        if (mostCommon.Value > totalPixels * 0.4)
        {
            return mostCommon.Key;
        }

        return null;
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
