# MonoBall Debug UI Visual Design Review

## Executive Summary

This document provides comprehensive visual design specifications for enhancing the MonoBall debug panels with status badges, icons, mini-widgets, and visual feedback systems. All recommendations use ImGui-compatible Unicode symbols and color-coding strategies aligned with the existing Pokéball theme.

---

## Part 1: Icon Concepts & Unicode Representations

### 1.1 Panel Icons (Toolbar Display)

All panels should display consistent icons in the toolbar area using Unicode symbols. These provide visual identity and quick recognition.

| Panel | Unicode | Character | Usage | Color |
|-------|---------|-----------|-------|-------|
| **Performance** | `U+23F1` | ⏱ | Timer/stopwatch icon | Info Blue |
| **Performance** | `U+1F4CA` | 📊 | Chart indicator (fallback) | Info Blue |
| **Console** | `U+2318` | ⌘ | Command/terminal | Text Primary |
| **Console** | `U+26A1` | ⚡ | Lightning (active) | Warning |
| **Logs** | `U+2630` | ☰ | List/scroll icon | Text Secondary |
| **Logs** | `U+1F4DD` | 📝 | Document (fallback) | Text Secondary |
| **Entity Inspector** | `U+25A0` | ■ | Cube/box symbol | Success Green |
| **Entity Inspector** | `U+23AE` | ⎮ | Component block | Info Blue |
| **Scene Inspector** | `U+2756` | ❖ | Hierarchy/node | Highlight Yellow |
| **Scene Inspector** | `U+1F332` | 🌲 | Tree (fallback) | Success Green |
| **Event Inspector** | `U+26A1` | ⚡ | Lightning/event | Blocking Orange |
| **Event Inspector** | `U+1F308` | 🌈 | Spectrum (fallback) | Blocking Orange |
| **Profiler** | `U+23F1` | ⏱ | Timer (primary) | Info Blue |
| **Profiler** | `U+1F506` | 🔆 | Brightness levels | Text Secondary |
| **Mod Browser** | `U+1F9E9` | 🧩 | Puzzle/component | Highlight Yellow |
| **Mod Browser** | `U+27A4` | ➤ | Navigation arrow | Info Blue |
| **Definition Browser** | `U+1F4D1` | 📑 | Bookmarks/database | Highlight Yellow |
| **Definition Browser** | `U+23F2` | ⏲ | Clock variant | Text Secondary |

### 1.2 Implementation Pattern

```csharp
// In each panel's DisplayName or toolbar
private string GetPanelIconLabel()
{
    return panel.Id switch
    {
        "performance" => "⏱ Performance",
        "console" => "⌘ Console",
        "logs" => "☰ Logs",
        "entity_inspector" => "■ Entities",
        "scene_inspector" => "❖ Scene",
        "event_inspector" => "⚡ Events",
        "profiler" => "⏱ Profiler",
        "mod_browser" => "🧩 Mods",
        "definition_browser" => "📑 Definitions",
        _ => panel.DisplayName
    };
}
```

---

## Part 2: Status Badges & Indicators

### 2.1 Error Count Badge (Logs Panel)

Display unread/recent error counts directly on the panel icon.

```csharp
public void DrawLogsPanelWithBadge(LogsPanel logs, int errorCount)
{
    // Main panel title
    ImGui.Text("☰ Logs");

    if (errorCount > 0)
    {
        ImGui.SameLine();

        // Draw red badge
        var badgeColor = DebugColors.Error;
        var badgeText = errorCount > 99 ? "99+" : errorCount.ToString();

        ImGui.TextColored(badgeColor, $"[{badgeText}]");

        // Optional: Pulse animation
        if (ShouldPulseBadge(errorCount))
        {
            ImGui.SameLine();
            ImGui.TextColored(badgeColor, "◆");
        }
    }
}

private bool ShouldPulseBadge(int errorCount)
{
    // Pulse for critical errors (>10)
    return errorCount > 10 && (DateTime.Now.Millisecond % 500) < 250;
}
```

**Badge Styling Rules:**
- Errors: Red (`DebugColors.Error`) - Uses `[NN]` format
- Warnings: Yellow (`DebugColors.Warning`) - Uses `{NN}` format
- Info: Blue (`DebugColors.Info`) - Uses `(NN)` format
- Max display: "99+" for large counts
- Pulse on critical: > 10 items, alternates every 500ms

