namespace MonoBall.Core.Diagnostics.Services;

using System;
using Microsoft.Xna.Framework;
using MonoBall.Core.Diagnostics.Console.Services;
using MonoBall.Core.Diagnostics.Panels;
using MonoBall.Core.Resources;
using MonoBall.Core.Scenes.Systems;

/// <summary>
/// Interface for the debug overlay service.
/// Provides abstraction for debug overlay functionality following DIP.
/// </summary>
public interface IDebugOverlayService : IDisposable
{
    /// <summary>
    /// Gets whether the debug overlay has been initialized.
    /// </summary>
    bool IsInitialized { get; }

    /// <summary>
    /// Gets whether the debug overlay is currently visible.
    /// </summary>
    bool IsVisible { get; }

    /// <summary>
    /// Gets whether ImGui wants to capture keyboard input.
    /// </summary>
    bool WantsCaptureKeyboard { get; }

    /// <summary>
    /// Gets whether ImGui wants to capture mouse input.
    /// </summary>
    bool WantsCaptureMouse { get; }

    /// <summary>
    /// Gets the panel registry for registering custom panels.
    /// </summary>
    IDebugPanelRegistry? PanelRegistry { get; }

    /// <summary>
    /// Gets the time control service for pausing/resuming the game.
    /// </summary>
    ITimeControl? TimeControl { get; }

    /// <summary>
    /// Initializes the debug overlay system.
    /// Call this after the game has been initialized.
    /// </summary>
    /// <param name="game">The MonoGame Game instance.</param>
    /// <param name="resourceManager">Optional resource manager for loading fonts from the mod system.</param>
    /// <param name="sceneSystem">Optional scene system for time control commands.</param>
    void Initialize(
        Game game,
        IResourceManager? resourceManager = null,
        SceneSystem? sceneSystem = null
    );

    /// <summary>
    /// Registers a debug panel.
    /// </summary>
    /// <param name="panel">The panel to register.</param>
    void RegisterPanel(IDebugPanel panel);

    /// <summary>
    /// Toggles the visibility of the debug overlay.
    /// </summary>
    void Toggle();

    /// <summary>
    /// Shows the debug overlay.
    /// </summary>
    void Show();

    /// <summary>
    /// Hides the debug overlay.
    /// </summary>
    void Hide();

    /// <summary>
    /// Updates the debug overlay.
    /// Call this at the start of your Update method.
    /// </summary>
    /// <param name="gameTime">The game time.</param>
    void BeginUpdate(GameTime gameTime);

    /// <summary>
    /// Renders debug panels and ends the ImGui frame.
    /// Call this at the end of your Update method.
    /// </summary>
    /// <param name="gameTime">The game time.</param>
    void EndUpdate(GameTime gameTime);

    /// <summary>
    /// Renders the debug overlay.
    /// Call this at the end of your Draw method.
    /// </summary>
    void Draw();
}
