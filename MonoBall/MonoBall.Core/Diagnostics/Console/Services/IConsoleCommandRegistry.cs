namespace MonoBall.Core.Diagnostics.Console.Services;

using System.Collections.Generic;
using Commands;
using Features;

/// <summary>
/// Interface for the console command registry.
/// Manages command discovery, registration, and lookup.
/// </summary>
public interface IConsoleCommandRegistry
{
    /// <summary>
    /// Gets all registered commands.
    /// </summary>
    IReadOnlyDictionary<string, IConsoleCommand> Commands { get; }

    /// <summary>
    /// Registers a command instance.
    /// </summary>
    /// <param name="command">The command to register.</param>
    void RegisterCommand(IConsoleCommand command);

    /// <summary>
    /// Unregisters a command by name.
    /// </summary>
    /// <param name="name">The command name.</param>
    /// <returns>True if the command was found and removed.</returns>
    bool UnregisterCommand(string name);

    /// <summary>
    /// Tries to get a command by name or alias.
    /// </summary>
    /// <param name="nameOrAlias">The command name or alias.</param>
    /// <param name="command">The found command, if any.</param>
    /// <returns>True if the command was found.</returns>
    bool TryGetCommand(string nameOrAlias, out IConsoleCommand? command);

    /// <summary>
    /// Gets commands grouped by category.
    /// </summary>
    /// <returns>Dictionary of category name to commands in that category.</returns>
    IReadOnlyDictionary<string, IReadOnlyList<IConsoleCommand>> GetCommandsByCategory();

    /// <summary>
    /// Gets completion suggestions for partial command input.
    /// </summary>
    /// <param name="partial">The partial command text.</param>
    /// <returns>Matching command names.</returns>
    IEnumerable<string> GetCompletions(string partial);

    /// <summary>
    /// Gets rich completion suggestions with descriptions for partial command input.
    /// </summary>
    /// <param name="partial">The partial command text.</param>
    /// <returns>Rich completion items with text and descriptions.</returns>
    IEnumerable<CompletionItem> GetRichCompletions(string partial);
}
