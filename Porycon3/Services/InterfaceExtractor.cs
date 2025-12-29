using System.Text.Json;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

namespace Porycon3.Services;

/// <summary>
/// Extracts interface graphics from pokeemerald graphics/interface directory.
/// </summary>
public class InterfaceExtractor
{
    private readonly string _inputPath;
    private readonly string _outputPath;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly string _emeraldGraphics;
    private readonly string _outputGraphics;
    private readonly string _outputData;

    public InterfaceExtractor(string inputPath, string outputPath)
    {
        _inputPath = inputPath;
        _outputPath = outputPath;

        _emeraldGraphics = Path.Combine(inputPath, "graphics", "interface");
        _outputGraphics = Path.Combine(outputPath, "Graphics", "UI", "Interface");
        _outputData = Path.Combine(outputPath, "Definitions", "Assets", "UI", "Interface");
    }

    /// <summary>
    /// Extract all interface graphics from pokeemerald.
    /// </summary>
    public int ExtractAll()
    {
        if (!Directory.Exists(_emeraldGraphics))
        {
            Console.WriteLine($"[InterfaceExtractor] Interface graphics not found: {_emeraldGraphics}");
            return 0;
        }

        Directory.CreateDirectory(_outputGraphics);
        Directory.CreateDirectory(_outputData);

        var count = 0;

        // Interface graphics with frame metadata for sprite sheets
        var interfaceGraphics = new (string fileName, string displayName, int? frameWidth, int? frameHeight, int? frameCount)[]
        {
            ("arrow_cursor", "Arrow Cursor", 8, 8, 1),
            ("blank", "Blank", null, null, null),
            ("category_icons", "Category Icons", 32, 16, 3),
            ("menu_info", "Menu Info", null, null, null),
            ("mon_markings", "Mon Markings", 8, 8, 6),
            ("mon_markings_menu", "Mon Markings Menu", null, null, null),
            ("mystery_gift_textbox_border", "Mystery Gift Textbox Border", null, null, null),
            ("option_menu_equals_sign", "Option Menu Equals Sign", null, null, null),
            ("outline_cursor", "Outline Cursor", null, null, null),
            ("scroll_indicator", "Scroll Indicator", 8, 8, 2),
            ("status_icons", "Status Icons", 16, 8, 7),
            ("swap_line", "Swap Line", null, null, null),
            ("ui_learn_move", "UI Learn Move", null, null, null),
        };

        foreach (var (fileName, displayName, frameWidth, frameHeight, frameCount) in interfaceGraphics)
        {
            if (ExtractInterfaceGraphic(fileName, displayName, frameWidth, frameHeight, frameCount))
                count++;
        }

        Console.WriteLine($"[InterfaceExtractor] Extracted {count} interface graphics");
        return count;
    }

    private bool ExtractInterfaceGraphic(string fileName, string displayName, int? frameWidth, int? frameHeight, int? frameCount)
    {
        var pngPath = Path.Combine(_emeraldGraphics, $"{fileName}.png");
        if (!File.Exists(pngPath)) return false;

        try
        {
            var pascalName = ToPascalCase(fileName);

            // Load and apply transparency using corner color method
            using var img = LoadWithCornerTransparency(pngPath);

            // Save processed PNG
            var outputPngPath = Path.Combine(_outputGraphics, $"{pascalName}.png");
            SaveAsRgbaPng(img, outputPngPath);

            // Create definition
            object definition;
            if (frameWidth.HasValue && frameHeight.HasValue && frameCount.HasValue)
            {
                definition = new
                {
                    id = $"{IdTransformer.Namespace}:sprite:ui/interface/{fileName.Replace("_", "-")}",
                    name = displayName,
                    type = "Sprite",
                    texturePath = $"Graphics/UI/Interface/{pascalName}.png",
                    width = img.Width,
                    height = img.Height,
                    frameWidth = frameWidth.Value,
                    frameHeight = frameHeight.Value,
                    frameCount = frameCount.Value
                };
            }
            else
            {
                definition = new
                {
                    id = $"{IdTransformer.Namespace}:sprite:ui/interface/{fileName.Replace("_", "-")}",
                    name = displayName,
                    type = "Sprite",
                    texturePath = $"Graphics/UI/Interface/{pascalName}.png",
                    width = img.Width,
                    height = img.Height
                };
            }

            var defPath = Path.Combine(_outputData, $"{pascalName}.json");
            File.WriteAllText(defPath, JsonSerializer.Serialize(definition, JsonOptions));

            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[InterfaceExtractor] Failed to extract {fileName}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Load PNG and make the corner (0,0) color transparent.
    /// Interface graphics use various palette backgrounds.
    /// </summary>
    private static Image<Rgba32> LoadWithCornerTransparency(string pngPath)
    {
        var img = Image.Load<Rgba32>(pngPath);
        var transparentColor = img[0, 0];

        img.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    if (row[x].R == transparentColor.R &&
                        row[x].G == transparentColor.G &&
                        row[x].B == transparentColor.B)
                    {
                        row[x] = new Rgba32(0, 0, 0, 0);
                    }
                }
            }
        });

        return img;
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

    private static string ToPascalCase(string snakeCase)
    {
        return string.Concat(snakeCase.Split('_')
            .Select(w => w.Length > 0 ? char.ToUpper(w[0]) + w[1..].ToLower() : w));
    }
}
