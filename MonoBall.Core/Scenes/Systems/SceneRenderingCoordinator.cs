using System;
using System.Collections.Generic;
using Arch.Core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoBall.Core.ECS.Components;
using MonoBall.Core.ECS.Services;
using MonoBall.Core.ECS.Systems;
using MonoBall.Core.Rendering;
using MonoBall.Core.Scenes.Components;
using Serilog;

namespace MonoBall.Core.Scenes.Systems;

/// <summary>
///     Coordinates rendering state for scene rendering.
///     Manages viewport, render targets, and SpriteBatch lifecycle per scene.
/// </summary>
public class SceneRenderingCoordinator : ISceneRenderingCoordinator
{
    private readonly World _world;
    private readonly GraphicsDevice _graphicsDevice;
    private readonly SpriteBatch _spriteBatch;
    private readonly ICameraService _cameraService;
    private readonly IShaderManager? _shaderManager;
    private readonly IShaderRenderer? _shaderRenderer;
    private readonly IRenderTargetManager? _renderTargetManager;
    private readonly ILogger _logger;

    // Cached collections for shader stacks (reusable to avoid allocations)
    private readonly List<(Effect effect, ShaderBlendMode blendMode, Entity entity)> _shaderStackCache = new();
    private IReadOnlyList<(Effect effect, ShaderBlendMode blendMode, Entity entity)>? _currentShaderStack;

