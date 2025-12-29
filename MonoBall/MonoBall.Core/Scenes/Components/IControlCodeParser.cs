namespace MonoBall.Core.Scenes.Components;

/// <summary>
///     Interface for parsing control codes into text tokens.
///     Enables extensible control code parsing using strategy pattern.
/// </summary>
public interface IControlCodeParser
{
    /// <summary>
    ///     Gets the control code name this parser handles (e.g., "PAUSE", "COLOR").
    ///     For parameterized codes, this is the prefix (e.g., "PAUSE" for "PAUSE:1.5").
    /// </summary>
    string ControlCodeName { get; }

    /// <summary>
    ///     Gets whether this parser handles parameterized control codes (e.g., "PAUSE:1.5").
    ///     If true, the parser will receive the full control code including parameters.
    ///     If false, the parser only handles exact matches.
    /// </summary>
    bool IsParameterized { get; }

    /// <summary>
    ///     Parses a control code into a text token.
    /// </summary>
    /// <param name="controlCode">The control code string (without braces, e.g., "PAUSE:1.5" or "CLEAR").</param>
    /// <param name="originalPosition">The original position in the text string.</param>
    /// <returns>The parsed text token.</returns>
    /// <exception cref="FormatException">Thrown if the control code format is invalid.</exception>
    TextToken Parse(string controlCode, int originalPosition);
}
