namespace MonoBall.Core.Diagnostics.Services;

using System;
using Arch.Core;
using Events;
using ImGui;
using Microsoft.Xna.Framework;
using MonoBall.Core.ECS;
using MonoBall.Core.Logging;
using Panels;
using Systems;

/// <summary>
/// Facade service for managing the debug overlay system.
/// Provides a simple API for initializing and using the debug system.
/// </summary>
public sealed class DebugOverlayService : IDebugOverlayService
{
    private readonly World _world;
    private IImGuiRenderer? _renderer;
    private ImGuiLifecycleSystem? _lifecycleSystem;
    private ImGuiInputBridgeSystem? _inputBridgeSystem;
    private DebugPanelRenderSystem? _panelRenderSystem;
    private DebugPanelRegistry? _panelRegistry;
    private bool _initialized;
    private bool _disposed;

    /// <summary>
    /// Gets whether the debug overlay has been initialized.
    /// </summary>
    public bool IsInitialized => _initialized;

    /// <summary>
    /// Gets whether the debug overlay is currently visible.
    /// </summary>
    public bool IsVisible => _lifecycleSystem?.IsVisible ?? false;

    /// <summary>
    /// Gets whether ImGui wants to capture keyboard input.
    /// </summary>
    public bool WantsCaptureKeyboard => _inputBridgeSystem?.WantsCaptureKeyboard ?? false;

    /// <summary>
    /// Gets whether ImGui wants to capture mouse input.
    /// </summary>
    public bool WantsCaptureMouse => _inputBridgeSystem?.WantsCaptureMouse ?? false;

    /// <summary>
    /// Gets the panel registry for registering custom panels.
    /// </summary>
    public IDebugPanelRegistry? PanelRegistry => _panelRegistry;

    /// <summary>
    /// Initializes a new debug overlay service.
    /// </summary>
    /// <param name="world">The ECS world.</param>
    /// <exception cref="ArgumentNullException">Thrown when world is null.</exception>
    public DebugOverlayService(World world)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
    }

    /// <summary>
    /// Initializes the debug overlay system.
    /// Call this after the game has been initialized.
    /// </summary>
    /// <param name="game">The MonoGame Game instance.</param>
    /// <exception cref="ArgumentNullException">Thrown when game is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when already initialized.</exception>
    public void Initialize(Game game)
    {
        if (game == null)
            throw new ArgumentNullException(nameof(game));

        if (_initialized)
            throw new InvalidOperationException("Debug overlay is already initialized.");

        _renderer = new MonoGameImGuiRenderer();
        _renderer.Initialize(game);

        _panelRegistry = new DebugPanelRegistry();
        _lifecycleSystem = new ImGuiLifecycleSystem(_world, _renderer);
        _inputBridgeSystem = new ImGuiInputBridgeSystem(_world, _lifecycleSystem);
        _panelRenderSystem = new DebugPanelRenderSystem(_world, _panelRegistry, _lifecycleSystem);

        _inputBridgeSystem.HookTextInput(game.Window);

        RegisterDefaultPanels();

        _initialized = true;
    }

    /// <summary>
    /// Registers a debug panel.
    /// </summary>
    /// <param name="panel">The panel to register.</param>
    /// <exception cref="InvalidOperationException">Thrown when not initialized.</exception>
    public void RegisterPanel(IDebugPanel panel)
    {
        ThrowIfNotInitialized();
        _panelRegistry!.Register(panel);
    }

    /// <summary>
    /// Toggles the visibility of the debug overlay.
    /// </summary>
    public void Toggle()
    {
        var evt = new DebugToggleEvent { Show = null };
        EventBus.Send(ref evt);
    }

    /// <summary>
    /// Shows the debug overlay.
    /// </summary>
    public void Show()
    {
        var evt = new DebugToggleEvent { Show = true };
        EventBus.Send(ref evt);
    }

    /// <summary>
    /// Hides the debug overlay.
    /// </summary>
    public void Hide()
    {
        var evt = new DebugToggleEvent { Show = false };
        EventBus.Send(ref evt);
    }

    /// <summary>
    /// Updates the debug overlay.
    /// Call this at the start of your Update method.
    /// </summary>
    /// <param name="gameTime">The game time.</param>
    public void BeginUpdate(GameTime gameTime)
    {
        if (!_initialized || _disposed)
            return;

        var deltaTime = (float)gameTime.ElapsedGameTime.TotalSeconds;
        _lifecycleSystem!.BeginFrame(deltaTime);
    }

    /// <summary>
    /// Renders debug panels and ends the ImGui frame.
    /// Call this at the end of your Update method.
    /// </summary>
    /// <param name="gameTime">The game time.</param>
    public void EndUpdate(GameTime gameTime)
    {
        if (!_initialized || _disposed)
            return;

        var deltaTime = (float)gameTime.ElapsedGameTime.TotalSeconds;
        _panelRenderSystem!.Update(in deltaTime);
        _lifecycleSystem!.EndFrame();
    }

    /// <summary>
    /// Renders the debug overlay.
    /// Call this at the end of your Draw method.
    /// </summary>
    public void Draw()
    {
        if (!_initialized || _disposed)
            return;

        _lifecycleSystem!.Render();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
            return;

        // Disconnect from Serilog sink
        ImGuiLogSink.SetLogsPanel(null);

        _panelRenderSystem?.Dispose();
        _inputBridgeSystem?.Dispose();
        _lifecycleSystem?.Dispose();
        _panelRegistry?.Dispose();
        _renderer?.Dispose();

        _disposed = true;
    }

    private void RegisterDefaultPanels()
    {
        // Register the default performance panel
        _panelRegistry!.Register(new PerformancePanel());

        // Register the entity inspector panel
        _panelRegistry.Register(new EntityInspectorPanel(_world));

        // Register the scene inspector panel
        _panelRegistry.Register(new SceneInspectorPanel(_world));

        // Register the system profiler panel
        _panelRegistry.Register(new SystemProfilerPanel());

        // Register the event inspector panel
        _panelRegistry.Register(new EventInspectorPanel());

        // Register the logs panel and connect to Serilog sink
        var logsPanel = new LogsPanel();
        _panelRegistry.Register(logsPanel);
        ImGuiLogSink.SetLogsPanel(logsPanel);
    }

    private void ThrowIfNotInitialized()
    {
        if (!_initialized)
            throw new InvalidOperationException(
                "Debug overlay has not been initialized. Call Initialize() first."
            );
    }
}
