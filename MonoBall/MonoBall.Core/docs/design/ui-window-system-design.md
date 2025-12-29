# UI Window System Design

## Overview

This document describes the design for a reusable UI window system that abstracts common window functionality (border,
background, content, position, size) while allowing customization for different window types (map popups, message boxes,
future UI elements).

## Problem Statement

Currently, window-like UI elements (map popups, message boxes) have duplicated code for:

- Border/outline rendering
- Background rendering
- Position and size calculation
- Viewport scaling
- Coordinate system management

Each implementation handles these concerns differently, leading to:

- Code duplication
- Inconsistent behavior
- Difficult maintenance
- Hard to add new window types

## Design Goals

1. **Abstract Common Functionality**: Extract shared window concerns (border, background, position, size)
2. **Allow Customization**: Support different border styles, background types, and content rendering
3. **Maintain Existing Behavior**: Don't break current map popup or message box functionality
4. **Follow Project Standards**: Adhere to SOLID principles, DRY, ECS architecture, nullable reference types
5. **Performance**: Cache queries, minimize allocations in hot paths

## Architecture

### Core Components

#### 1. `IWindowRenderer` Interface

Defines the contract for rendering window components:

```csharp
namespace MonoBall.Core.UI.Windows
{
    /// <summary>
    /// Interface for rendering window borders.
    /// </summary>
    public interface IBorderRenderer
    {
        /// <summary>
        /// Renders the window border around the specified interior bounds.
        /// All coordinates are in screen pixels (already scaled by the caller).
        /// </summary>
        /// <param name="spriteBatch">The sprite batch for rendering.</param>
        /// <param name="interiorX">The X position of the interior (content area).</param>
        /// <param name="interiorY">The Y position of the interior (content area).</param>
        /// <param name="interiorWidth">The width of the interior (content area).</param>
        /// <param name="interiorHeight">The height of the interior (content area).</param>
        void RenderBorder(
            SpriteBatch spriteBatch,
            int interiorX,
            int interiorY,
            int interiorWidth,
            int interiorHeight
        );
    }

    /// <summary>
    /// Interface for rendering window backgrounds.
    /// </summary>
    public interface IBackgroundRenderer
    {
        /// <summary>
        /// Renders the window background within the specified bounds.
        /// All coordinates are in screen pixels (already scaled by the caller).
        /// </summary>
        /// <param name="spriteBatch">The sprite batch for rendering.</param>
        /// <param name="x">The X position of the background.</param>
        /// <param name="y">The Y position of the background.</param>
        /// <param name="width">The width of the background.</param>
        /// <param name="height">The height of the background.</param>
        void RenderBackground(
            SpriteBatch spriteBatch,
            int x,
            int y,
            int width,
            int height
        );
    }

    /// <summary>
    /// Interface for rendering window content.
    /// </summary>
    public interface IContentRenderer
    {
        /// <summary>
        /// Renders the window content within the specified bounds.
        /// All coordinates are in screen pixels (already scaled by the caller).
        /// </summary>
        /// <param name="spriteBatch">The sprite batch for rendering.</param>
        /// <param name="x">The X position of the content area.</param>
        /// <param name="y">The Y position of the content area.</param>
        /// <param name="width">The width of the content area.</param>
        /// <param name="height">The height of the content area.</param>
        void RenderContent(
            SpriteBatch spriteBatch,
            int x,
            int y,
            int width,
            int height
        );
    }
}
```

#### 2. `WindowBounds` Structure

Encapsulates window position and size calculations:

