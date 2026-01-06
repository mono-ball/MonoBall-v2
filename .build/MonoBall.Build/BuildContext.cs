using System;
using Cake.Common;
using Cake.Common.IO;
using Cake.Core;
using Cake.Core.Diagnostics;
using Cake.Core.IO;
using Cake.Frosting;

namespace MonoBall.Build
{
    /// <summary>
    /// Build context containing configuration, paths, and build flags for Cake Frosting tasks.
    /// </summary>
    public class BuildContext : FrostingContext
    {
        private const string DefaultConfiguration = "Release";
        private const string DefaultTargetFramework = "net10.0";
        private const string DebugConfiguration = "Debug";
        private const string ReleaseConfiguration = "Release";

        /// <summary>
        /// Gets or sets the build configuration (Debug or Release).
        /// </summary>
        public new string Configuration { get; set; }

        /// <summary>
        /// Gets or sets the target framework (e.g., "net10.0").
        /// </summary>
        public string TargetFramework { get; set; }

        /// <summary>
        /// Gets or sets the root directory of the solution.
        /// </summary>
        public DirectoryPath RootDirectory { get; set; }

        /// <summary>
        /// Gets or sets the path to the solution file.
        /// </summary>
        public FilePath SolutionPath { get; set; }

        /// <summary>
        /// Gets or sets the directory containing mods.
        /// </summary>
        public DirectoryPath ModsDirectory { get; set; }

        /// <summary>
        /// Gets or sets the output directory for build artifacts.
        /// </summary>
        public DirectoryPath OutputDirectory { get; set; }

        /// <summary>
        /// Gets or sets the publish directory for published artifacts, or null if not set.
        /// </summary>
        public DirectoryPath? PublishDirectory { get; set; }

        /// <summary>
        /// Gets or sets the path to the Core project file.
        /// </summary>
        public FilePath CoreProjectPath { get; set; }

        /// <summary>
        /// Gets or sets the path to the DesktopGL project file.
        /// </summary>
        public FilePath DesktopGLProjectPath { get; set; }

