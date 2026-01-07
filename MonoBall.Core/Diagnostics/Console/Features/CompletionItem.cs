namespace MonoBall.Core.Diagnostics.Console.Features;

/// <summary>
/// Represents a completion suggestion with display text and metadata.
/// </summary>
public readonly struct CompletionItem
{
    /// <summary>
    /// Gets the text to insert when this completion is accepted.
    /// </summary>
    public string Text { get; init; }

    /// <summary>
    /// Gets the description shown alongside the completion.
    /// </summary>
    public string Description { get; init; }

    /// <summary>
    /// Gets the category or type of completion (e.g., "command", "argument").
    /// </summary>
    public string Category { get; init; }

    /// <summary>
    /// Creates a new completion item.
    /// </summary>
    /// <param name="text">The text to insert.</param>
    /// <param name="description">Optional description.</param>
    /// <param name="category">Optional category.</param>
    public CompletionItem(string text, string description = "", string category = "")
    {
        Text = text;
        Description = description;
        Category = category;
    }

    /// <summary>
    /// Creates a simple completion with just text.
    /// </summary>
    public static CompletionItem Simple(string text) => new(text);

    /// <summary>
    /// Creates a command completion with description.
    /// </summary>
    public static CompletionItem Command(string name, string description) =>
        new(name, description, "command");

    /// <summary>
    /// Creates an argument completion.
    /// </summary>
    public static CompletionItem Argument(string text, string description = "") =>
        new(text, description, "arg");

    /// <summary>
    /// Implicit conversion from string for simple completions.
    /// </summary>
    public static implicit operator CompletionItem(string text) => new(text);
}
