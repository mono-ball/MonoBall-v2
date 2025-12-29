using Microsoft.Xna.Framework;

namespace MonoBall.Core.Scenes.Components;

/// <summary>
///     Component that stores scene state and configuration.
///     Determines camera selection, blocking behavior, and priority ordering.
/// </summary>
/// <remarks>
///     CameraMode determines which camera transform to apply, NOT what content to render.
///     Scene types (GameScene, PopupScene, etc.) are identified by separate marker components.
/// </remarks>
public struct SceneComponent
{
    /// <summary>
    ///     Unique identifier for the scene.
    /// </summary>
    public string SceneId { get; set; }

    /// <summary>
    ///     Rendering/update/input priority (higher = first).
    /// </summary>
    public int Priority { get; set; }

    /// <summary>
    ///     Which camera to use for rendering.
    /// </summary>
    public SceneCameraMode CameraMode { get; set; }

    /// <summary>
    ///     Optional camera entity ID (required if CameraMode == SceneCamera, ignored otherwise).
    /// </summary>
    public int? CameraEntityId { get; set; }

    /// <summary>
    ///     Whether this scene blocks lower scenes from updating.
    /// </summary>
    public bool BlocksUpdate { get; set; }

    /// <summary>
    ///     Whether this scene blocks lower scenes from drawing.
    /// </summary>
    public bool BlocksDraw { get; set; }

    /// <summary>
    ///     Whether this scene blocks lower scenes from receiving input.
    /// </summary>
    public bool BlocksInput { get; set; }

    /// <summary>
    ///     Whether the scene is active.
    /// </summary>
    public bool IsActive { get; set; }

    /// <summary>
    ///     Whether the scene is paused.
    /// </summary>
    public bool IsPaused { get; set; }

    /// <summary>
    ///     Background color for the scene. If null, uses default based on scene type.
    /// </summary>
    public Color? BackgroundColor { get; set; }
}
