namespace MonoBall.Core.Diagnostics.Panels;

using System;
using System.Collections.Generic;
using System.Numerics;
using Hexa.NET.ImGui;
using MonoBall.Core.Diagnostics.UI;
using Serilog.Events;

/// <summary>
/// Debug panel for viewing and filtering application logs.
/// Integrates with Serilog via a custom sink.
/// </summary>
public sealed class LogsPanel : IDebugPanel, IDebugPanelLifecycle
{
    private const int MaxLogEntries = 1000;
    private const int DefaultVisibleLines = 50;

    private readonly LogEntry[] _logBuffer = new LogEntry[MaxLogEntries];
    private int _logHead; // Next write position
    private int _logCount; // Number of entries in buffer
    private readonly object _logLock = new();

    // Filtering state
    private LogEventLevel _minLevel = LogEventLevel.Verbose;
    private string _searchFilter = string.Empty;
    private string _categoryFilter = string.Empty;
    private bool _autoScroll = true;
    private bool _showTimestamp = true;
    private bool _showLevel = true;
    private bool _showCategory;

    // Cached filtered logs
    private readonly List<LogEntry> _filteredLogs = new();
    private bool _filterDirty = true;

    /// <inheritdoc />
    public string Id => "logs";

    /// <inheritdoc />
    public string DisplayName => "Logs";

    /// <inheritdoc />
    public bool IsVisible { get; set; }

    /// <inheritdoc />
    public string Category => "Diagnostics";

    /// <inheritdoc />
    public int SortOrder => 1;

    /// <inheritdoc />
    public Vector2? DefaultSize => new Vector2(600, 400);

    /// <inheritdoc />
    public void Initialize()
    {
        // Register with the ImGui log sink if available
    }

    /// <inheritdoc />
    public void Update(float deltaTime)
    {
        // Filter updates handled lazily on draw
    }

    /// <inheritdoc />
    public void Draw(float deltaTime)
    {
        DrawToolbar();
        ImGui.Separator();
        DrawLogList();
        ImGui.Separator();
        DrawStatusBar();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_logLock)
        {
            _logHead = 0;
            _logCount = 0;
            _filteredLogs.Clear();
        }
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Adds a log entry. Thread-safe.
    /// </summary>
    public void AddLog(
        LogEventLevel level,
        string message,
        string? category = null,
        DateTime? timestamp = null
    )
    {
        var entry = new LogEntry
        {
            Timestamp = timestamp ?? DateTime.Now,
            Level = level,
            Message = message,
            Category = category ?? "General",
        };

        lock (_logLock)
        {
            // Ring buffer: overwrite oldest entry when full
            _logBuffer[_logHead] = entry;
            _logHead = (_logHead + 1) % MaxLogEntries;
            _logCount = Math.Min(_logCount + 1, MaxLogEntries);

            _filterDirty = true;
        }
    }

    /// <summary>
    /// Clears all log entries.
    /// </summary>
    public void Clear()
    {
        lock (_logLock)
        {
            _logHead = 0;
            _logCount = 0;
            _filteredLogs.Clear();
            _filterDirty = true;
        }
    }

    private void DrawToolbar()
    {
        // Level filter dropdown
        ImGui.Text("Level:");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(100);
        if (ImGui.BeginCombo("##level", _minLevel.ToString()))
        {
            foreach (LogEventLevel level in Enum.GetValues<LogEventLevel>())
            {
                if (ImGui.Selectable(level.ToString(), _minLevel == level))
                {
                    _minLevel = level;
                    _filterDirty = true;
                }
            }
            ImGui.EndCombo();
        }

        ImGui.SameLine();

        // Search filter
        ImGui.SetNextItemWidth(200);
        if (ImGui.InputTextWithHint("##search", "Search...", ref _searchFilter, 256))
        {
            _filterDirty = true;
        }

        ImGui.SameLine();

        // Category filter
        ImGui.SetNextItemWidth(150);
        if (ImGui.InputTextWithHint("##category", "Category...", ref _categoryFilter, 128))
        {
            _filterDirty = true;
        }

        ImGui.SameLine();

        // Clear button
        if (ImGui.Button("Clear"))
        {
            Clear();
        }

        // Second row - options
        ImGui.Checkbox("Auto-scroll", ref _autoScroll);
        ImGui.SameLine();
        ImGui.Checkbox("Timestamp", ref _showTimestamp);
        ImGui.SameLine();
        ImGui.Checkbox("Level", ref _showLevel);
        ImGui.SameLine();
        ImGui.Checkbox("Category", ref _showCategory);
    }

    // Reusable list to avoid allocations during render
    private readonly List<LogEntry> _renderList = new();

