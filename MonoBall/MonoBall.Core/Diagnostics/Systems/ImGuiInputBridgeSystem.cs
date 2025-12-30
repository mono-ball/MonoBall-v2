namespace MonoBall.Core.Diagnostics.Systems;

using System;
using Arch.Core;
using Microsoft.Xna.Framework;

/// <summary>
/// Bridges input state between MonoGame and ImGui.
/// Provides methods to check if ImGui wants to consume input.
/// </summary>
public sealed class ImGuiInputBridgeSystem : DebugSystemBase
{
    private readonly ImGuiLifecycleSystem _lifecycleSystem;
    private GameWindow? _gameWindow;

    /// <summary>
    /// Initializes the ImGui input bridge system.
    /// </summary>
    /// <param name="world">The ECS world.</param>
    /// <param name="lifecycleSystem">The ImGui lifecycle system.</param>
    /// <exception cref="ArgumentNullException">Thrown when lifecycleSystem is null.</exception>
    public ImGuiInputBridgeSystem(World world, ImGuiLifecycleSystem lifecycleSystem)
        : base(world)
    {
        _lifecycleSystem =
            lifecycleSystem ?? throw new ArgumentNullException(nameof(lifecycleSystem));
    }

    /// <summary>
    /// Hooks text input events from the game window.
    /// Call this after game initialization.
    /// </summary>
    /// <param name="window">The game window.</param>
    public void HookTextInput(GameWindow window)
    {
        ThrowIfDisposed();

        if (window == null)
            throw new ArgumentNullException(nameof(window));

        if (_gameWindow != null)
            _gameWindow.TextInput -= OnTextInput;

        _gameWindow = window;
        _gameWindow.TextInput += OnTextInput;
    }

    /// <summary>
    /// Gets whether ImGui wants to capture keyboard input.
    /// When true, the game should not process keyboard input.
    /// </summary>
    public bool WantsCaptureKeyboard => _lifecycleSystem.WantsCaptureKeyboard;

    /// <summary>
    /// Gets whether ImGui wants to capture mouse input.
    /// When true, the game should not process mouse input.
    /// </summary>
    public bool WantsCaptureMouse => _lifecycleSystem.WantsCaptureMouse;

    /// <summary>
    /// Gets whether ImGui wants text input.
    /// When true, the game should not process text input.
    /// </summary>
    public bool WantsCaptureTextInput => _lifecycleSystem.WantsCaptureTextInput;

    /// <summary>
    /// Gets whether the debug overlay is currently visible.
    /// </summary>
    public bool IsDebugVisible => _lifecycleSystem.IsVisible;

    /// <inheritdoc />
    public override void Update(in float deltaTime)
    {
        // Input state is queried directly via properties
    }

    /// <inheritdoc />
    protected override void DisposeManagedResources()
    {
        if (_gameWindow != null)
        {
            _gameWindow.TextInput -= OnTextInput;
            _gameWindow = null;
        }

        base.DisposeManagedResources();
    }

    private void OnTextInput(object? sender, TextInputEventArgs e)
    {
        if (!_lifecycleSystem.IsVisible)
            return;

        var io = Hexa.NET.ImGui.ImGui.GetIO();
        io.AddInputCharacter(e.Character);
    }
}
