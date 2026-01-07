using System;
using System.IO;
using Cake.Common.IO;
using Cake.Common.Tools.DotNet;
using Cake.Common.Tools.DotNet.Build;
using Cake.Core.Diagnostics;
using Cake.Frosting;

namespace MonoBall.Build.Tasks
{
    /// <summary>
    /// Task that builds the ArchiveTool project early so it can be used by CompressModsTask.
    /// </summary>
    [TaskName("BuildArchiveTool")]
    [IsDependentOn(typeof(RestoreTask))]
    public sealed class BuildArchiveToolTask : FrostingTask<BuildContext>
    {
        /// <summary>
        /// Runs the ArchiveTool build task.
        /// </summary>
        /// <param name="context">The build context.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="context"/> is null.</exception>
        /// <exception cref="FileNotFoundException">Thrown when ArchiveTool executable is not found after build.</exception>
        public override void Run(BuildContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            context.Log.Information("Building ArchiveTool...");

            // Build only ArchiveTool project
            context.DotNetBuild(
                context.ArchiveToolProjectPath.FullPath,
                new DotNetBuildSettings
                {
                    Configuration = context.Configuration,
                });

            // Verify executable exists after build
            if (!context.FileExists(context.ArchiveToolExecutable))
            {
                throw new FileNotFoundException(
                    $"ArchiveTool executable not found after build: {context.ArchiveToolExecutable}. " +
                    "Ensure the build completed successfully.",
                    context.ArchiveToolExecutable.FullPath);
            }

            context.Log.Information($"ArchiveTool built successfully: {context.ArchiveToolExecutable}");
        }
    }
}
