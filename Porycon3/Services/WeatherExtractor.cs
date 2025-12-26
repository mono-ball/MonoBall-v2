using System.Text.Json;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

namespace Porycon3.Services;

/// <summary>
/// Extracts weather particle graphics from pokeemerald-expansion.
/// Converts 4bpp indexed PNGs to RGBA with proper palette and transparency.
/// Outputs TileSheet format matching other graphics extractors.
/// </summary>
public class WeatherExtractor
{
    private const int TileSize = 8;

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

    // Weather graphics mapping: internal name -> source file(s), optional palette, and sprite info
    private static readonly Dictionary<string, WeatherGraphicsInfo> WeatherGraphics = new()
    {
        ["rain"] = new(new[] { "rain.png" }, null, 8, 24, "Raindrop particles"),
        ["snow"] = new(new[] { "snow0.png", "snow1.png" }, null, 8, 8, "Snowflake particles"),
        ["sandstorm"] = new(new[] { "sandstorm.png" }, null, 64, 64, "Sandstorm particles"),
        ["fog_horizontal"] = new(new[] { "fog_horizontal.png" }, "fog.pal", 256, 64, "Horizontal fog overlay"),
        ["fog_diagonal"] = new(new[] { "fog_diagonal.png" }, "fog.pal", 256, 256, "Diagonal fog overlay"),
        ["volcanic_ash"] = new(new[] { "ash.png" }, null, 8, 8, "Volcanic ash particles"),
        ["underwater_bubbles"] = new(new[] { "bubble.png" }, null, 8, 8, "Underwater bubble particles"),
        ["clouds"] = new(new[] { "cloud.png" }, null, 64, 64, "Cloud particles")
    };

    public WeatherExtractor(string inputPath, string outputPath)
    {
        _inputPath = inputPath;
        _outputPath = outputPath;

        _emeraldGraphics = Path.Combine(inputPath, "graphics", "weather");
        _outputGraphics = Path.Combine(outputPath, "Graphics", "Weather");
        _outputData = Path.Combine(outputPath, "Definitions", "Assets", "Weather");
    }

    public (int Graphics, int Definitions) ExtractAll()
    {
        if (!Directory.Exists(_emeraldGraphics))
        {
            Console.WriteLine($"[WeatherExtractor] Weather graphics not found: {_emeraldGraphics}");
            return (0, 0);
        }

        Directory.CreateDirectory(_outputGraphics);
        Directory.CreateDirectory(_outputData);

        int graphicsCount = 0;
        int definitionsCount = 0;

        foreach (var (weatherType, info) in WeatherGraphics)
        {
            var extracted = ExtractWeatherGraphics(weatherType, info);
            if (extracted)
            {
                graphicsCount += info.SourceFiles.Length;
                definitionsCount++;
            }
        }

        Console.WriteLine($"[WeatherExtractor] Extracted {graphicsCount} graphics for {definitionsCount} weather types");
        return (graphicsCount, definitionsCount);
    }

