using System.Text.Json;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Porycon3.Services;

/// <summary>
/// Extracts battle environment graphics from pokeemerald-expansion.
/// Pre-renders composed backgrounds as Bitmaps (like popup backgrounds).
/// Outputs animation tiles as TileSheet for runtime swapping.
/// </summary>
public class BattleEnvironmentExtractor
{
    private const int TileSize = 8;
    private const int GbaScreenWidth = 240;
    private const int GbaScreenHeight = 160;
    private const int TilemapWidth = 32; // Standard GBA background width in tiles

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

    // Map pokeemerald environment names to their source directories
    private static readonly Dictionary<string, EnvironmentSource> EnvironmentMapping = new()
    {
        ["tall_grass"] = new("tall_grass", true),
        ["long_grass"] = new("long_grass", true),
        ["sand"] = new("sand", true),
        ["underwater"] = new("underwater", true),
        ["water"] = new("water", true),
        ["pond_water"] = new("pond_water", true),
        ["rock"] = new("rock", true),
        ["cave"] = new("cave", true),
        ["building"] = new("building", true),
        ["stadium"] = new("stadium", false), // No animation tiles
        ["sky"] = new("sky", true), // Rayquaza
    };

    // Palette variations for environments that share tilesets
    private static readonly Dictionary<string, PaletteVariant[]> PaletteVariants = new()
    {
        ["stadium"] = new PaletteVariant[]
        {
            new("aqua", "palette1.pal", "Stadium Aqua"),
            new("magma", "palette2.pal", "Stadium Magma"),
            new("sidney", "palette3.pal", "Sidney"),
            new("phoebe", "palette4.pal", "Phoebe"),
            new("glacia", "palette5.pal", "Glacia"),
            new("drake", "palette6.pal", "Drake"),
            new("wallace", "palette7.pal", "Wallace/Champion"),
            new("frontier", "battle_frontier.pal", "Battle Frontier"),
        },
        ["building"] = new PaletteVariant[]
        {
            new("gym", "palette2.pal", "Gym"),
            new("leader", "palette3.pal", "Gym Leader"),
        },
        ["cave"] = new PaletteVariant[]
        {
            new("groudon", "groudon.pal", "Groudon Battle"),
        },
        ["water"] = new PaletteVariant[]
        {
            new("kyogre", "kyogre.pal", "Kyogre Battle"),
        },
    };

    public BattleEnvironmentExtractor(string inputPath, string outputPath)
    {
        _inputPath = inputPath;
        _outputPath = outputPath;

        _emeraldGraphics = Path.Combine(inputPath, "graphics", "battle_environment");
        _outputGraphics = Path.Combine(outputPath, "Graphics", "Battle");
        _outputData = Path.Combine(outputPath, "Definitions", "Assets", "Battle");
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

        // Extract base environments
        foreach (var (envName, source) in EnvironmentMapping)
        {
            var envDir = Path.Combine(_emeraldGraphics, source.SourceDir);
            if (!Directory.Exists(envDir))
                continue;

            var (extracted, count) = ExtractEnvironment(envName, envDir, source);
            if (extracted)
            {
                envCount++;
                graphicsCount += count;
            }

            // Extract palette variants if any
            if (PaletteVariants.TryGetValue(envName, out var variants))
            {
                foreach (var variant in variants)
                {
                    var variantName = $"{envName}_{variant.Id}";
                    var (varExtracted, varCount) = ExtractPaletteVariant(variantName, envDir, source, variant);
                    if (varExtracted)
                    {
                        envCount++;
                        graphicsCount += varCount;
                    }
                }
            }
        }

        Console.WriteLine($"[BattleEnvironmentExtractor] Extracted {graphicsCount} graphics for {envCount} environments");
        return (envCount, graphicsCount);
    }

