# UI ECS Refactor Design

## Overview

This document describes the design for refactoring the UI system to follow true ECS (Entity Component System) principles. Currently, UI systems like `MessageBoxSceneSystem` violate ECS architecture by implementing custom rendering and animation logic instead of using existing ECS components and systems. This refactor will create a unified, component-based UI architecture that reuses existing sprite/animation components and leverages Arch.Extended Relationships for parent-child hierarchies.

## Problem Statement

### Current Issues

1. **MessageBoxSceneSystem Violates ECS Principles**
   - Custom sprite animation: Down arrow animation manually updates `DownArrowAnimationTime`, calculates frames, and renders directly (lines 1860-2025 in `MessageBoxSceneSystem.cs`)
   - Direct rendering: Creates renderer instances (`MessageBoxDialogueFrameBorderRenderer`, `TileSheetBackgroundRenderer`, `MessageBoxContentRenderer`) and calls them directly instead of using ECS entities
   - Mixed responsibilities: Handles scene lifecycle, text processing, input handling, AND rendering in one system

2. **Inconsistent UI Patterns**
   - `MapPopupSceneSystem` uses `WindowAnimationComponent` for animations (better), but still does direct rendering
   - No unified UI entity model: UI elements aren't consistently represented as entities with components

3. **Missing UI Component Infrastructure**
   - No `UIElementComponent` or `WindowComponent` for UI entities
   - No dedicated UI rendering system that queries UI entities
   - UI sprites (like down arrow) don't use existing `SpriteComponent` + `SpriteAnimationComponent`

4. **Entity Reference Management**
   - `SceneOwnershipComponent` stores `Entity` references in components (can become stale)
   - No efficient way to query "all UI elements belonging to a scene"
   - No hierarchical relationships (window → child elements)

## Design Goals

1. **True ECS Architecture**: UI elements are entities with components, not OOP classes
2. **Component Reuse**: Use existing `SpriteComponent`, `SpriteAnimationComponent`, `PositionComponent`, `RenderableComponent` for UI
3. **Separation of Concerns**: Components = data, Systems = logic, Render Systems = rendering
4. **Event-Driven**: UI interactions via events, not direct method calls
5. **Query-Based Rendering**: Render systems query entities with specific component combinations
6. **Hierarchical UI**: Support parent-child relationships (scene → window → child elements)
7. **Performance**: Efficient queries, batching, culling
8. **Extensibility**: Easy to add new UI element types by composing components

## Architecture Design

### Core Principles

1. **UI as Entities**: All UI elements (windows, sprites, text, borders, backgrounds) are ECS entities
2. **Component Composition**: UI elements are composed of multiple components (position, sprite, animation, visibility, etc.)
3. **System Separation**: 
   - Update systems: Process logic (text state machine, animations)
   - Render systems: Query and render entities
4. **Relationship-Based Hierarchy**: Use Arch.Extended Relationships for parent-child links

### Component Design

#### New Components

**`UIElementComponent`**
```csharp
namespace MonoBall.Core.UI.Components;

/// <summary>
///     Component that identifies an entity as a UI element and stores UI-specific metadata.
///     All UI entities (windows, sprites, text, etc.) should have this component.
/// </summary>
public struct UIElementComponent
{
    /// <summary>
    ///     The type of UI element (Window, Sprite, Text, Border, Background, etc.).
    /// </summary>
    public UIElementType ElementType { get; set; }

    /// <summary>
    ///     The z-order for rendering (higher values render on top).
    ///     Used within the same scene/relationship hierarchy.
    /// </summary>
    public int ZOrder { get; set; }

    /// <summary>
    ///     Whether this element can receive input events.
    /// </summary>
    public bool IsInteractive { get; set; }

    /// <summary>
    ///     Optional element ID for scripting API access.
    /// </summary>
    public string? ElementId { get; set; }
}

/// <summary>
///     Types of UI elements.
/// </summary>
public enum UIElementType
{
    Window,
    Sprite,
    Text,
    Border,
    Background,
    Button,
    Panel,
    Other
}
```

