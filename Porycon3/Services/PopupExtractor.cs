using System.Text.Json;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using Porycon3.Services.Extraction;

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
            using var img = LoadWithIndex0Transparency(sourceFile);
            SaveAsRgbaPng(img, destPng);
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
            using var img = LoadWithIndex0Transparency(sourceFile);
            SaveAsRgbaPng(img, destPng);
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

    private static string FormatDisplayName(string name)
    {
        return string.Join(" ", name.Split('_').Select(w =>
            w.Length > 0 ? char.ToUpper(w[0]) + w[1..].ToLower() : w));
    }

    private static string ToPascalCase(string name)
    {
        return string.Concat(name.Split('_').Select(w =>
            w.Length > 0 ? char.ToUpper(w[0]) + w[1..].ToLower() : w));
    }

    /// <summary>
    /// Load an indexed PNG and convert to RGBA with palette index 0 as transparent.
    /// </summary>
    private static Image<Rgba32> LoadWithIndex0Transparency(string pngPath)
    {
        var bytes = File.ReadAllBytes(pngPath);
        var palette = ExtractPngPalette(bytes);

        if (palette != null && palette.Length > 0)
        {
            var (indices, width, height, bitDepth) = ExtractPixelIndices(bytes);

            if (indices != null && width > 0 && height > 0)
            {
                var output = new Image<Rgba32>(width, height);
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
                            else
                            {
                                row[x] = new Rgba32(0, 0, 0, 255);
                            }
                        }
                    }
                });

                return output;
            }
        }

        // Fallback: load as RGBA and use first pixel as background color
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
            using var zlibStream = new System.IO.Compression.ZLibStream(compressedStream, System.IO.Compression.CompressionMode.Decompress);
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

        var srcPos = 0;
        for (int y = 0; y < height; y++)
        {
            if (srcPos >= decompressed.Length) break;

            var filterType = decompressed[srcPos++];
            var scanline = new byte[scanlineWidth];

            for (int i = 0; i < scanlineWidth && srcPos < decompressed.Length; i++)
            {
                var raw = decompressed[srcPos++];

                if (filterType == 1 && i > 0)
                {
                    raw = (byte)(raw + scanline[i - 1]);
                }

                scanline[i] = raw;
            }

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
