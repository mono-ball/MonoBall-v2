using System.Text.Json;
using System.Text.RegularExpressions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Porycon3.Services.Extraction;

namespace Porycon3.Services;

/// <summary>
/// Extracts font graphics and definitions from pokeemerald.
/// </summary>
public class FontExtractor : ExtractorBase
{
    // Animation timing for arrows: ~100ms per frame
    private const double ArrowFrameDuration = 0.1;

    public override string Name => "Fonts";
    public override string Description => "Extracts font graphics and character maps";

    public FontExtractor(string inputPath, string outputPath, bool verbose = false)
        : base(inputPath, outputPath, verbose)
    {
    }

    protected override int ExecuteExtraction()
    {
        var fontsPath = Path.Combine(InputPath, "graphics/fonts");
        var fontsSourcePath = Path.Combine(InputPath, "src/fonts.c");
        var charmapPath = Path.Combine(InputPath, "charmap.txt");

        if (!Directory.Exists(fontsPath))
        {
            AddError("", "Font graphics not found");
            return 0;
        }

        var count = 0;

        // Parse character map
        CharacterMap characterMap = null!;
        Dictionary<string, int[]> glyphWidths = null!;

        WithStatus("Parsing character map and glyph widths...", _ =>
        {
            characterMap = ParseCharacterMap(charmapPath);
            glyphWidths = ParseGlyphWidths(fontsSourcePath);
        });

        // Count total items for progress
        var totalItems = new List<string>();

        // Latin fonts
        var latinFonts = new[]
        {
            ("latin_normal", "Normal", "normal"),
            ("latin_narrow", "Narrow", "narrow"),
            ("latin_narrower", "Narrower", "narrower"),
            ("latin_small", "Small", "small"),
            ("latin_small_narrow", "Small Narrow", "small_narrow"),
            ("latin_small_narrower", "Small Narrower", "small_narrower"),
            ("latin_short", "Short", "short"),
            ("latin_short_narrow", "Short Narrow", "short_narrow"),
            ("latin_short_narrower", "Short Narrower", "short_narrower"),
        };

        foreach (var (fileName, _, _) in latinFonts)
        {
            if (File.Exists(Path.Combine(fontsPath, $"{fileName}.png")))
                totalItems.Add($"latin:{fileName}");
        }

        // Japanese fonts
        var japaneseFonts = new[]
        {
            ("japanese_normal", "Normal", "NormalJapanese", false),
            ("japanese_small", "Small", "SmallJapanese", false),
            ("japanese_short", "Short", "ShortJapanese", true),
            ("japanese_frlg_male", "FRLG Male", "FRLGMaleJapanese", true),
            ("japanese_frlg_female", "FRLG Female", "FRLGFemaleJapanese", true),
            ("japanese_bold", "Bold", null, false),
        };

        foreach (var (fileName, _, _, _) in japaneseFonts)
        {
            if (File.Exists(Path.Combine(fontsPath, $"{fileName}.png")))
                totalItems.Add($"jp:{fileName}");
        }

        // Animated elements
        var animatedElements = new[] { ("down_arrow", "Down Arrow", 8, 8), ("down_arrow_alt", "Down Arrow Alt", 8, 8) };
        foreach (var (fileName, _, _, _) in animatedElements)
        {
            if (File.Exists(Path.Combine(fontsPath, $"{fileName}.png")))
                totalItems.Add($"anim:{fileName}");
        }

        // Special graphics
        if (File.Exists(Path.Combine(fontsPath, "braille.png")))
            totalItems.Add("special:braille");
        if (File.Exists(Path.Combine(fontsPath, "keypad_icons.png")))
            totalItems.Add("special:keypad_icons");

        // Character maps
        totalItems.Add("charmap:latin");
        totalItems.Add("charmap:japanese");

        int latinCount = 0, japaneseCount = 0, animatedCount = 0, specialCount = 0;

        WithProgress("Extracting fonts", totalItems, (item, task) =>
        {
            var parts = item.Split(':');
            var category = parts[0];
            var name = parts[1];

            SetTaskDescription(task, $"[cyan]Extracting[/] [yellow]{name}[/]");

            switch (category)
            {
                case "latin":
                    var latinFont = latinFonts.First(f => f.Item1 == name);
                    if (ExtractLatinFont(fontsPath, latinFont.Item1, latinFont.Item2, latinFont.Item3, characterMap, glyphWidths))
                    {
                        latinCount++;
                        count++;
                    }
                    break;

                case "jp":
                    var jpFont = japaneseFonts.First(f => f.Item1 == name);
                    if (ExtractJapaneseFont(fontsPath, jpFont.Item1, jpFont.Item2, jpFont.Item3, jpFont.Item4, glyphWidths))
                    {
                        japaneseCount++;
                        count++;
                    }
                    break;

                case "anim":
                    var animElem = animatedElements.First(f => f.Item1 == name);
                    if (ExtractAnimatedElement(fontsPath, animElem.Item1, animElem.Item2, animElem.Item3, animElem.Item4))
                    {
                        animatedCount++;
                        count++;
                    }
                    break;

                case "special":
                    if (name == "braille" && ExtractBrailleFont(fontsPath))
                    {
                        specialCount++;
                        count++;
                    }
                    else if (name == "keypad_icons" && ExtractKeypadIcons(fontsPath))
                    {
                        specialCount++;
                        count++;
                    }
                    break;

                case "charmap":
                    if (name == "latin")
                        GenerateLatinCharacterMap(characterMap);
                    else if (name == "japanese")
                        GenerateJapaneseCharacterMap(characterMap);
                    break;
            }
        });

        SetCount("Latin", latinCount);
        SetCount("Japanese", japaneseCount);
        SetCount("Animated", animatedCount);
        SetCount("Special", specialCount);

        return count;
    }

