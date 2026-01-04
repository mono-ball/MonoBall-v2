using System.IO.Compression;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Porycon3.Models;
using Porycon3.Infrastructure;

namespace Porycon3.Services;

/// <summary>
/// Scans pokeemerald anim folders and extracts animation frame data.
/// Matches tiles by their rendered appearance to detect which metatiles need animation.
/// </summary>
public class AnimationScanner
{
    private readonly string _pokeemeraldPath;
    private readonly TilesetPathResolver _resolver;

    // Animation mappings extracted from pokeemerald's tileset_anims.c
    // Format: tileset_name -> list of animations
    private static readonly Dictionary<string, AnimationDefinition[]> AnimationMappings = new()
    {
        ["general"] = new[]
        {
            new AnimationDefinition("flower", 508, 4, "flower", 266, false, new[] { 0, 1, 0, 2 }),
            new AnimationDefinition("water", 432, 30, "water", 133, false, new[] { 0, 1, 2, 3, 4, 5, 6, 7 }),
            new AnimationDefinition("sand_water_edge", 464, 10, "sand_water_edge", 133, false, new[] { 0, 1, 2, 3, 4, 5, 6, 0 }),
            new AnimationDefinition("waterfall", 496, 6, "waterfall", 133, false, new[] { 0, 1, 2, 3 }),
            new AnimationDefinition("land_water_edge", 480, 10, "land_water_edge", 133, false, new[] { 0, 1, 2, 3 })
        },
        ["building"] = new[]
        {
            new AnimationDefinition("tv_turned_on", 496, 4, "tv_turned_on", 133, true, null)
        },
        ["rustboro"] = new[]
        {
            new AnimationDefinition("windy_water", 128, 8, "windy_water", 133, true, null),
            new AnimationDefinition("fountain", 448, 4, "fountain", 133, true, null)
        },
        ["dewford"] = new[]
        {
            new AnimationDefinition("flag", 170, 6, "flag", 133, true, null)
        },
        ["slateport"] = new[]
        {
            new AnimationDefinition("balloons", 224, 4, "balloons", 133, true, null)
        },
        ["mauville"] = new[]
        {
            new AnimationDefinition("flower_1", 96, 4, "flower_1", 133, true, null),
            new AnimationDefinition("flower_2", 128, 4, "flower_2", 133, true, null)
        },
        ["lavaridge"] = new[]
        {
            new AnimationDefinition("steam", 288, 4, "steam", 133, true, new[] { 0, 1, 2, 3 }),
            new AnimationDefinition("lava", 160, 4, "lava", 133, true, new[] { 0, 1, 2, 3 })
        },
        ["ever_grande"] = new[]
        {
            new AnimationDefinition("flowers", 224, 4, "flowers", 133, true, null)
        },
        ["pacifidlog"] = new[]
        {
            new AnimationDefinition("log_bridges", 464, 30, "log_bridges", 133, true, new[] { 0, 1, 2, 1 }),
            new AnimationDefinition("water_currents", 496, 8, "water_currents", 133, true, new[] { 0, 1, 2, 3, 4, 5, 6, 7 })
        },
        ["sootopolis"] = new[]
        {
            new AnimationDefinition("stormy_water", 240, 96, "stormy_water", 133, true, null)
        },
        ["underwater"] = new[]
        {
            new AnimationDefinition("seaweed", 496, 4, "seaweed", 133, true, new[] { 0, 1, 2, 3 })
        },
        ["cave"] = new[]
        {
            new AnimationDefinition("lava", 416, 4, "lava", 133, true, new[] { 0, 1, 2, 3 })
        },
        ["battle_frontier_outside_west"] = new[]
        {
            new AnimationDefinition("flag", 218, 6, "flag", 133, true, null)
        },
        ["battle_frontier_outside_east"] = new[]
        {
            new AnimationDefinition("flag", 218, 6, "flag", 133, true, null)
        },
        ["mauville_gym"] = new[]
        {
            new AnimationDefinition("electric_gates", 144, 16, "electric_gates", 133, true, null)
        },
        ["sootopolis_gym"] = new[]
        {
            new AnimationDefinition("side_waterfall", 496, 12, "side_waterfall", 133, true, null),
            new AnimationDefinition("front_waterfall", 464, 20, "front_waterfall", 133, true, null)
        },
        ["elite_four"] = new[]
        {
            new AnimationDefinition("floor_light", 480, 4, "floor_light", 133, true, null),
            new AnimationDefinition("wall_lights", 504, 1, "wall_lights", 133, true, null)
        },
        ["bike_shop"] = new[]
        {
            new AnimationDefinition("blinking_lights", 496, 9, "blinking_lights", 133, true, null)
        },
        ["battle_pyramid"] = new[]
        {
            new AnimationDefinition("torch", 151, 8, "torch", 133, true, null),
            new AnimationDefinition("statue_shadow", 135, 8, "statue_shadow", 133, true, null)
        }
    };

