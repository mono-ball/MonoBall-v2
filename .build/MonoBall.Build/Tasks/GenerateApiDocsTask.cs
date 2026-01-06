using System;
using Cake.Common.IO;
using Cake.Core;
using Cake.Core.Diagnostics;
using Cake.Core.IO;
using Cake.Frosting;

namespace MonoBall.Build.Tasks
{
    /// <summary>
    /// Task that generates .NET API documentation using Roslynator.
    /// Generates documentation files for the MonoBall.Core library API.
    /// </summary>
    [TaskName("GenerateApiDocs")]
    [IsDependentOn(typeof(BuildCoreTask))]
    public sealed class GenerateApiDocsTask : FrostingTask<BuildContext>
    {
        private const string DocsOutputDirectory = "docs/api";
        private const string DocsHost = "github"; // Options: docusaurus, github, sphinx

        /// <summary>
        /// Runs the API documentation generation task.
        /// </summary>
        /// <param name="context">The build context.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="context"/> is null.</exception>
        /// <exception cref="InvalidOperationException">Thrown when documentation generation fails.</exception>
        public override void Run(BuildContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            context.Log.Information("Generating .NET API documentation...");

            // Output directory for generated documentation
            var docsOutputDir = context.RootDirectory.Combine(DocsOutputDirectory);

            // Ensure output directory exists
            context.EnsureDirectoryExists(docsOutputDir);

            // Clean existing documentation if it exists
            if (context.DirectoryExists(docsOutputDir))
            {
                context.Log.Debug($"Cleaning existing documentation directory: {docsOutputDir}");
                context.CleanDirectory(docsOutputDir);
            }

            try
            {
                // Generate documentation files using Roslynator
                // Only generate docs for MonoBall.Core (the public API library)
                context.Log.Information($"Generating API documentation for MonoBall.Core...");
                context.Log.Debug($"Output directory: {docsOutputDir}");
                context.Log.Debug($"Host format: {DocsHost}");

                // Build roslynator command arguments
                // Use 'dotnet tool run roslynator' to invoke the tool
                var args = new ProcessArgumentBuilder();
                args.Append("tool");
                args.Append("run");
                args.Append("roslynator");
                args.Append("generate-doc");
                args.AppendQuoted(context.CoreProjectPath.FullPath);
                args.Append("--properties");
                args.Append($"Configuration={context.Configuration}");
                args.Append("-o");
                args.AppendQuoted(docsOutputDir.FullPath);
                args.Append("--host");
                args.Append(DocsHost);
                args.Append("--heading");
                args.AppendQuoted(".NET API Reference");
                args.Append("--group-by-common-namespace");
                args.Append("--ignored-common-parts");
                args.Append("content");
                args.Append("--ignored-root-parts");
                args.Append("all");
                args.Append("--max-derived-types");
                args.Append("10");
                // Exclude external library namespaces (not part of MonoBall API)
                // Use --ignored-names to exclude by namespace prefix (proper configuration option)
                // Note: --ignored-names only accepts a single namespace prefix, so we exclude Arch here
                // and handle Serilog via post-processing (see RemoveExternalLibraryFolders below)
                args.Append("--ignored-names");
                args.AppendQuoted("Arch");

                // Run roslynator CLI tool via dotnet tool run
                var process = context.ProcessRunner.Start(
                    "dotnet",
                    new ProcessSettings
                    {
                        Arguments = args,
                        WorkingDirectory = context.RootDirectory
                    });

                process.WaitForExit();
                var exitCode = process.GetExitCode();

                if (exitCode != 0)
                {
                    throw new InvalidOperationException(
                        $"Failed to generate API documentation. Roslynator exited with code {exitCode}.");
                }

                // Post-process: Remove external library namespace folders that --ignored-names couldn't exclude
                // Roslynator's --ignored-names only accepts a single namespace prefix, so we handle
                // additional exclusions (like Serilog) via post-processing
                RemoveExternalLibraryFolders(context, docsOutputDir);

                context.Log.Information($"API documentation generated successfully to: {docsOutputDir}");
                context.Log.Information($"Documentation format: {DocsHost}");
            }
            catch (Exception ex)
            {
                var errorMessage = "Failed to generate API documentation. " +
                                 "Ensure Roslynator CLI tool is installed (dotnet tool restore). " +
                                 "See https://josefpihrt.github.io/docs/roslynator/how-to-generate-net-api-docs for details.";

                context.Log.Error(errorMessage);
                throw new InvalidOperationException(errorMessage, ex);
            }
        }

        /// <summary>
        /// Removes external library namespace folders from the generated documentation.
        /// </summary>
        /// <remarks>
        /// This is a workaround for Roslynator's <c>--ignored-names</c> limitation (only accepts a single namespace prefix).
        /// Arch namespaces are excluded via <c>--ignored-names</c> during generation, while other external libraries
        /// (e.g., Serilog) are removed via this post-processing step.
        /// </remarks>
        /// <param name="context">The build context.</param>
        /// <param name="docsOutputDir">The documentation output directory.</param>
        private static void RemoveExternalLibraryFolders(BuildContext context, DirectoryPath docsOutputDir)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));
            if (docsOutputDir == null)
                throw new ArgumentNullException(nameof(docsOutputDir));

            context.Log.Debug("Removing external library namespace folders from documentation...");

            // List of external library namespace prefixes to exclude (in addition to Arch, which is handled by --ignored-names)
            var externalLibraryPrefixes = new[] { "Serilog" };

            var removedCount = 0;
            foreach (var prefix in externalLibraryPrefixes)
            {
                var folders = context.GetDirectories($"{docsOutputDir}/{prefix}*");
                foreach (var folder in folders)
                {
                    if (context.DirectoryExists(folder))
                    {
                        context.Log.Debug($"Removing external library folder: {folder.GetDirectoryName()}");
                        context.DeleteDirectory(folder, new Cake.Common.IO.DeleteDirectorySettings
                        {
                            Recursive = true,
                            Force = true
                        });
                        removedCount++;
                    }
                }
            }

            if (removedCount > 0)
            {
                context.Log.Information($"Removed {removedCount} external library folder(s) from documentation.");
            }
        }
    }
}
