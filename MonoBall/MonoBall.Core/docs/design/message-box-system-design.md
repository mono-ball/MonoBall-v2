# Message Box System Design

## Overview

This document describes the design for implementing a message box system with character-by-character typewriting
animation, based on the Pokemon Emerald (GBA) implementation. The system will integrate with MonoBall's ECS
architecture, event-driven design, and scripting API.

## Goals

1. **Replicate Pokemon Emerald message box behavior**: Character-by-character typewriting with configurable speed
2. **ECS/Event-First Architecture**: Use Arch ECS components and systems with event-driven communication
3. **Script API Integration**: Enable scripts to show messages via simple API calls
4. **Input Handling**: Support A/B button presses to speed up or advance text
5. **Rendering**: Use existing message box tilesheet (`base:textwindow:tilesheet/message_box`)

## Reference Implementation Analysis

### Pokemon Emerald (pokeemerald-expansion) Key Components

#### Text Printer System

- **TextPrinter**: State machine for rendering text character-by-character
- **States**: `RENDER_STATE_HANDLE_CHAR`, `RENDER_STATE_WAIT`, `RENDER_STATE_PAUSE`, `RENDER_STATE_CLEAR`
- **Delay Counter**: Frame-based delay between characters (controlled by text speed)
- **Button Speed-Up**: A/B button can skip delay when `canABSpeedUpPrint` flag is set
- **Auto-Scroll**: Optional automatic scrolling for long text

#### Message Box System

- **ShowFieldMessage()**: Shows message box with text
- **Task_DrawFieldMessage()**: Task-based rendering system
- **Window Rendering**: Tile-based window frame rendering
- **Text Flags**: `canABSpeedUpPrint`, `autoScroll`, `useAlternateDownArrow`

#### Text Speed System

- **Player Text Speed**: Configurable speed (Slow/Medium/Fast/Instant)
- **Speed Modifiers**: Multiplicative modifiers for each speed level
- **Instant Mode**: Renders all text immediately (for testing/debugging)

## Architecture Design

### Scene-Based Architecture

The message box system follows the established scene architecture pattern:

- **Message Box Scene**: An ECS entity with `SceneComponent` + `MessageBoxSceneComponent` (marker)
- **MessageBoxSceneSystem**: Implements `ISceneSystem` to handle update/render for message box scenes
- **Scene Lifecycle**: Managed by `SceneSystem` (create/destroy/activate)
- **Priority**: `ScenePriorities.GameScene + 20` (70) - above game scene, below loading/debug overlays

#### Scene Priority Hierarchy

```
DebugOverlay (100)        - Debug bar, console
LoadingScreen (75)        - Loading screens
MessageBoxScene (70)      - Message boxes (GameScene + 20)
GameScene (50)            - Main game scene
Background (0)            - Background scenes
```

#### Scene Configuration

- **CameraMode**: `SceneCameraMode.GameCamera` (uses game camera for proper scaling from GBA sprites)
- **BlocksUpdate**: `true` (blocks game updates when message box is active)
- **BlocksDraw**: `false` (allows game to render behind message box)
- **Priority**: `ScenePriorities.GameScene + 20` (renders above game scene)

### ECS Components

#### `MessageBoxSceneComponent`

```csharp
namespace MonoBall.Core.Scenes.Components
{
    /// <summary>
    /// Marker component that identifies a scene entity as a MessageBoxScene.
    /// MessageBoxScene renders message boxes with character-by-character typewriting animation.
    /// </summary>
    /// <remarks>
    /// <para>
    /// MessageBoxScene displays text in a message box window with typewriting effect.
    /// Supports A/B button input for speed-up and text advancement.
    /// </para>
    /// <para>
    /// Scene entities with MessageBoxSceneComponent should have SceneComponent with:
    /// - CameraMode = SceneCameraMode.GameCamera (uses game camera for proper scaling from GBA sprites)
    /// - Priority = ScenePriorities.GameScene + 20 (70) - above game scene, below loading/debug
    /// - BlocksUpdate = true (blocks game updates when active)
    /// - BlocksDraw = false (allows game to render behind message box)
    /// </para>
    /// </remarks>
    public struct MessageBoxSceneComponent
    {
        // Marker component - no data needed
    }
}
```

#### `MessageBoxComponent`

