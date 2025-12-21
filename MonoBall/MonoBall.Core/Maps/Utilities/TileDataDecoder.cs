using System;
using System.Text;

namespace MonoBall.Core.Maps.Utilities
{
    /// <summary>
    /// Utility class for decoding base64-encoded tile data from map layers.
    /// </summary>
    public static class TileDataDecoder
    {
        /// <summary>
        /// Decodes base64-encoded tile data into an array of tile indices.
        /// The tile data is encoded as base64 string containing 32-bit integers (little-endian).
        /// </summary>
        /// <param name="base64Data">The base64-encoded tile data string.</param>
        /// <param name="width">The width of the layer in tiles.</param>
        /// <param name="height">The height of the layer in tiles.</param>
        /// <returns>An array of tile indices (GIDs), or null if decoding fails.</returns>
        public static int[]? Decode(string? base64Data, int width, int height)
        {
            if (string.IsNullOrEmpty(base64Data))
            {
                return null;
            }

            try
            {
                // Decode base64 to bytes
                byte[] bytes = Convert.FromBase64String(base64Data);

                // Each tile is 4 bytes (32-bit integer, little-endian)
                int expectedSize = width * height * 4;
                if (bytes.Length < expectedSize)
                {
                    return null;
                }

                int[] tiles = new int[width * height];

                // Read little-endian 32-bit integers
                for (int i = 0; i < tiles.Length; i++)
                {
                    int offset = i * 4;
                    tiles[i] = BitConverter.ToInt32(bytes, offset);
                }

                return tiles;
            }
            catch
            {
                return null;
            }
        }
    }
}