**`WindowComponent`**
```csharp
namespace MonoBall.Core.UI.Components;

/// <summary>
///     Component that stores window-specific data (border, background, content configuration).
///     Entities with this component represent window-like UI elements (message boxes, popups, panels).
///     Position is stored in PositionComponent, not here (avoids duplication).
/// </summary>
public struct WindowComponent
{
    /// <summary>
    ///     The border/outline definition ID (e.g., "base:textwindow:tilesheet/message_box").
    ///     If null, no border is rendered.
    /// </summary>
    public string? BorderId { get; set; }

    /// <summary>
    ///     The background definition ID (e.g., "base:popup:background/default").
    ///     If null, no background is rendered.
    /// </summary>
    public string? BackgroundId { get; set; }

    /// <summary>
    ///     The interior width in pixels (at 1x scale, before viewport scaling).
    /// </summary>
    public int InteriorWidth { get; set; }

    /// <summary>
    ///     The interior height in pixels (at 1x scale, before viewport scaling).
    /// </summary>
    public int InteriorHeight { get; set; }
}
```

**Note**: Window position is stored in `PositionComponent.Position`, not in `WindowComponent`. This avoids duplication and follows ECS best practices.

**`UITextComponent`**
```csharp
namespace MonoBall.Core.UI.Components;

/// <summary>
///     Component that stores text rendering data for UI elements.
///     Used for text content in windows, labels, buttons, etc.
/// </summary>
public struct UITextComponent
{
    /// <summary>
    ///     The text content to render.
    /// </summary>
    public string Text { get; set; }

    /// <summary>
    ///     The font ID to use for rendering.
    /// </summary>
    public string FontId { get; set; }

    /// <summary>
    ///     The font size in pixels.
    /// </summary>
    public int FontSize { get; set; }

    /// <summary>
    ///     The text color.
    /// </summary>
    public Color TextColor { get; set; }

    /// <summary>
    ///     The shadow color (if text has shadow).
    /// </summary>
    public Color? ShadowColor { get; set; }

    /// <summary>
    ///     Text alignment (Left, Center, Right).
    /// </summary>
    public TextAlignment Alignment { get; set; }

    /// <summary>
    ///     Line spacing in pixels.
    /// </summary>
    public int LineSpacing { get; set; }
}

public enum TextAlignment
{
    Left,
    Center,
    Right
}
```

#### Reused Components

- **`SpriteComponent`** + **`SpriteAnimationComponent`**: For animated UI sprites (down arrow, icons, etc.)
- **`PositionComponent`**: For screen-space positioning of UI elements (used by all UI entities)
- **`RenderableComponent`**: For visibility and opacity control
- **`WindowAnimationComponent`**: For window animations (slide, fade, etc.) - already exists

#### Component Responsibilities Clarification

**`MessageBoxComponent`** (Existing, on Scene Entity):
- Stores text processing state machine data (current token index, delay counter, state)
- Stores text parsing results (parsed tokens, wrapped lines)
- Stores text rendering state (current character index, scroll offset, effect time)
- **Attached to scene entity** (not window entity) because it's scene-level state
- Updated by `MessageBoxSceneSystem` during text processing

**`UITextComponent`** (New, on Text Entity):
- Stores display text (current visible characters, updated by `MessageBoxSceneSystem`)
- Stores font, color, alignment, spacing
- **Attached to text entity** (child of window entity)
- Updated by `MessageBoxSceneSystem` as text is printed character-by-character

**Component Namespace Note**: UI components are in `MonoBall.Core.UI.Components` following the pattern of scene components in `MonoBall.Core.Scenes.Components`. This allows feature-specific components to have their own namespace while maintaining organization.

#### Relationship Types (Arch.Extended)

**`OwnsUIElement`**
```csharp
namespace MonoBall.Core.UI.Relationships;

/// <summary>
///     Relationship type for scene → UI element ownership.
///     Used to link UI elements to their parent scene.
/// </summary>
public struct OwnsUIElement
{
    // Marker relationship - no data needed
    // Can be extended with metadata if needed (e.g., ZOrder, ElementType)
}
```

**`ContainsUIElement`**
```csharp
namespace MonoBall.Core.UI.Relationships;

/// <summary>
///     Relationship type for window → child element ownership.
///     Used to link child UI elements (border, background, content, sprites) to their parent window.
/// </summary>
public struct ContainsUIElement
{
    // Marker relationship - no data needed
    // Can be extended with metadata if needed (e.g., ZOrder, Layout constraints)
}
```

