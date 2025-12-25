using System;
using System.Collections.Generic;
using Arch.Core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoBall.Core.ECS.Components;
using MonoBall.Core.Rendering;
using Serilog;

namespace MonoBall.Core.ECS.Systems
{
    /// <summary>
    /// System that handles shader application logic, separating concerns from rendering systems.
    /// Applies shader stacks with blend modes to render targets.
    /// </summary>
    public class ShaderRendererSystem
    {
        private readonly ILogger _logger;

        /// <summary>
        /// Initializes a new instance of the ShaderRendererSystem.
        /// </summary>
        /// <param name="logger">The logger for logging operations.</param>
        public ShaderRendererSystem(ILogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Applies a shader stack to a render target with blend modes.
        /// Each shader processes the output of the previous shader.
        /// </summary>
        /// <param name="source">The source render target (or texture) to apply shaders to.</param>
        /// <param name="target">The target render target (null = back buffer).</param>
        /// <param name="shaderStack">The shader stack to apply (sorted by RenderOrder).</param>
        /// <param name="spriteBatch">The sprite batch for rendering.</param>
        /// <param name="graphicsDevice">The graphics device.</param>
        /// <param name="renderTargetManager">The render target manager for intermediate targets.</param>
        public void ApplyShaderStack(
            RenderTarget2D source,
            RenderTarget2D? target,
            IReadOnlyList<(Effect effect, ShaderBlendMode blendMode, Entity entity)> shaderStack,
            SpriteBatch spriteBatch,
            GraphicsDevice graphicsDevice,
            RenderTargetManager? renderTargetManager = null
        )
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            if (spriteBatch == null)
                throw new ArgumentNullException(nameof(spriteBatch));
            if (graphicsDevice == null)
                throw new ArgumentNullException(nameof(graphicsDevice));
            if (shaderStack == null || shaderStack.Count == 0)
            {
                // No shaders - just render source to target
                RenderTextureToTarget(source, target, spriteBatch, graphicsDevice);
                return;
            }

            // For single shader with Replace blend, render directly
            if (shaderStack.Count == 1 && shaderStack[0].blendMode == ShaderBlendMode.Replace)
            {
                RenderWithShader(
                    source,
                    target,
                    shaderStack[0].effect,
                    ShaderBlendMode.Replace,
                    null,
                    spriteBatch,
                    graphicsDevice
                );
                return;
            }

            // Multiple shaders or blend modes - use render target chain
            RenderTarget2D? currentSource = source;
            RenderTarget2D? previousOutput = null; // Track previous output for blend modes

            for (int i = 0; i < shaderStack.Count; i++)
            {
                var (effect, blendMode, entity) = shaderStack[i];
                bool isLast = i == shaderStack.Count - 1;
                RenderTarget2D? nextTarget = null;

                // Determine target for this pass
                if (isLast)
                {
                    // Last shader - render to final target (or back buffer)
                    nextTarget = target;
                }
                else
                {
                    // Intermediate pass - need render target
                    if (renderTargetManager == null)
                    {
                        throw new InvalidOperationException(
                            "RenderTargetManager is required for multiple shader passes."
                        );
                    }

                    // Get or create intermediate render target
                    nextTarget = renderTargetManager.GetOrCreateRenderTarget(i + 1);
                    if (nextTarget == null)
                    {
                        throw new InvalidOperationException(
                            $"Failed to create intermediate render target for shader pass {i + 1}."
                        );
                    }
                }

                // Render with shader (pass previous output for blend modes)
                RenderWithShader(
                    currentSource,
                    nextTarget,
                    effect,
                    blendMode,
                    previousOutput,
                    spriteBatch,
                    graphicsDevice
                );

                // Update for next pass
                if (!isLast && nextTarget != null)
                {
                    // previousOutput is the OUTPUT of the current shader (for next shader's blend mode)
                    previousOutput = nextTarget;
                    // currentSource is the INPUT for the next shader (same as previousOutput for first iteration after first shader)
                    currentSource = nextTarget;
                }
            }
        }

