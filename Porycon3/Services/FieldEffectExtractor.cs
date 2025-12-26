using System.Text.Json;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Porycon3.Services;

/// <summary>
/// Extracts field effects from pokeemerald-expansion.
/// Slices sprite sheets into individual frames and creates definition files.
/// </summary>
public class FieldEffectExtractor
{
    private readonly string _inputPath;
    private readonly string _outputPath;
    private readonly bool _verbose;

    private readonly string _fieldEffectsPath;
    private readonly string _outputGraphics;
    private readonly string _outputData;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

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
        { "jump_long_grass", (16, 16, 7, false) },  // 7 frames (112/16)
        { "unused_grass_2", (16, 16, 4, false) },
        { "unused_grass_3", (16, 16, 4, false) },
        { "unused_sand", (16, 16, 4, false) },
        { "water_surfacing", (16, 16, 5, false) },  // 5 frames (80/16)
        { "sparkle", (16, 16, 6, false) },
        { "short_grass", (16, 16, 2, false) },
        { "ash_puff", (16, 16, 5, false) },
        { "ash_launch", (16, 16, 5, false) },
        { "small_sparkle", (16, 16, 2, false) },
        { "cave_dust", (16, 16, 4, true) },  // Vertical layout!
        { "secret_power_cave", (16, 16, 5, false) },
        { "secret_power_shrub", (16, 16, 5, false) },

        // 16x8 sprites (2x1 tiles)
        { "ground_impact_dust", (16, 8, 3, false) },
        { "jump_tall_grass", (16, 8, 4, false) },
        { "splash", (16, 8, 2, false) },
        { "jump_small_splash", (16, 8, 3, false) },
        { "sand_pile", (16, 8, 3, false) },
        { "field_move_streaks", (16, 8, 8, false) },
        { "field_move_streaks_indoors", (16, 8, 2, false) },
        { "record_mix_lights", (16, 8, 6, false) },

        // 16x32 sprites (2x4 tiles)
        { "tree_disguise", (16, 32, 7, false) },
        { "mountain_disguise", (16, 32, 7, false) },
        { "sand_disguise_placeholder", (16, 32, 7, false) },
        { "bubbles", (16, 32, 8, false) },

        // 32x32 sprites (4x4 tiles)
        { "surf_blob", (32, 32, 3, false) },
        { "rock_climb_blob", (32, 32, 3, false) },
        { "rock_climb_dust", (32, 32, 3, false) },
        { "bird", (32, 32, 1, false) },  // Single frame

        // 16x16 single frames (no slicing needed)
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

        // ORAS dowsing (larger frames)
        { "oras_dowsing_brendan", (16, 32, 9, false) },
        { "oras_dowsing_may", (16, 32, 9, false) },

