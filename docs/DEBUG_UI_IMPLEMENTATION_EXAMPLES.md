# Debug UI Visual Design - Implementation Examples

This document provides practical code examples showing how to integrate the new visual indicators and icons into existing panels.

---

## Example 1: Enhanced LogsPanel with Badges

### Before: Original
```csharp
public void Draw(float deltaTime)
{
    DrawToolbar();
    ImGui.Separator();
    DrawLogList();
    ImGui.Separator();
    DrawStatusBar();
}
```

### After: With Badges & Flash
```csharp
private readonly DebugUIIndicators.FlashSystem _flashSystem = new();
private readonly DebugUIIndicators.UnreadIndicator _unreadIndicator = new();

public void Initialize()
{
    // Register with Serilog sink to trigger unread notifications
}

public void Update(float deltaTime)
{
    _flashSystem.Update(deltaTime);
    _unreadIndicator.Update(deltaTime);
}

public void Draw(float deltaTime)
{
    // Draw flash overlay first (background)
    _flashSystem.DrawFlashOverlay("logs");

    // Enhanced toolbar with badges
    DrawEnhancedToolbar();
    ImGui.Separator();
    DrawLogList();
    ImGui.Separator();
    DrawStatusBar();
}

private void DrawEnhancedToolbar()
{
    // Icon + title
    ImGui.Text("☰ Logs");

    ImGui.SameLine();

    // Count error and warning entries
    int errorCount = 0, warningCount = 0;
    lock (_logLock)
    {
        for (var i = 0; i < _logCount; i++)
        {
            var index = (_logHead - _logCount + i + MaxLogEntries) % MaxLogEntries;
            var entry = _logBuffer[index];
            if (entry.Level >= LogEventLevel.Error)
                errorCount++;
            else if (entry.Level == LogEventLevel.Warning)
                warningCount++;
        }
    }

    // Error badge with pulse
    if (errorCount > 0)
    {
        DebugUIIndicators.DrawBadgeWithPulse(
            errorCount,
            DebugColors.Error,
            pulseThreshold: 10
        );
        ImGui.SameLine();
    }

    // Warning badge
    if (warningCount > 0)
    {
        DebugUIIndicators.DrawBadge(
            warningCount,
            DebugColors.Warning,
            format: '{'
        );
        ImGui.SameLine();
    }

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

    // ... rest of toolbar
}

public void AddLog(LogEventLevel level, string message, string? category = null, DateTime? timestamp = null)
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
        _logBuffer[_logHead] = entry;
        _logHead = (_logHead + 1) % MaxLogEntries;
        _logCount = Math.Min(_logCount + 1, MaxLogEntries);

        // Trigger visual feedback on new logs
        if (level >= LogEventLevel.Warning)
        {
            _flashSystem.Flash("logs");
            _unreadIndicator.OnNewItem();
        }

        _filterDirty = true;
    }
}
```

---

## Example 2: Enhanced PerformancePanel with Indicators

### Before: Original
```csharp
public void Draw(float deltaTime)
{
    DrawToolbar();
    ImGui.Separator();
    DrawFpsSection();
    ImGui.Separator();
    DrawFrameTimeSection();
    ImGui.Separator();
    DrawMemorySection();
}
```

### After: With Health Indicator & Sparkline
```csharp
private DebugUIIndicators.RecordingIndicator _recordingIndicator = new();

public void Draw(float deltaTime)
{
    DrawEnhancedToolbar();
    ImGui.Separator();
    DrawFpsSection();
    ImGui.Separator();
    DrawFrameTimeSection();
    ImGui.Separator();
    DrawMemorySection();
}

private void DrawEnhancedToolbar()
{
    // Status dot
    var health = DebugUIIndicators.GetHealthFromFps(_fps);
    DebugUIIndicators.DrawStatusDot(health);
    ImGui.SameLine();

    // Icon + health indicator
    ImGui.Text("⏱");
    ImGui.SameLine();
    DebugUIIndicators.DrawHealthIndicator(health, _fps);

    ImGui.SameLine(250f);

    // FPS Sparkline
    DebugUIIndicators.DrawFpsSparkline(
        _frameTimeHistory,
        FrameTimeHistorySize,
        targetFps: 60f,
        sparklineWidth: 120f,
        sparklineHeight: 20f
    );

    ImGui.SameLine();

    // Memory indicator
    DebugUIIndicators.DrawCompactMemory(_gcTotalMemory);

    // Recording indicator (for profiling integration)
    ImGui.SameLine();
    _recordingIndicator.Draw();

    ImGui.SameLine();
    ImGui.SetNextItemWidth(80);
    ImGui.SliderFloat("##refresh", ref _refreshInterval, 0.1f, 2f, "%.1fs");
}

private void DrawFpsSection()
{
    ImGui.Text("Frame Rate");
    ImGui.Indent();

    var health = DebugUIIndicators.GetHealthFromFps(_fps);
    var healthColor = DebugUIIndicators.GetHealthColor(health);
    var healthChar = DebugUIIndicators.GetHealthIndicator(health);

    ImGui.TextColored(healthColor, $"{healthChar} {_fps:F1} FPS");
    ImGui.SameLine();
    ImGui.Text("Target:");
    ImGui.SameLine();
    ImGui.TextColored(DebugColors.TextValue, "60 FPS (16.67 ms)");

    ImGui.Unindent();
}

private void DrawFrameTimeSection()
{
    ImGui.Text("Frame Time");
    ImGui.Indent();

    // Current frame time with color
    ImGui.Text("Current:");
    ImGui.SameLine();
    var currentColor = _frameTime switch
    {
        <= 16.67f => DebugColors.Success,
        <= 33.33f => DebugColors.Warning,
        _ => DebugColors.Error,
    };
    ImGui.TextColored(currentColor, $"{_frameTime:F2} ms");
    ImGui.SameLine();
    ImGui.TextDisabled($"| Avg: {_avgFrameTime:F2} ms");

    // Range indicator
    ImGui.Text("Range:");
    ImGui.SameLine();
    ImGui.TextColored(DebugColors.TextValue, $"{_minFrameTime:F2} - {_maxFrameTime:F2} ms");

    // Full history graph
    ImGui.PlotLines(
        "##frametime",
        ref _frameTimeHistory[0],
        FrameTimeHistorySize,
        _frameTimeIndex,
        string.Empty,
        0f,
        _maxFrameTime * 1.2f,
        new Vector2(ImGui.GetContentRegionAvail().X, 60)
    );

    ImGui.Unindent();
}
```

