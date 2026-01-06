using System;
using Cake.Core.Diagnostics;
using Cake.Frosting;

namespace MonoBall.Build.Tasks
{
    /// <summary>
    /// Task that runs unit tests. Currently a placeholder for future implementation.
    /// </summary>
    [TaskName("Test")]
    [IsDependentOn(typeof(BuildTask))]
    public sealed class TestTask : FrostingTask<BuildContext>
    {
        /// <summary>
        /// Runs the test task.
        /// </summary>
        /// <param name="context">The build context.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="context"/> is null.</exception>
        public override void Run(BuildContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            if (context.SkipTests)
            {
                context.Log.Information("Skipping tests (--skip-tests flag set).");
                return;
            }

            // TODO: Implement test discovery and execution
            context.Log.Warning("TestTask is not yet implemented.");
        }
    }
}
