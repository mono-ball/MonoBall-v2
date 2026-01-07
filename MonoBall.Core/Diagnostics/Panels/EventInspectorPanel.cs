namespace MonoBall.Core.Diagnostics.Panels;

using System;
using System.Collections.Generic;
using System.Numerics;
using Hexa.NET.ImGui;
using MonoBall.Core.Diagnostics;
using MonoBall.Core.Diagnostics.UI;
using MonoBall.Core.ECS;

/// <summary>
/// Debug panel for inspecting event bus activity.
/// Shows registered events, subscriptions, and performance metrics.
/// </summary>
public sealed class EventInspectorPanel : IDebugPanel, IDebugPanelLifecycle
{
    private readonly Dictionary<string, EventMetrics> _eventMetrics = new();
    private readonly List<EventMetrics> _sortedMetrics = new();
    private readonly TableSortState<EventMetrics> _sortState;
    private Func<IReadOnlyDictionary<string, EventData>?>? _eventProvider;
    private EventDispatchHook? _dispatchHookSubscription;

    private bool _showSubscribers = true;
    private float _refreshInterval = 0.5f;
    private float _timeSinceRefresh;
    private string _filterText = string.Empty;
    private string? _selectedEvent;

    /// <inheritdoc />
    public string Id => "event-inspector";

    /// <inheritdoc />
    public string DisplayName => "Event Inspector";

    /// <inheritdoc />
    public bool IsVisible { get; set; }

    /// <inheritdoc />
    public string Category => "ECS";

    /// <inheritdoc />
    public int SortOrder => 1;

    /// <inheritdoc />
    public Vector2? DefaultSize => new Vector2(600, 400);

    public EventInspectorPanel()
    {
        _sortState = new TableSortState<EventMetrics>()
            .AddColumn(
                "Event Type",
                (a, b) => string.Compare(a.EventType, b.EventType, StringComparison.Ordinal),
                ImGuiTableColumnFlags.WidthStretch | ImGuiTableColumnFlags.DefaultSort
            )
            .AddColumn(
                "Subs",
                (a, b) => a.SubscriberCount.CompareTo(b.SubscriberCount),
                ImGuiTableColumnFlags.WidthFixed,
                45
            )
            .AddColumn(
                "Count",
                (a, b) => a.DispatchCount.CompareTo(b.DispatchCount),
                ImGuiTableColumnFlags.WidthFixed,
                55
            )
            .AddColumn(
                "Avg (ms)",
                (a, b) => a.Tracker.AvgMs.CompareTo(b.Tracker.AvgMs),
                ImGuiTableColumnFlags.WidthFixed,
                65
            )
            .AddColumn(
                "Max (ms)",
                (a, b) => a.Tracker.MaxMs.CompareTo(b.Tracker.MaxMs),
                ImGuiTableColumnFlags.WidthFixed,
                65
            )
            .SetDefaultSort(2, SortDirection.Descending);
    }

    /// <inheritdoc />
    public void Initialize()
    {
        // Subscribe to event dispatch notifications using IDisposable pattern
        _dispatchHookSubscription = EventDispatchHook.Subscribe(
            (eventType, subscriberCount, elapsedMs) =>
            {
                RecordDispatch(eventType, subscriberCount, elapsedMs);
            }
        );
    }

