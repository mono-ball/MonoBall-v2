namespace MonoBall.Core.Diagnostics.Console.Scripting.Services;

using System;
using Console.Services;
using Constants;
using Interfaces;

/// <summary>
/// Routes console input to commands or REPL based on classification.
/// </summary>
public sealed class ConsoleInputRouter : IConsoleInputRouter
{
    private readonly IConsoleCommandRegistry _commandRegistry;

    /// <summary>
    /// Initializes a new instance of the <see cref="ConsoleInputRouter"/> class.
    /// </summary>
    /// <param name="commandRegistry">The command registry to check for commands.</param>
    /// <exception cref="ArgumentNullException">Thrown if commandRegistry is null.</exception>
    public ConsoleInputRouter(IConsoleCommandRegistry commandRegistry)
    {
        _commandRegistry =
            commandRegistry ?? throw new ArgumentNullException(nameof(commandRegistry));
    }

    /// <inheritdoc/>
    public ConsoleInputType ClassifyInput(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return ConsoleInputType.Unknown;

        var trimmed = input.TrimStart();

        // Check for explicit C# prefix
        if (HasCSharpPrefix(trimmed))
        {
            return ConsoleInputType.CSharpCode;
        }

        // Check if it's a registered command
        var firstWord = GetFirstWord(trimmed);
        if (HasCommand(firstWord))
        {
            return ConsoleInputType.Command;
        }

        // Default to C# code
        return ConsoleInputType.CSharpCode;
    }

    /// <inheritdoc/>
    public string StripPrefix(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return input;

        var trimmed = input.TrimStart();
        if (HasCSharpPrefix(trimmed))
        {
            return trimmed.Substring(1).TrimStart();
        }

        return input;
    }

    /// <summary>Checks if input starts with a C# prefix character.</summary>
    /// <param name="trimmedInput">The trimmed input string.</param>
    /// <returns>True if input starts with '>' or '#'.</returns>
    private static bool HasCSharpPrefix(string trimmedInput)
    {
        return trimmedInput.Length > 0
            && (
                trimmedInput[0] == ReplConstants.CSharpPrefix
                || trimmedInput[0] == ReplConstants.AlternativePrefix
            );
    }

    /// <summary>Checks if a command name or alias is registered.</summary>
    private bool HasCommand(string nameOrAlias)
    {
        return _commandRegistry.TryGetCommand(nameOrAlias, out _);
    }

    /// <summary>Gets the first word from input.</summary>
    private static string GetFirstWord(string input)
    {
        var spaceIndex = input.IndexOf(' ');
        return spaceIndex > 0 ? input.Substring(0, spaceIndex) : input;
    }
}
