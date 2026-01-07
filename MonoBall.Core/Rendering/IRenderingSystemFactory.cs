using MonoBall.Core.ECS;
using MonoBall.Core.ECS.Services;

namespace MonoBall.Core.Rendering;

/// <summary>
///     Factory interface for creating rendering systems.
/// </summary>
public interface IRenderingSystemFactory
{
    /// <summary>
    ///     Creates the rendering systems bundle.
    /// </summary>
    /// <param name="context">The system creation context with shared dependencies.</param>
    /// <param name="cameraService">The camera service for viewport calculations.</param>
    /// <param name="activeMapFilterService">The active map filter service.</param>
    /// <param name="shaderSystems">The shader systems bundle (may be null if shaders unavailable).</param>
    /// <returns>The created rendering systems bundle.</returns>
    IRenderingSystems Create(
        SystemCreationContext context,
        ICameraService cameraService,
        IActiveMapFilterService activeMapFilterService,
        IShaderSystems? shaderSystems
    );
}