```csharp
namespace MonoBall.Core.Scenes.Components
{
    /// <summary>
    /// Component that stores message box state and text printing data.
    /// Attached to message box scene entities (entities with MessageBoxSceneComponent).
    /// </summary>
    /// <remarks>
    /// This component stores pre-parsed text tokens and wrapped lines for performance.
    /// Text parsing and wrapping happen once when the message box is created, not every frame.
    /// </remarks>
    public struct MessageBoxComponent
    {
        /// <summary>
        /// The full original text to display (may contain control codes).
        /// Stored for reference and debugging.
        /// </summary>
        public string Text { get; set; }

        /// <summary>
        /// Pre-parsed text tokens (control codes parsed into tokens).
        /// Parsed once when message box is created for performance.
        /// </summary>
        public List<TextToken>? ParsedText { get; set; }

        /// <summary>
        /// Pre-wrapped text lines (text broken into lines based on pixel width).
        /// Wrapped once when message box is created for performance.
        /// </summary>
        public List<WrappedLine>? WrappedLines { get; set; }

        /// <summary>
        /// The current position in the text string (character index).
        /// </summary>
        public int CurrentCharIndex { get; set; }

        /// <summary>
        /// Text speed setting (0 = instant, higher = slower).
        /// Maps to player text speed preference.
        /// </summary>
        public int TextSpeed { get; set; }

        /// <summary>
        /// Frame delay counter (decrements each frame, when 0, advance character).
        /// </summary>
        public int DelayCounter { get; set; }

        /// <summary>
        /// Current rendering state.
        /// </summary>
        public MessageBoxRenderState State { get; set; }

        /// <summary>
        /// Whether A/B button can speed up printing.
        /// </summary>
        public bool CanSpeedUpWithButton { get; set; }

        /// <summary>
        /// Whether text should auto-scroll (for long messages).
        /// </summary>
        public bool AutoScroll { get; set; }

        /// <summary>
        /// Whether the player has pressed A/B to speed up (prevents repeated speed-up).
        /// </summary>
        public bool HasBeenSpedUp { get; set; }

        /// <summary>
        /// Message box window ID (for rendering multiple boxes in future).
        /// Currently always 0 (single message box).
        /// </summary>
        public int WindowId { get; set; }

        /// <summary>
        /// Text color (foreground).
        /// </summary>
        public Color TextColor { get; set; }

        /// <summary>
        /// Background color.
        /// </summary>
        public Color BackgroundColor { get; set; }

        /// <summary>
        /// Shadow color for text.
        /// </summary>
        public Color ShadowColor { get; set; }

        /// <summary>
        /// Font ID to use (maps to font definition).
        /// </summary>
        public string FontId { get; set; }

        /// <summary>
        /// Current X position in the message box (in characters).
        /// </summary>
        public int CurrentX { get; set; }

        /// <summary>
        /// Current Y position in the message box (in characters).
        /// </summary>
        public int CurrentY { get; set; }

        /// <summary>
        /// Starting X position (for line wrapping).
        /// </summary>
        public int StartX { get; set; }

        /// <summary>
        /// Starting Y position (for line wrapping).
        /// </summary>
        public int StartY { get; set; }

        /// <summary>
        /// Letter spacing (pixels between characters).
        /// </summary>
        public int LetterSpacing { get; set; }

        /// <summary>
        /// Line spacing (pixels between lines).
        /// </summary>
        public int LineSpacing { get; set; }

        /// <summary>
        /// Whether the message box is visible (rendering state).
        /// </summary>
        public bool IsVisible { get; set; }

        /// <summary>
        /// Whether the message box is waiting for player input to continue.
        /// </summary>
        public bool IsWaitingForInput { get; set; }
    }
}
```

#### Helper Types for Text Processing

#### `TextToken` and `TextTokenType`

```csharp
namespace MonoBall.Core.Scenes.Components
{
    /// <summary>
    /// Represents a parsed text token (character or control code).
    /// Used for efficient text processing (pre-parsed, not parsed every frame).
    /// </summary>
    public struct TextToken
    {
        /// <summary>
        /// The type of token.
        /// </summary>
        public TextTokenType TokenType { get; set; }

        /// <summary>
        /// The value of the token (character, pause frames, color values, etc.).
        /// </summary>
        public object? Value { get; set; }

        /// <summary>
        /// The original position in the text string.
        /// </summary>
        public int OriginalPosition { get; set; }
    }

    /// <summary>
    /// Types of text tokens.
    /// </summary>
    public enum TextTokenType
    {
        /// <summary>
        /// Regular character.
        /// </summary>
        Char,

        /// <summary>
        /// Newline character.
        /// </summary>
        Newline,

        /// <summary>
        /// Pause control code.
        /// </summary>
        Pause,

        /// <summary>
        /// Pause until button press.
        /// </summary>
        PauseUntilPress,

        /// <summary>
        /// Color change control code.
        /// </summary>
        Color,

        /// <summary>
        /// Speed change control code.
        /// </summary>
        Speed,

        /// <summary>
        /// Clear page control code.
        /// </summary>
        Clear
    }
}
```

#### `WrappedLine`

```csharp
namespace MonoBall.Core.Scenes.Components
{
    /// <summary>
    /// Represents a wrapped line of text.
    /// Used for efficient text rendering (pre-wrapped, not wrapped every frame).
    /// </summary>
    public struct WrappedLine
    {
        /// <summary>
        /// The text substring for this line.
        /// </summary>
        public string Text { get; set; }

        /// <summary>
        /// The start character index in the original text.
        /// </summary>
        public int StartIndex { get; set; }

        /// <summary>
        /// The end character index in the original text (exclusive).
        /// </summary>
        public int EndIndex { get; set; }

        /// <summary>
        /// The pixel width of this line.
        /// </summary>
        public float Width { get; set; }
    }
}
```

#### `MessageBoxRenderState` Enum

```csharp
namespace MonoBall.Core.Scenes.Components
{
    /// <summary>
    /// Rendering state for message box text printing.
    /// </summary>
    public enum MessageBoxRenderState
    {
        /// <summary>
        /// Handling current character (normal printing state).
        /// </summary>
        HandleChar,

        /// <summary>
        /// Waiting for player input (A/B button press).
        /// </summary>
        Wait,

        /// <summary>
        /// Paused (for control codes like PAUSE).
        /// </summary>
        Pause,

        /// <summary>
        /// Clearing text (for page breaks).
        /// </summary>
        Clear,

        /// <summary>
        /// Finished printing (all text displayed).
        /// </summary>
        Finished,

        /// <summary>
        /// Hidden (not visible).
        /// </summary>
        Hidden
    }
}
```

### ECS Events

#### `MessageBoxShowEvent`

```csharp
namespace MonoBall.Core.ECS.Events
{
    /// <summary>
    /// Event fired when a message box should be shown.
    /// </summary>
    public struct MessageBoxShowEvent
    {
        /// <summary>
        /// The text to display.
        /// </summary>
        public string Text { get; set; }

        /// <summary>
        /// Text speed override (null = use player preference).
        /// </summary>
        public int? TextSpeedOverride { get; set; }

        /// <summary>
        /// Whether A/B button can speed up printing.
        /// </summary>
        public bool CanSpeedUpWithButton { get; set; }

        /// <summary>
        /// Whether text should auto-scroll.
        /// </summary>
        public bool AutoScroll { get; set; }

        /// <summary>
        /// Font ID to use (null = default).
        /// </summary>
        public string? FontId { get; set; }

        /// <summary>
        /// Text colors (null = use defaults).
        /// </summary>
        public Color? TextColor { get; set; }
        public Color? BackgroundColor { get; set; }
        public Color? ShadowColor { get; set; }
    }
}
```