```csharp
namespace MonoBall.Core.UI.Windows
{
    /// <summary>
    /// Represents the bounds of a UI window, including outer bounds and interior bounds.
    /// All coordinates are in screen pixels (scaling is handled by the caller).
    /// </summary>
    public struct WindowBounds
    {
        /// <summary>
        /// Gets the outer X position (including border).
        /// </summary>
        public int OuterX { get; }

        /// <summary>
        /// Gets the outer Y position (including border).
        /// </summary>
        public int OuterY { get; }

        /// <summary>
        /// Gets the outer width (including border on both sides).
        /// </summary>
        public int OuterWidth { get; }

        /// <summary>
        /// Gets the outer height (including border on top and bottom).
        /// </summary>
        public int OuterHeight { get; }

        /// <summary>
        /// Gets the interior X position (content area, excluding border).
        /// </summary>
        public int InteriorX { get; }

        /// <summary>
        /// Gets the interior Y position (content area, excluding border).
        /// </summary>
        public int InteriorY { get; }

        /// <summary>
        /// Gets the interior width (content area width).
        /// </summary>
        public int InteriorWidth { get; }

        /// <summary>
        /// Gets the interior height (content area height).
        /// </summary>
        public int InteriorHeight { get; }

        /// <summary>
        /// Initializes a new instance of the WindowBounds structure.
        /// </summary>
        /// <param name="outerX">The outer X position (including border), in screen pixels.</param>
        /// <param name="outerY">The outer Y position (including border), in screen pixels.</param>
        /// <param name="outerWidth">The outer width (including border), in screen pixels.</param>
        /// <param name="outerHeight">The outer height (including border), in screen pixels.</param>
        /// <param name="interiorX">The interior X position (content area, excluding border), in screen pixels.</param>
        /// <param name="interiorY">The interior Y position (content area, excluding border), in screen pixels.</param>
        /// <param name="interiorWidth">The interior width (content area), in screen pixels.</param>
        /// <param name="interiorHeight">The interior height (content area), in screen pixels.</param>
        /// <remarks>
        /// Both outer and interior coordinates are provided directly. This allows for non-uniform borders
        /// (e.g., MessageBox has 2 tiles on left, 1 tile elsewhere). The caller is responsible for
        /// calculating both coordinate sets based on their specific border requirements.
        /// </remarks>
        public WindowBounds(
            int outerX,
            int outerY,
            int outerWidth,
            int outerHeight,
            int interiorX,
            int interiorY,
            int interiorWidth,
            int interiorHeight
        )
        {
            OuterX = outerX;
            OuterY = outerY;
            OuterWidth = outerWidth;
            OuterHeight = outerHeight;
            InteriorX = interiorX;
            InteriorY = interiorY;
            InteriorWidth = interiorWidth;
            InteriorHeight = interiorHeight;
        }
    }
}
```

#### 3. `WindowRenderer` Class

Main renderer that orchestrates border, background, and content rendering:

```csharp
namespace MonoBall.Core.UI.Windows
{
    /// <summary>
    /// Renders a UI window using pluggable border, background, and content renderers.
    /// </summary>
    public class WindowRenderer
    {
        private readonly IBorderRenderer? _borderRenderer;
        private readonly IBackgroundRenderer? _backgroundRenderer;
        private readonly IContentRenderer? _contentRenderer;
        private readonly ILogger _logger;

        /// <summary>
        /// Initializes a new instance of the WindowRenderer class.
        /// </summary>
        /// <param name="borderRenderer">Optional border renderer. If null, no border is rendered.</param>
        /// <param name="backgroundRenderer">Optional background renderer. If null, no background is rendered.</param>
        /// <param name="contentRenderer">Optional content renderer. If null, no content is rendered.</param>
        /// <param name="logger">The logger for logging operations.</param>
        public WindowRenderer(
            IBorderRenderer? borderRenderer,
            IBackgroundRenderer? backgroundRenderer,
            IContentRenderer? contentRenderer,
            ILogger logger
        )
        {
            _borderRenderer = borderRenderer;
            _backgroundRenderer = backgroundRenderer;
            _contentRenderer = contentRenderer;
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Renders the complete window (border, background, content).
        /// </summary>
        /// <param name="spriteBatch">The sprite batch for rendering.</param>
        /// <param name="bounds">The window bounds (coordinates already scaled by caller).</param>
        public void Render(SpriteBatch spriteBatch, WindowBounds bounds)
        {
            // Render background first (behind border)
            if (_backgroundRenderer != null)
            {
                _backgroundRenderer.RenderBackground(
                    spriteBatch,
                    bounds.InteriorX,
                    bounds.InteriorY,
                    bounds.InteriorWidth,
                    bounds.InteriorHeight
                );
            }

            // Render border around interior
            if (_borderRenderer != null)
            {
                _borderRenderer.RenderBorder(
                    spriteBatch,
                    bounds.InteriorX,
                    bounds.InteriorY,
                    bounds.InteriorWidth,
                    bounds.InteriorHeight
                );
            }

            // Render content last (on top)
            if (_contentRenderer != null)
            {
                _contentRenderer.RenderContent(
                    spriteBatch,
                    bounds.InteriorX,
                    bounds.InteriorY,
                    bounds.InteriorWidth,
                    bounds.InteriorHeight
                );
            }
        }
    }
}
```

