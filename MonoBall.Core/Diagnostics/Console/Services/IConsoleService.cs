namespace MonoBall.Core.Diagnostics.Console.Services;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Events;
using Features;

/// <summary>
/// Interface for the console service.
/// Orchestrates console functionality including command execution and output management.
/// </summary>
public interface IConsoleService : IConsoleContext, IDisposable
{
    /// <summary>
    /// Gets whether the console is currently visible.
    /// </summary>
    bool IsVisible { get; }

    /// <summary>
    /// Gets the output buffer.
    /// </summary>
    ConsoleBuffer OutputBuffer { get; }

    /// <summary>
    /// Gets the command history.
    /// </summary>
    ConsoleHistory History { get; }

    /// <summary>
    /// Shows the console.
    /// </summary>
    void Show();

    /// <summary>
    /// Hides the console.
    /// </summary>
    void Hide();

    /// <summary>
    /// Toggles console visibility.
    /// </summary>
    void Toggle();

    /// <summary>
    /// Submits a command for execution.
    /// </summary>
    /// <param name="commandText">The full command text.</param>
    /// <returns>Task that completes when command execution finishes.</returns>
    Task ExecuteCommandAsync(string commandText);

    /// <summary>
    /// Gets completions for partial input.
    /// </summary>
    /// <param name="partial">The partial input text.</param>
    /// <returns>Available completions.</returns>
    IEnumerable<string> GetCompletions(string partial);

    /// <summary>
    /// Gets rich completions with descriptions for partial input.
    /// </summary>
    /// <param name="partial">The partial input text.</param>
    /// <returns>Rich completion items with text and descriptions.</returns>
    IEnumerable<CompletionItem> GetRichCompletions(string partial);

    /// <summary>
    /// Writes a welcome message to the console.
    /// </summary>
    void WriteWelcome();
}
