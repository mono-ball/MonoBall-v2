namespace MonoBall.Core.Diagnostics.UI;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Hexa.NET.ImGui;

/// <summary>
/// Reusable indicator systems for debug panels.
/// Provides performance-optimized visual feedback components.
/// </summary>
public static class DebugUIIndicators
{
    // ═══════════════════════════════════════════════════════════════════════════
    // Icon Constants
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>Unicode icon constants for all panel types.</summary>
    public static class Icons
    {
        public const string Performance = "⏱";
        public const string PerformanceChart = "📊";
        public const string Console = "⌘";
        public const string ConsoleActive = "⚡";
        public const string Logs = "☰";
        public const string LogsDocument = "📝";
        public const string EntityInspector = "■";
        public const string EntityComponent = "⎮";
        public const string SceneInspector = "❖";
        public const string SceneTree = "🌲";
        public const string EventInspector = "⚡";
        public const string EventSpectrum = "🌈";
        public const string Profiler = "⏱";
        public const string ProfilerBrightness = "🔆";
        public const string ModBrowser = "🧩";
        public const string ModNavigation = "➤";
        public const string DefinitionBrowser = "📑";
        public const string DefinitionClock = "⏲";

        /// <summary>Gets the icon for a panel ID.</summary>
        public static string GetIconForPanel(string panelId)
        {
            return panelId switch
            {
                "performance" => Performance,
                "console" => Console,
                "logs" => Logs,
                "entity_inspector" => EntityInspector,
                "scene_inspector" => SceneInspector,
                "event_inspector" => EventInspector,
                "profiler" => Profiler,
                "mod_browser" => ModBrowser,
                "definition_browser" => DefinitionBrowser,
                _ => "?",
            };
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Badge Rendering
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>Draws a numeric badge with color coding.</summary>
    /// <param name="count">The count to display.</param>
    /// <param name="color">The badge color.</param>
    /// <param name="format">The format style: "[", "{", "(" for error, warning, info.</param>
    public static void DrawBadge(int count, Vector4 color, char format = '[')
    {
        var displayText = count > 99 ? "99+" : count.ToString();
        var left = format switch
        {
            '{' => "{",
            '(' => "(",
            _ => "[",
        };
        var right = format switch
        {
            '{' => "}",
            '(' => ")",
            _ => "]",
        };

        ImGui.TextColored(color, $"{left}{displayText}{right}");
    }

    /// <summary>Draws a badge with optional pulsing animation for critical values.</summary>
    /// <param name="count">The count to display.</param>
    /// <param name="color">The badge color.</param>
    /// <param name="pulseThreshold">Count value above which to pulse animation.</param>
    public static void DrawBadgeWithPulse(int count, Vector4 color, int pulseThreshold = 10)
    {
        DrawBadge(count, color);

        if (count > pulseThreshold)
        {
            ImGui.SameLine();
            var pulseAlpha = (Math.Sin(Environment.TickCount64 / 500.0) + 1.0) * 0.5;
            var pulseColor = new Vector4(color.X, color.Y, color.Z, (float)pulseAlpha);
            ImGui.TextColored(pulseColor, "◆");
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Performance Health Indicators
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>Performance health levels.</summary>
    public enum PerformanceHealth
    {
        Excellent, // 60+ FPS
        Good, // 45-60 FPS
        Fair, // 30-45 FPS
        Warning, // 20-30 FPS
        Critical, // < 20 FPS
    }

    /// <summary>Gets the performance health level based on FPS.</summary>
    public static PerformanceHealth GetHealthFromFps(float fps)
    {
        return fps switch
        {
            >= 60f => PerformanceHealth.Excellent,
            >= 45f => PerformanceHealth.Good,
            >= 30f => PerformanceHealth.Fair,
            >= 20f => PerformanceHealth.Warning,
            _ => PerformanceHealth.Critical,
        };
    }

    /// <summary>Gets the color for a performance health level.</summary>
    public static Vector4 GetHealthColor(PerformanceHealth health)
    {
        return health switch
        {
            PerformanceHealth.Excellent => DebugColors.Success,
            PerformanceHealth.Good => new Vector4(0.7f, 1f, 0.4f, 1f), // Light green
            PerformanceHealth.Fair => DebugColors.Warning,
            PerformanceHealth.Warning => DebugColors.Blocking,
            PerformanceHealth.Critical => DebugColors.Error,
            _ => DebugColors.TextSecondary,
        };
    }

    /// <summary>Gets the visual indicator character for a health level.</summary>
    public static char GetHealthIndicator(PerformanceHealth health)
    {
        return health switch
        {
            PerformanceHealth.Excellent => '●', // Solid circle
            PerformanceHealth.Good => '◐', // 3/4 filled
            PerformanceHealth.Fair => '◑', // Half filled
            PerformanceHealth.Warning => '◕', // 1/4 filled
            PerformanceHealth.Critical => '○', // Empty circle
            _ => '?',
        };
    }

    /// <summary>Draws a performance health indicator (character + optional FPS).</summary>
    public static void DrawHealthIndicator(PerformanceHealth health, float fps = -1f)
    {
        var color = GetHealthColor(health);
        var indicator = GetHealthIndicator(health);

        var text = fps >= 0f ? $"{indicator} {fps:F1} FPS" : indicator.ToString();
        ImGui.TextColored(color, text);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Recording/Active Indicators
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>Drawing state for recording indicators.</summary>
    public class RecordingIndicator
    {
        private float _pulsePhase;
        private bool _isRecording;
        private const float PulseFrequency = 3f; // Hz

        /// <summary>Gets or sets whether recording is active.</summary>
        public bool IsRecording
        {
            get => _isRecording;
            set => _isRecording = value;
        }

        /// <summary>Updates the pulse animation (call once per frame).</summary>
        public void Update(float deltaTime)
        {
            if (!_isRecording)
                return;

            _pulsePhase += deltaTime * PulseFrequency * MathF.PI * 2f;
            if (_pulsePhase > MathF.PI * 2f)
                _pulsePhase -= MathF.PI * 2f;
        }

        /// <summary>Gets the pulsed color for the current frame.</summary>
        public Vector4 GetPulsedColor(Vector4 baseColor)
        {
            if (!_isRecording)
                return baseColor;

            var intensity = 0.5f + 0.5f * MathF.Sin(_pulsePhase);
            return new Vector4(
                baseColor.X * intensity,
                baseColor.Y * intensity,
                baseColor.Z * intensity,
                baseColor.W
            );
        }

        /// <summary>Draws the recording indicator.</summary>
        public void Draw()
        {
            if (!_isRecording)
                return;

            var color = GetPulsedColor(DebugColors.Error);
            ImGui.TextColored(color, "● Recording");
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Entity Count Badges
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>Draws an entity count badge with health coloring.</summary>
    public static void DrawEntityCountBadge(int count, int maxCapacity = 10000)
    {
        var healthPercent = (float)count / maxCapacity;
        var color = healthPercent switch
        {
            <= 0.5f => DebugColors.Success, // Green
            <= 0.75f => DebugColors.Warning, // Yellow
            <= 0.9f => DebugColors.Blocking, // Orange
            _ => DebugColors.Error, // Red
        };

        var countStr = $"{count:N0}";
        ImGui.TextColored(color, $"[{countStr}]");

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip($"{count:N0} / {maxCapacity:N0} entities ({healthPercent * 100:F1}%)");
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Mini-Widgets
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>Draws a compact memory usage display.</summary>
    public static void DrawCompactMemory(long bytes)
    {
        var mb = bytes / (1024.0 * 1024.0);
        var color = mb switch
        {
            < 100 => DebugColors.Success,
            < 200 => DebugColors.Warning,
            < 300 => DebugColors.Blocking,
            _ => DebugColors.Error,
        };

        ImGui.TextColored(color, $"{mb:F1}MB");
    }

    /// <summary>Draws a compact status dot (colored circle, ~6x6 pixels).</summary>
    public static void DrawStatusDot(PerformanceHealth health)
    {
        const float dotSize = 6f;
        var color = GetHealthColor(health);

        var p0 = ImGui.GetCursorScreenPos();
        ImGui
            .GetWindowDrawList()
            .AddCircleFilled(
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

    /// <summary>Draws a progress bar for loading operations.</summary>
    public static void DrawLoadingProgress(float progress, float width = 100f)
    {
        progress = Math.Clamp(progress, 0f, 1f);
        var color = progress < 1.0f ? DebugColors.Blocking : DebugColors.Success;

        ImGui.PushStyleColor(ImGuiCol.PlotHistogram, color);
        ImGui.ProgressBar(progress, new Vector2(width, 12f), "");
        ImGui.PopStyleColor();

        ImGui.SameLine();
        ImGui.Text($"{progress * 100:F0}%");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Sparkline Graphs
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>Draws a mini sparkline graph for FPS history.</summary>
    public static void DrawFpsSparkline(
        float[] fpsHistory,
        int count,
        float targetFps = 60f,
        float sparklineWidth = 120f,
        float sparklineHeight = 20f
    )
    {
        // Calculate bounds
        float minFps = float.MaxValue,
            maxFps = 0f;
        for (int i = 0; i < count; i++)
        {
            var fps = fpsHistory[i];
            if (fps > 0)
            {
                minFps = Math.Min(minFps, fps);
                maxFps = Math.Max(maxFps, fps);
            }
        }

        if (minFps == float.MaxValue)
            minFps = 0f;
        maxFps = Math.Max(maxFps, targetFps);

        var drawList = ImGui.GetWindowDrawList();
        var p0 = ImGui.GetCursorScreenPos();
        var p1 = new Vector2(p0.X + sparklineWidth, p0.Y + sparklineHeight);

        // Background
        drawList.AddRectFilled(p0, p1, ImGui.GetColorU32(DebugColors.BackgroundSecondary));

        // Target line (60 FPS reference)
        if (maxFps > minFps)
        {
            var targetY =
                p0.Y
                + sparklineHeight
                - (sparklineHeight * (targetFps - minFps) / (maxFps - minFps + 0.1f));
            drawList.AddLine(
                new Vector2(p0.X, targetY),
                new Vector2(p1.X, targetY),
                ImGui.GetColorU32(DebugColors.TextDim),
                1f
            );
        }

        // Plot line
        for (int i = 0; i < count - 1; i++)
        {
            var x1 = p0.X + (float)i / (count - 1) * sparklineWidth;
            var x2 = p0.X + (float)(i + 1) / (count - 1) * sparklineWidth;

            var fps1 = fpsHistory[i];
            var fps2 = fpsHistory[i + 1];

            if (fps1 > 0 && fps2 > 0 && maxFps > minFps)
            {
                var y1 =
                    p0.Y
                    + sparklineHeight
                    - (sparklineHeight * (fps1 - minFps) / (maxFps - minFps + 0.1f));
                var y2 =
                    p0.Y
                    + sparklineHeight
                    - (sparklineHeight * (fps2 - minFps) / (maxFps - minFps + 0.1f));

                var color =
                    fps1 >= targetFps ? DebugColors.Success
                    : fps1 >= 30f ? DebugColors.Warning
                    : DebugColors.Error;

                drawList.AddLine(
                    new Vector2(x1, y1),
                    new Vector2(x2, y2),
                    ImGui.GetColorU32(color),
                    2f
                );
            }
        }

        // Border
        drawList.AddRect(
            p0,
            p1,
            ImGui.GetColorU32(DebugColors.TextDim),
            0f,
            ImDrawFlags.RoundCornersAll,
            1f
        );

        ImGui.Dummy(new Vector2(sparklineWidth, sparklineHeight));
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Flash Systems
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>Manages flash overlay effects for panels.</summary>
    public class FlashSystem
    {
        private readonly Dictionary<string, float> _flashTimers = new();
        private const float FlashDuration = 0.5f;

        /// <summary>Triggers a flash on a panel.</summary>
        public void Flash(string panelId)
        {
            _flashTimers[panelId] = 0f;
        }

        /// <summary>Updates all active flashes (call once per frame).</summary>
        public void Update(float deltaTime)
        {
            foreach (var key in _flashTimers.Keys.ToList())
            {
                _flashTimers[key] += deltaTime;
                if (_flashTimers[key] > FlashDuration)
                    _flashTimers.Remove(key);
            }
        }

        /// <summary>Draws the flash overlay for a panel window.</summary>
        public void DrawFlashOverlay(string panelId)
        {
            if (!_flashTimers.TryGetValue(panelId, out var elapsed))
                return;

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

    // ═══════════════════════════════════════════════════════════════════════════
    // Pulsing Indicators
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>Manages pulsing animation state.</summary>
    public class PulsingIndicator
    {
        private float _phase;
        private float _frequency = 3f; // Hz

        /// <summary>Gets or sets the pulse frequency in Hz.</summary>
        public float Frequency
        {
            get => _frequency;
            set => _frequency = value;
        }

        /// <summary>Updates the pulse animation (call once per frame).</summary>
        public void Update(float deltaTime)
        {
            _phase += deltaTime * _frequency * MathF.PI * 2f;
            if (_phase > MathF.PI * 2f)
                _phase -= MathF.PI * 2f;
        }

        /// <summary>Gets the pulsed color intensity for the current phase.</summary>
        public Vector4 GetPulsedColor(Vector4 baseColor)
        {
            var intensity = 0.5f + 0.5f * MathF.Sin(_phase);
            return new Vector4(
                baseColor.X * intensity,
                baseColor.Y * intensity,
                baseColor.Z * intensity,
                baseColor.W
            );
        }

        /// <summary>Gets the current phase (0 to 2π).</summary>
        public float Phase => _phase;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Unread Indicator
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>Tracks unread messages with visual feedback.</summary>
    public class UnreadIndicator
    {
        private int _unreadCount;
        private float _lastFlashTime;
        private const float FlashDuration = 0.3f;

        /// <summary>Gets the current unread count.</summary>
        public int UnreadCount => _unreadCount;

        /// <summary>Marks a new unread item.</summary>
        public void OnNewItem()
        {
            _unreadCount++;
            _lastFlashTime = 0f;
        }

        /// <summary>Clears all unread items.</summary>
        public void Clear()
        {
            _unreadCount = 0;
        }

        /// <summary>Updates the flash animation (call once per frame).</summary>
        public void Update(float deltaTime)
        {
            _lastFlashTime += deltaTime;
        }

        /// <summary>Draws a flash overlay when there are unread items.</summary>
        public void DrawFlashOverlay()
        {
            if (_unreadCount == 0)
                return;

            var alpha = Math.Max(0f, 1f - (_lastFlashTime / FlashDuration));

            var flashColor = new Vector4(
                DebugColors.Warning.X,
                DebugColors.Warning.Y,
                DebugColors.Warning.Z,
                alpha * 0.5f
            );

            var drawList = ImGui.GetWindowDrawList();
            var p0 = ImGui.GetWindowPos();
            var p1 = p0 + ImGui.GetWindowSize();

            drawList.AddRectFilled(p0, p1, ImGui.GetColorU32(flashColor));

            if (alpha <= 0.01f)
            {
                _unreadCount = 0;
            }
        }
    }
}