### Concrete Implementations

#### 1. Border Renderers

##### `PopupTileSheetBorderRenderer`

Renders borders using tile sheet definitions (for map popups):

```csharp
namespace MonoBall.Core.UI.Windows.Borders
{
    /// <summary>
    /// Renders window borders using a tile sheet definition (for map popups).
    /// </summary>
    public class PopupTileSheetBorderRenderer : IBorderRenderer
    {
        private readonly Texture2D _texture;
        private readonly PopupOutlineDefinition _outlineDef;
        private readonly IConstantsService _constants;
        private readonly ILogger _logger;

        // Cached tile lookup dictionary
        private readonly Dictionary<int, PopupTileDefinition> _tileLookup;

        /// <summary>
        /// Initializes a new instance of the PopupTileSheetBorderRenderer class.
        /// </summary>
        /// <param name="texture">The border texture.</param>
        /// <param name="outlineDef">The outline definition.</param>
        /// <param name="constants">The constants service.</param>
        /// <param name="logger">The logger.</param>
        public PopupTileSheetBorderRenderer(
            Texture2D texture,
            PopupOutlineDefinition outlineDef,
            IConstantsService constants,
            ILogger logger
        )
        {
            _texture = texture ?? throw new ArgumentNullException(nameof(texture));
            _outlineDef = outlineDef ?? throw new ArgumentNullException(nameof(outlineDef));
            _constants = constants ?? throw new ArgumentNullException(nameof(constants));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            // Build tile lookup dictionary
            _tileLookup = new Dictionary<int, PopupTileDefinition>();
            if (outlineDef.Tiles != null)
            {
                foreach (var tile in outlineDef.Tiles)
                {
                    if (tile != null && !_tileLookup.ContainsKey(tile.Index))
                    {
                        _tileLookup[tile.Index] = tile;
                    }
                }
            }
        }

        public void RenderBorder(
            SpriteBatch spriteBatch,
            int interiorX,
            int interiorY,
            int interiorWidth,
            int interiorHeight
        )
        {
            // Implementation similar to MapPopupRendererSystem.DrawTileSheetBorder
            // Uses _tileLookup for efficient tile access
            // Note: If scale is needed for tile size calculations, it should be passed
            // to the constructor or calculated from the coordinates received
        }
    }
}
```

##### `MessageBoxDialogueFrameBorderRenderer`

Renders message box dialogue frame (custom pattern):

```csharp
namespace MonoBall.Core.UI.Windows.Borders
{
    /// <summary>
    /// Renders message box dialogue frame using a custom tile pattern.
    /// </summary>
    public class MessageBoxDialogueFrameBorderRenderer : IBorderRenderer
    {
        private readonly Texture2D _texture;
        private readonly PopupOutlineDefinition _tilesheetDef;
        private readonly IConstantsService _constants;
        private readonly ILogger _logger;

        // Cached tile lookup
        private readonly Dictionary<int, PopupTileDefinition> _tileLookup;

        public void RenderBorder(
            SpriteBatch spriteBatch,
            int interiorX,
            int interiorY,
            int interiorWidth,
            int interiorHeight
        )
        {
            // Implementation similar to MessageBoxSceneSystem.DrawDialogueFrame
            // Handles decorative frame pattern with flipped bottom tiles
            // Note: If scale is needed for tile size calculations, it should be passed
            // to the constructor or calculated from the coordinates received
        }
    }
}
```

#### 2. Background Renderers

##### `BitmapBackgroundRenderer`

Renders bitmap backgrounds (for map popups):

```csharp
namespace MonoBall.Core.UI.Windows.Backgrounds
{
    /// <summary>
    /// Renders window backgrounds using a bitmap texture (for map popups).
    /// </summary>
    public class BitmapBackgroundRenderer : IBackgroundRenderer
    {
        private readonly Texture2D _texture;
        private readonly PopupBackgroundDefinition _backgroundDef;

        public void RenderBackground(
            SpriteBatch spriteBatch,
            int x,
            int y,
            int width,
            int height
        )
        {
            // Draw bitmap texture (coordinates already scaled by caller)
            spriteBatch.Draw(
                _texture,
                new Rectangle(x, y, width, height),
                new Rectangle(0, 0, _backgroundDef.Width, _backgroundDef.Height),
                Color.White
            );
        }
    }
}
```

