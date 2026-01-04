using System;
using System.Collections.Generic;
using Arch.Core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoBall.Core.ECS.Components;
using MonoBall.Core.ECS.Services;
using MonoBall.Core.Rendering;
using MonoBall.Core.Resources;
using Serilog;

namespace MonoBall.Core.ECS.Rendering;

/// <summary>
///     Implementation of ISpriteRenderer that renders sprites.
///     Extracted from SpriteRendererSystem for use with ElevationRendererSystem.
/// </summary>
internal sealed class SpriteRenderer : ISpriteRenderer
{
    private readonly GraphicsDevice _graphicsDevice;
    private readonly ILogger _logger;
    private readonly IResourceManager _resourceManager;
    private readonly IShaderService? _shaderService;

    /// <summary>
    ///     Initializes a new instance of the SpriteRenderer.
    /// </summary>
    /// <param name="graphicsDevice">The graphics device.</param>
    /// <param name="resourceManager">The resource manager for loading textures.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="shaderService">Optional shader service for per-entity shaders.</param>
    public SpriteRenderer(
        GraphicsDevice graphicsDevice,
        IResourceManager resourceManager,
        ILogger logger,
        IShaderService? shaderService = null
    )
    {
        _graphicsDevice = graphicsDevice ?? throw new ArgumentNullException(nameof(graphicsDevice));
        _resourceManager =
            resourceManager ?? throw new ArgumentNullException(nameof(resourceManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _shaderService = shaderService;
    }

    /// <inheritdoc />
    public void RenderSprite(
        World world,
        Entity entity,
        SpriteComponent sprite,
        PositionComponent pos,
        RenderableComponent render,
        SpriteBatch spriteBatch
    )
    {
        if (!render.IsVisible)
            return;

        // Get sprite texture
        Texture2D spriteTexture;
        try
        {
            spriteTexture = _resourceManager.LoadTexture(sprite.SpriteId);
        }
        catch (Exception ex)
        {
            _logger.Warning(
                ex,
                "SpriteRenderer.RenderSprite: Failed to get sprite texture for {SpriteId}",
                sprite.SpriteId
            );
            return;
        }

        // Get frame rectangle directly from SpriteComponent
        Rectangle frameRect;
        try
        {
            frameRect = _resourceManager.GetSpriteFrameRectangle(
                sprite.SpriteId,
                sprite.FrameIndex
            );
        }
        catch (Exception ex)
        {
            _logger.Warning(
                ex,
                "SpriteRenderer.RenderSprite: Failed to get frame rectangle for sprite {SpriteId}, frame {FrameIndex}",
                sprite.SpriteId,
                sprite.FrameIndex
            );
            return;
        }

        // Calculate color with opacity
        var color = Color.White * render.Opacity;

        // Determine sprite effects (can combine horizontal and vertical flips)
        var spriteEffects = SpriteEffects.None;
        if (sprite.FlipHorizontal)
            spriteEffects |= SpriteEffects.FlipHorizontally;
        if (sprite.FlipVertical)
            spriteEffects |= SpriteEffects.FlipVertically;

        // Draw the sprite
        spriteBatch.Draw(
            spriteTexture,
            pos.Position,
            frameRect,
            color,
            0.0f,
            Vector2.Zero,
            1.0f,
            spriteEffects,
            0.0f
        );
    }

    /// <inheritdoc />
    public Effect? GetEntityShader(World world, Entity entity)
    {
        if (_shaderService == null)
            return null;

        if (!world.Has<ShaderComponent>(entity))
            return null;

        ref var shaderComp = ref world.Get<ShaderComponent>(entity);
        if (!shaderComp.IsEnabled)
            return null;

        // Check if shader exists first
        if (!_shaderService.HasShader(shaderComp.ShaderId))
        {
            _logger.Warning(
                "Per-entity shader {ShaderId} not found, skipping",
                shaderComp.ShaderId
            );
            return null;
        }

        // Load shader
        Effect shader;
        try
        {
            shader = _shaderService.GetShader(shaderComp.ShaderId);
        }
        catch (Exception ex)
        {
            _logger.Warning(
                ex,
                "Failed to load per-entity shader {ShaderId}, skipping",
                shaderComp.ShaderId
            );
            return null;
        }

        // Ensure CurrentTechnique is set
        ShaderParameterApplier.EnsureCurrentTechnique(shader, _logger);

        // Automatically set ScreenSize parameter if the shader has it
        try
        {
            var screenSizeParam = shader.Parameters["ScreenSize"];
            if (
                screenSizeParam != null
                && screenSizeParam.ParameterClass == EffectParameterClass.Vector
                && screenSizeParam.ColumnCount == 2
            )
            {
                var viewport = _graphicsDevice.Viewport;
                var screenSize = new Vector2(viewport.Width, viewport.Height);
                screenSizeParam.SetValue(screenSize);
            }
        }
        catch (KeyNotFoundException)
        {
            // ScreenSize parameter doesn't exist - that's fine
        }
        catch (Exception ex)
        {
            _logger.Debug(
                ex,
                "Failed to set ScreenSize parameter automatically for per-entity shader"
            );
        }

        if (shaderComp.Parameters != null)
            ShaderParameterApplier.ApplyParameters(shader, shaderComp.Parameters, _logger);

        return shader;
    }
}
