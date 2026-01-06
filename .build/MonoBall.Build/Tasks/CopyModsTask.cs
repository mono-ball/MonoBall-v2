using System;
using System.Collections.Generic;
using Cake.Common.IO;
using Cake.Core.Diagnostics;
using Cake.Core.IO;
using Cake.Frosting;

namespace MonoBall.Build.Tasks
{
    /// <summary>
    /// Task that copies mods to output directories for all platforms.
    /// Note: This task only copies existing .monoball files - it does not compress or build mods.
    /// Mods should be compressed via CompressModsTask before this task runs.
    /// This task has no dependencies and can be run independently.
    /// </summary>
    [TaskName("CopyMods")]
    public sealed class CopyModsTask : FrostingTask<BuildContext>
    {
        /// <summary>
        /// Runs the copy mods task.
        /// </summary>
        /// <param name="context">The build context.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="context"/> is null.</exception>
        public override void Run(BuildContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            if (context.SkipModCopy)
            {
                context.Log.Information("Skipping mod copy (--skip-mod-copy flag set).");
                return;
            }

            var configInfo = context.AllConfigurations
                ? "all configurations (Debug + Release)"
                : $"{context.Configuration} configuration";
            context.Log.Information($"Copying mods to output directories ({configInfo})...");

            // Get all output directories for different platforms
            var outputDirectories = GetOutputDirectories(context);
            var compressedMods = context.GetFiles($"{context.ModsDirectory}/*.monoball");
            var manifestSource = context.ModsDirectory.CombineWithFilePath("mod.manifest");

            var copiedCount = 0;
            foreach (var outputDir in outputDirectories)
            {
                if (CopyModsToDirectory(context, outputDir, compressedMods, manifestSource))
                {
                    copiedCount++;
                }
            }

            context.Log.Information($"Mod copy complete. Copied {compressedMods.Count} mod archive(s) to {copiedCount} output directories.");
        }

        /// <summary>
        /// Gets the output directories for all platforms (DesktopGL, DesktopVK, WindowsDX12).
        /// When AllConfigurations is true, returns directories for both Debug and Release.
        /// </summary>
        private static List<DirectoryPath> GetOutputDirectories(BuildContext context)
        {
            var directories = new List<DirectoryPath>();
            var basePath = context.RootDirectory;
            var framework = context.TargetFramework;

            // Determine which configurations to copy to
            var configurations = context.AllConfigurations
                ? new[] { "Debug", "Release" }
                : new[] { context.Configuration };

            foreach (var config in configurations)
            {
                // DesktopGL
                var desktopGLPath = basePath.Combine($"MonoBall.DesktopGL/bin/{config}/{framework}");
                directories.Add(desktopGLPath);

                // DesktopVK
                var desktopVKPath = basePath.Combine($"MonoBall.DesktopVK/bin/{config}/{framework}");
                directories.Add(desktopVKPath);

                // WindowsDX12
                var windowsDX12Path = basePath.Combine($"MonoBall.WindowsDX12/bin/{config}/{framework}");
                directories.Add(windowsDX12Path);
            }

            return directories;
        }

        /// <summary>
        /// Copies mods to a specific output directory.
        /// </summary>
        /// <returns>True if mods were copied, false if the directory was skipped.</returns>
        private static bool CopyModsToDirectory(
            BuildContext context,
            DirectoryPath outputDir,
            FilePathCollection compressedMods,
            FilePath manifestSource)
        {
            // Skip if output directory doesn't exist (platform may not have been built)
            if (!context.DirectoryExists(outputDir))
            {
                context.Log.Verbose($"Skipping {outputDir} (directory does not exist)");
                return false;
            }

            var modsOutputDir = outputDir.Combine("Mods");

            // Delete existing Mods directory in output if it exists
            if (context.DirectoryExists(modsOutputDir))
            {
                context.DeleteDirectory(modsOutputDir, new DeleteDirectorySettings { Recursive = true, Force = true });
            }

            // Create Mods directory
            context.EnsureDirectoryExists(modsOutputDir);

            // Copy mod.manifest file if it exists
            if (context.FileExists(manifestSource))
            {
                var manifestDest = modsOutputDir.CombineWithFilePath("mod.manifest");
                context.CopyFile(manifestSource, manifestDest);
            }

            // Copy all .monoball archives
            foreach (var modArchive in compressedMods)
            {
                var destPath = modsOutputDir.CombineWithFilePath(modArchive.GetFilename());
                context.CopyFile(modArchive, destPath);
            }

            context.Log.Information($"Copied mods to {modsOutputDir}");
            return true;
        }
    }
}