#### `MessageBoxHideEvent`

```csharp
namespace MonoBall.Core.ECS.Events
{
    /// <summary>
    /// Event fired when a message box should be hidden.
    /// </summary>
    public struct MessageBoxHideEvent
    {
        /// <summary>
        /// Optional window ID (0 = all message boxes).
        /// </summary>
        public int WindowId { get; set; }
    }
}
```

#### `MessageBoxTextAdvanceEvent`

```csharp
namespace MonoBall.Core.ECS.Events
{
    /// <summary>
    /// Event fired when player presses A/B to advance text.
    /// </summary>
    public struct MessageBoxTextAdvanceEvent
    {
        /// <summary>
        /// Window ID of the message box.
        /// </summary>
        public int WindowId { get; set; }
    }
}
```

#### `MessageBoxTextFinishedEvent`

```csharp
namespace MonoBall.Core.ECS.Events
{
    /// <summary>
    /// Event fired when message box finishes printing all text.
    /// </summary>
    public struct MessageBoxTextFinishedEvent
    {
        /// <summary>
        /// Window ID of the message box.
        /// </summary>
        public int WindowId { get; set; }
    }
}
```

### ECS Systems

#### `MessageBoxSceneSystem`

**Purpose**: Manages message box scene lifecycle, text printing state machine, input handling, and rendering.

**Architecture**: Implements `ISceneSystem` interface following the established scene pattern.

**Responsibilities**:

- Handle `MessageBoxShowEvent` - Create message box scene entity
- Handle `MessageBoxHideEvent` - Destroy message box scene entity
- Update text printing state machine each frame (`ProcessInternal`)
- Process character-by-character printing
- Handle control codes (newline, pause, color changes, etc.)
- Manage delay counters and text speed
- Handle input (A/B button presses) for speed-up and advancement
- Render message box window frame and text (`RenderScene`)

**Interface Implementation**:

```csharp
namespace MonoBall.Core.Scenes.Systems
{
    /// <summary>
    /// System that handles lifecycle, updates, and rendering for MessageBoxScene entities.
    /// Manages message box text printing, input handling, and rendering.
    /// </summary>
    public class MessageBoxSceneSystem : BaseSystem<World, float>, IPrioritizedSystem, IDisposable, ISceneSystem
    {
        private readonly ISceneManager _sceneManager;
        private readonly FontService _fontService;
        private readonly IModManager _modManager;
        private readonly IInputBindingService _inputBindingService;
        private readonly IFlagVariableService _flagVariableService;
        private readonly GraphicsDevice _graphicsDevice;
        private readonly SpriteBatch _spriteBatch;
        private readonly ILogger _logger;

        private bool _disposed = false;

        // Track active message box scene entity (enforce single message box)
        private Entity? _activeMessageBoxSceneEntity;

        // Cached QueryDescription (created in constructor, never in hot paths)
        private readonly QueryDescription _messageBoxScenesQuery;

        /// <summary>
        /// Gets the execution priority for this system.
        /// </summary>
        /// <remarks>
        /// Note: SystemPriority.MessageBoxScene constant needs to be added to SystemPriority.cs.
        /// Suggested value: 360 (after MapPopupScene at 350, before MapPopupOrchestrator at 400).
        /// </remarks>
        public int Priority => SystemPriority.MessageBoxScene;

        /// <summary>
        /// Initializes a new instance of the MessageBoxSceneSystem.
        /// </summary>
        /// <param name="world">The ECS world. Required.</param>
        /// <param name="sceneManager">The scene manager for creating/destroying scenes. Required.</param>
        /// <param name="fontService">The font service for text rendering. Required.</param>
        /// <param name="modManager">The mod manager for accessing definitions. Required.</param>
        /// <param name="inputBindingService">The input binding service for button detection. Required.</param>
        /// <param name="flagVariableService">The flag/variable service for text speed preference. Required.</param>
        /// <param name="graphicsDevice">The graphics device. Required.</param>
        /// <param name="spriteBatch">The sprite batch for rendering. Required.</param>
        /// <param name="logger">The logger for logging operations. Required.</param>
        public MessageBoxSceneSystem(
            World world,
            ISceneManager sceneManager,
            FontService fontService,
            IModManager modManager,
            IInputBindingService inputBindingService,
            IFlagVariableService flagVariableService,
            GraphicsDevice graphicsDevice,
            SpriteBatch spriteBatch,
            ILogger logger
        ) : base(world)
        {
            _sceneManager = sceneManager ?? throw new ArgumentNullException(nameof(sceneManager));
            _fontService = fontService ?? throw new ArgumentNullException(nameof(fontService));
            _modManager = modManager ?? throw new ArgumentNullException(nameof(modManager));
            _inputBindingService = inputBindingService ?? throw new ArgumentNullException(nameof(inputBindingService));
            _flagVariableService = flagVariableService ?? throw new ArgumentNullException(nameof(flagVariableService));
            _graphicsDevice = graphicsDevice ?? throw new ArgumentNullException(nameof(graphicsDevice));
            _spriteBatch = spriteBatch ?? throw new ArgumentNullException(nameof(spriteBatch));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            // Cache QueryDescription in constructor (never create in Update/Render)
            _messageBoxScenesQuery = new QueryDescription()
                .WithAll<SceneComponent, MessageBoxSceneComponent, MessageBoxComponent>();

            // Subscribe to events (must unsubscribe in Dispose)
            EventBus.Subscribe<MessageBoxShowEvent>(OnMessageBoxShow);
            EventBus.Subscribe<MessageBoxHideEvent>(OnMessageBoxHide);
            EventBus.Subscribe<MessageBoxTextAdvanceEvent>(OnMessageBoxTextAdvance);
        }

        /// <summary>
        /// Updates a specific message box scene entity.
        /// Implements ISceneSystem interface.
        /// </summary>
        /// <param name="sceneEntity">The scene entity to update.</param>
        /// <param name="deltaTime">The elapsed time since last update.</param>
        /// <remarks>
        /// Most update logic is handled in ProcessInternal().
        /// This method exists to satisfy ISceneSystem interface.
        /// </remarks>
        public void Update(Entity sceneEntity, float deltaTime)
        {
            // Per-scene update (if needed)
            // Most logic handled in ProcessInternal()
        }

        /// <summary>
        /// Renders a specific message box scene entity.
        /// Implements ISceneSystem interface.
        /// </summary>
        /// <param name="sceneEntity">The scene entity to render.</param>
        /// <param name="gameTime">The game time.</param>
        /// <exception cref="InvalidOperationException">Thrown if scene entity does not have MessageBoxComponent.</exception>
        public void RenderScene(Entity sceneEntity, GameTime gameTime)
        {
            if (!World.Has<MessageBoxComponent>(sceneEntity))
            {
                throw new InvalidOperationException(
                    $"Scene entity {sceneEntity.Id} does not have MessageBoxComponent. " +
                    "Cannot render message box without component."
                );
            }

            ref var msgBox = ref World.Get<MessageBoxComponent>(sceneEntity);
            if (!msgBox.IsVisible || msgBox.State == MessageBoxRenderState.Hidden)
            {
                return;
            }

            RenderMessageBox(sceneEntity, ref msgBox, gameTime);
        }

        /// <summary>
        /// Performs internal processing that needs to run every frame.
        /// Updates text printing state machine, handles input, and processes character-by-character printing.
        /// </summary>
        /// <param name="deltaTime">The elapsed time since last update.</param>
        public void ProcessInternal(float deltaTime)
        {
            // Query for active message box scenes (only process active scene)
            if (!_activeMessageBoxSceneEntity.HasValue)
            {
                return;
            }

            var sceneEntity = _activeMessageBoxSceneEntity.Value;
            if (!World.Has<MessageBoxComponent>(sceneEntity))
            {
                return;
            }

            ref var msgBox = ref World.Get<MessageBoxComponent>(sceneEntity);

            if (msgBox.State == MessageBoxRenderState.Hidden)
            {
                return;
            }

            // Handle input (A/B button presses) - only for active message box
            HandleInput(sceneEntity, ref msgBox);

            // Update delay counter
            if (msgBox.DelayCounter > 0)
            {
                msgBox.DelayCounter--;
                return; // Wait for delay
            }

            // Process current character
            ProcessCharacter(sceneEntity, ref msgBox);
        }

        /// <summary>
        /// Disposes the system and unsubscribes from events.
        /// </summary>
        public new void Dispose() => Dispose(true);

        /// <summary>
        /// Protected dispose method following standard dispose pattern.
        /// </summary>
        /// <param name="disposing">True if called from Dispose(), false if called from finalizer.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed && disposing)
            {
                // Unsubscribe from events to prevent memory leaks
                EventBus.Unsubscribe<MessageBoxShowEvent>(OnMessageBoxShow);
                EventBus.Unsubscribe<MessageBoxHideEvent>(OnMessageBoxHide);
                EventBus.Unsubscribe<MessageBoxTextAdvanceEvent>(OnMessageBoxTextAdvance);

                // Clear tracked scene entity
                _activeMessageBoxSceneEntity = null;
            }
            _disposed = true;
        }
    }
}
```

