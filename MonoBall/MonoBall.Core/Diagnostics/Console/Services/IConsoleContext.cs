namespace MonoBall.Core.Diagnostics.Console.Services;

using System.Numerics;
using Events;
using UI;

/// <summary>
/// Context provided to console commands during execution.
/// Provides access to console services and output methods.
/// </summary>
public interface IConsoleContext
{
    /// <summary>
    /// Writes a line to the console output.
    /// </summary>
    /// <param name="text">The text to write.</param>
    /// <param name="level">The output level.</param>
    void WriteLine(string text, ConsoleOutputLevel level = ConsoleOutputLevel.Normal);

    /// <summary>
    /// Writes a success message.
    /// </summary>
    /// <param name="text">The text to write.</param>
    void WriteSuccess(string text);

    /// <summary>
    /// Writes a warning message.
    /// </summary>
    /// <param name="text">The text to write.</param>
    void WriteWarning(string text);

    /// <summary>
    /// Writes an error message.
    /// </summary>
    /// <param name="text">The text to write.</param>
    void WriteError(string text);

    /// <summary>
    /// Writes a system/info message.
    /// </summary>
    /// <param name="text">The text to write.</param>
    void WriteSystem(string text);

    /// <summary>
    /// Clears the console output.
    /// </summary>
    void Clear();

    /// <summary>
    /// Gets the command registry for command lookup.
    /// </summary>
    IConsoleCommandRegistry CommandRegistry { get; }

    /// <summary>
    /// Gets the performance statistics provider (may be null if not available).
    /// </summary>
    IPerformanceStats? PerformanceStats { get; }

    /// <summary>
    /// Gets the time control provider (may be null if not available).
    /// </summary>
    ITimeControl? TimeControl { get; }
}

/// <summary>
/// Color palette for console output.
/// Delegates to DebugColors for consistency with the Pokéball theme.
/// </summary>
public static class ConsoleColors
{
    /// <summary>Normal text color.</summary>
    public static Vector4 Normal => DebugColors.TextPrimary;

    /// <summary>Success/confirmation color (Grass type green).</summary>
    public static Vector4 Success => DebugColors.Success;

    /// <summary>Warning color (Pikachu yellow).</summary>
    public static Vector4 Warning => DebugColors.Warning;

    /// <summary>Error color (Pokéball red).</summary>
    public static Vector4 Error => DebugColors.Error;

    /// <summary>System/info color (Water type blue).</summary>
    public static Vector4 System => DebugColors.Info;

    /// <summary>Command echo color (dim gray).</summary>
    public static Vector4 Command => DebugColors.TextDim;

    /// <summary>Selected item highlight color (Info blue with alpha).</summary>
    public static Vector4 SelectedItem => DebugColors.Info with { W = 0.6f };

    /// <summary>
    /// Gets the color for a given output level.
    /// </summary>
    public static Vector4 GetColor(ConsoleOutputLevel level) =>
        level switch
        {
            ConsoleOutputLevel.Success => Success,
            ConsoleOutputLevel.Warning => Warning,
            ConsoleOutputLevel.Error => Error,
            ConsoleOutputLevel.System => System,
            _ => Normal,
        };
}
