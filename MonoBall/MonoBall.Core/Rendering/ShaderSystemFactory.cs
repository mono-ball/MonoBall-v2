using System;
using System.Collections.Generic;
using Arch.System;
using MonoBall.Core.ECS;
using MonoBall.Core.ECS.Services;
using MonoBall.Core.ECS.Systems;
using MonoBall.Core.Logging;
using MonoBall.Core.Scenes.Systems;

namespace MonoBall.Core.Rendering;

/// <summary>
///     Factory for creating shader-related systems.
///     Extracts shader system creation logic from SystemManager.
/// </summary>
public sealed class ShaderSystemFactory : IShaderSystemFactory
{
    /// <inheritdoc />
    public IShaderSystems Create(
        SystemCreationContext context,
        IInputBindingService inputBindingService,
        PlayerSystem? playerSystem,
        IList<BaseSystem<Arch.Core.World, float>> updateSystems
    )
    {
        if (context == null)
            throw new ArgumentNullException(nameof(context));
        if (inputBindingService == null)
            throw new ArgumentNullException(nameof(inputBindingService));
        if (updateSystems == null)
            throw new ArgumentNullException(nameof(updateSystems));

        // If shader services aren't available, return empty bundle
        if (context.ShaderService == null || context.ShaderParameterValidator == null)
        {
            context.Logger.Warning(
                "Shader services not available, shader systems will be disabled"
            );
            return new ShaderSystems(
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null
            );
        }

        // Create render target manager
        var renderTargetManager = new RenderTargetManager(
            context.GraphicsDevice,
            LoggerFactory.CreateLogger<RenderTargetManager>()
        );

        // Create shader manager (core component)
        var shaderManager = new ShaderManager(
            context.World,
            context.ShaderService,
            context.ShaderParameterValidator,
            context.GraphicsDevice,
            LoggerFactory.CreateLogger<ShaderManager>()
        );

        // Create shader renderer
        var shaderRenderer = new ShaderRendererSystem(
            LoggerFactory.CreateLogger<ShaderRendererSystem>()
        );

        // Create shader parameter animation system
        var parameterAnimation = new ShaderParameterAnimationSystem(
            context.World,
            shaderManager,
            LoggerFactory.CreateLogger<ShaderParameterAnimationSystem>()
        );
        updateSystems.Add(parameterAnimation);

        // Create shader template system
        var templateSystem = new ShaderTemplateSystem(
            context.World,
            context.ModManager,
            LoggerFactory.CreateLogger<ShaderTemplateSystem>()
        );

        // Create shader transition system
        var transitionSystem = new ShaderTransitionSystem(
            context.World,
            shaderManager,
            LoggerFactory.CreateLogger<ShaderTransitionSystem>()
        );
        updateSystems.Add(transitionSystem);

        // Create multi-parameter animation system
        var multiParameterAnimation = new ShaderMultiParameterAnimationSystem(
            context.World,
            shaderManager,
            LoggerFactory.CreateLogger<ShaderMultiParameterAnimationSystem>()
        );
        updateSystems.Add(multiParameterAnimation);

        // Create animation chain system
        var animationChain = new ShaderAnimationChainSystem(
            context.World,
            shaderManager,
            LoggerFactory.CreateLogger<ShaderAnimationChainSystem>()
        );
        updateSystems.Add(animationChain);

        // Create shader region detection system
        var regionDetection = new ShaderRegionDetectionSystem(
            context.World,
            transitionSystem,
            LoggerFactory.CreateLogger<ShaderRegionDetectionSystem>()
        );
        updateSystems.Add(regionDetection);

        // Create shader preset service
        var presetService = new ShaderPresetService(
            context.ModManager.Registry,
            LoggerFactory.CreateLogger<ShaderPresetService>()
        );

        // Create shader parameter timeline system
        var timelineSystem = new ShaderParameterTimelineSystem(
            context.World,
            shaderManager,
            LoggerFactory.CreateLogger<ShaderParameterTimelineSystem>()
        );
        updateSystems.Add(timelineSystem);

        // Create shader cycle system (F4/F5 key handling)
        var shaderCycleSystem = new ShaderCycleSystem(
            context.World,
            inputBindingService,
            shaderManager,
            context.GraphicsDevice,
            context.ModManager,
            context.ShaderService,
            playerSystem,
            timelineSystem,
            LoggerFactory.CreateLogger<ShaderCycleSystem>()
        );
        updateSystems.Add(shaderCycleSystem);

        context.Logger.Debug("Shader systems created successfully");

        return new ShaderSystems(
            shaderManager,
            shaderRenderer,
            renderTargetManager,
            parameterAnimation,
            transitionSystem,
            multiParameterAnimation,
            animationChain,
            regionDetection,
            templateSystem,
            presetService,
            timelineSystem,
            shaderCycleSystem
        );
    }
}