### System Design

#### New Systems

**`UIRenderSystem`**
```csharp
namespace MonoBall.Core.UI.Systems;

/// <summary>
///     System that renders UI elements for a specific scene.
///     Implements ISceneSystem to integrate with SceneSystem rendering pipeline.
///     Queries UI entities via Arch.Extended relationships, renders them in z-order.
/// </summary>
/// <exception cref="InvalidOperationException">Thrown if scene entity is not alive or missing SceneComponent.</exception>
public class UIRenderSystem : BaseSystem<World, float>, ISceneSystem, IDisposable
{
    private readonly GraphicsDevice _graphicsDevice;
    private readonly SpriteBatch _spriteBatch;
    private readonly IResourceManager _resourceManager;
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
    /// <param name="logger">The logger for logging operations. Required.</param>
    /// <exception cref="ArgumentNullException">Thrown if any required parameter is null.</exception>
    public UIRenderSystem(
        World world,
        GraphicsDevice graphicsDevice,
        SpriteBatch spriteBatch,
        IResourceManager resourceManager,
        ILogger logger
    ) : base(world)
    {
        _graphicsDevice = graphicsDevice ?? throw new ArgumentNullException(nameof(graphicsDevice));
        _spriteBatch = spriteBatch ?? throw new ArgumentNullException(nameof(spriteBatch));
        _resourceManager = resourceManager ?? throw new ArgumentNullException(nameof(resourceManager));
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
    /// <exception cref="InvalidOperationException">Thrown if scene entity is not alive or missing SceneComponent.</exception>
    public void RenderScene(Entity sceneEntity, GameTime gameTime)
    {
        if (!World.IsAlive(sceneEntity))
            throw new InvalidOperationException($"Scene entity {sceneEntity.Id} is not alive.");

        if (!World.Has<SceneComponent>(sceneEntity))
            throw new InvalidOperationException($"Scene entity {sceneEntity.Id} does not have SceneComponent.");

        // Clear reusable collection (avoids allocations in hot path)
        _renderList.Clear();

        // Collect UI elements via relationships
        ref var uiElements = ref sceneEntity.GetRelationships<OwnsUIElement>();
        foreach (var uiElement in uiElements)
        {
            if (!World.IsAlive(uiElement))
                continue;

            if (!World.Has<UIElementComponent>(uiElement))
                continue; // Not a valid UI element

            ref var ui = ref World.Get<UIElementComponent>(uiElement);
            ref var render = ref World.Get<RenderableComponent>(uiElement);
            
            if (!render.IsVisible)
                continue;

            _renderList.Add((uiElement, ui, ui.ZOrder));
        }

        if (_renderList.Count == 0)
        {
            _logger.Debug("Scene {SceneId} has no UI elements to render", sceneEntity.Id);
            return; // Not an error - scene might not have UI yet
        }

        // Sort by z-order (lower values render first)
        _renderList.Sort((a, b) => a.zOrder.CompareTo(b.zOrder));

        // Render in z-order
        foreach (var (entity, ui, _) in _renderList)
        {
            RenderUIElement(entity, ref ui);
        }
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
    /// <exception cref="InvalidOperationException">Thrown if entity is missing required components for its element type.</exception>
    private void RenderUIElement(Entity entity, ref UIElementComponent ui)
    {
        switch (ui.ElementType)
        {
            case UIElementType.Window:
                if (World.Has<WindowComponent>(entity))
                {
                    ref var window = ref World.Get<WindowComponent>(entity);
                    ref var pos = ref World.Get<PositionComponent>(entity);
                    RenderWindow(entity, ref window, ref pos);
                }
                break;

            case UIElementType.Sprite:
                if (World.Has<SpriteComponent>(entity))
                {
                    ref var sprite = ref World.Get<SpriteComponent>(entity);
                    ref var pos = ref World.Get<PositionComponent>(entity);
                    ref var render = ref World.Get<RenderableComponent>(entity);
                    RenderUISprite(entity, ref sprite, ref pos, ref render);
                }
                break;

            case UIElementType.Text:
                if (World.Has<UITextComponent>(entity))
                {
                    ref var text = ref World.Get<UITextComponent>(entity);
                    ref var pos = ref World.Get<PositionComponent>(entity);
                    RenderUIText(entity, ref text, ref pos);
                }
                break;

            // Border, Background, etc. - handled by window rendering
            default:
                _logger.Debug("Unhandled UI element type: {ElementType}", ui.ElementType);
                break;
        }
    }

    // Render methods for different UI element types...
    private void RenderWindow(Entity entity, ref WindowComponent window, ref PositionComponent pos) { }
    private void RenderUISprite(Entity entity, ref SpriteComponent sprite, ref PositionComponent pos, ref RenderableComponent render) { }
    private void RenderUIText(Entity entity, ref UITextComponent text, ref PositionComponent pos) { }

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
```

