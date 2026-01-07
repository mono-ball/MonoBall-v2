namespace MonoBall.Core.Diagnostics.Panels;

using System;
using System.Collections.Generic;
using System.Numerics;
using Hexa.NET.ImGui;
using MonoBall.Core.Diagnostics.UI;

/// <summary>
/// Debug panel for profiling ECS system execution times.
/// Shows per-system timing with sorting and filtering.
/// </summary>
public sealed class SystemProfilerPanel : IDebugPanel, IDebugPanelLifecycle
{
    private readonly Dictionary<string, SystemMetrics> _systemMetrics = new();
    private readonly List<SystemMetrics> _sortedMetrics = new();
    private readonly TableSortState<SystemMetrics> _sortState;
    private Func<IReadOnlyDictionary<string, SystemTimingData>?>? _timingProvider;
    private SystemTimingHook? _timingHookSubscription;

    private bool _showOnlyActive = true;
    private float _refreshInterval = 0.25f;
    private float _timeSinceRefresh;
    private string _filterText = string.Empty;

    /// <inheritdoc />
    public string Id => "system-profiler";

    /// <inheritdoc />
    public string DisplayName => "System Profiler";

    /// <inheritdoc />
    public bool IsVisible { get; set; }

    /// <inheritdoc />
    public string Category => "Diagnostics";

    /// <inheritdoc />
    public int SortOrder => 2;

    /// <inheritdoc />
    public Vector2? DefaultSize => new Vector2(550, 400);

    public SystemProfilerPanel()
    {
        _sortState = new TableSortState<SystemMetrics>()
            .AddColumn(
                "System",
                (a, b) => string.Compare(a.Name, b.Name, StringComparison.Ordinal),
                ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.DefaultSort,
                180
            )
            .AddColumn(
                "Last (ms)",
                (a, b) => a.Tracker.LastMs.CompareTo(b.Tracker.LastMs),
                ImGuiTableColumnFlags.WidthFixed,
                70
            )
            .AddColumn(
                "Avg (ms)",
                (a, b) => a.Tracker.AvgMs.CompareTo(b.Tracker.AvgMs),
                ImGuiTableColumnFlags.WidthFixed,
                70
            )
            .AddColumn(
                "Max (ms)",
                (a, b) => a.Tracker.MaxMs.CompareTo(b.Tracker.MaxMs),
                ImGuiTableColumnFlags.WidthFixed,
                70
            )
            .AddColumn(
                "Usage",
                (_, _) => 0,
                ImGuiTableColumnFlags.WidthStretch | ImGuiTableColumnFlags.NoSort
            )
            .SetDefaultSort(1, SortDirection.Descending);
    }

    /// <inheritdoc />
    public void Initialize()
    {
        // Subscribe to system timing events using IDisposable pattern
        _timingHookSubscription = SystemTimingHook.Subscribe(
            (systemName, elapsedMs) =>
            {
                RecordTiming(systemName, elapsedMs);
            }
        );
    }

    /// <inheritdoc />
    public void Update(float deltaTime)
    {
        // Update activity timers for all systems
        foreach (var metrics in _systemMetrics.Values)
        {
            metrics.Tracker.UpdateActivityTimer(deltaTime);
        }

        if (
            DebugPanelHelpers.UpdateRefreshTimer(ref _timeSinceRefresh, _refreshInterval, deltaTime)
        )
        {
            RefreshMetrics();
        }
    }