        /// <summary>
        /// Renders a texture to a render target with a shader and blend mode.
        /// </summary>
        /// <param name="source">The source texture.</param>
        /// <param name="target">The target render target (null = back buffer).</param>
        /// <param name="shader">The shader effect to apply.</param>
        /// <param name="blendMode">The blend mode to use.</param>
        /// <param name="previousOutput">The previous shader output (for blend modes, null for first shader).</param>
        /// <param name="spriteBatch">The sprite batch for rendering.</param>
        /// <param name="graphicsDevice">The graphics device.</param>
        private void RenderWithShader(
            RenderTarget2D source,
            RenderTarget2D? target,
            Effect shader,
            ShaderBlendMode blendMode,
            RenderTarget2D? previousOutput,
            SpriteBatch spriteBatch,
            GraphicsDevice graphicsDevice
        )
        {
            if (shader == null)
                throw new ArgumentNullException(nameof(shader));

            // Ensure CurrentTechnique is set
            ShaderParameterApplier.EnsureCurrentTechnique(shader, _logger);

            // Configure shader for blend mode (pass previous output if available)
            if (blendMode != ShaderBlendMode.Replace && previousOutput != null)
            {
                ApplyBlendMode(blendMode, shader, previousOutput);
            }

            // Get and cache current render target
            var renderTargets = graphicsDevice.GetRenderTargets();
            RenderTarget2D? previousTarget =
                renderTargets.Length > 0 ? renderTargets[0].RenderTarget as RenderTarget2D : null;

            try
            {
                graphicsDevice.SetRenderTarget(target);
                // Clear render target to prevent visual artifacts
                graphicsDevice.Clear(Color.Transparent);

                // Render source texture with shader
                spriteBatch.Begin(
                    SpriteSortMode.Immediate,
                    BlendState.Opaque, // Use Opaque for shader-based blending
                    SamplerState.LinearClamp,
                    DepthStencilState.None,
                    RasterizerState.CullCounterClockwise,
                    shader,
                    Matrix.Identity
                );

                spriteBatch.Draw(source, Vector2.Zero, Color.White);
                spriteBatch.End();
            }
            finally
            {
                // Restore previous render target
                graphicsDevice.SetRenderTarget(previousTarget);
            }
        }

        /// <summary>
        /// Renders a texture to a render target without a shader.
        /// </summary>
        /// <param name="source">The source texture.</param>
        /// <param name="target">The target render target (null = back buffer).</param>
        /// <param name="spriteBatch">The sprite batch for rendering.</param>
        /// <param name="graphicsDevice">The graphics device.</param>
        private void RenderTextureToTarget(
            RenderTarget2D source,
            RenderTarget2D? target,
            SpriteBatch spriteBatch,
            GraphicsDevice graphicsDevice
        )
        {
            // Get and cache current render target
            var renderTargets = graphicsDevice.GetRenderTargets();
            RenderTarget2D? previousTarget =
                renderTargets.Length > 0 ? renderTargets[0].RenderTarget as RenderTarget2D : null;

            try
            {
                graphicsDevice.SetRenderTarget(target);
                // Clear render target to prevent visual artifacts
                graphicsDevice.Clear(Color.Transparent);

                spriteBatch.Begin(
                    SpriteSortMode.Deferred,
                    BlendState.Opaque,
                    SamplerState.LinearClamp,
                    DepthStencilState.None,
                    RasterizerState.CullCounterClockwise,
                    null,
                    Matrix.Identity
                );

                spriteBatch.Draw(source, Vector2.Zero, Color.White);
                spriteBatch.End();
            }
            finally
            {
                graphicsDevice.SetRenderTarget(previousTarget);
            }
        }

        /// <summary>
        /// Configures a shader for a specific blend mode.
        /// Sets shader parameters to enable blend mode functionality.
        /// </summary>
        /// <param name="blendMode">The blend mode to apply.</param>
        /// <param name="shader">The shader effect.</param>
        /// <param name="previousOutput">The previous shader output texture (for blend modes).</param>
        /// <summary>
        /// Configures a shader for a specific blend mode.
        /// Sets shader parameters to enable blend mode functionality.
        /// </summary>
        /// <param name="blendMode">The blend mode to apply.</param>
        /// <param name="shader">The shader effect.</param>
        /// <param name="previousOutput">The previous shader output texture (for blend modes).</param>
        /// <returns>True if blend mode was successfully applied, false if shader doesn't support it.</returns>
        private bool ApplyBlendMode(
            ShaderBlendMode blendMode,
            Effect shader,
            RenderTarget2D previousOutput
        )
        {
            if (blendMode == ShaderBlendMode.Replace)
            {
                // Replace mode - no special configuration needed
                return true;
            }

            // For blend modes, pass previous output as texture parameter
            // Shader must implement blend mode logic
            try
            {
                bool blendModeSet = false;
                bool textureSet = false;

                // Try to set blend mode parameter
                var blendModeParam = shader.Parameters["BlendMode"];
                if (blendModeParam != null)
                {
                    blendModeParam.SetValue((int)blendMode);
                    blendModeSet = true;
                }

                // Try to set previous output texture
                var previousTextureParam = shader.Parameters["PreviousTexture"];
                if (previousTextureParam != null && previousOutput != null)
                {
                    previousTextureParam.SetValue(previousOutput);
                    textureSet = true;
                }

                if (!blendModeSet || !textureSet)
                {
                    _logger.Warning(
                        "Shader does not fully support blend mode {BlendMode}. Missing parameters: BlendMode={HasBlendMode}, PreviousTexture={HasTexture}",
                        blendMode,
                        blendModeSet,
                        textureSet
                    );
                    return false;
                }

                return true;
            }
            catch (KeyNotFoundException)
            {
                // Parameter doesn't exist - expected
                _logger.Warning(
                    "Shader does not support blend mode {BlendMode}. Required parameters not found.",
                    blendMode
                );
                return false;
            }
            catch (Exception ex)
            {
                // Unexpected error - fail fast per .cursorrules
                throw new InvalidOperationException(
                    $"Failed to apply blend mode {blendMode} to shader: {ex.Message}",
                    ex
                );
            }
        }
    }
}
