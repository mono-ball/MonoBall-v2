namespace MonoBall.Core.Diagnostics.Console.Features;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Events;
using Services;

/// <summary>
/// Manages the console output buffer with line limit and color support.
/// Thread-safe for adding entries from multiple sources.
/// </summary>
public sealed class ConsoleBuffer
{
    private readonly List<BufferEntry> _entries = [];
    private readonly object _lock = new();
    private readonly int _maxLines;

    /// <summary>
    /// Initializes a new console buffer.
    /// </summary>
    /// <param name="maxLines">Maximum number of lines to retain.</param>
    public ConsoleBuffer(int maxLines = 1000)
    {
        if (maxLines <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxLines), "Max lines must be positive.");

        _maxLines = maxLines;
    }

    /// <summary>
    /// Gets the current number of entries.
    /// </summary>
    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _entries.Count;
            }
        }
    }

    /// <summary>
    /// Appends a line to the buffer.
    /// </summary>
    /// <param name="text">The text to append.</param>
    /// <param name="level">The output level.</param>
    public void AppendLine(string text, ConsoleOutputLevel level = ConsoleOutputLevel.Normal)
    {
        AppendLine(text, ConsoleColors.GetColor(level));
    }

    /// <summary>
    /// Appends a line to the buffer with a specific color.
    /// </summary>
    /// <param name="text">The text to append.</param>
    /// <param name="color">The text color.</param>
    public void AppendLine(string text, Vector4 color)
    {
        var entry = new BufferEntry(text, color, DateTime.Now);

        lock (_lock)
        {
            _entries.Add(entry);

            // Trim if over limit
            while (_entries.Count > _maxLines)
            {
                _entries.RemoveAt(0);
            }
        }
    }

    /// <summary>
    /// Clears all entries from the buffer.
    /// </summary>
    public void Clear()
    {
        lock (_lock)
        {
            _entries.Clear();
        }
    }

    /// <summary>
    /// Gets a snapshot of all entries.
    /// </summary>
    /// <returns>A copy of the current entries.</returns>
    public IReadOnlyList<BufferEntry> GetEntries()
    {
        lock (_lock)
        {
            return _entries.ToList();
        }
    }

    /// <summary>
    /// Iterates over entries without copying (for rendering).
    /// </summary>
    /// <param name="action">Action to perform on each entry.</param>
    public void ForEach(Action<BufferEntry> action)
    {
        lock (_lock)
        {
            foreach (var entry in _entries)
            {
                action(entry);
            }
        }
    }

    /// <summary>
    /// Represents a single buffer entry.
    /// </summary>
    /// <param name="Text">The text content.</param>
    /// <param name="Color">The display color.</param>
    /// <param name="Timestamp">When the entry was added.</param>
    public readonly record struct BufferEntry(string Text, Vector4 Color, DateTime Timestamp);
}