### 2.2 Performance Health Indicator

Visual indicator showing system performance state.

```csharp
public enum PerformanceHealth
{
    Excellent,  // 60+ FPS
    Good,       // 45-60 FPS
    Fair,       // 30-45 FPS
    Warning,    // 20-30 FPS
    Critical    // < 20 FPS
}

public string GetPerformanceIndicator(float fps)
{
    var health = fps switch
    {
        >= 60f => PerformanceHealth.Excellent,
        >= 45f => PerformanceHealth.Good,
        >= 30f => PerformanceHealth.Fair,
        >= 20f => PerformanceHealth.Warning,
        _ => PerformanceHealth.Critical
    };

    return health switch
    {
        PerformanceHealth.Excellent => "● " + fps.ToString("F1"),  // Full circle - green
        PerformanceHealth.Good => "◐ " + fps.ToString("F1"),       // 3/4 circle - lime
        PerformanceHealth.Fair => "◑ " + fps.ToString("F1"),       // Half circle - yellow
        PerformanceHealth.Warning => "◕ " + fps.ToString("F1"),    // 1/4 circle - orange
        PerformanceHealth.Critical => "○ " + fps.ToString("F1"),   // Empty circle - red
        _ => "? " + fps.ToString("F1")
    };
}

public Vector4 GetHealthColor(PerformanceHealth health)
{
    return health switch
    {
        PerformanceHealth.Excellent => DebugColors.Success,
        PerformanceHealth.Good => new Vector4(0.7f, 1f, 0.4f, 1f),  // Light green
        PerformanceHealth.Fair => DebugColors.Warning,
        PerformanceHealth.Warning => DebugColors.Blocking,
        PerformanceHealth.Critical => DebugColors.Error,
        _ => DebugColors.TextSecondary
    };
}
```

**Visual Representation:**
```
Excellent  ●  (solid full circle)     - Green    - 60+ FPS
Good       ◐  (3/4 filled)             - Lime     - 45-60 FPS
Fair       ◑  (half filled)            - Yellow   - 30-45 FPS
Warning    ◕  (1/4 filled)             - Orange   - 20-30 FPS
Critical   ○  (empty circle)           - Red      - <20 FPS
```

### 2.3 Active/Recording Indicator (Profiler)

Show when profiler is actively recording.

```csharp
public class ProfilerRecordingIndicator
{
    private float _pulsePhase;
    private bool _isRecording;

    public void Update(float deltaTime)
    {
        if (_isRecording)
        {
            _pulsePhase += deltaTime * 3f; // 3x speed
            if (_pulsePhase > 1f) _pulsePhase -= 1f;
        }
    }

    public void DrawRecordingBadge()
    {
        if (!_isRecording) return;

        // Pulsing red dot
        var intensity = 0.5f + 0.5f * MathF.Sin(_pulsePhase * MathF.PI * 2f);
        var pulsedColor = new Vector4(
            DebugColors.Error.X * intensity,
            DebugColors.Error.Y,
            DebugColors.Error.Z,
            1f
        );

        ImGui.TextColored(pulsedColor, "● Recording");
    }
}
```

**Indicator States:**
- Idle: ○ "Ready" (gray)
- Recording: ● "Recording" (pulsing red, 3 Hz)
- Paused: ⏸ "Paused" (yellow)

### 2.4 Entity Count Badge

Display current entity count with visual style.

```csharp
public void DrawEntityCountBadge(int count, int maxCapacity = 10000)
{
    var healthPercent = (float)count / maxCapacity;
    var color = healthPercent switch
    {
        <= 0.5f => DebugColors.Success,      // Green - under 50%
        <= 0.75f => DebugColors.Warning,     // Yellow - 50-75%
        <= 0.9f => DebugColors.Blocking,     // Orange - 75-90%
        _ => DebugColors.Error                // Red - over 90%
    };

    var countStr = $"{count:N0}";
    ImGui.TextColored(color, $"[{countStr}]");

    // Show capacity bar on hover
    if (ImGui.IsItemHovered())
    {
        ImGui.SetTooltip($"{count:N0} / {maxCapacity:N0} entities ({healthPercent*100:F1}%)");
    }
}
```

