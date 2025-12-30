namespace MonoBall.Core.Diagnostics.Console.Events;

/// <summary>
/// Event fired when the console visibility is toggled.
/// </summary>
public struct ConsoleToggledEvent
{
    /// <summary>
    /// Gets or sets whether the console is now visible.
    /// </summary>
    public bool IsVisible { get; set; }
}

/// <summary>
/// Event fired when a command is submitted for execution.
/// </summary>
public struct CommandSubmittedEvent
{
    /// <summary>
    /// Gets or sets the full command text.
    /// </summary>
    public string CommandText { get; set; }
}

/// <summary>
/// Event fired when a command execution completes.
/// </summary>
public struct CommandExecutedEvent
{
    /// <summary>
    /// Gets or sets the command that was executed.
    /// </summary>
    public string CommandText { get; set; }

    /// <summary>
    /// Gets or sets whether execution was successful.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Gets or sets the error message if execution failed.
    /// </summary>
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Event fired when completions are requested for partial input.
/// </summary>
public struct CompletionRequestedEvent
{
    /// <summary>
    /// Gets or sets the partial input text.
    /// </summary>
    public string PartialText { get; set; }

    /// <summary>
    /// Gets or sets the cursor position in the text.
    /// </summary>
    public int CursorPosition { get; set; }
}

/// <summary>
/// Event fired when completions are available.
/// </summary>
public struct CompletionsAvailableEvent
{
    /// <summary>
    /// Gets or sets the available completions.
    /// </summary>
    public string[] Completions { get; set; }

    /// <summary>
    /// Gets or sets the descriptions for each completion.
    /// </summary>
    public string[] Descriptions { get; set; }
}

/// <summary>
/// Event fired when console output is written.
/// </summary>
public struct ConsoleOutputEvent
{
    /// <summary>
    /// Gets or sets the output text.
    /// </summary>
    public string Text { get; set; }

    /// <summary>
    /// Gets or sets the output level.
    /// </summary>
    public ConsoleOutputLevel Level { get; set; }
}

/// <summary>
/// Output level for console messages.
/// </summary>
public enum ConsoleOutputLevel
{
    /// <summary>Normal output.</summary>
    Normal,

    /// <summary>Success/confirmation message.</summary>
    Success,

    /// <summary>Warning message.</summary>
    Warning,

    /// <summary>Error message.</summary>
    Error,

    /// <summary>System/info message.</summary>
    System,
}
