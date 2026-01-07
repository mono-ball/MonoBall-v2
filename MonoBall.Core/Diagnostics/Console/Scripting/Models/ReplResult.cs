namespace MonoBall.Core.Diagnostics.Console.Scripting.Models;

using System;
using System.Collections.Generic;

/// <summary>
/// Result of a REPL evaluation.
/// </summary>
public sealed class ReplResult
{
    /// <summary>Gets whether the evaluation succeeded.</summary>
    public bool Success { get; init; }

    /// <summary>Gets the return value, if any.</summary>
    public object? ReturnValue { get; init; }

    /// <summary>Gets the return type, if any.</summary>
    public Type? ReturnType { get; init; }

    /// <summary>Gets whether this was a statement (no return value).</summary>
    public bool IsStatement { get; init; }

    /// <summary>Gets the error message, if evaluation failed.</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>Gets compilation diagnostics, if any.</summary>
    public IReadOnlyList<string> Diagnostics { get; init; } = Array.Empty<string>();

    /// <summary>Gets the execution time in milliseconds.</summary>
    public double ExecutionTimeMs { get; init; }

    /// <summary>Creates a successful result with a return value.</summary>
    /// <param name="value">The return value.</param>
    /// <param name="type">The return type.</param>
    /// <param name="timeMs">Execution time in milliseconds.</param>
    /// <returns>A successful result.</returns>
    public static ReplResult Ok(object? value, Type? type, double timeMs) =>
        new()
        {
            Success = true,
            ReturnValue = value,
            ReturnType = type,
            ExecutionTimeMs = timeMs,
        };

    /// <summary>Creates a successful result for a statement (no return value).</summary>
    /// <param name="timeMs">Execution time in milliseconds.</param>
    /// <returns>A successful statement result.</returns>
    public static ReplResult Statement(double timeMs) =>
        new()
        {
            Success = true,
            IsStatement = true,
            ExecutionTimeMs = timeMs,
        };

    /// <summary>Creates a failed result with an error message.</summary>
    /// <param name="error">The error message.</param>
    /// <param name="diagnostics">Optional compilation diagnostics.</param>
    /// <returns>A failed result.</returns>
    public static ReplResult Fail(string error, IReadOnlyList<string>? diagnostics = null) =>
        new()
        {
            Success = false,
            ErrorMessage = error,
            Diagnostics = diagnostics ?? Array.Empty<string>(),
        };
}
