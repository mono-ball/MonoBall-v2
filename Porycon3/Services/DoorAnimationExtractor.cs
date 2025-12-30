using System.Text.Json;
using System.Text.RegularExpressions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Porycon3.Services.Extraction;

namespace Porycon3.Services;

/// <summary>
/// Extracts door animation sprites and definitions from pokeemerald-expansion.
/// </summary>
public class DoorAnimationExtractor : ExtractorBase
{
    // Frame timing: 4 game frames per animation frame (~67ms at 60fps)
    private const double FrameDurationSeconds = 0.06666666666666667;

    public override string Name => "Door Animations";
    public override string Description => "Extracts door animation sprites and definitions";

    public DoorAnimationExtractor(string inputPath, string outputPath, bool verbose = false)
        : base(inputPath, outputPath, verbose)
    {
    }

    protected override int ExecuteExtraction()
    {
        var doorAnimPath = Path.Combine(InputPath, "graphics/door_anims");
        var fieldDoorPath = Path.Combine(InputPath, "src/field_door.c");

        if (!Directory.Exists(doorAnimPath) || !File.Exists(fieldDoorPath))
        {
            AddError("", "Door animation source not found");
            return 0;
        }

        // Parse the door graphics table
        List<DoorMapping> doorMappings = [];
        WithStatus("Parsing door graphics table...", _ =>
        {
            doorMappings = ParseDoorGraphicsTable(fieldDoorPath);
        });

        // Get all PNG files
        var pngFiles = Directory.GetFiles(doorAnimPath, "*.png")
            .Where(f => !Path.GetFileNameWithoutExtension(f).StartsWith("unused_"))
            .ToList();

        int count = 0;
        int graphicsCount = 0;

        WithProgress("Extracting door animations", pngFiles, (pngFile, task) =>
        {
            var fileName = Path.GetFileNameWithoutExtension(pngFile);
            SetTaskDescription(task, $"[cyan]Extracting[/] [yellow]{fileName}[/]");

            if (ExtractDoorAnimation(pngFile, fileName, doorMappings))
            {
                count++;
                graphicsCount++;
            }
        });

        SetCount("Graphics", graphicsCount);
        return count;
    }

    private bool ExtractDoorAnimation(string pngFile, string fileName, List<DoorMapping> doorMappings)
    {
        var pascalName = ToPascalCase(fileName);

        // Find mapping info
        var mapping = doorMappings.FirstOrDefault(m =>
            m.TileName.Equals(fileName, StringComparison.OrdinalIgnoreCase) ||
            m.TileName.Replace("_", "").Equals(fileName.Replace("_", ""), StringComparison.OrdinalIgnoreCase));

        // Convert sound type to sound ID
        var soundType = mapping?.SoundType ?? "normal";
        var soundId = $"{IdTransformer.Namespace}:sound:door-{soundType}";
        var metatileId = mapping?.MetatileId;

        // Get frame info from the sprite sheet
        var (frameCount, frameWidth, frameHeight) = GetFrameInfo(pngFile);
        if (frameCount == 0)
        {
            AddError(fileName, "Could not read frame information");
            return false;
        }

        // Copy the sprite sheet
        var outputPngPath = GetGraphicsPath("DoorAnimations", $"{pascalName}.png");
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
        var definition = new
        {
            id = $"{IdTransformer.Namespace}:sprite:door-animations/{fileName.Replace("_", "-")}",
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

        var defPath = GetDefinitionPath("DoorAnimations", $"{pascalName}.json");
        var json = JsonSerializer.Serialize(definition, JsonOptions.Default);
        File.WriteAllText(defPath, json);

        LogVerbose($"Extracted {pascalName} ({frameCount} frames)");
        return true;
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
            AddError(Path.GetFileName(pngPath), $"Error reading image: {ex.Message}", ex);
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
                    metatileId = $"{IdTransformer.Namespace}:metatile:{tileset}/{name}";
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