    /// <summary>
    /// Converts an indexed PNG to RGBA, making background colors transparent.
    /// </summary>
    private void ConvertToTransparentPng(string sourcePath, string destPath)
    {
        using var sourceImage = Image.Load<Rgba32>(sourcePath);
        using var destImage = new Image<Rgba32>(sourceImage.Width, sourceImage.Height);

        for (int y = 0; y < sourceImage.Height; y++)
        {
            for (int x = 0; x < sourceImage.Width; x++)
            {
                var pixel = sourceImage[x, y];

                // Make both background blue AND white box color transparent
                bool isBackground = pixel.R == 0x90 && pixel.G == 0xC8 && pixel.B == 0xFF;
                bool isBoxColor = pixel.R == 0xFF && pixel.G == 0xFF && pixel.B == 0xFF;

                destImage[x, y] = (isBackground || isBoxColor)
                    ? new Rgba32(0, 0, 0, 0)
                    : pixel;
            }
        }

        destImage.SaveAsPng(destPath);
    }

    /// <summary>
    /// Converts a PNG to RGBA, making the color at (0,0) transparent.
    /// </summary>
    private void ConvertToTransparentPngByCornerColor(string sourcePath, string destPath)
    {
        using var sourceImage = Image.Load<Rgba32>(sourcePath);
        var transparentColor = sourceImage[0, 0];

        using var destImage = new Image<Rgba32>(sourceImage.Width, sourceImage.Height);

        for (int y = 0; y < sourceImage.Height; y++)
        {
            for (int x = 0; x < sourceImage.Width; x++)
            {
                var pixel = sourceImage[x, y];

                destImage[x, y] = (pixel.R == transparentColor.R &&
                                   pixel.G == transparentColor.G &&
                                   pixel.B == transparentColor.B)
                    ? new Rgba32(0, 0, 0, 0)
                    : pixel;
            }
        }

        destImage.SaveAsPng(destPath);
    }

