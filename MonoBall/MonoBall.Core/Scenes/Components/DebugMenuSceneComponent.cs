namespace MonoBall.Core.Scenes.Components;

/// <summary>
///     Marker component that identifies a scene entity as a DebugMenuScene.
///     DebugMenuScene renders the ImGui debug overlay with performance panels and entity inspector.
/// </summary>
/// <remarks>
///     <para>
///         DebugMenuScene displays the ImGui debug overlay on top of the game.
///         Supports backtick (`) to toggle and ESC to close.
///     </para>
///     <para>
///         Scene entities with DebugMenuSceneComponent should have SceneComponent with:
///         - CameraMode = SceneCameraMode.ScreenCamera (UI overlay, not affected by game camera)
///         - Priority = ScenePriorities.DebugOverlay (highest priority, renders on top)
///         - BlocksUpdate = false (game continues running)
///         - BlocksDraw = false (game renders behind overlay)
///         - BlocksInput = true (blocks game input while menu is open)
///     </para>
/// </remarks>
public struct DebugMenuSceneComponent
{
    // Marker component - no data needed
}