    private (bool Success, int Count) ExtractEnvironment(string envName, string envDir, EnvironmentSource source)
    {
        var pascalName = ToPascalCase(envName);
        var outputDir = Path.Combine(_outputGraphics, pascalName);
        Directory.CreateDirectory(outputDir);

        // Load palette
        var palette = LoadBattlePaletteFromDir(envDir);
        if (palette == null)
        {
            Console.WriteLine($"[BattleEnvironmentExtractor] No palette for {envName}");
            return (false, 0);
        }

        // Load tiles
        var tilesPath = Path.Combine(envDir, "tiles.png");
        var mapPath = Path.Combine(envDir, "map.bin");
        if (!File.Exists(tilesPath))
        {
            Console.WriteLine($"[BattleEnvironmentExtractor] No tiles.png for {envName}");
            return (false, 0);
        }

        // Load tile sheet as indexed image
        var tileSheet = LoadIndexedTileSheet(tilesPath, palette);
        if (tileSheet == null)
            return (false, 0);

        // Parse tilemap
        var tilemap = File.Exists(mapPath) ? ParseTilemap(mapPath) : null;

        // Compose background from tiles using tilemap
        var destPath = Path.Combine(outputDir, $"{pascalName}.png");
        var (width, height) = ComposeBackground(tileSheet, tilemap, destPath);
        if (width == 0)
            return (false, 0);

        int graphicsCount = 1;

        // Process animation tiles if available
        string? animTexturePath = null;
        int animTileCount = 0;
        List<object>? animTiles = null;

        if (source.HasAnimation)
        {
            var animTilesPath = Path.Combine(envDir, "anim_tiles.png");
            if (File.Exists(animTilesPath))
            {
                var destAnimPath = Path.Combine(outputDir, "AnimTiles.png");
                var (animWidth, animHeight, count) = RenderTileSheet(animTilesPath, destAnimPath, palette);
                if (count > 0)
                {
                    animTexturePath = $"Graphics/Battle/{pascalName}/AnimTiles.png";
                    animTileCount = count;
                    animTiles = GenerateTileDefinitions(count, animWidth / TileSize);
                    graphicsCount++;
                }
            }
        }

        // Create environment definition as Bitmap (pre-rendered background)
        var definition = new Dictionary<string, object?>
        {
            ["id"] = $"{IdTransformer.Namespace}:battle:environment/{envName}",
            ["name"] = FormatDisplayName(envName),
            ["type"] = "Bitmap",
            ["texturePath"] = $"Graphics/Battle/{pascalName}/{pascalName}.png",
            ["width"] = width,
            ["height"] = height,
            ["description"] = $"Battle environment background for {FormatDisplayName(envName)}"
        };

        // Add animation as Sprite format (matching porycon2)
        if (animTileCount > 0 && animTiles != null)
        {
            // Parse animation tilemap for animation sequences
            var animMapPath = Path.Combine(envDir, "anim_map.bin");
            var animSequences = ParseAnimationSequences(animMapPath, animTileCount);

            definition["animation"] = new
            {
                type = "Sprite",
                texturePath = animTexturePath,
                frameWidth = TileSize,
                frameHeight = TileSize,
                frameCount = animTileCount,
                frames = animTiles,
                animations = animSequences
            };
        }

        var defPath = Path.Combine(_outputData, $"{pascalName}.json");
        File.WriteAllText(defPath, JsonSerializer.Serialize(definition, JsonOptions));

        tileSheet.Dispose();
        return (true, graphicsCount);
    }

