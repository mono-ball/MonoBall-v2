namespace MonoBall.Core.Scenes.Components
{
    /// <summary>
    /// Marker component that identifies a scene entity as a LoadingScene.
    /// LoadingScene is a screen-space scene that displays loading progress.
    /// </summary>
    /// <remarks>
    /// <para>
    /// LoadingScene renders a loading screen with progress bar and current step text
    /// in screen coordinates. It blocks all other scenes from updating/drawing while active.
    /// </para>
    /// <para>
    /// Scene entities with LoadingSceneComponent should have:
    /// - SceneComponent with CameraMode = SceneCameraMode.ScreenCamera
    /// - LoadingProgressComponent for progress tracking
    /// </para>
    /// </remarks>
    public struct LoadingSceneComponent
    {
        // Marker component - no data needed
    }
}
