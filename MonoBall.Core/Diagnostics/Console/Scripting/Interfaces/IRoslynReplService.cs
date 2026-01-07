namespace MonoBall.Core.Diagnostics.Console.Scripting.Interfaces;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Models;

/// <summary>
/// Service contract for Roslyn-based REPL execution.
/// </summary>
/// <remarks>
/// <para>This service is NOT thread-safe. It is designed for single-threaded
/// UI usage (console input). Do not call EvaluateAsync from multiple threads.</para>
/// </remarks>
public interface IRoslynReplService : IDisposable
{
    /// <summary>Gets whether the service has been initialized.</summary>
    bool IsInitialized { get; }

    /// <summary>Gets the number of evaluations since last reset.</summary>
    int EvaluationCount { get; }

    /// <summary>Initializes the REPL service. Must be called before evaluation.</summary>
    /// <exception cref="InvalidOperationException">Thrown if already initialized.</exception>
    void Initialize();

    /// <summary>Resets the REPL state, clearing variables and subscriptions.</summary>
    void Reset();

    /// <summary>Evaluates C# code and returns the result.</summary>
    /// <param name="code">The C# code to evaluate.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The evaluation result.</returns>
    /// <exception cref="InvalidOperationException">Thrown if not initialized or re-entrant call.</exception>
    Task<ReplResult> EvaluateAsync(string code, CancellationToken ct = default);

    /// <summary>
    /// Gets code completions at the specified position.
    /// NOTE: Not implemented in v1.0. Returns empty list.
    /// </summary>
    /// <param name="code">The code being edited.</param>
    /// <param name="position">The cursor position.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of completion items (empty in v1.0).</returns>
    Task<IReadOnlyList<ReplCompletionItem>> GetCompletionsAsync(
        string code,
        int position,
        CancellationToken ct = default
    );

    /// <summary>Gets all variables defined in the current session.</summary>
    /// <returns>List of variable information.</returns>
    IReadOnlyList<ReplVariable> GetVariables();
}
