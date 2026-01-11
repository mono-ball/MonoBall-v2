using System.Text.Json;
using System.Text.RegularExpressions;
using Porycon3.Infrastructure;
using Porycon3.Services.Extraction;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using static Porycon3.Infrastructure.StringUtilities;

namespace Porycon3.Services;

/// <summary>
/// Extracts Pokemon sprites and animations from pokeemerald-expansion.
/// Handles front/back sprites, icons, overworld sprites, and animation data.
/// Applies JASC-PAL palettes and proper transparency.
/// </summary>
public class PokemonExtractor : ExtractorBase
{
    public override string Name => "Pokemon Sprites";
    public override string Description => "Extracts Pokemon sprites, icons, and animation data";

    private readonly string _pokemonGraphics;
    private readonly string _speciesInfoPath;
    private readonly string _outputGraphics;
    private readonly string _outputData;

    // Standard sprite sizes
    private const int BattleSpriteSize = 64;
    private const int IconWidth = 32;
    private const int IconHeight = 32;
    private const int OverworldFrameSize = 32;

    // Profile ID constants - must match SpriteExtractor for consistency
    private const string MovementProfileNpc = "pokeemerald:profile:movement/npc";
    private const string AnimationProfileStandard = "pokeemerald:profile:animation/standard";

    public PokemonExtractor(string inputPath, string outputPath, bool verbose = false)
        : base(inputPath, outputPath, verbose)
    {
        _pokemonGraphics = Path.Combine(inputPath, "graphics", "pokemon");
        _speciesInfoPath = Path.Combine(inputPath, "src", "data", "pokemon", "species_info");
        _outputGraphics = Path.Combine(outputPath, "Graphics", "Pokemon");
        _outputData = Path.Combine(outputPath, "Definitions", "Assets", "Pokemon");
    }

    protected override int ExecuteExtraction()
    {
        if (!Directory.Exists(_pokemonGraphics))
        {
            LogWarning($"Pokemon graphics not found: {_pokemonGraphics}");
            return 0;
        }

        EnsureDirectory(_outputGraphics);
        EnsureDirectory(_outputData);

        // Parse animation data from species_info headers
        Dictionary<string, PokemonAnimationInfo> animationData = new();
        WithStatus("Parsing animation data...", _ =>
        {
            animationData = ParseAnimationData();
        });

        // Thread-safe counters
        int pokemonCount = 0;
        int spriteCount = 0;
        int formCount = 0;

        // Get all Pokemon directories
        var pokemonDirs = Directory.GetDirectories(_pokemonGraphics).ToList();

        // Process Pokemon in parallel for better performance
        WithParallelProgress("Extracting Pokemon sprites", pokemonDirs, pokemonDir =>
        {
            var pokemonName = Path.GetFileName(pokemonDir);

            try
            {
                var (sprites, forms) = ExtractPokemon(pokemonDir, pokemonName, animationData);
                if (sprites > 0)
                {
                    Interlocked.Increment(ref pokemonCount);
                    Interlocked.Add(ref spriteCount, sprites);
                    Interlocked.Add(ref formCount, forms);
                }
            }
            catch (Exception ex)
            {
                AddError(pokemonName, ex.Message, ex);
                LogVerbose($"Error extracting {pokemonName}: {ex.Message}");
            }
        });

        SetCount("Sprites", spriteCount);
        SetCount("Forms", formCount);
        return pokemonCount;
    }

