namespace Porycon3.Infrastructure;

public class MapBinReader
{
    private readonly string _pokeemeraldPath;

    public MapBinReader(string pokeemeraldPath)
    {
        _pokeemeraldPath = pokeemeraldPath ?? throw new ArgumentNullException(nameof(pokeemeraldPath));

        if (!Directory.Exists(pokeemeraldPath))
            throw new DirectoryNotFoundException($"Pokeemerald path not found: {pokeemeraldPath}");
    }

    /// <summary>
    /// Reads map.bin which contains metatile indices for the map.
    /// Each entry is 2 bytes (little-endian).
    /// Lower 10 bits = metatile ID, upper bits = collision/elevation.
    /// </summary>
    public ushort[] ReadMapBin(string layoutId, int width, int height, string? blockdataPath = null)
    {
        if (string.IsNullOrWhiteSpace(layoutId))
            throw new ArgumentException("Layout ID cannot be null or empty", nameof(layoutId));

        if (width <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "Width must be positive");

        if (height <= 0)
            throw new ArgumentOutOfRangeException(nameof(height), "Height must be positive");

        string mapBinPath;
        if (!string.IsNullOrEmpty(blockdataPath))
        {
            // Use the actual path from layouts.json
            mapBinPath = Path.Combine(_pokeemeraldPath, blockdataPath);
        }
        else
        {
            // Fallback: Convert LAYOUT_ROUTE101 -> Route101
            var layoutFolder = LayoutIdToFolderName(layoutId);
            mapBinPath = Path.Combine(_pokeemeraldPath, "data", "layouts", layoutFolder, "map.bin");
        }

        if (!File.Exists(mapBinPath))
            throw new FileNotFoundException($"Map binary not found: {mapBinPath}");

        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(mapBinPath);
        }
        catch (Exception ex)
        {
            throw new IOException($"Failed to read map binary: {mapBinPath}", ex);
        }

        var expected = width * height * 2;

        if (bytes.Length < expected)
            throw new InvalidDataException(
                $"Map.bin too small for dimensions {width}×{height}: " +
                $"expected at least {expected} bytes, got {bytes.Length}");

        // Warn if file is larger than expected (may indicate wrong dimensions)
        if (bytes.Length > expected)
        {
            Console.WriteLine($"Warning: Map.bin is larger than expected ({bytes.Length} > {expected}). " +
                $"Dimensions may be incorrect or file may contain border data.");
        }

        var result = new ushort[width * height];

        try
        {
            for (int i = 0; i < result.Length; i++)
            {
                result[i] = BitConverter.ToUInt16(bytes, i * 2);
            }
        }
        catch (Exception ex)
        {
            throw new InvalidDataException("Failed to parse map binary data", ex);
        }

        return result;
    }

    /// <summary>
    /// Reads map.bin and returns a 2D array indexed by [y][x].
    /// </summary>
    public ushort[,] ReadMapBin2D(string layoutId, int width, int height)
    {
        var flat = ReadMapBin(layoutId, width, height);
        var result = new ushort[height, width];

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                result[y, x] = flat[y * width + x];
            }
        }

        return result;
    }

    /// <summary>
    /// Extracts just the metatile ID from a map entry.
    /// Lower 10 bits contain the metatile index (0-1023).
    /// </summary>
    public static int GetMetatileId(ushort mapEntry) => mapEntry & 0x3FF;

    /// <summary>
    /// Extracts collision override from a map entry.
    /// Bits 10-11 contain collision override (0=passable, 1-3=blocked).
    /// </summary>
    public static int GetCollision(ushort mapEntry) => (mapEntry >> 10) & 0x3;

    /// <summary>
    /// Checks if a map entry has collision enabled.
    /// </summary>
    public static bool HasCollision(ushort mapEntry)
    {
        var collision = GetCollision(mapEntry);
        return collision != 0;
    }

    /// <summary>
    /// Gets the elevation level from a map entry.
    /// </summary>
    public static int GetElevation(ushort mapEntry)
    {
        // Elevation is typically in bits 12-15 (upper 4 bits)
        return (mapEntry >> 12) & 0xF;
    }

    /// <summary>
    /// Creates a map entry from a metatile ID, collision override, and elevation.
    /// </summary>
    public static ushort CreateMapEntry(int metatileId, int collision = 0, int elevation = 0)
    {
        if (metatileId < 0 || metatileId > 0x3FF)
            throw new ArgumentOutOfRangeException(nameof(metatileId),
                "Metatile ID must be between 0 and 1023");

        if (collision < 0 || collision > 0x3)
            throw new ArgumentOutOfRangeException(nameof(collision),
                "Collision must be between 0 and 3");

        if (elevation < 0 || elevation > 0xF)
            throw new ArgumentOutOfRangeException(nameof(elevation),
                "Elevation must be between 0 and 15");

        return (ushort)((elevation << 12) | (collision << 10) | metatileId);
    }

    /// <summary>
    /// Converts LAYOUT_ROUTE101 -> Route101, LAYOUT_LITTLEROOT_TOWN -> LittlerootTown
    /// </summary>
    private static string LayoutIdToFolderName(string layoutId)
    {
        var name = layoutId;

        // Remove LAYOUT_ prefix
        if (name.StartsWith("LAYOUT_", StringComparison.OrdinalIgnoreCase))
        {
            name = name.Substring(7);
        }

        // Convert UNDERSCORE_CASE to PascalCase
        var parts = name.Split('_', StringSplitOptions.RemoveEmptyEntries);
        var result = string.Join("", parts.Select(p =>
            char.ToUpperInvariant(p[0]) + p.Substring(1).ToLowerInvariant()));

        return result;
    }
}
