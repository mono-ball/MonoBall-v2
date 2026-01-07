using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using K4os.Compression.LZ4;

namespace MonoBall.ArchiveTool;

/// <summary>
///     Creates .monoball archive files from mod directories.
/// </summary>
public class ArchiveCreator
{
    private const string Magic = "MONOBALL";
    private const ushort CurrentVersion = 1;
    private const int DefaultCompressionLevel = 1; // LZ4 fast compression

    /// <summary>
    ///     Progress callback for reporting compression progress.
    ///     Parameters: current file index, total files, current file path, bytes processed, total bytes.
    /// </summary>
    public Action<int, int, string, long, long>? ProgressCallback { get; set; }

    /// <summary>
    ///     Creates a .monoball archive from a mod directory.
    /// </summary>
    /// <param name="modDirectory">Path to the mod directory to compress.</param>
    /// <param name="outputPath">Path where the archive will be created.</param>
    /// <param name="compressionLevel">LZ4 compression level (1-9, default 1 for fastest).</param>
    /// <exception cref="ArgumentNullException">Thrown when modDirectory or outputPath is null.</exception>
    /// <exception cref="DirectoryNotFoundException">Thrown when mod directory doesn't exist.</exception>
    /// <exception cref="IOException">Thrown when file I/O operations fail.</exception>
    /// <exception cref="UnauthorizedAccessException">Thrown when access is denied.</exception>
    public void CreateArchive(string modDirectory, string outputPath, int compressionLevel = DefaultCompressionLevel)
    {
        if (modDirectory == null) throw new ArgumentNullException(nameof(modDirectory));

        if (outputPath == null) throw new ArgumentNullException(nameof(outputPath));

        if (!Directory.Exists(modDirectory))
            throw new DirectoryNotFoundException($"Mod directory not found: {modDirectory}");

        if (compressionLevel < 1 || compressionLevel > 9)
            throw new ArgumentOutOfRangeException(
                nameof(compressionLevel),
                compressionLevel,
                "Compression level must be between 1 and 9."
            );

        // Ensure output directory exists
        var outputDir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir)) Directory.CreateDirectory(outputDir);

        var files = Directory.GetFiles(modDirectory, "*", SearchOption.AllDirectories);
        var toc = new List<FileEntry>();
        var dataOffset = 18UL; // Header size (8 bytes magic + 2 bytes version + 8 bytes TOC offset)
        var totalFiles = files.Length;
        long totalBytesProcessed = 0;
        var totalBytes = GetTotalSize(files);

        try
        {
            using (var writer = new BinaryWriter(File.Create(outputPath), Encoding.UTF8, false))
            {
                // Write header (placeholder TOC offset)
                writer.Write(Encoding.ASCII.GetBytes(Magic));
                writer.Write(CurrentVersion);
                writer.Write((ulong)0); // TOC offset (fill later)

                // Compress and write each file
                for (var i = 0; i < files.Length; i++)
                {
                    var file = files[i];
                    var relativePath = Path.GetRelativePath(modDirectory, file)
                        .Replace('\\', '/')
                        .TrimStart('/');

                    byte[] uncompressed;
                    try
                    {
                        uncompressed = File.ReadAllBytes(file);
                    }
                    catch (IOException ex)
                    {
                        throw new IOException($"Failed to read file '{file}': {ex.Message}", ex);
                    }
                    catch (UnauthorizedAccessException ex)
                    {
                        throw new UnauthorizedAccessException(
                            $"Access denied when reading file '{file}': {ex.Message}",
                            ex
                        );
                    }

                    byte[] compressed;
                    try
                    {
                        if (uncompressed.Length == 0)
                        {
                            // Empty files don't need compression
                            compressed = Array.Empty<byte>();
                        }
                        else
                        {
                            // Allocate destination buffer
                            var maxCompressedSize = LZ4Codec.MaximumOutputSize(uncompressed.Length);
                            var compressedBuffer = new byte[maxCompressedSize];

                            // Convert compression level (1-9) to LZ4Level enum
                            // Note: LZ4Level enum has L00_FAST, then L03_HC through L09_HC
                            var level = compressionLevel switch
                            {
                                1 => LZ4Level.L00_FAST,
                                2 => LZ4Level.L00_FAST, // No L01/L02, use L00_FAST
                                3 => LZ4Level.L03_HC,
                                4 => LZ4Level.L04_HC,
                                5 => LZ4Level.L05_HC,
                                6 => LZ4Level.L06_HC,
                                7 => LZ4Level.L07_HC,
                                8 => LZ4Level.L08_HC,
                                9 => LZ4Level.L09_HC,
                                _ => LZ4Level.L00_FAST
                            };

                            // Compress data
                            var compressedLength = LZ4Codec.Encode(
                                uncompressed.AsSpan(),
                                compressedBuffer.AsSpan(),
                                level
                            );

                            // Trim to actual compressed size
                            compressed = new byte[compressedLength];
                            Array.Copy(compressedBuffer, compressed, compressedLength);
                        }
                    }
                    catch (Exception ex)
                    {
                        throw new InvalidOperationException(
                            $"Failed to compress file '{file}': {ex.Message}",
                            ex
                        );
                    }

                    toc.Add(new FileEntry
                    {
                        Path = relativePath,
                        UncompressedSize = (ulong)uncompressed.Length,
                        CompressedSize = (ulong)compressed.Length,
                        DataOffset = dataOffset
                    });

                    if (compressed.Length > 0) writer.Write(compressed);

                    dataOffset += (ulong)compressed.Length;
                    totalBytesProcessed += uncompressed.Length;

                    // Report progress
                    ProgressCallback?.Invoke(i + 1, totalFiles, relativePath, totalBytesProcessed, totalBytes);
                }

                // Write TOC
                var tocOffset = (ulong)writer.BaseStream.Position;
                writer.Write((uint)toc.Count);

                foreach (var entry in toc)
                {
                    var pathBytes = Encoding.UTF8.GetBytes(entry.Path);
                    if (pathBytes.Length > ushort.MaxValue)
                        throw new InvalidOperationException(
                            $"Path too long: '{entry.Path}' ({pathBytes.Length} bytes, max {ushort.MaxValue})"
                        );

                    writer.Write((ushort)pathBytes.Length);
                    writer.Write(pathBytes);
                    writer.Write(entry.UncompressedSize);
                    writer.Write(entry.CompressedSize);
                    writer.Write(entry.DataOffset);
                }

                // Update header with TOC offset
                writer.BaseStream.Position = 10; // After magic (8) + version (2)
                writer.Write(tocOffset);
            }
        }
        catch (IOException ex)
        {
            throw new IOException($"Failed to create archive '{outputPath}': {ex.Message}", ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new UnauthorizedAccessException(
                $"Access denied when creating archive '{outputPath}': {ex.Message}",
                ex
            );
        }
    }

    private static long GetTotalSize(string[] files)
    {
        long total = 0;
        foreach (var file in files)
            try
            {
                total += new FileInfo(file).Length;
            }
            catch
            {
                // Ignore files we can't access
            }

        return total;
    }

    /// <summary>
    ///     Internal file entry structure for TOC.
    /// </summary>
    private class FileEntry
    {
        public string Path { get; set; } = string.Empty;
        public ulong UncompressedSize { get; set; }
        public ulong CompressedSize { get; set; }
        public ulong DataOffset { get; set; }
    }
}