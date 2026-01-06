using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Cake.Common.IO;
using Cake.Core;
using Cake.Core.Diagnostics;
using Cake.Core.IO;
using Cake.Frosting;

namespace MonoBall.Build.Tasks
{
    /// <summary>
    /// Task that compiles mod shaders from .fx to .mgfxo files.
    /// </summary>
    [TaskName("CompileShaders")]
    [IsDependentOn(typeof(RestoreTask))]
    public sealed class CompileShadersTask : FrostingTask<BuildContext>
    {
        private const string ShaderExtension = ".fx";
        private const string CompiledShaderExtension = ".mgfxo";
        private const string ShaderProfile = "OpenGL";

        /// <summary>
        /// Runs the shader compilation task.
        /// </summary>
        /// <param name="context">The build context.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="context"/> is null.</exception>
        /// <exception cref="InvalidOperationException">Thrown when shader compilation fails.</exception>
        public override void Run(BuildContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            if (context.SkipShaderCompilation)
            {
                context.Log.Information("Skipping shader compilation (--skip-shader-compilation flag set).");
                return;
            }

            if (!context.DirectoryExists(context.ModsDirectory))
            {
                context.Log.Warning($"Mods directory not found: {context.ModsDirectory}. Skipping shader compilation.");
                return;
            }

            context.Log.Information("Compiling mod shaders...");

            // Discover all .fx files in Mods directory
            var shaderFiles = context.GetFiles($"{context.ModsDirectory}/**/*{ShaderExtension}");
            var failedShaders = new List<string>();
            var compiledCount = 0;

            foreach (var shaderFile in shaderFiles)
            {
                try
                {
                    var outputPath = shaderFile.ChangeExtension(CompiledShaderExtension);
                    CompileShader(context, shaderFile, outputPath);
                    compiledCount++;
                }
                catch (InvalidOperationException ex)
                {
                    // Check if this is a macOS Wine error - allow build to continue with warning
                    var isMacOS = RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
                    var isWineError = ex.Message.Contains("Wine") ||
                                     ex.Message.Contains("MGFXC0001") ||
                                     ex.Message.Contains("WineHelper");

                    if (isMacOS && isWineError)
                    {
                        context.Log.Warning($"Skipped shader {shaderFile}: {ex.Message}");
                        // Don't add to failedShaders - allow build to continue
                    }
                    else
                    {
                        context.Log.Error($"Failed to compile shader {shaderFile}: {ex.Message}");
                        failedShaders.Add(shaderFile.FullPath);
                    }
                }
                catch (Exception ex)
                {
                    context.Log.Error($"Failed to compile shader {shaderFile}: {ex.Message}");
                    failedShaders.Add(shaderFile.FullPath);
                }
            }

            if (failedShaders.Any())
            {
                throw new InvalidOperationException(
                    $"Failed to compile {failedShaders.Count} shader(s). " +
                    $"Failed shaders: {string.Join(", ", failedShaders)}");
            }

            if (compiledCount > 0)
            {
                context.Log.Information($"Shader compilation complete. Compiled {compiledCount} shader(s).");
            }
            else if (shaderFiles.Count > 0)
            {
                context.Log.Warning($"No shaders were compiled. {shaderFiles.Count} shader(s) were skipped.");
            }
        }

