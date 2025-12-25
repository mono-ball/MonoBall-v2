using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
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
    /// Extract animation frame images with proper palette handling.
    /// Returns a list of frame images (each frame contains all tiles for that frame).
    /// </summary>
    public List<Image<Rgba32>> ExtractAnimationFrames(
        string tilesetName,
        AnimationDefinition animDef,
        Rgba32[]?[]? palettes)
    {
        var frames = new List<Image<Rgba32>>();
        var framePaths = ScanAnimationFrames(tilesetName, animDef.AnimFolder, animDef.IsSecondary);

        if (framePaths.Count == 0) return frames;

        foreach (var framePath in framePaths)
        {
            try
            {
                using var image = Image.Load<Rgba32>(framePath);

                // Check if this is a 16x16 metatile frame
                if (image.Width == 16 && image.Height == 16)
                {
                    // Apply palette and transparency
                    var processed = ApplyPaletteToFrame(image, palettes);
                    frames.Add(processed);
                }
                else
                {
                    // This is a tile strip - apply palette to entire image
                    var processed = ApplyPaletteToFrame(image, palettes);
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
    /// Apply palette to an animation frame image.
    /// Animation frames are indexed PNGs like tileset tiles.
    /// </summary>
    private Image<Rgba32> ApplyPaletteToFrame(Image<Rgba32> sourceImage, Rgba32[]?[]? palettes)
    {
        var result = new Image<Rgba32>(sourceImage.Width, sourceImage.Height);

        // Get the first available palette (animation frames typically use palette 0)
        Rgba32[]? palette = null;
        if (palettes != null)
        {
            foreach (var p in palettes)
            {
                if (p != null)
                {
                    palette = p;
                    break;
                }
            }
        }

        sourceImage.ProcessPixelRows(result, (srcAccessor, dstAccessor) =>
        {
            for (int y = 0; y < srcAccessor.Height; y++)
            {
                var srcRow = srcAccessor.GetRowSpan(y);
                var dstRow = dstAccessor.GetRowSpan(y);

                for (int x = 0; x < srcAccessor.Width; x++)
                {
                    var pixel = srcRow[x];

                    // Extract palette index from grayscale (inverted: white=0, black=15)
                    var colorIndex = (byte)(15 - (pixel.R + 8) / 17);

                    // Color index 0 is always transparent
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
                        // Fallback to grayscale
                        var gray = (byte)(colorIndex * 17);
                        dstRow[x] = new Rgba32(gray, gray, gray, 255);
                    }
                }
            }
        });

        return result;
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
