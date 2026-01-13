using System;
using Arch.Core;
using Arch.System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoBall.Core.ECS;
using MonoBall.Core.ECS.Components;
using MonoBall.Core.ECS.Services;
using MonoBall.Core.ECS.Systems;
using MonoBall.Core.Rendering;
using MonoBall.Core.Scenes;
using MonoBall.Core.Scenes.Components;
using MonoBall.Core.Scenes.Systems;
using Serilog;

namespace MonoBall.Core.Scenes.Systems;

/// <summary>
///     System that handles update and rendering for GameScene entities.
///     Queries for GameSceneComponent entities and processes them.
/// </summary>
public class GameSceneSystem : BaseSystem<World, float>, IPrioritizedSystem, ISceneSystem
{
    // Cached query descriptions to avoid allocations in hot paths
    private readonly QueryDescription _gameScenesQuery = new QueryDescription().WithAll<
        SceneComponent,
        GameSceneComponent
    >();

    private readonly ICameraService _cameraService;
    private readonly ElevationRendererSystem _elevationRendererSystem;
    private readonly GraphicsDevice _graphicsDevice;
    private readonly ILogger _logger;
    private readonly IRenderTargetManager? _renderTargetManager;
    private readonly IShaderManager? _shaderManagerSystem;
    private readonly IShaderRenderer? _shaderRendererSystem;
    private readonly SpriteBatch _spriteBatch;

    /// <summary>
    ///     Initializes a new instance of the GameSceneSystem.
    /// </summary>
    /// <param name="world">The ECS world.</param>
    /// <param name="graphicsDevice">The graphics device.</param>
    /// <param name="spriteBatch">The sprite batch for rendering.</param>
    /// <param name="elevationRendererSystem">The elevation-based renderer system.</param>
    /// <param name="cameraService">The camera service for camera queries.</param>
    /// <param name="shaderManagerSystem">The shader manager system (optional).</param>
    /// <param name="shaderRendererSystem">The shader renderer system (optional).</param>
    /// <param name="renderTargetManager">The render target manager (optional).</param>
    /// <param name="logger">The logger for logging operations.</param>
    public GameSceneSystem(
        World world,
        GraphicsDevice graphicsDevice,
        SpriteBatch spriteBatch,
        ElevationRendererSystem elevationRendererSystem,
        ICameraService cameraService,
        IShaderManager? shaderManagerSystem = null,
        IShaderRenderer? shaderRendererSystem = null,
        IRenderTargetManager? renderTargetManager = null,
        ILogger? logger = null
    )
        : base(world)
    {
        _graphicsDevice = graphicsDevice ?? throw new ArgumentNullException(nameof(graphicsDevice));
        _spriteBatch = spriteBatch ?? throw new ArgumentNullException(nameof(spriteBatch));
        _elevationRendererSystem =
            elevationRendererSystem
            ?? throw new ArgumentNullException(nameof(elevationRendererSystem));
        _cameraService = cameraService ?? throw new ArgumentNullException(nameof(cameraService));
        _shaderManagerSystem = shaderManagerSystem;
        _shaderRendererSystem = shaderRendererSystem;
        _renderTargetManager = renderTargetManager;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    ///     Gets the execution priority for this system.
    /// </summary>
    public int Priority => SystemPriority.GameScene;

    /// <summary>
    ///     Updates a specific game scene entity.
    ///     Implements ISceneSystem interface.
    /// </summary>
    /// <param name="sceneEntity">The scene entity to update.</param>
    /// <param name="deltaTime">The elapsed time since last update.</param>
    public void Update(Entity sceneEntity, float deltaTime)
    {
        // Game scenes typically don't need per-scene updates
        // This method exists to satisfy ISceneSystem interface
    }

    /// <summary>
    ///     Performs internal processing for game scenes.
    ///     Implements ISceneSystem interface.
    /// </summary>
    /// <param name="deltaTime">The elapsed time since last update.</param>
    public void ProcessInternal(float deltaTime)
    {
        // Game scenes don't need internal processing
    }

    /// <summary>
    ///     Renders a single game scene. Called by SceneSystem (coordinator) for a single scene.
    /// </summary>
    /// <param name="sceneEntity">The scene entity to render.</param>
    /// <param name="gameTime">The game time.</param>
    /// <param name="renderContext">The render context with prepared SpriteBatch and camera. Required.</param>
    /// <exception cref="ArgumentNullException">Thrown when renderContext is null.</exception>
    public void RenderScene(Entity sceneEntity, GameTime gameTime, IRenderContext renderContext)
    {
        if (renderContext == null)
            throw new ArgumentNullException(nameof(renderContext));

        // Validate entity is alive before accessing components
        if (!World.IsAlive(sceneEntity))
            throw new ArgumentException($"Scene entity {sceneEntity.Id} is not alive.", nameof(sceneEntity));

        // Verify this is actually a game scene
        if (!World.Has<GameSceneComponent>(sceneEntity))
            return;

        ref var scene = ref World.Get<SceneComponent>(sceneEntity);
        if (!scene.IsActive)
            return;

        // Render content (SpriteBatch already begun, viewport already set)
        // ElevationRendererSystem receives renderContext, doesn't manage its own batch
        _elevationRendererSystem.Render(gameTime, sceneEntity, renderContext);
        // No state management needed - coordinator handles it
    }

    /// <summary>
    ///     Updates active, unpaused game scenes.
    /// </summary>
    /// <param name="deltaTime">The elapsed time since last update.</param>
    public override void Update(in float deltaTime)
    {
        // Query for active, unpaused game scenes
        World.Query(
            in _gameScenesQuery,
            (Entity e, ref SceneComponent scene) =>
            {
                if (scene.IsActive && !scene.IsPaused && !scene.BlocksUpdate)
                {
                    // Game scenes typically don't need per-frame updates
                    // But if they do, add logic here
                }
            }
        );
    }


}
