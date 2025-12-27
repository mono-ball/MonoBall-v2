using System.Text.Json;
using System.Text.RegularExpressions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Porycon3.Services;

public class DoorAnimationExtractor
{
    private readonly string _inputPath;
    private readonly string _outputPath;

    // Frame timing: 4 game frames per animation frame (~67ms at 60fps)
    private const double FrameDurationSeconds = 0.06666666666666667;

    public DoorAnimationExtractor(string inputPath, string outputPath)
    {
        _inputPath = inputPath;
        _outputPath = outputPath;
    }

    public int Extract()
    {
        var doorAnimPath = Path.Combine(_inputPath, "graphics/door_anims");
        var fieldDoorPath = Path.Combine(_inputPath, "src/field_door.c");

        if (!Directory.Exists(doorAnimPath) || !File.Exists(fieldDoorPath))
        {
            Console.WriteLine("[DoorAnimationExtractor] Door animation source not found");
            return 0;
        }

        // Parse the door graphics table from field_door.c
        var doorMappings = ParseDoorGraphicsTable(fieldDoorPath);

        // Get all PNG files
        var pngFiles = Directory.GetFiles(doorAnimPath, "*.png");

        var count = 0;

        foreach (var pngFile in pngFiles)
        {
            var fileName = Path.GetFileNameWithoutExtension(pngFile);

            // Skip unused files
            if (fileName.StartsWith("unused_"))
                continue;

            // Convert to PascalCase for output
            var pascalName = ToPascalCase(fileName);

            // Find mapping info
            var mapping = doorMappings.FirstOrDefault(m =>
                m.TileName.Equals(fileName, StringComparison.OrdinalIgnoreCase) ||
                m.TileName.Replace("_", "").Equals(fileName.Replace("_", ""), StringComparison.OrdinalIgnoreCase));

            // Convert sound type to sound ID
            var soundType = mapping?.SoundType ?? "normal";
            var soundId = $"base:sound:door-{soundType}";
            var metatileId = mapping?.MetatileId;

            // Get frame info from the sprite sheet (detect width from image)
            var (frameCount, frameWidth, frameHeight) = GetFrameInfo(pngFile);
            if (frameCount == 0) continue;

            // Copy the sprite sheet
            var outputGraphicsDir = Path.Combine(_outputPath, "Graphics/DoorAnimations");
            Directory.CreateDirectory(outputGraphicsDir);
            var outputPngPath = Path.Combine(outputGraphicsDir, $"{pascalName}.png");
            File.Copy(pngFile, outputPngPath, overwrite: true);

            // Build frames array (frames are stacked vertically)
            var frames = new List<object>();
            for (var i = 0; i < frameCount; i++)
            {
                frames.Add(new
                {
                    index = i,
                    x = 0,
                    y = i * frameHeight,
                    width = frameWidth,
                    height = frameHeight
                });
            }

            // Build animations
            var openFrameIndices = Enumerable.Range(0, frameCount).ToList();
            var closeFrameIndices = Enumerable.Range(0, frameCount).Reverse().ToList();
            var frameDurations = Enumerable.Repeat(FrameDurationSeconds, frameCount).ToList();

            var animations = new List<object>
            {
                new
                {
                    name = "open",
                    loop = false,
                    frameIndices = openFrameIndices,
                    frameDurations = frameDurations,
                    flipHorizontal = false
                },
                new
                {
                    name = "close",
                    loop = false,
                    frameIndices = closeFrameIndices,
                    frameDurations = frameDurations,
                    flipHorizontal = false
                }
            };

            // Create definition
            var outputDefDir = Path.Combine(_outputPath, "Definitions/Assets/DoorAnimations");
            Directory.CreateDirectory(outputDefDir);

            var definition = new
            {
                id = $"base:sprite:door-animations/{fileName.Replace("_", "-")}",
                name = FormatName(fileName),
                type = "Sprite",
                texturePath = $"Graphics/DoorAnimations/{pascalName}.png",
                frameWidth,
                frameHeight,
                frameCount,
                soundId,
                metatileId,
                frames,
                animations
            };

            var defPath = Path.Combine(outputDefDir, $"{pascalName}.json");
            var json = JsonSerializer.Serialize(definition, new JsonSerializerOptions
            {
                WriteIndented = true,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            });
            File.WriteAllText(defPath, json);

            count++;
        }

        Console.WriteLine($"[DoorAnimationExtractor] Extracted {count} door animations");
        return count;
    }

