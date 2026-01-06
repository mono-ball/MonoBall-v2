using System;
using Arch.System;
using MonoBall.Core.ECS.Services;
using MonoBall.Core.ECS.Systems;
using MonoBall.Core.Scenes.Systems;

namespace MonoBall.Core.Rendering;

/// <summary>
///     Bundle implementation for shader-related systems.
///     Owns and disposes all shader systems.
/// </summary>
public sealed class ShaderSystems : IShaderSystems
{
    // Concrete types for disposal
    private readonly ShaderParameterAnimationSystem? _parameterAnimationConcrete;
    private readonly ShaderTransitionSystem? _transitionSystemConcrete;
    private readonly ShaderMultiParameterAnimationSystem? _multiParameterAnimationConcrete;
    private readonly ShaderAnimationChainSystem? _animationChainConcrete;
    private readonly ShaderRegionDetectionSystem? _regionDetectionConcrete;
    private readonly ShaderParameterTimelineSystem? _timelineSystemConcrete;
    private readonly ShaderCycleSystem? _shaderCycleSystemConcrete;
    private bool _isDisposed;

    /// <summary>
    ///     Initializes a new instance of the ShaderSystems bundle.
    /// </summary>
    public ShaderSystems(
        ShaderManager? shaderManager,
        ShaderRendererSystem? shaderRenderer,
        RenderTargetManager? renderTargetManager,
        ShaderParameterAnimationSystem? parameterAnimation,
        ShaderTransitionSystem? transitionSystem,
        ShaderMultiParameterAnimationSystem? multiParameterAnimation,
        ShaderAnimationChainSystem? animationChain,
        ShaderRegionDetectionSystem? regionDetection,
        ShaderTemplateSystem? templateSystem,
        IShaderPresetService? presetService,
        ShaderParameterTimelineSystem? timelineSystem,
        ShaderCycleSystem? shaderCycleSystem
    )
    {
        // Store concrete types for disposal
        _parameterAnimationConcrete = parameterAnimation;
        _transitionSystemConcrete = transitionSystem;
        _multiParameterAnimationConcrete = multiParameterAnimation;
        _animationChainConcrete = animationChain;
        _regionDetectionConcrete = regionDetection;
        _timelineSystemConcrete = timelineSystem;
        _shaderCycleSystemConcrete = shaderCycleSystem;

        // Expose as interfaces
        ShaderManager = shaderManager;
        ShaderRenderer = shaderRenderer;
        RenderTargetManager = renderTargetManager;
        ParameterAnimation = parameterAnimation;
        TransitionSystem = transitionSystem;
        MultiParameterAnimation = multiParameterAnimation;
        AnimationChain = animationChain;
        RegionDetection = regionDetection;
        TemplateSystem = templateSystem;
        PresetService = presetService;
        TimelineSystem = timelineSystem;
        IsAvailable = shaderManager != null;
    }

    /// <inheritdoc />
    public IShaderManager? ShaderManager { get; }

    /// <inheritdoc />
    public IShaderRenderer? ShaderRenderer { get; }

    /// <inheritdoc />
    public IRenderTargetManager? RenderTargetManager { get; }

    /// <inheritdoc />
    public IShaderParameterAnimationSystem? ParameterAnimation { get; }

    /// <inheritdoc />
    public IShaderTransitionSystem? TransitionSystem { get; }

    /// <inheritdoc />
    public IShaderMultiParameterAnimationSystem? MultiParameterAnimation { get; }

    /// <inheritdoc />
    public IShaderAnimationChainSystem? AnimationChain { get; }

    /// <inheritdoc />
    public IShaderRegionDetectionSystem? RegionDetection { get; }

    /// <inheritdoc />
    public IShaderTemplateSystem? TemplateSystem { get; }

    /// <inheritdoc />
    public IShaderPresetService? PresetService { get; }

    /// <inheritdoc />
    public IShaderParameterTimelineSystem? TimelineSystem { get; }

    /// <inheritdoc />
    public bool IsAvailable { get; }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_isDisposed)
            return;
        _isDisposed = true;

        // Dispose systems in reverse creation order using concrete types
        _shaderCycleSystemConcrete?.Dispose();
        _timelineSystemConcrete?.Dispose();
        _regionDetectionConcrete?.Dispose();
        _animationChainConcrete?.Dispose();
        _multiParameterAnimationConcrete?.Dispose();
        _transitionSystemConcrete?.Dispose();
        _parameterAnimationConcrete?.Dispose();
        // TemplateSystem doesn't implement IDisposable
        // ShaderRenderer doesn't implement IDisposable
        // ShaderManager doesn't implement IDisposable (no managed resources)
        (RenderTargetManager as IDisposable)?.Dispose();
        // PresetService doesn't implement IDisposable
    }
}
