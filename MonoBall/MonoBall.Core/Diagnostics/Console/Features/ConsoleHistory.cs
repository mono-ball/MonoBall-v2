namespace MonoBall.Core.Diagnostics.Console.Features;

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Manages command history with navigation and search.
/// </summary>
public sealed class ConsoleHistory
{
    private readonly List<string> _history = [];
    private readonly int _maxHistory;
    private int _navigationIndex = -1;
    private string _savedInput = string.Empty;

    /// <summary>
    /// Initializes a new command history.
    /// </summary>
    /// <param name="maxHistory">Maximum number of commands to retain.</param>
    public ConsoleHistory(int maxHistory = 100)
    {
        if (maxHistory <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(maxHistory),
                "Max history must be positive."
            );

        _maxHistory = maxHistory;
    }

    /// <summary>
    /// Gets the number of commands in history.
    /// </summary>
    public int Count => _history.Count;

    /// <summary>
    /// Gets whether currently navigating history.
    /// </summary>
    public bool IsNavigating => _navigationIndex >= 0;

    /// <summary>
    /// Adds a command to history.
    /// </summary>
    /// <param name="command">The command to add.</param>
    public void Add(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
            return;

        // Don't add duplicates of the last command
        if (_history.Count > 0 && _history[^1] == command)
            return;

        _history.Add(command);

        // Trim if over limit
        while (_history.Count > _maxHistory)
        {
            _history.RemoveAt(0);
        }

        // Reset navigation
        ResetNavigation();
    }

    /// <summary>
    /// Navigates to the previous command in history.
    /// </summary>
    /// <param name="currentInput">The current input to save.</param>
    /// <returns>The previous command, or null if at the beginning.</returns>
    public string? NavigatePrevious(string currentInput)
    {
        if (_history.Count == 0)
            return null;

        // First navigation - save current input
        if (_navigationIndex < 0)
        {
            _savedInput = currentInput;
            _navigationIndex = _history.Count;
        }

        // Move back if possible
        if (_navigationIndex > 0)
        {
            _navigationIndex--;
            return _history[_navigationIndex];
        }

        return _history[0];
    }

    /// <summary>
    /// Navigates to the next command in history.
    /// </summary>
    /// <returns>The next command, or the saved input if at the end.</returns>
    public string NavigateNext()
    {
        if (!IsNavigating)
            return _savedInput;

        _navigationIndex++;

        // Past the end - return to saved input
        if (_navigationIndex >= _history.Count)
        {
            ResetNavigation();
            return _savedInput;
        }

        return _history[_navigationIndex];
    }

    /// <summary>
    /// Resets history navigation state.
    /// </summary>
    public void ResetNavigation()
    {
        _navigationIndex = -1;
        _savedInput = string.Empty;
    }

    /// <summary>
    /// Searches history for commands containing the query.
    /// </summary>
    /// <param name="query">The search query.</param>
    /// <returns>Matching commands, most recent first.</returns>
    public IEnumerable<string> Search(string query)
    {
        if (string.IsNullOrEmpty(query))
            return _history.AsEnumerable().Reverse();

        return _history
            .Where(cmd => cmd.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Reverse();
    }

    /// <summary>
    /// Clears all history.
    /// </summary>
    public void Clear()
    {
        _history.Clear();
        ResetNavigation();
    }

    /// <summary>
    /// Gets all history entries.
    /// </summary>
    /// <returns>History entries, oldest first.</returns>
    public IReadOnlyList<string> GetAll() => _history;
}
