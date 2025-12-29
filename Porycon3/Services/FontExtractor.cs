using System.Text.Json;
using System.Text.RegularExpressions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Porycon3.Services;

public class FontExtractor
{
    private readonly string _inputPath;
    private readonly string _outputPath;

    // Animation timing for arrows: ~100ms per frame
    private const double ArrowFrameDuration = 0.1;

    public FontExtractor(string inputPath, string outputPath)
    {
        _inputPath = inputPath;
        _outputPath = outputPath;
    }

    /// <summary>
    /// Converts an indexed PNG to RGBA, making background colors transparent.
    /// GBA fonts use a 4-color palette where:
    ///   - Index 0 (blue #90C8FF) = background/transparent
    ///   - Index 1 (dark grey #383838) = foreground text
    ///   - Index 2 (light grey #D8D8D8) = shadow
    ///   - Index 3 (white #FFFFFF) = "box" color, also treated as transparent at runtime
    /// The text rendering lookup table maps both index 0 and index 3 to bgColor,
    /// so white boxes around glyphs are an editing artifact that render as transparent.
    /// </summary>
    private void ConvertToTransparentPng(string sourcePath, string destPath)
    {
        using var sourceImage = Image.Load<Rgba32>(sourcePath);

        // Create a new RGBA image to ensure we output as true RGBA, not indexed
        using var destImage = new Image<Rgba32>(sourceImage.Width, sourceImage.Height);

        for (int y = 0; y < sourceImage.Height; y++)
        {
            for (int x = 0; x < sourceImage.Width; x++)
            {
                var pixel = sourceImage[x, y];

                // Make both background blue AND white box color transparent
                // Blue background: #90C8FF (palette index 0)
                // White box: #FFFFFF (palette index 3) - mapped to bgColor at runtime
                bool isBackground = pixel.R == 0x90 && pixel.G == 0xC8 && pixel.B == 0xFF;
                bool isBoxColor = pixel.R == 0xFF && pixel.G == 0xFF && pixel.B == 0xFF;

                if (isBackground || isBoxColor)
                {
                    destImage[x, y] = new Rgba32(0, 0, 0, 0);
                }
                else
                {
                    destImage[x, y] = pixel;
                }
            }
        }

        destImage.SaveAsPng(destPath);
    }

    /// <summary>
    /// Converts a PNG to RGBA, making the color at (0,0) transparent.
    /// Used for graphics that don't use the standard font palette (like keypad icons).
    /// </summary>
    private void ConvertToTransparentPngByCornerColor(string sourcePath, string destPath)
    {
        using var sourceImage = Image.Load<Rgba32>(sourcePath);

        // Get the color at (0,0) which should be the transparent/background color
        var transparentColor = sourceImage[0, 0];

        // Create a new RGBA image to ensure we output as true RGBA, not indexed
        using var destImage = new Image<Rgba32>(sourceImage.Width, sourceImage.Height);

        for (int y = 0; y < sourceImage.Height; y++)
        {
            for (int x = 0; x < sourceImage.Width; x++)
            {
                var pixel = sourceImage[x, y];

                // Make pixels matching the corner color fully transparent
                if (pixel.R == transparentColor.R &&
                    pixel.G == transparentColor.G &&
                    pixel.B == transparentColor.B)
                {
                    destImage[x, y] = new Rgba32(0, 0, 0, 0);
                }
                else
                {
                    destImage[x, y] = pixel;
                }
            }
        }

        destImage.SaveAsPng(destPath);
    }

    public int Extract()
    {
        var fontsPath = Path.Combine(_inputPath, "graphics/fonts");
        var fontsSourcePath = Path.Combine(_inputPath, "src/fonts.c");
        var charmapPath = Path.Combine(_inputPath, "charmap.txt");

        if (!Directory.Exists(fontsPath))
        {
            Console.WriteLine("[FontExtractor] Font graphics not found");
            return 0;
        }

        var count = 0;

        // Parse character map
        var characterMap = ParseCharacterMap(charmapPath);

        // Parse glyph widths from fonts.c
        var glyphWidths = ParseGlyphWidths(fontsSourcePath);

        // Extract Latin fonts
        count += ExtractLatinFonts(fontsPath, characterMap, glyphWidths);

        // Extract Japanese fonts
        count += ExtractJapaneseFonts(fontsPath, glyphWidths);

        // Extract animated elements (arrows)
        count += ExtractAnimatedElements(fontsPath);

        // Extract special graphics (braille, keypad)
        count += ExtractSpecialGraphics(fontsPath);

        // Generate character map definition
        GenerateCharacterMapDefinition(characterMap);

        Console.WriteLine($"[FontExtractor] Extracted {count} font assets");
        return count;
    }

