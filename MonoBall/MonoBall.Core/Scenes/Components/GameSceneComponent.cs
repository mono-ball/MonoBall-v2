namespace MonoBall.Core.Scenes.Components
{
    /// <summary>
    /// Marker component that identifies a scene entity as a GameScene.
    /// GameScene is the main game scene where game world content is rendered.
    /// </summary>
    /// <remarks>
    /// <para>
    /// GameScene renders all game content: maps (via MapRendererSystem), NPCs, player, etc.
    /// (NPCs and player rendering to be added in future)
    /// </para>
    /// <para>
    /// GameScene doesn't need to store map entity IDs - it renders ALL loaded maps.
    /// Maps are global to the game world, not scene-specific.
    /// </para>
    /// <para>
    /// Scene entities with GameSceneComponent should have SceneComponent with CameraMode = SceneCameraMode.GameCamera.
    /// </para>
    /// </remarks>
    public struct GameSceneComponent
    {
        // Marker component - no data needed
    }
}