    private (bool Success, int Count) ExtractPaletteVariant(string variantName, string envDir, EnvironmentSource source, PaletteVariant variant)
    {
        var pascalName = ToPascalCase(variantName);
        var outputDir = Path.Combine(_outputGraphics, pascalName);
        Directory.CreateDirectory(outputDir);

        // Load variant palette
        var palettePath = Path.Combine(envDir, variant.PaletteFile);
        var palette = LoadBattlePalette(palettePath);
        if (palette == null)
            return (false, 0);

        // Load tiles
        var tilesPath = Path.Combine(envDir, "tiles.png");
        var mapPath = Path.Combine(envDir, "map.bin");
        if (!File.Exists(tilesPath))
            return (false, 0);

        // Load tile sheet with variant palette
        var tileSheet = LoadIndexedTileSheet(tilesPath, palette);
        if (tileSheet == null)
            return (false, 0);

        // Parse tilemap
        var tilemap = File.Exists(mapPath) ? ParseTilemap(mapPath) : null;

        // Compose background
        var destPath = Path.Combine(outputDir, $"{pascalName}.png");
        var (width, height) = ComposeBackground(tileSheet, tilemap, destPath);
        if (width == 0)
        {
            tileSheet.Dispose();
            return (false, 0);
        }

        // Determine base environment
        var baseEnv = variantName.Split('_')[0];

        var definition = new Dictionary<string, object?>
        {
            ["id"] = $"{IdTransformer.Namespace}:battle:environment/{variantName}",
            ["name"] = variant.DisplayName,
            ["type"] = "Bitmap",
            ["texturePath"] = $"Graphics/Battle/{pascalName}/{pascalName}.png",
            ["width"] = width,
            ["height"] = height,
            ["baseEnvironment"] = $"{IdTransformer.Namespace}:battle:environment/{baseEnv}",
            ["description"] = $"Battle environment background for {variant.DisplayName}"
        };

        var defPath = Path.Combine(_outputData, $"{pascalName}.json");
        File.WriteAllText(defPath, JsonSerializer.Serialize(definition, JsonOptions));

        tileSheet.Dispose();
        return (true, 1);
    }

    /// <summary>
    /// Load indexed tile sheet and apply palette. Returns RGBA image.
    /// </summary>
    private Image<Rgba32>? LoadIndexedTileSheet(string tilesPath, Rgba32[][] palettes)
    {
        try
        {
            using var srcImage = Image.Load<Rgba32>(tilesPath);
            var width = srcImage.Width;
            var height = srcImage.Height;

            var destImage = new Image<Rgba32>(width, height);

            srcImage.ProcessPixelRows(destImage, (srcAccessor, destAccessor) =>
            {
                for (int y = 0; y < height; y++)
                {
                    var srcRow = srcAccessor.GetRowSpan(y);
                    var destRow = destAccessor.GetRowSpan(y);

                    for (int x = 0; x < width; x++)
                    {
                        var srcPixel = srcRow[x];

                        // Convert grayscale to palette index (inverted: white=0, black=15)
                        var colorIndex = (byte)(15 - (srcPixel.R + 8) / 17);

                        if (colorIndex == 0)
                        {
                            destRow[x] = new Rgba32(0, 0, 0, 0);
                        }
                        else if (palettes.Length > 0 && colorIndex < palettes[0].Length)
                        {
                            destRow[x] = palettes[0][colorIndex];
                        }
                        else
                        {
                            destRow[x] = srcPixel;
                        }
                    }
                }
            });

            return destImage;
        }
        catch (Exception e)
        {
            Console.WriteLine($"[BattleEnvironmentExtractor] Failed to load {tilesPath}: {e.Message}");
            return null;
        }
    }

