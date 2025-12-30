namespace MonoBall.Core.Diagnostics.Panels;

using System;
using System.Diagnostics;
using System.Numerics;
using Hexa.NET.ImGui;
using MonoBall.Core.Diagnostics.UI;

/// <summary>
/// Debug panel showing performance metrics: FPS, frame time, memory usage.
/// </summary>
public sealed class PerformancePanel : IDebugPanel, IDebugPanelLifecycle
{
    private const int FrameTimeHistorySize = 120;
    private readonly float[] _frameTimeHistory = new float[FrameTimeHistorySize];
    private int _frameTimeIndex;

    private float _fps;
    private float _frameTime;
    private float _minFrameTime = float.MaxValue;
    private float _maxFrameTime;
    private float _avgFrameTime;
    private long _gcTotalMemory;
    private int _gc0Collections;
    private int _gc1Collections;
    private int _gc2Collections;

    private readonly Stopwatch _updateTimer = new();
    private float _refreshInterval = 0.5f;

    /// <inheritdoc />
    public string Id => "performance";

    /// <inheritdoc />
    public string DisplayName => "Performance";

    /// <inheritdoc />
    public bool IsVisible { get; set; } = true;

    /// <inheritdoc />
    public string Category => "Diagnostics";

    /// <inheritdoc />
    public int SortOrder => 0;

    /// <inheritdoc />
    public Vector2? DefaultSize => new Vector2(350, 350);

    /// <inheritdoc />
    public void Initialize()
    {
        _updateTimer.Start();
    }

    /// <inheritdoc />
    public void Update(float deltaTime)
    {
        // Always update frame time history for smooth graph
        _frameTimeHistory[_frameTimeIndex] = deltaTime * 1000f;
        _frameTimeIndex = (_frameTimeIndex + 1) % FrameTimeHistorySize;

        // Update stats periodically
        if (_updateTimer.Elapsed.TotalSeconds >= _refreshInterval)
        {
            _updateTimer.Restart();
            UpdateStats(deltaTime);
        }
    }

    /// <inheritdoc />
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

    private void DrawToolbar()
    {
        // FPS color indicator
        var fpsColor = _fps switch
        {
            >= 60 => DebugColors.Success,
            >= 30 => DebugColors.Warning,
            _ => DebugColors.Error,
        };
        ImGui.TextColored(fpsColor, $"{_fps:F0} FPS");

        ImGui.SameLine();
        ImGui.TextDisabled($"({_frameTime:F1} ms)");

        ImGui.SameLine();
        ImGui.SetNextItemWidth(80);
        ImGui.SliderFloat("##refresh", ref _refreshInterval, 0.1f, 2f, "%.1fs");
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Refresh interval");
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _updateTimer.Stop();
        GC.SuppressFinalize(this);
    }

    private void UpdateStats(float deltaTime)
    {
        _frameTime = deltaTime * 1000f;
        _fps = deltaTime > 0 ? 1f / deltaTime : 0f;

        // Calculate min/max/avg from history
        _minFrameTime = float.MaxValue;
        _maxFrameTime = 0f;
        var sum = 0f;
        for (var i = 0; i < FrameTimeHistorySize; i++)
        {
            var ft = _frameTimeHistory[i];
            if (ft > 0)
            {
                _minFrameTime = Math.Min(_minFrameTime, ft);
                _maxFrameTime = Math.Max(_maxFrameTime, ft);
                sum += ft;
            }
        }
        _avgFrameTime = sum / FrameTimeHistorySize;

        if (_minFrameTime == float.MaxValue)
            _minFrameTime = 0;

        // Memory stats
        _gcTotalMemory = GC.GetTotalMemory(false);
        _gc0Collections = GC.CollectionCount(0);
        _gc1Collections = GC.CollectionCount(1);
        _gc2Collections = GC.CollectionCount(2);
    }

    private void DrawFpsSection()
    {
        ImGui.Text("Frame Rate");
        ImGui.Indent();

        var fpsColor = _fps switch
        {
            >= 60 => DebugColors.Success,
            >= 30 => DebugColors.Warning,
            _ => DebugColors.Error,
        };

        ImGui.TextColored(fpsColor, $"{_fps:F1} FPS");
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

        // Row 1: Current, Avg
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

        // Row 2: Min/Max range
        ImGui.Text("Range:");
        ImGui.SameLine();
        ImGui.TextColored(DebugColors.TextValue, $"{_minFrameTime:F2} - {_maxFrameTime:F2} ms");

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

    private void DrawMemorySection()
    {
        ImGui.Text("Memory");
        ImGui.Indent();

        var memoryMb = _gcTotalMemory / (1024.0 * 1024.0);
        ImGui.Text("GC Heap:");
        ImGui.SameLine();
        ImGui.TextColored(DebugColors.TextValue, $"{memoryMb:F2} MB");

        ImGui.Text("Collections:");
        ImGui.SameLine();
        ImGui.TextColored(
            DebugColors.TextSecondary,
            $"Gen0={_gc0Collections}  Gen1={_gc1Collections}  Gen2={_gc2Collections}"
        );

        if (ImGui.Button("Force GC"))
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        ImGui.Unindent();
    }
}