    /// <inheritdoc />
    public void Update(float deltaTime)
    {
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

        var availableWidth = ImGui.GetContentRegionAvail().X;
        var leftPanelWidth = _showSubscribers ? availableWidth * 0.6f : availableWidth;

        // Event list (resizable)
        ImGui.BeginChild(
            "EventList",
            new Vector2(leftPanelWidth, 0),
            DebugPanelHelpers.ResizableChildFlags
        );
        DrawEventTable();
        ImGui.EndChild();

        if (_showSubscribers)
        {
            ImGui.SameLine();

            // Subscriber details
            ImGui.BeginChild(
                "SubscriberDetails",
                new Vector2(0, 0),
                DebugPanelHelpers.StandardChildFlags
            );
            DrawSubscriberDetails();
            ImGui.EndChild();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        // Dispose hook subscription (per .cursorrules - must unsubscribe in Dispose)
        _dispatchHookSubscription?.Dispose();
        _dispatchHookSubscription = null;

        _eventMetrics.Clear();
        _sortedMetrics.Clear();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Sets the event data provider function.
    /// </summary>
    public void SetEventProvider(Func<IReadOnlyDictionary<string, EventData>?>? provider)
    {
        _eventProvider = provider;
    }

    /// <summary>
    /// Records an event dispatch.
    /// </summary>
    public void RecordDispatch(string eventType, int subscriberCount, double elapsedMs)
    {
        if (!_eventMetrics.TryGetValue(eventType, out var metrics))
        {
            metrics = new EventMetrics(eventType);
            _eventMetrics[eventType] = metrics;
        }

        metrics.RecordDispatch(subscriberCount, elapsedMs);
    }

    private void DrawToolbar()
    {
        ImGui.Checkbox("Subscribers", ref _showSubscribers);

        ImGui.SameLine();
        DebugPanelHelpers.DrawFilterInput(ref _filterText, "Filter events...");

        ImGui.SameLine();
        DebugPanelHelpers.DrawRefreshSlider(ref _refreshInterval);
    }

    private void DrawEventTable()
    {
        // Show message if no data
        if (_eventMetrics.Count == 0)
        {
            DebugPanelHelpers.DrawDisabledText("No events recorded.");
            DebugPanelHelpers.DrawDisabledText("Events will appear here when dispatched.");
            if (_eventProvider == null)
            {
                ImGui.Spacing();
                DebugPanelHelpers.DrawWarningText("Note: No event provider configured.");
                DebugPanelHelpers.DrawDisabledText(
                    "Call SetEventProvider() or use RecordDispatch()"
                );
            }
            return;
        }

        if (!ImGui.BeginTable("EventTable", 5, DebugPanelHelpers.SortableTableFlags))
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
                && !metrics.EventType.Contains(_filterText, StringComparison.OrdinalIgnoreCase)
            )
                continue;

            DrawEventRow(metrics);
        }

        ImGui.EndTable();
    }

    private void DrawEventRow(EventMetrics metrics)
    {
        var isSelected = _selectedEvent == metrics.EventType;
        var color = GetEventColor(metrics);

        ImGui.TableNextRow();

        // Event type (selectable)
        ImGui.TableNextColumn();
        if (ImGui.Selectable(metrics.ShortName, isSelected, ImGuiSelectableFlags.SpanAllColumns))
        {
            _selectedEvent = metrics.EventType;
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(metrics.EventType);
        }

        // Subscribers
        ImGui.TableNextColumn();
        ImGui.Text(metrics.SubscriberCount.ToString());

        // Dispatch count
        ImGui.TableNextColumn();
        ImGui.TextColored(
            metrics.DispatchCount > 0 ? DebugColors.Success : DebugColors.Inactive,
            metrics.DispatchCount.ToString()
        );

        // Avg time
        ImGui.TableNextColumn();
        ImGui.TextColored(color, $"{metrics.Tracker.AvgMs:F3}");

        // Max time
        ImGui.TableNextColumn();
        ImGui.TextColored(color, $"{metrics.Tracker.MaxMs:F3}");
    }

    private void DrawSubscriberDetails()
    {
        ImGui.Text("Subscriber Details");
        ImGui.Separator();

        if (string.IsNullOrEmpty(_selectedEvent))
        {
            DebugPanelHelpers.DrawDisabledText("Select an event to view subscribers");
            return;
        }

        if (!_eventMetrics.TryGetValue(_selectedEvent, out var metrics))
        {
            DebugPanelHelpers.DrawDisabledText("Event not found");
            return;
        }

        ImGui.Text($"Event: {metrics.ShortName}");
        DebugPanelHelpers.DrawDisabledText(metrics.EventType);
        ImGui.Separator();

        ImGui.Text($"Subscribers: {metrics.SubscriberCount}");
        ImGui.Text($"Total Dispatches: {metrics.DispatchCount}");
        ImGui.Text($"Dispatches/sec: {metrics.DispatchesPerSecond:F1}");
        ImGui.Separator();

        ImGui.Text("Timing:");
        ImGui.Indent();
        ImGui.Text($"Last: {metrics.Tracker.LastMs:F3} ms");
        ImGui.Text($"Avg:  {metrics.Tracker.AvgMs:F3} ms");
        ImGui.Text($"Max:  {metrics.Tracker.MaxMs:F3} ms");
        ImGui.Unindent();

        if (metrics.Subscribers.Count > 0)
        {
            ImGui.Separator();
            ImGui.Text("Subscriber List:");
            ImGui.BeginChild(
                "SubscriberList",
                new Vector2(0, 0),
                DebugPanelHelpers.StandardChildFlags
            );
            foreach (var subscriber in metrics.Subscribers)
            {
                ImGui.BulletText(subscriber);
            }
            ImGui.EndChild();
        }
    }