### 2.5 Unread Log Messages Indicator

Highlight panel when new logs arrive.

```csharp
public class UnreadLogIndicator
{
    private int _unreadCount;
    private float _lastFlashTime;
    private const float FlashDuration = 0.3f;

    public void OnLogAdded(LogEventLevel level)
    {
        if (level >= LogEventLevel.Warning)
        {
            _unreadCount++;
            _lastFlashTime = 0f;
        }
    }

    public void Update(float deltaTime)
    {
        _lastFlashTime += deltaTime;
    }

    public void DrawFlashIndicator()
    {
        if (_unreadCount == 0) return;

        // Fade out over FlashDuration
        var alpha = Math.Max(0f, 1f - (_lastFlashTime / FlashDuration));

        var flashColor = new Vector4(
            DebugColors.Warning.X,
            DebugColors.Warning.Y,
            DebugColors.Warning.Z,
            alpha * 0.5f
        );

        ImGui.GetWindowDrawList().AddRectFilled(
            ImGui.GetWindowPos(),
            ImGui.GetWindowPos() + ImGui.GetWindowSize(),
            ImGui.GetColorU32(flashColor)
        );

        if (alpha <= 0.01f)
        {
            _unreadCount = 0;
        }
    }
}
```

---

## Part 3: Mini-Widgets for Toolbar

### 3.1 FPS Sparkline (Performance Panel)

Compact graph of last 60 FPS samples.

```csharp
public void DrawFpsSparkline(float[] fpsHistory, int count, float targetFps = 60f)
{
    const float sparklineHeight = 20f;
    const float sparklineWidth = 120f;

    // Calculate bounds
    float minFps = float.MaxValue, maxFps = 0f;
    for (int i = 0; i < count; i++)
    {
        var fps = fpsHistory[i];
        if (fps > 0)
        {
            minFps = Math.Min(minFps, fps);
            maxFps = Math.Max(maxFps, fps);
        }
    }

    if (minFps == float.MaxValue) minFps = 0f;
    maxFps = Math.Max(maxFps, targetFps);

    var drawList = ImGui.GetWindowDrawList();
    var p0 = ImGui.GetCursorScreenPos();
    var p1 = new Vector2(p0.X + sparklineWidth, p0.Y + sparklineHeight);

    // Background
    drawList.AddRectFilled(p0, p1, ImGui.GetColorU32(DebugColors.BackgroundSecondary));

    // Target line (60 FPS reference)
    var targetY = p0.Y + sparklineHeight - (sparklineHeight * (targetFps - minFps) / (maxFps - minFps + 0.1f));
    drawList.AddLine(
        new Vector2(p0.X, targetY),
        new Vector2(p1.X, targetY),
        ImGui.GetColorU32(DebugColors.TextDim),
        1f
    );

    // Plot line
    for (int i = 0; i < count - 1; i++)
    {
        var x1 = p0.X + (float)i / (count - 1) * sparklineWidth;
        var x2 = p0.X + (float)(i + 1) / (count - 1) * sparklineWidth;

        var fps1 = fpsHistory[i];
        var fps2 = fpsHistory[i + 1];

        if (fps1 > 0 && fps2 > 0)
        {
            var y1 = p0.Y + sparklineHeight - (sparklineHeight * (fps1 - minFps) / (maxFps - minFps + 0.1f));
            var y2 = p0.Y + sparklineHeight - (sparklineHeight * (fps2 - minFps) / (maxFps - minFps + 0.1f));

            // Color based on performance
            var color = fps1 >= targetFps ? DebugColors.Success :
                       fps1 >= 30f ? DebugColors.Warning : DebugColors.Error;

            drawList.AddLine(
                new Vector2(x1, y1),
                new Vector2(x2, y2),
                ImGui.GetColorU32(color),
                2f
            );
        }
    }

    // Border
    drawList.AddRect(p0, p1, ImGui.GetColorU32(DebugColors.TextDim), 0f, ImDrawCornerFlags.All, 1f);

    ImGui.Dummy(new Vector2(sparklineWidth, sparklineHeight));
}
```