        // Spotlight (special vertical layout)
        { "spotlight", (48, 24, 5, true) },
    };

    public FieldEffectExtractor(string inputPath, string outputPath, bool verbose = false)
    {
        _inputPath = inputPath;
        _outputPath = outputPath;
        _verbose = verbose;

        _fieldEffectsPath = Path.Combine(inputPath, "graphics", "field_effects", "pics");
        _outputGraphics = Path.Combine(outputPath, "Graphics", "FieldEffects");
        _outputData = Path.Combine(outputPath, "Definitions", "Assets", "FieldEffects");
    }

    /// <summary>
    /// Extract all field effects.
    /// </summary>
    /// <returns>Number of field effects extracted.</returns>
    public int ExtractAll()
    {
        if (!Directory.Exists(_fieldEffectsPath))
        {
            Console.WriteLine($"[FieldEffectExtractor] Field effects not found: {_fieldEffectsPath}");
            return 0;
        }

        Directory.CreateDirectory(_outputGraphics);
        Directory.CreateDirectory(_outputData);

        int count = 0;

        // Process all PNG files in the root directory
        foreach (var pngFile in Directory.GetFiles(_fieldEffectsPath, "*.png"))
        {
            var name = Path.GetFileNameWithoutExtension(pngFile);

            // Skip if there's a subdirectory with the same name (those are handled separately)
            var subDir = Path.Combine(_fieldEffectsPath, name);
            if (Directory.Exists(subDir))
                continue;

            ExtractFieldEffect(pngFile, name);
            count++;
        }

        // Process subdirectories (pre-separated animation frames)
        foreach (var subDir in Directory.GetDirectories(_fieldEffectsPath))
        {
            var name = Path.GetFileName(subDir);
            var frames = Directory.GetFiles(subDir, "*.png")
                .OrderBy(f => f)
                .ToList();

            if (frames.Count > 0)
            {
                ExtractPreSeparatedFrames(subDir, name, frames);
                count++;
            }
        }

        Console.WriteLine($"[FieldEffectExtractor] Extracted {count} field effects");
        return count;
    }

    private void ExtractFieldEffect(string sourcePath, string name)
    {
        var id = NormalizeId(name);
        var pascalName = ToPascalCase(name);

        // Check if this is a sprite sheet that needs slicing
        if (SpriteSheetInfo.TryGetValue(name, out var info) && info.Frames > 1)
        {
            ExtractSpriteSheet(sourcePath, name, info);
        }
        else
        {
            // Single frame - just copy with transparency
            ExtractSingleFrame(sourcePath, name);
        }
    }

    private void ExtractSpriteSheet(string sourcePath, string name, (int Width, int Height, int Frames, bool Vertical) info)
    {
        var id = NormalizeId(name);
        var pascalName = ToPascalCase(name);

        // Create subdirectory for frames
        var framesDir = Path.Combine(_outputGraphics, pascalName);
        Directory.CreateDirectory(framesDir);

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
                if (_verbose)
                    Console.WriteLine($"[FieldEffectExtractor] Warning: Frame {i} out of bounds for {name}");
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
            Id = $"base:field_effect:{id}",
            Name = FormatDisplayName(name),
            FrameWidth = info.Width,
            FrameHeight = info.Height,
            Frames = frameNames
        };

        var jsonPath = Path.Combine(_outputData, $"{pascalName}.json");
        var json = JsonSerializer.Serialize(definition, JsonOptions);
        File.WriteAllText(jsonPath, json);

        if (_verbose)
            Console.WriteLine($"[FieldEffectExtractor] Extracted {name} ({frameNames.Count} frames, {info.Width}x{info.Height})");
    }

    private void ExtractSingleFrame(string sourcePath, string name)
    {
        var id = NormalizeId(name);
        var pascalName = ToPascalCase(name);

        // Load with proper GBA transparency and save
        var graphicPath = Path.Combine(_outputGraphics, $"{pascalName}.png");
        using var img = LoadWithIndex0Transparency(sourcePath);
        img.Save(graphicPath, PngEncoder);

        // Create definition
        var definition = new FieldEffectDefinition
        {
            Id = $"base:field_effect:{id}",
            Name = FormatDisplayName(name),
            Graphic = $"Graphics/FieldEffects/{pascalName}.png",
            Width = img.Width,
            Height = img.Height
        };

        var jsonPath = Path.Combine(_outputData, $"{pascalName}.json");
        var json = JsonSerializer.Serialize(definition, JsonOptions);
        File.WriteAllText(jsonPath, json);

        if (_verbose)
            Console.WriteLine($"[FieldEffectExtractor] Extracted {name} (single frame, {img.Width}x{img.Height})");
    }

    private void ExtractPreSeparatedFrames(string sourceDir, string name, List<string> frames)
    {
        var id = NormalizeId(name);
        var pascalName = ToPascalCase(name);

        // Create subdirectory for frames
        var framesDir = Path.Combine(_outputGraphics, pascalName);
        Directory.CreateDirectory(framesDir);

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
            Id = $"base:field_effect:{id}",
            Name = FormatDisplayName(name),
            FrameWidth = frameWidth,
            FrameHeight = frameHeight,
            Frames = frameNames
        };

        var jsonPath = Path.Combine(_outputData, $"{pascalName}.json");
        var json = JsonSerializer.Serialize(definition, JsonOptions);
        File.WriteAllText(jsonPath, json);

        if (_verbose)
            Console.WriteLine($"[FieldEffectExtractor] Extracted {name} ({frames.Count} pre-separated frames)");
    }

    #region Transparency Helpers

    /// <summary>
    /// Load an indexed PNG and convert to RGBA with palette index 0 as transparent.
    /// This is how GBA/pokeemerald handles sprite transparency.
    /// </summary>
    private static Image<Rgba32> LoadWithIndex0Transparency(string pngPath)
    {
        // Read raw PNG bytes to extract palette
        var bytes = File.ReadAllBytes(pngPath);

        // Extract palette from PNG PLTE chunk
        var palette = ExtractPngPalette(bytes);

        if (palette != null && palette.Length > 0)
        {
            // Load as RGBA
            using var tempImage = Image.Load<Rgba32>(pngPath);

            // Get the color that was at palette index 0
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
                            // Check if pixel matches palette index 0 color
                            if (row[x].R == bgColor.R && row[x].G == bgColor.G && row[x].B == bgColor.B)
                            {
                                row[x] = new Rgba32(0, 0, 0, 0);
                            }
                        }
                    }
                });
            }

            // Also apply magenta transparency as fallback
            ApplyMagentaTransparency(tempImage);

            return tempImage.Clone();
        }

        // Fallback: load as RGBA and use first pixel as background color
        var img = Image.Load<Rgba32>(pngPath);
        var firstPixel = img[0, 0];

        // Make all pixels matching first pixel transparent
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

        // Also apply magenta transparency
        ApplyMagentaTransparency(img);

        return img;
    }

    /// <summary>
    /// Extract RGB palette from PNG PLTE chunk.
    /// </summary>
    private static Rgba32[]? ExtractPngPalette(byte[] pngData)
    {
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
    /// Extract a specific palette color from PNG PLTE chunk.
    /// </summary>
    private static Rgba32? ExtractPaletteColor(byte[] pngData, int index)
    {
        var pos = 8; // Skip PNG signature

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

    /// <summary>
    /// Apply transparency for magenta (#FF00FF) pixels, a common GBA transparency mask.
    /// </summary>
    private static void ApplyMagentaTransparency(Image<Rgba32> image)
    {
        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    // Magenta (#FF00FF) is commonly used as transparency mask in GBA graphics
                    if (row[x].R == 255 && row[x].G == 0 && row[x].B == 255 && row[x].A > 0)
                    {
                        row[x] = new Rgba32(0, 0, 0, 0);
                    }
                }
            }
        });
    }

    #endregion

    private string NormalizeId(string name)
    {
        // Convert to lowercase with underscores to hyphens
        return name.ToLowerInvariant().Replace("_", "-");
    }

    private string ToPascalCase(string name)
    {
        // Convert snake_case to PascalCase
        return string.Join("", name.Split('_')
            .Select(p => char.ToUpper(p[0]) + p.Substring(1).ToLower()));
    }

    private string FormatDisplayName(string name)
    {
        // Convert snake_case to Title Case
        return string.Join(" ", name.Split('_')
            .Select(p => char.ToUpper(p[0]) + p.Substring(1).ToLower()));
    }

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
