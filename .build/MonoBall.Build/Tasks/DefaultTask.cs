using System;
using Cake.Core.Diagnostics;
using Cake.Frosting;

namespace MonoBall.Build.Tasks
{
    /// <summary>
    /// Default task that runs the full build and publish pipeline.
    /// </summary>
    [TaskName("Default")]
    [IsDependentOn(typeof(AnalyzeTask))]
    [IsDependentOn(typeof(PublishTask))]
    public sealed class DefaultTask : FrostingTask<BuildContext>
    {
        /// <summary>
        /// Runs the default task.
        /// </summary>
        /// <param name="context">The build context.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="context"/> is null.</exception>
        public override void Run(BuildContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            context.Log.Information("Default task completed successfully.");
        }
    }
}