    /// <summary>
    /// Extract all sprites for a single Pokemon, including forms.
    /// </summary>
    private (int Sprites, int Forms) ExtractPokemon(string pokemonDir, string pokemonName, Dictionary<string, PokemonAnimationInfo> animationData)
    {
        var pascalName = PokemonToPascalCase(pokemonName);
        var pokemonOutputGraphics = Path.Combine(_outputGraphics, pascalName);
        var pokemonOutputData = Path.Combine(_outputData, pascalName);

        Directory.CreateDirectory(pokemonOutputGraphics);
        Directory.CreateDirectory(pokemonOutputData);

        int spriteCount = 0;
        int formCount = 0;

        // Load palettes
        var normalPalette = LoadJascPalette(Path.Combine(pokemonDir, "normal.pal"));
        var shinyPalette = LoadJascPalette(Path.Combine(pokemonDir, "shiny.pal"));
        var overworldNormalPal = LoadJascPalette(Path.Combine(pokemonDir, "overworld_normal.pal"));
        var overworldShinyPal = LoadJascPalette(Path.Combine(pokemonDir, "overworld_shiny.pal"));

        // Fallback to GBA versions if modern versions don't exist
        normalPalette ??= LoadJascPalette(Path.Combine(pokemonDir, "normal_gba.pal"));
        shinyPalette ??= LoadJascPalette(Path.Combine(pokemonDir, "shiny_gba.pal"));

        // Get animation info
        var animInfo = animationData.GetValueOrDefault(pokemonName.ToUpperInvariant());

        // Extract front sprite (normal and shiny)
        var frontPath = Path.Combine(pokemonDir, "anim_front.png");
        if (!File.Exists(frontPath))
            frontPath = Path.Combine(pokemonDir, "anim_front_gba.png");
        if (!File.Exists(frontPath))
            frontPath = Path.Combine(pokemonDir, "front.png");

        if (File.Exists(frontPath) && normalPalette != null)
        {
            if (ExtractBattleSprite(frontPath, normalPalette, pokemonOutputGraphics, $"{pascalName}Front", animInfo?.FrontFrames ?? 2))
                spriteCount++;

            if (shinyPalette != null && ExtractBattleSprite(frontPath, shinyPalette, pokemonOutputGraphics, $"{pascalName}FrontShiny", animInfo?.FrontFrames ?? 2))
                spriteCount++;
        }

        // Extract back sprite (normal and shiny)
        var backPath = Path.Combine(pokemonDir, "back.png");
        if (!File.Exists(backPath))
            backPath = Path.Combine(pokemonDir, "back_gba.png");

        if (File.Exists(backPath) && normalPalette != null)
        {
            if (ExtractBattleSprite(backPath, normalPalette, pokemonOutputGraphics, $"{pascalName}Back", 1))
                spriteCount++;

            if (shinyPalette != null && ExtractBattleSprite(backPath, shinyPalette, pokemonOutputGraphics, $"{pascalName}BackShiny", 1))
                spriteCount++;
        }

        // Extract party icon
        var iconPath = Path.Combine(pokemonDir, "icon.png");
        if (!File.Exists(iconPath))
            iconPath = Path.Combine(pokemonDir, "icon_gba.png");

        if (File.Exists(iconPath))
        {
            if (ExtractIcon(iconPath, pokemonOutputGraphics, $"{pascalName}Icon"))
                spriteCount++;
        }

        // Extract overworld sprite
        var overworldPath = Path.Combine(pokemonDir, "overworld.png");
        if (File.Exists(overworldPath) && overworldNormalPal != null)
        {
            if (ExtractOverworldSprite(overworldPath, overworldNormalPal, pokemonOutputGraphics, $"{pascalName}Overworld"))
                spriteCount++;

            if (overworldShinyPal != null && ExtractOverworldSprite(overworldPath, overworldShinyPal, pokemonOutputGraphics, $"{pascalName}OverworldShiny"))
                spriteCount++;
        }

        // Extract female variants if they exist
        var frontFPath = Path.Combine(pokemonDir, "anim_frontf.png");
        if (!File.Exists(frontFPath))
            frontFPath = Path.Combine(pokemonDir, "anim_frontf_gba.png");
        if (!File.Exists(frontFPath))
            frontFPath = Path.Combine(pokemonDir, "frontf.png");

        if (File.Exists(frontFPath) && normalPalette != null)
        {
            if (ExtractBattleSprite(frontFPath, normalPalette, pokemonOutputGraphics, $"{pascalName}FrontFemale", animInfo?.FrontFrames ?? 2))
            {
                spriteCount++;
                formCount++;
            }

            if (shinyPalette != null && ExtractBattleSprite(frontFPath, shinyPalette, pokemonOutputGraphics, $"{pascalName}FrontFemaleShiny", animInfo?.FrontFrames ?? 2))
                spriteCount++;
        }

        // Extract female back sprite if it exists
        var backFPath = Path.Combine(pokemonDir, "backf.png");
        if (!File.Exists(backFPath))
            backFPath = Path.Combine(pokemonDir, "backf_gba.png");

        if (File.Exists(backFPath) && normalPalette != null)
        {
            if (ExtractBattleSprite(backFPath, normalPalette, pokemonOutputGraphics, $"{pascalName}BackFemale", 1))
                spriteCount++;

            if (shinyPalette != null && ExtractBattleSprite(backFPath, shinyPalette, pokemonOutputGraphics, $"{pascalName}BackFemaleShiny", 1))
                spriteCount++;
        }

        // Extract forms (subdirectories)
        var formDirs = Directory.GetDirectories(pokemonDir);
        foreach (var formDir in formDirs)
        {
            var formName = Path.GetFileName(formDir);
            var formSprites = ExtractPokemonForm(formDir, pokemonName, formName, pokemonOutputGraphics, animationData);
            spriteCount += formSprites;
            if (formSprites > 0) formCount++;
        }

        // Generate individual sprite definitions (one file per sprite)
        if (spriteCount > 0)
        {
            GenerateSpriteDefinitions(pokemonName, pokemonOutputData, pokemonOutputGraphics, animInfo);
        }

        return (spriteCount, formCount);
    }

