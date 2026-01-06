namespace MonoBall.Core.Diagnostics.Console.Scripting.Models;

using System;

/// <summary>
/// Information about a variable defined in a REPL session.
/// </summary>
/// <param name="Name">The variable name.</param>
/// <param name="Type">The variable type.</param>
/// <param name="Value">The current value.</param>
public sealed record ReplVariable(string Name, Type Type, object? Value);
