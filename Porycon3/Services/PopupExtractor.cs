using System.Text.Json;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Porycon3.Infrastructure;
using Porycon3.Services.Extraction;
using static Porycon3.Infrastructure.StringUtilities;

namespace Porycon3.Services;

/// <summary>
/// Extracts map popup graphics from pokeemerald and processes them.
/// Copies backgrounds and outline tile sheets with proper transparency.
/// </summary>
public class PopupExtractor : ExtractorBase
{
    public override string Name => "Popup Graphics";
    public override string Description => "Extracts map popup backgrounds and outlines";

    private readonly string _emeraldGraphics;

    public PopupExtractor(string inputPath, string outputPath, bool verbose = false)
        : base(inputPath, outputPath, verbose)
    {
        _emeraldGraphics = Path.Combine(inputPath, "graphics", "map_popup");
    }

    protected override int ExecuteExtraction()
    {
        if (!Directory.Exists(_emeraldGraphics))
        {
            AddError("", $"Map popup graphics not found: {_emeraldGraphics}");
            return 0;
        }

        int bgCount = 0;
        int outlineCount = 0;

        // Discover popup styles
        List<string> popupStyles = [];
        WithStatus("Discovering popup styles...", _ =>
        {
            popupStyles = DiscoverPopupStyles();
        });

        WithProgress("Extracting popup graphics", popupStyles, (styleName, task) =>
        {
            SetTaskDescription(task, $"[cyan]Extracting[/] [yellow]{styleName}[/]");

            // Process background
            if (ExtractBackground(styleName))
                bgCount++;

            // Process outline (with transparency)
            if (ExtractOutline(styleName))
                outlineCount++;
        });

        SetCount("Backgrounds", bgCount);
        SetCount("Outlines", outlineCount);
        return bgCount + outlineCount;
    }

    /// <summary>
    /// Discover available popup styles in pokeemerald.
    /// </summary>
    private List<string> DiscoverPopupStyles()
    {
        var styles = new HashSet<string>();

        if (Directory.Exists(_emeraldGraphics))
        {
            var pngFiles = Directory.GetFiles(_emeraldGraphics, "*.png");

            foreach (var pngFile in pngFiles)
            {
                var filename = Path.GetFileNameWithoutExtension(pngFile);

                // Check for outline pattern first (e.g., wood_outline.png)
                if (filename.Contains("_outline"))
                {
                    styles.Add(filename.Replace("_outline", ""));
                }
                else if (filename.Contains("_border") || filename.Contains("_frame"))
                {
                    styles.Add(filename.Replace("_border", "").Replace("_frame", ""));
                }
                else if (filename.Contains("_bg") || filename.Contains("_background"))
                {
                    styles.Add(filename.Replace("_bg", "").Replace("_background", ""));
                }
                else
                {
                    // Check if there's a corresponding outline file to confirm
                    var possibleOutline = Path.Combine(_emeraldGraphics, $"{filename}_outline.png");
                    if (File.Exists(possibleOutline))
                    {
                        styles.Add(filename);
                    }
                }
            }
        }

        var stylesList = styles.OrderBy(s => s).ToList();

        // Fallback: use default pokeemerald styles
        if (stylesList.Count == 0)
        {
            LogWarning("No popup graphics found, using defaults");
            stylesList = new List<string> { "wood", "stone", "brick", "marble", "underwater", "stone2" };
        }

        LogVerbose($"Discovered {stylesList.Count} popup styles: {string.Join(", ", stylesList)}");
        return stylesList;
    }

