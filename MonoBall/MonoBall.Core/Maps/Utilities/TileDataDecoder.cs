using System;

namespace MonoBall.Core.Maps.Utilities;

/// <summary>
///     Utility class for decoding base64-encoded tile data from map layers.
/// </summary>
public static class TileDataDecoder
{
    /// <summary>
    ///     Decodes base64-encoded tile data into an array of tile indices.
    ///     The tile data is encoded as base64 string containing 32-bit unsigned integers (little-endian).
    ///     Tiled format uses unsigned 32-bit integers where the high 3 bits encode flip flags.
    /// </summary>
    /// <param name="base64Data">The base64-encoded tile data string.</param>
    /// <param name="width">The width of the layer in tiles.</param>
    /// <param name="height">The height of the layer in tiles.</param>
    /// <returns>An array of tile indices (GIDs with potential flip flags), or null if decoding fails.</returns>
    public static int[]? Decode(string? base64Data, int width, int height)
    {
        if (string.IsNullOrEmpty(base64Data))
            return null;

        try
        {
            // Decode base64 to bytes
            var bytes = Convert.FromBase64String(base64Data);

            // Each tile is 4 bytes (32-bit unsigned integer, little-endian)
            var expectedSize = width * height * 4;
            if (bytes.Length < expectedSize)
                return null;

            var tiles = new int[width * height];

            // Read little-endian 32-bit unsigned integers and convert to int
            // This preserves the bit pattern including flip flags in high bits
            // Casting uint to int preserves bits (0x80000000 becomes -2147483648, but bits are correct)
            for (var i = 0; i < tiles.Length; i++)
            {
                var offset = i * 4;
                var uintValue = BitConverter.ToUInt32(bytes, offset);
                tiles[i] = (int)uintValue; // Cast preserves bit pattern
            }

            return tiles;
        }
        catch
        {
            return null;
        }
    }
}