**Event Subscriptions**:

- `MessageBoxShowEvent` - Create scene and show message box
- `MessageBoxHideEvent` - Destroy scene and hide message box
- `MessageBoxTextAdvanceEvent` - Handle button press advancement

**Scene Creation**:

```csharp
/// <summary>
/// Handles MessageBoxShowEvent by creating a new message box scene.
/// Enforces single message box policy (destroys existing message box if present).
/// </summary>
/// <param name="evt">The show event containing message text and options.</param>
private void OnMessageBoxShow(ref MessageBoxShowEvent evt)
{
    if (string.IsNullOrWhiteSpace(evt.Text))
    {
        _logger.Warning("MessageBoxShowEvent received with null or empty text, ignoring");
        return;
    }

    // Enforce single message box policy: destroy existing message box if present
    if (_activeMessageBoxSceneEntity.HasValue)
    {
        _logger.Debug("Destroying existing message box before showing new one");
        var hideEvent = new MessageBoxHideEvent { WindowId = 0 };
        EventBus.Send(ref hideEvent);
    }

    // Get player text speed preference (with fallback to default)
    int textSpeed = evt.TextSpeedOverride ?? GetPlayerTextSpeed();

    // Create scene entity with SceneComponent + MessageBoxSceneComponent
    var sceneEntity = _sceneManager.CreateScene(
        sceneId: $"messagebox:{Guid.NewGuid()}",
        priority: ScenePriorities.GameScene + MessageBoxConstants.ScenePriorityOffset,
        cameraMode: SceneCameraMode.GameCamera,  // Uses game camera for proper scaling from GBA sprites
        blocksUpdate: true,  // Block game updates when message box is active
        blocksDraw: false    // Allow game to render behind message box
    );

    // Add MessageBoxSceneComponent marker
    World.Add<MessageBoxSceneComponent>(sceneEntity);

    // Validate font exists (fail fast, no fallback)
    string fontId = evt.FontId ?? MessageBoxConstants.DefaultFontId;
    var fontSystem = _fontService.GetFontSystem(fontId);
    if (fontSystem == null)
    {
        throw new InvalidOperationException(
            $"Font '{fontId}' not found. Cannot create message box without valid font. " +
            $"Font must exist in mod registry."
        );
    }

    // Validate tilesheet exists (fail fast, no fallback)
    var tilesheet = _modManager.GetDefinition<TileSheetDefinition>(MessageBoxConstants.MessageBoxTilesheetId);
    if (tilesheet == null)
    {
        throw new InvalidOperationException(
            $"Tilesheet '{MessageBoxConstants.MessageBoxTilesheetId}' not found. " +
            $"Cannot create message box without tilesheet. Tilesheet must exist in mod registry."
        );
    }

    // Pre-parse control codes and wrap text (performance optimization)
    var parsedText = ParseControlCodes(evt.Text);
    var wrappedLines = WrapText(parsedText, fontId);

    // Add MessageBoxComponent with text and state
    World.Add<MessageBoxComponent>(sceneEntity, new MessageBoxComponent
    {
        Text = evt.Text, // Original text (for reference)
        ParsedText = parsedText, // Pre-parsed tokens
        WrappedLines = wrappedLines, // Pre-wrapped lines
        TextSpeed = textSpeed,
        DelayCounter = textSpeed,
        State = MessageBoxRenderState.HandleChar,
        CanSpeedUpWithButton = evt.CanSpeedUpWithButton,
        AutoScroll = evt.AutoScroll,
        HasBeenSpedUp = false,
        WindowId = 0, // Single message box always uses ID 0
        TextColor = evt.TextColor ?? Color.White,
        BackgroundColor = evt.BackgroundColor ?? Color.Transparent,
        ShadowColor = evt.ShadowColor ?? new Color(72, 72, 80, 255), // Dark gray
        FontId = evt.FontId ?? MessageBoxConstants.DefaultFontId,
        CurrentX = 0,
        CurrentY = 0,
        StartX = 0,
        StartY = 0,
        LetterSpacing = 0,
        LineSpacing = 2,
        IsVisible = true,
        IsWaitingForInput = false,
        CurrentCharIndex = 0
    });

    // Track active scene entity
    _activeMessageBoxSceneEntity = sceneEntity;

    _logger.Debug("Created message box scene {SceneId} with text: {Text}", sceneEntity.Id, evt.Text);
}

/// <summary>
/// Handles MessageBoxHideEvent by destroying the message box scene.
/// </summary>
/// <param name="evt">The hide event containing window ID.</param>
private void OnMessageBoxHide(ref MessageBoxHideEvent evt)
{
    if (evt.WindowId == 0)
    {
        // Hide all (single message box policy)
        if (_activeMessageBoxSceneEntity.HasValue)
        {
            var sceneEntity = _activeMessageBoxSceneEntity.Value;
            _sceneManager.DestroyScene(sceneEntity);
            _activeMessageBoxSceneEntity = null;
            _logger.Debug("Destroyed message box scene {SceneId}", sceneEntity.Id);
        }
    }
    else
    {
        // Hide specific window (future: multiple message boxes)
        _logger.Warning("MessageBoxHideEvent with WindowId {WindowId} not supported (single message box policy)", evt.WindowId);
    }
}

/// <summary>
/// Gets the player's text speed preference from global variables.
/// </summary>
/// <returns>The text speed in frames per character.</returns>
private int GetPlayerTextSpeed()
{
    string? speedStr = _flagVariableService.GetVariable<string>(MessageBoxConstants.TextSpeedVariableName);
    
    if (string.IsNullOrEmpty(speedStr))
    {
        speedStr = MessageBoxConstants.DefaultTextSpeed;
    }

    return speedStr.ToLowerInvariant() switch
    {
        "slow" => MessageBoxConstants.TextSpeedSlowFrames,
        "medium" => MessageBoxConstants.TextSpeedMediumFrames,
        "fast" => MessageBoxConstants.TextSpeedFastFrames,
        "instant" => MessageBoxConstants.TextSpeedInstantFrames,
        _ => MessageBoxConstants.TextSpeedMediumFrames // Default fallback
    };
}
```