    /// <inheritdoc />
    public void Draw(float deltaTime)
    {
        DrawToolbar();
        ImGui.Separator();
        DrawSystemTable();
        DrawSummary();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        // Dispose hook subscription (per .cursorrules - must unsubscribe in Dispose)
        _timingHookSubscription?.Dispose();
        _timingHookSubscription = null;

        _systemMetrics.Clear();
        _sortedMetrics.Clear();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Sets the timing data provider function.
    /// </summary>
    public void SetTimingProvider(Func<IReadOnlyDictionary<string, SystemTimingData>?>? provider)
    {
        _timingProvider = provider;
    }

    /// <summary>
    /// Records a system timing measurement.
    /// </summary>
    public void RecordTiming(string systemName, double elapsedMs)
    {
        if (!_systemMetrics.TryGetValue(systemName, out var metrics))
        {
            metrics = new SystemMetrics(systemName);
            _systemMetrics[systemName] = metrics;
        }

        metrics.Tracker.RecordTiming(elapsedMs);
    }

    private void DrawToolbar()
    {
        ImGui.Checkbox("Active only", ref _showOnlyActive);

        ImGui.SameLine();
        DebugPanelHelpers.DrawFilterInput(ref _filterText);

        ImGui.SameLine();
        DebugPanelHelpers.DrawRefreshSlider(ref _refreshInterval);
    }

    private void DrawSystemTable()
    {
        // Show message if no data
        if (_systemMetrics.Count == 0)
        {
            DebugPanelHelpers.DrawDisabledText("No system timing data.");
            DebugPanelHelpers.DrawDisabledText("Systems will appear here when profiled.");
            if (_timingProvider == null)
            {
                ImGui.Spacing();
                DebugPanelHelpers.DrawWarningText("Note: No timing provider configured.");
                DebugPanelHelpers.DrawDisabledText(
                    "Call SetTimingProvider() or use RecordTiming()"
                );
            }
            return;
        }

        var availableHeight = ImGui.GetContentRegionAvail().Y - 45;

        if (
            !ImGui.BeginTable(
                "ProfilerTable",
                5,
                DebugPanelHelpers.SortableTableFlags,
                new Vector2(0, availableHeight)
            )
        )
            return;

        _sortState.SetupColumns();
        _sortState.HandleSortSpecs();

        // Sort and filter metrics
        UpdateSortedMetrics();

        foreach (var metrics in _sortedMetrics)
        {
            // Filter check
            if (
                !string.IsNullOrEmpty(_filterText)
                && !metrics.Name.Contains(_filterText, StringComparison.OrdinalIgnoreCase)
            )
                continue;

            // Active only check
            if (_showOnlyActive && !metrics.Tracker.IsActive)
                continue;

            DrawSystemRow(metrics);
        }

        ImGui.EndTable();
    }

    private void DrawSystemRow(SystemMetrics metrics)
    {
        var isActive = metrics.Tracker.IsActive;
        var color = isActive
            ? DebugPanelHelpers.GetTimingColor(metrics.Tracker.LastMs)
            : DebugColors.Inactive;

        ImGui.TableNextRow();

        // System name
        ImGui.TableNextColumn();
        ImGui.TextColored(color, metrics.Name);

        // Last
        ImGui.TableNextColumn();
        ImGui.TextColored(color, $"{metrics.Tracker.LastMs:F3}");

        // Avg
        ImGui.TableNextColumn();
        ImGui.TextColored(
            isActive ? DebugColors.TextSecondary : DebugColors.Inactive,
            $"{metrics.Tracker.AvgMs:F3}"
        );

        // Max
        ImGui.TableNextColumn();
        ImGui.TextColored(
            isActive ? DebugColors.TextSecondary : DebugColors.Inactive,
            $"{metrics.Tracker.MaxMs:F3}"
        );

        // Progress bar showing percentage of frame budget
        ImGui.TableNextColumn();
        var fraction = (float)(metrics.Tracker.LastMs / DebugPanelHelpers.TargetFrameTimeMs);
        if (isActive)
        {
            ImGui.ProgressBar(Math.Min(fraction, 1f), new Vector2(-1, 14), $"{fraction * 100:F1}%");
        }
        else
        {
            ImGui.TextColored(DebugColors.Inactive, "-");
        }
    }

    private void DrawSummary()
    {
        double totalMs = 0;
        var activeCount = 0;
        var maxSystemMs = 0.0;
        var slowestSystem = "";

        foreach (var metrics in _systemMetrics.Values)
        {
            if (metrics.Tracker.IsActive)
            {
                totalMs += metrics.Tracker.LastMs;
                activeCount++;
                if (metrics.Tracker.LastMs > maxSystemMs)
                {
                    maxSystemMs = metrics.Tracker.LastMs;
                    slowestSystem = metrics.Name;
                }
            }
        }

        var budgetUsed = totalMs / DebugPanelHelpers.TargetFrameTimeMs * 100;
        var budgetColor = DebugPanelHelpers.GetBudgetColor(budgetUsed);

        // Left side: counts
        ImGui.Text($"Active: {activeCount}/{_systemMetrics.Count}");

        // Right side: budget info
        var budgetText = $"{totalMs:F2}ms ({budgetUsed:F0}%)";

        ImGui.SameLine(ImGui.GetContentRegionAvail().X - ImGui.CalcTextSize(budgetText).X - 10);
        ImGui.TextColored(budgetColor, budgetText);

        if (!string.IsNullOrEmpty(slowestSystem))
        {
            DebugPanelHelpers.DrawDisabledText($"Slowest: {slowestSystem}");
        }
    }

    private void RefreshMetrics()
    {
        var timingData = _timingProvider?.Invoke();
        if (timingData == null)
            return;

        foreach (var (name, data) in timingData)
        {
            if (!_systemMetrics.TryGetValue(name, out var metrics))
            {
                metrics = new SystemMetrics(name);
                _systemMetrics[name] = metrics;
            }

            metrics.Tracker.RecordTiming(data.LastMs);
        }
    }

    private void UpdateSortedMetrics()
    {
        _sortedMetrics.Clear();
        _sortedMetrics.AddRange(_systemMetrics.Values);
        _sortState.Sort(_sortedMetrics);
    }

    /// <summary>
    /// Metrics wrapper that uses the shared MetricsTracker.
    /// </summary>
    private sealed class SystemMetrics
    {
        public string Name { get; }
        public MetricsTracker Tracker { get; }

        public SystemMetrics(string name)
        {
            Name = name;
            Tracker = new MetricsTracker(60);
        }
    }
}

/// <summary>
/// Timing data for a single system.
/// </summary>
public struct SystemTimingData
{
    /// <summary>
    /// Last execution time in milliseconds.
    /// </summary>
    public double LastMs;

    /// <summary>
    /// Average execution time in milliseconds.
    /// </summary>
    public double AvgMs;

    /// <summary>
    /// Maximum execution time in milliseconds.
    /// </summary>
    public double MaxMs;
}
