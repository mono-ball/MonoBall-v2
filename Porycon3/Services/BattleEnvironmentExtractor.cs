using System.Text.Json;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Porycon3.Services;

/// <summary>
/// Extracts and renders battle environment graphics from pokeemerald.
/// Composes tiles + palette + tilemap into final background images.
/// </summary>
public class BattleEnvironmentExtractor
{
    private const int TileSize = 8;
    private const int GbaScreenWidth = 240;
    private const int GbaScreenHeight = 160; // Full height for battle BG

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

    public BattleEnvironmentExtractor(string inputPath, string outputPath)
    {
        _inputPath = inputPath;
        _outputPath = outputPath;

        _emeraldGraphics = Path.Combine(inputPath, "graphics", "battle_environment");
        _outputGraphics = Path.Combine(outputPath, "Graphics", "Battle", "Environment");
        _outputData = Path.Combine(outputPath, "Definitions", "Battle", "Environment");
    }

    public (int Environments, int Graphics) ExtractAll()
    {
        if (!Directory.Exists(_emeraldGraphics))
        {
            Console.WriteLine($"[BattleEnvironmentExtractor] Battle environment graphics not found: {_emeraldGraphics}");
            return (0, 0);
        }

        Directory.CreateDirectory(_outputGraphics);
        Directory.CreateDirectory(_outputData);

        int envCount = 0;
        int graphicsCount = 0;

        foreach (var envDir in Directory.GetDirectories(_emeraldGraphics))
        {
            var envName = Path.GetFileName(envDir);
            var (extracted, count) = ExtractEnvironment(envName, envDir);
            if (extracted)
            {
                envCount++;
                graphicsCount += count;
            }
        }

        Console.WriteLine($"[BattleEnvironmentExtractor] Extracted {graphicsCount} graphics for {envCount} environments");
        return (envCount, graphicsCount);
    }

    private (bool Success, int Count) ExtractEnvironment(string envName, string envDir)
    {
        var pascalName = ToPascalCase(envName);
        var outputDir = Path.Combine(_outputGraphics, pascalName);
        Directory.CreateDirectory(outputDir);

        var extractedFiles = new List<string>();
        int count = 0;

        // Load palette
        var palettePath = Path.Combine(envDir, "palette.pal");
        var palettes = LoadBattlePalette(palettePath);
        if (palettes == null)
        {
            Console.WriteLine($"[BattleEnvironmentExtractor] No palette for {envName}");
            return (false, 0);
        }

        // Load and render static background
        var tilesPath = Path.Combine(envDir, "tiles.png");
        var mapPath = Path.Combine(envDir, "map.bin");
        if (File.Exists(tilesPath) && File.Exists(mapPath))
        {
            var destPath = Path.Combine(outputDir, "Background.png");
            if (RenderTilemapToImage(tilesPath, mapPath, palettes, destPath))
            {
                extractedFiles.Add($"Graphics/Battle/Environment/{pascalName}/Background.png");
                count++;
            }
        }

        // Load and render animated background if exists
        var animTilesPath = Path.Combine(envDir, "anim_tiles.png");
        var animMapPath = Path.Combine(envDir, "anim_map.bin");
        if (File.Exists(animTilesPath) && File.Exists(animMapPath))
        {
            var destPath = Path.Combine(outputDir, "AnimBackground.png");
            if (RenderTilemapToImage(animTilesPath, animMapPath, palettes, destPath))
            {
                extractedFiles.Add($"Graphics/Battle/Environment/{pascalName}/AnimBackground.png");
                count++;
            }
        }

        if (extractedFiles.Count == 0)
            return (false, 0);

        // Create environment definition
        var definition = new
        {
            id = $"base:battle:environment/{envName}",
            name = FormatDisplayName(envName),
            type = "RenderedBackground",
            backgroundTexture = extractedFiles.FirstOrDefault(f => f.EndsWith("Background.png")),
            animBackgroundTexture = extractedFiles.FirstOrDefault(f => f.EndsWith("AnimBackground.png")),
            hasAnimation = extractedFiles.Any(f => f.Contains("Anim")),
            width = GbaScreenWidth,
            height = GbaScreenHeight,
            description = $"Battle environment for {FormatDisplayName(envName)}"
        };

        var defPath = Path.Combine(_outputData, $"{pascalName}.json");
        File.WriteAllText(defPath, JsonSerializer.Serialize(definition, JsonOptions));

        return (true, count);
    }

    /// <summary>
    /// Load JASC-PAL palette file with multiple palettes (48 colors = 3 palettes of 16).
    /// </summary>
    private Rgba32[][]? LoadBattlePalette(string path)
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

            // Battle environments use 3 palettes of 16 colors each
            var numPalettes = (numColors + 15) / 16;
            var palettes = new Rgba32[numPalettes][];