**Responsibilities:**
- Query UI entities belonging to a scene (via Arch.Extended relationships)
- Render UI elements in correct z-order
- Handle viewport scaling and screen-space coordinates
- Batch rendering where possible
- Validate scene entity and components (fail-fast)

**Integration with SceneSystem:**
- `UIRenderSystem` implements `ISceneSystem` interface
- `SystemManager` creates `UIRenderSystem` and passes it to `SceneSystem`
- `SceneSystem` registers `UIRenderSystem` for UI scene types (MessageBoxScene, MapPopupScene)
- When rendering UI scenes, `SceneSystem.Render()` calls `UIRenderSystem.RenderScene(sceneEntity, gameTime)`

**`UIAnimationSystem`** (Optional)
```csharp
namespace MonoBall.Core.UI.Systems;

/// <summary>
///     System that updates UI-specific animations.
///     Currently, sprite animations are handled by SpriteAnimationSystem,
///     but this could handle UI-specific animations (fade, slide, etc.).
/// </summary>
public class UIAnimationSystem : BaseSystem<World, float>
{
    // Can extend SpriteAnimationSystem or be separate for UI-specific animations
}
```

#### Refactored Systems

**`MessageBoxSceneSystem`** (Refactored)
```csharp
namespace MonoBall.Core.Scenes.Systems;

/// <summary>
///     System that handles message box text processing and creates UI entities.
///     NO LONGER handles rendering - that's UIRenderSystem's job.
/// </summary>
public class MessageBoxSceneSystem : BaseSystem<World, float>, ISceneSystem
{
    // Responsibilities:
    // 1. Handle MessageBoxShowEvent → Create scene + UI entities
    // 2. Update text state machine (character-by-character printing)
    // 3. Handle input (speed-up, advance text)
    // 4. Create/destroy UI entities (window, border, background, text, down arrow sprite)
    
    // NO RENDERING - delegates to UIRenderSystem
}
```

**Responsibilities:**
- Text processing state machine (character-by-character printing)
- Input handling (speed-up, advance)
- Creating UI entities (window, border, background, text, down arrow)
- Updating `MessageBoxComponent` state (on scene entity)
- Updating `UITextComponent.Text` (on text entity) as characters are printed
- Updating UI entity visibility/position (e.g., down arrow visibility based on `IsWaitingForInput`)
- **NOT rendering** - that's `UIRenderSystem`'s job

**Component Responsibilities:**
- `MessageBoxComponent` (on scene entity): Stores text processing state (tokens, current index, state machine)
- `UITextComponent` (on text entity): Stores display text (current visible characters, updated as text prints)

**`MapPopupSceneSystem`** (Refactored)
```csharp
namespace MonoBall.Core.Scenes.Systems;

/// <summary>
///     System that handles map popup lifecycle and creates UI entities.
///     NO LONGER handles rendering - that's UIRenderSystem's job.
/// </summary>
public class MapPopupSceneSystem : BaseSystem<World, float>, ISceneSystem
{
    // Responsibilities:
    // 1. Handle MapPopupShowEvent → Create scene + UI entities
    // 2. Create popup window entity with WindowComponent
    // 3. Create child entities (border, background, text) via relationships
    
    // NO RENDERING - delegates to UIRenderSystem
}
```

### Component Responsibilities Summary

**Scene Entity Components:**
- `SceneComponent`: Scene configuration (priority, camera mode, blocking behavior)
- `MessageBoxSceneComponent`: Marker component identifying message box scene
- `MessageBoxComponent`: Text processing state (tokens, current index, state machine, delay counters)

