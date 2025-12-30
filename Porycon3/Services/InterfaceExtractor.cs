using System.Text.Json;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using Porycon3.Services.Extraction;
using static Porycon3.Infrastructure.StringUtilities;

namespace Porycon3.Services;

/// <summary>
/// Extracts interface graphics from pokeemerald graphics/interface directory.
/// </summary>
public class InterfaceExtractor : ExtractorBase
{
    public override string Name => "Interface Graphics";
    public override string Description => "Extracts UI interface graphics and sprites";

    // Interface graphics with frame metadata for sprite sheets
    private static readonly (string fileName, string displayName, int? frameWidth, int? frameHeight, int? frameCount)[] InterfaceGraphics =
    [
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
    ];

    public InterfaceExtractor(string inputPath, string outputPath, bool verbose = false)
        : base(inputPath, outputPath, verbose)
    {
    }

    protected override int ExecuteExtraction()
    {
        var emeraldGraphics = Path.Combine(InputPath, "graphics", "interface");

        if (!Directory.Exists(emeraldGraphics))
        {
            AddError("", $"Interface graphics not found: {emeraldGraphics}");
            return 0;
        }

        int count = 0;

        WithProgress("Extracting interface graphics", InterfaceGraphics.ToList(), (item, task) =>
        {
            var (fileName, displayName, frameWidth, frameHeight, frameCount) = item;
            SetTaskDescription(task, $"[cyan]Extracting[/] [yellow]{displayName}[/]");

            if (ExtractInterfaceGraphic(emeraldGraphics, fileName, displayName, frameWidth, frameHeight, frameCount))
                count++;
        });

        return count;
    }

    private bool ExtractInterfaceGraphic(string emeraldGraphics, string fileName, string displayName,
        int? frameWidth, int? frameHeight, int? frameCount)
    {
        var pngPath = Path.Combine(emeraldGraphics, $"{fileName}.png");
        if (!File.Exists(pngPath))
        {
            LogWarning($"File not found: {fileName}.png");
            return false;
        }

        var pascalName = ToPascalCase(fileName);

        // Load and apply transparency using corner color method
        using var img = LoadWithCornerTransparency(pngPath);

        // Save processed PNG
        var outputPngPath = GetGraphicsPath("UI", "Interface", $"{pascalName}.png");
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

        var defPath = GetDefinitionPath("UI", "Interface", $"{pascalName}.json");
        File.WriteAllText(defPath, JsonSerializer.Serialize(definition, JsonOptions.Default));

        LogVerbose($"Extracted {pascalName}");
        return true;
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
}