**Update Logic** (`ProcessInternal`):

```csharp
public void ProcessInternal(float deltaTime)
{
    // Query for active message box scenes
    World.Query(in _messageBoxScenesQuery, (Entity sceneEntity, ref MessageBoxComponent msgBox) =>
    {
        if (msgBox.State == MessageBoxRenderState.Hidden)
            return;

        // Handle input (A/B button presses)
        HandleInput(sceneEntity, ref msgBox);

        // Update delay counter
        if (msgBox.DelayCounter > 0)
        {
            msgBox.DelayCounter--;
            return; // Wait for delay
        }

        // Process current character
        ProcessCharacter(sceneEntity, ref msgBox);
    });
}
```

**Character Processing**:

- Use pre-parsed tokens (from `ParsedText`) instead of parsing every frame
- Process current token based on `CurrentCharIndex`
- Handle control codes (newline, pause, color changes) from parsed tokens
- Handle newlines (use pre-wrapped lines from `WrappedLines`)
- Advance `CurrentCharIndex`
- Set delay counter based on text speed
- Check if finished (all tokens processed)
- Fire `MessageBoxTextFinishedEvent` when done

**Control Code Parsing** (Pre-processing, not in hot path):

- Parse control codes when message box is created
- Store as list of tokens: `List<TextToken>` where `TextToken` contains:
    - `TokenType` (Char, Newline, Pause, Color, Speed, Clear)
    - `Value` (character, pause frames, color values, etc.)
    - `Position` (original position in text)
- Handle escape sequences (`\{` for literal `{`)
- Validate control codes (throw exception for malformed codes)

**Text Wrapping** (Pre-processing, not in hot path):

- Use pixel-based wrapping (measure string width with font)
- Break words if necessary (hyphenate or truncate very long words)
- Store wrapped lines: `List<WrappedLine>` where each line contains:
    - `Text` (substring for this line)
    - `StartIndex` (character index in original text)
    - `EndIndex` (character index in original text)
    - `Width` (pixel width of line)

**Input Handling**:

- Check `InputAction.Interact` (A button) - primary interaction button
- **Note**: `InputAction` enum currently has `Interact` but no explicit `Cancel` action
- For B button functionality, check for `Keys.Escape` or add `InputAction.Cancel` to enum (future enhancement)
- If message box is printing: Speed up (set delay to 0, mark as sped up)
- If message box is waiting: Advance to next page or hide
- If message box is finished: Hide message box
- **Input Blocking**: When message box scene is active, it blocks game updates (`BlocksUpdate = true`)

**Rendering** (`RenderScene`):

