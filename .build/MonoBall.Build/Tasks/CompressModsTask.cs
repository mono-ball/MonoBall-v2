using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Cake.Common.IO;
using Cake.Core;
using Cake.Core.Diagnostics;
using Cake.Core.IO;
using Cake.Frosting;

namespace MonoBall.Build.Tasks
{
    /// <summary>
    /// Task that compresses mod directories to .monoball archives.
    /// </summary>
    [TaskName("CompressMods")]
    [IsDependentOn(typeof(BuildArchiveToolTask))]
    [IsDependentOn(typeof(CompileShadersTask))]
    public sealed class CompressModsTask : FrostingTask<BuildContext>
    {
        private const string ArchiveExtension = ".monoball";
        private const int CompressionLevel = 1;

        /// <summary>
        /// Runs the mod compression task.
        /// </summary>
        /// <param name="context">The build context.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="context"/> is null.</exception>
        /// <exception cref="FileNotFoundException">Thrown when ArchiveTool executable is not found.</exception>
        /// <exception cref="InvalidOperationException">Thrown when mod compression fails.</exception>
        public override void Run(BuildContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            if (context.SkipModCompression)
            {
                context.Log.Information("Skipping mod compression (--skip-mod-compression flag set).");
                return;
            }

            if (!context.FileExists(context.ArchiveToolExecutable))
            {
                throw new FileNotFoundException(
                    $"ArchiveTool executable not found: {context.ArchiveToolExecutable}. " +
                    "Ensure BuildArchiveToolTask completed successfully.",
                    context.ArchiveToolExecutable.FullPath);
            }

            if (!context.DirectoryExists(context.ModsDirectory))
            {
                context.Log.Warning($"Mods directory not found: {context.ModsDirectory}. Skipping mod compression.");
                return;
            }

            context.Log.Information("Compressing mods...");

            // Discover all directories in Mods folder (excluding .monoball files)
            var modDirectories = context.GetDirectories($"{context.ModsDirectory}/*")
                .Where(dir => !dir.GetDirectoryName().EndsWith(ArchiveExtension))
                .ToList();

            var failedMods = new List<string>();

            foreach (var modDir in modDirectories)
            {
                try
                {
                    var archiveName = $"{modDir.GetDirectoryName()}{ArchiveExtension}";
                    var archivePath = context.ModsDirectory.CombineWithFilePath(archiveName);
                    CompressMod(context, modDir, archivePath);
                }
                catch (Exception ex)
                {
                    context.Log.Error($"Failed to compress mod {modDir}: {ex.Message}");
                    failedMods.Add(modDir.FullPath);
                }
            }

            if (failedMods.Any())
            {
                throw new InvalidOperationException(
                    $"Failed to compress {failedMods.Count} mod(s). " +
                    $"Failed mods: {string.Join(", ", failedMods)}");
            }

            context.Log.Information($"Mod compression complete. Compressed {modDirectories.Count} mod(s).");
        }

        private static void CompressMod(BuildContext context, DirectoryPath modDir, FilePath archivePath)
        {
            context.Log.Debug($"Compressing {modDir} -> {archivePath}");

            // BuildContext inherits from FrostingContext which provides ICakeContext methods
            var args = new ProcessArgumentBuilder();
            args.Append("pack");
            args.AppendQuoted(modDir.FullPath);
            args.Append("--output");
            args.AppendQuoted(archivePath.FullPath);
            args.Append("--compression-level");
            args.Append(CompressionLevel.ToString());

            var process = context.ProcessRunner.Start(
                context.ArchiveToolExecutable,
                new ProcessSettings
                {
                    Arguments = args
                });

            process.WaitForExit();
            var exitCode = process.GetExitCode();
            if (exitCode != 0)
            {
                throw new InvalidOperationException(
                    $"Mod compression failed for {modDir} with exit code {exitCode}.");
            }
        }
    }
}
