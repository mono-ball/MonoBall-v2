using System;
using Cake.Common.Tools.DotNet;
using Cake.Common.Tools.DotNet.Build;
using Cake.Core.Diagnostics;
using Cake.Frosting;

namespace MonoBall.Build.Tasks
{
    /// <summary>
    /// Task that builds the solution (Core + DesktopGL + DesktopVK + WindowsDX12, ArchiveTool already built).
    /// </summary>
    [TaskName("Build")]
    [IsDependentOn(typeof(CompressModsTask))]
    public sealed class BuildTask : FrostingTask<BuildContext>
    {
        /// <summary>
        /// Runs the build task.
        /// </summary>
        /// <param name="context">The build context.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="context"/> is null.</exception>
        /// <exception cref="InvalidOperationException">Thrown when build fails.</exception>
        public override void Run(BuildContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            context.Log.Information("Building solution...");

            // Build Core project
            context.DotNetBuild(
                context.CoreProjectPath.FullPath,
                new DotNetBuildSettings
                {
                    Configuration = context.Configuration,
                    NoRestore = true, // RestoreTask already ran
                });

            // Build DesktopGL project
            context.DotNetBuild(
                context.DesktopGLProjectPath.FullPath,
                new DotNetBuildSettings
                {
                    Configuration = context.Configuration,
                    NoRestore = true, // RestoreTask already ran
                });

            // Build DesktopVK project
            context.DotNetBuild(
                context.DesktopVKProjectPath.FullPath,
                new DotNetBuildSettings
                {
                    Configuration = context.Configuration,
                    NoRestore = true, // RestoreTask already ran
                });

            // Build WindowsDX12 project
            context.DotNetBuild(
                context.WindowsDX12ProjectPath.FullPath,
                new DotNetBuildSettings
                {
                    Configuration = context.Configuration,
                    NoRestore = true, // RestoreTask already ran
                });

            context.Log.Information("Build completed successfully.");
        }
    }
}
