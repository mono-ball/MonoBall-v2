namespace MonoBall.Core.Diagnostics.Services;

using Arch.Core;
using Console.Services;
using MonoBall.Core.Logging;
using MonoBall.Core.Mods;
using MonoBall.Core.Scenes.Systems;
using Panels;

/// <summary>
/// Factory for creating and configuring debug panels.
/// </summary>
public sealed class DebugPanelFactory
{
    private readonly World _world;
    private readonly IModManager? _modManager;
    private readonly SceneSystem? _sceneSystem;

    /// <summary>
    /// Gets the console service created during panel registration.
    /// </summary>
    public ConsoleService? ConsoleService { get; private set; }

    /// <summary>
    /// Gets the performance stats adapter created during panel registration.
    /// </summary>
    public PerformanceStatsAdapter? PerformanceStats { get; private set; }

    /// <summary>
    /// Gets the time control service created during panel registration.
    /// </summary>
    public TimeControlService? TimeControl { get; private set; }

    /// <summary>
    /// Initializes the panel factory.
    /// </summary>
    /// <param name="world">The ECS world.</param>
    /// <param name="sceneSystem">Optional scene system for time control.</param>
    /// <param name="modManager">Optional mod manager for mod panels.</param>
    public DebugPanelFactory(
        World world,
        SceneSystem? sceneSystem = null,
        IModManager? modManager = null
    )
    {
        _world = world;
        _sceneSystem = sceneSystem;
        _modManager = modManager;
    }

    /// <summary>
    /// Registers all default debug panels with the registry.
    /// </summary>
    /// <param name="registry">The panel registry.</param>
    public void RegisterDefaultPanels(IDebugPanelRegistry registry)
    {
        // Core diagnostic panels
        registry.Register(new PerformancePanel());
        registry.Register(new EntityInspectorPanel(_world));
        registry.Register(new SceneInspectorPanel(_world));
        registry.Register(new SystemProfilerPanel());
        registry.Register(new EventInspectorPanel());

        // Logs panel with Serilog integration
        var logsPanel = new LogsPanel();
        registry.Register(logsPanel);
        ImGuiLogSink.SetLogsPanel(logsPanel);

        // Console panel with services
        RegisterConsolePanel(registry);

        // Mod panels (if mod manager available)
        RegisterModPanels(registry);
    }

    private void RegisterConsolePanel(IDebugPanelRegistry registry)
    {
        PerformanceStats = new PerformanceStatsAdapter();

        if (_sceneSystem != null)
        {
            TimeControl = new TimeControlService(_sceneSystem);
        }

        ConsoleService = new ConsoleService
        {
            PerformanceStats = PerformanceStats,
            TimeControl = TimeControl,
        };

        registry.Register(new ConsolePanel(ConsoleService));
    }

    private void RegisterModPanels(IDebugPanelRegistry registry)
    {
        if (_modManager == null)
            return;

        registry.Register(new ModBrowserPanel(_modManager));
        registry.Register(new DefinitionBrowserPanel(_modManager));
    }
}
