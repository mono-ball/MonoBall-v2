using System.Text.Json;
using System.Text.RegularExpressions;

namespace Porycon3.Services;

public class DoorAnimationExtractor
{
    private readonly string _inputPath;
    private readonly string _outputPath;

    // Frame timing: 4 game frames per animation frame (~67ms at 60fps)
    private const int FrameDuration = 4;
    private const int FrameDurationMs = 67;

    public DoorAnimationExtractor(string inputPath, string outputPath)
    {
        _inputPath = inputPath;
        _outputPath = outputPath;
    }

    public record DoorAnimation(
        string Id,
        string Name,
        string GraphicsPath,
        string SoundType,
        int Size,
        int FrameCount,
        int FrameDurationMs,
        string? MetatileId
    );

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

        var outputGraphicsDir = Path.Combine(_outputPath, "Graphics/DoorAnimations");
        var outputDefDir = Path.Combine(_outputPath, "Definitions/Animations/Doors");
        Directory.CreateDirectory(outputGraphicsDir);
        Directory.CreateDirectory(outputDefDir);

        var animations = new List<DoorAnimation>();

        foreach (var pngFile in pngFiles)
        {
            var fileName = Path.GetFileNameWithoutExtension(pngFile);

            // Skip unused files
            if (fileName.StartsWith("unused_"))
                continue;

            // Copy graphics
            var destPath = Path.Combine(outputGraphicsDir, Path.GetFileName(pngFile));
            File.Copy(pngFile, destPath, true);

            // Determine frame count from image dimensions
            var frameCount = GetFrameCount(pngFile);

            // Find mapping info
            var mapping = doorMappings.FirstOrDefault(m =>
                m.TileName.Equals(fileName, StringComparison.OrdinalIgnoreCase) ||
                m.TileName.Replace("_", "").Equals(fileName.Replace("_", ""), StringComparison.OrdinalIgnoreCase));

            var soundType = mapping?.SoundType ?? "normal";
            var size = mapping?.Size ?? 1;
            var metatileId = mapping?.MetatileId;

            var id = $"base:door_anim:{fileName}";
            var name = FormatName(fileName);

            animations.Add(new DoorAnimation(
                id,
                name,
                $"base:graphics:door_animations/{fileName}",
                soundType,
                size,
                frameCount,
                FrameDurationMs,
                metatileId
            ));
        }

        // Write individual definitions
        foreach (var anim in animations)
        {
            var defPath = Path.Combine(outputDefDir, $"{anim.Name.ToLowerInvariant().Replace(" ", "_")}.json");
            var json = JsonSerializer.Serialize(new
            {
                id = anim.Id,
                name = anim.Name,
                graphicsId = anim.GraphicsPath,
                soundType = anim.SoundType,
                size = anim.Size,
                frameCount = anim.FrameCount,
                frameDurationMs = anim.FrameDurationMs,
                metatileId = anim.MetatileId
            }, new JsonSerializerOptions { WriteIndented = true, DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull });
            File.WriteAllText(defPath, json);
        }

        Console.WriteLine($"[DoorAnimationExtractor] Extracted {animations.Count} door animations");
        return animations.Count;
    }

    private int GetFrameCount(string pngPath)
    {
        // Read PNG header to get dimensions
        using var stream = File.OpenRead(pngPath);
        using var reader = new BinaryReader(stream);

        // Skip PNG signature (8 bytes)
        reader.ReadBytes(8);

        // Read IHDR chunk
        reader.ReadBytes(4); // length
        reader.ReadBytes(4); // "IHDR"

        // Width and height are big-endian
        var widthBytes = reader.ReadBytes(4);
        var heightBytes = reader.ReadBytes(4);

        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(widthBytes);
            Array.Reverse(heightBytes);
        }

        var width = BitConverter.ToInt32(widthBytes);
        var height = BitConverter.ToInt32(heightBytes);

        // Each frame is 32 pixels tall (2 metatiles of 16px each)
        // Normal doors: 16px wide, Big doors: 32px wide
        return height / 32;
    }

    private record DoorMapping(string TileName, string SoundType, int Size, string? MetatileId);

    private List<DoorMapping> ParseDoorGraphicsTable(string filePath)
    {
        var content = File.ReadAllText(filePath);
        var mappings = new List<DoorMapping>();

        // Parse sDoorAnimGraphicsTable entries
        // Format: {METATILE_xxx, DOOR_SOUND_xxx, size, sDoorAnimTiles_xxx, sDoorAnimPalettes_xxx},
        var tableMatch = Regex.Match(content,
            @"static const struct DoorGraphics sDoorAnimGraphicsTable\[\]\s*=\s*\{([^}]+)\}",
            RegexOptions.Singleline);

        if (!tableMatch.Success) return mappings;

        var entries = tableMatch.Groups[1].Value;

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

            // Convert tileName from CamelCase to snake_case
            var snakeCaseName = ConvertToSnakeCase(tileName);

            // Build metatile ID
            string? metatileId = null;
            if (metatileLabel.StartsWith("METATILE_"))
            {
                var parts = metatileLabel[9..].Split('_', 2);
                if (parts.Length == 2)
                {
                    var tileset = parts[0].ToLowerInvariant();
                    var name = ConvertToSnakeCase(parts[1]);
                    metatileId = $"base:metatile:{tileset}/{name}";
                }
            }

            mappings.Add(new DoorMapping(snakeCaseName, soundType, size, metatileId));
        }

        return mappings;
    }

    private static string ConvertToSnakeCase(string input)
    {
        // Insert underscore before uppercase letters and convert to lowercase
        var result = Regex.Replace(input, "([a-z])([A-Z])", "$1_$2");
        return result.ToLowerInvariant();
    }

    private static string FormatName(string fileName)
    {
        // Convert snake_case to Title Case
        return string.Join(" ", fileName.Split('_')
            .Select(w => char.ToUpperInvariant(w[0]) + w[1..].ToLowerInvariant()));
    }
}