    private (int frameCount, int frameWidth, int frameHeight) GetFrameInfo(string pngPath)
    {
        try
        {
            using var image = Image.Load<Rgba32>(pngPath);

            var frameWidth = image.Width; // 16 for single, 32 for double-wide
            var frameHeight = 32; // 2 metatiles tall
            var frameCount = image.Height / frameHeight;

            return (frameCount, frameWidth, frameHeight);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DoorAnimationExtractor] Error reading {pngPath}: {ex.Message}");
            return (0, 0, 0);
        }
    }

    private record DoorMapping(string TileName, string SoundType, int Size, string? MetatileId);

    private List<DoorMapping> ParseDoorGraphicsTable(string filePath)
    {
        var content = File.ReadAllText(filePath);
        var mappings = new List<DoorMapping>();

        // Find the table start and extract until the closing };
        var tableStartIdx = content.IndexOf("static const struct DoorGraphics sDoorAnimGraphicsTable[]");
        if (tableStartIdx == -1) return mappings;

        var braceStart = content.IndexOf('{', tableStartIdx);
        if (braceStart == -1) return mappings;

        // Find matching closing brace
        var braceCount = 1;
        var braceEnd = braceStart + 1;
        while (braceEnd < content.Length && braceCount > 0)
        {
            if (content[braceEnd] == '{') braceCount++;
            else if (content[braceEnd] == '}') braceCount--;
            braceEnd++;
        }

        var entries = content.Substring(braceStart + 1, braceEnd - braceStart - 2);

        // Match each entry
        var entryPattern = new Regex(
            @"\{\s*(METATILE_\w+|0x[0-9A-Fa-f]+)\s*,\s*DOOR_SOUND_(\w+)\s*,\s*(\d+)\s*,\s*sDoorAnimTiles_(\w+)",
            RegexOptions.Multiline);

        foreach (Match match in entryPattern.Matches(entries))
        {
            var metatileLabel = match.Groups[1].Value;
            var soundType = match.Groups[2].Value.ToLowerInvariant();
            var size = int.Parse(match.Groups[3].Value);
            var tileName = match.Groups[4].Value;

            // Convert tileName from CamelCase to snake_case for matching
            var snakeCaseName = ConvertToSnakeCase(tileName);

            // Build metatile ID
            string? metatileId = null;
            if (metatileLabel.StartsWith("METATILE_"))
            {
                var parts = metatileLabel[9..].Split('_', 2);
                if (parts.Length == 2)
                {
                    var tileset = parts[0].ToLowerInvariant();
                    var name = ConvertToSnakeCase(parts[1]).Replace("_", "-");
                    metatileId = $"base:metatile:{tileset}/{name}";
                }
            }

            mappings.Add(new DoorMapping(snakeCaseName, soundType, size, metatileId));
        }

        return mappings;
    }

    private static string ConvertToSnakeCase(string input)
    {
        var result = Regex.Replace(input, "([a-z])([A-Z])", "$1_$2");
        return result.ToLowerInvariant();
    }

    private static string ToPascalCase(string snakeCase)
    {
        return string.Concat(snakeCase.Split('_')
            .Select(w => char.ToUpperInvariant(w[0]) + w[1..].ToLowerInvariant()));
    }

    private static string FormatName(string fileName)
    {
        return string.Join(" ", fileName.Split('_')
            .Select(w => char.ToUpperInvariant(w[0]) + w[1..].ToLowerInvariant()));
    }
}
