using System;
using MonoBall.Core.ECS.Services;
using MonoBall.Core.ECS.Systems;

namespace MonoBall.Core.Rendering;

/// <summary>
///     Bundle interface for shader-related systems.
///     Exposes interfaces, not concrete types, for proper dependency inversion.
/// </summary>
public interface IShaderSystems : IDisposable
{
    /// <summary>
    ///     Gets the shader manager for applying shaders to entities and screen.
    /// </summary>
    IShaderManager? ShaderManager { get; }

    /// <summary>
    ///     Gets the shader renderer system for rendering with shaders.
    /// </summary>
    IShaderRenderer? ShaderRenderer { get; }

    /// <summary>
    ///     Gets the render target manager for managing render targets.
    /// </summary>
    IRenderTargetManager? RenderTargetManager { get; }

    /// <summary>
    ///     Gets the shader parameter animation system.
    /// </summary>
    IShaderParameterAnimationSystem? ParameterAnimation { get; }

    /// <summary>
    ///     Gets the shader transition system.
    /// </summary>
    IShaderTransitionSystem? TransitionSystem { get; }

    /// <summary>
    ///     Gets the multi-parameter animation system.
    /// </summary>
    IShaderMultiParameterAnimationSystem? MultiParameterAnimation { get; }

    /// <summary>
    ///     Gets the animation chain system.
    /// </summary>
    IShaderAnimationChainSystem? AnimationChain { get; }

    /// <summary>
    ///     Gets the shader region detection system.
    /// </summary>
    IShaderRegionDetectionSystem? RegionDetection { get; }

    /// <summary>
    ///     Gets the shader template system.
    /// </summary>
    IShaderTemplateSystem? TemplateSystem { get; }

    /// <summary>
    ///     Gets the shader preset service.
    /// </summary>
    IShaderPresetService? PresetService { get; }

    /// <summary>
    ///     Gets the shader parameter timeline system.
    /// </summary>
    IShaderParameterTimelineSystem? TimelineSystem { get; }

    /// <summary>
    ///     Gets whether shader systems are available (shader service was provided).
    /// </summary>
    bool IsAvailable { get; }
}
