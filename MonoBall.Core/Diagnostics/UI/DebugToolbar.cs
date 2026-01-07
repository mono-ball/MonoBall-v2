namespace MonoBall.Core.Diagnostics.UI;

using System;
using System.Collections.Generic;
using System.Numerics;
using Hexa.NET.ImGui;
using MonoBall.Core.Diagnostics.Panels;
using MonoBall.Core.Diagnostics.Services;

/// <summary>
/// Quick-access toolbar for debug panels with status indicators.
/// Provides icon buttons, FPS display, and error badges.
/// </summary>
public sealed class DebugToolbar
{
    private const float ToolbarHeight = 28f;
    private const float ButtonSpacing = 2f;
    private const float SectionSpacing = 12f;
    private const float FpsIndicatorWidth = 60f;
    private const float BadgeWidth = 40f;

    private readonly IDebugPanelRegistry _registry;
    private readonly List<ToolbarItem> _toolbarItems = new();

    private float _fps;
    private int _errorCount;
    private int _warningCount;

    // Cached reference to avoid repeated lookups
    private ILogCountProvider? _logCountProvider;
    private IDebugPanel? _logsPanel;

    /// <summary>
    /// Initializes the debug toolbar.
    /// </summary>
    /// <param name="registry">The panel registry.</param>
    /// <exception cref="ArgumentNullException">Thrown when registry is null.</exception>
    public DebugToolbar(IDebugPanelRegistry registry)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        BuildToolbarItems();
    }

    /// <summary>
    /// Updates toolbar metrics.
    /// </summary>
    /// <param name="deltaTime">Frame delta time in seconds.</param>
    public void Update(float deltaTime)
    {
        _fps = deltaTime > 0 ? 1f / deltaTime : 0f;

        // Lazy lookup and cache the log count provider
        _logCountProvider ??= _registry.GetPanel("logs") as ILogCountProvider;

        if (_logCountProvider != null)
        {
            _errorCount = _logCountProvider.ErrorCount;
            _warningCount = _logCountProvider.WarningCount;
        }

        // Cache logs panel for click handling
        _logsPanel ??= _registry.GetPanel("logs");
    }

    /// <summary>
    /// Draws the toolbar in the main menu bar area.
    /// </summary>
    public void DrawInMenuBar()
    {
        var availableWidth = ImGui.GetContentRegionAvail().X;
        var toolbarWidth = CalculateToolbarWidth();
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + availableWidth - toolbarWidth);

        DrawStatusSection();
        ImGui.SameLine(0, SectionSpacing);
        DrawPanelButtons();
    }

    /// <summary>
    /// Draws the toolbar as a standalone window.
    /// </summary>
    /// <param name="position">Position for the toolbar window.</param>
    public void DrawAsWindow(Vector2 position)
    {
        ImGui.SetNextWindowPos(position, ImGuiCond.Always);
        ImGui.SetNextWindowSize(new Vector2(0, ToolbarHeight));

        var flags =
            ImGuiWindowFlags.NoTitleBar
            | ImGuiWindowFlags.NoResize
            | ImGuiWindowFlags.NoMove
            | ImGuiWindowFlags.NoScrollbar
            | ImGuiWindowFlags.NoSavedSettings
            | ImGuiWindowFlags.AlwaysAutoResize;

        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(4, 4));
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(ButtonSpacing, 0));

        if (ImGui.Begin("##DebugToolbar", flags))
        {
            DrawPanelButtons();
            ImGui.SameLine(0, SectionSpacing);
            DrawStatusSection();
        }
        ImGui.End();

        ImGui.PopStyleVar(2);
    }

    private void DrawPanelButtons()
    {
        var framePadding = ImGui.GetStyle().FramePadding;
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(framePadding.X, 2));

        foreach (var item in _toolbarItems)
        {
            var panel = _registry.GetPanel(item.PanelId);
            if (panel == null)
                continue;

            var isActive = panel.IsVisible;
            PushButtonStyle(isActive);

            ImGui.PushID(item.PanelId);

            if (ImGui.SmallButton(item.Icon))
            {
                panel.IsVisible = !panel.IsVisible;
            }

            ImGui.PopID();
            ImGui.PopStyleColor(2);

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(item.Tooltip);
            }

            ImGui.SameLine(0, ButtonSpacing);
        }

        ImGui.PopStyleVar();
    }

    private static void PushButtonStyle(bool isActive)
    {
        if (isActive)
        {
            ImGui.PushStyleColor(ImGuiCol.Button, DebugColors.Active);
            ImGui.PushStyleColor(ImGuiCol.Text, DebugColors.TextPrimary);
        }
        else
        {
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0, 0, 0, 0));
            ImGui.PushStyleColor(ImGuiCol.Text, DebugColors.TextDim);
        }
    }

    private void DrawStatusSection()
    {
        DrawFpsIndicator();
        ImGui.SameLine(0, SectionSpacing);
        DrawErrorBadge();
        DrawWarningBadge();
    }

    private void DrawFpsIndicator()
    {
        var fpsColor = _fps switch
        {
            >= 60f => DebugColors.Success,
            >= 30f => DebugColors.Warning,
            _ => DebugColors.Error,
        };

        ImGui.TextColored(fpsColor, $"{_fps:F0}");
        ImGui.SameLine(0, 2);
        ImGui.TextColored(DebugColors.TextDim, "FPS");
    }

    private void DrawErrorBadge()
    {
        if (_errorCount <= 0)
            return;

        ImGui.TextColored(DebugColors.Error, $"{NerdFontIcons.ErrorCircle} {_errorCount}");

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip($"{_errorCount} errors");
        }

        if (ImGui.IsItemClicked())
        {
            ShowLogsPanel();
        }

        ImGui.SameLine(0, 4);
    }

    private void DrawWarningBadge()
    {
        if (_warningCount <= 0)
            return;

        ImGui.TextColored(DebugColors.Warning, $"{NerdFontIcons.Warning} {_warningCount}");

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip($"{_warningCount} warnings");
        }

        if (ImGui.IsItemClicked())
        {
            ShowLogsPanel();
        }
    }

    private void ShowLogsPanel()
    {
        if (_logsPanel != null)
        {
            _logsPanel.IsVisible = true;
        }
    }

    private float CalculateToolbarWidth()
    {
        var width = 0f;

        // Panel buttons (estimate based on SmallButton sizing)
        width += _toolbarItems.Count * 20f;
        width += _toolbarItems.Count * ButtonSpacing;
        width += SectionSpacing;

        // FPS indicator
        width += FpsIndicatorWidth;
        width += SectionSpacing;

        // Error/warning badges
        if (_errorCount > 0)
            width += BadgeWidth;
        if (_warningCount > 0)
            width += BadgeWidth;

        return width;
    }

    private void BuildToolbarItems()
    {
        _toolbarItems.Clear();

        // Panel quick-access definitions
        _toolbarItems.Add(new ToolbarItem("performance", NerdFontIcons.Performance, "Performance"));
        _toolbarItems.Add(new ToolbarItem("console", NerdFontIcons.Console, "Console"));
        _toolbarItems.Add(
            new ToolbarItem("entity-inspector", NerdFontIcons.Entity, "Entity Inspector")
        );
        _toolbarItems.Add(new ToolbarItem("logs", NerdFontIcons.Log, "Logs"));
        _toolbarItems.Add(
            new ToolbarItem("system-profiler", NerdFontIcons.Timer, "System Profiler")
        );
        _toolbarItems.Add(
            new ToolbarItem("event-inspector", NerdFontIcons.Bolt, "Event Inspector")
        );
    }

    /// <summary>
    /// Immutable toolbar item definition.
    /// </summary>
    private readonly struct ToolbarItem
    {
        public readonly string PanelId;
        public readonly string Icon;
        public readonly string Tooltip;

        public ToolbarItem(string panelId, string icon, string tooltip)
        {
            PanelId = panelId;
            Icon = icon;
            Tooltip = tooltip;
        }
    }
}