    /// <summary>
    /// Compose background from tiles using tilemap. Renders to GBA screen size.
    /// </summary>
    private (int Width, int Height) ComposeBackground(Image<Rgba32> tileSheet, TilemapData? tilemap, string destPath)
    {
        try
        {
            var tilesX = tileSheet.Width / TileSize;

            // If no tilemap, just save the tile sheet as-is
            if (tilemap == null)
            {
                tileSheet.SaveAsPng(destPath);
                return (tileSheet.Width, tileSheet.Height);
            }

            // Compose using tilemap - render to tilemap dimensions
            var bgWidth = tilemap.Width * TileSize;
            var bgHeight = tilemap.Height * TileSize;

            using var composed = new Image<Rgba32>(bgWidth, bgHeight);

            for (int ty = 0; ty < tilemap.Height; ty++)
            {
                for (int tx = 0; tx < tilemap.Width; tx++)
                {
                    var mapIndex = ty * tilemap.Width + tx;
                    if (mapIndex >= tilemap.Data.Length)
                        continue;

                    var entry = tilemap.Data[mapIndex];

                    // Parse GBA tilemap entry
                    var tileIndex = entry & 0x3FF; // Bits 0-9
                    var hFlip = (entry & 0x400) != 0; // Bit 10
                    var vFlip = (entry & 0x800) != 0; // Bit 11
                    // Bits 12-15: palette (not used since we pre-apply palette)

                    // Get tile position in source
                    var srcTileX = (tileIndex % tilesX) * TileSize;
                    var srcTileY = (tileIndex / tilesX) * TileSize;

                    // Destination position
                    var destX = tx * TileSize;
                    var destY = ty * TileSize;

                    // Copy tile with flip handling
                    CopyTile(tileSheet, composed, srcTileX, srcTileY, destX, destY, hFlip, vFlip);
                }
            }

            composed.SaveAsPng(destPath);
            return (bgWidth, bgHeight);
        }
        catch (Exception e)
        {
            Console.WriteLine($"[BattleEnvironmentExtractor] Failed to compose background: {e.Message}");
            return (0, 0);
        }
    }

    /// <summary>
    /// Copy a single tile with optional horizontal/vertical flip.
    /// </summary>
    private void CopyTile(Image<Rgba32> src, Image<Rgba32> dest, int srcX, int srcY, int destX, int destY, bool hFlip, bool vFlip)
    {
        src.ProcessPixelRows(dest, (srcAccessor, destAccessor) =>
        {
            for (int y = 0; y < TileSize; y++)
            {
                var srcRowY = srcY + y;
                var destRowY = destY + (vFlip ? (TileSize - 1 - y) : y);

                if (srcRowY >= srcAccessor.Height || destRowY >= destAccessor.Height)
                    continue;

                var srcRow = srcAccessor.GetRowSpan(srcRowY);
                var destRow = destAccessor.GetRowSpan(destRowY);

                for (int x = 0; x < TileSize; x++)
                {
                    var srcColX = srcX + x;
                    var destColX = destX + (hFlip ? (TileSize - 1 - x) : x);

                    if (srcColX >= srcAccessor.Width || destColX >= destAccessor.Width)
                        continue;

                    destRow[destColX] = srcRow[srcColX];
                }
            }
        });
    }