    private void RefreshMetrics()
    {
        var eventData = _eventProvider?.Invoke();
        if (eventData == null)
            return;

        foreach (var (eventType, data) in eventData)
        {
            if (!_eventMetrics.TryGetValue(eventType, out var metrics))
            {
                metrics = new EventMetrics(eventType);
                _eventMetrics[eventType] = metrics;
            }

            metrics.UpdateFromData(data);
        }
    }

    private void UpdateSortedMetrics()
    {
        _sortedMetrics.Clear();
        _sortedMetrics.AddRange(_eventMetrics.Values);
        _sortState.Sort(_sortedMetrics);
    }

    private static Vector4 GetEventColor(EventMetrics metrics)
    {
        return metrics.Tracker.AvgMs switch
        {
            < 0.1 => DebugColors.TimingFast,
            < 0.5 => DebugColors.TimingMedium,
            _ => DebugColors.TimingSlow,
        };
    }

    /// <summary>
    /// Metrics for a single event type using shared MetricsTracker.
    /// </summary>
    private sealed class EventMetrics
    {
        private DateTime _lastDispatchTime = DateTime.Now;
        private int _dispatchesInWindow;

        public string EventType { get; }
        public string ShortName =>
            EventType.Contains('.') ? EventType[(EventType.LastIndexOf('.') + 1)..] : EventType;

        public MetricsTracker Tracker { get; }
        public int SubscriberCount { get; private set; }
        public long DispatchCount { get; private set; }
        public double DispatchesPerSecond { get; private set; }
        public List<string> Subscribers { get; } = new();

        public EventMetrics(string eventType)
        {
            EventType = eventType;
            Tracker = new MetricsTracker(30);
        }

        public void RecordDispatch(int subscriberCount, double elapsedMs)
        {
            SubscriberCount = subscriberCount;
            DispatchCount++;
            Tracker.RecordTiming(elapsedMs);

            // Track dispatches per second
            _dispatchesInWindow++;
            var elapsed = (DateTime.Now - _lastDispatchTime).TotalSeconds;
            if (elapsed >= 1.0)
            {
                DispatchesPerSecond = _dispatchesInWindow / elapsed;
                _dispatchesInWindow = 0;
                _lastDispatchTime = DateTime.Now;
            }
        }

        public void UpdateFromData(EventData data)
        {
            SubscriberCount = data.SubscriberCount;
            DispatchCount = data.DispatchCount;

            // Update tracker with latest timing
            if (data.LastMs > 0)
            {
                Tracker.RecordTiming(data.LastMs);
            }

            Subscribers.Clear();
            if (data.SubscriberNames != null)
            {
                Subscribers.AddRange(data.SubscriberNames);
            }
        }
    }
}

/// <summary>
/// Data for a single event type.
/// </summary>
public struct EventData
{
    /// <summary>Number of active subscribers.</summary>
    public int SubscriberCount;

    /// <summary>Total number of dispatches.</summary>
    public long DispatchCount;

    /// <summary>Last dispatch time in milliseconds.</summary>
    public double LastMs;

    /// <summary>Average dispatch time in milliseconds.</summary>
    public double AvgMs;

    /// <summary>Maximum dispatch time in milliseconds.</summary>
    public double MaxMs;

    /// <summary>Names of subscribers (optional).</summary>
    public IReadOnlyList<string>? SubscriberNames;
}
