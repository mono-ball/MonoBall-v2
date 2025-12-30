namespace MonoBall.Core.Diagnostics.Console.Services;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Commands;
using Events;
using Features;
using MonoBall.Core.ECS;
using Serilog;

/// <summary>
/// Main console service that orchestrates command execution and output.
/// </summary>
public sealed class ConsoleService : IConsoleService
{
    private static readonly ILogger Logger = Log.ForContext<ConsoleService>();

    private readonly ConsoleBuffer _outputBuffer;
    private readonly ConsoleHistory _history;
    private readonly IConsoleCommandRegistry _commandRegistry;
    private bool _isVisible;
    private bool _disposed;

    /// <summary>
    /// Gets whether the console is visible.
    /// </summary>
    public bool IsVisible => _isVisible;

    /// <summary>
    /// Gets the output buffer.
    /// </summary>
    public ConsoleBuffer OutputBuffer => _outputBuffer;

    /// <summary>
    /// Gets the command history.
    /// </summary>
    public ConsoleHistory History => _history;

    /// <summary>
    /// Gets the command registry.
    /// </summary>
    public IConsoleCommandRegistry CommandRegistry => _commandRegistry;

    /// <summary>
    /// Gets or sets the performance statistics provider.
    /// </summary>
    public IPerformanceStats? PerformanceStats { get; set; }

    /// <summary>
    /// Gets or sets the time control provider.
    /// </summary>
    public ITimeControl? TimeControl { get; set; }

    /// <summary>
    /// Initializes a new console service.
    /// </summary>
    /// <param name="commandRegistry">The command registry. If null, creates a new one with auto-discovery.</param>
    /// <param name="maxOutputLines">Maximum output buffer lines.</param>
    /// <param name="maxHistoryLines">Maximum history entries.</param>
    public ConsoleService(
        IConsoleCommandRegistry? commandRegistry = null,
        int maxOutputLines = 1000,
        int maxHistoryLines = 100
    )
    {
        _outputBuffer = new ConsoleBuffer(maxOutputLines);
        _history = new ConsoleHistory(maxHistoryLines);
        _commandRegistry = commandRegistry ?? new ConsoleCommandRegistry();
    }

    /// <summary>
    /// Shows the console.
    /// </summary>
    public void Show()
    {
        if (_isVisible)
            return;

        _isVisible = true;

        var evt = new ConsoleToggledEvent { IsVisible = true };
        EventBus.Send(ref evt);

        Logger.Debug("Console shown");
    }

    /// <summary>
    /// Hides the console.
    /// </summary>
    public void Hide()
    {
        if (!_isVisible)
            return;

        _isVisible = false;

        var evt = new ConsoleToggledEvent { IsVisible = false };
        EventBus.Send(ref evt);

        Logger.Debug("Console hidden");
    }

    /// <summary>
    /// Toggles console visibility.
    /// </summary>
    public void Toggle()
    {
        if (_isVisible)
            Hide();
        else
            Show();
    }

    /// <summary>
    /// Executes a command.
    /// </summary>
    public async Task ExecuteCommandAsync(string commandText)
    {
        if (string.IsNullOrWhiteSpace(commandText))
            return;

        // Add to history
        _history.Add(commandText);
        _history.ResetNavigation();

        // Echo command
        _outputBuffer.AppendLine($"> {commandText}", ConsoleColors.Command);

        // Parse command and arguments
        var parts = ParseCommandLine(commandText);
        if (parts.Length == 0)
            return;

        var commandName = parts[0];
        var args = parts.Skip(1).ToArray();

        // Fire submission event
        var submitEvt = new CommandSubmittedEvent { CommandText = commandText };
        EventBus.Send(ref submitEvt);

        // Look up and execute command
        if (_commandRegistry.TryGetCommand(commandName, out var command))
        {
            try
            {
                var success = await command!.ExecuteAsync(this, args);

                var execEvt = new CommandExecutedEvent
                {
                    CommandText = commandText,
                    Success = success,
                };
                EventBus.Send(ref execEvt);
            }
            catch (Exception ex)
            {
                WriteError($"Error: {ex.Message}");
                Logger.Error(ex, "Command execution failed: {Command}", commandName);

                var execEvt = new CommandExecutedEvent
                {
                    CommandText = commandText,
                    Success = false,
                    ErrorMessage = ex.Message,
                };
                EventBus.Send(ref execEvt);
            }
        }
        else
        {
            WriteError($"Unknown command: {commandName}");
            WriteSystem("Type 'help' for a list of available commands.");

            var execEvt = new CommandExecutedEvent
            {
                CommandText = commandText,
                Success = false,
                ErrorMessage = $"Unknown command: {commandName}",
            };
            EventBus.Send(ref execEvt);
        }
    }