    /// <summary>
    /// Render indexed tile PNG with palette applied. Returns (tileWidth, tileHeight, tileCount).
    /// Used for animation tiles which stay as TileSheet.
    /// </summary>
    private (int TileWidth, int TileHeight, int TileCount) RenderTileSheet(string tilesPath, string destPath, Rgba32[][] palettes)
    {
        try
        {
            using var srcImage = Image.Load<Rgba32>(tilesPath);
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
                        var colorIndex = (byte)(15 - (srcPixel.R + 8) / 17);

                        if (colorIndex == 0)
                        {
                            destRow[x] = new Rgba32(0, 0, 0, 0);
                        }
                        else if (palettes.Length > 0 && colorIndex < palettes[0].Length)
                        {
                            destRow[x] = palettes[0][colorIndex];
                        }
                        else
                        {
                            destRow[x] = srcPixel;
                        }
                    }
                }
            });

            destImage.SaveAsPng(destPath);

            var tilesX = width / TileSize;
            var tilesY = height / TileSize;
            return (width, height, tilesX * tilesY);
        }
        catch (Exception e)
        {
            Console.WriteLine($"[BattleEnvironmentExtractor] Failed to render {tilesPath}: {e.Message}");
            return (0, 0, 0);
        }
    }

    /// <summary>
    /// Parse animation sequences from anim_map.bin.
    /// GBA battle environments typically have 4-8 animation frames.
    /// </summary>
    private List<object> ParseAnimationSequences(string animMapPath, int totalTileCount)
    {
        var animations = new List<object>();

        if (!File.Exists(animMapPath))
        {
            // Default animation: cycle through all frames
            var frameIndices = Enumerable.Range(0, Math.Min(totalTileCount, 16)).ToList();
            var frameDurations = frameIndices.Select(_ => 0.1).ToList(); // 100ms per frame

            animations.Add(new
            {
                name = "default",
                loop = true,
                frameIndices = frameIndices,
                frameDurations = frameDurations,
                flipHorizontal = false
            });
            return animations;
        }

        try
        {
            var data = File.ReadAllBytes(animMapPath);
            var entryCount = data.Length / 2;

            // Parse tilemap entries to find unique tile indices used in animation
            var animTileIndices = new HashSet<int>();
            for (int i = 0; i < entryCount; i++)
            {
                var entry = data[i * 2] | (data[i * 2 + 1] << 8);
                var tileIndex = entry & 0x3FF;
                if (tileIndex < totalTileCount)
                {
                    animTileIndices.Add(tileIndex);
                }
            }

            // Create animation sequence from unique tiles
            var sortedIndices = animTileIndices.OrderBy(x => x).ToList();
            if (sortedIndices.Count == 0)
            {
                sortedIndices = Enumerable.Range(0, Math.Min(totalTileCount, 8)).ToList();
            }

            // Default animation with 100ms per frame
            var durations = sortedIndices.Select(_ => 0.1).ToList();

            animations.Add(new
            {
                name = "default",
                loop = true,
                frameIndices = sortedIndices,
                frameDurations = durations,
                flipHorizontal = false
            });
        }
        catch
        {
            // Fallback: simple sequence
            var frameIndices = Enumerable.Range(0, Math.Min(totalTileCount, 8)).ToList();
            animations.Add(new
            {
                name = "default",
                loop = true,
                frameIndices = frameIndices,
                frameDurations = frameIndices.Select(_ => 0.1).ToList(),
                flipHorizontal = false
            });
        }

        return animations;
    }

    /// <summary>
    /// Parse GBA tilemap binary data.
    /// </summary>
    private TilemapData? ParseTilemap(string mapPath)
    {
        try
        {
            var data = File.ReadAllBytes(mapPath);
            var entryCount = data.Length / 2;
            var height = entryCount / TilemapWidth;

            var tilemap = new int[entryCount];

            for (int i = 0; i < entryCount; i++)
            {
                tilemap[i] = data[i * 2] | (data[i * 2 + 1] << 8);
            }

            return new TilemapData(TilemapWidth, height, tilemap);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Generate tile definitions array for TileSheet format.
    /// </summary>
    private List<object> GenerateTileDefinitions(int tileCount, int tilesPerRow)
    {
        var tiles = new List<object>();

        for (int i = 0; i < tileCount; i++)
        {
            var col = i % tilesPerRow;
            var row = i / tilesPerRow;
            tiles.Add(new
            {
                index = i,
                x = col * TileSize,
                y = row * TileSize,
                width = TileSize,
                height = TileSize
            });
        }

        return tiles;
    }

    private Rgba32[][]? LoadBattlePaletteFromDir(string envDir)
    {
        var palettePath = Path.Combine(envDir, "palette.pal");
        if (File.Exists(palettePath))
            return LoadBattlePalette(palettePath);

        var palette1Path = Path.Combine(envDir, "palette1.pal");
        if (File.Exists(palette1Path))
            return LoadBattlePalette(palette1Path);

        var frontierPath = Path.Combine(envDir, "battle_frontier.pal");
        if (File.Exists(frontierPath))
            return LoadBattlePalette(frontierPath);

        var palFiles = Directory.GetFiles(envDir, "*.pal");
        if (palFiles.Length > 0)
            return LoadBattlePalette(palFiles[0]);

        return null;
    }

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

    private record EnvironmentSource(string SourceDir, bool HasAnimation);
    private record PaletteVariant(string Id, string PaletteFile, string DisplayName);
    private record TilemapData(int Width, int Height, int[] Data);
}
