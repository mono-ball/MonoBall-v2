namespace MonoBall.Core.Scenes.Components;

/// <summary>
///     Marker component that identifies a scene entity as a DebugBarScene.
///     DebugBarScene is a screen-space overlay that displays performance statistics.
/// </summary>
/// <remarks>
///     <para>
///         DebugBarScene renders debug information (FPS, frame time, entity count, memory, draw calls, GC info)
///         in a horizontal bar at the bottom of the screen using screen coordinates.
///     </para>
///     <para>
///         Scene entities with DebugBarSceneComponent should have SceneComponent with CameraMode =
///         SceneCameraMode.ScreenCamera.
///     </para>
/// </remarks>
public struct DebugBarSceneComponent
{
    // Marker component - no data needed
}