### 3.2 Color-Coded Status Dot

Compact status indicator (6x6 pixels).

```csharp
public void DrawStatusDot(PerformanceHealth health)
{
    const float dotSize = 6f;
    var color = GetHealthColor(health);

    var p0 = ImGui.GetCursorScreenPos();
    var p1 = new Vector2(p0.X + dotSize, p0.Y + dotSize);

    ImGui.GetWindowDrawList().AddCircleFilled(
        new Vector2(p0.X + dotSize / 2, p0.Y + dotSize / 2),
        dotSize / 2,
        ImGui.GetColorU32(color)
    );

    ImGui.Dummy(new Vector2(dotSize, dotSize));

    if (ImGui.IsItemHovered())
    {
        ImGui.SetTooltip($"Performance: {health}");
    }
}
```

### 3.3 Progress Bar for Loading

Show ongoing operations (async data loading, etc.).

```csharp
public void DrawLoadingProgress(float progress, float width = 100f)
{
    // progress: 0.0 to 1.0
    var color = progress < 1.0f ? DebugColors.Blocking : DebugColors.Success;

    ImGui.PushStyleColor(ImGuiCol.PlotHistogram, color);
    ImGui.ProgressBar(progress, new Vector2(width, 12f), "");
    ImGui.PopStyleColor();

    ImGui.SameLine();
    ImGui.Text($"{progress * 100:F0}%");
}
```

### 3.4 Compact Memory Display

Show memory usage inline.

```csharp
public void DrawCompactMemory(long bytes)
{
    var mb = bytes / (1024.0 * 1024.0);
    var color = mb switch
    {
        < 100 => DebugColors.Success,
        < 200 => DebugColors.Warning,
        < 300 => DebugColors.Blocking,
        _ => DebugColors.Error
    };

    ImGui.TextColored(color, $"{mb:F1}MB");
}
```

---

## Part 4: Visual Feedback Systems

### 4.1 Panel Flash on New Data

Flash when critical updates occur.

```csharp
public class PanelFlashSystem
{
    private Dictionary<string, float> _flashTimers = new();
    private const float FlashDuration = 0.5f;

    public void OnDataChanged(string panelId)
    {
        _flashTimers[panelId] = 0f;
    }

    public void Update(float deltaTime)
    {
        foreach (var key in _flashTimers.Keys.ToList())
        {
            _flashTimers[key] += deltaTime;
            if (_flashTimers[key] > FlashDuration)
                _flashTimers.Remove(key);
        }
    }

    public void DrawPanelFlash(string panelId)
    {
        if (!_flashTimers.TryGetValue(panelId, out var elapsed))
            return;

        // Fade out: 100% to 0% over FlashDuration
        var alpha = (1f - (elapsed / FlashDuration)) * 0.3f;
        var flashColor = new Vector4(
            DebugColors.Highlight.X,
            DebugColors.Highlight.Y,
            DebugColors.Highlight.Z,
            alpha
        );

        var drawList = ImGui.GetWindowDrawList();
        var p0 = ImGui.GetWindowPos();
        var p1 = p0 + ImGui.GetWindowSize();

        drawList.AddRectFilled(p0, p1, ImGui.GetColorU32(flashColor));
    }
}
```

### 4.2 Pulsing Indicators

Used for active/recording states.

```csharp
public class PulsingIndicator
{
    private float _phase;
    private float _frequency = 3f; // Hz

    public void Update(float deltaTime)
    {
        _phase += deltaTime * _frequency * MathF.PI * 2f;
        if (_phase > MathF.PI * 2f)
            _phase -= MathF.PI * 2f;
    }

    public Vector4 GetPulsedColor(Vector4 baseColor)
    {
        // Pulse between 50% and 100% intensity
        var intensity = 0.5f + 0.5f * MathF.Sin(_phase);
        return new Vector4(
            baseColor.X * intensity,
            baseColor.Y * intensity,
            baseColor.Z * intensity,
            baseColor.W
        );
    }

    public string GetPulsingIndicator()
    {
        // Rotate through: ◑ → ◐ → ● → ◕ → ○ → ◔
        var index = (int)(_phase / (MathF.PI / 3f));
        return index switch
        {
            0 => "◐",
            1 => "◑",
            2 => "●",
            3 => "◕",
            4 => "○",
            _ => "◔"
        };
    }
}
```

