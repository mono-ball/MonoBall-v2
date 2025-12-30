using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using Porycon3.Services.Extraction;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

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

        int pokemonCount = 0;
        int spriteCount = 0;
        int formCount = 0;

        // Get all Pokemon directories
        var pokemonDirs = Directory.GetDirectories(_pokemonGraphics).ToList();

        WithProgress("Extracting Pokemon sprites", pokemonDirs, (pokemonDir, task) =>
        {
            var pokemonName = Path.GetFileName(pokemonDir);
            SetTaskDescription(task, $"[cyan]Extracting[/] [yellow]{pokemonName}[/]");

            try
            {
                var (sprites, forms) = ExtractPokemon(pokemonDir, pokemonName, animationData);
                if (sprites > 0)
                {
                    pokemonCount++;
                    spriteCount += sprites;
                    formCount += forms;
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
        var pascalName = ToPascalCase(pokemonName);
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

        var pascalPokemon = ToPascalCase(pokemonName);
        var pascalForm = ToPascalCase(formName);
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
            var (indices, width, height, _) = ExtractPixelIndices(bytes);

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
            SaveAsRgbaPng(output, outputPath);

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
            var palette = ExtractPngPalette(bytes);
            var (indices, width, height, _) = ExtractPixelIndices(bytes);

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
                SaveAsRgbaPng(img, outputPath);
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
            SaveAsRgbaPng(output, outPath);
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
            var (indices, width, height, _) = ExtractPixelIndices(bytes);

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
            SaveAsRgbaPng(output, outputPath);
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
    /// </summary>
    private void GenerateSpriteDefinitions(string pokemonName, string outputDataDir, string outputGraphicsDir, PokemonAnimationInfo? animInfo)
    {
        var pascalName = ToPascalCase(pokemonName);
        var normalizedName = pokemonName.ToLowerInvariant();

        var graphicsFiles = Directory.GetFiles(outputGraphicsDir, "*.png");

        foreach (var file in graphicsFiles)
        {
            var spriteName = Path.GetFileNameWithoutExtension(file);
            var spriteType = DetermineSpriteType(spriteName);
            var (frameWidth, frameHeight, frameCount) = DetermineFrameInfo(file, spriteType);

            var animations = GenerateAnimations(spriteName, spriteType, frameCount, animInfo);

            var spriteDefinition = new
            {
                id = $"{IdTransformer.Namespace}:pokemon:sprite/{normalizedName}/{spriteName.ToLowerInvariant()}",
                name = FormatDisplayName(spriteName),
                type = "Sprite",
                texturePath = $"Graphics/Pokemon/{pascalName}/{spriteName}.png",
                spriteType,
                frameWidth,
                frameHeight,
                frameCount,
                animations = animations.Count > 0 ? animations : null
            };

            var defPath = Path.Combine(outputDataDir, $"{spriteName}.json");
            File.WriteAllText(defPath, JsonSerializer.Serialize(spriteDefinition, JsonOptions.Default));
        }
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

    /// <summary>
    /// Extract raw pixel indices from PNG IDAT chunk.
    /// </summary>
    private static (byte[]? Indices, int Width, int Height, int BitDepth) ExtractPixelIndices(byte[] pngData)
    {
        int width = 0, height = 0, bitDepth = 0, colorType = 0;
        var idatChunks = new List<byte[]>();

        var pos = 8;
        while (pos < pngData.Length - 12)
        {
            var length = (pngData[pos] << 24) | (pngData[pos + 1] << 16) |
                         (pngData[pos + 2] << 8) | pngData[pos + 3];
            var type = System.Text.Encoding.ASCII.GetString(pngData, pos + 4, 4);

            if (type == "IHDR" && length >= 13)
            {
                width = (pngData[pos + 8] << 24) | (pngData[pos + 9] << 16) |
                        (pngData[pos + 10] << 8) | pngData[pos + 11];
                height = (pngData[pos + 12] << 24) | (pngData[pos + 13] << 16) |
                         (pngData[pos + 14] << 8) | pngData[pos + 15];
                bitDepth = pngData[pos + 16];
                colorType = pngData[pos + 17];
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

        if (colorType != 3 || width == 0 || height == 0)
            return (null, 0, 0, 0);

        var compressedData = idatChunks.SelectMany(c => c).ToArray();
        byte[] decompressed;

        try
        {
            using var compressedStream = new MemoryStream(compressedData);
            using var zlibStream = new ZLibStream(compressedStream, CompressionMode.Decompress);
            using var outputStream = new MemoryStream();
            zlibStream.CopyTo(outputStream);
            decompressed = outputStream.ToArray();
        }
        catch
        {
            return (null, 0, 0, 0);
        }

        var indices = new byte[width * height];
        var scanlineWidth = (width * bitDepth + 7) / 8;
        var previousScanline = new byte[scanlineWidth];

        var srcPos = 0;
        for (int y = 0; y < height; y++)
        {
            if (srcPos >= decompressed.Length) break;

            var filterType = decompressed[srcPos++];
            var scanline = new byte[scanlineWidth];

            for (int i = 0; i < scanlineWidth && srcPos < decompressed.Length; i++)
            {
                var raw = decompressed[srcPos++];

                switch (filterType)
                {
                    case 0: // None
                        scanline[i] = raw;
                        break;
                    case 1: // Sub
                        scanline[i] = (byte)(raw + (i > 0 ? scanline[i - 1] : 0));
                        break;
                    case 2: // Up
                        scanline[i] = (byte)(raw + previousScanline[i]);
                        break;
                    case 3: // Average
                        var left = i > 0 ? scanline[i - 1] : 0;
                        scanline[i] = (byte)(raw + (left + previousScanline[i]) / 2);
                        break;
                    case 4: // Paeth
                        var a = i > 0 ? scanline[i - 1] : 0;
                        var b = previousScanline[i];
                        var c = i > 0 ? previousScanline[i - 1] : 0;
                        scanline[i] = (byte)(raw + PaethPredictor(a, b, c));
                        break;
                }
            }

            Array.Copy(scanline, previousScanline, scanlineWidth);

            for (int x = 0; x < width; x++)
            {
                int byteIdx = (x * bitDepth) / 8;
                int bitOffset = 8 - bitDepth - ((x * bitDepth) % 8);

                if (byteIdx < scanline.Length)
                {
                    var mask = (1 << bitDepth) - 1;
                    var idx = (scanline[byteIdx] >> bitOffset) & mask;
                    indices[y * width + x] = (byte)idx;
                }
            }
        }

        return (indices, width, height, bitDepth);
    }

    private static int PaethPredictor(int a, int b, int c)
    {
        var p = a + b - c;
        var pa = Math.Abs(p - a);
        var pb = Math.Abs(p - b);
        var pc = Math.Abs(p - c);

        if (pa <= pb && pa <= pc) return a;
        if (pb <= pc) return b;
        return c;
    }

    /// <summary>
    /// Extract RGB palette from PNG PLTE chunk.
    /// </summary>
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

    private static List<object> GenerateAnimations(string spriteName, string spriteType, int frameCount, PokemonAnimationInfo? animInfo)
    {
        var animations = new List<object>();

        if (spriteType == "front" && animInfo?.AnimationCommands != null)
        {
            var frames = animInfo.AnimationCommands.Select(c => c.Frame).ToList();
            var durations = animInfo.AnimationCommands.Select(c => c.Duration / 60.0).ToList();

            animations.Add(new
            {
                name = "idle",
                loop = true,
                frameIndices = frames,
                frameDurations = durations
            });
        }
        else if (spriteType == "icon" && frameCount >= 2)
        {
            animations.Add(new
            {
                name = "bounce",
                loop = true,
                frameIndices = new[] { 0, 1 },
                frameDurations = new[] { 0.5, 0.5 }
            });
        }
        else if (spriteType == "overworld" && frameCount >= 4)
        {
            // Standard overworld: 6 frames (down, down-walk, up, up-walk, side, side-walk)
            animations.Add(new { name = "face_down", loop = true, frameIndices = new[] { 0 }, frameDurations = new[] { 1.0 } });
            animations.Add(new { name = "walk_down", loop = true, frameIndices = new[] { 0, 1 }, frameDurations = new[] { 0.25, 0.25 } });
            if (frameCount >= 4)
            {
                animations.Add(new { name = "face_up", loop = true, frameIndices = new[] { 2 }, frameDurations = new[] { 1.0 } });
                animations.Add(new { name = "walk_up", loop = true, frameIndices = new[] { 2, 3 }, frameDurations = new[] { 0.25, 0.25 } });
            }
            if (frameCount >= 6)
            {
                animations.Add(new { name = "face_side", loop = true, frameIndices = new[] { 4 }, frameDurations = new[] { 1.0 } });
                animations.Add(new { name = "walk_side", loop = true, frameIndices = new[] { 4, 5 }, frameDurations = new[] { 0.25, 0.25 } });
            }
        }

        return animations;
    }

    private static string ToPascalCase(string name)
    {
        return string.Concat(name.Split('_', '-').Select(w =>
            w.Length > 0 ? char.ToUpperInvariant(w[0]) + w[1..].ToLowerInvariant() : ""));
    }

    private static string FormatDisplayName(string name)
    {
        var result = new System.Text.StringBuilder();
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (i > 0 && char.IsUpper(c) && !char.IsUpper(name[i - 1]))
                result.Append(' ');
            result.Append(c);
        }
        return result.ToString();
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