    public AnimationScanner(string pokeemeraldPath)
    {
        _pokeemeraldPath = pokeemeraldPath;
        _resolver = new TilesetPathResolver(pokeemeraldPath);
    }

    /// <summary>
    /// Get animation definitions for a tileset.
    /// </summary>
    public AnimationDefinition[] GetAnimationsForTileset(string tilesetName)
    {
        // Normalize tileset name
        var normalized = NormalizeTilesetName(tilesetName);

        if (AnimationMappings.TryGetValue(normalized, out var animations))
            return animations;

        return Array.Empty<AnimationDefinition>();
    }

    /// <summary>
    /// Find the anim folder for a tileset.
    /// </summary>
    public string? FindAnimFolder(string tilesetName, bool isSecondary)
    {
        var result = _resolver.FindTilesetPath(tilesetName);
        if (result == null) return null;

        var animPath = Path.Combine(result.Value.Path, "anim");
        if (Directory.Exists(animPath))
            return animPath;

        return null;
    }

    /// <summary>
    /// Scan for animation frame images in an anim subfolder.
    /// </summary>
    public List<string> ScanAnimationFrames(string tilesetName, string animFolderName, bool isSecondary)
    {
        var animFolder = FindAnimFolder(tilesetName, isSecondary);
        if (animFolder == null) return new List<string>();

        var animSubfolder = Path.Combine(animFolder, animFolderName);
        if (!Directory.Exists(animSubfolder)) return new List<string>();

        // Find all frame images (0.png, 1.png, etc.)
        var frames = new List<(int Index, string Path)>();
        foreach (var file in Directory.GetFiles(animSubfolder, "*.png"))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            if (int.TryParse(name, out var index))
            {
                frames.Add((index, file));
            }
        }