### 4.3 Smooth Fade Transitions

Used when panels appear/hide.

```csharp
public class FadeTransition
{
    private float _alpha = 1f;
    private float _targetAlpha;
    private const float TransitionSpeed = 3f; // seconds

    public void SetTarget(float target)
    {
        _targetAlpha = Math.Clamp(target, 0f, 1f);
    }

    public void Update(float deltaTime)
    {
        if (Math.Abs(_alpha - _targetAlpha) < 0.01f)
        {
            _alpha = _targetAlpha;
            return;
        }

        var delta = (_targetAlpha - _alpha) * TransitionSpeed * deltaTime;
        _alpha = Math.Clamp(_alpha + delta, 0f, 1f);
    }

    public Vector4 ApplyAlpha(Vector4 color)
    {
        return new Vector4(color.X, color.Y, color.Z, color.W * _alpha);
    }

    public bool IsComplete => Math.Abs(_alpha - _targetAlpha) < 0.01f;
}
```

### 4.4 Hover Previews

Show additional context on hover.

```csharp
public void DrawWithHoverPreview(string label, string preview)
{
    ImGui.Text(label);

    if (ImGui.IsItemHovered())
    {
        ImGui.SetTooltip(preview);
    }
}

// Advanced: Custom tooltip window
public void DrawDetailedHoverPreview(string label, Action drawPreview)
{
    ImGui.Text(label);

    if (ImGui.IsItemHovered())
    {
        ImGui.BeginTooltip();
        drawPreview();
        ImGui.EndTooltip();
    }
}
```

---

## Part 5: Implementation Roadmap

### Phase 1: Core Icons (Week 1)
- [ ] Add icon constants to `DebugPanelHelpers`
- [ ] Update all panel DisplayNames to include icons
- [ ] Create `PanelIconRenderer` utility class
- [ ] Test Unicode rendering across platforms

### Phase 2: Badges & Indicators (Week 2)
- [ ] Implement error count badges in LogsPanel
- [ ] Add performance health indicator to PerformancePanel
- [ ] Create profiler recording indicator
- [ ] Implement entity count badge visualization

### Phase 3: Mini-Widgets (Week 3)
- [ ] Create `MiniWidgetRenderer` class
- [ ] Implement FPS sparkline
- [ ] Add status dot indicator
- [ ] Build memory usage compact display
- [ ] Create progress bar component

### Phase 4: Visual Feedback (Week 4)
- [ ] Implement PanelFlashSystem
- [ ] Add pulsing indicator system
- [ ] Create fade transition utilities
- [ ] Implement hover preview system
- [ ] Add visual polish effects

### Phase 5: Polish & Testing (Week 5)
- [ ] Performance profiling of visual effects
- [ ] Cross-platform Unicode testing
- [ ] Color accessibility review
- [ ] User testing & refinement

---

## Part 6: Integration Examples

### Example 1: Enhanced Logs Panel Header

```csharp
public void DrawLogsHeader()
{
    ImGui.Text("☰ Logs");

    ImGui.SameLine();

    // Error badge
    var (errorCount, warningCount) = CountLogLevels();
    if (errorCount > 0)
    {
        ImGui.TextColored(DebugColors.Error, $"[{errorCount}]");
        ImGui.SameLine();
    }

    // Warning badge
    if (warningCount > 0)
    {
        ImGui.TextColored(DebugColors.Warning, $"{{{warningCount}}}");
        ImGui.SameLine();
    }

    // Unread flash
    _unreadIndicator.DrawFlashIndicator();
}
```

### Example 2: Enhanced Performance Panel

```csharp
public void DrawPerformanceToolbar()
{
    // Status dot
    _healthIndicator.DrawStatusDot(GetPerformanceHealth());
    ImGui.SameLine();

    // FPS text with color
    var fpsColor = DebugPanelHelpers.GetFpsColor(_fps);
    ImGui.TextColored(fpsColor, $"⏱ {_fps:F0} FPS");

    ImGui.SameLine();

    // FPS Sparkline
    DrawFpsSparkline(_frameTimeHistory, FrameTimeHistorySize);

    ImGui.SameLine();

    // Memory
    DrawCompactMemory(_gcTotalMemory);
}
```

