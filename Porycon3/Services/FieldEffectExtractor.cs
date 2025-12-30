using System.Text.Json;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Porycon3.Infrastructure;
using Porycon3.Services.Extraction;
using static Porycon3.Infrastructure.StringUtilities;

namespace Porycon3.Services;

/// <summary>
/// Extracts field effects from pokeemerald-expansion.
/// Slices sprite sheets into individual frames and creates definition files.
/// </summary>
public class FieldEffectExtractor : ExtractorBase
{
    public override string Name => "Field Effects";
    public override string Description => "Extracts field effect sprites and animations";

    private static readonly PngEncoder PngEncoder = new()
    {
        ColorType = PngColorType.RgbWithAlpha,
        BitDepth = PngBitDepth.Bit8,
        CompressionLevel = PngCompressionLevel.BestCompression
    };

    /// <summary>
    /// Sprite sheet definitions from pokeemerald field_effect_objects.h
    /// Format: (frameWidth, frameHeight, frameCount, isVertical)
    /// </summary>
    private static readonly Dictionary<string, (int Width, int Height, int Frames, bool Vertical)> SpriteSheetInfo = new()
    {
        // 16x16 sprites (2x2 tiles)
        { "arrow", (16, 16, 8, false) },
        { "tall_grass", (16, 16, 5, false) },
        { "ripple", (16, 16, 5, false) },
        { "ash", (16, 16, 5, false) },
        { "sand_footprints", (16, 16, 2, false) },
        { "deep_sand_footprints", (16, 16, 2, false) },
        { "bug_tracks", (16, 16, 2, false) },
        { "spot_tracks", (16, 16, 2, false) },
        { "bike_tire_tracks", (16, 16, 4, false) },
        { "slither_tracks", (16, 16, 4, false) },
        { "jump_big_splash", (16, 16, 4, false) },
        { "long_grass", (16, 16, 4, false) },
        { "jump_long_grass", (16, 16, 7, false) },
        { "unused_grass_2", (16, 16, 4, false) },
        { "unused_grass_3", (16, 16, 4, false) },
        { "unused_sand", (16, 16, 4, false) },
        { "water_surfacing", (16, 16, 5, false) },
        { "sparkle", (16, 16, 6, false) },
        { "short_grass", (16, 16, 2, false) },
        { "ash_puff", (16, 16, 5, false) },
        { "ash_launch", (16, 16, 5, false) },
        { "small_sparkle", (16, 16, 2, false) },
        { "cave_dust", (16, 16, 4, true) },
        { "secret_power_cave", (16, 16, 5, false) },
        { "secret_power_shrub", (16, 16, 5, false) },
        // 16x8 sprites
        { "ground_impact_dust", (16, 8, 3, false) },
        { "jump_tall_grass", (16, 8, 4, false) },
        { "splash", (16, 8, 2, false) },
        { "jump_small_splash", (16, 8, 3, false) },
        { "sand_pile", (16, 8, 3, false) },
        { "field_move_streaks", (16, 8, 8, false) },
        { "field_move_streaks_indoors", (16, 8, 2, false) },
        { "record_mix_lights", (16, 8, 6, false) },
        // 16x32 sprites
        { "tree_disguise", (16, 32, 7, false) },
        { "mountain_disguise", (16, 32, 7, false) },
        { "sand_disguise_placeholder", (16, 32, 7, false) },
        { "bubbles", (16, 32, 8, false) },
        // 32x32 sprites
        { "surf_blob", (32, 32, 3, false) },
        { "rock_climb_blob", (32, 32, 3, false) },
        { "rock_climb_dust", (32, 32, 3, false) },
        { "bird", (32, 32, 1, false) },
        // 16x16 single frames
        { "emote_x", (16, 16, 1, false) },
        { "emotion_double_exclamation", (16, 16, 1, false) },
        { "emotion_exclamation", (16, 16, 1, false) },
        { "emotion_heart", (16, 16, 1, false) },
        { "emotion_question", (16, 16, 1, false) },
        { "hot_springs_water", (16, 16, 1, false) },
        // 8x8 single frames
        { "shadow_small", (8, 8, 1, false) },
        { "cut_grass", (8, 8, 1, false) },
        { "pokeball_glow", (8, 8, 1, false) },
        { "deoxys_rock_fragment_bottom_left", (8, 8, 1, false) },
        { "deoxys_rock_fragment_bottom_right", (8, 8, 1, false) },
        { "deoxys_rock_fragment_top_left", (8, 8, 1, false) },
        { "deoxys_rock_fragment_top_right", (8, 8, 1, false) },
        // Other single frames
        { "shadow_medium", (16, 8, 1, false) },
        { "shadow_large", (32, 8, 1, false) },
        { "shadow_extra_large", (64, 32, 1, false) },
        // Special multi-frame sprites
        { "secret_power_tree", (16, 16, 6, false) },
        { "hof_monitor_big", (16, 16, 4, false) },
        { "hof_monitor_small", (16, 16, 2, false) },
        { "unknown_17", (16, 16, 8, false) },
        // ORAS dowsing
        { "oras_dowsing_brendan", (16, 32, 9, false) },
        { "oras_dowsing_may", (16, 32, 9, false) },
        // Spotlight
        { "spotlight", (48, 24, 5, true) },
    };