    /// <summary>
    /// Extract sprites for a Pokemon form (regional, mega, etc.)
    /// </summary>
    private int ExtractPokemonForm(string formDir, string pokemonName, string formName, string outputDir, Dictionary<string, PokemonAnimationInfo> animationData)
    {
        var normalPalette = LoadJascPalette(Path.Combine(formDir, "normal.pal"));
        var shinyPalette = LoadJascPalette(Path.Combine(formDir, "shiny.pal"));

        normalPalette ??= LoadJascPalette(Path.Combine(formDir, "normal_gba.pal"));
        shinyPalette ??= LoadJascPalette(Path.Combine(formDir, "shiny_gba.pal"));

        if (normalPalette == null)
        {
            // Try to use parent palette
            var parentDir = Path.GetDirectoryName(formDir)!;
            normalPalette = LoadJascPalette(Path.Combine(parentDir, "normal.pal"));
            shinyPalette = LoadJascPalette(Path.Combine(parentDir, "shiny.pal"));
        }

        if (normalPalette == null)
            return 0;

        var pascalPokemon = PokemonToPascalCase(pokemonName);
        var pascalForm = PokemonToPascalCase(formName);
        var prefix = $"{pascalPokemon}{pascalForm}";

        int spriteCount = 0;

        // Front sprite
        var frontPath = Path.Combine(formDir, "anim_front.png");
        if (!File.Exists(frontPath))
            frontPath = Path.Combine(formDir, "anim_front_gba.png");
        if (!File.Exists(frontPath))
            frontPath = Path.Combine(formDir, "front.png");

        if (File.Exists(frontPath))
        {
            if (ExtractBattleSprite(frontPath, normalPalette, outputDir, $"{prefix}Front", 2))
                spriteCount++;
            if (shinyPalette != null && ExtractBattleSprite(frontPath, shinyPalette, outputDir, $"{prefix}FrontShiny", 2))
                spriteCount++;
        }

        // Back sprite
        var backPath = Path.Combine(formDir, "back.png");
        if (!File.Exists(backPath))
            backPath = Path.Combine(formDir, "back_gba.png");

        if (File.Exists(backPath))
        {
            if (ExtractBattleSprite(backPath, normalPalette, outputDir, $"{prefix}Back", 1))
                spriteCount++;
            if (shinyPalette != null && ExtractBattleSprite(backPath, shinyPalette, outputDir, $"{prefix}BackShiny", 1))
                spriteCount++;
        }

        // Icon
        var iconPath = Path.Combine(formDir, "icon.png");
        if (!File.Exists(iconPath))
            iconPath = Path.Combine(formDir, "icon_gba.png");

        if (File.Exists(iconPath))
        {
            if (ExtractIcon(iconPath, outputDir, $"{prefix}Icon"))
                spriteCount++;
        }

        // Overworld
        var overworldPath = Path.Combine(formDir, "overworld.png");
        var formParentDir = Path.GetDirectoryName(formDir)!;
        var overworldNormalPal = LoadJascPalette(Path.Combine(formDir, "overworld_normal.pal"));
        var overworldShinyPal = LoadJascPalette(Path.Combine(formDir, "overworld_shiny.pal"));

        // Fallback to parent directory palettes
        overworldNormalPal ??= LoadJascPalette(Path.Combine(formParentDir, "overworld_normal.pal"));
        overworldShinyPal ??= LoadJascPalette(Path.Combine(formParentDir, "overworld_shiny.pal"));

        if (File.Exists(overworldPath) && overworldNormalPal != null)
        {
            if (ExtractOverworldSprite(overworldPath, overworldNormalPal, outputDir, $"{prefix}Overworld"))
                spriteCount++;

            if (overworldShinyPal != null && ExtractOverworldSprite(overworldPath, overworldShinyPal, outputDir, $"{prefix}OverworldShiny"))
                spriteCount++;
        }

        return spriteCount;
    }

