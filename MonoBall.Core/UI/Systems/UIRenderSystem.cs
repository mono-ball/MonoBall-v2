using System;
using System.Collections.Generic;
using Arch.Core;
using Arch.Relationships;
using Arch.System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoBall.Core.Constants;
using MonoBall.Core.ECS;
using MonoBall.Core.ECS.Components;
using MonoBall.Core.ECS.Services;
using MonoBall.Core.Rendering;
using MonoBall.Core.Mods;
using MonoBall.Core.Resources;
using MonoBall.Core.Scenes;
using MonoBall.Core.Scenes.Components;
using MonoBall.Core.Scenes.Systems;
using MonoBall.Core.UI.Components;
using MonoBall.Core.UI.Relationships;
using MonoBall.Core.UI.Windows.Backgrounds;
using MonoBall.Core.UI.Windows.Borders;
using Serilog;

namespace MonoBall.Core.UI.Systems;

/// <summary>
///     System that renders UI elements for a specific scene.
///     Implements ISceneSystem to integrate with SceneSystem rendering pipeline.
///     Queries UI entities via Arch.Relationships, renders them in z-order.
/// </summary>
/// <remarks>
///     <para>
///         UIRenderSystem is a helper system that can be called by scene systems (MessageBoxSceneSystem, MapPopupSceneSystem)
///         to render UI entities. It queries UI entities via Arch.Relationships and renders them in z-order.
///     </para>
///     <para>
///         For Phase 1, this system provides the infrastructure. Full rendering implementation will be completed in Phase 3
///         when UI entities are created by scene systems.
///     </para>
/// </remarks>
public class UIRenderSystem : BaseSystem<World, float>, ISceneSystem, IDisposable
{
    private readonly GraphicsDevice _graphicsDevice;
    private readonly SpriteBatch _spriteBatch;
    private readonly IResourceManager _resourceManager;
    private readonly ICameraService _cameraService;
    private readonly IConstantsService _constants;
    private readonly IModManager _modManager;
    private readonly ILogger _logger;

    // Cached queries (created in constructor, never in hot paths)
    private readonly QueryDescription _uiWindowQuery;
    private readonly QueryDescription _uiSpriteQuery;
    private readonly QueryDescription _uiTextQuery;

    // Reusable collection for collecting UI elements to render (avoids allocations in hot path)
    private readonly List<(Entity entity, UIElementComponent ui, int zOrder)> _renderList = new();

    private bool _disposed;