- Render message box window frame using tilesheet (9-slice)
- Render text characters up to `CurrentCharIndex` (using pre-wrapped lines)
- Render down arrow indicator (when `IsWaitingForInput`)
- Position message box on screen (bottom of screen, centered)
- Use pre-calculated wrapped lines (no wrapping during rendering)

**Dependencies**:

- `ISceneManager` - For creating/destroying scenes (required, validated in constructor)
- `FontService` - For font loading and rendering (required, validated in constructor)
- `SpriteBatch` - For rendering text and sprites (required, validated in constructor)
- `GraphicsDevice` - For rendering operations (required, validated in constructor)
- `IModManager` - For accessing tilesheet definitions (required, validated in constructor)
- `IInputBindingService` - For checking input state (required, validated in constructor)
- `IFlagVariableService` - For text speed preference (required, validated in constructor)
- `ILogger` - For logging operations (required, validated in constructor)

**Tilesheet Usage**:

- Use `MessageBoxConstants.MessageBoxTilesheetId` (`base:textwindow:tilesheet/message_box`)
- Tiles 0-13 represent different parts of the message box frame
- 9-slice rendering: corners, edges, center fill
- Load tilesheet via `IModManager.GetDefinition<TileSheetDefinition>(tilesheetId)`
- **Error Handling**: If tilesheet not found, throw `InvalidOperationException` with clear message
- No fallback - fail fast per `.cursorrules` rule #2 (NO FALLBACK CODE)
- Tilesheet must exist in mod registry - validate during scene creation

**Font Usage**:

- Use font ID from `MessageBoxComponent.FontId` (default: `MessageBoxConstants.DefaultFontId`)
- Load font via `FontService.GetFontSystem(fontId)`
- **Error Handling**: If font not found, throw `InvalidOperationException` with clear message
- No fallback - fail fast per `.cursorrules` rule #2 (NO FALLBACK CODE)
- Font must exist in mod registry - validate during scene creation

**Text Rendering Details**:

- Use FontStashSharp `DynamicSpriteFont` for text rendering
- Text colors: White (255,255,255,255) for main text, Dark Gray (72,72,80,255) for shadow
- Shadow offset: 1 pixel down and 1 pixel right
- Fully opaque rendering (alpha = 255) to match GBA style
- Render shadow first, then main text on top
- Render substring up to `CurrentCharIndex` (batch rendering, not character-by-character)

### Script API Integration

#### `IMessageBoxApi` Interface

```csharp
namespace MonoBall.Core.Scripting.Api
{
    /// <summary>
    /// API for showing message boxes from scripts.
    /// </summary>
    public interface IMessageBoxApi
    {
        /// <summary>
        /// Shows a message box with the specified text.
        /// </summary>
        /// <param name="text">The text to display.</param>
        /// <param name="textSpeedOverride">Optional text speed override (null = use player preference).</param>
        void ShowMessage(string text, int? textSpeedOverride = null);

        /// <summary>
        /// Hides the current message box.
        /// </summary>
        void HideMessage();

        /// <summary>
        /// Checks if a message box is currently visible.
        /// </summary>
        bool IsMessageBoxVisible();
    }
}
```

#### Implementation

```csharp
namespace MonoBall.Core.Scripting.Api
{
    public class MessageBoxApi : IMessageBoxApi
    {
        private readonly World _world;
        private readonly EventBus _eventBus;

        public MessageBoxApi(World world, EventBus eventBus)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        }

        public void ShowMessage(string text, int? textSpeedOverride = null)
        {
            if (string.IsNullOrWhiteSpace(text))
                throw new ArgumentException("Text cannot be null or whitespace.", nameof(text));

            var evt = new MessageBoxShowEvent
            {
                Text = text,
                TextSpeedOverride = textSpeedOverride,
                CanSpeedUpWithButton = true,
                AutoScroll = false
            };

            _eventBus.Send(ref evt);
        }

        public void HideMessage()
        {
            var evt = new MessageBoxHideEvent { WindowId = 0 };
            _eventBus.Send(ref evt);
        }

        public bool IsMessageBoxVisible()
        {
            // Query for active message box
            var query = new QueryDescription().WithAll<MessageBoxComponent>();
            bool found = false;
            _world.Query(in query, (ref MessageBoxComponent msgBox) =>
            {
                if (msgBox.IsVisible && msgBox.State != MessageBoxRenderState.Hidden)
                {
                    found = true;
                }
            });
            return found;
        }
    }
}
```

### Text Speed System

#### Player Text Speed Preference

- **Storage**: Stored in global variables via `IFlagVariableService`
- **Variable Name**: `"player:textSpeed"` (string value: "slow", "medium", "fast", "instant")
- **Default**: "medium" if not set
- **Options**: Slow, Medium, Fast, Instant
- Maps to delay values (frames per character):
    - Slow: 8 frames per character
    - Medium: 4 frames per character
    - Fast: 2 frames per character
    - Instant: 0 frames (render all immediately)

#### Text Speed Modifiers

- **Storage**: Configurable in mod definitions or config files (future enhancement)
- **Application**: Multiplicative (`actualDelay = baseDelay * modifier`)
- **Default Modifier**: 1.0 (no change)
- Allows fine-tuning of text speed
- Can be adjusted per-message if needed

#### Constants

```csharp
namespace MonoBall.Core.Scenes.Components
{
    /// <summary>
    /// Constants for message box text speed and rendering.
    /// </summary>
    public static class MessageBoxConstants
    {
        /// <summary>
        /// Scene priority offset above GameScene (70 = GameScene + 20).
        /// </summary>
        public const int ScenePriorityOffset = 20;

        /// <summary>
        /// Text speed delay values (frames per character).
        /// </summary>
        public const int TextSpeedSlowFrames = 8;
        public const int TextSpeedMediumFrames = 4;
        public const int TextSpeedFastFrames = 2;
        public const int TextSpeedInstantFrames = 0;

        /// <summary>
        /// Default text speed variable name in global variables.
        /// </summary>
        public const string TextSpeedVariableName = "player:textSpeed";

        /// <summary>
        /// Default text speed value if not set.
        /// </summary>
        public const string DefaultTextSpeed = "medium";

        /// <summary>
        /// Default font ID to use if font not specified or not found.
        /// </summary>
        public const string DefaultFontId = "base:font:game/pokemon";

        /// <summary>
        /// Required tilesheet ID for message box rendering.
        /// </summary>
        public const string MessageBoxTilesheetId = "base:textwindow:tilesheet/message_box";
    }
}
```