    public FieldEffectExtractor(string inputPath, string outputPath, bool verbose = false)
        : base(inputPath, outputPath, verbose)
    {
    }

    protected override int ExecuteExtraction()
    {
        var fieldEffectsPath = Path.Combine(InputPath, "graphics", "field_effects", "pics");

        if (!Directory.Exists(fieldEffectsPath))
        {
            AddError("", $"Field effects not found: {fieldEffectsPath}");
            return 0;
        }

        int count = 0;
        int frameCount = 0;

        // Get all PNG files that don't have matching subdirectories
        var pngFiles = Directory.GetFiles(fieldEffectsPath, "*.png")
            .Where(f => !Directory.Exists(Path.Combine(fieldEffectsPath, Path.GetFileNameWithoutExtension(f))))
            .ToList();

        // Get subdirectories with pre-separated frames
        var subDirs = Directory.GetDirectories(fieldEffectsPath).ToList();

        var allItems = pngFiles.Select(f => (Path: f, Name: Path.GetFileNameWithoutExtension(f), IsSubDir: false))
            .Concat(subDirs.Select(d => (Path: d, Name: Path.GetFileName(d), IsSubDir: true)))
            .ToList();

        WithProgress("Extracting field effects", allItems, (item, task) =>
        {
            SetTaskDescription(task, $"[cyan]Extracting[/] [yellow]{item.Name}[/]");

            if (item.IsSubDir)
            {
                var frames = Directory.GetFiles(item.Path, "*.png").OrderBy(f => f).ToList();
                if (frames.Count > 0)
                {
                    ExtractPreSeparatedFrames(item.Path, item.Name, frames);
                    count++;
                    frameCount += frames.Count;
                }
            }
            else
            {
                var extracted = ExtractFieldEffect(item.Path, item.Name);
                if (extracted > 0)
                {
                    count++;
                    frameCount += extracted;
                }
            }
        });

        SetCount("Frames", frameCount);
        return count;
    }

    private int ExtractFieldEffect(string sourcePath, string name)
    {
        // Check if this is a sprite sheet that needs slicing
        if (SpriteSheetInfo.TryGetValue(name, out var info) && info.Frames > 1)
        {
            return ExtractSpriteSheet(sourcePath, name, info);
        }
        else
        {
            ExtractSingleFrame(sourcePath, name);
            return 1;
        }
    }

    private int ExtractSpriteSheet(string sourcePath, string name, (int Width, int Height, int Frames, bool Vertical) info)
    {
        var id = NormalizeId(name);
        var pascalName = ToPascalCase(name);

        // Create subdirectory for frames
        var framesDir = GetGraphicsPath("FieldEffects", pascalName);
        EnsureDirectory(framesDir);

        // Load the sprite sheet with transparency
        using var spriteSheet = LoadWithIndex0Transparency(sourcePath);

        var frameNames = new List<string>();

        for (int i = 0; i < info.Frames; i++)
        {
            int x, y;
            if (info.Vertical)
            {
                x = 0;
                y = i * info.Height;
            }
            else
            {
                x = i * info.Width;
                y = 0;
            }

            // Bounds check
            if (x + info.Width > spriteSheet.Width || y + info.Height > spriteSheet.Height)
            {
                LogWarning($"Frame {i} out of bounds for {name}");
                break;
            }

            // Extract frame
            var frameRect = new Rectangle(x, y, info.Width, info.Height);
            using var frame = spriteSheet.Clone(ctx => ctx.Crop(frameRect));

            var framePath = Path.Combine(framesDir, $"{i}.png");
            frame.Save(framePath, PngEncoder);

            frameNames.Add($"Graphics/FieldEffects/{pascalName}/{i}.png");
        }

        // Create definition with frames array
        var definition = new SpriteFieldEffectDefinition
        {
            Id = $"{IdTransformer.Namespace}:field_effect:{id}",
            Name = FormatDisplayName(name),
            FrameWidth = info.Width,
            FrameHeight = info.Height,
            Frames = frameNames
        };

        var jsonPath = GetDefinitionPath("FieldEffects", $"{pascalName}.json");
        File.WriteAllText(jsonPath, JsonSerializer.Serialize(definition, JsonOptions.Default));

        LogVerbose($"Extracted {name} ({frameNames.Count} frames, {info.Width}x{info.Height})");
        return frameNames.Count;
    }

    private void ExtractSingleFrame(string sourcePath, string name)
    {
        var id = NormalizeId(name);
        var pascalName = ToPascalCase(name);

        // Load with proper GBA transparency and save
        var graphicPath = GetGraphicsPath("FieldEffects", $"{pascalName}.png");
        using var img = LoadWithIndex0Transparency(sourcePath);
        img.Save(graphicPath, PngEncoder);

        // Create definition
        var definition = new FieldEffectDefinition
        {
            Id = $"{IdTransformer.Namespace}:field_effect:{id}",
            Name = FormatDisplayName(name),
            Graphic = $"Graphics/FieldEffects/{pascalName}.png",
            Width = img.Width,
            Height = img.Height
        };

        var jsonPath = GetDefinitionPath("FieldEffects", $"{pascalName}.json");
        File.WriteAllText(jsonPath, JsonSerializer.Serialize(definition, JsonOptions.Default));

        LogVerbose($"Extracted {name} (single frame, {img.Width}x{img.Height})");
    }

