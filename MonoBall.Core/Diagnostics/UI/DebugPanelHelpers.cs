namespace MonoBall.Core.Diagnostics.UI;

using System.Numerics;
using Hexa.NET.ImGui;

/// <summary>
/// Common UI patterns and helpers for debug panels.
/// Ensures consistent behavior and appearance across all panels.
/// </summary>
public static class DebugPanelHelpers
{
    // ═══════════════════════════════════════════════════════════════════════════
    // Standard Widths
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>Standard width for refresh interval sliders.</summary>
    public const float RefreshSliderWidth = 80f;

    /// <summary>Standard width for filter input fields.</summary>
    public const float FilterInputWidth = 150f;

    /// <summary>Standard width for combo boxes.</summary>
    public const float ComboBoxWidth = 120f;

    /// <summary>Standard width for property labels.</summary>
    public const float PropertyLabelWidth = 150f;

    // ═══════════════════════════════════════════════════════════════════════════
    // Standard Hints
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>Standard filter hint text.</summary>
    public const string FilterHint = "Filter...";

    /// <summary>Standard search hint text.</summary>
    public const string SearchHint = "Search...";

    // ═══════════════════════════════════════════════════════════════════════════
    // Default Values
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>Default refresh interval in seconds.</summary>
    public const float DefaultRefreshInterval = 0.5f;

    /// <summary>Minimum refresh interval in seconds.</summary>
    public const float MinRefreshInterval = 0.1f;

    /// <summary>Maximum refresh interval in seconds.</summary>
    public const float MaxRefreshInterval = 2f;

    // ═══════════════════════════════════════════════════════════════════════════
    // Timing Thresholds
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>Threshold for fast timing (ms).</summary>
    public const double TimingFastThreshold = 0.5;

    /// <summary>Threshold for medium timing (ms).</summary>
    public const double TimingMediumThreshold = 2.0;

    /// <summary>Target frame time at 60 FPS (ms).</summary>
    public const float TargetFrameTimeMs = 16.67f;

    // ═══════════════════════════════════════════════════════════════════════════
    // Helper Methods
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Draws a standard refresh interval slider.
    /// </summary>
    /// <param name="interval">The refresh interval value.</param>
    /// <param name="idSuffix">Optional ID suffix to ensure unique ImGui IDs across panels.</param>
    /// <returns>True if the value changed.</returns>
    public static bool DrawRefreshSlider(ref float interval, string? idSuffix = null)
    {
        ImGui.SetNextItemWidth(RefreshSliderWidth);
        var id = string.IsNullOrEmpty(idSuffix) ? "##refresh" : $"##refresh_{idSuffix}";
        return ImGui.SliderFloat(id, ref interval, MinRefreshInterval, MaxRefreshInterval, "%.1fs");
    }

    /// <summary>
    /// Draws a standard filter input field.
    /// </summary>
    /// <param name="filter">The filter string.</param>
    /// <param name="hint">Optional custom hint text.</param>
    /// <param name="idSuffix">Optional ID suffix to ensure unique ImGui IDs across panels.</param>
    /// <returns>True if the value changed.</returns>
    public static bool DrawFilterInput(
        ref string filter,
        string? hint = null,
        string? idSuffix = null
    )
    {
        ImGui.SetNextItemWidth(FilterInputWidth);
        var id = string.IsNullOrEmpty(idSuffix) ? "##filter" : $"##filter_{idSuffix}";
        return ImGui.InputTextWithHint(id, hint ?? FilterHint, ref filter, 256);
    }

    /// <summary>
    /// Gets the appropriate color for a timing value in milliseconds.
    /// </summary>
    /// <param name="ms">The timing in milliseconds.</param>
    /// <returns>The appropriate color vector.</returns>
    public static Vector4 GetTimingColor(double ms)
    {
        return ms switch
        {
            < TimingFastThreshold => DebugColors.TimingFast,
            < TimingMediumThreshold => DebugColors.TimingMedium,
            _ => DebugColors.TimingSlow,
        };
    }

    /// <summary>
    /// Gets the appropriate color for an FPS value.
    /// </summary>
    /// <param name="fps">The frames per second.</param>
    /// <returns>The appropriate color vector.</returns>
    public static Vector4 GetFpsColor(float fps)
    {
        return fps switch
        {
            >= 60f => DebugColors.Success,
            >= 30f => DebugColors.Warning,
            _ => DebugColors.Error,
        };
    }

