using System.Collections.Generic;
using Arch.System;
using MonoBall.Core.Diagnostics.Services;
using MonoBall.Core.ECS;
using MonoBall.Core.ECS.Systems;
using MonoBall.Core.Logging;
using MonoBall.Core.Scenes.Systems;
using MonoBall.Core.TextEffects;
using MonoBall.Core.UI.Systems;

namespace MonoBall.Core.Scenes;

/// <summary>
///     Factory for creating scene-related systems.
///     Extracts scene system creation logic from SystemManager for cleaner architecture.
/// </summary>
public sealed class SceneSystemFactory : ISceneSystemFactory
{
    /// <inheritdoc />
    public ISceneSystems Create(
        SceneSystemCreationContext context,
        IList<BaseSystem<Arch.Core.World, float>> updateSystems
    )
    {
        // Get performance stats system from animation systems bundle (needed for debug bar)
        var performanceStatsSystem = context.AnimationSystems?.PerformanceStatsSystem;
        if (performanceStatsSystem == null)
        {
            // Return empty bundle if performance stats not available
            return new SceneSystems(
                sceneSystem: null,
                gameSceneSystem: null,
                loadingSceneSystem: null,
                debugBarSceneSystem: null,
                mapPopupSceneSystem: null,
                messageBoxSceneSystem: null,
                debugMenuSceneSystem: null,
                debugOverlayService: null,
                uiRenderSystem: null
            );
        }

        // Create scene-specific systems first
        var loadingSceneSystem = new LoadingSceneSystem(
            context.World,
            context.GraphicsDevice,
            context.SpriteBatch,
            context.Game,
            LoggerFactory.CreateLogger<LoadingSceneSystem>()
        );

        var gameSceneSystem = new GameSceneSystem(
            context.World,
            context.GraphicsDevice,
            context.SpriteBatch,
            context.RenderingSystems?.ElevationRendererSystem!,
            context.ShaderSystems?.ShaderManager,
            context.ShaderSystems?.ShaderRenderer,
            context.ShaderSystems?.RenderTargetManager,
            LoggerFactory.CreateLogger<GameSceneSystem>()
        );

        var debugBarSceneSystem = new DebugBarSceneSystem(
            context.World,
            context.GraphicsDevice,
            context.SpriteBatch,
            context.ResourceManager,
            performanceStatsSystem,
            LoggerFactory.CreateLogger<DebugBarSceneSystem>()
        );

        // Create SceneSystem coordinator with initial scene systems
        // MapPopupSceneSystem and MessageBoxSceneSystem will be set after (they need SceneSystem)
        var sceneSystem = new SceneSystem(
            context.World,
            LoggerFactory.CreateLogger<SceneSystem>(),
            context.GraphicsDevice,
            context.ShaderSystems?.ShaderManager,
            gameSceneSystem,
            loadingSceneSystem,
            debugBarSceneSystem
        );

        // Create MapPopupSceneSystem (needs ISceneManager, which SceneSystem implements)
        var mapPopupSceneSystem = new MapPopupSceneSystem(
            context.World,
            sceneSystem,
            context.GraphicsDevice,
            context.SpriteBatch,
            context.ModManager,
            LoggerFactory.CreateLogger<MapPopupSceneSystem>(),
            context.ConstantsService,
            context.ResourceManager
        );

        // Register MapPopupSceneSystem with SceneSystem
        sceneSystem.SetMapPopupSceneSystem(mapPopupSceneSystem);

        // Create TextEffectCalculator for animated text effects
        var textEffectCalculator = new TextEffectCalculator();

        // Create UIRenderSystem (needed by MessageBoxSceneSystem for rendering UI entities)
        var uiRenderSystem = new UIRenderSystem(
            context.World,
            context.GraphicsDevice,
            context.SpriteBatch,
            context.ResourceManager,
            context.CameraService,
            context.ConstantsService,
            context.ModManager,
            LoggerFactory.CreateLogger<UIRenderSystem>()
        );

        // Create MessageBoxSceneSystem (needs ISceneManager and UIRenderSystem)
        var messageBoxSceneSystem = new MessageBoxSceneSystem(
            context.World,
            sceneSystem,
            context.ModManager,
            context.InputBindingService,
            context.FlagVariableService,
            context.CameraService,
            context.GraphicsDevice,
            context.SpriteBatch,
            LoggerFactory.CreateLogger<MessageBoxSceneSystem>(),
            context.ConstantsService,
            textEffectCalculator,
            context.ResourceManager,
            uiRenderSystem
        );

        // Register MessageBoxSceneSystem with SceneSystem
        sceneSystem.SetMessageBoxSceneSystem(messageBoxSceneSystem);

        // Register MessageBoxSceneSystem with update systems (it implements IPrioritizedSystem)
        updateSystems.Add(messageBoxSceneSystem);

        // Register SceneSystem with update systems
        updateSystems.Add(sceneSystem);

        // Create ImGui debug overlay service
        var debugOverlayService = new DebugOverlayService(context.World);
        debugOverlayService.Initialize(
            context.Game,
            context.ResourceManager,
            sceneSystem,
            context.ModManager
        );

        // Create DebugMenuSceneSystem (needs SceneSystem and DebugOverlayService)
        var debugMenuSceneSystem = new DebugMenuSceneSystem(
            context.World,
            sceneSystem,
            context.InputBindingService,
            debugOverlayService
        );

        // Register DebugMenuSceneSystem with SceneSystem
        sceneSystem.SetDebugMenuSceneSystem(debugMenuSceneSystem);

        // Register DebugMenuSceneSystem with update systems
        updateSystems.Add(debugMenuSceneSystem);

        // Return the bundle with all created systems
        // Note: uiRenderSystem was created earlier and passed to MessageBoxSceneSystem
        return new SceneSystems(
            sceneSystem: sceneSystem,
            gameSceneSystem: gameSceneSystem,
            loadingSceneSystem: loadingSceneSystem,
            debugBarSceneSystem: debugBarSceneSystem,
            mapPopupSceneSystem: mapPopupSceneSystem,
            messageBoxSceneSystem: messageBoxSceneSystem,
            debugMenuSceneSystem: debugMenuSceneSystem,
            debugOverlayService: debugOverlayService,
            uiRenderSystem: uiRenderSystem
        );
    }
}