### Control Codes

#### Supported Control Codes

- `\n` - Newline (wrap to next line)
- `\r` - Carriage return (reset to start of line)
- `{PAUSE:30}` - Pause for N frames (e.g., `{PAUSE:30}` pauses for 30 frames)
- `{PAUSE_UNTIL_PRESS}` - Wait for A/B button press
- `{COLOR:r,g,b}` - Change text color (e.g., `{COLOR:255,0,0}` for red)
- `{SPEED:n}` - Change text speed for remainder of message (e.g., `{SPEED:2}` for fast)
- `{CLEAR}` - Clear current text and start new page

#### Control Code Parsing Implementation

**Pre-Parsing Strategy** (Performance Optimization):

- Parse control codes once when message box is created
- Store as `List<TextToken>` for efficient processing
- Don't parse during character-by-character printing

**TextToken Structure**:

```csharp
namespace MonoBall.Core.Scenes.Components
{
    /// <summary>
    /// Represents a parsed text token (character or control code).
    /// </summary>
    public struct TextToken
    {
        /// <summary>
        /// The type of token.
        /// </summary>
        public TextTokenType TokenType { get; set; }

        /// <summary>
        /// The value of the token (character, pause frames, color values, etc.).
        /// </summary>
        public object? Value { get; set; }

        /// <summary>
        /// The original position in the text string.
        /// </summary>
        public int OriginalPosition { get; set; }
    }

    /// <summary>
    /// Types of text tokens.
    /// </summary>
    public enum TextTokenType
    {
        /// <summary>
        /// Regular character.
        /// </summary>
        Char,

        /// <summary>
        /// Newline character.
        /// </summary>
        Newline,

        /// <summary>
        /// Pause control code.
        /// </summary>
        Pause,

        /// <summary>
        /// Pause until button press.
        /// </summary>
        PauseUntilPress,

        /// <summary>
        /// Color change control code.
        /// </summary>
        Color,

        /// <summary>
        /// Speed change control code.
        /// </summary>
        Speed,

        /// <summary>
        /// Clear page control code.
        /// </summary>
        Clear
    }
}
```

**Parsing Algorithm**:

1. Iterate through text string character-by-character
2. Detect control code start (`{`)
3. Parse control code type and parameters
4. Handle escape sequences (`\{` for literal `{`)
5. Validate control code format (throw exception for malformed codes)
6. Create `TextToken` and add to list
7. Continue until end of string

**Error Handling**:

- Malformed control codes: Throw `ArgumentException` with position information
- Invalid color values: Throw `ArgumentException` with clear message
- Invalid speed values: Throw `ArgumentException` with clear message
- Unclosed control codes: Throw `ArgumentException` with position information

### Font System

#### Font Definition

- Font definitions stored in mod registry
- Font properties: `maxLetterWidth`, `maxLetterHeight`, `letterSpacing`, `lineSpacing`
- Font rendering: Character glyph lookup and rendering

#### Font Rendering

- **Existing System**: Use `FontService` (already implemented) which uses FontStashSharp
- **Font Loading**: Fonts loaded via `FontService.GetFontSystem(fontId)`
- **Font Rendering**: Use `FontSystem.GetFont(size)` to get `DynamicSpriteFont` for rendering
- **Character Width**: Use `font.MeasureString(char)` for character width calculation
- **Text Rendering**: Use `font.DrawText(spriteBatch, text, position, color)` for rendering
- **Default Font**: Use existing font definitions (e.g., `base:font:debug/mono` or create message box specific font)

**Note**: The existing `FontService` already handles font loading and caching. The message box system should inject
`FontService` and use it for text rendering.

### Message Box Positioning

#### Screen Position

- Default: Bottom of screen, centered horizontally
- Configurable: Top, Bottom, Center, Custom position (future enhancement)
- Account for screen resolution changes
- Use game camera coordinates (not screen space) for proper scaling

#### Size Calculation

- Calculate message box size based on pre-wrapped text lines
- Maximum width: Screen width - margins (in game pixels, accounting for camera scale)
- Maximum height: Configurable (default: 4 lines)
- Auto-resize for long messages (with scrolling support)

**Text Wrapping Algorithm** (Pre-processing):

- Use pixel-based wrapping (measure string width with font)
- Break at word boundaries when possible
- Break words if necessary (hyphenate or truncate very long words)
- Store wrapped lines: `List<WrappedLine>`

**WrappedLine Structure**:

```csharp
namespace MonoBall.Core.Scenes.Components
{
    /// <summary>
    /// Represents a wrapped line of text.
    /// </summary>
    public struct WrappedLine
    {
        /// <summary>
        /// The text substring for this line.
        /// </summary>
        public string Text { get; set; }

        /// <summary>
        /// The start character index in the original text.
        /// </summary>
        public int StartIndex { get; set; }

        /// <summary>
        /// The end character index in the original text (exclusive).
        /// </summary>
        public int EndIndex { get; set; }

        /// <summary>
        /// The pixel width of this line.
        /// </summary>
        public float Width { get; set; }
    }
}
```

**Wrapping Algorithm**:

1. Measure text width using font (`font.MeasureString()`)
2. If text fits on line, add as single line
3. If text doesn't fit, find last word boundary that fits
4. Break at word boundary and continue on next line
5. If word is too long for single line, break word (hyphenate or truncate)
6. Repeat until all text is wrapped
7. Store wrapped lines in `WrappedLines` list

## Implementation Phases

### Phase 1: Core Components and Events

