namespace MonoBall.Core.Diagnostics.Console.Scripting.Models;

/// <summary>
/// A code completion suggestion.
/// </summary>
/// <param name="DisplayText">Text shown in completion list.</param>
/// <param name="InsertText">Text inserted when selected.</param>
/// <param name="Kind">Completion kind (Method, Property, etc.).</param>
/// <param name="Description">Optional description/documentation.</param>
public sealed record ReplCompletionItem(
    string DisplayText,
    string InsertText,
    string Kind,
    string? Description = null
);