**Window Entity Components:**
- `WindowComponent`: Window configuration (border ID, background ID, dimensions)
- `UIElementComponent`: UI metadata (element type, z-order, interactivity)
- `PositionComponent`: Window position in screen space
- `RenderableComponent`: Visibility and opacity

**Text Entity Components:**
- `UITextComponent`: Display text (current visible characters, font, color, alignment)
- `UIElementComponent`: UI metadata (element type, z-order)
- `PositionComponent`: Text position in screen space
- `RenderableComponent`: Visibility and opacity

**Down Arrow Sprite Entity Components:**
- `SpriteComponent`: Sprite data (sprite ID, frame index, flip flags)
- `SpriteAnimationComponent`: Animation state (animation name, timing, playback)
- `UIElementComponent`: UI metadata (element type, z-order)
- `PositionComponent`: Sprite position in screen space
- `RenderableComponent`: Visibility (updated by `MessageBoxSceneSystem` based on `IsWaitingForInput`)

**Data Flow:**
1. `MessageBoxSceneSystem` processes text state machine (updates `MessageBoxComponent`)
2. As characters are printed, `MessageBoxSceneSystem` updates `UITextComponent.Text`
3. `MessageBoxSceneSystem` updates UI entity visibility/position (e.g., down arrow visibility)
4. `SpriteAnimationSystem` updates `SpriteComponent.FrameIndex` for animated sprites
5. `UIRenderSystem` queries UI entities and renders them

### Entity Structure Examples

#### Message Box Entity Hierarchy

```
Scene Entity (MessageBoxSceneComponent)
  ├─ Relationship: OwnsUIElement → Window Entity
  │
  └─ Window Entity (WindowComponent, UIElementComponent, PositionComponent, RenderableComponent)
      ├─ Relationship: ContainsUIElement → Border Entity
      ├─ Relationship: ContainsUIElement → Background Entity
      ├─ Relationship: ContainsUIElement → Text Entity
      └─ Relationship: ContainsUIElement → Down Arrow Sprite Entity
          └─ Down Arrow Sprite Entity (SpriteComponent, SpriteAnimationComponent, PositionComponent, RenderableComponent, UIElementComponent)
```

#### Entity Creation Example

```csharp
// In MessageBoxSceneSystem.OnMessageBoxShow()

// 1. Create scene entity
var sceneEntity = _sceneManager.CreateScene(sceneComponent, messageBoxSceneComponent);

// 2. Create window entity
var windowEntity = World.Create(
    new WindowComponent
    {
        BorderId = _messageBoxTilesheetId,
        BackgroundId = null, // Or background ID if needed
        InteriorWidth = _messageBoxInteriorWidth,
        InteriorHeight = _messageBoxInteriorHeight
        // Note: Position stored in PositionComponent, not WindowComponent
    },
    new UIElementComponent
    {
        ElementType = UIElementType.Window,
        ZOrder = 0,
        IsInteractive = false
    },
    new PositionComponent { Position = new Vector2(msgBoxInteriorX, msgBoxInteriorY) },
    new RenderableComponent { IsVisible = true, Opacity = 1.0f }
);

// 3. Link window to scene via relationship
sceneEntity.AddRelationship<OwnsUIElement>(windowEntity);

// 4. Create border entity
var borderEntity = World.Create(
    new UIElementComponent
    {
        ElementType = UIElementType.Border,
        ZOrder = 1 // Renders before background
    },
    new RenderableComponent { IsVisible = true }
);

// 5. Link border to window
windowEntity.AddRelationship<ContainsUIElement>(borderEntity);

// 6. Create background entity
var backgroundEntity = World.Create(
    new UIElementComponent
    {
        ElementType = UIElementType.Background,
        ZOrder = 2
    },
    new RenderableComponent { IsVisible = true }
);

windowEntity.AddRelationship<ContainsUIElement>(backgroundEntity);

// 7. Create text entity
// Note: UITextComponent.Text starts empty - updated by MessageBoxSceneSystem as text prints
var textEntity = World.Create(
    new UITextComponent
    {
        Text = string.Empty, // Starts empty, updated character-by-character by MessageBoxSceneSystem
        FontId = fontId,
        FontSize = _defaultFontSize,
        TextColor = initialTextColor,
        ShadowColor = initialShadowColor,
        Alignment = TextAlignment.Left,
        LineSpacing = _defaultLineSpacing
    },
    new UIElementComponent
    {
        ElementType = UIElementType.Text,
        ZOrder = 10 // Renders on top
    },
    new PositionComponent { Position = new Vector2(textStartX, textStartY) },
    new RenderableComponent { IsVisible = true }
);

windowEntity.AddRelationship<ContainsUIElement>(textEntity);

// 8. Create down arrow sprite entity (uses existing sprite components!)
var downArrowEntity = World.Create(
    new SpriteComponent
    {
        SpriteId = _constants.GetString("DownArrowSpriteId"),
        FrameIndex = 0
    },
    new SpriteAnimationComponent
    {
        CurrentAnimationName = _constants.GetString("DownArrowAnimation"),
        IsPlaying = true,
        ElapsedTime = 0f
    },
    new PositionComponent
    {
        Position = new Vector2(arrowX, arrowY) // Calculated based on text cursor
    },
    new RenderableComponent
    {
        IsVisible = msgBox.IsWaitingForInput // Only visible when waiting
    },
    new UIElementComponent
    {
        ElementType = UIElementType.Sprite,
        ZOrder = 20 // Renders on top of text
    }
);

windowEntity.AddRelationship<ContainsUIElement>(downArrowEntity);
```