        /// <summary>
        /// Gets or sets the path to the ArchiveTool project file.
        /// </summary>
        public FilePath ArchiveToolProjectPath { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether to skip tests.
        /// </summary>
        public bool SkipTests { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether to skip shader compilation.
        /// </summary>
        public bool SkipShaderCompilation { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether to skip mod compression.
        /// </summary>
        public bool SkipModCompression { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether to skip mod copying.
        /// </summary>
        public bool SkipModCopy { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether to treat analyzer warnings as errors.
        /// </summary>
        public bool TreatWarningsAsErrors { get; set; }

        /// <summary>
        /// Gets or sets the path to the MGFXC shader compiler tool, or null if not set.
        /// </summary>
        public FilePath? MgfxcPath { get; set; }

        /// <summary>
        /// Gets or sets the path to the ArchiveTool executable.
        /// </summary>
        public FilePath ArchiveToolExecutable { get; set; }

        /// <summary>
        /// Gets a value indicating whether the build is running on GitHub Actions.
        /// </summary>
        public bool IsRunningOnGitHubActions => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GITHUB_ACTIONS"));

        /// <summary>
        /// Gets the GitHub commit SHA, or null if not running on GitHub Actions.
        /// </summary>
        public string? GitHubSha => Environment.GetEnvironmentVariable("GITHUB_SHA");

        /// <summary>
        /// Gets a value indicating whether this is a pull request build.
        /// </summary>
        public bool IsPullRequest => Environment.GetEnvironmentVariable("GITHUB_REF")?.StartsWith("refs/pull/") ?? false;

        /// <summary>
        /// Initializes a new instance of the <see cref="BuildContext"/> class.
        /// </summary>
        /// <param name="context">The Cake context.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="context"/> is null.</exception>
        /// <exception cref="FileNotFoundException">Thrown when solution file is not found.</exception>
        /// <exception cref="ArgumentException">Thrown when configuration is invalid.</exception>
        public BuildContext(ICakeContext context)
            : base(context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            // Build Configuration
            Configuration = context.Argument("configuration", DefaultConfiguration);
            if (Configuration != DebugConfiguration && Configuration != ReleaseConfiguration)
            {
                throw new ArgumentException(
                    $"Invalid configuration '{Configuration}'. Must be '{DebugConfiguration}' or '{ReleaseConfiguration}'.",
                    nameof(Configuration));
            }

            TargetFramework = context.Argument("target-framework", DefaultTargetFramework);

            // Path Resolution - find solution file relative to current working directory
            // When running via dotnet run --project, working directory may be the project dir or repo root
            var currentDir = context.Environment.WorkingDirectory;

            // Try multiple locations for solution file
            FilePath solutionPath;
            DirectoryPath rootDir;

            // Try current directory first
            var candidate1 = currentDir.CombineWithFilePath("MonoBall.sln");
            if (context.FileExists(candidate1))
            {
                solutionPath = candidate1;
                rootDir = currentDir;
            }
            else
            {
                // Try going up from current directory (in case we're in .build/MonoBall.Build/)
                var candidate2 = currentDir.Combine("../..").Collapse().CombineWithFilePath("MonoBall.sln");
                if (context.FileExists(candidate2))
                {
                    solutionPath = candidate2;
                    rootDir = currentDir.Combine("../..").Collapse();
                }
                else
                {
                    // Last resort: assume repo root is two levels up from build project
                    var buildProjectDir = context.MakeAbsolute(context.Directory(".build/MonoBall.Build"));
                    rootDir = buildProjectDir.Combine("../..").Collapse();
                    solutionPath = rootDir.CombineWithFilePath("MonoBall.sln");
                }
            }

            RootDirectory = rootDir;
            SolutionPath = solutionPath;

            // Mods Directory (with environment variable override)
            var modsDir = context.EnvironmentVariable("MONOBALL_MODS_DIR");
            ModsDirectory = modsDir != null
                ? context.MakeAbsolute(context.Directory(modsDir))
                : RootDirectory.Combine("Mods");

            // Output Directory
            var outputDir = context.EnvironmentVariable("MONOBALL_OUTPUT_DIR");
            OutputDirectory = outputDir != null
                ? context.MakeAbsolute(context.Directory(outputDir))
                : RootDirectory.Combine("MonoBall/MonoBall.DesktopGL/bin")
                              .Combine(Configuration)
                              .Combine(TargetFramework);

            // Publish Directory (initialized but can be null until publish is run)
            PublishDirectory = RootDirectory.Combine("MonoBall/MonoBall.DesktopGL/bin")
                                           .Combine(Configuration)
                                           .Combine(TargetFramework)
                                           .Combine("publish");

            // Project Paths
            CoreProjectPath = RootDirectory.CombineWithFilePath("MonoBall/MonoBall.Core/MonoBall.Core.csproj");
            DesktopGLProjectPath = RootDirectory.CombineWithFilePath("MonoBall/MonoBall.DesktopGL/MonoBall.DesktopGL.csproj");
            ArchiveToolProjectPath = RootDirectory.CombineWithFilePath("MonoBall/MonoBall.ArchiveTool/MonoBall.ArchiveTool.csproj");

            // ArchiveTool Executable (platform-specific)
            var archiveToolBinDir = RootDirectory.Combine("MonoBall/MonoBall.ArchiveTool/bin")
                                                   .Combine(Configuration)
                                                   .Combine(TargetFramework);
            ArchiveToolExecutable = context.IsRunningOnWindows()
                ? archiveToolBinDir.CombineWithFilePath("MonoBall.ArchiveTool.exe")
                : archiveToolBinDir.CombineWithFilePath("MonoBall.ArchiveTool");

            // Build Flags
            SkipTests = context.HasArgument("skip-tests");
            SkipShaderCompilation = context.HasArgument("skip-shader-compilation");
            SkipModCompression = context.HasArgument("skip-mod-compression");
            SkipModCopy = context.HasArgument("skip-mod-copy");
            TreatWarningsAsErrors = context.HasArgument("treat-warnings-as-errors");

            // Validate critical paths (fail fast)
            if (!context.FileExists(SolutionPath))
                throw new System.IO.FileNotFoundException($"Solution not found: {SolutionPath}", SolutionPath.FullPath);

            if (!context.DirectoryExists(ModsDirectory))
                context.Log.Warning($"Mods directory not found: {ModsDirectory}");
        }
    }
}
