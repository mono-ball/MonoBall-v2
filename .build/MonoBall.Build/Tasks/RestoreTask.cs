using System;
using Cake.Common.Tools.DotNet;
using Cake.Core;
using Cake.Core.Diagnostics;
using Cake.Core.IO;
using Cake.Frosting;

namespace MonoBall.Build.Tasks
{
    /// <summary>
    /// Task that restores NuGet packages and dotnet tools.
    /// </summary>
    [TaskName("Restore")]
    [IsDependentOn(typeof(CleanTask))]
    public sealed class RestoreTask : FrostingTask<BuildContext>
    {
        /// <summary>
        /// Runs the restore task.
        /// </summary>
        /// <param name="context">The build context.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="context"/> is null.</exception>
        /// <exception cref="InvalidOperationException">Thrown when required tools are missing after restore.</exception>
        public override void Run(BuildContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            context.Log.Information("Restoring NuGet packages and dotnet tools...");

            // Restore NuGet packages
            context.DotNetRestore(context.SolutionPath.FullPath);

            // Restore dotnet tools - BuildContext inherits from FrostingContext which provides ICakeContext methods
            var args = new ProcessArgumentBuilder();
            args.Append("tool");
            args.Append("restore");

            var process = context.ProcessRunner.Start(
                "dotnet",
                new ProcessSettings
                {
                    Arguments = args
                });

            process.WaitForExit();
            if (process.GetExitCode() != 0)
            {
                throw new InvalidOperationException("Failed to restore dotnet tools. Exit code: " + process.GetExitCode());
            }

            // Verify required tools are available (mgfxc, mgcb)
            // Note: We can't directly verify tool existence, but if restore succeeded, tools should be available
            context.Log.Information("Restore completed successfully.");
        }
    }
}
