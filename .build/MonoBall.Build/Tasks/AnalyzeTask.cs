using System;
using System.Collections.Generic;
using Cake.Common;
using Cake.Common.Tools.DotNet;
using Cake.Common.Tools.DotNet.Build;
using Cake.Core.Diagnostics;
using Cake.Core.IO;
using Cake.Frosting;

namespace MonoBall.Build.Tasks
{
    /// <summary>
    /// Task that runs Roslynator analyzers to find code quality issues.
    /// Fails the build if analyzers report errors or warnings (depending on configuration).
    /// This task only builds the projects for analysis - it does not run the full build pipeline.
    /// </summary>
    [TaskName("Analyze")]
    [IsDependentOn(typeof(RestoreTask))]
    public sealed class AnalyzeTask : FrostingTask<BuildContext>
    {
        /// <summary>
        /// Runs the code analysis task.
        /// </summary>
        /// <param name="context">The build context.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="context"/> is null.</exception>
        /// <exception cref="InvalidOperationException">Thrown when analysis finds issues that should fail the build.</exception>
        public override void Run(BuildContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            context.Log.Information("Running Roslynator analyzers...");

            if (context.TreatWarningsAsErrors)
            {
                context.Log.Information("Treating analyzer warnings as errors (fail-fast mode).");
            }

            // Build with analyzers enabled - analyzers run automatically during build
            // We use NoRestore since RestoreTask already ran
            // Pass TreatWarningsAsErrors as MSBuild property if flag is set
            var buildSettings = new DotNetBuildSettings
            {
                Configuration = context.Configuration,
                NoRestore = true
            };

            // Add MSBuild property to treat warnings as errors if flag is set
            if (context.TreatWarningsAsErrors)
            {
                buildSettings.MSBuildSettings = new Cake.Common.Tools.DotNet.MSBuild.DotNetMSBuildSettings
                {
                    Properties = { ["TreatWarningsAsErrors"] = new[] { "true" } }
                };
            }

            try
            {
                // Build Core project - analyzers will run and report issues
                context.Log.Debug("Analyzing MonoBall.Core...");
                context.DotNetBuild(
                    context.CoreProjectPath.FullPath,
                    buildSettings);

                // Build DesktopGL project - analyzers will run and report issues
                context.Log.Debug("Analyzing MonoBall.DesktopGL...");
                context.DotNetBuild(
                    context.DesktopGLProjectPath.FullPath,
                    buildSettings);

                // Build ArchiveTool project - analyzers will run and report issues
                context.Log.Debug("Analyzing MonoBall.ArchiveTool...");
                context.DotNetBuild(
                    context.ArchiveToolProjectPath.FullPath,
                    buildSettings);

                context.Log.Information("Code analysis completed successfully. No issues found.");
            }
            catch (Exception ex)
            {
                var errorMessage = "Code analysis found issues. " +
                                 "Review the build output above for Roslynator analyzer warnings/errors. " +
                                 "Fix the issues or adjust analyzer severity in Directory.Build.props if needed.";

                context.Log.Error(errorMessage);
                throw new InvalidOperationException(errorMessage, ex);
            }
        }
    }
}
