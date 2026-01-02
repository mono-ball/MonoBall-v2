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

    // Game frames to seconds conversion (GBA runs at 60fps)
    private const double GameFrameToSeconds = 1.0 / 60.0;

    /// <summary>
    /// Sprite sheet dimensions from pokeemerald field_effect_objects.h
    /// Format: (frameWidth, frameHeight, frameCount, isVertical)
    /// </summary>
    private static readonly Dictionary<string, FieldEffectDimensions> SpriteDimensions = new()
    {
        // 16x16 sprites (2x2 tiles)
        { "arrow", new(16, 16, 8, false) },
        { "tall_grass", new(16, 16, 5, false) },
        { "ripple", new(16, 16, 5, false) },
        { "ash", new(16, 16, 5, false) },
        { "sand_footprints", new(16, 16, 2, false) },
        { "deep_sand_footprints", new(16, 16, 2, false) },
        { "bug_tracks", new(16, 16, 2, false) },
        { "spot_tracks", new(16, 16, 2, false) },
        { "bike_tire_tracks", new(16, 16, 4, false) },
        { "slither_tracks", new(16, 16, 4, false) },
        { "jump_big_splash", new(16, 16, 4, false) },
        { "long_grass", new(16, 16, 4, false) },
        { "jump_long_grass", new(16, 16, 7, false) },
        { "unused_grass_2", new(16, 16, 4, false) },
        { "unused_grass_3", new(16, 16, 4, false) },
        { "unused_sand", new(16, 16, 4, false) },
        { "water_surfacing", new(16, 16, 4, false) },
        { "sparkle", new(16, 16, 6, false) },
        { "short_grass", new(16, 16, 2, false) },
        { "ash_puff", new(16, 16, 5, false) },
        { "ash_launch", new(16, 16, 5, false) },
        { "small_sparkle", new(16, 16, 2, false) },
        { "cave_dust", new(16, 16, 4, false) },
        { "secret_power_cave", new(16, 16, 5, false) },
        { "secret_power_shrub", new(16, 16, 5, false) },
        { "unknown_17", new(16, 16, 8, false) },
        // 16x8 sprites
        { "ground_impact_dust", new(16, 8, 3, false) },
        { "jump_tall_grass", new(16, 8, 4, false) },
        { "splash", new(16, 8, 2, false) },
        { "jump_small_splash", new(16, 8, 3, false) },
        { "sand_pile", new(16, 8, 3, false) },
        { "field_move_streaks", new(16, 8, 8, false) },
        { "field_move_streaks_indoors", new(16, 8, 2, false) },
        { "record_mix_lights", new(16, 8, 6, false) },
        // 16x32 sprites
        { "tree_disguise", new(16, 32, 7, false) },
        { "mountain_disguise", new(16, 32, 7, false) },
        { "sand_disguise_placeholder", new(16, 32, 7, false) },
        { "bubbles", new(16, 32, 8, false) },
        // 32x32 sprites
        { "surf_blob", new(32, 32, 3, false) },
        { "rock_climb_blob", new(32, 32, 3, false) },
        { "rock_climb_dust", new(32, 32, 3, false) },
        { "bird", new(32, 32, 1, false) },
        // 16x16 single frames
        { "emote_x", new(16, 16, 1, false) },
        { "emotion_double_exclamation", new(16, 16, 1, false) },
        { "emotion_exclamation", new(16, 16, 1, false) },
        { "emotion_heart", new(16, 16, 1, false) },
        { "emotion_question", new(16, 16, 1, false) },
        { "hot_springs_water", new(16, 16, 1, false) },
        // 8x8 single frames
        { "shadow_small", new(8, 8, 1, false) },
        { "cut_grass", new(8, 8, 1, false) },
        { "pokeball_glow", new(8, 8, 1, false) },
        { "deoxys_rock_fragment_bottom_left", new(8, 8, 1, false) },
        { "deoxys_rock_fragment_bottom_right", new(8, 8, 1, false) },
        { "deoxys_rock_fragment_top_left", new(8, 8, 1, false) },
        { "deoxys_rock_fragment_top_right", new(8, 8, 1, false) },
        // Other single frames
        { "shadow_medium", new(16, 8, 1, false) },
        { "shadow_large", new(32, 8, 1, false) },
        { "shadow_extra_large", new(64, 32, 1, false) },
        // Special multi-frame sprites
        { "secret_power_tree", new(16, 16, 6, false) },
        { "hof_monitor_big", new(16, 16, 4, false) },
        { "hof_monitor_small", new(16, 16, 2, false) },
        // ORAS dowsing
        { "oras_dowsing_brendan", new(16, 32, 9, false) },
        { "oras_dowsing_may", new(16, 32, 9, false) },
        // Spotlight
        { "spotlight", new(48, 24, 5, true) },
    };

    /// <summary>
    /// Animation data from pokeemerald field_effect_objects.h
    /// Each entry contains named animations with frame indices (into spritesheet),
    /// durations (in game frames), and loop flag
    /// </summary>
    private static readonly Dictionary<string, AnimationData[]> AnimationDefinitions = new()
    {
        // tall_grass: sAnim_TallGrass - frames 1,2,3,4,0 at 10 game frames each, ENDS
        ["tall_grass"] = [new("default", [1, 2, 3, 4, 0], [10, 10, 10, 10, 10], false)],

        // ripple: sAnim_Ripple - complex timing, ENDS
        ["ripple"] = [new("default", [0, 1, 2, 3, 0, 1, 2, 4], [12, 9, 9, 9, 9, 9, 11, 11], false)],

        // ash: sAnim_Ash - ENDS
        ["ash"] = [new("default", [0, 1, 2, 3, 4], [12, 12, 8, 12, 12], false)],

        // surf_blob: 4 directional animations (south, north, west, east), single frames, JUMP(0)
        ["surf_blob"] =
        [
            new("south", [0], [1], true),
            new("north", [1], [1], true),
            new("west", [2], [1], true),
            new("east", [2], [1], true, true) // hFlip
        ],

        // arrow: 4 directional animations, 2 frames each at 32 game frames, JUMP(0)
        ["arrow"] =
        [
            new("south", [3, 7], [32, 32], true),
            new("north", [0, 4], [32, 32], true),
            new("west", [1, 5], [32, 32], true),
            new("east", [2, 6], [32, 32], true)
        ],

        // ground_impact_dust: sAnim_GroundImpactDust - ENDS
        ["ground_impact_dust"] = [new("default", [0, 1, 2], [8, 8, 8], false)],

        // jump_tall_grass: sAnim_JumpTallGrass - ENDS
        ["jump_tall_grass"] = [new("default", [0, 1, 2, 3], [8, 8, 8, 8], false)],

        // sand_footprints: 4 directional, single frames with flips - ENDS
        ["sand_footprints"] =
        [
            new("south", [0], [1], false, false, true), // vFlip
            new("north", [0], [1], false),
            new("west", [1], [1], false),
            new("east", [1], [1], false, true) // hFlip
        ],

        // deep_sand_footprints: same as sand_footprints
        ["deep_sand_footprints"] =
        [
            new("south", [0], [1], false, false, true),
            new("north", [0], [1], false),
            new("west", [1], [1], false),
            new("east", [1], [1], false, true)
        ],

        // bug_tracks and spot_tracks use same anim table as deep_sand_footprints
        ["bug_tracks"] =
        [
            new("south", [0], [1], false, false, true),
            new("north", [0], [1], false),
            new("west", [1], [1], false),
            new("east", [1], [1], false, true)
        ],
        ["spot_tracks"] =
        [
            new("south", [0], [1], false, false, true),
            new("north", [0], [1], false),
            new("west", [1], [1], false),
            new("east", [1], [1], false, true)
        ],

        // bike_tire_tracks: 9 directional/corner animations - ENDS
        ["bike_tire_tracks"] =
        [
            new("south", [2], [1], false),
            new("north", [2], [1], false),
            new("west", [1], [1], false),
            new("east", [1], [1], false),
            new("se_corner", [0], [1], false),
            new("sw_corner", [0], [1], false, true), // hFlip
            new("nw_corner", [3], [1], false, true), // hFlip
            new("ne_corner", [3], [1], false)
        ],

        // slither_tracks: uses same anims as bike_tire_tracks
        ["slither_tracks"] =
        [
            new("south", [2], [1], false),
            new("north", [2], [1], false),
            new("west", [1], [1], false),
            new("east", [1], [1], false),
            new("se_corner", [0], [1], false),
            new("sw_corner", [0], [1], false, true),
            new("nw_corner", [3], [1], false, true),
            new("ne_corner", [3], [1], false)
        ],

        // jump_big_splash: sAnim_JumpBigSplash - ENDS
        ["jump_big_splash"] = [new("default", [0, 1, 2, 3], [8, 8, 8, 8], false)],

        // splash: 2 animations - simple (ENDS) and continuous (JUMP)
        ["splash"] =
        [
            new("default", [0, 1], [4, 4], false),
            new("continuous", [0, 1, 0, 1, 0, 1, 0, 1], [4, 4, 6, 6, 8, 8, 6, 6], true)
        ],

        // jump_small_splash: sAnim_JumpSmallSplash - ENDS
        ["jump_small_splash"] = [new("default", [0, 1, 2], [4, 4, 4], false)],

        // long_grass: sAnim_LongGrass - ENDS
        ["long_grass"] = [new("default", [1, 2, 0, 3, 0, 3, 0], [3, 3, 4, 4, 4, 4, 4], false)],

        // jump_long_grass: sAnim_JumpLongGrass - frame 5 is index 6 in source but we use sequential - ENDS
        ["jump_long_grass"] = [new("default", [0, 1, 2, 3, 4, 5], [4, 4, 8, 8, 8, 8], false)],

        // unused_grass_2: sAnim_UnusedGrass2 - JUMP(0)
        ["unused_grass_2"] = [new("default", [0, 1, 2, 3, 2, 1], [4, 4, 4, 4, 4, 4], true)],

        // unused_sand: sAnim_UnusedSand - JUMP(0)
        ["unused_sand"] = [new("default", [0, 1, 2, 3], [4, 4, 4, 4], true)],

        // sand_pile: sAnim_SandPile - ENDS
        ["sand_pile"] = [new("default", [0, 1, 2], [4, 4, 4], false)],

        // water_surfacing: sAnim_WaterSurfacing - JUMP(0)
        ["water_surfacing"] = [new("default", [0, 1, 2, 3, 2, 1], [4, 4, 4, 4, 4, 4], true)],

        // cave_dust: uses sAnimTable_WaterSurfacing - JUMP(0)
        ["cave_dust"] = [new("default", [0, 1, 2, 3, 2, 1], [4, 4, 4, 4, 4, 4], true)],

        // sparkle: sAnim_Sparkle - complex with LOOP, simplified to main sequence - ENDS
        ["sparkle"] = [new("default", [0, 1, 2, 3, 4, 5], [8, 8, 8, 8, 8, 8], false)],

        // tree_disguise: 2 animations (idle + reveal) - both END
        ["tree_disguise"] =
        [
            new("idle", [0], [16], false),
            new("reveal", [0, 1, 2, 3, 4, 5, 6], [4, 4, 4, 4, 4, 4, 4], false)
        ],

        // mountain_disguise: same pattern as tree_disguise
        ["mountain_disguise"] =
        [
            new("idle", [0], [16], false),
            new("reveal", [0, 1, 2, 3, 4, 5, 6], [4, 4, 4, 4, 4, 4, 4], false)
        ],

        // sand_disguise_placeholder: uses tree_disguise anims
        ["sand_disguise_placeholder"] =
        [
            new("idle", [0], [16], false),
            new("reveal", [0, 1, 2, 3, 4, 5, 6], [4, 4, 4, 4, 4, 4, 4], false)
        ],

        // short_grass: sAnim_ShortGrass - ENDS
        ["short_grass"] = [new("default", [0, 1], [4, 4], false)],

        // hot_springs_water: sAnim_HotSpringsWater - ENDS
        ["hot_springs_water"] = [new("default", [0], [4], false)],

        // ash_puff: sAnim_AshPuff - ENDS
        ["ash_puff"] = [new("default", [0, 1, 2, 3, 4], [6, 6, 6, 6, 6], false)],

        // ash_launch: sAnim_AshLaunch - ENDS
        ["ash_launch"] = [new("default", [0, 1, 2, 3, 4], [6, 6, 6, 6, 6], false)],

        // bubbles: sAnim_Bubbles - ENDS
        ["bubbles"] = [new("default", [0, 1, 2, 3, 4, 5, 6, 7], [4, 4, 4, 6, 6, 4, 4, 4], false)],

        // small_sparkle: sAnim_SmallSparkle - ENDS
        ["small_sparkle"] = [new("default", [0, 1, 0], [3, 5, 5], false)],

        // rock_climb_blob: uses surf_blob anims
        ["rock_climb_blob"] =
        [
            new("south", [0], [1], true),
            new("north", [1], [1], true),
            new("west", [2], [1], true),
            new("east", [2], [1], true, true)
        ],

        // rock_climb_dust: sAnim_RockClimbDust - ENDS
        ["rock_climb_dust"] = [new("default", [0, 1, 2], [12, 12, 12], false)],
    };

    private record FieldEffectDimensions(int Width, int Height, int Frames, bool Vertical);

    private record AnimationData(
        string Name,
        int[] FrameIndices,
        int[] GameFrameDurations,
        bool Loop,
        bool HFlip = false,
        bool VFlip = false);

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
        // Check if this is a sprite sheet with animation
        if (SpriteDimensions.TryGetValue(name, out var dims) && dims.Frames > 1)
        {
            return ExtractSpriteSheet(sourcePath, name, dims);
        }
        else
        {
            ExtractSingleFrame(sourcePath, name);
            return 1;
        }
    }

    private int ExtractSpriteSheet(string sourcePath, string name, FieldEffectDimensions dims)
    {
        var id = NormalizeId(name);
        var pascalName = ToPascalCase(name);

        // Load the sprite sheet with transparency and save as single image
        using var spriteSheet = LoadWithIndex0Transparency(sourcePath);
        var graphicPath = GetGraphicsPath("FieldEffects", $"{pascalName}.png");
        spriteSheet.Save(graphicPath, PngEncoder);

        // Build frames array with positions
        var frames = new List<object>();
        for (int i = 0; i < dims.Frames; i++)
        {
            int x, y;
            if (dims.Vertical)
            {
                x = 0;
                y = i * dims.Height;
            }
            else
            {
                x = i * dims.Width;
                y = 0;
            }

            // Bounds check
            if (x + dims.Width > spriteSheet.Width || y + dims.Height > spriteSheet.Height)
            {
                LogWarning($"Frame {i} out of bounds for {name}");
                break;
            }

            frames.Add(new
            {
                index = i,
                x,
                y,
                width = dims.Width,
                height = dims.Height
            });
        }

        // Build animations array from parsed animation data
        var animations = new List<object>();
        if (AnimationDefinitions.TryGetValue(name, out var animDefs))
        {
            foreach (var animDef in animDefs)
            {
                // Convert game frames to seconds
                var frameDurations = animDef.GameFrameDurations
                    .Select(gf => gf * GameFrameToSeconds)
                    .ToList();

                var animObj = new Dictionary<string, object>
                {
                    ["name"] = animDef.Name,
                    ["loop"] = animDef.Loop,
                    ["frameIndices"] = animDef.FrameIndices.ToList(),
                    ["frameDurations"] = frameDurations
                };

                if (animDef.HFlip)
                    animObj["flipHorizontal"] = true;
                if (animDef.VFlip)
                    animObj["flipVertical"] = true;

                animations.Add(animObj);
            }
        }

        // Create definition with spritesheet format
        var definition = new
        {
            id = $"{IdTransformer.Namespace}:field_effect:{id}",
            name = FormatDisplayName(name),
            type = "Sprite",
            texturePath = $"Graphics/FieldEffects/{pascalName}.png",
            frameWidth = dims.Width,
            frameHeight = dims.Height,
            frameCount = frames.Count,
            frames,
            animations = animations.Count > 0 ? animations : null
        };

        var jsonPath = GetDefinitionPath("FieldEffects", $"{pascalName}.json");
        File.WriteAllText(jsonPath, JsonSerializer.Serialize(definition, JsonOptions.Default));

        LogVerbose($"Extracted {name} ({frames.Count} frames, {dims.Width}x{dims.Height}, {animations.Count} animations)");
        return frames.Count;
    }

    private void ExtractSingleFrame(string sourcePath, string name)
    {
        var id = NormalizeId(name);
        var pascalName = ToPascalCase(name);

        // Load with proper GBA transparency and save
        var graphicPath = GetGraphicsPath("FieldEffects", $"{pascalName}.png");
        using var img = LoadWithIndex0Transparency(sourcePath);
        img.Save(graphicPath, PngEncoder);

        // Create definition with consistent format
        var definition = new
        {
            id = $"{IdTransformer.Namespace}:field_effect:{id}",
            name = FormatDisplayName(name),
            type = "Sprite",
            texturePath = $"Graphics/FieldEffects/{pascalName}.png",
            frameWidth = img.Width,
            frameHeight = img.Height,
            frameCount = 1,
            frames = new[]
            {
                new { index = 0, x = 0, y = 0, width = img.Width, height = img.Height }
            }
        };

        var jsonPath = GetDefinitionPath("FieldEffects", $"{pascalName}.json");
        File.WriteAllText(jsonPath, JsonSerializer.Serialize(definition, JsonOptions.Default));

        LogVerbose($"Extracted {name} (single frame, {img.Width}x{img.Height})");
    }

    private void ExtractPreSeparatedFrames(string sourceDir, string name, List<string> sourceFrames)
    {
        var id = NormalizeId(name);
        var pascalName = ToPascalCase(name);

        // Load first frame to get dimensions
        using var firstImg = LoadWithIndex0Transparency(sourceFrames[0]);
        var frameWidth = firstImg.Width;
        var frameHeight = firstImg.Height;

        // Create combined spritesheet (horizontal layout)
        var sheetWidth = frameWidth * sourceFrames.Count;
        using var spriteSheet = new Image<Rgba32>(sheetWidth, frameHeight);

        var frames = new List<object>();
        for (int i = 0; i < sourceFrames.Count; i++)
        {
            using var frameImg = LoadWithIndex0Transparency(sourceFrames[i]);

            // Copy frame to spritesheet
            var x = i * frameWidth;
            spriteSheet.Mutate(ctx => ctx.DrawImage(frameImg, new Point(x, 0), 1f));

            frames.Add(new
            {
                index = i,
                x,
                y = 0,
                width = frameWidth,
                height = frameHeight
            });
        }

        // Save combined spritesheet
        var graphicPath = GetGraphicsPath("FieldEffects", $"{pascalName}.png");
        spriteSheet.Save(graphicPath, PngEncoder);

        // Build animations array from parsed animation data
        var animations = new List<object>();
        if (AnimationDefinitions.TryGetValue(name, out var animDefs))
        {
            foreach (var animDef in animDefs)
            {
                // Convert game frames to seconds
                var frameDurations = animDef.GameFrameDurations
                    .Select(gf => gf * GameFrameToSeconds)
                    .ToList();

                var animObj = new Dictionary<string, object>
                {
                    ["name"] = animDef.Name,
                    ["loop"] = animDef.Loop,
                    ["frameIndices"] = animDef.FrameIndices.ToList(),
                    ["frameDurations"] = frameDurations
                };

                if (animDef.HFlip)
                    animObj["flipHorizontal"] = true;
                if (animDef.VFlip)
                    animObj["flipVertical"] = true;

                animations.Add(animObj);
            }
        }
        else if (sourceFrames.Count > 1)
        {
            // Default animation for pre-separated frames without explicit animation data
            // Use 4 game frames per frame as a reasonable default
            var frameIndices = Enumerable.Range(0, sourceFrames.Count).ToList();
            var frameDurations = Enumerable.Repeat(4 * GameFrameToSeconds, sourceFrames.Count).ToList();

            animations.Add(new Dictionary<string, object>
            {
                ["name"] = "default",
                ["loop"] = false,
                ["frameIndices"] = frameIndices,
                ["frameDurations"] = frameDurations
            });
        }

        // Create definition
        var definition = new
        {
            id = $"{IdTransformer.Namespace}:field_effect:{id}",
            name = FormatDisplayName(name),
            type = "Sprite",
            texturePath = $"Graphics/FieldEffects/{pascalName}.png",
            frameWidth,
            frameHeight,
            frameCount = frames.Count,
            frames,
            animations = animations.Count > 0 ? animations : null
        };

        var jsonPath = GetDefinitionPath("FieldEffects", $"{pascalName}.json");
        File.WriteAllText(jsonPath, JsonSerializer.Serialize(definition, JsonOptions.Default));

        LogVerbose($"Extracted {name} ({sourceFrames.Count} pre-separated frames, combined to spritesheet, {animations.Count} animations)");
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
}
