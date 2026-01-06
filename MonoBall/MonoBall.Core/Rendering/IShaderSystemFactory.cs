using System;
using System.Collections.Generic;
using Arch.System;
using MonoBall.Core.ECS;
using MonoBall.Core.ECS.Services;
using MonoBall.Core.ECS.Systems;

namespace MonoBall.Core.Rendering;

/// <summary>
///     Factory interface for creating shader-related systems.
/// </summary>
public interface IShaderSystemFactory
{
    /// <summary>
    ///     Creates the shader systems bundle.
    /// </summary>
    /// <param name="context">The system creation context with shared dependencies.</param>
    /// <param name="inputBindingService">The input binding service for shader cycling (F4/F5 keys).</param>
    /// <param name="playerSystem">The player system for getting player entity (optional, for F5 shader cycling).</param>
    /// <param name="updateSystems">List to add update systems to for registration.</param>
    /// <returns>The created shader systems bundle.</returns>
    IShaderSystems Create(
        SystemCreationContext context,
        IInputBindingService inputBindingService,
        PlayerSystem? playerSystem,
        IList<BaseSystem<Arch.Core.World, float>> updateSystems
    );
}
