namespace MonoBall.Core.Scenes.Components;

/// <summary>
///     Marker component that identifies a scene entity as a MapPopupScene.
///     MapPopupScene renders map popup banners that display when transitioning between maps.
/// </summary>
/// <remarks>
///     <para>
///         MapPopupScene renders popup banners with map section names (e.g., "LITTLEROOT TOWN").
///         Popups slide down from the top, pause, then slide back up.
///     </para>
///     <para>
///         Scene entities with MapPopupSceneComponent should have SceneComponent with:
///         - CameraMode = SceneCameraMode.GameCamera
///         - Priority = ScenePriorities.GameScene + 10 (60)
///         - BlocksUpdate = false
///         - BlocksDraw = false
///     </para>
/// </remarks>
public struct MapPopupSceneComponent
{
    // Marker component - no data needed
}