    /// <summary>
    ///     Initializes a new instance of the SceneRenderingCoordinator.
    /// </summary>
    /// <param name="world">The ECS world.</param>
    /// <param name="graphicsDevice">The graphics device for rendering.</param>
    /// <param name="spriteBatch">The sprite batch for rendering.</param>
    /// <param name="cameraService">The camera service for camera queries.</param>
    /// <param name="shaderManager">The shader manager system (optional).</param>
    /// <param name="shaderRenderer">The shader renderer system (optional).</param>
    /// <param name="renderTargetManager">The render target manager (optional).</param>
    /// <param name="logger">The logger for logging operations. Required.</param>
    public SceneRenderingCoordinator(
        World world,
        GraphicsDevice graphicsDevice,
        SpriteBatch spriteBatch,
        ICameraService cameraService,
        ILogger logger,
        IShaderManager? shaderManager = null,
        IShaderRenderer? shaderRenderer = null,
        IRenderTargetManager? renderTargetManager = null
    )
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _graphicsDevice = graphicsDevice ?? throw new ArgumentNullException(nameof(graphicsDevice));
        _spriteBatch = spriteBatch ?? throw new ArgumentNullException(nameof(spriteBatch));
        _cameraService = cameraService ?? throw new ArgumentNullException(nameof(cameraService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _shaderManager = shaderManager;
        _shaderRenderer = shaderRenderer;
        _renderTargetManager = renderTargetManager;
    }

    /// <summary>
    ///     Prepares rendering state for a scene.
    ///     Sets viewport, render target, and begins SpriteBatch with correct transform.
    /// </summary>
    /// <param name="sceneEntity">The scene entity to prepare.</param>
    /// <param name="camera">The camera component for this scene (null for ScreenCamera).</param>
    /// <returns>Render context with prepared state.</returns>
    /// <exception cref="InvalidOperationException">
    ///     Thrown when scene entity does not have SceneComponent.
    /// </exception>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when sceneEntity is invalid or required dependencies are null.
    /// </exception>
    public IRenderContext PrepareScene(Entity sceneEntity, CameraComponent? camera)
    {
        if (!_world.IsAlive(sceneEntity))
            throw new ArgumentException($"Scene entity {sceneEntity.Id} is not alive.", nameof(sceneEntity));

        if (!_world.Has<SceneComponent>(sceneEntity))
            throw new InvalidOperationException($"Scene entity {sceneEntity.Id} missing SceneComponent.");

        ref var scene = ref _world.Get<SceneComponent>(sceneEntity);

        // Save original state
        var savedViewport = _graphicsDevice.Viewport;
        var savedRenderTargets = _graphicsDevice.GetRenderTargets();
        var savedRenderTarget = savedRenderTargets.Length > 0
            ? savedRenderTargets[0].RenderTarget as RenderTarget2D
            : null;

        // Determine render target (for post-processing)
        RenderTarget2D? renderTarget = null;
        var hasPostProcessing = false;

        if (_shaderManager != null)
        {
            _shaderStackCache.Clear();
            _currentShaderStack = null;
            var shaderStack = _shaderManager.GetCombinedLayerShaderStack(sceneEntity);
            if (shaderStack != null && shaderStack.Count > 0)
            {
                _shaderStackCache.AddRange(shaderStack);
                _currentShaderStack = _shaderStackCache; // Direct reference, no allocation
                hasPostProcessing = true;
            }
        }

        if (hasPostProcessing && _renderTargetManager != null)
        {
            renderTarget = _renderTargetManager.GetOrCreateRenderTarget();
            if (renderTarget == null)
            {
                _logger.Warning("Failed to create render target for post-processing. Rendering directly to back buffer.");
            }
        }

        // Set render target
        if (renderTarget != null)
        {
            _graphicsDevice.SetRenderTarget(renderTarget);
            _graphicsDevice.Clear(Color.Transparent);
        }

        // Set viewport based on camera
        if (camera.HasValue && camera.Value.VirtualViewport != Rectangle.Empty)
        {
            _graphicsDevice.Viewport = new Viewport(camera.Value.VirtualViewport);
        }

        // Calculate transform matrix
        Matrix transform = Matrix.Identity;
        if (camera.HasValue)
        {
            transform = camera.Value.GetTransformMatrix();
        }

        // Begin SpriteBatch
        // Defensive check: if a batch is already active (shouldn't happen), end it first
        // This can happen if FinishScene() didn't properly end a batch from a previous scene
        try
        {
            _spriteBatch.Begin(
                SpriteSortMode.Deferred,
                BlendState.AlphaBlend,
                SamplerState.PointClamp,
                DepthStencilState.None,
                RasterizerState.CullCounterClockwise,
                null,
                transform
            );
        }
        catch (InvalidOperationException ex)
        {
            // Batch is already active - this indicates FinishScene() didn't properly end the previous batch
            _logger.Warning(
                ex,
                "SpriteBatch.Begin() failed - batch is already active. " +
                "Attempting to end the active batch and retry. " +
                "This indicates FinishScene() didn't properly end the previous scene's batch."
            );
            try
            {
                // Try to end the active batch and retry
                _spriteBatch.End();
                _spriteBatch.Begin(
                    SpriteSortMode.Deferred,
                    BlendState.AlphaBlend,
                    SamplerState.PointClamp,
                    DepthStencilState.None,
                    RasterizerState.CullCounterClockwise,
                    null,
                    transform
                );
                _logger.Information("Successfully recovered from active batch state.");
            }
            catch (Exception recoveryEx)
            {
                _logger.Error(
                    recoveryEx,
                    "Failed to recover from active batch state. This indicates a serious bug in batch lifecycle management."
                );
                throw new InvalidOperationException(
                    "SpriteBatch is already active and recovery failed. " +
                    "Previous scene's batch was not properly ended. " +
                    "This indicates a bug in SceneRenderingCoordinator or a system not properly managing batch state.",
                    ex
                );
            }
        }

        // Create render context
        return new RenderContext
        {
            SceneEntity = sceneEntity,
            Camera = camera,
            SpriteBatch = _spriteBatch,
            SavedViewport = savedViewport,
            SavedRenderTarget = savedRenderTarget,
            RenderTarget = renderTarget,
            HasPostProcessing = hasPostProcessing,
            ShaderStack = _currentShaderStack, // Direct reference, no allocation
            IsBatchEnded = false, // Batch is active when context is created
            HasNewBatchStarted = false, // No new batch started yet
            IsNewBatchEnded = false // New batch not ended yet (if one is started)
        };
    }

    /// <summary>
    ///     Finishes rendering for a scene.
    ///     Ends SpriteBatch, restores viewport/render target, applies post-processing.
    /// </summary>
    /// <param name="context">The render context from PrepareScene.</param>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when context is null.
    /// </exception>
    public void FinishScene(IRenderContext context)
    {
        if (context == null)
            throw new ArgumentNullException(nameof(context));

        // Use internal interface to access state without downcasting (LSP compliance)
        if (context is not IRenderContextInternal renderContext)
            throw new ArgumentException("Render context must implement IRenderContextInternal.", nameof(context));

        // End SpriteBatch:
        // 1. If batch wasn't ended by a system, end it now
        // 2. If a system ended the batch and started a new one, end that new batch (if not already ended by system)
        if (!context.IsBatchEnded)
        {
            // Coordinator's batch is still active, end it
            _spriteBatch.End();
        }
        else if (context.HasNewBatchStarted && !context.IsNewBatchEnded)
        {
            // System ended coordinator's batch and started a new one
            // The new batch is still active (system didn't end it), so end it now
            _spriteBatch.End();
        }
        // If IsBatchEnded is true but HasNewBatchStarted is false, batch was ended but no new batch started
        // (e.g., for shader changes that don't require a new batch)
        // If IsNewBatchEnded is true, system already ended its new batch, so we skip ending it

        // Apply post-processing if needed
        if (renderContext.HasPostProcessing
            && renderContext.RenderTarget != null
            && renderContext.ShaderStack != null
            && _shaderRenderer != null)
        {
            // Restore render target before applying shaders
            _graphicsDevice.SetRenderTarget(renderContext.SavedRenderTarget);
            if (context.Camera.HasValue
                && context.Camera.Value.VirtualViewport != Rectangle.Empty)
            {
                _graphicsDevice.Viewport = new Viewport(context.Camera.Value.VirtualViewport);
            }

            // Update shader parameters
            _shaderManager?.ForceUpdateCombinedLayerParameters();

            // Apply shader stack
            _shaderRenderer.ApplyShaderStack(
                renderContext.RenderTarget,
                null, // Render to back buffer
                renderContext.ShaderStack,
                _spriteBatch,
                _graphicsDevice,
                _renderTargetManager
            );
        }

        // Restore viewport
        _graphicsDevice.Viewport = renderContext.SavedViewport;

        // Restore render target (if not already restored)
        if (!renderContext.HasPostProcessing)
        {
            _graphicsDevice.SetRenderTarget(renderContext.SavedRenderTarget);
        }
    }
}