            for (int p = 0; p < numPalettes; p++)
            {
                palettes[p] = new Rgba32[16];
                for (int i = 0; i < 16; i++)
                {
                    var lineIndex = 3 + p * 16 + i;
                    if (lineIndex >= lines.Length)
                    {
                        palettes[p][i] = new Rgba32(0, 0, 0, i == 0 ? (byte)0 : (byte)255);
                        continue;
                    }

                    var parts = lines[lineIndex].Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 3 &&
                        int.TryParse(parts[0], out var r) &&
                        int.TryParse(parts[1], out var g) &&
                        int.TryParse(parts[2], out var b))
                    {
                        // Color 0 is transparent in GBA
                        palettes[p][i] = i == 0
                            ? new Rgba32(0, 0, 0, 0)
                            : new Rgba32((byte)r, (byte)g, (byte)b, 255);
                    }
                    else
                    {
                        palettes[p][i] = new Rgba32(0, 0, 0, i == 0 ? (byte)0 : (byte)255);
                    }
                }
            }

            return palettes;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Render a tilemap to a composed image using tiles and palette.
    /// </summary>
    private bool RenderTilemapToImage(string tilesPath, string mapPath, Rgba32[][] palettes, string destPath)
    {
        try
        {
            // Load indexed tile data
            var indexedTiles = LoadIndexedTiles(tilesPath);
            if (indexedTiles == null)
                return false;

            var (tilePixels, tilesWidth, tilesHeight) = indexedTiles.Value;
            var tilesPerRow = tilesWidth / TileSize;

            // Load tilemap
            var mapData = File.ReadAllBytes(mapPath);
            var numTilemapEntries = mapData.Length / 2;

            // Calculate tilemap dimensions (GBA battle BG is typically 32 tiles wide)
            var mapWidth = 32; // tiles
            var mapHeight = numTilemapEntries / mapWidth;

            // Create output image at GBA screen resolution
            using var output = new Image<Rgba32>(GbaScreenWidth, GbaScreenHeight);

            // Calculate how many tiles to render (30x20 for 240x160 screen)
            var renderTilesX = GbaScreenWidth / TileSize;  // 30
            var renderTilesY = GbaScreenHeight / TileSize; // 20

            output.ProcessPixelRows(accessor =>
            {
                for (int mapY = 0; mapY < Math.Min(mapHeight, renderTilesY); mapY++)
                {
                    for (int mapX = 0; mapX < Math.Min(mapWidth, renderTilesX); mapX++)
                    {
                        var mapIndex = mapY * mapWidth + mapX;
                        if (mapIndex * 2 + 1 >= mapData.Length)
                            continue;

                        // Read 16-bit tilemap entry (little-endian)
                        var entry = mapData[mapIndex * 2] | (mapData[mapIndex * 2 + 1] << 8);

                        // Parse GBA tilemap entry:
                        // Bits 0-9: Tile index
                        // Bits 10-11: Palette index (for 16-color mode)
                        // Bit 12: Horizontal flip
                        // Bit 13: Vertical flip
                        var tileIndex = entry & 0x3FF;
                        var paletteIndex = (entry >> 12) & 0x3; // Use bits 12-13 for palette in 4bpp mode
                        var flipH = ((entry >> 10) & 1) != 0;
                        var flipV = ((entry >> 11) & 1) != 0;

                        // Ensure valid palette index
                        if (paletteIndex >= palettes.Length)
                            paletteIndex = 0;

                        var palette = palettes[paletteIndex];

                        // Calculate source tile position
                        var srcTileX = (tileIndex % tilesPerRow) * TileSize;
                        var srcTileY = (tileIndex / tilesPerRow) * TileSize;

                        // Render tile to output
                        var destX = mapX * TileSize;
                        var destY = mapY * TileSize;

                        for (int ty = 0; ty < TileSize && destY + ty < GbaScreenHeight; ty++)
                        {
                            int srcY = flipV ? (TileSize - 1 - ty) : ty;
                            var row = accessor.GetRowSpan(destY + ty);

                            for (int tx = 0; tx < TileSize && destX + tx < GbaScreenWidth; tx++)
                            {
                                int srcX = flipH ? (TileSize - 1 - tx) : tx;

                                var pixelIndex = (srcTileY + srcY) * tilesWidth + (srcTileX + srcX);
                                if (pixelIndex >= tilePixels.Length)
                                    continue;

                                var colorIndex = tilePixels[pixelIndex];

                                if (colorIndex == 0)
                                {
                                    row[destX + tx] = new Rgba32(0, 0, 0, 0);
                                }
                                else if (colorIndex < palette.Length)
                                {
                                    row[destX + tx] = palette[colorIndex];
                                }
                            }
                        }
                    }
                }
            });

            output.SaveAsPng(destPath);
            return true;
        }
        catch (Exception e)
        {
            Console.WriteLine($"[BattleEnvironmentExtractor] Failed to render {tilesPath}: {e.Message}");
            return false;
        }
    }

    /// <summary>
    /// Load indexed tiles from PNG (4bpp indexed color).
    /// pokeemerald tiles.png files use inverted grayscale: 0=white (index 0), 255=black (index 15).
    /// </summary>
    private (byte[] Pixels, int Width, int Height)? LoadIndexedTiles(string path)
    {
        try
        {
            using var image = Image.Load<Rgba32>(path);
            var width = image.Width;
            var height = image.Height;
            var pixels = new byte[width * height];

            image.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < height; y++)
                {
                    var row = accessor.GetRowSpan(y);
                    for (int x = 0; x < width; x++)
                    {
                        var pixel = row[x];
                        // 4bpp indexed: inverted grayscale
                        // Index 0 = white (R=255), Index 15 = black (R=0)
                        pixels[y * width + x] = (byte)(15 - (pixel.R + 8) / 17);
                    }
                }
            });

            return (pixels, width, height);
        }
        catch
        {
            return null;
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
}