    private bool ExtractLatinFont(string fontsPath, string fileName, string displayName, string widthKey,
        CharacterMap charMap, Dictionary<string, int[]> glyphWidths)
    {
        var pngPath = Path.Combine(fontsPath, $"{fileName}.png");
        if (!File.Exists(pngPath)) return false;

        var (width, height, glyphWidth, glyphHeight, glyphsPerRow) = GetFontDimensions(pngPath);
        if (width == 0) return false;

        var pascalName = ToPascalCase(fileName);
        var outputPngPath = GetGraphicsPath("Fonts", $"{pascalName}.png");
        ConvertToTransparentPng(pngPath, outputPngPath);

        var fontWidthKey = $"gFont{ToPascalCase(widthKey)}LatinGlyphWidths";
        var widths = glyphWidths.TryGetValue(fontWidthKey, out var w) ? w : null;

        var definition = new
        {
            id = $"{IdTransformer.Namespace}:font:{fileName.Replace("_", "-")}",
            name = $"Latin {displayName}",
            type = "Font",
            texturePath = $"Graphics/Fonts/{pascalName}.png",
            glyphWidth,
            glyphHeight,
            glyphsPerRow,
            characterMapId = $"{IdTransformer.Namespace}:charmap:latin",
            glyphWidths = widths,
            lineHeight = glyphHeight - 2,
            baseline = glyphHeight - 4
        };

        var defPath = GetDefinitionPath("Fonts", $"{pascalName}.json");
        File.WriteAllText(defPath, JsonSerializer.Serialize(definition, JsonOptions.Default));

        LogVerbose($"Extracted Latin font: {pascalName}");
        return true;
    }

    private bool ExtractJapaneseFont(string fontsPath, string fileName, string displayName, string? widthKey,
        bool hasWidths, Dictionary<string, int[]> glyphWidths)
    {
        var pngPath = Path.Combine(fontsPath, $"{fileName}.png");
        if (!File.Exists(pngPath)) return false;

        var (width, height, glyphWidth, glyphHeight, glyphsPerRow) = GetFontDimensions(pngPath);
        if (width == 0) return false;

        var pascalName = ToPascalCase(fileName);
        var outputPngPath = GetGraphicsPath("Fonts", $"{pascalName}.png");
        ConvertToTransparentPng(pngPath, outputPngPath);

        int[]? widths = null;
        if (hasWidths && widthKey != null)
        {
            var fontWidthKey = $"gFont{widthKey}GlyphWidths";
            widths = glyphWidths.TryGetValue(fontWidthKey, out var w) ? w : null;
        }

        var definition = new
        {
            id = $"{IdTransformer.Namespace}:font:{fileName.Replace("_", "-")}",
            name = $"Japanese {displayName}",
            type = "Font",
            texturePath = $"Graphics/Fonts/{pascalName}.png",
            glyphWidth,
            glyphHeight,
            glyphsPerRow,
            characterMapId = $"{IdTransformer.Namespace}:charmap:japanese",
            glyphWidths = widths,
            lineHeight = glyphHeight - 2,
            baseline = glyphHeight - 4
        };

        var defPath = GetDefinitionPath("Fonts", $"{pascalName}.json");
        File.WriteAllText(defPath, JsonSerializer.Serialize(definition, JsonOptions.Default));

        LogVerbose($"Extracted Japanese font: {pascalName}");
        return true;
    }

    private bool ExtractAnimatedElement(string fontsPath, string fileName, string displayName, int frameWidth, int frameHeight)
    {
        var pngPath = Path.Combine(fontsPath, $"{fileName}.png");
        if (!File.Exists(pngPath)) return false;

        using var image = Image.Load<Rgba32>(pngPath);
        var frameCount = image.Height / frameHeight;

        var pascalName = ToPascalCase(fileName);
        var outputPngPath = GetGraphicsPath("UI", "Interface", $"{pascalName}.png");
        ConvertToTransparentPngByCornerColor(pngPath, outputPngPath);

        var frames = new List<object>();
        for (var i = 0; i < frameCount; i++)
        {
            frames.Add(new { index = i, x = 0, y = i * frameHeight, width = frameWidth, height = frameHeight });
        }

        var frameIndices = Enumerable.Range(0, frameCount).ToList();
        var frameDurations = Enumerable.Repeat(ArrowFrameDuration, frameCount).ToList();

        var animations = new List<object>
        {
            new { name = "idle", loop = true, frameIndices, frameDurations, flipHorizontal = false }
        };

        var definition = new
        {
            id = $"{IdTransformer.Namespace}:sprite:ui/interface/{fileName.Replace("_", "-")}",
            name = displayName,
            type = "Sprite",
            texturePath = $"Graphics/UI/Interface/{pascalName}.png",
            frameWidth,
            frameHeight,
            frameCount,
            frames,
            animations
        };

        var defPath = GetDefinitionPath("UI", "Interface", $"{pascalName}.json");
        File.WriteAllText(defPath, JsonSerializer.Serialize(definition, JsonOptions.Default));

        LogVerbose($"Extracted animated element: {pascalName}");
        return true;
    }