    /// <summary>
    /// Gets completions for partial input.
    /// </summary>
    public IEnumerable<string> GetCompletions(string partial)
    {
        if (string.IsNullOrEmpty(partial))
            return _commandRegistry.GetCompletions(string.Empty);

        var parts = ParseCommandLine(partial);
        if (parts.Length <= 1)
        {
            // Completing command name
            return _commandRegistry.GetCompletions(parts.FirstOrDefault() ?? string.Empty);
        }

        // Completing command arguments
        var commandName = parts[0];
        if (_commandRegistry.TryGetCommand(commandName, out var command))
        {
            var args = parts.Skip(1).ToArray();
            return command!.GetCompletions(args, args.Length - 1);
        }

        return [];
    }

    /// <summary>
    /// Gets rich completions with descriptions for partial input.
    /// </summary>
    public IEnumerable<CompletionItem> GetRichCompletions(string partial)
    {
        if (string.IsNullOrEmpty(partial))
            return _commandRegistry.GetRichCompletions(string.Empty);

        var parts = ParseCommandLine(partial);
        if (parts.Length <= 1)
        {
            // Completing command name
            return _commandRegistry.GetRichCompletions(parts.FirstOrDefault() ?? string.Empty);
        }

        // Completing command arguments
        var commandName = parts[0];
        if (_commandRegistry.TryGetCommand(commandName, out var command))
        {
            var args = parts.Skip(1).ToArray();
            // Convert simple string completions to CompletionItems
            return command!
                .GetCompletions(args, args.Length - 1)
                .Select(c => CompletionItem.Argument(c));
        }

        return [];
    }

    /// <summary>
    /// Writes a welcome message.
    /// </summary>
    public void WriteWelcome()
    {
        WriteSystem("Type 'help' for commands.");
    }

    #region IConsoleContext Implementation

    /// <summary>
    /// Writes a line to the console.
    /// </summary>
    public void WriteLine(string text, ConsoleOutputLevel level = ConsoleOutputLevel.Normal)
    {
        _outputBuffer.AppendLine(text, level);

        var evt = new ConsoleOutputEvent { Text = text, Level = level };
        EventBus.Send(ref evt);
    }

    /// <summary>
    /// Writes a success message.
    /// </summary>
    public void WriteSuccess(string text) => WriteLine(text, ConsoleOutputLevel.Success);

    /// <summary>
    /// Writes a warning message.
    /// </summary>
    public void WriteWarning(string text) => WriteLine(text, ConsoleOutputLevel.Warning);

    /// <summary>
    /// Writes an error message.
    /// </summary>
    public void WriteError(string text) => WriteLine(text, ConsoleOutputLevel.Error);

    /// <summary>
    /// Writes a system message.
    /// </summary>
    public void WriteSystem(string text) => WriteLine(text, ConsoleOutputLevel.System);

    /// <summary>
    /// Clears the console.
    /// </summary>
    public void Clear()
    {
        _outputBuffer.Clear();
        Logger.Debug("Console cleared");
    }

    #endregion

    /// <summary>
    /// Parses a command line into parts, respecting quotes.
    /// </summary>
    private static string[] ParseCommandLine(string commandLine)
    {
        var parts = new List<string>();
        var current = new System.Text.StringBuilder();
        var inQuotes = false;
        var escapeNext = false;

        foreach (var c in commandLine)
        {
            if (escapeNext)
            {
                current.Append(c);
                escapeNext = false;
                continue;
            }

            switch (c)
            {
                case '\\':
                    escapeNext = true;
                    break;
                case '"':
                    inQuotes = !inQuotes;
                    break;
                case ' ' when !inQuotes:
                    if (current.Length > 0)
                    {
                        parts.Add(current.ToString());
                        current.Clear();
                    }
                    break;
                default:
                    current.Append(c);
                    break;
            }
        }

        if (current.Length > 0)
        {
            parts.Add(current.ToString());
        }

        return parts.ToArray();
    }

    /// <summary>
    /// Disposes the console service.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
    }
}
