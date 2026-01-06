using System;
using Cake.Common.IO;
using Cake.Core.Diagnostics;
using Cake.Frosting;

namespace MonoBall.Build.Tasks
{
    /// <summary>
    /// Task that copies mods to output directory.
    /// Note: This task only copies existing .monoball files - it does not compress or build mods.
    /// Mods should be compressed via CompressModsTask before this task runs.
    /// </summary>
    [TaskName("CopyMods")]
    [IsDependentOn(typeof(BuildTask))]
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

            context.Log.Information("Copying mods to output directory...");

            // Delete existing Mods directory in output if it exists
            var modsOutputDir = context.OutputDirectory.Combine("Mods");
            if (context.DirectoryExists(modsOutputDir))
            {
                context.DeleteDirectory(modsOutputDir, new Cake.Common.IO.DeleteDirectorySettings { Recursive = true, Force = true });
            }

            // Create Mods directory
            context.EnsureDirectoryExists(modsOutputDir);

            // Copy mod.manifest file if it exists
            var manifestSource = context.ModsDirectory.CombineWithFilePath("mod.manifest");
            if (context.FileExists(manifestSource))
            {
                var manifestDest = modsOutputDir.CombineWithFilePath("mod.manifest");
                context.CopyFile(manifestSource, manifestDest);
            }

            // Copy all .monoball archives
            var compressedMods = context.GetFiles($"{context.ModsDirectory}/*.monoball");
            foreach (var modArchive in compressedMods)
            {
                var destPath = modsOutputDir.CombineWithFilePath(modArchive.GetFilename());
                context.CopyFile(modArchive, destPath);
            }

            context.Log.Information($"Mod copy complete. Copied {compressedMods.Count} mod archive(s).");
        }
    }
}