    private bool ExtractBrailleFont(string fontsPath)
    {
        var pngPath = Path.Combine(fontsPath, "braille.png");
        if (!File.Exists(pngPath)) return false;

        var outputPngPath = GetGraphicsPath("Fonts", "Braille.png");
        ConvertToTransparentPng(pngPath, outputPngPath);

        var definition = new
        {
            id = $"{IdTransformer.Namespace}:font:braille",
            name = "Braille",
            type = "Font",
            texturePath = "Graphics/Fonts/Braille.png",
            glyphWidth = 8,
            glyphHeight = 16,
            glyphsPerRow = 32,
            characterMapId = $"{IdTransformer.Namespace}:charmap:latin",
            lineHeight = 16,
            baseline = 14
        };

        var defPath = GetDefinitionPath("Fonts", "Braille.json");
        File.WriteAllText(defPath, JsonSerializer.Serialize(definition, JsonOptions.Default));

        LogVerbose("Extracted Braille font");
        return true;
    }

    private bool ExtractKeypadIcons(string fontsPath)
    {
        var pngPath = Path.Combine(fontsPath, "keypad_icons.png");
        if (!File.Exists(pngPath)) return false;

        var outputPngPath = GetGraphicsPath("UI", "Interface", "KeypadIcons.png");
        ConvertToTransparentPngByCornerColor(pngPath, outputPngPath);

        var definition = new
        {
            id = $"{IdTransformer.Namespace}:sprite:ui/interface/keypad-icons",
            name = "Keypad Icons",
            type = "Sprite",
            texturePath = "Graphics/UI/Interface/KeypadIcons.png",
            frameWidth = 16,
            frameHeight = 16,
            frameCount = 16
        };

        var defPath = GetDefinitionPath("UI", "Interface", "KeypadIcons.json");
        File.WriteAllText(defPath, JsonSerializer.Serialize(definition, JsonOptions.Default));

        LogVerbose("Extracted Keypad Icons");
        return true;
    }

    private (int width, int height, int glyphWidth, int glyphHeight, int glyphsPerRow) GetFontDimensions(string pngPath)
    {
        try
        {
            using var image = Image.Load<Rgba32>(pngPath);
            var width = image.Width;
            var height = image.Height;

            int glyphWidth, glyphHeight, glyphsPerRow;

            if (width == 256 && height == 512)
            {
                glyphWidth = 16; glyphHeight = 16; glyphsPerRow = 16;
            }
            else if (width == 128 && height == 512)
            {
                glyphWidth = 8; glyphHeight = 16; glyphsPerRow = 16;
            }
            else if (width == 128 && height == 256)
            {
                glyphWidth = 8; glyphHeight = 16; glyphsPerRow = 16;
            }
            else
            {
                glyphWidth = 16; glyphHeight = 16; glyphsPerRow = width / 16;
            }

            return (width, height, glyphWidth, glyphHeight, glyphsPerRow);
        }
        catch (Exception ex)
        {
            AddError(Path.GetFileName(pngPath), $"Error reading image: {ex.Message}", ex);
            return (0, 0, 0, 0, 0);
        }
    }

    private record CharacterMap(
        Dictionary<string, int> LatinMappings,
        Dictionary<string, int> HiraganaMappings,
        Dictionary<string, int> KatakanaMappings,
        Dictionary<string, int[]> SpecialSymbols
    );

