using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.Xna.Framework;
using MonoBall.Core.Scripting.Runtime;
using Serilog;

namespace MonoBall.Core.Scripting.Services
{
    /// <summary>
    /// Service for compiling C# scripts (.csx files) using Roslyn.
    /// Compiles scripts to types that can be instantiated.
    /// </summary>
    public class ScriptCompilerService
    {
        private readonly ILogger _logger;
        private readonly List<MetadataReference> _metadataReferences;
        private readonly List<string> _globalUsings;

        /// <summary>
        /// Initializes a new instance of the ScriptCompilerService class.
        /// </summary>
        /// <param name="logger">The logger instance.</param>
        public ScriptCompilerService(ILogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _metadataReferences = GetDefaultMetadataReferences();
            _globalUsings = GetDefaultGlobalUsings();

            _logger.Debug(
                "ScriptCompilerService initialized with {RefCount} references and {UsingCount} global usings",
                _metadataReferences.Count,
                _globalUsings.Count
            );
        }

        /// <summary>
        /// Compiles a script file and returns the compiled type.
        /// </summary>
        /// <param name="scriptPath">Path to the .csx script file.</param>
        /// <param name="additionalReferences">Optional additional metadata references (e.g., from mod dependencies).</param>
        /// <returns>The compiled script type, or null if compilation failed.</returns>
        public Type? CompileScript(
            string scriptPath,
            IEnumerable<MetadataReference>? additionalReferences = null
        )
        {
            if (string.IsNullOrWhiteSpace(scriptPath))
            {
                _logger.Error("Script path cannot be null or empty");
                return null;
            }

            if (!File.Exists(scriptPath))
            {
                _logger.Error("Script file not found: {ScriptPath}", scriptPath);
                return null;
            }

            try
            {
                // Read script content
                string scriptContent = File.ReadAllText(scriptPath);

                // Prepare script with global usings and class wrapper
                string fullScript = PrepareScriptWithUsings(scriptContent);

                // Parse the script
                SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(
                    fullScript,
                    CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest),
                    scriptPath
                );

                // Combine default references with additional references
                var allReferences = new List<MetadataReference>(_metadataReferences);
                if (additionalReferences != null)
                {
                    allReferences.AddRange(additionalReferences);
                }

                // Create compilation
                string assemblyName =
                    $"Script_{Path.GetFileNameWithoutExtension(scriptPath)}_{Guid.NewGuid():N}";
                var compilation = CSharpCompilation.Create(
                    assemblyName,
                    new[] { syntaxTree },
                    allReferences,
                    new CSharpCompilationOptions(
                        OutputKind.DynamicallyLinkedLibrary,
                        optimizationLevel: OptimizationLevel.Release,
                        allowUnsafe: false,
                        checkOverflow: false,
                        platform: Platform.AnyCpu
                    ).WithMetadataImportOptions(MetadataImportOptions.Public)
                );

                // Emit to memory stream
                using var ms = new MemoryStream();
                EmitResult emitResult = compilation.Emit(ms);

                // Check for errors
                if (!emitResult.Success)
                {
                    var errors = emitResult
                        .Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)
                        .Select(d => d.GetMessage())
                        .ToList();

                    _logger.Error(
                        "Script compilation failed for {ScriptPath} with {ErrorCount} errors",
                        scriptPath,
                        errors.Count
                    );

                    foreach (var error in errors)
                    {
                        _logger.Error("  {Error}", error);
                    }

                    return null;
                }

                // Load assembly and extract type
                ms.Seek(0, SeekOrigin.Begin);
                var assembly = Assembly.Load(ms.ToArray());

                // Find the first public class that inherits from ScriptBase
                Type? compiledType = FindScriptType(assembly);

                if (compiledType == null)
                {
                    _logger.Error(
                        "No valid ScriptBase-derived class found in {ScriptPath}",
                        scriptPath
                    );
                    return null;
                }

                _logger.Debug(
                    "Successfully compiled {ScriptPath} -> {TypeName}",
                    scriptPath,
                    compiledType.Name
                );