        private static void CompileShader(BuildContext context, FilePath inputPath, FilePath outputPath)
        {
            context.Log.Debug($"Compiling {inputPath} -> {outputPath}");

            // Try to find mgfxc tool directly for better error output
            var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var mgfxcPath = System.IO.Path.Combine(homeDir, ".dotnet", "tools", "mgfxc");
            
            ProcessArgumentBuilder args;
            string executable;
            
            if (context.FileExists(mgfxcPath))
            {
                // Use mgfxc directly - this gives us better error messages
                executable = mgfxcPath;
                args = new ProcessArgumentBuilder();
                args.AppendQuoted(inputPath.FullPath);
                args.AppendQuoted(outputPath.FullPath);
                args.Append($"/Profile:{ShaderProfile}");
            }
            else
            {
                // Fallback to dotnet tool run
                executable = "dotnet";
                args = new ProcessArgumentBuilder();
                args.Append("tool");
                args.Append("run");
                args.Append("mgfxc");
                args.Append("--");
                args.AppendQuoted(inputPath.FullPath);
                args.AppendQuoted(outputPath.FullPath);
                args.Append($"/Profile:{ShaderProfile}");
            }

            // Ensure Wine is accessible in PATH for MGFXC
            // MGFXC's WineHelper checks for Wine in PATH, so we need to ensure it's there
            var env = new Dictionary<string, string>();
            var currentPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            
            // Add common Wine locations to PATH (check both Apple Silicon and Intel Macs)
            var winePaths = new[] { "/opt/homebrew/bin", "/usr/local/bin" };
            var updatedPath = currentPath;
            foreach (var winePath in winePaths)
            {
                if (System.IO.Directory.Exists(winePath) && !updatedPath.Contains(winePath))
                {
                    updatedPath = $"{winePath}:{updatedPath}";
                }
            }
            
            if (updatedPath != currentPath)
            {
                env["PATH"] = updatedPath;
            }
            
            // Ensure MGFXC_WINE_PATH is set if Wine prefix exists
            var winePrefix = Environment.GetEnvironmentVariable("MGFXC_WINE_PATH") ?? 
                            System.IO.Path.Combine(homeDir, ".winemonogame");
            if (System.IO.Directory.Exists(winePrefix))
            {
                env["MGFXC_WINE_PATH"] = winePrefix;
            }

            var process = context.ProcessRunner.Start(
                executable,
                new ProcessSettings
                {
                    Arguments = args,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    EnvironmentVariables = env.Count > 0 ? env : null
                });

            process.WaitForExit();
            var exitCode = process.GetExitCode();
            
            // Read both stdout and stderr to check for specific error types
            var errorLines = process.GetStandardError() ?? Enumerable.Empty<string>();
            var outputLines = process.GetStandardOutput() ?? Enumerable.Empty<string>();
            var errorText = string.Join("\n", errorLines);
            var outputText = string.Join("\n", outputLines);
            var allOutput = errorText + "\n" + outputText;
            
            if (exitCode != 0)
            {
                var isMacOS = RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
                
                // Check if this is specifically a Wine detection error (MGFXC0001)
                // Check both stderr and stdout as MGFXC may write errors to either
                var isWineDetectionError = allOutput.Contains("MGFXC0001") || 
                                          allOutput.Contains("WineHelper") ||
                                          allOutput.Contains("requires a valid Wine installation") ||
                                          allOutput.Contains("type initializer") && allOutput.Contains("WineHelper");
                
                if (isMacOS && isWineDetectionError)
                {
                    // This is specifically a Wine detection issue - provide helpful message
                    var winePrefixPath = Environment.GetEnvironmentVariable("MGFXC_WINE_PATH") ?? 
                                    System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".winemonogame");
                    var winePrefixExists = Directory.Exists(winePrefixPath);
                    
                    var errorMsg = $"Shader compilation failed for {inputPath} (exit code {exitCode}). ";
                    
                    if (!winePrefixExists)
                    {
                        errorMsg += "MGFXC requires a Wine prefix to be set up for MonoGame. " +
                                   "Run the MonoGame Wine setup script or set MGFXC_WINE_PATH environment variable. ";
                    }
                    else
                    {
                        errorMsg += "MGFXC cannot detect or use Wine on macOS. " +
                                   "Ensure Wine is properly installed and accessible in PATH. " +
                                   "Check that MGFXC_WINE_PATH points to a valid Wine prefix. ";
                    }
                    
                    errorMsg += "Visit https://docs.monogame.net/errors/mgfx0001?tab=macos for troubleshooting. " +
                               "You can skip shader compilation with --skip-shader-compilation flag.";
                    
                    throw new InvalidOperationException(errorMsg);
                }
                
                // Not a Wine detection error - this is a real shader compilation error
                // Include the actual error message from both stdout and stderr
                var actualError = !string.IsNullOrWhiteSpace(allOutput.Trim()) 
                    ? $"\nError details: {allOutput.Trim()}" 
                    : string.Empty;
                
                throw new InvalidOperationException(
                    $"Shader compilation failed for {inputPath} with exit code {exitCode}.{actualError}");
            }
        }
    }
}