### Rendering Pipeline

#### Current (Problematic)
```
MessageBoxSceneSystem.RenderScene()
  → Creates renderer instances
  → Calls renderer.Render() directly
  → Manually animates down arrow sprite
  → Renders everything inline
```

#### Proposed (ECS-First)
```
MessageBoxSceneSystem.OnMessageBoxShow()
  → Creates scene entity
  → Creates UI entities (window, border, background, text, down arrow)
  → Adds components and relationships

SpriteAnimationSystem.Update()
  → Updates ALL sprites (including UI sprites like down arrow)
  → Updates SpriteComponent.FrameIndex for animated sprites

UIRenderSystem.RenderScene(sceneEntity, gameTime)
  → Validates scene entity (fail-fast if invalid)
  → Queries UI entities via Arch.Extended relationships
  → Collects UI elements into reusable collection (sorted by z-order)
  → Renders in z-order: backgrounds → borders → content → sprites → text
  → Uses existing sprite rendering logic for UI sprites
```

### Query Patterns

#### Query UI Elements for a Scene

**Using Relationships (Arch.Extended) - Recommended:**
```csharp
// Get all UI elements owned by a scene
ref var uiElements = ref sceneEntity.GetRelationships<OwnsUIElement>();
foreach (var uiElement in uiElements)
{
    // Validate entity is still alive (fail-fast)
    if (!World.IsAlive(uiElement))
        continue;

    // Validate entity has required components
    if (!World.Has<UIElementComponent>(uiElement))
        continue; // Not a valid UI element

    // Get components and validate visibility
    ref var ui = ref World.Get<UIElementComponent>(uiElement);
    ref var render = ref World.Get<RenderableComponent>(uiElement);
    
    if (!render.IsVisible)
        continue;

    // Render UI element
    RenderUIElement(uiElement, ref ui);
}
```

**Note**: This pattern validates entities and components at each step, following fail-fast principles. No fallback to `SceneOwnershipComponent` - all UI uses relationships.

#### Query Child Elements of a Window

```csharp
// Get all child elements of a window
ref var children = ref windowEntity.GetRelationships<ContainsUIElement>();
foreach (var child in children)
{
    // Validate entity is still alive
    if (!World.IsAlive(child))
        continue;

    // Validate entity has required components
    if (!World.Has<UIElementComponent>(child) || !World.Has<RenderableComponent>(child))
        continue;

    // Get components and check visibility
    ref var ui = ref World.Get<UIElementComponent>(child);
    ref var render = ref World.Get<RenderableComponent>(child);
    
    if (!render.IsVisible)
        continue;

    // Render child element
    RenderUIElement(child, ref ui);
}
```

### Migration Strategy

#### Phase 1: Infrastructure Setup

1. **Add Arch.Extended Package**
   ```xml
   <PackageReference Include="Arch.Extended" Version="..." />
   ```