    /// <summary>
    /// Extract a battle sprite (front or back) with palette.
    /// </summary>
    private bool ExtractBattleSprite(string pngPath, Rgba32[] palette, string outputDir, string name, int expectedFrames)
    {
        try
        {
            var bytes = File.ReadAllBytes(pngPath);
            var (indices, width, height, _) = IndexedPngLoader.ExtractPixelIndices(bytes);

            if (indices == null || width == 0 || height == 0)
            {
                LogVerbose($"Failed to extract indices from {pngPath}");
                return false;
            }

            // Build RGBA image with palette and transparency
            using var output = new Image<Rgba32>(width, height);
            output.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < height; y++)
                {
                    var row = accessor.GetRowSpan(y);
                    for (int x = 0; x < width; x++)
                    {
                        var idx = indices[y * width + x];
                        if (idx == 0)
                        {
                            row[x] = new Rgba32(0, 0, 0, 0);
                        }
                        else if (idx < palette.Length)
                        {
                            row[x] = palette[idx];
                        }
                    }
                }
            });

            var outputPath = Path.Combine(outputDir, $"{name}.png");
            IndexedPngLoader.SaveAsRgbaPng(output, outputPath);

            return true;
        }
        catch (Exception ex)
        {
            LogVerbose($"Error extracting battle sprite {name}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Extract party icon sprite.
    /// </summary>
    private bool ExtractIcon(string pngPath, string outputDir, string name)
    {
        try
        {
            // Icons use their own embedded palette, just need transparency
            var bytes = File.ReadAllBytes(pngPath);
            var palette = IndexedPngLoader.ExtractPalette(bytes);
            var (indices, width, height, _) = IndexedPngLoader.ExtractPixelIndices(bytes);

            if (indices == null || palette == null || width == 0 || height == 0)
            {
                // Fall back to loading as-is with first pixel transparency
                using var img = Image.Load<Rgba32>(pngPath);
                var firstPixel = img[0, 0];
                img.ProcessPixelRows(accessor =>
                {
                    for (int y = 0; y < accessor.Height; y++)
                    {
                        var row = accessor.GetRowSpan(y);
                        for (int x = 0; x < row.Length; x++)
                        {
                            if (row[x].R == firstPixel.R && row[x].G == firstPixel.G && row[x].B == firstPixel.B)
                                row[x] = new Rgba32(0, 0, 0, 0);
                        }
                    }
                });
                var outputPath = Path.Combine(outputDir, $"{name}.png");
                IndexedPngLoader.SaveAsRgbaPng(img, outputPath);
                return true;
            }

            using var output = new Image<Rgba32>(width, height);
            output.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < height; y++)
                {
                    var row = accessor.GetRowSpan(y);
                    for (int x = 0; x < width; x++)
                    {
                        var idx = indices[y * width + x];
                        if (idx == 0)
                        {
                            row[x] = new Rgba32(0, 0, 0, 0);
                        }
                        else if (idx < palette.Length)
                        {
                            row[x] = palette[idx];
                        }
                    }
                }
            });

            var outPath = Path.Combine(outputDir, $"{name}.png");
            IndexedPngLoader.SaveAsRgbaPng(output, outPath);
            return true;
        }
        catch (Exception ex)
        {
            LogVerbose($"Error extracting icon {name}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Extract overworld following sprite.
    /// </summary>
    private bool ExtractOverworldSprite(string pngPath, Rgba32[] palette, string outputDir, string name)
    {
        try
        {
            var bytes = File.ReadAllBytes(pngPath);
            var (indices, width, height, _) = IndexedPngLoader.ExtractPixelIndices(bytes);

            if (indices == null || width == 0 || height == 0)
                return false;

            using var output = new Image<Rgba32>(width, height);
            output.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < height; y++)
                {
                    var row = accessor.GetRowSpan(y);
                    for (int x = 0; x < width; x++)
                    {
                        var idx = indices[y * width + x];
                        if (idx == 0)
                        {
                            row[x] = new Rgba32(0, 0, 0, 0);
                        }
                        else if (idx < palette.Length)
                        {
                            row[x] = palette[idx];
                        }
                    }
                }
            });

            var outputPath = Path.Combine(outputDir, $"{name}.png");
            IndexedPngLoader.SaveAsRgbaPng(output, outputPath);
            return true;
        }
        catch (Exception ex)
        {
            LogVerbose($"Error extracting overworld sprite {name}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Generate individual sprite definition JSON files for a Pokemon.
    /// One file per sprite, not a combined manifest.
    /// Uses the same format as SpriteExtractor with profiles and capabilities.
    /// </summary>
    private void GenerateSpriteDefinitions(string pokemonName, string outputDataDir, string outputGraphicsDir, PokemonAnimationInfo? animInfo)
    {
        var pascalName = PokemonToPascalCase(pokemonName);
        var normalizedName = pokemonName.ToLowerInvariant();

        var graphicsFiles = Directory.GetFiles(outputGraphicsDir, "*.png");

        foreach (var file in graphicsFiles)
        {
            var spriteName = Path.GetFileNameWithoutExtension(file);
            var spriteType = DetermineSpriteType(spriteName);
            var (frameWidth, frameHeight, frameCount) = DetermineFrameInfo(file, spriteType);

            // Generate animations with proper format (animationType, frameSequence)
            var animResult = GenerateAnimationsWithProfile(spriteName, spriteType, frameCount, frameWidth, animInfo);

            // Generate consistent underscore ID: ho_oh/ho_oh_front instead of ho_oh/hoohfront
            var spriteIdName = GenerateSpriteIdName(normalizedName, spriteName, pascalName);

            // Build frames array - required for animation frame lookup
            var frames = new List<object>();
            for (int i = 0; i < frameCount; i++)
            {
                frames.Add(new
                {
                    index = i,
                    x = spriteType == "overworld" ? i * frameWidth : 0, // Overworld is horizontal strip
                    y = spriteType == "overworld" ? 0 : i * frameHeight, // Other sprites are vertical
                    width = frameWidth,
                    height = frameHeight
                });
            }

            // Determine if movement profile is needed (only for sprites with movement animations)
            string? movementProfileId = animResult.Capabilities.MovementAnimated ? MovementProfileNpc : null;

            var spriteDefinition = new
            {
                id = $"{IdTransformer.Namespace}:pokemon:sprite/{normalizedName}/{spriteIdName}",
                name = FormatDisplayName(spriteName),
                type = "Sprite",
                texturePath = $"Graphics/Pokemon/{pascalName}/{spriteName}.png",
                spriteType,
                frameWidth,
                frameHeight,
                frameCount,
                movementProfileId,
                animationProfileId = AnimationProfileStandard,
                defaultAnimation = animResult.DefaultAnimation,
                capabilities = new
                {
                    directional = animResult.Capabilities.Directional,
                    movementAnimated = animResult.Capabilities.MovementAnimated
                },
                frames,
                animations = animResult.Animations.Count > 0 ? animResult.Animations : null
            };

            var defPath = Path.Combine(outputDataDir, $"{spriteName}.json");
            File.WriteAllText(defPath, JsonSerializer.Serialize(spriteDefinition, JsonOptions.Default));
        }
    }

    /// <summary>
    /// Generate sprite ID name with consistent underscores.
    /// E.g., "HoOhFrontShiny" with pokemon "ho_oh" becomes "ho_oh_front_shiny"
    /// </summary>
    private static string GenerateSpriteIdName(string normalizedPokemonName, string spriteName, string pascalPokemonName)
    {
        // Remove the pokemon name prefix from sprite name to get suffix
        // "HoOhFrontShiny" -> "FrontShiny"
        var suffix = spriteName;
        if (spriteName.StartsWith(pascalPokemonName, StringComparison.OrdinalIgnoreCase))
            suffix = spriteName.Substring(pascalPokemonName.Length);

        // Convert PascalCase suffix to snake_case: "FrontShiny" -> "front_shiny"
        var snakeSuffix = PascalToSnakeCase(suffix);

        // Combine: "ho_oh" + "_" + "front_shiny" = "ho_oh_front_shiny"
        return string.IsNullOrEmpty(snakeSuffix)
            ? normalizedPokemonName
            : $"{normalizedPokemonName}_{snakeSuffix}";
    }

    /// <summary>
    /// Convert PascalCase to snake_case.
    /// E.g., "FrontShiny" -> "front_shiny", "Overworld" -> "overworld"
    /// </summary>
    private static string PascalToSnakeCase(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;

        var result = new System.Text.StringBuilder();
        for (int i = 0; i < input.Length; i++)
        {
            var c = input[i];
            if (char.IsUpper(c))
            {
                if (i > 0) result.Append('_');
                result.Append(char.ToLower(c));
            }
            else
            {
                result.Append(c);
            }
        }
        return result.ToString();
    }

    /// <summary>
    /// Parse animation data from species_info headers.
    /// </summary>
    private Dictionary<string, PokemonAnimationInfo> ParseAnimationData()
    {
        var result = new Dictionary<string, PokemonAnimationInfo>(StringComparer.OrdinalIgnoreCase);

        if (!Directory.Exists(_speciesInfoPath))
            return result;

        var headerFiles = Directory.GetFiles(_speciesInfoPath, "*.h");
        var animFrameRegex = new Regex(@"\.frontAnimFrames\s*=\s*ANIM_FRAMES\s*\(\s*((?:ANIMCMD_FRAME\s*\([^)]+\)\s*,?\s*)+)\s*\)", RegexOptions.Singleline);
        var speciesRegex = new Regex(@"\[SPECIES_(\w+)\]\s*=\s*\{([^}]+)\}", RegexOptions.Singleline);
        var animCmdRegex = new Regex(@"ANIMCMD_FRAME\s*\(\s*(\d+)\s*,\s*(\d+)\s*\)");

        foreach (var headerFile in headerFiles)
        {
            try
            {
                var content = File.ReadAllText(headerFile);
                var speciesMatches = speciesRegex.Matches(content);

                foreach (Match speciesMatch in speciesMatches)
                {
                    var speciesName = speciesMatch.Groups[1].Value;
                    var speciesBody = speciesMatch.Groups[2].Value;

                    var animMatch = animFrameRegex.Match(speciesBody);
                    if (animMatch.Success)
                    {
                        var frames = new List<(int Frame, int Duration)>();
                        var cmdMatches = animCmdRegex.Matches(animMatch.Groups[1].Value);

                        foreach (Match cmdMatch in cmdMatches)
                        {
                            var frame = int.Parse(cmdMatch.Groups[1].Value);
                            var duration = int.Parse(cmdMatch.Groups[2].Value);
                            frames.Add((frame, duration));
                        }

                        if (frames.Count > 0)
                        {
                            var maxFrame = frames.Max(f => f.Frame) + 1;
                            result[speciesName] = new PokemonAnimationInfo
                            {
                                FrontFrames = maxFrame,
                                AnimationCommands = frames
                            };
                        }
                    }
                }
            }
            catch
            {
                // Skip files that can't be parsed
            }
        }

        return result;
    }

    /// <summary>
    /// Load JASC-PAL palette file.
    /// </summary>
    private static Rgba32[]? LoadJascPalette(string path)
    {
        if (!File.Exists(path))
            return null;

        try
        {
            var lines = File.ReadAllLines(path);
            if (lines.Length < 3 || lines[0].Trim() != "JASC-PAL")
                return null;

            if (!int.TryParse(lines[2].Trim(), out var numColors))
                return null;

            var palette = new Rgba32[numColors];
            for (int i = 0; i < numColors && i + 3 < lines.Length; i++)
            {
                var parts = lines[i + 3].Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 3 &&
                    int.TryParse(parts[0], out var r) &&
                    int.TryParse(parts[1], out var g) &&
                    int.TryParse(parts[2], out var b))
                {
                    // Index 0 is transparent in GBA
                    palette[i] = i == 0
                        ? new Rgba32(0, 0, 0, 0)
                        : new Rgba32((byte)r, (byte)g, (byte)b, 255);
                }
            }

            return palette;
        }
        catch
        {
            return null;
        }
    }

    private static string DetermineSpriteType(string spriteName)
    {
        var lower = spriteName.ToLowerInvariant();
        if (lower.Contains("front")) return "front";
        if (lower.Contains("back")) return "back";
        if (lower.Contains("icon")) return "icon";
        if (lower.Contains("overworld")) return "overworld";
        return "unknown";
    }

    private static (int Width, int Height, int Count) DetermineFrameInfo(string pngPath, string spriteType)
    {
        using var img = Image.Load(pngPath);

        return spriteType switch
        {
            "front" => (BattleSpriteSize, BattleSpriteSize, img.Height / BattleSpriteSize),
            "back" => (BattleSpriteSize, BattleSpriteSize, 1),
            "icon" => (IconWidth, IconHeight, img.Height / IconHeight),
            "overworld" => (OverworldFrameSize, OverworldFrameSize, img.Width / OverworldFrameSize),
            _ => (img.Width, img.Height, 1)
        };
    }

    /// <summary>
    /// Generate animations with proper profile-compatible format.
    /// Uses animationType and frameSequence instead of legacy frameDurations.
    /// Returns capabilities and defaultAnimation for sprite definition.
    /// </summary>
    private static PokemonAnimationResult GenerateAnimationsWithProfile(string spriteName, string spriteType, int frameCount, int frameWidth, PokemonAnimationInfo? animInfo)
    {
        var result = new PokemonAnimationResult();

        if (spriteType == "front" && animInfo?.AnimationCommands != null)
        {
            var frames = animInfo.AnimationCommands.Select(c => c.Frame).ToList();
            var durations = animInfo.AnimationCommands.Select(c => c.Duration / 60.0).ToArray();

            result.Animations.Add(new
            {
                name = "idle",
                animationType = "go", // Battle animations use "go" type
                loop = true,
                frameIndices = frames,
                frameSequence = durations,
                flipHorizontal = false
            });

            result.DefaultAnimation = "idle";
            result.Capabilities = new PokemonSpriteCapabilities { Directional = false, MovementAnimated = false };
        }
        else if (spriteType == "icon" && frameCount >= 2)
        {
            result.Animations.Add(new
            {
                name = "bounce",
                animationType = "go",
                loop = true,
                frameIndices = new[] { 0, 1 },
                frameSequence = new[] { 0.5, 0.5 },
                flipHorizontal = false
            });

            result.DefaultAnimation = "bounce";
            result.Capabilities = new PokemonSpriteCapabilities { Directional = false, MovementAnimated = false };
        }
        else if (spriteType == "overworld" && frameCount >= 4)
        {
            // Standard Pokemon overworld: 6 frames (down, down-walk, up, up-walk, side, side-walk)
            // Maps to standard directional animations used by NPCs

            // Face animations use "face" animation type
            result.Animations.Add(new { name = "face_south", animationType = "face", loop = true, frameIndices = new[] { 0 }, flipHorizontal = false });
            result.Animations.Add(new { name = "face_north", animationType = "face", loop = true, frameIndices = new[] { 2 }, flipHorizontal = false });

            // Walk animations use "go" animation type with 4-frame cycle
            result.Animations.Add(new { name = "go_south", animationType = "go", loop = true, frameIndices = new[] { 1, 0 }, flipHorizontal = false });
            result.Animations.Add(new { name = "go_north", animationType = "go", loop = true, frameIndices = new[] { 3, 2 }, flipHorizontal = false });

            if (frameCount >= 6)
            {
                // Side animations - west is normal, east is flipped
                result.Animations.Add(new { name = "face_west", animationType = "face", loop = true, frameIndices = new[] { 4 }, flipHorizontal = false });
                result.Animations.Add(new { name = "face_east", animationType = "face", loop = true, frameIndices = new[] { 4 }, flipHorizontal = true });
                result.Animations.Add(new { name = "go_west", animationType = "go", loop = true, frameIndices = new[] { 5, 4 }, flipHorizontal = false });
                result.Animations.Add(new { name = "go_east", animationType = "go", loop = true, frameIndices = new[] { 5, 4 }, flipHorizontal = true });
            }
            else
            {
                // Fallback for 4-frame sprites - use south frame for side
                result.Animations.Add(new { name = "face_west", animationType = "face", loop = true, frameIndices = new[] { 0 }, flipHorizontal = false });
                result.Animations.Add(new { name = "face_east", animationType = "face", loop = true, frameIndices = new[] { 0 }, flipHorizontal = true });
                result.Animations.Add(new { name = "go_west", animationType = "go", loop = true, frameIndices = new[] { 1, 0 }, flipHorizontal = false });
                result.Animations.Add(new { name = "go_east", animationType = "go", loop = true, frameIndices = new[] { 1, 0 }, flipHorizontal = true });
            }

            result.DefaultAnimation = "face_south";
            result.Capabilities = new PokemonSpriteCapabilities { Directional = true, MovementAnimated = true };
        }
        else if (spriteType == "back")
        {
            // Back sprites are single frame, non-animated
            result.Animations.Add(new
            {
                name = "idle",
                animationType = "face",
                loop = true,
                frameIndices = new[] { 0 },
                flipHorizontal = false
            });

            result.DefaultAnimation = "idle";
            result.Capabilities = new PokemonSpriteCapabilities { Directional = false, MovementAnimated = false };
        }
        else
        {
            // Default: single frame idle animation
            result.Animations.Add(new
            {
                name = "idle",
                animationType = "face",
                loop = true,
                frameIndices = new[] { 0 },
                flipHorizontal = false
            });

            result.DefaultAnimation = "idle";
            result.Capabilities = new PokemonSpriteCapabilities { Directional = false, MovementAnimated = false };
        }

        return result;
    }

    /// <summary>
    /// Extended version that splits on both underscores and hyphens for Pokemon names.
    /// </summary>
    private static string PokemonToPascalCase(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;
        return string.Concat(name.Split('_', '-').Select(w =>
            w.Length > 0 ? char.ToUpperInvariant(w[0]) + w[1..].ToLowerInvariant() : ""));
    }
}

/// <summary>
/// Animation info parsed from species_info headers.
/// </summary>
public class PokemonAnimationInfo
{
    public int FrontFrames { get; set; }
    public List<(int Frame, int Duration)> AnimationCommands { get; set; } = new();
}

/// <summary>
/// Result of animation generation for Pokemon sprites.
/// </summary>
public class PokemonAnimationResult
{
    public List<object> Animations { get; set; } = new();
    public PokemonSpriteCapabilities Capabilities { get; set; } = new();
    public string DefaultAnimation { get; set; } = "";
}

/// <summary>
/// Sprite capabilities for Pokemon sprites.
/// </summary>
public class PokemonSpriteCapabilities
{
    public bool Directional { get; set; }
    public bool MovementAnimated { get; set; }
}