---

## Example 3: Enhanced EntityInspectorPanel with Count Badge

### Before: Original
```csharp
public void Draw(float deltaTime)
{
    ImGui.Text("Entity Inspector");
    ImGui.Separator();
    // ... rest of content
}
```

### After: With Entity Count Badge & Loading Progress
```csharp
private DebugUIIndicators.FlashSystem _flashSystem = new();
private float _loadingProgress = 1f;
private bool _isLoading;

public void Update(float deltaTime)
{
    _flashSystem.Update(deltaTime);
}

public void Draw(float deltaTime)
{
    // Draw flash overlay
    _flashSystem.DrawFlashOverlay("entity_inspector");

    DrawEnhancedHeader();
    ImGui.Separator();
    // ... rest of content
}

private void DrawEnhancedHeader()
{
    ImGui.Text("■ Entity Inspector");

    ImGui.SameLine();

    // Entity count badge with capacity indicator
    DebugUIIndicators.DrawEntityCountBadge(_entityCount, MaxEntities);

    ImGui.SameLine();

    // Loading indicator if async operation
    if (_isLoading)
    {
        DebugUIIndicators.DrawLoadingProgress(_loadingProgress, 100f);
    }
}

// Trigger flash when entities change significantly
public void OnEntitiesChanged(int oldCount, int newCount)
{
    if (Math.Abs(newCount - oldCount) > 100)
    {
        _flashSystem.Flash("entity_inspector");
    }
}
```

---

## Example 4: Panel Menu Bar with Icons

Enhance the main menu bar to show panel icons.

```csharp
private void DrawMainMenuBar()
{
    if (!ImGui.BeginMainMenuBar())
        return;

    if (ImGui.BeginMenu("Panels"))
    {
        foreach (var category in _registry.Categories)
        {
            if (ImGui.BeginMenu(category))
            {
                foreach (var panel in _registry.GetPanelsByCategory(category))
                {
                    var icon = DebugUIIndicators.Icons.GetIconForPanel(panel.Id);
                    var label = $"{icon} {panel.DisplayName}";

                    var isVisible = panel.IsVisible;
                    if (ImGui.MenuItem(label, string.Empty, ref isVisible))
                    {
                        panel.IsVisible = isVisible;
                    }
                }
                ImGui.EndMenu();
            }
        }
        ImGui.EndMenu();
    }

    // Allow panels to add custom menu items
    foreach (var panel in _registry.Panels)
    {
        if (panel is IDebugPanelMenu menuPanel)
        {
            menuPanel.DrawMenuItems();
        }
    }

    ImGui.EndMainMenuBar();
}
```

---

## Example 5: Profiler with Recording Indicator

```csharp
public sealed class SystemProfilerPanel : IDebugPanel, IDebugPanelLifecycle
{
    private DebugUIIndicators.RecordingIndicator _recordingIndicator = new();

    public void Update(float deltaTime)
    {
        _recordingIndicator.Update(deltaTime);
    }

    public void Draw(float deltaTime)
    {
        DrawProfilerToolbar();
        ImGui.Separator();
        DrawProfilerData();
    }

    private void DrawProfilerToolbar()
    {
        ImGui.Text("⏱ System Profiler");

        ImGui.SameLine();

        if (ImGui.Button(_recordingIndicator.IsRecording ? "Stop" : "Start"))
        {
            _recordingIndicator.IsRecording = !_recordingIndicator.IsRecording;
        }

        ImGui.SameLine();
        _recordingIndicator.Draw();

        ImGui.SameLine();
        if (ImGui.Button("Clear"))
        {
            ClearProfilingData();
        }
    }

    public void StartRecording()
    {
        _recordingIndicator.IsRecording = true;
    }

    public void StopRecording()
    {
        _recordingIndicator.IsRecording = false;
    }
}
```

