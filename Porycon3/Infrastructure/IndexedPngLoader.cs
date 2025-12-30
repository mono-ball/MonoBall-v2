using System.IO.Compression;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

namespace Porycon3.Infrastructure;

/// <summary>
/// Shared utilities for loading and processing indexed (paletted) PNG images.
/// Handles 4bpp and 8bpp indexed PNGs from pokeemerald-expansion.
/// </summary>
public static class IndexedPngLoader
{
    /// <summary>
    /// Extracts the RGB palette from a PNG file's PLTE chunk.
    /// </summary>
    public static Rgba32[]? ExtractPalette(byte[] pngData)
    {
        var pos = 8; // Skip PNG signature
        while (pos < pngData.Length - 12)
        {
            var length = ReadInt32BigEndian(pngData, pos);
            var type = System.Text.Encoding.ASCII.GetString(pngData, pos + 4, 4);

            if (type == "PLTE")
            {
                var colorCount = length / 3;
                var palette = new Rgba32[colorCount];
                for (var i = 0; i < colorCount; i++)
                {
                    var offset = pos + 8 + i * 3;
                    palette[i] = new Rgba32(pngData[offset], pngData[offset + 1], pngData[offset + 2], 255);
                }
                return palette;
            }

            pos += 12 + length; // 4 length + 4 type + data + 4 CRC
        }
        return null;
    }

    /// <summary>
    /// Extracts the RGB palette from a PNG file.
    /// </summary>
    public static Rgba32[]? ExtractPalette(string filePath)
    {
        if (!File.Exists(filePath)) return null;
        return ExtractPalette(File.ReadAllBytes(filePath));
    }

    /// <summary>
    /// Extracts raw pixel indices from an indexed PNG's IDAT chunk.
    /// Handles PNG filtering (None, Sub, Up, Average, Paeth).
    /// </summary>
    public static (byte[]? Indices, int Width, int Height, int BitDepth) ExtractPixelIndices(byte[] pngData)
    {
        int width = 0, height = 0, bitDepth = 0, colorType = 0;
        var idatChunks = new List<byte[]>();

        var pos = 8; // Skip PNG signature
        while (pos < pngData.Length - 12)
        {
            var length = ReadInt32BigEndian(pngData, pos);
            var type = System.Text.Encoding.ASCII.GetString(pngData, pos + 4, 4);

            if (type == "IHDR" && length >= 13)
            {
                width = ReadInt32BigEndian(pngData, pos + 8);
                height = ReadInt32BigEndian(pngData, pos + 12);
                bitDepth = pngData[pos + 16];
                colorType = pngData[pos + 17];
            }
            else if (type == "IDAT")
            {
                var chunk = new byte[length];
                Array.Copy(pngData, pos + 8, chunk, 0, length);
                idatChunks.Add(chunk);
            }
            else if (type == "IEND")
            {
                break;
            }

            pos += 12 + length;
        }

        // Only indexed color type (3) is supported
        if (colorType != 3 || width == 0 || height == 0)
            return (null, 0, 0, 0);

        var compressedData = idatChunks.SelectMany(c => c).ToArray();
        byte[] decompressed;

        try
        {
            using var compressedStream = new MemoryStream(compressedData);
            using var zlibStream = new ZLibStream(compressedStream, CompressionMode.Decompress);
            using var outputStream = new MemoryStream();
            zlibStream.CopyTo(outputStream);
            decompressed = outputStream.ToArray();
        }
        catch
        {
            return (null, 0, 0, 0);
        }

        return DecodeFilteredScanlines(decompressed, width, height, bitDepth);
    }

