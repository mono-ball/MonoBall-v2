using System;
using Cake.Common.Tools.DotNet;
using Cake.Common.Tools.DotNet.Build;
using Cake.Core.Diagnostics;
using Cake.Frosting;

namespace MonoBall.Build.Tasks
{
    /// <summary>
    /// Task that builds only the MonoBall.Core project.
    /// Used for tasks that only need the Core library (e.g., API documentation generation).
    /// </summary>
    [TaskName("BuildCore")]
    [IsDependentOn(typeof(RestoreTask))]
    public sealed class BuildCoreTask : FrostingTask<BuildContext>
    {
        /// <summary>
        /// Runs the Core build task.
        /// </summary>
        /// <param name="context">The build context.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="context"/> is null.</exception>
        /// <exception cref="InvalidOperationException">Thrown when build fails.</exception>
        public override void Run(BuildContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            context.Log.Information("Building MonoBall.Core...");

            // Build only Core project
            context.DotNetBuild(
                context.CoreProjectPath.FullPath,
                new DotNetBuildSettings
                {
                    Configuration = context.Configuration,
                    NoRestore = true, // RestoreTask already ran
                });

            context.Log.Information("MonoBall.Core build completed successfully.");
        }
    }
}
