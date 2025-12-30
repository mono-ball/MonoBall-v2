namespace MonoBall.Core.Diagnostics.Console.Commands;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Services;

/// <summary>
/// Interface for console commands that can be executed by the debug console.
/// </summary>
public interface IConsoleCommand
{
    /// <summary>
    /// Gets the name of the command (e.g., "help", "clear").
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets a brief description of what this command does.
    /// </summary>
    string Description { get; }

    /// <summary>
    /// Gets the usage/syntax information for this command.
    /// </summary>
    string Usage { get; }

    /// <summary>
    /// Gets the category for grouping commands in help.
    /// </summary>
    string Category { get; }

    /// <summary>
    /// Gets command aliases (alternative names).
    /// </summary>
    string[] Aliases => [];

    /// <summary>
    /// Executes the command with the given arguments.
    /// </summary>
    /// <param name="context">The console context providing access to console services.</param>
    /// <param name="args">Command arguments (excluding the command name itself).</param>
    /// <returns>True if execution was successful, false otherwise.</returns>
    Task<bool> ExecuteAsync(IConsoleContext context, string[] args);

    /// <summary>
    /// Gets auto-completion suggestions for the given partial argument.
    /// </summary>
    /// <param name="args">Current arguments.</param>
    /// <param name="argIndex">Index of the argument being completed.</param>
    /// <returns>Completion suggestions.</returns>
    IEnumerable<string> GetCompletions(string[] args, int argIndex) => [];
}

/// <summary>
/// Attribute to mark a class as a console command for auto-discovery.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ConsoleCommandAttribute : Attribute
{
    /// <summary>
    /// Gets or sets whether this command is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;
}