        // Sort by frame number and return paths
        frames.Sort((a, b) => a.Index.CompareTo(b.Index));
        return frames.Select(f => f.Path).ToList();
    }

    /// <summary>
    /// Extract individual 8x8 tiles from an animation frame image.
    /// Handles various layouts: vertical strips, horizontal strips, 2-column grids.
    /// </summary>
    public List<Image<Rgba32>> ExtractTilesFromFrame(
        Image<Rgba32> frameImage,
        int numTiles,
        int tileSize = 8)
    {
        var tiles = new List<Image<Rgba32>>();

        // Calculate dimensions
        int tilesPerRow = frameImage.Width / tileSize;
        int tilesPerCol = frameImage.Height / tileSize;
        int totalAvailableTiles = tilesPerRow * tilesPerCol;

        if (tilesPerRow == 0) tilesPerRow = 1;
        if (tilesPerCol == 0) tilesPerCol = 1;

        // Determine layout type and extract tiles
        bool isSingleColumn = (tilesPerRow == 1 && tilesPerCol >= numTiles);
        bool isSingleRow = (tilesPerCol == 1 && tilesPerRow >= numTiles);

        for (int i = 0; i < numTiles; i++)
        {
            int x, y;

            if (isSingleColumn)
            {
                // Vertical strip: tiles stacked top to bottom
                x = 0;
                y = i * tileSize;
            }
            else if (isSingleRow)
            {
                // Horizontal strip: tiles side by side
                x = i * tileSize;
                y = 0;
            }
            else
            {
                // Grid layout: left-to-right, top-to-bottom
                int col = i % tilesPerRow;
                int row = i / tilesPerRow;
                x = col * tileSize;
                y = row * tileSize;
            }

            // Bounds check
            if (x + tileSize <= frameImage.Width && y + tileSize <= frameImage.Height)
            {
                var tile = frameImage.Clone(ctx => ctx.Crop(new Rectangle(x, y, tileSize, tileSize)));
                tiles.Add(tile);
            }
            else
            {
                // Create transparent tile for out-of-bounds
                tiles.Add(new Image<Rgba32>(tileSize, tileSize, new Rgba32(0, 0, 0, 0)));
            }
        }

        return tiles;
    }

    /// <summary>
    /// Extract animation frame images with proper palette handling.
    /// Returns a list of frame images (each frame contains all tiles for that frame).
    /// </summary>
    public List<Image<Rgba32>> ExtractAnimationFrames(
        string tilesetName,
        AnimationDefinition animDef,
        Rgba32[]?[]? palettes,
        int paletteIndex = 0)
    {
        var frames = new List<Image<Rgba32>>();
        var framePaths = ScanAnimationFrames(tilesetName, animDef.AnimFolder, animDef.IsSecondary);

        if (framePaths.Count == 0) return frames;

        // Select the appropriate palette
        Rgba32[]? palette = null;
        if (palettes != null && paletteIndex >= 0 && paletteIndex < palettes.Length)
        {
            palette = palettes[paletteIndex];
        }
        palette ??= palettes?.FirstOrDefault(p => p != null);

        foreach (var framePath in framePaths)
        {
            try
            {
                var processed = LoadAndApplyPaletteToFrame(framePath, palette);
                if (processed != null)
                {
                    frames.Add(processed);
                }
            }
            catch
            {
                // Skip frames that fail to load
            }
        }

        return frames;
    }

    /// <summary>
    /// Load an animation frame PNG and apply palette.
    /// Prefers the PNG's embedded palette over external palette.
    /// </summary>
    private Image<Rgba32>? LoadAndApplyPaletteToFrame(string framePath, Rgba32[]? externalPalette)
    {
        var pngBytes = File.ReadAllBytes(framePath);

        // Extract embedded palette from PNG (animation frames typically have their own colors)
        var embeddedPalette = IndexedPngLoader.ExtractPalette(pngBytes);
        var (indices, width, height, bitDepth) = IndexedPngLoader.ExtractPixelIndices(pngBytes);

        if (indices == null || width == 0 || height == 0)
        {
            // Fallback: load as RGBA (already has colors applied)
            using var image = Image.Load<Rgba32>(framePath);
            // Apply transparency to index 0 equivalent
            var result = image.Clone();
            result.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < accessor.Height; y++)
                {
                    var row = accessor.GetRowSpan(y);
                    for (int x = 0; x < row.Length; x++)
                    {
                        // Check if this is the background color (first pixel or white)
                        if (row[x].R == 255 && row[x].G == 255 && row[x].B == 255)
                        {
                            row[x] = new Rgba32(0, 0, 0, 0);
                        }
                    }
                }
            });
            return result;
        }

        // Prefer embedded palette over external palette
        var palette = embeddedPalette ?? externalPalette;

        var resultImage = new Image<Rgba32>(width, height);

        resultImage.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (int x = 0; x < width; x++)
                {
                    var colorIndex = indices[y * width + x];

                    if (colorIndex == 0)
                    {
                        row[x] = new Rgba32(0, 0, 0, 0);
                    }
                    else if (palette != null && colorIndex < palette.Length)
                    {
                        row[x] = palette[colorIndex];
                    }
                    else
                    {
                        var gray = (byte)(colorIndex * 17);
                        row[x] = new Rgba32(gray, gray, gray, 255);
                    }
                }
            }
        });

        return resultImage;
    }

    /// <summary>
    /// Fallback palette application for non-indexed images.
    /// </summary>
    private Image<Rgba32> ApplyPaletteToFrameFallback(Image<Rgba32> sourceImage, Rgba32[]? palette)
    {
        var result = new Image<Rgba32>(sourceImage.Width, sourceImage.Height);

        sourceImage.ProcessPixelRows(result, (srcAccessor, dstAccessor) =>
        {
            for (int y = 0; y < srcAccessor.Height; y++)
            {
                var srcRow = srcAccessor.GetRowSpan(y);
                var dstRow = dstAccessor.GetRowSpan(y);

                for (int x = 0; x < srcAccessor.Width; x++)
                {
                    var pixel = srcRow[x];
                    var colorIndex = (byte)(15 - (pixel.R + 8) / 17);

                    if (colorIndex == 0)
                    {
                        dstRow[x] = new Rgba32(0, 0, 0, 0);
                    }
                    else if (palette != null && colorIndex < palette.Length)
                    {
                        dstRow[x] = palette[colorIndex];
                    }
                    else
                    {
                        var gray = (byte)(colorIndex * 17);
                        dstRow[x] = new Rgba32(gray, gray, gray, 255);
                    }
                }
            }
        });

        return result;
    }

    /// <summary>
    /// Extract raw palette indices from PNG IDAT chunks.
    /// </summary>
    private static (int Width, int Height, int BitDepth, byte[]? Indices) ExtractPngIndices(byte[] pngData)
    {
        int width = 0, height = 0, bitDepth = 0, colorType = 0;
        var idatChunks = new List<byte[]>();
        var pos = 8;

        while (pos < pngData.Length - 12)
        {
            var length = (pngData[pos] << 24) | (pngData[pos + 1] << 16) |
                         (pngData[pos + 2] << 8) | pngData[pos + 3];
            var type = System.Text.Encoding.ASCII.GetString(pngData, pos + 4, 4);

            if (type == "IHDR")
            {
                var dataStart = pos + 8;
                width = (pngData[dataStart] << 24) | (pngData[dataStart + 1] << 16) |
                        (pngData[dataStart + 2] << 8) | pngData[dataStart + 3];
                height = (pngData[dataStart + 4] << 24) | (pngData[dataStart + 5] << 16) |
                         (pngData[dataStart + 6] << 8) | pngData[dataStart + 7];
                bitDepth = pngData[dataStart + 8];
                colorType = pngData[dataStart + 9];
            }
            else if (type == "IDAT")
            {
                var chunk = new byte[length];
                Array.Copy(pngData, pos + 8, chunk, 0, length);
                idatChunks.Add(chunk);
            }
            else if (type == "IEND") break;

            pos += 12 + length;
        }

        if (colorType != 3 || width == 0 || height == 0)
            return (width, height, bitDepth, null);

        var totalLength = idatChunks.Sum(c => c.Length);
        var compressedData = new byte[totalLength];
        var offset = 0;
        foreach (var chunk in idatChunks)
        {
            Array.Copy(chunk, 0, compressedData, offset, chunk.Length);
            offset += chunk.Length;
        }

        byte[] decompressedData;
        try
        {
            using var compressedStream = new MemoryStream(compressedData, 2, compressedData.Length - 2);
            using var deflateStream = new DeflateStream(compressedStream, CompressionMode.Decompress);
            using var decompressedStream = new MemoryStream();
            deflateStream.CopyTo(decompressedStream);
            decompressedData = decompressedStream.ToArray();
        }
        catch { return (width, height, bitDepth, null); }

        var pixelsPerByte = 8 / bitDepth;
        var bytesPerRow = (width + pixelsPerByte - 1) / pixelsPerByte;
        var rowSize = bytesPerRow + 1;

        var indices = new byte[width * height];
        var prevRow = new byte[bytesPerRow];

        for (var y = 0; y < height; y++)
        {
            var rowStart = y * rowSize;
            if (rowStart >= decompressedData.Length) break;

            var filterType = decompressedData[rowStart];
            var currentRow = new byte[bytesPerRow];
            var dataStart = rowStart + 1;
            var copyLen = Math.Min(bytesPerRow, decompressedData.Length - dataStart);
            if (copyLen > 0) Array.Copy(decompressedData, dataStart, currentRow, 0, copyLen);

            switch (filterType)
            {
                case 0: break;
                case 1: for (var i = 1; i < bytesPerRow; i++) currentRow[i] = (byte)(currentRow[i] + currentRow[i - 1]); break;
                case 2: for (var i = 0; i < bytesPerRow; i++) currentRow[i] = (byte)(currentRow[i] + prevRow[i]); break;
                case 3: for (var i = 0; i < bytesPerRow; i++) { var left = i > 0 ? currentRow[i - 1] : 0; currentRow[i] = (byte)(currentRow[i] + (left + prevRow[i]) / 2); } break;
                case 4: for (var i = 0; i < bytesPerRow; i++) { var a = i > 0 ? currentRow[i - 1] : 0; var b = prevRow[i]; var c = i > 0 ? prevRow[i - 1] : 0; currentRow[i] = (byte)(currentRow[i] + IndexedPngLoader.PaethPredictor(a, b, c)); } break;
            }

            for (var x = 0; x < width; x++)
            {
                int index;
                if (bitDepth == 8) { index = x < currentRow.Length ? currentRow[x] : 0; }
                else if (bitDepth == 4)
                {
                    var byteIndex = x / 2;
                    var nibble = x % 2;
                    index = byteIndex < currentRow.Length
                        ? (nibble == 0 ? (currentRow[byteIndex] >> 4) & 0x0F : currentRow[byteIndex] & 0x0F)
                        : 0;
                }
                else if (bitDepth == 2) { var byteIndex = x / 4; var shift = 6 - (x % 4) * 2; index = byteIndex < currentRow.Length ? (currentRow[byteIndex] >> shift) & 0x03 : 0; }
                else if (bitDepth == 1) { var byteIndex = x / 8; var shift = 7 - (x % 8); index = byteIndex < currentRow.Length ? (currentRow[byteIndex] >> shift) & 0x01 : 0; }
                else index = 0;
                indices[y * width + x] = (byte)index;
            }
            Array.Copy(currentRow, prevRow, bytesPerRow);
        }
        return (width, height, bitDepth, indices);
    }

    /// <summary>
    /// Normalize tileset name for lookup in animation mappings.
    /// </summary>
    private static string NormalizeTilesetName(string name)
    {
        // Remove common prefixes
        foreach (var prefix in new[] { "gTileset_", "Tileset_", "g_tileset_" })
        {
            if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                name = name[prefix.Length..];
                break;
            }
        }

        // Convert to snake_case and lowercase
        return ToSnakeCase(name).ToLowerInvariant();
    }

    private static string ToSnakeCase(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;

        var result = new System.Text.StringBuilder();
        result.Append(char.ToLower(text[0]));

        for (int i = 1; i < text.Length; i++)
        {
            if (char.IsUpper(text[i]))
            {
                result.Append('_');
                result.Append(char.ToLower(text[i]));
            }
            else
            {
                result.Append(text[i]);
            }
        }

        return result.ToString();
    }
}
