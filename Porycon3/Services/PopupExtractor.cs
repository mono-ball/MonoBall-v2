using System.Text.Json;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Porycon3.Services;

/// <summary>
/// Extracts map popup graphics from pokeemerald and processes them.
/// Copies backgrounds and outline tile sheets with proper transparency.
/// Matches porycon2's popup_extractor.py output format.
/// </summary>
public class PopupExtractor
{
    private readonly string _inputPath;
    private readonly string _outputPath;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    // Pokeemerald paths
    private readonly string _emeraldGraphics;

    // Output paths
    private readonly string _outputGraphicsBg;
    private readonly string _outputGraphicsOutline;
    private readonly string _outputDataBg;
    private readonly string _outputDataOutline;

    public PopupExtractor(string inputPath, string outputPath)
    {
        _inputPath = inputPath;
        _outputPath = outputPath;

        _emeraldGraphics = Path.Combine(inputPath, "graphics", "map_popup");
        _outputGraphicsBg = Path.Combine(outputPath, "Graphics", "Sprites", "Popups", "Backgrounds");
        _outputGraphicsOutline = Path.Combine(outputPath, "Graphics", "Sprites", "Popups", "Outlines");
        _outputDataBg = Path.Combine(outputPath, "Definitions", "Sprites", "Popups", "Backgrounds");
        _outputDataOutline = Path.Combine(outputPath, "Definitions", "Sprites", "Popups", "Outlines");
    }

    /// <summary>
    /// Extract all popup graphics from pokeemerald.
    /// </summary>
    /// <returns>Tuple of (backgrounds_extracted, outlines_extracted)</returns>
    public (int Backgrounds, int Outlines) ExtractAll()
    {
        if (!Directory.Exists(_emeraldGraphics))
        {
            Console.WriteLine($"[PopupExtractor] Map popup graphics not found: {_emeraldGraphics}");
            return (0, 0);
        }

        // Create output directories
        Directory.CreateDirectory(_outputGraphicsBg);
        Directory.CreateDirectory(_outputGraphicsOutline);
        Directory.CreateDirectory(_outputDataBg);
        Directory.CreateDirectory(_outputDataOutline);

        int bgCount = 0;
        int outlineCount = 0;

        // Discover popup styles
        var popupStyles = DiscoverPopupStyles();

        foreach (var styleName in popupStyles)
        {
            // Process background
            if (ExtractBackground(styleName))
                bgCount++;

            // Process outline (with transparency)
            if (ExtractOutline(styleName))
                outlineCount++;
        }

        Console.WriteLine($"[PopupExtractor] Extracted {bgCount} backgrounds and {outlineCount} outlines");
        return (bgCount, outlineCount);
    }

    /// <summary>
    /// Discover available popup styles in pokeemerald.
    /// Looks for common naming patterns in graphics/map_popup/
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
            Console.WriteLine("[PopupExtractor] No popup graphics found in map_popup folder, using defaults");
            stylesList = new List<string> { "wood", "stone", "brick", "marble", "underwater", "stone2" };
        }

        Console.WriteLine($"[PopupExtractor] Discovered {stylesList.Count} popup styles: {string.Join(", ", stylesList)}");
        return stylesList;
    }

    /// <summary>
    /// Extract and copy a background texture.
    /// </summary>
    private bool ExtractBackground(string styleName)
    {
        // Try various naming conventions (pokeemerald uses just {style}.png for backgrounds)
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
        {
            return false;
        }

        // Copy PNG with PascalCase filename
        var pascalName = ToPascalCase(styleName);
        var destPng = Path.Combine(_outputGraphicsBg, $"{pascalName}.png");
        try
        {
            using var img = Image.Load<Rgba32>(sourceFile);
            img.SaveAsPng(destPng);
        }
        catch (Exception e)
        {
            Console.WriteLine($"[PopupExtractor] Failed to copy background {styleName}: {e.Message}");
            return false;
        }

        // Create JSON definition for background bitmap with unified ID
        var unifiedId = $"base:popup:background/{styleName}";
        var jsonDef = new
        {
            id = unifiedId,
            name = FormatDisplayName(styleName),
            type = "Bitmap",
            texturePath = $"Graphics/Sprites/Popups/Backgrounds/{pascalName}.png",
            width = 80,
            height = 24,
            description = "Background bitmap for map popup"
        };

        var destJson = Path.Combine(_outputDataBg, $"{pascalName}.json");
        try
        {
            File.WriteAllText(destJson, JsonSerializer.Serialize(jsonDef, JsonOptions));
        }
        catch (Exception e)
        {
            Console.WriteLine($"[PopupExtractor] Failed to create background definition {styleName}: {e.Message}");
            return false;
        }

        return true;
    }

    /// <summary>
    /// Extract an outline tile sheet and convert transparency.
    /// </summary>
    private bool ExtractOutline(string styleName)
    {
        // Try various naming conventions (pokeemerald uses {style}_outline.png)
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
        {
            return false;
        }

        // Load and convert PNG with PascalCase filename (no _outline suffix)
        // Note: These are tile sheets (10x3 = 30 tiles of 8x8 pixels), not 9-slice sprites!
        // Palette index 0 is transparent in GBA
        var pascalName = ToPascalCase(styleName);
        var destPng = Path.Combine(_outputGraphicsOutline, $"{pascalName}.png");
        try
        {
            using var img = Image.Load<Rgba32>(sourceFile);

            // For indexed images, we need to ensure transparency is applied
            // The first color (index 0) in GBA palettes is typically the transparent color
            // We handle this by checking if the pixel matches the first pixel (likely transparent color)
            var transparentColor = img[0, 0];

            // Make the transparent color actually transparent
            img.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < accessor.Height; y++)
                {
                    var row = accessor.GetRowSpan(y);
                    for (int x = 0; x < row.Length; x++)
                    {
                        if (row[x] == transparentColor)
                        {
                            row[x] = new Rgba32(0, 0, 0, 0);
                        }
                    }
                }
            });

            img.SaveAsPng(destPng);
        }
        catch (Exception e)
        {
            Console.WriteLine($"[PopupExtractor] Failed to copy outline {styleName}: {e.Message}");
            return false;
        }

        // Create JSON definition for outline tile sheet
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

        // Create JSON definition with unified ID (no _outline suffix)
        var unifiedId = $"base:popup:outline/{styleName}";
        var jsonDef = new
        {
            id = unifiedId,
            name = $"{FormatDisplayName(styleName)} Outline",
            type = "TileSheet",
            texturePath = $"Graphics/Sprites/Popups/Outlines/{pascalName}.png",
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

        var destJson = Path.Combine(_outputDataOutline, $"{pascalName}.json");
        try
        {
            File.WriteAllText(destJson, JsonSerializer.Serialize(jsonDef, JsonOptions));
        }
        catch (Exception e)
        {
            Console.WriteLine($"[PopupExtractor] Failed to create outline definition {styleName}: {e.Message}");
            return false;
        }

        return true;
    }

    private static string FormatDisplayName(string name)
    {
        return string.Join(" ", name.Split('_').Select(w =>
            w.Length > 0 ? char.ToUpper(w[0]) + w[1..].ToLower() : w));
    }

    private static string ToPascalCase(string name)
    {
        // Convert wood to Wood, bw_default to BwDefault
        return string.Concat(name.Split('_').Select(w =>
            w.Length > 0 ? char.ToUpper(w[0]) + w[1..].ToLower() : w));
    }
}