2. **Create UI Components**
   - `UIElementComponent`
   - `WindowComponent`
   - `UITextComponent`
   - Relationship types: `OwnsUIElement`, `ContainsUIElement`

3. **Create UIRenderSystem**
   - Implement `ISceneSystem` interface
   - Cache `QueryDescription` in constructor
   - Add reusable collections for hot paths
   - Add fail-fast validation
   - Register with `SceneSystem` via `SystemManager`

#### Phase 2: Down Arrow Refactoring

1. **Extract Down Arrow to Entity**
   - Remove `DownArrowAnimationTime` from `MessageBoxComponent`
   - Remove `RenderDownArrow()` method from `MessageBoxSceneSystem`
   - Create down arrow entity with `SpriteComponent` + `SpriteAnimationComponent`
   - Link to window via `ContainsUIElement` relationship

2. **Verify Animation**
   - `SpriteAnimationSystem` should handle animation automatically
   - Down arrow should animate correctly

#### Phase 3: Message Box Rendering Refactoring

1. **Create UI Entities in MessageBoxSceneSystem**
   - Window entity with `WindowComponent`
   - Border entity
   - Background entity
   - Text entity with `UITextComponent`
   - Down arrow entity (from Phase 2)

2. **Move Rendering to UIRenderSystem**
   - Remove rendering code from `MessageBoxSceneSystem.RenderScene()`
   - Implement rendering in `UIRenderSystem`
   - Query UI entities and render in z-order

3. **Update SceneSystem Integration**
   - `SystemManager` creates `UIRenderSystem` and passes it to `SceneSystem`
   - `SceneSystem` registers `UIRenderSystem` for UI scene types (MessageBoxScene, MapPopupScene)
   - `SceneSystem.Render()` calls `UIRenderSystem.RenderScene(sceneEntity, gameTime)` for UI scenes

#### Phase 4: Other UI Systems

1. **Refactor MapPopupSceneSystem**
   - Create UI entities instead of direct rendering
   - Use `UIRenderSystem` for rendering

2. **Refactor Other UI Systems**
   - Apply same pattern to any other UI systems

#### Phase 5: Cleanup

1. **Remove Old Code**
   - Remove direct rendering methods from `MessageBoxSceneSystem` and `MapPopupSceneSystem`
   - Remove `RenderDownArrow()` method
   - Remove `DownArrowAnimationTime` from `MessageBoxComponent`
   - Remove unused renderer classes (or keep as helpers for `UIRenderSystem`)

2. **Optimization**
   - Batch rendering where possible
   - Add culling for off-screen UI elements
   - Optimize queries (already cached in constructor)
   - Profile and optimize relationship iteration if needed

### Benefits

1. **True ECS Architecture**: UI elements are entities, following ECS principles
2. **Component Reuse**: Same sprite/animation components for UI and game entities
3. **Separation of Concerns**: Clear boundaries between systems
4. **Performance**: Query-based rendering can be optimized (batching, culling)
5. **Extensibility**: Easy to add new UI elements by composing components
6. **Maintainability**: Single rendering path, consistent patterns
7. **Testability**: Systems can be tested independently
8. **Hierarchical UI**: Natural parent-child relationships via Arch.Extended

### Migration Approach

**No Backward Compatibility:**
- All systems migrate immediately to use Arch.Extended relationships
- Remove `SceneOwnershipComponent` usage for UI elements
- Update all call sites to use relationships
- Fail fast if old patterns are detected (validation in `UIRenderSystem`)

**Migration Order:**
1. Add Arch.Extended and create components
2. Create `UIRenderSystem` with relationship queries
3. Refactor `MessageBoxSceneSystem` to create UI entities
4. Refactor `MapPopupSceneSystem` to create UI entities
5. Remove old rendering code

### Performance Considerations

1. **Query Caching**: Cache `QueryDescription` in constructors (never create in Update/Render)
2. **Reusable Collections**: Cache `List<T>` as instance fields, clear and reuse in Render methods
3. **Batch Rendering**: Group UI elements by type for batching (sprites, text, etc.)
4. **Culling**: Skip off-screen UI elements (check viewport bounds)
5. **Relationship Iteration**: Arch.Extended relationships are efficient (direct iteration)
6. **Component Access**: Minimize component lookups in hot paths (get once, reuse)
7. **Validation**: Early exits for invalid entities/components (fail-fast, but efficient)

