using System.Text.Json;
using SixLabors.ImageSharp;
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
        _outputData = Path.Combine(outputPath, "Definitions", "Weather", "Graphics");
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
    /// Render indexed PNG to RGBA with palette application.
    /// Returns (width, height) of the processed image.
    /// </summary>
    private (int Width, int Height) RenderIndexedPng(string sourcePath, string destPath, Rgba32[]? palette)
    {
        try
        {
            using var srcImage = Image.Load<Rgba32>(sourcePath);
            var width = srcImage.Width;
            var height = srcImage.Height;

            using var destImage = new Image<Rgba32>(width, height);

            srcImage.ProcessPixelRows(destImage, (srcAccessor, destAccessor) =>
            {
                for (int y = 0; y < height; y++)
                {
                    var srcRow = srcAccessor.GetRowSpan(y);
                    var destRow = destAccessor.GetRowSpan(y);

                    for (int x = 0; x < width; x++)
                    {
                        var srcPixel = srcRow[x];

                        // Convert grayscale to palette index
                        // pokeemerald uses inverted grayscale: white (255) = index 0, black (0) = index 15
                        var colorIndex = (byte)(15 - (srcPixel.R + 8) / 17);

                        if (colorIndex == 0)
                        {
                            // Index 0 is transparent
                            destRow[x] = new Rgba32(0, 0, 0, 0);
                        }
                        else if (palette != null && colorIndex < palette.Length)
                        {
                            // Apply palette color
                            destRow[x] = palette[colorIndex];
                        }
                        else
                        {
                            // No palette - keep original colors but ensure index 0 is transparent
                            destRow[x] = srcPixel;
                        }
                    }
                }
            });

            destImage.SaveAsPng(destPath);
            return (width, height);
        }
        catch (Exception e)
        {
            Console.WriteLine($"[WeatherExtractor] Failed to extract {sourcePath}: {e.Message}");
            return (0, 0);
        }
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