    private int ExtractLatinFonts(string fontsPath, CharacterMap charMap, Dictionary<string, int[]> glyphWidths)
    {
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

        var count = 0;

        foreach (var (fileName, displayName, widthKey) in latinFonts)
        {
            var pngPath = Path.Combine(fontsPath, $"{fileName}.png");
            if (!File.Exists(pngPath)) continue;

            // Get dimensions
            var (width, height, glyphWidth, glyphHeight, glyphsPerRow) = GetFontDimensions(pngPath);
            if (width == 0) continue;

            // Copy the font image
            var pascalName = ToPascalCase(fileName);
            var outputGraphicsDir = Path.Combine(_outputPath, "Graphics/Fonts");
            Directory.CreateDirectory(outputGraphicsDir);
            var outputPngPath = Path.Combine(outputGraphicsDir, $"{pascalName}.png");
            ConvertToTransparentPng(pngPath, outputPngPath);

            // Get glyph widths for this font
            var fontWidthKey = $"gFont{ToPascalCase(widthKey)}LatinGlyphWidths";
            var widths = glyphWidths.TryGetValue(fontWidthKey, out var w) ? w : null;

            // Create font definition
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

            var outputDefDir = Path.Combine(_outputPath, "Definitions/Assets/Fonts");
            Directory.CreateDirectory(outputDefDir);
            var defPath = Path.Combine(outputDefDir, $"{pascalName}.json");
            var json = JsonSerializer.Serialize(definition, new JsonSerializerOptions
            {
                WriteIndented = true,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            });
            File.WriteAllText(defPath, json);

            count++;
        }

        return count;
    }

    private int ExtractJapaneseFonts(string fontsPath, Dictionary<string, int[]> glyphWidths)
    {
        var japaneseFonts = new[]
        {
            ("japanese_normal", "Normal", "NormalJapanese", false),
            ("japanese_small", "Small", "SmallJapanese", false),
            ("japanese_short", "Short", "ShortJapanese", true),
            ("japanese_frlg_male", "FRLG Male", "FRLGMaleJapanese", true),
            ("japanese_frlg_female", "FRLG Female", "FRLGFemaleJapanese", true),
            ("japanese_bold", "Bold", null, false),
        };

        var count = 0;

        foreach (var (fileName, displayName, widthKey, hasWidths) in japaneseFonts)
        {
            var pngPath = Path.Combine(fontsPath, $"{fileName}.png");
            if (!File.Exists(pngPath)) continue;

            var (width, height, glyphWidth, glyphHeight, glyphsPerRow) = GetFontDimensions(pngPath);
            if (width == 0) continue;

            // Copy the font image
            var pascalName = ToPascalCase(fileName);
            var outputGraphicsDir = Path.Combine(_outputPath, "Graphics/Fonts");
            Directory.CreateDirectory(outputGraphicsDir);
            var outputPngPath = Path.Combine(outputGraphicsDir, $"{pascalName}.png");
            ConvertToTransparentPng(pngPath, outputPngPath);

            // Get glyph widths if available
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

            var outputDefDir = Path.Combine(_outputPath, "Definitions/Assets/Fonts");
            Directory.CreateDirectory(outputDefDir);
            var defPath = Path.Combine(outputDefDir, $"{pascalName}.json");
            var json = JsonSerializer.Serialize(definition, new JsonSerializerOptions
            {
                WriteIndented = true,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            });
            File.WriteAllText(defPath, json);

            count++;
        }

        return count;
    }