    private void DrawLogList()
    {
        UpdateFilteredLogs();

        // Copy filtered logs while holding lock (minimal time)
        lock (_logLock)
        {
            _renderList.Clear();
            _renderList.AddRange(_filteredLogs);
        }

        // Render without holding the lock
        var availableHeight = ImGui.GetContentRegionAvail().Y - 25; // Reserve space for status bar
        ImGui.BeginChild("LogList", new Vector2(0, availableHeight), ImGuiChildFlags.Borders);

        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(0, 2));

        foreach (var entry in _renderList)
        {
            DrawLogEntry(entry);
        }

        ImGui.PopStyleVar();

        // Auto-scroll to bottom
        if (_autoScroll && ImGui.GetScrollY() >= ImGui.GetScrollMaxY() - 20)
        {
            ImGui.SetScrollHereY(1.0f);
        }

        ImGui.EndChild();
    }

    private void DrawLogEntry(LogEntry entry)
    {
        var color = GetLevelColor(entry.Level);

        // Build the log line
        var line = "";

        if (_showTimestamp)
        {
            line += $"[{entry.Timestamp:HH:mm:ss.fff}] ";
        }

        if (_showLevel)
        {
            line += $"[{GetLevelShortName(entry.Level)}] ";
        }

        if (_showCategory)
        {
            line += $"[{entry.Category}] ";
        }

        line += entry.Message;

        ImGui.TextColored(color, line);
    }

    private void DrawStatusBar()
    {
        int total,
            filtered,
            errors,
            warnings;
        lock (_logLock)
        {
            total = _logCount;
            filtered = _filteredLogs.Count;
            errors = 0;
            warnings = 0;

            // Iterate ring buffer in order
            for (var i = 0; i < _logCount; i++)
            {
                var index = (_logHead - _logCount + i + MaxLogEntries) % MaxLogEntries;
                var entry = _logBuffer[index];
                if (entry.Level >= LogEventLevel.Error)
                    errors++;
                else if (entry.Level == LogEventLevel.Warning)
                    warnings++;
            }
        }

        var statusText = $"Total: {total}";
        if (filtered != total)
        {
            statusText += $" | Showing: {filtered}";
        }
        if (errors > 0)
        {
            statusText += $" | Errors: {errors}";
        }
        if (warnings > 0)
        {
            statusText += $" | Warnings: {warnings}";
        }

        ImGui.Text(statusText);

        ImGui.SameLine(ImGui.GetContentRegionAvail().X - 100);
        ImGui.TextDisabled($"Level: {_minLevel}+");
    }

    private void UpdateFilteredLogs()
    {
        if (!_filterDirty)
            return;

        lock (_logLock)
        {
            _filteredLogs.Clear();

            // Iterate ring buffer in chronological order (oldest to newest)
            for (var i = 0; i < _logCount; i++)
            {
                var index = (_logHead - _logCount + i + MaxLogEntries) % MaxLogEntries;
                var entry = _logBuffer[index];

                if (!PassesFilter(entry))
                    continue;

                _filteredLogs.Add(entry);
            }

            _filterDirty = false;
        }
    }

    private bool PassesFilter(LogEntry entry)
    {
        // Level filter
        if (entry.Level < _minLevel)
            return false;

        // Category filter
        if (
            !string.IsNullOrEmpty(_categoryFilter)
            && !entry.Category.Contains(_categoryFilter, StringComparison.OrdinalIgnoreCase)
        )
            return false;

        // Search filter
        if (
            !string.IsNullOrEmpty(_searchFilter)
            && !entry.Message.Contains(_searchFilter, StringComparison.OrdinalIgnoreCase)
        )
            return false;

        return true;
    }

    private static Vector4 GetLevelColor(LogEventLevel level)
    {
        return level switch
        {
            LogEventLevel.Verbose => DebugColors.LogVerbose,
            LogEventLevel.Debug => DebugColors.LogDebug,
            LogEventLevel.Information => DebugColors.LogInfo,
            LogEventLevel.Warning => DebugColors.LogWarning,
            LogEventLevel.Error => DebugColors.LogError,
            LogEventLevel.Fatal => DebugColors.LogFatal,
            _ => DebugColors.LogInfo,
        };
    }

    private static string GetLevelShortName(LogEventLevel level)
    {
        return level switch
        {
            LogEventLevel.Verbose => "VERB",
            LogEventLevel.Debug => "DBUG",
            LogEventLevel.Information => "INFO",
            LogEventLevel.Warning => "WARN",
            LogEventLevel.Error => "EROR",
            LogEventLevel.Fatal => "FATL",
            _ => "????",
        };
    }

    /// <summary>
    /// Internal log entry structure.
    /// </summary>
    private struct LogEntry
    {
        public DateTime Timestamp;
        public LogEventLevel Level;
        public string Message;
        public string Category;
    }
}
