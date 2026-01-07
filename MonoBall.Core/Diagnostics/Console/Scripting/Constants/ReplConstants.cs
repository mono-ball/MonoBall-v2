namespace MonoBall.Core.Diagnostics.Console.Scripting.Constants;

/// <summary>
/// Constants for REPL configuration.
/// </summary>
public static class ReplConstants
{
    /// <summary>Prefix character to force C# evaluation.</summary>
    public const char CSharpPrefix = '>';

    /// <summary>Alternative prefix for C# evaluation.</summary>
    public const char AlternativePrefix = '#';

    /// <summary>Default timeout for script evaluation in milliseconds.</summary>
    public const int DefaultEvaluationTimeoutMs = 5000;

    /// <summary>Default maximum completions to return.</summary>
    public const int DefaultCompletionLimit = 50;

    /// <summary>Number of evaluations before warning about memory usage.</summary>
    public const int MaxEvaluationsBeforeResetWarning = 100;

    /// <summary>Threshold in milliseconds above which execution time is displayed.</summary>
    public const double ExecutionTimeDisplayThresholdMs = 100.0;

    /// <summary>Script definition ID for REPL context.</summary>
    public const string ReplScriptId = "__repl__";

    /// <summary>Logger source context name for REPL.</summary>
    public const string LogSourceContext = "REPL";
}
