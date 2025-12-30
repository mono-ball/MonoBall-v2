namespace MonoBall.Core.Diagnostics.Systems;

using System;
using Arch.Core;
using Events;
using ImGui;
using MonoBall.Core.ECS;
using Serilog;

/// <summary>
/// Manages ImGui lifecycle: BeginFrame, EndFrame, and Render.
/// This system should be updated early in the frame and rendered late.
/// </summary>
public sealed class ImGuiLifecycleSystem : DebugSystemBase
{
    private readonly IImGuiRenderer _renderer;
    private IDisposable? _toggleSubscription;
    private bool _isVisible = true;
    private bool _frameStarted;

    /// <summary>
    /// Gets whether the debug overlay is currently visible.
    /// </summary>
    public bool IsVisible => _isVisible;

    /// <summary>
    /// Gets whether ImGui wants to capture keyboard input.
    /// </summary>
    public bool WantsCaptureKeyboard =>
        _isVisible && Hexa.NET.ImGui.ImGui.GetIO().WantCaptureKeyboard;

    /// <summary>
    /// Gets whether ImGui wants to capture mouse input.
    /// </summary>
    public bool WantsCaptureMouse => _isVisible && Hexa.NET.ImGui.ImGui.GetIO().WantCaptureMouse;

    /// <summary>
    /// Gets whether ImGui wants to capture text input.
    /// </summary>
    public bool WantsCaptureTextInput => _isVisible && Hexa.NET.ImGui.ImGui.GetIO().WantTextInput;

    /// <summary>
    /// Initializes the ImGui lifecycle system.
    /// </summary>
    /// <param name="world">The ECS world.</param>
    /// <param name="renderer">The ImGui renderer.</param>
    /// <exception cref="ArgumentNullException">Thrown when renderer is null.</exception>
    public ImGuiLifecycleSystem(World world, IImGuiRenderer renderer)
        : base(world)
    {
        _renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
        _toggleSubscription = EventBus.Subscribe<DebugToggleEvent>(OnDebugToggle);
    }

    /// <summary>
    /// Begins a new ImGui frame. Call at the start of Update.
    /// </summary>
    /// <param name="deltaTime">Time since last frame in seconds.</param>
    public void BeginFrame(float deltaTime)
    {
        ThrowIfDisposed();

        if (!_isVisible || !_renderer.IsInitialized)
            return;

        // Prevent double BeginFrame without EndFrame (would duplicate input)
        if (_frameStarted)
            return;

        _renderer.BeginFrame(deltaTime);
        _frameStarted = true;
    }

    /// <summary>
    /// Ends the ImGui frame. Call at the end of Update after all ImGui drawing.
    /// </summary>
    public void EndFrame()
    {
        ThrowIfDisposed();

        if (!_frameStarted)
            return;

        _renderer.EndFrame();
        _frameStarted = false;
    }

    /// <summary>
    /// Renders ImGui draw data. Call during Draw phase.
    /// </summary>
    public void Render()
    {
        ThrowIfDisposed();

        if (!_isVisible || !_renderer.IsInitialized)
            return;

        _renderer.Render();
    }

    /// <inheritdoc />
    public override void Update(in float deltaTime)
    {
        // This system manages lifecycle externally via BeginFrame/EndFrame/Render
        // The Update method is not used in the standard way
    }

    /// <inheritdoc />
    protected override void DisposeManagedResources()
    {
        _toggleSubscription?.Dispose();
        _toggleSubscription = null;
        base.DisposeManagedResources();
    }

    private void OnDebugToggle(DebugToggleEvent evt)
    {
        var newVisible = evt.Show ?? !_isVisible;

        // If turning off visibility during a frame, end the current frame first
        if (!newVisible && _frameStarted)
        {
            _renderer.EndFrame();
            _frameStarted = false;
            Log.Debug("ImGui frame ended early due to visibility toggle");
        }

        _isVisible = newVisible;
    }
}