##### `TileSheetBackgroundRenderer`

Renders tile sheet backgrounds (for message boxes):

```csharp
namespace MonoBall.Core.UI.Windows.Backgrounds
{
    /// <summary>
    /// Renders window backgrounds using a tile sheet (for message boxes).
    /// </summary>
    public class TileSheetBackgroundRenderer : IBackgroundRenderer
    {
        private readonly Texture2D _texture;
        private readonly PopupOutlineDefinition _tilesheetDef;
        private readonly int _backgroundTileIndex;

        public void RenderBackground(
            SpriteBatch spriteBatch,
            int x,
            int y,
            int width,
            int height
        )
        {
            // Fill interior with background tile (tile 0 for message box)
            // Note: If scale is needed for tile size calculations, it should be passed
            // to the constructor. The tile size can be inferred from width/height
            // if the interior size is known, or stored in constructor.
            // Draw tiled background
        }
    }
}
```

#### 3. Content Renderers

##### `SimpleTextContentRenderer`

Renders simple centered text (for map popups):

```csharp
namespace MonoBall.Core.UI.Windows.Content
{
    /// <summary>
    /// Renders simple centered text content (for map popups).
    /// </summary>
    public class SimpleTextContentRenderer : IContentRenderer
    {
        private readonly FontService _fontService;
        private readonly string _fontId;
        private readonly string _text;
        private readonly Color _textColor;
        private readonly Color _shadowColor;
        private readonly IConstantsService _constants;
        private readonly ILogger _logger;

        public void RenderContent(
            SpriteBatch spriteBatch,
            int x,
            int y,
            int width,
            int height
        )
        {
            // Render centered text with shadow
            // Similar to MapPopupRendererSystem text rendering
            // Note: If scale is needed for font size calculations, it should be passed
            // to the constructor or calculated from the coordinates received
        }
    }
}
```

##### `MessageBoxContentRenderer`

Renders complex message box text with scrolling, wrapping, control codes:

```csharp
namespace MonoBall.Core.UI.Windows.Content
{
    /// <summary>
    /// Renders message box text content with scrolling, wrapping, and control codes.
    /// </summary>
    public class MessageBoxContentRenderer : IContentRenderer
    {
        private readonly MessageBoxComponent _messageBox;
        private readonly FontService _fontService;
        private readonly IConstantsService _constants;
        private readonly ILogger _logger;

        public void RenderContent(
            SpriteBatch spriteBatch,
            int x,
            int y,
            int width,
            int height
        )
        {
            // Complex text rendering logic from MessageBoxSceneSystem.RenderText
            // Handles character-by-character rendering, scrolling, control codes
            // Note: If scale is needed for font size calculations, it should be passed
            // to the constructor or calculated from the coordinates received
        }
    }
}
```

### Window Configuration

#### `WindowConfiguration` Class

Encapsulates window configuration (position, size, renderers):

```csharp
namespace MonoBall.Core.UI.Windows
{
    /// <summary>
    /// Configuration for a UI window, including position, size, and renderers.
    /// </summary>
    public class WindowConfiguration
    {
        /// <summary>
        /// Gets or sets the window position calculator.
        /// </summary>
        public IWindowPositionCalculator PositionCalculator { get; set; }

        /// <summary>
        /// Gets or sets the window size calculator.
        /// </summary>
        public IWindowSizeCalculator SizeCalculator { get; set; }

        /// <summary>
        /// Gets or sets the border renderer.
        /// </summary>
        public IBorderRenderer? BorderRenderer { get; set; }

        /// <summary>
        /// Gets or sets the background renderer.
        /// </summary>
        public IBackgroundRenderer? BackgroundRenderer { get; set; }

        /// <summary>
        /// Gets or sets the content renderer.
        /// </summary>
        public IContentRenderer? ContentRenderer { get; set; }
    }
}
```

#### Position and Size Calculators