    /// <summary>
    /// Decodes PNG-filtered scanlines into raw pixel indices.
    /// </summary>
    private static (byte[]? Indices, int Width, int Height, int BitDepth) DecodeFilteredScanlines(
        byte[] decompressed, int width, int height, int bitDepth)
    {
        var indices = new byte[width * height];
        var scanlineWidth = (width * bitDepth + 7) / 8;
        var previousScanline = new byte[scanlineWidth];

        var srcPos = 0;
        for (int y = 0; y < height; y++)
        {
            if (srcPos >= decompressed.Length) break;

            var filterType = decompressed[srcPos++];
            var scanline = new byte[scanlineWidth];

            for (int i = 0; i < scanlineWidth && srcPos < decompressed.Length; i++)
            {
                var raw = decompressed[srcPos++];
                scanline[i] = filterType switch
                {
                    0 => raw, // None
                    1 => (byte)(raw + (i > 0 ? scanline[i - 1] : 0)), // Sub
                    2 => (byte)(raw + previousScanline[i]), // Up
                    3 => (byte)(raw + ((i > 0 ? scanline[i - 1] : 0) + previousScanline[i]) / 2), // Average
                    4 => (byte)(raw + PaethPredictor(
                        i > 0 ? scanline[i - 1] : 0,
                        previousScanline[i],
                        i > 0 ? previousScanline[i - 1] : 0)), // Paeth
                    _ => raw
                };
            }

            Array.Copy(scanline, previousScanline, scanlineWidth);

            // Extract indices from packed scanline
            for (int x = 0; x < width; x++)
            {
                int byteIdx = (x * bitDepth) / 8;
                int bitOffset = 8 - bitDepth - ((x * bitDepth) % 8);

                if (byteIdx < scanline.Length)
                {
                    var mask = (1 << bitDepth) - 1;
                    var idx = (scanline[byteIdx] >> bitOffset) & mask;
                    indices[y * width + x] = (byte)idx;
                }
            }
        }

        return (indices, width, height, bitDepth);
    }

    /// <summary>
    /// Paeth predictor filter used in PNG decompression.
    /// </summary>
    public static int PaethPredictor(int a, int b, int c)
    {
        var p = a + b - c;
        var pa = Math.Abs(p - a);
        var pb = Math.Abs(p - b);
        var pc = Math.Abs(p - c);

        if (pa <= pb && pa <= pc) return a;
        if (pb <= pc) return b;
        return c;
    }

    /// <summary>
    /// Saves an RGBA image as a PNG with optimal settings.
    /// </summary>
    public static void SaveAsRgbaPng(Image<Rgba32> image, string path)
    {
        var encoder = new PngEncoder
        {
            ColorType = PngColorType.RgbWithAlpha,
            BitDepth = PngBitDepth.Bit8,
            CompressionLevel = PngCompressionLevel.BestCompression
        };
        image.SaveAsPng(path, encoder);
    }

    /// <summary>
    /// Loads an indexed PNG and converts it to RGBA using its embedded palette.
    /// Index 0 is treated as transparent.
    /// </summary>
    public static Image<Rgba32>? LoadWithIndex0Transparency(string filePath)
    {
        if (!File.Exists(filePath)) return null;

        var bytes = File.ReadAllBytes(filePath);
        var palette = ExtractPalette(bytes);
        if (palette == null || palette.Length == 0) return null;

        var (indices, width, height, _) = ExtractPixelIndices(bytes);
        if (indices == null || width == 0 || height == 0) return null;

        var image = new Image<Rgba32>(width, height);
        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (int x = 0; x < width; x++)
                {
                    var idx = indices[y * width + x];
                    if (idx == 0)
                    {
                        row[x] = new Rgba32(0, 0, 0, 0); // Transparent
                    }
                    else if (idx < palette.Length)
                    {
                        row[x] = palette[idx];
                    }
                }
            }
        });

        return image;
    }

    /// <summary>
    /// Loads an indexed PNG and converts it to RGBA using the specified palette.
    /// </summary>
    public static Image<Rgba32>? LoadWithPalette(string filePath, Rgba32[] palette, bool index0Transparent = true)
    {
        if (!File.Exists(filePath)) return null;

        var bytes = File.ReadAllBytes(filePath);
        var (indices, width, height, _) = ExtractPixelIndices(bytes);
        if (indices == null || width == 0 || height == 0) return null;

        var image = new Image<Rgba32>(width, height);
        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (int x = 0; x < width; x++)
                {
                    var idx = indices[y * width + x];
                    if (index0Transparent && idx == 0)
                    {
                        row[x] = new Rgba32(0, 0, 0, 0);
                    }
                    else if (idx < palette.Length)
                    {
                        row[x] = palette[idx];
                    }
                }
            }
        });

        return image;
    }

    private static int ReadInt32BigEndian(byte[] data, int offset)
    {
        return (data[offset] << 24) | (data[offset + 1] << 16) |
               (data[offset + 2] << 8) | data[offset + 3];
    }
}
