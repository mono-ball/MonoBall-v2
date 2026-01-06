using System;
using System.Collections.Generic;
using Arch.System;
using MonoBall.Core.ECS.Services;
using MonoBall.Core.Scenes;

namespace MonoBall.Core.ECS.Systems.Animation;

/// <summary>
///     Factory interface for creating animation and visibility systems.
/// </summary>
public interface IAnimationSystemFactory
{
    /// <summary>
    ///     Creates the animation systems bundle.
    /// </summary>
    /// <param name="context">The system creation context with shared dependencies.</param>
    /// <param name="flagVariableService">The flag variable service for visibility system.</param>
    /// <param name="sceneSystemsProvider">Function to lazily get the scene systems bundle (may be null initially).</param>
    /// <param name="updateSystems">List to add update systems to for registration.</param>
    /// <returns>The created animation systems bundle.</returns>
    IAnimationSystems Create(
        SystemCreationContext context,
        IFlagVariableService flagVariableService,
        Func<ISceneSystems?> sceneSystemsProvider,
        IList<BaseSystem<Arch.Core.World, float>> updateSystems
    );
}