```csharp
namespace MonoBall.Core.UI.Windows
{
    /// <summary>
    /// Interface for calculating window position.
    /// </summary>
    public interface IWindowPositionCalculator
    {
        /// <summary>
        /// Calculates the window position based on camera and viewport.
        /// </summary>
        /// <param name="camera">The camera component.</param>
        /// <param name="viewportWidth">The viewport width.</param>
        /// <param name="viewportHeight">The viewport height.</param>
        /// <param name="scale">The viewport scale factor.</param>
        /// <param name="windowWidth">The window width.</param>
        /// <param name="windowHeight">The window height.</param>
        /// <returns>The window position (outer bounds, top-left corner).</returns>
        (int x, int y) CalculatePosition(
            CameraComponent camera,
            int viewportWidth,
            int viewportHeight,
            int scale,
            int windowWidth,
            int windowHeight
        );
    }

    /// <summary>
    /// Interface for calculating window size.
    /// </summary>
    public interface IWindowSizeCalculator
    {
        /// <summary>
        /// Calculates the window interior size.
        /// </summary>
        /// <param name="scale">The viewport scale factor.</param>
        /// <returns>The interior width and height.</returns>
        (int width, int height) CalculateSize(int scale);
    }
}
```

Concrete implementations:

- `FixedSizeWindowCalculator`: Fixed size (map popups, message boxes)
- `DynamicSizeWindowCalculator`: Size based on content (future)
- `TopLeftPositionCalculator`: Top-left positioning (map popups)
- `BottomCenterPositionCalculator`: Bottom-center positioning (message boxes)

## Usage Examples

### Map Popup Window

```csharp
// In MapPopupRendererSystem
var borderRenderer = new PopupTileSheetBorderRenderer(
    outlineTexture,
    outlineDef,
    _constants,
    _logger
);

var backgroundRenderer = new BitmapBackgroundRenderer(
    backgroundTexture,
    backgroundDef
);

var contentRenderer = new SimpleTextContentRenderer(
    _fontService,
    "base:font:game/pokemon",
    popup.MapSectionName,
    Color.White,
    new Color(72, 72, 80, 255),
    _constants,
    _logger
);

var windowRenderer = new WindowRenderer(
    borderRenderer,
    backgroundRenderer,
    contentRenderer,
    _logger
);

// Calculate bounds (scaling handled here, before creating WindowBounds)
int currentScale = CameraTransformUtility.GetViewportScale(camera, _constants.Get<int>("GbaReferenceWidth"));
int borderThickness = (outlineDef.IsTileSheet ? outlineDef.TileWidth : 8) * currentScale;
int interiorWidth = _constants.Get<int>("PopupBackgroundWidth") * currentScale;
int interiorHeight = _constants.Get<int>("PopupBackgroundHeight") * currentScale;
int outerX = _constants.Get<int>("PopupScreenPadding") * currentScale;
int outerY = (int)MathF.Round(anim.CurrentY * currentScale);

// Calculate interior position and outer dimensions
int interiorX = outerX + borderThickness;
int interiorY = outerY + borderThickness;
int outerWidth = interiorWidth + (borderThickness * 2);
int outerHeight = interiorHeight + (borderThickness * 2);

var bounds = new WindowBounds(
    outerX,
    outerY,
    outerWidth,
    outerHeight,
    interiorX,
    interiorY,
    interiorWidth,
    interiorHeight
);

// Render (no scale parameter - coordinates already scaled)
windowRenderer.Render(_spriteBatch, bounds);
```

### Message Box Window

```csharp
// In MessageBoxSceneSystem
var borderRenderer = new MessageBoxDialogueFrameBorderRenderer(
    tilesheetTexture,
    tilesheetDef,
    _constants,
    _logger
);

var backgroundRenderer = new TileSheetBackgroundRenderer(
    tilesheetTexture,
    tilesheetDef,
    0 // Background tile index
);

var contentRenderer = new MessageBoxContentRenderer(
    msgBox,
    _fontService,
    _constants,
    _logger
);

var windowRenderer = new WindowRenderer(
    borderRenderer,
    backgroundRenderer,
    contentRenderer,
    _logger
);

// Calculate bounds (scaling handled here, before creating WindowBounds)
int currentScale = CameraTransformUtility.GetViewportScale(camera, _constants.Get<int>("GbaReferenceWidth"));
int tileSize = tilesheetDef.TileWidth * currentScale;
int interiorWidth = _constants.Get<int>("MessageBoxInteriorWidth") * currentScale;
int interiorHeight = _constants.Get<int>("MessageBoxInteriorHeight") * currentScale;
// Calculate position based on GBA tile coordinates (scaled)...
// MessageBox has non-uniform border: 2 tiles left, 1 tile top/bottom/right
int outerX = interiorX - (2 * tileSize); // 2 tiles on left
int outerY = interiorY - tileSize; // 1 tile on top
int outerWidth = interiorWidth + (2 * tileSize) + tileSize; // 2 tiles left + 1 tile right
int outerHeight = interiorHeight + (tileSize * 2); // 1 tile top + 1 tile bottom

var bounds = new WindowBounds(
    outerX,
    outerY,
    outerWidth,
    outerHeight,
    interiorX,
    interiorY,
    interiorWidth,
    interiorHeight
);

// Render (no scale parameter - coordinates already scaled)
windowRenderer.Render(_spriteBatch, bounds);
```

