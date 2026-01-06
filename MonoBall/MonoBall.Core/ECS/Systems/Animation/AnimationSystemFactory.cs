using System;
using System.Collections.Generic;
using Arch.System;
using MonoBall.Core.ECS.Services;
using MonoBall.Core.Logging;
using MonoBall.Core.Scenes;
using MonoBall.Core.UI.Windows.Animations;

namespace MonoBall.Core.ECS.Systems.Animation;

/// <summary>
///     Factory for creating animation and visibility systems.
///     Extracts animation system creation logic from SystemManager.
/// </summary>
public sealed class AnimationSystemFactory : IAnimationSystemFactory
{
    /// <inheritdoc />
    public IAnimationSystems Create(
        SystemCreationContext context,
        IFlagVariableService flagVariableService,
        Func<ISceneSystems?> sceneSystemsProvider,
        IList<BaseSystem<Arch.Core.World, float>> updateSystems
    )
    {
        if (context == null)
            throw new ArgumentNullException(nameof(context));
        if (flagVariableService == null)
            throw new ArgumentNullException(nameof(flagVariableService));
        if (updateSystems == null)
            throw new ArgumentNullException(nameof(updateSystems));

        // Create animated tile system
        var animatedTileSystem = new AnimatedTileSystem(
            context.World,
            context.ResourceManager,
            LoggerFactory.CreateLogger<AnimatedTileSystem>()
        );
        updateSystems.Add(animatedTileSystem);

        // Create sprite animation system
        var spriteAnimationSystem = new SpriteAnimationSystem(
            context.World,
            context.ResourceManager,
            LoggerFactory.CreateLogger<SpriteAnimationSystem>()
        );
        updateSystems.Add(spriteAnimationSystem);

        // Create sprite sheet system
        var spriteSheetSystem = new SpriteSheetSystem(
            context.World,
            context.ResourceManager,
            LoggerFactory.CreateLogger<SpriteSheetSystem>()
        );
        updateSystems.Add(spriteSheetSystem);

        // Create window animation system with lazy scene systems reference
        var windowAnimationSystem = new WindowAnimationSystem(
            context.World,
            LoggerFactory.CreateLogger<WindowAnimationSystem>(),
            sceneSystemsProvider
        );
        updateSystems.Add(windowAnimationSystem);

        // Create visibility flag system
        var visibilityFlagSystem = new VisibilityFlagSystem(
            context.World,
            flagVariableService,
            LoggerFactory.CreateLogger<VisibilityFlagSystem>()
        );
        updateSystems.Add(visibilityFlagSystem);

        // Create performance stats system
        var performanceStatsSystem = new PerformanceStatsSystem(
            context.World,
            LoggerFactory.CreateLogger<PerformanceStatsSystem>()
        );
        updateSystems.Add(performanceStatsSystem);

        context.Logger.Debug("Animation systems created successfully");

        return new AnimationSystems(
            animatedTileSystem,
            spriteAnimationSystem,
            spriteSheetSystem,
            visibilityFlagSystem,
            performanceStatsSystem
        );
    }
}
