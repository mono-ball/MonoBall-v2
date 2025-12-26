using System.Text.Json;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

namespace Porycon3.Services;

/// <summary>
/// Extracts text window graphics from pokeemerald and processes them.
/// Copies text window tile sheets with proper transparency.
/// Matches porycon2's text_window_extractor.py output format.
/// </summary>
public class TextWindowExtractor
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
    private readonly string _outputGraphics;
    private readonly string _outputData;

    public TextWindowExtractor(string inputPath, string outputPath)
    {
        _inputPath = inputPath;
        _outputPath = outputPath;

        _emeraldGraphics = Path.Combine(inputPath, "graphics", "text_window");
        _outputGraphics = Path.Combine(outputPath, "Graphics", "UI", "TextWindows");
        _outputData = Path.Combine(outputPath, "Definitions", "Assets", "UI", "TextWindows");
    }

    /// <summary>
    /// Extract all text window graphics from pokeemerald.
    /// </summary>
    /// <returns>Number of text windows extracted</returns>
    public int ExtractAll()
    {
        if (!Directory.Exists(_emeraldGraphics))
        {
            Console.WriteLine($"[TextWindowExtractor] Text window graphics not found: {_emeraldGraphics}");
            return 0;
        }

        // Create output directories
        Directory.CreateDirectory(_outputGraphics);
        Directory.CreateDirectory(_outputData);

        int count = 0;

        // Find all PNG files in text_window directory
        var pngFiles = Directory.GetFiles(_emeraldGraphics, "*.png");
        Console.WriteLine($"[TextWindowExtractor] Found {pngFiles.Length} PNG files in text_window directory");

        foreach (var pngFile in pngFiles)
        {
            if (ExtractTextWindow(pngFile))
                count++;
        }

        Console.WriteLine($"[TextWindowExtractor] Extracted {count} text window graphics");
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
            using var img = LoadWithIndex0Transparency(sourceFile);

            // Create PascalCase filename
            var pascalName = ToPascalCase(filename);

            // Save processed PNG as 32-bit RGBA
            var destPng = Path.Combine(_outputGraphics, $"{pascalName}.png");
            SaveAsRgbaPng(img, destPng);

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
            var unifiedId = $"base:textwindow:tilesheet/{IdTransformer.Normalize(filename)}";
            var jsonDef = new
            {
                id = unifiedId,
                displayName = FormatDisplayName(filename),
                type = "TileSheet",
                texturePath = $"Graphics/UI/TextWindows/{pascalName}.png",
                tileWidth,
                tileHeight,
                tileCount,
                tiles,
                description = "Text window tile sheet from Pokemon Emerald (GBA tile-based rendering)"
            };

            // Save definition JSON
            var destJson = Path.Combine(_outputData, $"{pascalName}.json");
            File.WriteAllText(destJson, JsonSerializer.Serialize(jsonDef, JsonOptions));

            return true;
        }
        catch (Exception e)
        {
            Console.WriteLine($"[TextWindowExtractor] Failed to extract text window {filename}: {e.Message}");
            return false;
        }
    }

    /// <summary>
    /// Load an indexed PNG and convert to RGBA with palette index 0 as transparent.
    /// This is how GBA/pokeemerald handles sprite transparency.
    /// </summary>
    private static Image<Rgba32> LoadWithIndex0Transparency(string pngPath)
    {
        // Read raw PNG bytes to extract palette
        var bytes = File.ReadAllBytes(pngPath);

        // Extract palette from PNG PLTE chunk
        var palette = ExtractPngPalette(bytes);

        if (palette != null && palette.Length > 0)
        {
            // Make palette index 0 transparent
            palette[0] = new Rgba32(0, 0, 0, 0);

            // Load as RGBA and apply palette with index 0 transparency
            using var tempImage = Image.Load<Rgba32>(pngPath);

            // Get the color that was at palette index 0 (before we made it transparent)
            // We need to find all pixels with this color and make them transparent
            var index0Color = ExtractPaletteColor(bytes, 0);

            if (index0Color.HasValue)
            {
                var bgColor = index0Color.Value;
                tempImage.ProcessPixelRows(accessor =>
                {
                    for (int y = 0; y < accessor.Height; y++)
                    {
                        var row = accessor.GetRowSpan(y);
                        for (int x = 0; x < row.Length; x++)
                        {
                            // Check if pixel matches palette index 0 color
                            if (row[x].R == bgColor.R && row[x].G == bgColor.G && row[x].B == bgColor.B)
                            {
                                row[x] = new Rgba32(0, 0, 0, 0);
                            }
                        }
                    }
                });
            }

            // Also apply magenta transparency as fallback
            ApplyMagentaTransparency(tempImage);

            return tempImage.Clone();
        }

        // Fallback: load as RGBA and use first pixel as background color
        var img = Image.Load<Rgba32>(pngPath);
        var firstPixel = img[0, 0];

        // Make all pixels matching first pixel transparent
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

        // Also apply magenta transparency
        ApplyMagentaTransparency(img);

        return img;
    }

    /// <summary>
    /// Extract RGB palette from PNG PLTE chunk.
    /// </summary>
    private static Rgba32[]? ExtractPngPalette(byte[] pngData)
    {
        // Find PLTE chunk
        // PNG structure: 8-byte signature, then chunks (4-byte length, 4-byte type, data, 4-byte CRC)
        var pos = 8; // Skip PNG signature

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

            pos += 12 + length; // 4 length + 4 type + data + 4 CRC
        }

        return null;
    }

    /// <summary>
    /// Extract a specific palette color from PNG PLTE chunk.
    /// </summary>
    private static Rgba32? ExtractPaletteColor(byte[] pngData, int index)
    {
        var pos = 8; // Skip PNG signature

        while (pos < pngData.Length - 12)
        {
            var length = (pngData[pos] << 24) | (pngData[pos + 1] << 16) |
                         (pngData[pos + 2] << 8) | pngData[pos + 3];
            var type = System.Text.Encoding.ASCII.GetString(pngData, pos + 4, 4);

            if (type == "PLTE")
            {
                var colorCount = length / 3;
                if (index < colorCount)
                {
                    var offset = pos + 8 + index * 3;
                    return new Rgba32(pngData[offset], pngData[offset + 1], pngData[offset + 2], 255);
                }
                return null;
            }

            pos += 12 + length;
        }

        return null;
    }

    /// <summary>
    /// Apply transparency for magenta (#FF00FF) pixels, a common GBA transparency mask.
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
                    // Magenta (#FF00FF) is commonly used as transparency mask in GBA graphics
                    if (row[x].R == 255 && row[x].G == 0 && row[x].B == 255 && row[x].A > 0)
                    {
                        row[x] = new Rgba32(0, 0, 0, 0);
                    }
                }
            }
        });
    }

    /// <summary>
    /// Save image as 32-bit RGBA PNG (not indexed).
    /// </summary>
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

    private static string FormatDisplayName(string name)
    {
        // Handle numeric names like "1", "2", etc.
        if (int.TryParse(name, out var num))
        {
            return $"Text Window {num}";
        }

        // Handle snake_case names
        return string.Join(" ", name.Split('_').Select(w =>
            w.Length > 0 ? char.ToUpper(w[0]) + w[1..].ToLower() : w));
    }

    private static string ToPascalCase(string name)
    {
        // Handle numeric names - prefix with "Window"
        if (int.TryParse(name, out _))
        {
            return $"Window{name}";
        }

        // Convert snake_case to PascalCase
        return string.Concat(name.Split('_').Select(w =>
            w.Length > 0 ? char.ToUpper(w[0]) + w[1..].ToLower() : w));
    }
}