    private void ExtractPreSeparatedFrames(string sourceDir, string name, List<string> frames)
    {
        var id = NormalizeId(name);
        var pascalName = ToPascalCase(name);

        // Create subdirectory for frames
        var framesDir = GetGraphicsPath("FieldEffects", pascalName);
        EnsureDirectory(framesDir);

        // Process all frames with transparency
        var frameNames = new List<string>();
        int frameWidth = 0, frameHeight = 0;

        foreach (var frame in frames)
        {
            var frameName = Path.GetFileName(frame);
            var destPath = Path.Combine(framesDir, frameName);

            using var img = LoadWithIndex0Transparency(frame);
            img.Save(destPath, PngEncoder);

            if (frameWidth == 0)
            {
                frameWidth = img.Width;
                frameHeight = img.Height;
            }

            frameNames.Add($"Graphics/FieldEffects/{pascalName}/{frameName}");
        }

        // Create definition with frames array
        var definition = new SpriteFieldEffectDefinition
        {
            Id = $"{IdTransformer.Namespace}:field_effect:{id}",
            Name = FormatDisplayName(name),
            FrameWidth = frameWidth,
            FrameHeight = frameHeight,
            Frames = frameNames
        };

        var jsonPath = GetDefinitionPath("FieldEffects", $"{pascalName}.json");
        File.WriteAllText(jsonPath, JsonSerializer.Serialize(definition, JsonOptions.Default));

        LogVerbose($"Extracted {name} ({frames.Count} pre-separated frames)");
    }

    #region Transparency Helpers

    private static Image<Rgba32> LoadWithIndex0Transparency(string pngPath)
    {
        var bytes = File.ReadAllBytes(pngPath);
        var palette = ExtractPngPalette(bytes);

        if (palette != null && palette.Length > 0)
        {
            using var tempImage = Image.Load<Rgba32>(pngPath);
            var index0Color = ExtractPaletteColor(bytes, 0);

            if (index0Color.HasValue)
            {
                var bgColor = index0Color.Value;
                tempImage.ProcessPixelRows(accessor =>
                {
                    for (int y = 0; y < accessor.Height; y++)
                    {
                        var row = accessor.GetRowSpan(y);
                        for (int x = 0; x < row.Length; x++)
                        {
                            if (row[x].R == bgColor.R && row[x].G == bgColor.G && row[x].B == bgColor.B)
                            {
                                row[x] = new Rgba32(0, 0, 0, 0);
                            }
                        }
                    }
                });
            }

            ApplyMagentaTransparency(tempImage);
            return tempImage.Clone();
        }

        var img = Image.Load<Rgba32>(pngPath);
        var firstPixel = img[0, 0];

        img.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    if (row[x].R == firstPixel.R && row[x].G == firstPixel.G && row[x].B == firstPixel.B)
                    {
                        row[x] = new Rgba32(0, 0, 0, 0);
                    }
                }
            }
        });

        ApplyMagentaTransparency(img);
        return img;
    }

    private static Rgba32[]? ExtractPngPalette(byte[] pngData)
    {
        var pos = 8;
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
            pos += 12 + length;
        }
        return null;
    }

    private static Rgba32? ExtractPaletteColor(byte[] pngData, int index)
    {
        var pos = 8;
        while (pos < pngData.Length - 12)
        {
            var length = (pngData[pos] << 24) | (pngData[pos + 1] << 16) |
                         (pngData[pos + 2] << 8) | pngData[pos + 3];
            var type = System.Text.Encoding.ASCII.GetString(pngData, pos + 4, 4);

            if (type == "PLTE")
            {
                var colorCount = length / 3;
                if (index < colorCount)
                {
                    var offset = pos + 8 + index * 3;
                    return new Rgba32(pngData[offset], pngData[offset + 1], pngData[offset + 2], 255);
                }
                return null;
            }
            pos += 12 + length;
        }
        return null;
    }

    private static void ApplyMagentaTransparency(Image<Rgba32> image)
    {
        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    if (row[x].R == 255 && row[x].G == 0 && row[x].B == 255 && row[x].A > 0)
                    {
                        row[x] = new Rgba32(0, 0, 0, 0);
                    }
                }
            }
        });
    }

    #endregion

    private static string NormalizeId(string name) => name.ToLowerInvariant().Replace("_", "-");

    private class FieldEffectDefinition
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public string? Graphic { get; set; }
        public int? Width { get; set; }
        public int? Height { get; set; }
    }

    private class SpriteFieldEffectDefinition
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public int FrameWidth { get; set; }
        public int FrameHeight { get; set; }
        public List<string>? Frames { get; set; }
    }
}