### Example 3: Enhanced Entity Inspector

```csharp
public void DrawEntityInspectorHeader()
{
    ImGui.Text("■ Entity Inspector");

    ImGui.SameLine();

    // Entity count badge
    DrawEntityCountBadge(_entityCount, MaxEntities);

    ImGui.SameLine();

    // Loading indicator if async operation
    if (_isLoading)
    {
        DrawLoadingProgress(_loadingProgress);
    }
}
```

---

## Part 7: Color Palette Reference

### Using Existing DebugColors

```csharp
// Status colors (recommend these for consistency)
DebugColors.Success       // Green - healthy/good
DebugColors.Warning       // Yellow - caution
DebugColors.Error         // Red - critical
DebugColors.Blocking      // Orange - active/recording
DebugColors.Info          // Blue - informational

// Text colors
DebugColors.TextPrimary   // Light gray - main text
DebugColors.TextSecondary // Medium gray - secondary
DebugColors.TextDim       // Dark gray - disabled
DebugColors.TextValue     // Cyan - highlighted values

// Background colors
DebugColors.BackgroundPrimary   // Dark - main bg
DebugColors.BackgroundSecondary // Darker - nested bg
DebugColors.BackgroundElevated  // Lighter - popups
```

---

## Part 8: Performance Considerations

### Optimization Checklist

- Use `ImGui.GetWindowDrawList()` for custom rendering (avoid per-frame allocations)
- Cache Unicode characters as string constants
- Update animations at fixed time steps (e.g., _phase calculation)
- Batch similar colored text renders
- Use `ImGui.IsItemHovered()` efficiently (only when needed)
- Profile sparkline calculations (consider decimation for large history)

### Memory Impact
- Mini-widgets: < 1KB per panel
- Pulsing indicators: < 100 bytes state
- Flash system: O(n) where n = number of visible panels
- Total estimated overhead: < 10KB for all visual systems

---

## Part 9: Accessibility Considerations

### Color-Blind Safe Palette

- Primary distinction: Hue (Red/Yellow/Green) + Symbol variety
- Secondary distinction: Saturation/Brightness levels
- Always use text labels alongside colors
- Use shape/symbol variety (●/○/◐/◑/◕/◔) for redundant encoding

### Unicode Fallbacks

For environments with limited Unicode support:

```csharp
public string GetIconFallback(string preferredIcon)
{
    return preferredIcon switch
    {
        "⏱" => "[⏱]",     // Timer fallback
        "⚡" => "[!]",     // Lightning fallback
        "☰" => "[≡]",     // List fallback
        "■" => "[#]",     // Box fallback
        "❖" => "[+]",     // Hierarchy fallback
        _ => "[?]"         // Unknown fallback
    };
}
```

---

## Part 10: Testing Checklist

### Visual Testing
- [ ] Icons render correctly on Windows/Linux/Mac
- [ ] Unicode characters display without artifacts
- [ ] Colors meet contrast requirements (WCAG AA)
- [ ] Animations run smoothly (no jank)
- [ ] Tooltips appear correctly positioned

### Functional Testing
- [ ] Badges update on data changes
- [ ] Pulses stop when conditions are met
- [ ] Flashes fade correctly
- [ ] Sparklines track historical data accurately
- [ ] All indicators respond to theme changes

### Performance Testing
- [ ] No GC allocations in Draw() methods
- [ ] 60 FPS maintained with all indicators active
- [ ] Memory usage < 10KB additional
- [ ] Sparkline calculation < 1ms per frame

---

## Conclusion

This design system provides a comprehensive visual enhancement framework for MonoBall's debug panels while maintaining:
- **Consistency**: All elements use existing color palette and ImGui patterns
- **Clarity**: Unicode icons and badges provide immediate visual feedback
- **Performance**: Efficient implementations suitable for real-time rendering
- **Accessibility**: Color-blind safe and Unicode-fallback compatible
- **Extensibility**: Each system can be independently enhanced or modified

Implementation should proceed incrementally, with each phase validated before proceeding to the next.
