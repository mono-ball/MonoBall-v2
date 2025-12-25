using System.Text.Json;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Porycon3.Services;

/// <summary>
/// Extracts weather particle graphics from pokeemerald.
/// </summary>
public class WeatherExtractor
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

    // Weather graphics mapping: internal name -> source file(s)
    private static readonly Dictionary<string, string[]> WeatherGraphics = new()
    {
        ["rain"] = new[] { "rain.png" },
        ["snow"] = new[] { "snow0.png", "snow1.png" },
        ["sandstorm"] = new[] { "sandstorm.png" },
        ["fog_horizontal"] = new[] { "fog_horizontal.png" },
        ["fog_diagonal"] = new[] { "fog_diagonal.png" },
        ["volcanic_ash"] = new[] { "ash.png" },
        ["underwater_bubbles"] = new[] { "bubble.png" },
        ["clouds"] = new[] { "cloud.png" }
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

        foreach (var (weatherType, sourceFiles) in WeatherGraphics)
        {
            var extracted = ExtractWeatherGraphics(weatherType, sourceFiles);
            if (extracted)
            {
                graphicsCount += sourceFiles.Length;
                definitionsCount++;
            }
        }

        Console.WriteLine($"[WeatherExtractor] Extracted {graphicsCount} graphics for {definitionsCount} weather types");
        return (graphicsCount, definitionsCount);
    }

    private bool ExtractWeatherGraphics(string weatherType, string[] sourceFiles)
    {
        var pascalName = ToPascalCase(weatherType);
        var typeDir = Path.Combine(_outputGraphics, pascalName);
        Directory.CreateDirectory(typeDir);

        var extractedFiles = new List<string>();

        foreach (var sourceFile in sourceFiles)
        {
            var sourcePath = Path.Combine(_emeraldGraphics, sourceFile);
            if (!File.Exists(sourcePath))
                continue;

            var destFilename = ToPascalCase(Path.GetFileNameWithoutExtension(sourceFile)) + ".png";
            var destPath = Path.Combine(typeDir, destFilename);

            try
            {
                using var img = Image.Load<Rgba32>(sourcePath);

                // Apply transparency (first pixel is usually transparent color)
                var transparentColor = img[0, 0];
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

                img.SaveAsPng(destPath);
                extractedFiles.Add($"Graphics/Weather/{pascalName}/{destFilename}");
            }
            catch (Exception e)
            {
                Console.WriteLine($"[WeatherExtractor] Failed to extract {sourceFile}: {e.Message}");
            }
        }

        if (extractedFiles.Count == 0)
            return false;

        // Create graphics definition
        var definition = new
        {
            id = $"base:weather:graphics/{weatherType}",
            name = FormatDisplayName(weatherType),
            type = "ParticleSheet",
            textures = extractedFiles,
            description = $"Weather particle graphics for {FormatDisplayName(weatherType)}"
        };

        var defPath = Path.Combine(_outputData, $"{pascalName}.json");
        File.WriteAllText(defPath, JsonSerializer.Serialize(definition, JsonOptions));

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
}
