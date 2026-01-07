using System;
using Arch.Core;
using MonoBall.Core.ECS;
using MonoBall.Core.Scenes.Components;
using MonoBall.Core.Scenes.Events;

namespace MonoBall.Core.Scenes;

/// <summary>
///     Helper methods for scene event handling.
///     Provides convenient methods for firing scene lifecycle events with proper context.
/// </summary>
public static class SceneEventHelper
{
    /// <summary>
    ///     Fires a SceneCreatingEvent and returns whether creation was cancelled.
    /// </summary>
    /// <param name="sceneId">The scene ID that will be created.</param>
    /// <returns>True if scene creation was cancelled, false otherwise.</returns>
    public static bool FireSceneCreating(string sceneId)
    {
        var evt = new SceneCreatingEvent { SceneId = sceneId, Cancel = false };
        EventBus.Send(ref evt);
        return evt.Cancel;
    }

    /// <summary>
    ///     Fires a SceneCreatedEvent with context from the scene component.
    /// </summary>
    /// <param name="sceneEntity">The created scene entity.</param>
    /// <param name="sceneComponent">The scene component.</param>
    /// <param name="sceneType">Optional scene type identifier.</param>
    public static void FireSceneCreated(
        Entity sceneEntity,
        ref SceneComponent sceneComponent,
        string? sceneType = null
    )
    {
        var evt = new SceneCreatedEvent
        {
            SceneId = sceneComponent.SceneId,
            SceneEntity = sceneEntity,
            Priority = sceneComponent.Priority,
            CameraMode = sceneComponent.CameraMode,
            SceneType = sceneType,
            CreatedAt = DateTime.UtcNow,
        };
        EventBus.Send(ref evt);
    }

    /// <summary>
    ///     Fires a SceneDestroyedEvent with context from the scene component.
    /// </summary>
    /// <param name="sceneEntity">The destroyed scene entity.</param>
    /// <param name="sceneComponent">The scene component.</param>
    /// <param name="sceneType">Optional scene type identifier.</param>
    public static void FireSceneDestroyed(
        Entity sceneEntity,
        ref SceneComponent sceneComponent,
        string? sceneType = null
    )
    {
        var evt = new SceneDestroyedEvent
        {
            SceneId = sceneComponent.SceneId,
            SceneEntity = sceneEntity,
            Priority = sceneComponent.Priority,
            SceneType = sceneType,
            DestroyedAt = DateTime.UtcNow,
        };
        EventBus.Send(ref evt);
    }

    /// <summary>
    ///     Fires a SceneActivatedEvent with context from the scene component.
    /// </summary>
    /// <param name="sceneEntity">The activated scene entity.</param>
    /// <param name="sceneComponent">The scene component.</param>
    public static void FireSceneActivated(Entity sceneEntity, ref SceneComponent sceneComponent)
    {
        var evt = new SceneActivatedEvent
        {
            SceneId = sceneComponent.SceneId,
            SceneEntity = sceneEntity,
            Priority = sceneComponent.Priority,
        };
        EventBus.Send(ref evt);
    }

    /// <summary>
    ///     Fires a SceneDeactivatedEvent with context from the scene component.
    /// </summary>
    /// <param name="sceneEntity">The deactivated scene entity.</param>
    /// <param name="sceneComponent">The scene component.</param>
    public static void FireSceneDeactivated(Entity sceneEntity, ref SceneComponent sceneComponent)
    {
        var evt = new SceneDeactivatedEvent
        {
            SceneId = sceneComponent.SceneId,
            SceneEntity = sceneEntity,
            Priority = sceneComponent.Priority,
        };
        EventBus.Send(ref evt);
    }

    /// <summary>
    ///     Fires a ScenePausedEvent with context from the scene entity.
    /// </summary>
    /// <param name="sceneEntity">The paused scene entity.</param>
    /// <param name="sceneComponent">The scene component.</param>
    public static void FireScenePaused(Entity sceneEntity, ref SceneComponent sceneComponent)
    {
        var evt = new ScenePausedEvent
        {
            SceneId = sceneComponent.SceneId,
            SceneEntity = sceneEntity,
        };
        EventBus.Send(ref evt);
    }

    /// <summary>
    ///     Fires a SceneResumedEvent with context from the scene entity.
    /// </summary>
    /// <param name="sceneEntity">The resumed scene entity.</param>
    /// <param name="sceneComponent">The scene component.</param>
    public static void FireSceneResumed(Entity sceneEntity, ref SceneComponent sceneComponent)
    {
        var evt = new SceneResumedEvent
        {
            SceneId = sceneComponent.SceneId,
            SceneEntity = sceneEntity,
        };
        EventBus.Send(ref evt);
    }

    /// <summary>
    ///     Fires a SceneCameraModeChangedEvent.
    /// </summary>
    /// <param name="sceneId">The scene ID.</param>
    /// <param name="oldMode">The old camera mode.</param>
    /// <param name="newMode">The new camera mode.</param>
    public static void FireSceneCameraModeChanged(
        string sceneId,
        SceneCameraMode oldMode,
        SceneCameraMode newMode
    )
    {
        var evt = new SceneCameraModeChangedEvent
        {
            SceneId = sceneId,
            OldMode = oldMode,
            NewMode = newMode,
        };
        EventBus.Send(ref evt);
    }

    /// <summary>
    ///     Gets the scene type identifier from marker components.
    /// </summary>
    /// <param name="world">The ECS world.</param>
    /// <param name="sceneEntity">The scene entity.</param>
    /// <returns>The scene type identifier, or null if unknown.</returns>
    public static string? GetSceneType(World world, Entity sceneEntity)
    {
        if (world.Has<GameSceneComponent>(sceneEntity))
            return "GameScene";

        if (world.Has<LoadingSceneComponent>(sceneEntity))
            return "LoadingScene";

        if (world.Has<DebugBarSceneComponent>(sceneEntity))
            return "DebugBarScene";

        if (world.Has<MapPopupSceneComponent>(sceneEntity))
            return "MapPopupScene";

        return null;
    }
}
