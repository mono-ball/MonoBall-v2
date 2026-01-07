using System;
using Cake.Common.IO;
using Cake.Common.Tools.DotNet;
using Cake.Common.Tools.DotNet.Publish;
using Cake.Core.Diagnostics;
using Cake.Frosting;

namespace MonoBall.Build.Tasks
{
    /// <summary>
    /// Task that publishes the application.
    /// </summary>
    [TaskName("Publish")]
    [IsDependentOn(typeof(BuildTask))]
    [IsDependentOn(typeof(GenerateApiDocsTask))]
    [IsDependentOn(typeof(CopyModsTask))]
    public sealed class PublishTask : FrostingTask<BuildContext>
    {
        /// <summary>
        /// Runs the publish task.
        /// </summary>
        /// <param name="context">The build context.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="context"/> is null.</exception>
        /// <exception cref="InvalidOperationException">Thrown when publish fails.</exception>
        public override void Run(BuildContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            context.Log.Information("Publishing application...");

            // Publish DesktopGL project
            context.DotNetPublish(
                context.DesktopGLProjectPath.FullPath,
                new DotNetPublishSettings
                {
                    Configuration = context.Configuration,
                    OutputDirectory = context.PublishDirectory,
                });

            // Copy mods to publish directory (similar to CopyModsTask)
            if (!context.SkipModCopy && context.PublishDirectory != null)
            {
                var modsPublishDir = context.PublishDirectory.Combine("Mods");

                // Delete existing Mods directory if it exists
                if (context.DirectoryExists(modsPublishDir))
                {
                    context.DeleteDirectory(modsPublishDir, new Cake.Common.IO.DeleteDirectorySettings { Recursive = true, Force = true });
                }

                // Create Mods directory
                context.EnsureDirectoryExists(modsPublishDir);

                // Copy mod.manifest file if it exists
                var manifestSource = context.ModsDirectory.CombineWithFilePath("mod.manifest");
                if (context.FileExists(manifestSource))
                {
                    var manifestDest = modsPublishDir.CombineWithFilePath("mod.manifest");
                    context.CopyFile(manifestSource, manifestDest);
                }

                // Copy all .monoball archives
                var compressedMods = context.GetFiles($"{context.ModsDirectory}/*.monoball");
                foreach (var modArchive in compressedMods)
                {
                    var destPath = modsPublishDir.CombineWithFilePath(modArchive.GetFilename());
                    context.CopyFile(modArchive, destPath);
                }

                context.Log.Information($"Mods copied to publish directory. Copied {compressedMods.Count} mod archive(s).");
            }

            context.Log.Information("Publish completed successfully.");
        }
    }
}