    /// <summary>
    /// Gets the appropriate color for a frame budget percentage.
    /// </summary>
    /// <param name="budgetPercent">The percentage of frame budget used.</param>
    /// <returns>The appropriate color vector.</returns>
    public static Vector4 GetBudgetColor(double budgetPercent)
    {
        return budgetPercent switch
        {
            <= 80 => DebugColors.Success,
            <= 100 => DebugColors.Warning,
            _ => DebugColors.Error,
        };
    }

    /// <summary>
    /// Draws a labeled property row with consistent formatting.
    /// </summary>
    /// <param name="label">The property label.</param>
    /// <param name="value">The property value.</param>
    /// <param name="color">Optional color for the value text.</param>
    public static void DrawPropertyRow(string label, string value, Vector4? color = null)
    {
        ImGui.Text($"{label}:");
        ImGui.SameLine(PropertyLabelWidth);
        ImGui.TextColored(color ?? DebugColors.TextValue, value);
    }

    /// <summary>
    /// Draws a labeled property row with a boolean value.
    /// </summary>
    /// <param name="label">The property label.</param>
    /// <param name="value">The boolean value.</param>
    public static void DrawPropertyRow(string label, bool value)
    {
        var color = value ? DebugColors.Active : DebugColors.Inactive;
        DrawPropertyRow(label, value.ToString(), color);
    }

    /// <summary>
    /// Draws a labeled property row with a timing value.
    /// </summary>
    /// <param name="label">The property label.</param>
    /// <param name="ms">The timing in milliseconds.</param>
    /// <param name="format">The format string for the timing.</param>
    public static void DrawPropertyRowTiming(string label, double ms, string format = "F3")
    {
        var color = GetTimingColor(ms);
        DrawPropertyRow(label, $"{ms.ToString(format)} ms", color);
    }

    /// <summary>
    /// Draws entity count with appropriate coloring.
    /// </summary>
    /// <param name="count">The entity count.</param>
    public static void DrawEntityCount(int count)
    {
        var color = count > 0 ? DebugColors.Success : DebugColors.Inactive;
        ImGui.TextColored(color, count.ToString());
    }

    /// <summary>
    /// Draws disabled/placeholder text.
    /// </summary>
    /// <param name="text">The text to display.</param>
    public static void DrawDisabledText(string text)
    {
        ImGui.TextColored(DebugColors.TextDim, text);
    }

    /// <summary>
    /// Draws an error message.
    /// </summary>
    /// <param name="message">The error message.</param>
    public static void DrawErrorText(string message)
    {
        ImGui.TextColored(DebugColors.Error, message);
    }

    /// <summary>
    /// Draws a warning message.
    /// </summary>
    /// <param name="message">The warning message.</param>
    public static void DrawWarningText(string message)
    {
        ImGui.TextColored(DebugColors.Warning, message);
    }

    /// <summary>
    /// Draws an info message.
    /// </summary>
    /// <param name="message">The info message.</param>
    public static void DrawInfoText(string message)
    {
        ImGui.TextColored(DebugColors.Info, message);
    }

    /// <summary>
    /// Updates a refresh timer and returns true when refresh is needed.
    /// </summary>
    /// <param name="timeSinceRefresh">Time accumulated since last refresh.</param>
    /// <param name="refreshInterval">The refresh interval threshold.</param>
    /// <param name="deltaTime">Delta time to add.</param>
    /// <returns>True if refresh is needed (timer reset automatically).</returns>
    public static bool UpdateRefreshTimer(
        ref float timeSinceRefresh,
        float refreshInterval,
        float deltaTime
    )
    {
        timeSinceRefresh += deltaTime;
        if (timeSinceRefresh >= refreshInterval)
        {
            timeSinceRefresh = 0;
            return true;
        }
        return false;
    }

    /// <summary>
    /// Standard table flags for debug panels.
    /// </summary>
    public static ImGuiTableFlags StandardTableFlags =>
        ImGuiTableFlags.Borders
        | ImGuiTableFlags.RowBg
        | ImGuiTableFlags.Resizable
        | ImGuiTableFlags.ScrollY
        | ImGuiTableFlags.SizingFixedFit;

    /// <summary>
    /// Standard table flags with sorting support.
    /// </summary>
    public static ImGuiTableFlags SortableTableFlags =>
        StandardTableFlags | ImGuiTableFlags.Sortable;

    /// <summary>
    /// Standard child panel flags for resizable panels.
    /// </summary>
    public static ImGuiChildFlags ResizableChildFlags =>
        ImGuiChildFlags.Borders | ImGuiChildFlags.ResizeX;

    /// <summary>
    /// Standard child panel flags without resize.
    /// </summary>
    public static ImGuiChildFlags StandardChildFlags => ImGuiChildFlags.Borders;
}
