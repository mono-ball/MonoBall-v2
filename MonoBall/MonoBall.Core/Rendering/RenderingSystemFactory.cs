using System;
using MonoBall.Core.ECS;
using MonoBall.Core.ECS.Rendering;
using MonoBall.Core.ECS.Services;
using MonoBall.Core.ECS.Systems;
using MonoBall.Core.Logging;

namespace MonoBall.Core.Rendering;

/// <summary>
///     Factory for creating rendering systems.
///     Extracts render system creation logic from SystemManager.
/// </summary>
public sealed class RenderingSystemFactory : IRenderingSystemFactory
{
    /// <inheritdoc />
    public IRenderingSystems Create(
        SystemCreationContext context,
        ICameraService cameraService,
        IActiveMapFilterService activeMapFilterService,
        IShaderSystems? shaderSystems
    )
    {
        if (context == null)
            throw new ArgumentNullException(nameof(context));
        if (cameraService == null)
            throw new ArgumentNullException(nameof(cameraService));
        if (activeMapFilterService == null)
            throw new ArgumentNullException(nameof(activeMapFilterService));

        if (context.SpriteBatch == null)
            throw new InvalidOperationException(
                "SpriteBatch is null. Ensure Initialize() was called with a valid SpriteBatch."
            );

        // Create map border renderer system
        var mapBorderRendererSystem = new MapBorderRendererSystem(
            context.World,
            context.GraphicsDevice,
            context.ResourceManager,
            cameraService,
            activeMapFilterService,
            LoggerFactory.CreateLogger<MapBorderRendererSystem>()
        );
        mapBorderRendererSystem.SetSpriteBatch(context.SpriteBatch);

        // Create helper renderers for ElevationRendererSystem
        var tileChunkRenderer = new TileChunkRenderer(
            context.ResourceManager,
            context.ModManager.Registry,
            LoggerFactory.CreateLogger<TileChunkRenderer>()
        );

        var spriteRenderer = new SpriteRenderer(
            context.GraphicsDevice,
            context.ResourceManager,
            cameraService,
            LoggerFactory.CreateLogger<SpriteRenderer>(),
            context.ShaderService
        );

        // Create unified elevation-based renderer
        var elevationRendererSystem = new ElevationRendererSystem(
            context.World,
            context.GraphicsDevice,
            context.ResourceManager,
            cameraService,
            tileChunkRenderer,
            spriteRenderer,
            context.SpriteBatch,
            LoggerFactory.CreateLogger<ElevationRendererSystem>(),
            mapBorderRendererSystem,
            shaderSystems?.ShaderManager,
            shaderSystems?.ShaderRenderer,
            shaderSystems?.RenderTargetManager
        );

        context.Logger.Debug("Rendering systems created successfully");

        return new RenderingSystems(elevationRendererSystem, mapBorderRendererSystem);
    }
}
