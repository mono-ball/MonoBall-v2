using System.Collections.Generic;
using Arch.Core;
using Microsoft.Xna.Framework.Graphics;
using MonoBall.Core.ECS.Components;
using MonoBall.Core.Rendering;

namespace MonoBall.Core.ECS.Systems;

/// <summary>
///     Interface for shader rendering operations.
///     Applies shader stacks with blend modes to render targets.
/// </summary>
public interface IShaderRenderer
{
    /// <summary>
    ///     Applies a shader stack to a render target with blend modes.
    ///     Each shader processes the output of the previous shader.
    /// </summary>
    /// <param name="source">The source render target (or texture) to apply shaders to.</param>
    /// <param name="target">The target render target (null = back buffer).</param>
    /// <param name="shaderStack">The shader stack to apply (sorted by RenderOrder).</param>
    /// <param name="spriteBatch">The sprite batch for rendering.</param>
    /// <param name="graphicsDevice">The graphics device.</param>
    /// <param name="renderTargetManager">The render target manager for intermediate targets.</param>
    void ApplyShaderStack(
        RenderTarget2D source,
        RenderTarget2D? target,
        IReadOnlyList<(Effect effect, ShaderBlendMode blendMode, Entity entity)> shaderStack,
        SpriteBatch spriteBatch,
        GraphicsDevice graphicsDevice,
        IRenderTargetManager? renderTargetManager = null
    );
}
