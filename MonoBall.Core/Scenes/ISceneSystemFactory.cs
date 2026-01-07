using System.Collections.Generic;
using Arch.System;
using MonoBall.Core.ECS;

namespace MonoBall.Core.Scenes;

/// <summary>
///     Factory interface for creating scene-related systems.
/// </summary>
public interface ISceneSystemFactory
{
    /// <summary>
    ///     Creates scene systems using the provided context.
    /// </summary>
    /// <param name="context">The system creation context with shared dependencies.</param>
    /// <param name="updateSystems">List to add update systems to for registration.</param>
    /// <returns>The created scene systems bundle.</returns>
    ISceneSystems Create(
        SceneSystemCreationContext context,
        IList<BaseSystem<Arch.Core.World, float>> updateSystems
    );
}
