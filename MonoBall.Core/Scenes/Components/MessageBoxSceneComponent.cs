namespace MonoBall.Core.Scenes.Components;

/// <summary>
///     Marker component that identifies a scene entity as a MessageBoxScene.
///     MessageBoxScene renders message boxes with character-by-character typewriting animation.
/// </summary>
/// <remarks>
///     <para>
///         MessageBoxScene displays text in a message box window with typewriting effect.
///         Supports A/B button input for speed-up and text advancement.
///     </para>
///     <para>
///         Scene entities with MessageBoxSceneComponent should have SceneComponent with:
///         - CameraMode = SceneCameraMode.GameCamera (uses game camera for proper scaling from GBA sprites)
///         - Priority = ScenePriorities.GameScene + 20 (70) - above game scene, below loading/debug
///         - BlocksUpdate = true (blocks game updates when active)
///         - BlocksDraw = false (allows game to render behind message box)
///     </para>
/// </remarks>
public struct MessageBoxSceneComponent
{
    // Marker component - no data needed
}
