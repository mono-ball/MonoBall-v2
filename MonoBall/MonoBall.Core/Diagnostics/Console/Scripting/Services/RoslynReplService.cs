namespace MonoBall.Core.Diagnostics.Console.Scripting.Services;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Arch.Core;
using Console.Services;
using Constants;
using Context;
using Interfaces;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using Models;
using MonoBall.Core.Scripting;
using MonoBall.Core.Scripting.Runtime;
using Serilog;

/// <summary>
/// Roslyn-based REPL service implementation.
/// Creates ScriptContext for REPL and manages CSharpScript state.
/// </summary>
/// <remarks>
/// <para>This service is NOT thread-safe. It is designed for single-threaded
/// UI usage (console input). Do not call EvaluateAsync from multiple threads.</para>
/// </remarks>
public sealed class RoslynReplService : IRoslynReplService
{
    private readonly World _world;
    private readonly IScriptApiProvider _apis;
    private readonly IConsoleContext _console;
    private readonly ILogger _logger;

    private ScriptState<object>? _scriptState;
    private ReplGlobals? _globals;
    private ScriptContext? _replContext;
    private bool _disposed;
    private bool _isEvaluating;

    /// <inheritdoc/>
    public bool IsInitialized => _globals != null;

    /// <inheritdoc/>
    public int EvaluationCount { get; private set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="RoslynReplService"/> class.
    /// </summary>
    /// <param name="world">The ECS world.</param>
    /// <param name="apis">The script API provider.</param>
    /// <param name="console">The console context for output.</param>
    /// <param name="logger">The logger.</param>
    /// <exception cref="ArgumentNullException">Thrown if any parameter is null.</exception>
    public RoslynReplService(
        World world,
        IScriptApiProvider apis,
        IConsoleContext console,
        ILogger logger
    )
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _apis = apis ?? throw new ArgumentNullException(nameof(apis));
        _console = console ?? throw new ArgumentNullException(nameof(console));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public void Initialize()
    {
        ThrowIfDisposed();

        if (IsInitialized)
        {
            throw new InvalidOperationException(
                "RoslynReplService is already initialized. Call Reset() to reinitialize."
            );
        }

        CreateReplContext();
        _logger.Information("RoslynReplService initialized");
    }

    /// <inheritdoc/>
    public void Reset()
    {
        ThrowIfDisposed();

        _globals?.ClearSubscriptions();
        _globals?.Dispose();

        CreateReplContext();
        _scriptState = null;

        _logger.Information("RoslynReplService reset");
    }

    /// <summary>
    /// Creates or recreates the REPL context and globals.
    /// </summary>
    private void CreateReplContext()
    {
        _replContext = new ScriptContext(
            world: _world,
            entity: null, // No attached entity - REPL is global
            logger: _logger.ForContext("SourceContext", ReplConstants.LogSourceContext),
            apis: _apis,
            scriptDefinitionId: ReplConstants.ReplScriptId,
            parameters: new Dictionary<string, object>()
        );

        _globals = new ReplGlobals(_replContext, _console);
        EvaluationCount = 0;
    }

    /// <inheritdoc/>
    public async Task<ReplResult> EvaluateAsync(string code, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ThrowIfNotInitialized();

        if (_isEvaluating)
        {
            throw new InvalidOperationException(
                "Re-entrancy detected. Cannot evaluate while another evaluation is in progress."
            );
        }

        if (string.IsNullOrWhiteSpace(code))
        {
            return ReplResult.Statement(0);
        }

        _isEvaluating = true;
        var stopwatch = Stopwatch.StartNew();

        // Create combined cancellation token with timeout
        using var timeoutCts = new CancellationTokenSource(
            ReplConstants.DefaultEvaluationTimeoutMs
        );
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
        var combinedToken = linkedCts.Token;

        try
        {
            var options = ScriptOptions
                .Default.AddReferences(GetAssemblyReferences())
                .AddImports(GetDefaultImports());

            if (_scriptState == null)
            {
                _scriptState = await CSharpScript.RunAsync<object>(
                    code,
                    options,
                    _globals,
                    typeof(ReplGlobals),
                    combinedToken
                );
            }
            else
            {
                _scriptState = await _scriptState.ContinueWithAsync<object>(
                    code,
                    options,
                    combinedToken
                );
            }

            stopwatch.Stop();
            EvaluationCount++;

            if (EvaluationCount == ReplConstants.MaxEvaluationsBeforeResetWarning)
            {
                _console.WriteWarning(
                    $"Reached {EvaluationCount} evaluations. Consider Reset() to free memory."
                );
            }

            var returnValue = _scriptState.ReturnValue;
            var returnType = returnValue?.GetType();

            return returnValue == null
                ? ReplResult.Statement(stopwatch.Elapsed.TotalMilliseconds)
                : ReplResult.Ok(returnValue, returnType, stopwatch.Elapsed.TotalMilliseconds);
        }
        catch (OperationCanceledException)
            when (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            stopwatch.Stop();
            return ReplResult.Fail(
                $"Evaluation timed out after {ReplConstants.DefaultEvaluationTimeoutMs}ms"
            );
        }
        catch (CompilationErrorException ex)
        {
            stopwatch.Stop();
            return ReplResult.Fail(ex.Message, ex.Diagnostics.Select(d => d.ToString()).ToList());
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            stopwatch.Stop();
            _logger.Error(ex, "REPL evaluation error");
            return ReplResult.Fail($"Runtime error: {ex.Message}");
        }
        finally
        {
            _isEvaluating = false;
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// NOTE: Code completion is not implemented in v1.0.
    /// This is planned for a future release.
    /// </remarks>
    public async Task<IReadOnlyList<ReplCompletionItem>> GetCompletionsAsync(
        string code,
        int position,
        CancellationToken ct = default
    )
    {
        // Phase 2 feature - not implemented in v1.0
        await Task.CompletedTask;
        return Array.Empty<ReplCompletionItem>();
    }

    /// <inheritdoc/>
    public IReadOnlyList<ReplVariable> GetVariables()
    {
        if (_scriptState == null)
            return Array.Empty<ReplVariable>();
        return _scriptState
            .Variables.Select(v => new ReplVariable(v.Name, v.Type, v.Value))
            .ToList();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>Disposes managed resources.</summary>
    private void Dispose(bool disposing)
    {
        if (!_disposed && disposing)
        {
            _globals?.Dispose();
            _globals = null;
            _replContext = null;
            _scriptState = null;
        }
        _disposed = true;
    }

    /// <summary>Throws ObjectDisposedException if disposed.</summary>
    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(RoslynReplService));
    }

    /// <summary>Throws InvalidOperationException if not initialized.</summary>
    private void ThrowIfNotInitialized()
    {
        if (!IsInitialized)
            throw new InvalidOperationException("Call Initialize() first.");
    }

    /// <summary>Gets assembly references for Roslyn script compilation.</summary>
    private static IEnumerable<Assembly> GetAssemblyReferences()
    {
        return new[]
        {
            typeof(object).Assembly, // mscorlib/System.Runtime
            typeof(Enumerable).Assembly, // System.Linq
            typeof(Entity).Assembly, // Arch.Core
            typeof(ReplGlobals).Assembly, // MonoBall.Core
            typeof(Microsoft.Xna.Framework.Vector2).Assembly, // MonoGame
        };
    }

    /// <summary>Gets default namespace imports for Roslyn scripts.</summary>
    private static IEnumerable<string> GetDefaultImports()
    {
        return new[]
        {
            "System",
            "System.Collections.Generic",
            "System.Linq",
            "Arch.Core",
            "MonoBall.Core.ECS.Components",
            "MonoBall.Core.ECS",
        };
    }
}
