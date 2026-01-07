using System;
using Arch.Core;
using Microsoft.Xna.Framework;
using MonoBall.Core.Diagnostics.Services;
using MonoBall.Core.Scenes.Components;

namespace MonoBall.Core.Scenes;

/// <summary>
///     Bundle interface for scene-related systems.
///     Exposes interfaces for proper dependency inversion.
/// </summary>
public interface ISceneSystems : IDisposable
{
    /// <summary>
    ///     Gets the scene manager/coordinator (SceneSystem implements ISceneManager).
    /// </summary>
    ISceneManager? SceneManager { get; }

    /// <summary>
    ///     Renders all scenes in reverse priority order (lowest priority first, highest priority last).
    ///     This ensures higher priority scenes render on top of lower priority scenes.
    /// </summary>
    /// <param name="gameTime">The game time.</param>
    void Render(GameTime gameTime);

    /// <summary>
    ///     Helper method to iterate scenes in priority order with a callback.
    ///     Handles dead entity checks and provides a consistent iteration pattern.
    /// </summary>
    /// <param name="processScene">Callback that processes each scene. Return false to stop iteration, true to continue.</param>
    void IterateScenes(Func<Entity, SceneComponent, bool> processScene);

    /// <summary>
    ///     Cleans up scene resources and destroys all active scenes.
    /// </summary>
    void Cleanup();

    /// <summary>
    ///     Gets the game scene system for main game rendering.
    /// </summary>
    ISceneSystem? GameSceneSystem { get; }

    /// <summary>
    ///     Gets the loading scene system for loading screens.
    /// </summary>
    ISceneSystem? LoadingSceneSystem { get; }

    /// <summary>
    ///     Gets the debug bar scene system for debug bar UI.
    /// </summary>
    ISceneSystem? DebugBarSceneSystem { get; }

    /// <summary>
    ///     Gets the map popup scene system for map popups.
    /// </summary>
    ISceneSystem? MapPopupSceneSystem { get; }

    /// <summary>
    ///     Gets the message box scene system for message boxes.
    /// </summary>
    ISceneSystem? MessageBoxSceneSystem { get; }

    /// <summary>
    ///     Gets the debug menu scene system for ImGui debug overlay.
    /// </summary>
    ISceneSystem? DebugMenuSceneSystem { get; }

    /// <summary>
    ///     Gets the debug overlay service for ImGui rendering.
    /// </summary>
    IDebugOverlayService? DebugOverlayService { get; }

    /// <summary>
    ///     Gets whether scene systems are available.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    ///     Checks if any active scene is currently blocking updates.
    /// </summary>
    /// <returns>True if updates are blocked, false otherwise.</returns>
    bool IsUpdateBlocked();

    /// <summary>
    ///     Checks if an entity belongs to a scene that is currently blocking updates.
    /// </summary>
    /// <param name="entity">The entity to check.</param>
    /// <returns>True if the entity belongs to a blocking scene, false otherwise.</returns>
    bool DoesEntityBelongToBlockingScene(Entity entity);

    /// <summary>
    ///     Gets the background color based on the topmost active scene.
    /// </summary>
    /// <returns>The background color.</returns>
    Color GetBackgroundColor();
}