    private CharacterMap ParseCharacterMap(string filePath)
    {
        var latinMappings = new Dictionary<string, int>();
        var hiraganaMappings = new Dictionary<string, int>();
        var katakanaMappings = new Dictionary<string, int>();
        var specialSymbols = new Dictionary<string, int[]>();

        if (!File.Exists(filePath))
        {
            LogWarning("charmap.txt not found, using defaults");
            return new CharacterMap(latinMappings, hiraganaMappings, katakanaMappings, specialSymbols);
        }

        var lines = File.ReadAllLines(filePath);
        var section = "latin";

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;

            if (trimmed.StartsWith("@"))
            {
                if (trimmed.Contains("Hiragana")) section = "hiragana";
                else if (trimmed.Contains("Katakana")) section = "katakana";
                else if (trimmed.Contains("punctuation")) section = "latin";
                continue;
            }

            var match = Regex.Match(trimmed, @"^'(.)'(?:\s+@.*?)?\s*=\s*([0-9A-Fa-f]+)$");
            if (match.Success)
            {
                var charValue = match.Groups[1].Value;
                var index = Convert.ToInt32(match.Groups[2].Value, 16);

                switch (section)
                {
                    case "hiragana": hiraganaMappings[charValue] = index; break;
                    case "katakana": katakanaMappings[charValue] = index; break;
                    default: latinMappings[charValue] = index; break;
                }
                continue;
            }

            match = Regex.Match(trimmed, @"^([A-Z_][A-Z0-9_]*)\s*=\s*([0-9A-Fa-f]+(?:\s+[0-9A-Fa-f]+)*)");
            if (match.Success)
            {
                var symbol = match.Groups[1].Value;

                var fontGlyphSymbols = new HashSet<string>
                {
                    "SUPER_ER", "LV", "V_D_ARROW", "NBSP",
                    "PK", "PKMN", "POKEBLOCK",
                    "UNK_SPACER", "UP_ARROW", "DOWN_ARROW", "LEFT_ARROW", "RIGHT_ARROW",
                    "SUPER_E", "SUPER_RE"
                };

                if (!fontGlyphSymbols.Contains(symbol)) continue;

                var hexValues = match.Groups[2].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var indices = hexValues.Select(h => Convert.ToInt32(h, 16)).ToArray();

                specialSymbols[symbol] = indices;
            }
        }

        return new CharacterMap(latinMappings, hiraganaMappings, katakanaMappings, specialSymbols);
    }

    private void GenerateLatinCharacterMap(CharacterMap charMap)
    {
        var latinDef = new
        {
            id = $"{IdTransformer.Namespace}:charmap:latin",
            name = "Latin Character Map",
            type = "CharacterMap",
            mappings = charMap.LatinMappings,
            specialSymbols = charMap.SpecialSymbols
        };

        var latinPath = GetDefinitionPath("Fonts", "CharacterMapLatin.json");
        File.WriteAllText(latinPath, JsonSerializer.Serialize(latinDef, JsonOptions.Default));
    }

    private void GenerateJapaneseCharacterMap(CharacterMap charMap)
    {
        var japaneseMappings = new Dictionary<string, int>();
        foreach (var (ch, idx) in charMap.HiraganaMappings)
            japaneseMappings[ch] = idx;
        foreach (var (ch, idx) in charMap.KatakanaMappings)
            japaneseMappings[ch] = idx;

        var japaneseDef = new
        {
            id = $"{IdTransformer.Namespace}:charmap:japanese",
            name = "Japanese Character Map",
            type = "CharacterMap",
            mappings = japaneseMappings
        };

        var japanesePath = GetDefinitionPath("Fonts", "CharacterMapJapanese.json");
        File.WriteAllText(japanesePath, JsonSerializer.Serialize(japaneseDef, JsonOptions.Default));
    }

    private Dictionary<string, int[]> ParseGlyphWidths(string filePath)
    {
        var result = new Dictionary<string, int[]>();

        if (!File.Exists(filePath))
        {
            LogWarning("fonts.c not found");
            return result;
        }

        var content = File.ReadAllText(filePath);

        var pattern = new Regex(
            @"const\s+u8\s+(gFont\w+GlyphWidths)\[\]\s*=\s*\{([^}]+)\}",
            RegexOptions.Singleline);

        foreach (Match match in pattern.Matches(content))
        {
            var arrayName = match.Groups[1].Value;
            var valuesStr = match.Groups[2].Value;

            var values = Regex.Matches(valuesStr, @"\d+")
                .Select(m => int.Parse(m.Value))
                .ToArray();

            result[arrayName] = values;
        }

        return result;
    }

    private static string ToPascalCase(string snakeCase)
    {
        return string.Concat(snakeCase.Split('_')
            .Select(w => char.ToUpperInvariant(w[0]) + w[1..].ToLowerInvariant()));
    }
}