    /// <summary>
    /// Extract and copy a background texture.
    /// </summary>
    private bool ExtractBackground(string styleName)
    {
        // Try various naming conventions
        var possibleNames = new[]
        {
            $"{styleName}.png",              // Standard: wood.png
            $"{styleName}_bg.png",           // Alternate: wood_bg.png
            $"{styleName}_background.png",   // Alternate: wood_background.png
        };

        string? sourceFile = null;
        foreach (var name in possibleNames)
        {
            var candidate = Path.Combine(_emeraldGraphics, name);
            if (File.Exists(candidate))
            {
                sourceFile = candidate;
                break;
            }
        }

        if (sourceFile == null)
            return false;

        // Load and convert PNG with PascalCase filename, applying GBA transparency
        var pascalName = ToPascalCase(styleName);
        var destPng = GetGraphicsPath("UI", "Popups", "Backgrounds", $"{pascalName}.png");

        try
        {
            var img = IndexedPngLoader.LoadWithIndex0Transparency(sourceFile);
            if (img == null)
            {
                AddError(styleName, "Failed to load indexed PNG");
                return false;
            }
            using (img)
            {
                IndexedPngLoader.SaveAsRgbaPng(img, destPng);
            }
        }
        catch (Exception e)
        {
            AddError(styleName, $"Failed to copy background: {e.Message}", e);
            return false;
        }

        // Create JSON definition for background bitmap
        var unifiedId = $"{IdTransformer.Namespace}:popup:background/{styleName}";
        var jsonDef = new
        {
            id = unifiedId,
            name = FormatDisplayName(styleName),
            type = "Bitmap",
            texturePath = $"Graphics/UI/Popups/Backgrounds/{pascalName}.png",
            width = 80,
            height = 24,
            description = "Background bitmap for map popup"
        };

        var destJson = GetDefinitionPath("UI", "Popups", "Backgrounds", $"{pascalName}.json");
        File.WriteAllText(destJson, JsonSerializer.Serialize(jsonDef, JsonOptions.Default));

        LogVerbose($"Extracted background: {pascalName}");
        return true;
    }

    /// <summary>
    /// Extract an outline tile sheet and convert transparency.
    /// </summary>
    private bool ExtractOutline(string styleName)
    {
        // Try various naming conventions
        var possibleNames = new[]
        {
            $"{styleName}_outline.png",      // Standard: wood_outline.png
            $"{styleName}_border.png",       // Alternate: wood_border.png
            $"{styleName}_frame.png",        // Alternate: wood_frame.png
        };

        string? sourceFile = null;
        foreach (var name in possibleNames)
        {
            var candidate = Path.Combine(_emeraldGraphics, name);
            if (File.Exists(candidate))
            {
                sourceFile = candidate;
                break;
            }
        }

        if (sourceFile == null)
            return false;

        // Load and convert PNG with PascalCase filename
        var pascalName = ToPascalCase(styleName);
        var destPng = GetGraphicsPath("UI", "Popups", "Outlines", $"{pascalName}.png");

        try
        {
            var img = IndexedPngLoader.LoadWithIndex0Transparency(sourceFile);
            if (img == null)
            {
                AddError(styleName, "Failed to load indexed PNG");
                return false;
            }
            using (img)
            {
                IndexedPngLoader.SaveAsRgbaPng(img, destPng);
            }
        }
        catch (Exception e)
        {
            AddError(styleName, $"Failed to copy outline: {e.Message}", e);
            return false;
        }

        // Generate tile definitions for all 30 tiles (10x3 grid, 8x8 each)
        var tiles = new List<object>();
        for (int tileIdx = 0; tileIdx < 30; tileIdx++)
        {
            int row = tileIdx / 10;
            int col = tileIdx % 10;
            tiles.Add(new
            {
                index = tileIdx,
                x = col * 8,
                y = row * 8,
                width = 8,
                height = 8
            });
        }

        // Create JSON definition
        var unifiedId = $"{IdTransformer.Namespace}:popup:outline/{styleName}";
        var jsonDef = new
        {
            id = unifiedId,
            name = $"{FormatDisplayName(styleName)} Outline",
            type = "TileSheet",
            texturePath = $"Graphics/UI/Popups/Outlines/{pascalName}.png",
            tileWidth = 8,
            tileHeight = 8,
            tileCount = 30,
            tiles,
            tileUsage = new
            {
                topEdge = Enumerable.Range(0, 12).ToList(),
                leftEdge = new List<int> { 12, 14, 16 },
                rightEdge = new List<int> { 13, 15, 17 },
                bottomEdge = Enumerable.Range(18, 12).ToList()
            },
            description = "9-patch frame tile sheet for map popup (GBA tile-based rendering)"
        };

        var destJson = GetDefinitionPath("UI", "Popups", "Outlines", $"{pascalName}.json");
        File.WriteAllText(destJson, JsonSerializer.Serialize(jsonDef, JsonOptions.Default));

        LogVerbose($"Extracted outline: {pascalName}");
        return true;
    }
}