    private int ExtractAnimatedElements(string fontsPath)
    {
        var animatedElements = new[]
        {
            ("down_arrow", "Down Arrow", 8, 8),
            ("down_arrow_alt", "Down Arrow Alt", 8, 8),
        };

        var count = 0;

        foreach (var (fileName, displayName, frameWidth, frameHeight) in animatedElements)
        {
            var pngPath = Path.Combine(fontsPath, $"{fileName}.png");
            if (!File.Exists(pngPath)) continue;

            using var image = Image.Load<Rgba32>(pngPath);
            var frameCount = image.Height / frameHeight;

            // Copy the sprite sheet (arrows use non-standard palettes, use corner color for transparency)
            var pascalName = ToPascalCase(fileName);
            var outputGraphicsDir = Path.Combine(_outputPath, "Graphics/UI/Interface");
            Directory.CreateDirectory(outputGraphicsDir);
            var outputPngPath = Path.Combine(outputGraphicsDir, $"{pascalName}.png");
            ConvertToTransparentPngByCornerColor(pngPath, outputPngPath);

            // Build frames array (frames stacked vertically)
            var frames = new List<object>();
            for (var i = 0; i < frameCount; i++)
            {
                frames.Add(new
                {
                    index = i,
                    x = 0,
                    y = i * frameHeight,
                    width = frameWidth,
                    height = frameHeight
                });
            }

            // Build animation
            var frameIndices = Enumerable.Range(0, frameCount).ToList();
            var frameDurations = Enumerable.Repeat(ArrowFrameDuration, frameCount).ToList();

            var animations = new List<object>
            {
                new
                {
                    name = "idle",
                    loop = true,
                    frameIndices,
                    frameDurations,
                    flipHorizontal = false
                }
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

            var outputDefDir = Path.Combine(_outputPath, "Definitions/Assets/UI/Interface");
            Directory.CreateDirectory(outputDefDir);
            var defPath = Path.Combine(outputDefDir, $"{pascalName}.json");
            var json = JsonSerializer.Serialize(definition, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(defPath, json);

            count++;
        }

        return count;
    }

    private int ExtractSpecialGraphics(string fontsPath)
    {
        var count = 0;

        // Braille is a font - uses same character indices as Latin
        count += ExtractBrailleFont(fontsPath);

        // Keypad icons are UI elements, not a font
        count += ExtractKeypadIcons(fontsPath);

        return count;
    }

    private int ExtractBrailleFont(string fontsPath)
    {
        var pngPath = Path.Combine(fontsPath, "braille.png");
        if (!File.Exists(pngPath)) return 0;

        var outputGraphicsDir = Path.Combine(_outputPath, "Graphics/Fonts");
        Directory.CreateDirectory(outputGraphicsDir);
        var outputPngPath = Path.Combine(outputGraphicsDir, "Braille.png");

        // Braille uses standard font palette (blue bg, white box = transparent)
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

        var outputDefDir = Path.Combine(_outputPath, "Definitions/Assets/Fonts");
        Directory.CreateDirectory(outputDefDir);
        var defPath = Path.Combine(outputDefDir, "Braille.json");
        var json = JsonSerializer.Serialize(definition, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(defPath, json);

        return 1;
    }

    private int ExtractKeypadIcons(string fontsPath)
    {
        var pngPath = Path.Combine(fontsPath, "keypad_icons.png");
        if (!File.Exists(pngPath)) return 0;

        var outputGraphicsDir = Path.Combine(_outputPath, "Graphics/UI/Interface");
        Directory.CreateDirectory(outputGraphicsDir);
        var outputPngPath = Path.Combine(outputGraphicsDir, "KeypadIcons.png");

        // Keypad icons use non-standard palette (green bg)
        ConvertToTransparentPngByCornerColor(pngPath, outputPngPath);

        var definition = new
        {
            id = $"{IdTransformer.Namespace}:sprite:ui/interface/keypad-icons",
            name = "Keypad Icons",
            type = "Sprite",
            texturePath = "Graphics/UI/Interface/KeypadIcons.png",
            frameWidth = 16,
            frameHeight = 16,
            frameCount = 16  // 2 rows of 8 icons
        };

        var outputDefDir = Path.Combine(_outputPath, "Definitions/Assets/UI/Interface");
        Directory.CreateDirectory(outputDefDir);
        var defPath = Path.Combine(outputDefDir, "KeypadIcons.json");
        var json = JsonSerializer.Serialize(definition, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(defPath, json);

        return 1;
    }

    private (int width, int height, int glyphWidth, int glyphHeight, int glyphsPerRow) GetFontDimensions(string pngPath)
    {
        try
        {
            using var image = Image.Load<Rgba32>(pngPath);
            var width = image.Width;
            var height = image.Height;

            // Latin fonts are 256x512 with 16x16 glyphs (16 per row, 32 rows = 512 glyphs)
            // Japanese hw fonts are 128x512 with 8x16 glyphs (16 per row)
            // Japanese fw fonts are 256x512 with 16x16 glyphs

            int glyphWidth, glyphHeight, glyphsPerRow;

            if (width == 256 && height == 512)
            {
                // Standard Latin or full-width Japanese font
                glyphWidth = 16;
                glyphHeight = 16;
                glyphsPerRow = 16;
            }
            else if (width == 128 && height == 512)
            {
                // Half-width Japanese font
                glyphWidth = 8;
                glyphHeight = 16;
                glyphsPerRow = 16;
            }
            else if (width == 128 && height == 256)
            {
                // Bold Japanese font (smaller)
                glyphWidth = 8;
                glyphHeight = 16;
                glyphsPerRow = 16;
            }
            else
            {
                // Default to 16x16 glyphs
                glyphWidth = 16;
                glyphHeight = 16;
                glyphsPerRow = width / glyphWidth;
            }

            return (width, height, glyphWidth, glyphHeight, glyphsPerRow);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FontExtractor] Error reading {pngPath}: {ex.Message}");
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
            Console.WriteLine("[FontExtractor] charmap.txt not found, using defaults");
            return new CharacterMap(latinMappings, hiraganaMappings, katakanaMappings, specialSymbols);
        }

        var lines = File.ReadAllLines(filePath);
        var section = "latin";

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;

            // Skip comments that aren't section markers
            if (trimmed.StartsWith("@"))
            {
                // Detect section changes from comment headers
                if (trimmed.Contains("Hiragana"))
                {
                    section = "hiragana";
                }
                else if (trimmed.Contains("Katakana"))
                {
                    section = "katakana";
                }
                else if (trimmed.Contains("punctuation"))
                {
                    section = "latin"; // Japanese punctuation still maps to same indices
                }
                continue;
            }

            // Parse character mapping: 'char' = HEX (single character to single glyph)
            var match = Regex.Match(trimmed, @"^'(.)'(?:\s+@.*?)?\s*=\s*([0-9A-Fa-f]+)$");
            if (match.Success)
            {
                var charValue = match.Groups[1].Value;
                var index = Convert.ToInt32(match.Groups[2].Value, 16);

                switch (section)
                {
                    case "hiragana":
                        hiraganaMappings[charValue] = index;
                        break;
                    case "katakana":
                        katakanaMappings[charValue] = index;
                        break;
                    default:
                        latinMappings[charValue] = index;
                        break;
                }
                continue;
            }

            // Parse special symbols: SYMBOL = HEX [HEX...] (multi-glyph sequences that map to font sprites)
            // Only include actual font glyph symbols, not control codes, colors, sounds, music, etc.
            match = Regex.Match(trimmed, @"^([A-Z_][A-Z0-9_]*)\s*=\s*([0-9A-Fa-f]+(?:\s+[0-9A-Fa-f]+)*)");
            if (match.Success)
            {
                var symbol = match.Groups[1].Value;

                // Whitelist of special symbols that are actual font glyphs
                // Everything after SUPER_RE in charmap.txt is control codes, colors, sounds, music, phonemes
                var fontGlyphSymbols = new HashSet<string>
                {
                    "SUPER_ER", "LV", "V_D_ARROW", "NBSP",
                    "PK", "PKMN", "POKEBLOCK",
                    "UNK_SPACER", "UP_ARROW", "DOWN_ARROW", "LEFT_ARROW", "RIGHT_ARROW",
                    "SUPER_E", "SUPER_RE"
                };

                if (!fontGlyphSymbols.Contains(symbol))
                {
                    continue;
                }

                var hexValues = match.Groups[2].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var indices = hexValues.Select(h => Convert.ToInt32(h, 16)).ToArray();

                specialSymbols[symbol] = indices;
            }
        }

        return new CharacterMap(latinMappings, hiraganaMappings, katakanaMappings, specialSymbols);
    }

    private void GenerateCharacterMapDefinition(CharacterMap charMap)
    {
        var outputDefDir = Path.Combine(_outputPath, "Definitions/Assets/Fonts");
        Directory.CreateDirectory(outputDefDir);

        // Latin character map with special symbols (multi-glyph sequences like PKMN)
        // Use integers directly as glyph indices
        var latinDef = new
        {
            id = $"{IdTransformer.Namespace}:charmap:latin",
            name = "Latin Character Map",
            type = "CharacterMap",
            mappings = charMap.LatinMappings,
            specialSymbols = charMap.SpecialSymbols
        };

        var latinPath = Path.Combine(outputDefDir, "CharacterMapLatin.json");
        File.WriteAllText(latinPath, JsonSerializer.Serialize(latinDef, new JsonSerializerOptions { WriteIndented = true }));

        // Japanese character map
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

        var japanesePath = Path.Combine(outputDefDir, "CharacterMapJapanese.json");
        File.WriteAllText(japanesePath, JsonSerializer.Serialize(japaneseDef, new JsonSerializerOptions { WriteIndented = true }));
    }

    private Dictionary<string, int[]> ParseGlyphWidths(string filePath)
    {
        var result = new Dictionary<string, int[]>();

        if (!File.Exists(filePath))
        {
            Console.WriteLine("[FontExtractor] fonts.c not found");
            return result;
        }

        var content = File.ReadAllText(filePath);

        // Match glyph width arrays: const u8 gFontXxxGlyphWidths[] = { ... };
        var pattern = new Regex(
            @"const\s+u8\s+(gFont\w+GlyphWidths)\[\]\s*=\s*\{([^}]+)\}",
            RegexOptions.Singleline);

        foreach (Match match in pattern.Matches(content))
        {
            var arrayName = match.Groups[1].Value;
            var valuesStr = match.Groups[2].Value;

            // Parse comma-separated integers
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
