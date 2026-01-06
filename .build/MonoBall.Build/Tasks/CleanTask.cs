using System;
using Cake.Common.IO;
using Cake.Core.Diagnostics;
using Cake.Frosting;

namespace MonoBall.Build.Tasks
{
    /// <summary>
    /// Task that cleans build artifacts and output directories.
    /// </summary>
    [TaskName("Clean")]
    public sealed class CleanTask : FrostingTask<BuildContext>
    {
        /// <summary>
        /// Runs the clean task.
        /// </summary>
        /// <param name="context">The build context.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="context"/> is null.</exception>
        public override void Run(BuildContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            context.Log.Information("Cleaning build artifacts...");

            // Clean bin/ and obj/ directories in all projects
            var projects = new[] { context.CoreProjectPath, context.DesktopGLProjectPath, context.DesktopVKProjectPath, context.WindowsDX12ProjectPath, context.ArchiveToolProjectPath };
            foreach (var project in projects)
            {
                var projectDir = project.GetDirectory();
                var binDir = projectDir.Combine("bin");
                var objDir = projectDir.Combine("obj");

                if (context.DirectoryExists(binDir))
                {
                    context.CleanDirectory(binDir);
                }

                if (context.DirectoryExists(objDir))
                {
                    context.CleanDirectory(objDir);
                }
            }

            // Clean output Mods directory if it exists
            var modsOutputDir = context.OutputDirectory.Combine("Mods");
            if (context.DirectoryExists(modsOutputDir))
            {
                context.CleanDirectory(modsOutputDir);
            }

            // Clean publish directory if it exists
            if (context.PublishDirectory != null && context.DirectoryExists(context.PublishDirectory))
            {
                context.CleanDirectory(context.PublishDirectory);
            }

            context.Log.Information("Clean completed successfully.");
        }
    }
}