    private bool ExtractWeatherGraphics(string weatherType, WeatherGraphicsInfo info)
    {
        var pascalName = ToPascalCase(weatherType);
        var typeDir = Path.Combine(_outputGraphics, pascalName);
        Directory.CreateDirectory(typeDir);

        // Load palette if specified
        Rgba32[]? palette = null;
        if (info.PaletteName != null)
        {
            var palettePath = Path.Combine(_emeraldGraphics, info.PaletteName);
            palette = LoadJascPalette(palettePath);
        }

        var extractedTextures = new List<string>();
        var allTiles = new List<object>();
        int totalTileCount = 0;

        foreach (var sourceFile in info.SourceFiles)
        {
            var sourcePath = Path.Combine(_emeraldGraphics, sourceFile);
            if (!File.Exists(sourcePath))
                continue;

            var destFilename = ToPascalCase(Path.GetFileNameWithoutExtension(sourceFile)) + ".png";
            var destPath = Path.Combine(typeDir, destFilename);

            var (width, height) = RenderIndexedPng(sourcePath, destPath, palette);
            if (width > 0 && height > 0)
            {
                extractedTextures.Add($"Graphics/Weather/{pascalName}/{destFilename}");

                // Calculate tiles for this texture
                var tilesX = width / TileSize;
                var tilesY = height / TileSize;
                var tileCountForTexture = tilesX * tilesY;

                for (int i = 0; i < tileCountForTexture; i++)
                {
                    var col = i % tilesX;
                    var row = i / tilesX;
                    allTiles.Add(new
                    {
                        index = totalTileCount + i,
                        x = col * TileSize,
                        y = row * TileSize,
                        width = TileSize,
                        height = TileSize
                    });
                }
                totalTileCount += tileCountForTexture;
            }
        }

        if (extractedTextures.Count == 0)
            return false;

        // Create graphics definition with TileSheet format
        var definition = new Dictionary<string, object>
        {
            ["id"] = $"base:weather:graphics/{weatherType}",
            ["name"] = FormatDisplayName(weatherType),
            ["type"] = "TileSheet",
            ["texturePath"] = extractedTextures[0],
            ["tileWidth"] = TileSize,
            ["tileHeight"] = TileSize,
            ["tileCount"] = totalTileCount,
            ["tiles"] = allTiles,
            ["spriteWidth"] = info.SpriteWidth,
            ["spriteHeight"] = info.SpriteHeight,
            ["description"] = info.Description
        };

        // Include all textures if multiple files
        if (extractedTextures.Count > 1)
        {
            definition["textures"] = extractedTextures;
            definition["frameCount"] = extractedTextures.Count;
        }

        var defPath = Path.Combine(_outputData, $"{pascalName}.json");
        File.WriteAllText(defPath, JsonSerializer.Serialize(definition, JsonOptions));

        return true;
    }

    /// <summary>
    /// Load JASC-PAL palette file.
    /// </summary>
    private Rgba32[]? LoadJascPalette(string path)
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

            var palette = new Rgba32[16];
            for (int i = 0; i < 16; i++)
                palette[i] = new Rgba32(0, 0, 0, i == 0 ? (byte)0 : (byte)255);

            for (int i = 0; i < Math.Min(numColors, 16); i++)
            {
                var lineIndex = 3 + i;
                if (lineIndex >= lines.Length)
                    break;

                var parts = lines[lineIndex].Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 3 &&
                    int.TryParse(parts[0], out var r) &&
                    int.TryParse(parts[1], out var g) &&
                    int.TryParse(parts[2], out var b))
                {
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
    /// Render indexed PNG to RGBA with palette index 0 as transparent.
    /// Returns (width, height) of the processed image.
    /// </summary>
    private (int Width, int Height) RenderIndexedPng(string sourcePath, string destPath, Rgba32[]? externalPalette)
    {
        try
        {
            // Read raw PNG bytes to extract palette
            var bytes = File.ReadAllBytes(sourcePath);

            // Extract palette from PNG PLTE chunk
            var pngPalette = ExtractPngPalette(bytes);

            // Load image as RGBA
            using var image = Image.Load<Rgba32>(sourcePath);
            var width = image.Width;
            var height = image.Height;

            // Get the color at palette index 0 - this should be transparent
            Rgba32? index0Color = null;
            if (pngPalette != null && pngPalette.Length > 0)
            {
                index0Color = pngPalette[0];
            }

            // Apply transparency for palette index 0 color
            if (index0Color.HasValue)
            {
                var bgColor = index0Color.Value;
                image.ProcessPixelRows(accessor =>
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

            // Save as 32-bit RGBA
            var encoder = new PngEncoder
            {
                ColorType = PngColorType.RgbWithAlpha,
                BitDepth = PngBitDepth.Bit8,
                CompressionLevel = PngCompressionLevel.BestCompression
            };
            image.SaveAsPng(destPath, encoder);

            return (width, height);
        }
        catch (Exception e)
        {
            Console.WriteLine($"[WeatherExtractor] Failed to extract {sourcePath}: {e.Message}");
            return (0, 0);
        }
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

    private record WeatherGraphicsInfo(string[] SourceFiles, string? PaletteName, int SpriteWidth, int SpriteHeight, string Description);
}
