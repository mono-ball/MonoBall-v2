using SixLabors.ImageSharp.PixelFormats;

namespace Porycon3.Services;

/// <summary>
/// Loads JASC-PAL format palette files used by pokeemerald.
/// </summary>
public static class PaletteLoader
{
    /// <summary>
    /// Load a JASC-PAL format palette file.
    /// Format:
    /// JASC-PAL
    /// 0100
    /// &lt;num_colors&gt;
    /// &lt;r&gt; &lt;g&gt; &lt;b&gt;
    /// ...
    /// </summary>
    public static Rgba32[]? LoadPalette(string path)
    {
        if (!File.Exists(path))
            return null;

        try
        {
            var lines = File.ReadAllLines(path);
            if (lines.Length < 3)
                return null;

            // Check header
            if (lines[0].Trim() != "JASC-PAL")
                return null;

            // Skip version line (0100)
            // Read number of colors
            if (!int.TryParse(lines[2].Trim(), out var numColors))
                return null;

            var palette = new Rgba32[16];
            // Initialize with transparent black
            for (int i = 0; i < 16; i++)
                palette[i] = new Rgba32(0, 0, 0, 0);

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
                    // Color 0 is transparent in GBA
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
    /// Load all 16 palettes (00.pal - 15.pal) from a tileset directory.
    /// </summary>
    public static Rgba32[]?[] LoadTilesetPalettes(string tilesetDir)
    {
        var palettes = new Rgba32[]?[16];
        var palettesDir = Path.Combine(tilesetDir, "palettes");

        for (int i = 0; i < 16; i++)
        {
            var palPath = Path.Combine(palettesDir, $"{i:D2}.pal");
            palettes[i] = LoadPalette(palPath);
        }

        return palettes;
    }
}
