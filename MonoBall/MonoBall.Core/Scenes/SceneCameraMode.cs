namespace MonoBall.Core.Scenes
{
    /// <summary>
    /// Determines which camera transform to use for scene rendering.
    /// </summary>
    public enum SceneCameraMode
    {
        /// <summary>
        /// Uses the active game camera (CameraComponent.IsActive == true).
        /// </summary>
        GameCamera,

        /// <summary>
        /// Uses screen space (identity transform, full window).
        /// </summary>
        ScreenCamera,

        /// <summary>
        /// Uses a scene-specific camera (CameraEntityId must be set in SceneComponent).
        /// </summary>
        SceneCamera,
    }
}
