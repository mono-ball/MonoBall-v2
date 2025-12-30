using System;
using Arch.Core;
using Arch.System;
using Microsoft.Xna.Framework;
using MonoBall.Core.Diagnostics.Services;
using MonoBall.Core.ECS;
using MonoBall.Core.ECS.Input;
using MonoBall.Core.ECS.Services;
using MonoBall.Core.Scenes.Components;
using Serilog;

namespace MonoBall.Core.Scenes.Systems;

/// <summary>
///     Scene system for the ImGui debug menu overlay.
///     Handles lifecycle, input, and rendering following MessageBoxSceneSystem pattern.
/// </summary>
/// <remarks>
///     <para>
///         DebugMenuSceneSystem manages the ImGui debug overlay as a scene.
///         Uses IInputBindingService directly (not blocked by SceneInputBlocker).
///     </para>
///     <para>
///         Input handling:
///         - Backtick (`) toggles the menu open/closed
///         - ESC closes the menu if open (otherwise propagates to Pause)
///     </para>
/// </remarks>
public sealed class DebugMenuSceneSystem
    : BaseSystem<World, float>,
        IPrioritizedSystem,
        ISceneSystem,
        IDisposable
{
    private readonly IDebugOverlayService _debugOverlay;
    private readonly IInputBindingService _inputBindingService;
    private readonly ISceneManager _sceneManager;

    private Entity? _activeSceneEntity;
    private bool _disposed;

    /// <summary>
    ///     Initializes a new instance of the DebugMenuSceneSystem.
    /// </summary>
    /// <param name="world">The ECS world.</param>
    /// <param name="sceneManager">The scene manager for creating/destroying scenes.</param>
    /// <param name="inputBindingService">The input binding service for detecting toggle input.</param>
    /// <param name="debugOverlay">The debug overlay service for rendering ImGui.</param>
    public DebugMenuSceneSystem(
        World world,
        ISceneManager sceneManager,
        IInputBindingService inputBindingService,
        IDebugOverlayService debugOverlay
    )
        : base(world)
    {
        _sceneManager = sceneManager ?? throw new ArgumentNullException(nameof(sceneManager));
        _inputBindingService =
            inputBindingService ?? throw new ArgumentNullException(nameof(inputBindingService));
        _debugOverlay = debugOverlay ?? throw new ArgumentNullException(nameof(debugOverlay));
    }

    /// <inheritdoc />
    public int Priority => SystemPriority.DebugMenuScene;

    /// <summary>
    ///     Updates a specific debug menu scene entity.
    ///     Implements ISceneSystem interface.
    /// </summary>
    /// <param name="sceneEntity">The scene entity to update.</param>
    /// <param name="deltaTime">The elapsed time since last update.</param>
    /// <remarks>
    ///     Per-scene updates are handled by ProcessInternal().
    ///     This method exists to satisfy ISceneSystem interface.
    /// </remarks>
    public void Update(Entity sceneEntity, float deltaTime)
    {
        // Per-scene update not needed - input handling is in override Update()
    }

    /// <summary>
    ///     Renders a specific debug menu scene entity.
    ///     Implements ISceneSystem interface.
    /// </summary>
    /// <param name="sceneEntity">The scene entity to render.</param>
    /// <param name="gameTime">The game time.</param>
    public void RenderScene(Entity sceneEntity, GameTime gameTime)
    {
        if (!_activeSceneEntity.HasValue || !World.IsAlive(_activeSceneEntity.Value))
            return;

        // Render the ImGui overlay
        _debugOverlay.Draw();
    }

    /// <summary>
    ///     Performs internal processing that needs to run every frame.
    ///     Implements ISceneSystem interface.
    /// </summary>
    /// <param name="deltaTime">The elapsed time since last update.</param>
    public void ProcessInternal(float deltaTime)
    {
        // Delegate to Update() for input handling
        Update(in deltaTime);
    }

    /// <summary>
    ///     Updates the debug menu system, handling input and overlay state.
    ///     Overrides BaseSystem.Update() to follow standard Arch ECS pattern.
    /// </summary>
    /// <param name="deltaTime">The elapsed time since last update.</param>
    public override void Update(in float deltaTime)
    {
        // Handle toggle input (backtick key)
        // IInputBindingService is NOT blocked by SceneInputBlocker
        if (_inputBindingService.IsActionJustPressed(InputAction.ToggleDebugMenu))
        {
            if (_activeSceneEntity.HasValue)
            {
                CloseMenu();
            }
            else
            {
                OpenMenu();
                // Don't call BeginUpdate/EndUpdate here - the code below handles it
                // This prevents double frame processing on menu open
            }

            // Fall through to update overlay if now open (handles the open case)
        }

        // Handle ESC to close (only when menu is open)
        // This intercepts Pause action when menu is open
        if (
            _activeSceneEntity.HasValue
            && _inputBindingService.IsActionJustPressed(InputAction.Pause)
        )
        {
            CloseMenu();
            return;
        }

        // Update overlay if menu is open
        if (_activeSceneEntity.HasValue)
        {
            // Create GameTime from deltaTime (RenderScene hasn't run yet on first frame)
            var gameTime = new GameTime(TimeSpan.Zero, TimeSpan.FromSeconds(deltaTime));
            _debugOverlay.BeginUpdate(gameTime);
            _debugOverlay.EndUpdate(gameTime);
        }
    }

    /// <summary>
    ///     Opens the debug menu by creating a scene entity.
    /// </summary>
    private void OpenMenu()
    {
        if (_activeSceneEntity.HasValue)
            return;

        var sceneComponent = new SceneComponent
        {
            SceneId = "debug:menu",
            Priority = ScenePriorities.DebugOverlay,
            CameraMode = SceneCameraMode.ScreenCamera,
            BlocksUpdate = false,
            BlocksDraw = false,
            BlocksInput = true,
            IsActive = true,
            IsPaused = false,
            BackgroundColor = Color.Transparent, // Transparent overlay - game renders behind
        };

        _activeSceneEntity = _sceneManager.CreateScene(
            sceneComponent,
            new DebugMenuSceneComponent()
        );

        _debugOverlay.Show();
    }

    /// <summary>
    ///     Closes the debug menu by destroying the scene entity.
    /// </summary>
    private void CloseMenu()
    {
        if (!_activeSceneEntity.HasValue)
            return;

        _debugOverlay.Hide();

        if (World.IsAlive(_activeSceneEntity.Value))
            _sceneManager.DestroyScene(_activeSceneEntity.Value);

        _activeSceneEntity = null;
    }

    /// <summary>
    ///     Gets whether the debug menu is currently open.
    /// </summary>
    public bool IsMenuOpen => _activeSceneEntity.HasValue;

    /// <inheritdoc />
    public new void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    ///     Dispose implementation following standard dispose pattern.
    /// </summary>
    /// <param name="disposing">True if called from Dispose(), false if called from finalizer.</param>
    private void Dispose(bool disposing)
    {
        if (_disposed)
            return;

        if (disposing)
            CloseMenu();

        _disposed = true;
    }
}