    /// <summary>
    ///     Initializes a new instance of the UIRenderSystem.
    /// </summary>
    /// <param name="world">The ECS world. Required.</param>
    /// <param name="graphicsDevice">The graphics device. Required.</param>
    /// <param name="spriteBatch">The sprite batch for rendering. Required.</param>
    /// <param name="resourceManager">The resource manager for loading textures/fonts. Required.</param>
    /// <param name="cameraService">The camera service for viewport calculations. Required.</param>
    /// <param name="constants">The constants service for reference width. Required.</param>
    /// <param name="modManager">The mod manager for accessing definitions. Required.</param>
    /// <param name="logger">The logger for logging operations. Required.</param>
    /// <exception cref="ArgumentNullException">Thrown if any required parameter is null.</exception>
    public UIRenderSystem(
        World world,
        GraphicsDevice graphicsDevice,
        SpriteBatch spriteBatch,
        IResourceManager resourceManager,
        ICameraService cameraService,
        IConstantsService constants,
        IModManager modManager,
        ILogger logger
    ) : base(world)
    {
        _graphicsDevice = graphicsDevice ?? throw new ArgumentNullException(nameof(graphicsDevice));
        _spriteBatch = spriteBatch ?? throw new ArgumentNullException(nameof(spriteBatch));
        _resourceManager = resourceManager ?? throw new ArgumentNullException(nameof(resourceManager));
        _cameraService = cameraService ?? throw new ArgumentNullException(nameof(cameraService));
        _constants = constants ?? throw new ArgumentNullException(nameof(constants));
        _modManager = modManager ?? throw new ArgumentNullException(nameof(modManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Cache QueryDescription in constructor (never create in Update/Render)
        _uiWindowQuery = new QueryDescription()
            .WithAll<WindowComponent, UIElementComponent, PositionComponent, RenderableComponent>();

        _uiSpriteQuery = new QueryDescription()
            .WithAll<SpriteComponent, UIElementComponent, PositionComponent, RenderableComponent>();

        _uiTextQuery = new QueryDescription()
            .WithAll<UITextComponent, UIElementComponent, PositionComponent, RenderableComponent>();

    }

    /// <summary>
    ///     Updates a specific scene entity.
    ///     Implements ISceneSystem interface.
    /// </summary>
    /// <param name="sceneEntity">The scene entity to update.</param>
    /// <param name="deltaTime">The elapsed time since last update.</param>
    /// <remarks>
    ///     UIRenderSystem is a pure render system - no update logic needed.
    ///     This method exists to satisfy ISceneSystem interface.
    /// </remarks>
    public void Update(Entity sceneEntity, float deltaTime)
    {
        // Pure render system - no update logic
    }

    /// <summary>
    ///     Renders UI elements for a specific scene entity.
    ///     Implements ISceneSystem interface.
    /// </summary>
    /// <param name="sceneEntity">The scene entity to render UI for.</param>
    /// <param name="gameTime">The game time.</param>
    /// <param name="renderContext">The render context with prepared SpriteBatch and camera. Required.</param>
    /// <exception cref="ArgumentNullException">Thrown when renderContext is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown if scene entity is not alive or missing SceneComponent.</exception>
    public void RenderScene(Entity sceneEntity, GameTime gameTime, IRenderContext renderContext)
    {
        if (renderContext == null)
            throw new ArgumentNullException(nameof(renderContext));

        if (!World.IsAlive(sceneEntity))
            throw new InvalidOperationException($"Scene entity {sceneEntity.Id} is not alive.");

        if (!World.Has<SceneComponent>(sceneEntity))
            throw new InvalidOperationException(
                $"Scene entity {sceneEntity.Id} does not have SceneComponent."
            );

        ref var scene = ref World.Get<SceneComponent>(sceneEntity);

        // UX scenes render in screen space (Matrix.Identity), not with camera transform
        // End the coordinator's batch and begin a new one with Matrix.Identity
        renderContext.SpriteBatch.End();
        renderContext.MarkBatchEnded(); // Notify coordinator that we ended the batch

        // Begin new batch for screen space rendering
        renderContext.MarkNewBatchStarted(); // Notify coordinator that we're starting a new batch
        renderContext.SpriteBatch.Begin(
            SpriteSortMode.Deferred,
            BlendState.AlphaBlend,
            SamplerState.PointClamp,
            DepthStencilState.None,
            RasterizerState.CullCounterClockwise,
            null,
            Matrix.Identity // Screen space - no camera transform for UI overlays
        );

        // Calculate viewport scale factor using IRenderContext method
        var gbaReferenceWidth = _constants.Get<int>("ReferenceWidth");
        var currentScale = renderContext.GetViewportScale(gbaReferenceWidth);

        // Render UI elements (use renderContext.SpriteBatch with Matrix.Identity)
        RenderUIElements(sceneEntity, ref scene, renderContext.SpriteBatch, currentScale);
        // Don't End() here - leave batch open for text rendering or coordinator cleanup
        // Note: Coordinator will check IsBatchEnded and skip ending
    }

    /// <summary>
    ///     Performs internal processing that needs to run every frame.
    ///     Implements ISceneSystem interface.
    /// </summary>
    /// <param name="deltaTime">The elapsed time since last update.</param>
    /// <remarks>
    ///     UIRenderSystem is a pure render system - no internal processing needed.
    /// </remarks>
    public void ProcessInternal(float deltaTime)
    {
        // Pure render system - no internal processing
    }

    /// <summary>
    ///     Renders a single UI element based on its component composition.
    /// </summary>
    /// <param name="entity">The UI element entity to render.</param>
    /// <param name="ui">The UI element component.</param>
    /// <param name="scale">The viewport scale factor.</param>
    /// <exception cref="InvalidOperationException">Thrown if entity is missing required components for its element type.</exception>
    private void RenderUIElement(Entity entity, ref UIElementComponent ui, int scale)
    {
        switch (ui.ElementType)
        {
            case UIElementType.Window:
                if (World.Has<WindowComponent>(entity))
                {
                    ref var window = ref World.Get<WindowComponent>(entity);
                    ref var pos = ref World.Get<PositionComponent>(entity);
                    RenderWindow(entity, ref window, ref pos, scale);
                }

                break;

            case UIElementType.Sprite:
                if (
                    World.Has<SpriteComponent>(entity)
                    && World.Has<PositionComponent>(entity)
                    && World.Has<RenderableComponent>(entity)
                )
                {
                    ref var sprite = ref World.Get<SpriteComponent>(entity);
                    ref var pos = ref World.Get<PositionComponent>(entity);
                    ref var render = ref World.Get<RenderableComponent>(entity);
                    RenderUISprite(entity, ref sprite, ref pos, ref render, scale);
                }

                break;

            case UIElementType.Text:
                if (World.Has<UITextComponent>(entity) && World.Has<PositionComponent>(entity))
                {
                    ref var text = ref World.Get<UITextComponent>(entity);
                    ref var pos = ref World.Get<PositionComponent>(entity);
                    RenderUIText(entity, ref text, ref pos, scale);
                }

                break;

            // Border, Background, etc. - handled by window rendering or separate entities
            default:
                _logger.Debug("Unhandled UI element type: {ElementType}", ui.ElementType);
                break;
        }
    }

    /// <summary>
    ///     Renders UI elements for a scene (shared logic for both coordinator and fallback paths).
    /// </summary>
    private void RenderUIElements(
        Entity sceneEntity,
        ref SceneComponent scene,
        SpriteBatch spriteBatch,
        float currentScale
    )
    {
        // Clear reusable collection (avoids allocations in hot path)
        _renderList.Clear();

        // Collect UI elements via relationships
        // Arch.Relationships API: World.GetRelationships<T>(entity) returns Dictionary<Entity, T>
        try
        {
            // Get all UI elements owned by this scene
            var relationships = World.GetRelationships<OwnsUIElement>(sceneEntity);
            foreach (var kvp in relationships)
            {
                var uiElement = kvp.Key;
                
                if (!World.IsAlive(uiElement))
                    continue;

                if (!World.Has<UIElementComponent>(uiElement))
                    continue; // Not a valid UI element

                var ui = World.Get<UIElementComponent>(uiElement);
                var render = World.Get<RenderableComponent>(uiElement);

                if (!render.IsVisible)
                    continue;

                _renderList.Add((uiElement, ui, ui.ZOrder));
            }
        }
        catch (Exception ex)
        {
            // Arch.Relationships API might differ - log and continue
            _logger.Warning(
                ex,
                "Failed to query UI elements via relationships for scene {SceneId}. "
                    + "UI entities may not be created yet (Phase 3). Error: {Error}",
                scene.SceneId,
                ex.Message
            );
            // Continue - this is expected during Phase 1/2 when UI entities don't exist yet
        }

        if (_renderList.Count == 0)
        {
            _logger.Debug("Scene {SceneId} has no UI elements to render", scene.SceneId);
            return; // Not an error - scene might not have UI yet
        }

        // Sort by z-order (lower values render first)
        _renderList.Sort((a, b) => a.zOrder.CompareTo(b.zOrder));

        // Render in z-order
        foreach (var (entity, ui, _) in _renderList)
        {
            // Get fresh component reference for rendering (avoid ref to iteration variable)
            var uiComponent = World.Get<UIElementComponent>(entity);
            RenderUIElement(entity, ref uiComponent, (int)currentScale);
        }
    }

    /// <summary>
    ///     Renders a window UI element (border, background, and child elements).
    /// </summary>
    /// <param name="entity">The window entity.</param>
    /// <param name="window">The window component.</param>
    /// <param name="pos">The position component (in screen space, already scaled).</param>
    /// <param name="scale">The viewport scale factor.</param>
    private void RenderWindow(
        Entity entity,
        ref WindowComponent window,
        ref PositionComponent pos,
        int scale
    )
    {
        // Position is in screen space (already scaled by viewport)
        // Dimensions need to be scaled from GBA reference to screen space
        var interiorX = (int)pos.Position.X;
        var interiorY = (int)pos.Position.Y;
        var interiorWidth = window.InteriorWidth * scale;
        var interiorHeight = window.InteriorHeight * scale;

        // Render border if BorderId is specified
        if (!string.IsNullOrEmpty(window.BorderId))
        {
            try
            {
                var borderDef = _modManager.GetDefinition<PopupOutlineDefinition>(window.BorderId);
                if (borderDef != null)
                {
                    var borderTexture = _resourceManager.LoadTexture(window.BorderId);
                    // Tile size scaled to viewport (screen space)
                    var tileSize = borderDef.TileWidth * scale;
                    var borderRenderer = new MessageBoxDialogueFrameBorderRenderer(
                        borderTexture,
                        borderDef,
                        tileSize,
                        _constants,
                        _logger
                    );
                    borderRenderer.RenderBorder(
                        _spriteBatch,
                        interiorX,
                        interiorY,
                        interiorWidth,
                        interiorHeight
                    );
                }
            }
            catch (Exception ex)
            {
                _logger.Warning(
                    ex,
                    "Failed to render border for window {EntityId}",
                    entity.Id
                );
            }
        }

        // Render background if BackgroundId is specified
        if (!string.IsNullOrEmpty(window.BackgroundId))
        {
            try
            {
                var backgroundDef = _modManager.GetDefinition<PopupBackgroundDefinition>(
                    window.BackgroundId
                );
                if (backgroundDef != null)
                {
                    var backgroundTexture = _resourceManager.LoadTexture(window.BackgroundId);
                    var backgroundRenderer = new BitmapBackgroundRenderer(
                        backgroundTexture,
                        backgroundDef
                    );
                    backgroundRenderer.RenderBackground(
                        _spriteBatch,
                        interiorX,
                        interiorY,
                        interiorWidth,
                        interiorHeight
                    );
                }
            }
            catch (Exception ex)
            {
                _logger.Warning(
                    ex,
                    "Failed to render background for window {EntityId}",
                    entity.Id
                );
            }
        }
        else if (!string.IsNullOrEmpty(window.BorderId))
        {
            // Use tilesheet background (tile 0) if no BackgroundId but BorderId exists
            try
            {
                var tilesheetDef = _modManager.GetDefinition<PopupOutlineDefinition>(
                    window.BorderId
                );
                if (tilesheetDef != null)
                {
                    var tilesheetTexture = _resourceManager.LoadTexture(window.BorderId);
                    // Tile size scaled to viewport (screen space)
                    var tileSize = tilesheetDef.TileWidth * scale;
                    var backgroundRenderer = new TileSheetBackgroundRenderer(
                        tilesheetTexture,
                        tilesheetDef,
                        tileSize,
                        0, // Background tile index
                        _constants
                    );
                    backgroundRenderer.RenderBackground(
                        _spriteBatch,
                        interiorX,
                        interiorY,
                        interiorWidth,
                        interiorHeight
                    );
                }
            }
            catch (Exception ex)
            {
                _logger.Warning(
                    ex,
                    "Failed to render tilesheet background for window {EntityId}",
                    entity.Id
                );
            }
        }

        // Render child elements (content, sprites, text)
        // Get all child elements via ContainsUIElement relationship
        try
        {
            var relationships = World.GetRelationships<ContainsUIElement>(entity);
            foreach (var kvp in relationships)
            {
                var child = kvp.Key;

                if (!World.IsAlive(child) || !World.Has<UIElementComponent>(child))
                    continue;

                var childUi = World.Get<UIElementComponent>(child);
                RenderUIElement(child, ref childUi, scale);
            }
        }
        catch (Exception ex)
        {
            _logger.Debug(
                ex,
                "Failed to query child elements for window {EntityId}",
                entity.Id
            );
        }
    }

    /// <summary>
    ///     Renders a UI sprite element in screen space.
    /// </summary>
    /// <param name="entity">The sprite entity.</param>
    /// <param name="sprite">The sprite component.</param>
    /// <param name="pos">The position component (screen space coordinates, already scaled).</param>
    /// <param name="render">The renderable component.</param>
    /// <param name="scale">The viewport scale factor for scaling the sprite.</param>
    private void RenderUISprite(
        Entity entity,
        ref SpriteComponent sprite,
        ref PositionComponent pos,
        ref RenderableComponent render,
        int scale
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
                "UIRenderSystem.RenderUISprite: Failed to load sprite texture for {SpriteId}",
                sprite.SpriteId
            );
            return;
        }

        // Get frame rectangle (texture coordinates)
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
                "UIRenderSystem.RenderUISprite: Failed to get frame rectangle for sprite {SpriteId}, frame {FrameIndex}",
                sprite.SpriteId,
                sprite.FrameIndex
            );
            return;
        }

        // Calculate color with opacity
        var color = Color.White * render.Opacity;

        // Determine sprite effects
        var spriteEffects = SpriteEffects.None;
        if (sprite.FlipHorizontal)
            spriteEffects |= SpriteEffects.FlipHorizontally;
        if (sprite.FlipVertical)
            spriteEffects |= SpriteEffects.FlipVertically;

        // Position is in screen space (already scaled by viewport)
        var drawPosition = pos.Position;

        // Draw the sprite in screen space, scaled by viewport scale
        // Sprite texture is at 1x (GBA reference), so scale it to match viewport
        _spriteBatch.Draw(
            spriteTexture,
            drawPosition,
            frameRect,
            color,
            0.0f,
            Vector2.Zero, // Top-left origin for UI sprites
            scale, // Scale sprite to match viewport
            spriteEffects,
            0.0f
        );
    }

    /// <summary>
    ///     Renders a UI text element.
    /// </summary>
    /// <param name="entity">The text entity.</param>
    /// <param name="text">The text component.</param>
    /// <param name="pos">The position component (in screen space).</param>
    /// <param name="scale">The viewport scale factor.</param>
    private void RenderUIText(
        Entity entity,
        ref UITextComponent text,
        ref PositionComponent pos,
        int scale
    )
    {
        // TODO: Phase 3 - Implement text rendering
        // For now, this is a placeholder
        _logger.Debug(
            "Rendering text entity {EntityId}: '{Text}' at ({X}, {Y})",
            entity.Id,
            text.Text,
            pos.Position.X,
            pos.Position.Y
        );
    }

    /// <summary>
    ///     Disposes the system and cleans up resources.
    /// </summary>
    public new void Dispose() => Dispose(true);

    /// <summary>
    ///     Protected dispose method following standard dispose pattern.
    /// </summary>
    /// <param name="disposing">True if called from Dispose(), false if called from finalizer.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed && disposing)
        {
            // Clear reusable collections
            _renderList.Clear();
        }

        _disposed = true;
    }
}