### Testing Strategy

1. **Unit Tests**: Test component creation, relationship linking
2. **Integration Tests**: Test UI entity creation and rendering
3. **Visual Tests**: Verify message box, popup rendering matches current behavior
4. **Performance Tests**: Measure query and rendering performance

### SystemManager Integration

**UIRenderSystem Creation:**
- `SystemManager` creates `UIRenderSystem` in the scene systems creation phase
- `UIRenderSystem` is passed to `SceneSystem` constructor
- `SceneSystem` registers `UIRenderSystem` for UI scene types

**Registration Pattern:**
```csharp
// In SystemManager or SceneSystemsFactory
var uiRenderSystem = new UIRenderSystem(
    world,
    graphicsDevice,
    spriteBatch,
    resourceManager,
    logger
);

var sceneSystem = new SceneSystem(
    world,
    // ... other dependencies
    uiRenderSystem  // Pass UIRenderSystem to SceneSystem
);

// In SceneSystem constructor
public SceneSystem(
    World world,
    // ... other dependencies
    UIRenderSystem? uiRenderSystem = null
)
{
    _uiRenderSystem = uiRenderSystem;
    
    // Register UIRenderSystem for UI scene types
    if (_uiRenderSystem != null)
    {
        RegisterSceneSystem(typeof(MessageBoxSceneComponent), _uiRenderSystem);
        RegisterSceneSystem(typeof(MapPopupSceneComponent), _uiRenderSystem);
    }
}
```

**Note**: `UIRenderSystem` can handle multiple scene types (MessageBoxScene, MapPopupScene) by checking scene entity marker components in `RenderScene()`.

### Future Enhancements

1. **UI Layout System**: Automatic positioning based on relationships
2. **UI Input System**: Handle clicks, hover, focus for interactive elements
3. **UI Animation System**: Fade, slide, scale animations for UI elements
4. **UI Scripting API**: Scripts can create/modify UI elements
5. **UI Theme System**: Shared styling for UI elements

---

## Implementation Checklist

### Phase 1: Infrastructure
- [ ] Add Arch.Extended package
- [ ] Create `UIElementComponent` (in `MonoBall.Core.UI.Components`)
- [ ] Create `WindowComponent` (in `MonoBall.Core.UI.Components`)
- [ ] Create `UITextComponent` (in `MonoBall.Core.UI.Components`)
- [ ] Create relationship types (`OwnsUIElement`, `ContainsUIElement`) in `MonoBall.Core.UI.Relationships`
- [ ] Create `UIRenderSystem` implementing `ISceneSystem`
- [ ] Register `UIRenderSystem` with `SceneSystem` via `SystemManager`

### Phase 2: Down Arrow
- [ ] Remove `DownArrowAnimationTime` from `MessageBoxComponent`
- [ ] Remove `RenderDownArrow()` from `MessageBoxSceneSystem`
- [ ] Create down arrow entity in `OnMessageBoxShow()`
- [ ] Link via `ContainsUIElement` relationship
- [ ] Verify animation works via `SpriteAnimationSystem`

### Phase 3: Message Box Rendering
- [ ] Create window entity in `OnMessageBoxShow()` with `WindowComponent` and `PositionComponent`
- [ ] Create border/background/text entities
- [ ] Create down arrow sprite entity (from Phase 2)
- [ ] Link entities via `OwnsUIElement` and `ContainsUIElement` relationships
- [ ] Implement `UIRenderSystem.RenderScene()` with relationship queries
- [ ] Update `MessageBoxSceneSystem` to update `UITextComponent.Text` as text prints
- [ ] Remove rendering code from `MessageBoxSceneSystem.RenderScene()`
- [ ] Verify `SceneSystem` calls `UIRenderSystem.RenderScene()` for MessageBoxScene

### Phase 4: Other Systems
- [ ] Refactor `MapPopupSceneSystem`
- [ ] Refactor other UI systems

### Phase 5: Cleanup
- [ ] Remove old rendering code
- [ ] Optimize queries and rendering
- [ ] Update documentation
