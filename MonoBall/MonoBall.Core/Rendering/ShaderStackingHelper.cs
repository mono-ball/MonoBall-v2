using System.Collections.Generic;
using Arch.Core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoBall.Core.ECS.Components;
using MonoBall.Core.ECS.Services;
using MonoBall.Core.ECS.Systems;
using Serilog;

namespace MonoBall.Core.Rendering;

/// <summary>
///     Helper class for shader stacking operations (DRY - shared logic for MapRendererSystem and SpriteRendererSystem).
/// </summary>
public static class ShaderStackingHelper
{
    /// <summary>
    ///     Determines if shader stacking is needed based on shader stack configuration.
    /// </summary>
    /// <param name="shaderStack">The shader stack to check.</param>
    /// <returns>True if shader stacking is needed, false otherwise.</returns>
    public static bool NeedsShaderStacking(
        IReadOnlyList<(Effect effect, ShaderBlendMode blendMode, Entity entity)>? shaderStack
    )
    {
        return shaderStack != null
            && shaderStack.Count > 0
            && (shaderStack.Count > 1 || shaderStack[0].blendMode != ShaderBlendMode.Replace);
    }

    /// <summary>
    ///     Validates that shader stacking dependencies are available.
    ///     Logs warning and returns false if dependencies are missing.
    /// </summary>
    /// <param name="shaderRendererSystem">The shader renderer system (optional).</param>
    /// <param name="renderTargetManager">The render target manager (optional).</param>
    /// <param name="logger">The logger for warning messages.</param>
    /// <param name="systemName">The name of the system (for logging context).</param>
    /// <returns>True if dependencies are available, false otherwise.</returns>
    public static bool ValidateShaderStackingDependencies(
        ShaderRendererSystem? shaderRendererSystem,
        RenderTargetManager? renderTargetManager,
        ILogger logger,
        string systemName
    )
    {
        if (shaderRendererSystem == null || renderTargetManager == null)
        {
            logger.Warning(
                "{SystemName}: Shader stacking requested but ShaderRendererSystem or RenderTargetManager not available. Falling back to single shader.",
                systemName
            );
            return false;
        }

        return true;
    }

    /// <summary>
    ///     Gets or creates a render target for shader stacking.
    ///     Checks if a render target is already set, otherwise creates one using the provided index.
    /// </summary>
    /// <param name="graphicsDevice">The graphics device.</param>
    /// <param name="renderTargetManager">The render target manager.</param>
    /// <param name="renderTargetIndex">The index to use when creating a new render target.</param>
    /// <param name="logger">The logger for warning messages.</param>
    /// <param name="systemName">The name of the system (for logging context).</param>
    /// <returns>The render target to use, or null if creation failed.</returns>
    public static RenderTarget2D? GetOrCreateRenderTargetForStacking(
        GraphicsDevice graphicsDevice,
        RenderTargetManager renderTargetManager,
        int renderTargetIndex,
        ILogger logger,
        string systemName
    )
    {
        // Check if render target is already set (e.g., by SceneRendererSystem for post-processing)
        var currentRenderTargets = graphicsDevice.GetRenderTargets();
        var renderTarget =
            currentRenderTargets.Length > 0
                ? currentRenderTargets[0].RenderTarget as RenderTarget2D
                : null;

        // If no render target is set, create one for shader stacking
        if (renderTarget == null)
        {
            renderTarget = renderTargetManager.GetOrCreateRenderTarget(renderTargetIndex);
            if (renderTarget == null)
            {
                logger.Warning(
                    "{SystemName}: Failed to create render target for shader stacking. Falling back to direct rendering.",
                    systemName
                );
                return null;
            }
        }

        return renderTarget;
    }

    /// <summary>
    ///     Sets up the render target for shader stacking, clearing it if needed.
    ///     Only sets the render target if it's different from the current one (avoids unnecessary state changes).
    /// </summary>
    /// <param name="graphicsDevice">The graphics device.</param>
    /// <param name="renderTarget">The render target to set.</param>
    /// <returns>True if the render target was set (different from previous), false if it was already set.</returns>
    public static bool SetupRenderTarget(GraphicsDevice graphicsDevice, RenderTarget2D renderTarget)
    {
        var renderTargets = graphicsDevice.GetRenderTargets();
        var previousTarget =
            renderTargets.Length > 0 ? renderTargets[0].RenderTarget as RenderTarget2D : null;

        // Only set render target if it's different from current (avoid unnecessary state changes)
        var needToSetTarget = previousTarget != renderTarget;
        if (needToSetTarget)
        {
            graphicsDevice.SetRenderTarget(renderTarget);
            graphicsDevice.Clear(Color.Transparent);
        }

        return needToSetTarget;
    }
}