                return compiledType;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Unexpected error compiling script: {ScriptPath}", scriptPath);
                return null;
            }
        }

        /// <summary>
        /// Prepares script content with global usings and wraps in a namespace if needed.
        /// </summary>
        private string PrepareScriptWithUsings(string scriptContent)
        {
            var sb = new StringBuilder();

            // Add global usings
            foreach (string globalUsing in _globalUsings)
            {
                sb.AppendLine($"using {globalUsing};");
            }

            sb.AppendLine();

            // If script doesn't already have a class definition, we need to wrap it
            // For now, assume scripts define their own classes
            sb.Append(scriptContent);

            return sb.ToString();
        }

        /// <summary>
        /// Finds the first public class in the assembly that inherits from ScriptBase.
        /// </summary>
        private Type? FindScriptType(Assembly assembly)
        {
            try
            {
                return assembly
                    .GetTypes()
                    .FirstOrDefault(t =>
                        t.IsClass
                        && t.IsPublic
                        && !t.IsAbstract
                        && typeof(ScriptBase).IsAssignableFrom(t)
                    );
            }
            catch (ReflectionTypeLoadException ex)
            {
                _logger.Error(ex, "Error loading types from compiled assembly");
                if (ex.LoaderExceptions != null)
                {
                    foreach (var loaderEx in ex.LoaderExceptions)
                    {
                        _logger.Error(loaderEx, "Loader exception");
                    }
                }
                return null;
            }
        }

        /// <summary>
        /// Gets default metadata references for script compilation.
        /// Matches the approach used in oldmonoball's RoslynScriptCompiler.
        /// </summary>
        private List<MetadataReference> GetDefaultMetadataReferences()
        {
            var references = new List<MetadataReference>
            {
                // Core .NET runtime assemblies
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location), // System.Private.CoreLib
                MetadataReference.CreateFromFile(typeof(Console).Assembly.Location), // System.Console
                MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location), // System.Linq
                MetadataReference.CreateFromFile(typeof(List<>).Assembly.Location), // System.Collections
            };

            // Add runtime references from the same directory as System.Private.CoreLib
            // This matches oldmonoball's approach - using runtime assemblies from the runtime directory
            string? runtimePath = Path.GetDirectoryName(typeof(object).Assembly.Location);
            if (!string.IsNullOrEmpty(runtimePath))
            {
                string[] runtimeRefs = new[]
                {
                    "System.Runtime.dll",
                    "System.Collections.dll",
                    "System.Linq.dll",
                    "netstandard.dll",
                };

                foreach (string runtimeRef in runtimeRefs)
                {
                    string refPath = Path.Combine(runtimePath, runtimeRef);
                    if (File.Exists(refPath))
                    {
                        references.Add(MetadataReference.CreateFromFile(refPath));
                        _logger.Debug("Added runtime reference: {Path}", refPath);
                    }
                }
            }

            // MonoGame
            references.Add(MetadataReference.CreateFromFile(typeof(Vector2).Assembly.Location));

            // Arch ECS
            references.Add(
                MetadataReference.CreateFromFile(typeof(Arch.Core.World).Assembly.Location)
            );

            // MonoBall.Core (current assembly)
            references.Add(MetadataReference.CreateFromFile(typeof(ScriptBase).Assembly.Location));

            // Serilog
            references.Add(MetadataReference.CreateFromFile(typeof(ILogger).Assembly.Location));

            return references;
        }

        /// <summary>
        /// Gets default global using directives for scripts.
        /// </summary>
        private List<string> GetDefaultGlobalUsings()
        {
            return new List<string>
            {
                "System",
                "System.Linq",
                "System.Collections.Generic",
                "Arch.Core",
                "Microsoft.Xna.Framework",
                "MonoBall.Core.Scripting.Runtime",
                "MonoBall.Core.ECS",
                "MonoBall.Core.ECS.Components",
                "MonoBall.Core.ECS.Events",
                "Serilog",
            };
        }
    }
}
