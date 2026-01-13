using System;
using Arch.Core;
using Microsoft.Xna.Framework;
using MonoBall.Core.Diagnostics.Services;
using MonoBall.Core.Scenes.Components;
using MonoBall.Core.Scenes.Systems;
using MonoBall.Core.UI.Systems;

namespace MonoBall.Core.Scenes;

/// <summary>
///     Bundle implementation containing scene-related systems.
///     Groups scene systems for coordinated lifecycle management.
/// </summary>
public sealed class SceneSystems : ISceneSystems
{
    private readonly SceneSystem? _sceneSystem;
    private bool _disposed;

    /// <summary>
    ///     Creates a new scene systems bundle.
    /// </summary>
    public SceneSystems(
        SceneSystem? sceneSystem,
        ISceneSystem? gameSceneSystem,
        ISceneSystem? loadingSceneSystem,
        ISceneSystem? debugBarSceneSystem,
        ISceneSystem? mapPopupSceneSystem,
        ISceneSystem? messageBoxSceneSystem,
        ISceneSystem? debugMenuSceneSystem,
        IDebugOverlayService? debugOverlayService,
        UIRenderSystem? uiRenderSystem = null
    )
    {
        _sceneSystem = sceneSystem;
        GameSceneSystem = gameSceneSystem;
        LoadingSceneSystem = loadingSceneSystem;
        DebugBarSceneSystem = debugBarSceneSystem;
        MapPopupSceneSystem = mapPopupSceneSystem;
        MessageBoxSceneSystem = messageBoxSceneSystem;
        DebugMenuSceneSystem = debugMenuSceneSystem;
        DebugOverlayService = debugOverlayService;
        UIRenderSystem = uiRenderSystem;
    }

    /// <inheritdoc />
    public ISceneManager? SceneManager => _sceneSystem;

    /// <summary>
    ///     Gets the SceneSystem directly (for internal use when concrete type is needed).
    /// </summary>
    internal SceneSystem? SceneSystemInternal => _sceneSystem;

    /// <inheritdoc />
    public ISceneSystem? GameSceneSystem { get; }

    /// <inheritdoc />
    public ISceneSystem? LoadingSceneSystem { get; }

    /// <inheritdoc />
    public ISceneSystem? DebugBarSceneSystem { get; }

    /// <inheritdoc />
    public ISceneSystem? MapPopupSceneSystem { get; }

    /// <inheritdoc />
    public ISceneSystem? MessageBoxSceneSystem { get; }

    /// <inheritdoc />
    public ISceneSystem? DebugMenuSceneSystem { get; }

    /// <inheritdoc />
    public IDebugOverlayService? DebugOverlayService { get; }

    /// <summary>
    ///     Gets the UI render system for rendering UI entities.
    /// </summary>
    public UIRenderSystem? UIRenderSystem { get; }

    /// <inheritdoc />
    public bool IsAvailable => _sceneSystem != null;

    /// <inheritdoc />
    public bool IsUpdateBlocked()
    {
        return _sceneSystem?.IsUpdateBlocked() ?? false;
    }

    /// <inheritdoc />
    public bool DoesEntityBelongToBlockingScene(Entity entity)
    {
        return _sceneSystem?.DoesEntityBelongToBlockingScene(entity) ?? false;
    }

    /// <inheritdoc />
    public Color GetBackgroundColor()
    {
        return _sceneSystem?.GetBackgroundColor() ?? Color.Black;
    }

    /// <inheritdoc />
    public void Render(GameTime gameTime)
    {
        _sceneSystem?.Render(gameTime);
    }

    /// <inheritdoc />
    public void IterateScenes(Func<Entity, SceneComponent, bool> processScene)
    {
        _sceneSystem?.IterateScenes(processScene);
    }

    /// <inheritdoc />
    public void Cleanup()
    {
        _sceneSystem?.Cleanup();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        // Dispose scene system (it manages its own scene systems)
        _sceneSystem?.Dispose();

        // Dispose debug overlay service
        DebugOverlayService?.Dispose();

        // Dispose UI render system
        UIRenderSystem?.Dispose();

        // Note: Individual scene systems are owned by SceneSystem and disposed by it
        // We don't dispose them here to avoid double-disposal
    }
}