## Migration Strategy

### Phase 1: Create Core Infrastructure

1. Create `UI/Windows` namespace and interfaces
2. Implement `WindowBounds` structure
3. Implement `WindowRenderer` class
4. Create base border/background/content renderer interfaces

### Phase 2: Implement Concrete Renderers

1. Extract border rendering logic from `MapPopupRendererSystem` → `PopupTileSheetBorderRenderer`
2. Extract border rendering logic from `MessageBoxSceneSystem` → `MessageBoxDialogueFrameBorderRenderer`
3. Extract background rendering logic → `BitmapBackgroundRenderer`, `TileSheetBackgroundRenderer`
4. Extract content rendering logic → `SimpleTextContentRenderer`, `MessageBoxContentRenderer`

### Phase 3: Refactor Existing Systems

1. Refactor `MapPopupRendererSystem` to use `WindowRenderer`
2. Refactor `MessageBoxSceneSystem` to use `WindowRenderer`
3. Verify behavior matches existing implementation
4. Remove duplicate code

### Phase 4: Add Position/Size Calculators (Optional)

1. Create position calculator interfaces and implementations
2. Extract position calculation logic
3. Simplify window creation code

## Benefits

1. **Code Reuse**: Common window functionality is shared
2. **Consistency**: All windows use the same coordinate system and rendering order
3. **Maintainability**: Changes to window rendering affect all windows
4. **Extensibility**: Easy to add new window types by implementing interfaces
5. **Testability**: Renderers can be tested independently
6. **Separation of Concerns**: Border, background, and content rendering are separated

## Future Enhancements

1. **Window Animations**: Build a general window animation system to support slide in/out, fade, etc.
    - **Design Document**: See `window-animation-system-design.md` for complete design
    - Migrate map popup animations (`PopupAnimationComponent`) to use the window animation system
    - Support common animation types: slide down/up, fade in/out, scale in/out, combined animations
    - Allow windows to specify animation parameters (duration, easing, etc.)
    - Map popups currently use slide down → pause → slide up animation pattern
    - Uses Arch ECS components (`WindowAnimationComponent`) and events (`WindowAnimationCompletedEvent`)
    - `WindowRenderer` will accept optional `WindowAnimationComponent` to apply transformations

2. **Window Themes**: Support theme switching for TextWindow numbered sprites (Window1, Window2, etc.)
    - Needed when implementing 9-slice renderer for TextWindow numbered sprites
    - Allow switching between different window frame styles at runtime

## File Structure

```
MonoBall.Core/
├── UI/
│   └── Windows/
│       ├── WindowBounds.cs
│       ├── WindowRenderer.cs
│       ├── WindowConfiguration.cs
│       ├── IBorderRenderer.cs
│       ├── IBackgroundRenderer.cs
│       ├── IContentRenderer.cs
│       ├── IWindowPositionCalculator.cs
│       ├── IWindowSizeCalculator.cs
│       ├── Borders/
│       │   ├── PopupTileSheetBorderRenderer.cs
│       │   └── MessageBoxDialogueFrameBorderRenderer.cs
│       ├── Backgrounds/
│       │   ├── BitmapBackgroundRenderer.cs
│       │   └── TileSheetBackgroundRenderer.cs
│       └── Content/
│           ├── SimpleTextContentRenderer.cs
│           └── MessageBoxContentRenderer.cs
```

## Notes

- All renderers should cache textures and definitions to avoid repeated lookups
- Renderers should validate required dependencies and throw exceptions if missing (fail fast)
- Window bounds calculations should be consistent across all window types
- Coordinate system: Interior bounds are used for content, border is drawn around interior
- Scaling is handled by the caller (scene/camera system) before creating WindowBounds or calling renderers
- If renderers need scale internally (e.g., for tile size calculations), it should be passed to their constructor

