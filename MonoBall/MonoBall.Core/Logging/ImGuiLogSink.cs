using System;
using System.IO;
using MonoBall.Core.Diagnostics.Panels;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting.Display;

namespace MonoBall.Core.Logging;

/// <summary>
/// Custom Serilog sink that routes log events to the ImGui LogsPanel.
/// Thread-safe: logs are queued and processed on the main thread.
/// </summary>
public sealed class ImGuiLogSink : ILogEventSink
{
    private static LogsPanel? _logsPanel;
    private static long _totalEmitCalls;
    private static long _totalPanelNull;
    private readonly MessageTemplateTextFormatter _formatter;

    /// <summary>
    /// Debug: Get sink statistics.
    /// </summary>
    public static (long emitCalls, long panelNull, bool hasPanel) GetStats() =>
        (_totalEmitCalls, _totalPanelNull, _logsPanel != null);

    /// <summary>
    /// Initializes a new instance of the ImGuiLogSink.
    /// </summary>
    public ImGuiLogSink()
    {
        _formatter = new MessageTemplateTextFormatter("{Message:lj}");
    }

    /// <summary>
    /// Sets the LogsPanel instance to receive log events.
    /// </summary>
    /// <param name="panel">The LogsPanel instance.</param>
    public static void SetLogsPanel(LogsPanel? panel)
    {
        _logsPanel = panel;
    }

    /// <summary>
    /// Clears the LogsPanel only if it matches the specified instance.
    /// This prevents one DebugOverlayService from clearing another's panel during disposal.
    /// </summary>
    /// <param name="panel">The panel instance to clear (only clears if current panel matches).</param>
    public static void ClearLogsPanelIfMatch(LogsPanel? panel)
    {
        if (panel != null && ReferenceEquals(_logsPanel, panel))
        {
            _logsPanel = null;
        }
    }

    /// <summary>
    /// Emits a log event to the LogsPanel.
    /// </summary>
    /// <param name="logEvent">The log event to emit.</param>
    public void Emit(LogEvent logEvent)
    {
        System.Threading.Interlocked.Increment(ref _totalEmitCalls);

        var panel = _logsPanel;
        if (panel == null)
        {
            System.Threading.Interlocked.Increment(ref _totalPanelNull);
            return;
        }

        // Format the message
        using var writer = new StringWriter();
        _formatter.Format(logEvent, writer);
        var message = writer.ToString();

        // Extract category from source context
        var category = "General";
        if (logEvent.Properties.TryGetValue("SourceContext", out var sourceContext))
        {
            var contextStr = sourceContext.ToString().Trim('"');
            // Use just the class name, not the full namespace
            var lastDot = contextStr.LastIndexOf('.');
            category = lastDot >= 0 ? contextStr[(lastDot + 1)..] : contextStr;
        }

        // Add to the panel (thread-safe via panel's internal locking)
        panel.AddLog(logEvent.Level, message, category, logEvent.Timestamp.LocalDateTime);
    }
}