---

## Example 6: Unified Icon System in DebugPanelHelpers

Extend DebugPanelHelpers to automatically apply icons to panel titles.

```csharp
// Add to DebugPanelHelpers.cs
public static string GetPanelDisplayName(string panelId, string baseDisplayName)
{
    var icon = DebugUIIndicators.Icons.GetIconForPanel(panelId);
    return string.IsNullOrEmpty(icon) ? baseDisplayName : $"{icon} {baseDisplayName}";
}

// Usage in panels:
public string DisplayName => DebugPanelHelpers.GetPanelDisplayName("logs", "Logs");
```

---

## Example 7: Creating a Custom Indicator Component

For reusable indicator patterns in future panels:

```csharp
public class PerformanceThresholdIndicator
{
    private float _value;
    private float _thresholdWarning;
    private float _thresholdCritical;
    private string _label;

    public PerformanceThresholdIndicator(
        string label,
        float thresholdWarning = 80f,
        float thresholdCritical = 100f
    )
    {
        _label = label;
        _thresholdWarning = thresholdWarning;
        _thresholdCritical = thresholdCritical;
    }

    public void SetValue(float value)
    {
        _value = value;
    }

    public void Draw()
    {
        var color = _value switch
        {
            <= _thresholdWarning => DebugColors.Success,
            <= _thresholdCritical => DebugColors.Warning,
            _ => DebugColors.Error
        };

        ImGui.TextColored(color, $"{_label}: {_value:F1}");
    }
}

// Usage:
private PerformanceThresholdIndicator _cpuLoad =
    new("CPU Load %", thresholdWarning: 70f, thresholdCritical: 90f);

// In Update:
_cpuLoad.SetValue(currentCpuLoadPercent);

// In Draw:
_cpuLoad.Draw();
```

---

## Implementation Checklist

### Phase 1: Core Icons (1-2 hours)
- [ ] Add DebugUIIndicators.cs to project
- [ ] Update all IDebugPanel implementations to use icon constants
- [ ] Test Unicode rendering on target platform
- [ ] Verify icons display in panel titles

### Phase 2: Badges (2-3 hours)
- [ ] Implement badge drawing in LogsPanel
- [ ] Add error count tracking
- [ ] Test badge animations
- [ ] Add performance health badges to PerformancePanel

### Phase 3: Mini-Widgets (2-3 hours)
- [ ] Implement FPS sparkline in PerformancePanel
- [ ] Add entity count badge to EntityInspectorPanel
- [ ] Add memory compact display
- [ ] Test all widgets render correctly

### Phase 4: Animations (2-3 hours)
- [ ] Implement FlashSystem
- [ ] Add pulsing indicators to critical panels
- [ ] Test animation smoothness
- [ ] Profile animation performance

### Phase 5: Integration (2-3 hours)
- [ ] Update all existing panels with new indicators
- [ ] Ensure consistency across panels
- [ ] Full integration testing
- [ ] Performance benchmarking

---

## Testing Verification Points

### Visual Testing
- Verify Unicode characters render on Windows, Linux, macOS
- Check contrast ratios meet WCAG AA standards
- Ensure animations are smooth (60 FPS)
- Validate color consistency with Pokéball theme

### Functional Testing
- Badges update correctly when data changes
- Flash overlays fade properly
- Pulsing animations sync correctly
- Sparklines plot data accurately

### Performance Testing
- No GC allocations in Draw() methods
- Maintain 60 FPS with all effects active
- Memory usage < 50KB additional
- Animation calculations complete < 1ms/frame

---

## Platform-Specific Considerations

### Windows
- Unicode characters: ✓ All supported
- ImGui rendering: Standard
- Recommended fonts: Segoe UI, Consolas

### Linux
- Unicode characters: ✓ All supported
- ImGui rendering: Standard
- Recommended fonts: Liberation Sans, Ubuntu Mono

### macOS
- Unicode characters: ✓ All supported
- ImGui rendering: Standard
- Recommended fonts: SF Pro Display, Menlo

---

## Future Enhancement Ideas

1. **Animated Transitions**: Smooth color transitions when status changes
2. **Custom Themes**: Allow theme selection (neon, pastel, monochrome)
3. **Accessibility**: High contrast mode, dyslexia-friendly fonts
4. **Customization**: User-configurable badge positions/sizes
5. **Advanced Sparklines**: Multi-line sparklines for comparison
6. **Sound Effects**: Optional audio alerts for critical states
7. **Export**: Screenshot/video capture of debug sessions