1. Create `MessageBoxConstants` class in `Scenes/Components/` with all constants
2. Create `MessageBoxSceneComponent` marker struct in `Scenes/Components/`
3. Create `MessageBoxComponent` struct in `Scenes/Components/`
4. Create `MessageBoxRenderState` enum in `Scenes/Components/`
5. Create helper types: `TextToken`, `TextTokenType`, `WrappedLine` in `Scenes/Components/`
6. Create events: `MessageBoxShowEvent`, `MessageBoxHideEvent`, `MessageBoxTextAdvanceEvent`,
   `MessageBoxTextFinishedEvent` in `ECS/Events/`
7. Create `MessageBoxSceneSystem` skeleton in `Scenes/Systems/` (implements `ISceneSystem`)
    - Add constructor with all required dependencies (validate nulls)
    - Cache `QueryDescription` in constructor
    - Implement `IDisposable` with event unsubscription
    - Add XML documentation to all public methods

### Phase 2: Scene Integration

1. Register `MessageBoxSceneSystem` with `SceneSystem` in `SystemManager`
    - Call `sceneSystem.RegisterSceneSystem(typeof(MessageBoxSceneComponent), messageBoxSceneSystem)`
2. Implement scene creation in `OnMessageBoxShow` event handler
    - Use `ISceneManager.CreateScene()` with:
        - Priority: `ScenePriorities.GameScene + 20`
        - CameraMode: `SceneCameraMode.GameCamera` (for proper scaling from GBA sprites)
        - BlocksUpdate: `true`
        - BlocksDraw: `false`
3. Implement scene destruction in `OnMessageBoxHide` event handler
    - Use `ISceneManager.DestroyScene()`
4. Test scene lifecycle (create/destroy)

### Phase 3: Text Processing Utilities

1. Implement `ParseControlCodes()` method - parse text into `TextToken` list
2. Implement `WrapText()` method - wrap text into `WrappedLine` list using font measurement
3. Handle escape sequences (`\{` for literal `{`)
4. Validate control codes (throw exceptions for malformed codes)
5. Test parsing with various control codes

### Phase 4: Text Printing System

1. Implement `ProcessInternal()` - process pre-parsed tokens (not character-by-character parsing)
2. Implement delay counter system
3. Implement text speed handling (use `GetPlayerTextSpeed()` with fallback)
4. Implement token processing (handle control codes from parsed tokens)
5. Use pre-wrapped lines for rendering (no wrapping during rendering)
6. Test with simple messages

### Phase 4: Input Handling

1. Implement input detection in `ProcessInternal()`
2. Implement A/B button detection
3. Implement speed-up logic
4. Implement advance-to-next-page logic
5. Test input responsiveness

### Phase 5: Rendering System

1. Implement `RenderScene()` method
2. Implement window frame rendering (9-slice)
3. Implement text rendering (using pre-wrapped lines, render substring up to `CurrentCharIndex`)
4. Implement down arrow indicator (when `IsWaitingForInput`)
5. Test visual appearance

### Phase 6: Script API Integration

1. Create `IMessageBoxApi` interface
2. Implement `MessageBoxApi` class
3. Register API in script context
4. Test from scripts
5. Update NPC interaction scripts in littleroot_town

### Phase 7: Polish and Features

1. Add advanced control codes (color, speed changes)
2. Add auto-scroll support
3. Add multiple message box support (stacking)
4. Add text wrapping improvements
5. Performance optimization

## Integration Points

### With Existing Systems

#### Scene System

- `MessageBoxSceneSystem` implements `ISceneSystem` interface
- Registered with `SceneSystem` via `RegisterSceneSystem(typeof(MessageBoxSceneComponent), messageBoxSceneSystem)`
- Scene lifecycle managed by `SceneSystem` (create/destroy/activate)
- Scene priority: `ScenePriorities.GameScene + 20` (70) - above game scene, below loading/debug overlays
- Blocks game updates when active (`BlocksUpdate = true`)

#### Input System

- `MessageBoxSceneSystem` queries `IInputBindingService` for button presses
- Scene blocks game updates when active (prevents player movement)
- Input handled in `ProcessInternal()` method

#### Rendering System

- `MessageBoxSceneSystem.RenderScene()` called by `SceneRendererSystem`
- Renders after game scene (due to higher priority)
- Uses `SpriteBatch` for rendering
- Integrates with existing scene rendering pipeline

#### Script System

- `MessageBoxApi` available in script context
- Scripts can call `Context.MessageBox.ShowMessage("text")`
- Events can be subscribed to in scripts for message box lifecycle

#### NPC Interaction System

- NPC interaction scripts can show messages
- Example: `Context.MessageBox.ShowMessage("Hello, trainer!")`

## Testing Strategy

### Unit Tests

- Control code parsing
- Text wrapping logic
- Delay counter calculations
- State machine transitions

### Integration Tests

- Event-driven message box creation/destruction
- Input handling with message box active
- Script API calls
- Rendering output verification

### Manual Testing

- Visual appearance matches Pokemon Emerald
- Text speed feels responsive
- Button presses work correctly
- Long messages wrap correctly
- Control codes work as expected

## Future Enhancements

1. **Speaker Name Boxes**: Display speaker names above message box (separate window, Pokemon Emerald style)
2. **Multiple Message Boxes**: Support stacking multiple message boxes
3. **Advanced Control Codes**: More control codes (sound effects, animations)
4. **Custom Fonts**: Support for custom font definitions
5. **Text Effects**: Fade-in, slide-in animations
6. **Choice Menus**: Yes/No or multi-choice menus in message boxes
7. **Portrait Support**: Display NPC portraits alongside text
8. **Localization**: Support for multiple languages and text directions

## References

- `pokeemerald-expansion/src/field_message_box.c` - Message box implementation
- `pokeemerald-expansion/src/text.c` - Text printer system
- `pokeemerald-expansion/include/text.h` - Text system definitions
- `Mods/pokemon-emerald/Definitions/TextWindow/message_box.json` - Message box tilesheet definition
- `Mods/pokemon-emerald/Definitions/Maps/Regions/Hoenn/littleroot_town.json` - NPC interaction examples

