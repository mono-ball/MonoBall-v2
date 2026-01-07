namespace MonoBall.Core.Diagnostics.Console.Scripting.Interfaces;

/// <summary>
/// Classifies console input as commands or C# code.
/// </summary>
public interface IConsoleInputRouter
{
    /// <summary>Classifies the input type.</summary>
    /// <param name="input">The raw input string.</param>
    /// <returns>The classification result.</returns>
    ConsoleInputType ClassifyInput(string input);

    /// <summary>Strips the C# prefix from input if present.</summary>
    /// <param name="input">The input string.</param>
    /// <returns>The input without prefix.</returns>
    string StripPrefix(string input);
}

/// <summary>
/// Type of console input.
/// </summary>
public enum ConsoleInputType
{
    /// <summary>Input is a registered console command.</summary>
    Command,

    /// <summary>Input is C# code for REPL evaluation.</summary>
    CSharpCode,

    /// <summary>Input type could not be determined.</summary>
    Unknown,
}
