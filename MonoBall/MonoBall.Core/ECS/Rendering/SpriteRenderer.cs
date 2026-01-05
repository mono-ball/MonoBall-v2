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
/// <remarks>
///     <para>
///         Character sprites (2-tile tall, 16x32) use feet-based positioning following oldmonoball's pattern:
///     </para>
///     <list type="bullet">
///         <item>Grid Y represents the FEET tile (where the character stands)</item>
///         <item>PixelY is the TOP of the feet tile</item>
///         <item>Rendering draws at PixelY + TileHeight with bottom-left origin</item>
///         <item>This makes the sprite draw UPWARD from the feet position</item>
///     </list>
/// </remarks>
internal sealed class SpriteRenderer : ISpriteRenderer
{
    private readonly ICameraService _cameraService;
    private readonly GraphicsDevice _graphicsDevice;
    private readonly ILogger _logger;
    private readonly IResourceManager _resourceManager;
    private readonly IShaderService? _shaderService;

    /// <summary>
    ///     Initializes a new instance of the SpriteRenderer.
    /// </summary>
    /// <param name="graphicsDevice">The graphics device.</param>
    /// <param name="resourceManager">The resource manager for loading textures.</param>
    /// <param name="cameraService">The camera service for getting tile dimensions.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="shaderService">Optional shader service for per-entity shaders.</param>
    public SpriteRenderer(
        GraphicsDevice graphicsDevice,
        IResourceManager resourceManager,
        ICameraService cameraService,
        ILogger logger,
        IShaderService? shaderService = null
    )
    {
        _graphicsDevice = graphicsDevice ?? throw new ArgumentNullException(nameof(graphicsDevice));
        _resourceManager =
            resourceManager ?? throw new ArgumentNullException(nameof(resourceManager));
        _cameraService = cameraService ?? throw new ArgumentNullException(nameof(cameraService));
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

        // Get tile height for feet-based positioning calculation
        var camera = _cameraService.GetActiveCamera();
        var tileHeight = camera?.TileHeight ?? 16;

        // Determine if this is a multi-tile sprite (taller than one tile)
        // Multi-tile sprites use feet-based positioning (oldmonoball pattern):
        // - Draw position Y = PixelY + TileHeight (bottom of feet tile)
        // - Origin = (0, frameHeight) - bottom-left anchor
        // - This makes the sprite draw UPWARD from the feet position
        Vector2 drawPosition;
        Vector2 origin;

        if (frameRect.Height > tileHeight)
        {
            // Multi-tile sprite: use feet-based positioning
            // PixelY is top of feet tile, add TileHeight to get bottom of feet tile
            drawPosition = new Vector2(pos.PixelX, pos.PixelY + tileHeight);
            origin = new Vector2(0, frameRect.Height);
        }
        else
        {
            // Single-tile sprite: use standard top-left positioning
            drawPosition = pos.Position;
            origin = Vector2.Zero;
        }

        // Draw the sprite
        spriteBatch.Draw(
            spriteTexture,
            drawPosition,
            frameRect,
            color,
            0.0f,
            origin,
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
