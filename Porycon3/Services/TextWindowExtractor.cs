using System.Text.Json;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Porycon3.Infrastructure;
using Porycon3.Services.Extraction;
using static Porycon3.Infrastructure.StringUtilities;

namespace Porycon3.Services;

/// <summary>
/// Extracts text window graphics from pokeemerald and processes them.
/// Copies text window tile sheets with proper transparency.
/// </summary>
public class TextWindowExtractor : ExtractorBase
{
    public override string Name => "Text Windows";
    public override string Description => "Extracts text window tile sheets";

    private readonly string _emeraldGraphics;

    public TextWindowExtractor(string inputPath, string outputPath, bool verbose = false)
        : base(inputPath, outputPath, verbose)
    {
        _emeraldGraphics = Path.Combine(inputPath, "graphics", "text_window");
    }

    protected override int ExecuteExtraction()
    {
        if (!Directory.Exists(_emeraldGraphics))
        {
            AddError("", $"Text window graphics not found: {_emeraldGraphics}");
            return 0;
        }

        // Find all PNG files in text_window directory
        var pngFiles = Directory.GetFiles(_emeraldGraphics, "*.png").ToList();

        int count = 0;

        WithProgress("Extracting text windows", pngFiles, (pngFile, task) =>
        {
            var filename = Path.GetFileNameWithoutExtension(pngFile);
            SetTaskDescription(task, $"[cyan]Extracting[/] [yellow]{filename}[/]");

            if (ExtractTextWindow(pngFile))
                count++;
        });

        return count;
    }

    /// <summary>
    /// Extract a single text window graphic and apply transparency.
    /// </summary>
    private bool ExtractTextWindow(string sourceFile)
    {
        var filename = Path.GetFileNameWithoutExtension(sourceFile);

        try
        {
            // Load with proper index 0 transparency (GBA palette index 0 = transparent)
            var img = IndexedPngLoader.LoadWithIndex0Transparency(sourceFile);
            if (img == null)
            {
                AddError(filename, "Failed to load indexed PNG");
                return false;
            }

            using (img)
            {
                // Apply magenta transparency as additional fallback
                ApplyMagentaTransparency(img);

                // Create PascalCase filename
                var pascalName = TextWindowToPascalCase(filename);

                // Save processed PNG as 32-bit RGBA
                var destPng = GetGraphicsPath("UI", "TextWindows", $"{pascalName}.png");
                IndexedPngLoader.SaveAsRgbaPng(img, destPng);

            // Get image dimensions
            var width = img.Width;
            var height = img.Height;

            // GBA text windows use 8x8 pixel tiles
            const int tileWidth = 8;
            const int tileHeight = 8;

            // Calculate grid dimensions
            var tilesPerRow = width / tileWidth;
            var tilesPerCol = height / tileHeight;
            var tileCount = tilesPerRow * tilesPerCol;

            // Generate tile definitions for all tiles in the sheet
            var tiles = new List<object>();
            for (int tileIdx = 0; tileIdx < tileCount; tileIdx++)
            {
                int row = tileIdx / tilesPerRow;
                int col = tileIdx % tilesPerRow;
                tiles.Add(new
                {
                    index = tileIdx,
                    x = col * tileWidth,
                    y = row * tileHeight,
                    width = tileWidth,
                    height = tileHeight
                });
            }

            // Create JSON definition - using TileSheet format
            var unifiedId = $"{IdTransformer.Namespace}:textwindow:tilesheet/{IdTransformer.Normalize(filename)}";
            var jsonDef = new
            {
                id = unifiedId,
                displayName = FormatTextWindowDisplayName(filename),
                type = "TileSheet",
                texturePath = $"Graphics/UI/TextWindows/{pascalName}.png",
                tileWidth,
                tileHeight,
                tileCount,
                tiles,
                description = "Text window tile sheet from Pokemon Emerald (GBA tile-based rendering)"
            };

                // Save definition JSON
                var destJson = GetDefinitionPath("UI", "TextWindows", $"{pascalName}.json");
                File.WriteAllText(destJson, JsonSerializer.Serialize(jsonDef, JsonOptions.Default));

                LogVerbose($"Extracted {pascalName} ({tileCount} tiles)");
                return true;
            }
        }
        catch (Exception e)
        {
            AddError(filename, $"Failed to extract: {e.Message}", e);
            return false;
        }
    }

    /// <summary>
    /// Apply transparency for magenta (#FF00FF) pixels.
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
                    if (row[x].R == 255 && row[x].G == 0 && row[x].B == 255 && row[x].A > 0)
                    {
                        row[x] = new Rgba32(0, 0, 0, 0);
                    }
                }
            }
        });
    }

    private static string FormatTextWindowDisplayName(string name)
    {
        // Handle numeric names like "1", "2", etc.
        if (int.TryParse(name, out var num))
        {
            return $"Text Window {num}";
        }

        // Use the shared implementation for snake_case names
        return FormatDisplayName(name);
    }

    private static string TextWindowToPascalCase(string name)
    {
        // Handle numeric names - prefix with "Window"
        if (int.TryParse(name, out _))
        {
            return $"Window{name}";
        }

        // Use the shared implementation for snake_case names
        return ToPascalCase(name);
    }
}
