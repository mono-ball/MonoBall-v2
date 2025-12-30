namespace MonoBall.Core.Diagnostics.Systems;

using System;
using System.Numerics;
using Arch.Core;
using Hexa.NET.ImGui;
using Panels;
using Services;

/// <summary>
/// System that renders all registered debug panels.
/// </summary>
public sealed class DebugPanelRenderSystem : DebugSystemBase
{
    private readonly IDebugPanelRegistry _registry;
    private readonly ImGuiLifecycleSystem _lifecycleSystem;
    private bool _showMainMenuBar = true;

    /// <summary>
    /// Gets or sets whether to show the main debug menu bar.
    /// </summary>
    public bool ShowMainMenuBar
    {
        get => _showMainMenuBar;
        set => _showMainMenuBar = value;
    }

    /// <summary>
    /// Initializes the debug panel render system.
    /// </summary>
    /// <param name="world">The ECS world.</param>
    /// <param name="registry">The panel registry.</param>
    /// <param name="lifecycleSystem">The ImGui lifecycle system.</param>
    /// <exception cref="ArgumentNullException">Thrown when registry or lifecycleSystem is null.</exception>
    public DebugPanelRenderSystem(
        World world,
        IDebugPanelRegistry registry,
        ImGuiLifecycleSystem lifecycleSystem
    )
        : base(world)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _lifecycleSystem =
            lifecycleSystem ?? throw new ArgumentNullException(nameof(lifecycleSystem));
    }

    /// <inheritdoc />
    public override void Update(in float deltaTime)
    {
        ThrowIfDisposed();

        if (!_lifecycleSystem.IsVisible)
            return;

        if (_registry is DebugPanelRegistry concreteRegistry)
        {
            concreteRegistry.Update(deltaTime);
        }

        if (_showMainMenuBar)
        {
            DrawMainMenuBar();
        }

        // Create a fullscreen dockspace for panels
        DrawDockSpace();

        DrawPanels(deltaTime);
    }

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
                        var isVisible = panel.IsVisible;
                        if (ImGui.MenuItem(panel.DisplayName, string.Empty, ref isVisible))
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

    private uint _dockspaceId;

    private void DrawDockSpace()
    {
        // Use DockSpaceOverViewport for seamless integration with menu bar
        var dockspaceFlags = ImGuiDockNodeFlags.PassthruCentralNode;
        _dockspaceId = ImGui.DockSpaceOverViewport(0, null, dockspaceFlags);
    }

    private void DrawPanels(float deltaTime)
    {
        foreach (var panel in _registry.Panels)
        {
            if (!panel.IsVisible)
                continue;

            // Set default window size on first use
            if (panel.DefaultSize.HasValue)
            {
                ImGui.SetNextWindowSize(panel.DefaultSize.Value, ImGuiCond.FirstUseEver);
            }

            // Dock new windows into the dockspace by default
            ImGui.SetNextWindowDockID(_dockspaceId, ImGuiCond.FirstUseEver);

            var isOpen = panel.IsVisible;
            if (ImGui.Begin(panel.DisplayName, ref isOpen))
            {
                panel.Draw(deltaTime);
            }
            ImGui.End();

            if (!isOpen)
            {
                panel.IsVisible = false;
            }
        }
    }
}
