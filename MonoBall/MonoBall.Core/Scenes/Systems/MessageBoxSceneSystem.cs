using System;
using Arch.Core;
using Arch.System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoBall.Core.ECS;
using MonoBall.Core.ECS.Components;
using MonoBall.Core.ECS.Events;
using MonoBall.Core.ECS.Input;
using MonoBall.Core.ECS.Services;
using MonoBall.Core.Mods;
using MonoBall.Core.Rendering;
using MonoBall.Core.Scenes;
using MonoBall.Core.Scenes.Components;
using Serilog;

namespace MonoBall.Core.Scenes.Systems
{
    /// <summary>
    /// System that handles lifecycle, updates, and rendering for MessageBoxScene entities.
    /// Manages message box text printing, input handling, and rendering.
    /// </summary>
    public class MessageBoxSceneSystem
        : BaseSystem<World, float>,
            IPrioritizedSystem,
            IDisposable,
            ISceneSystem
    {
        private readonly ISceneManager _sceneManager;
        private readonly FontService _fontService;
        private readonly IModManager _modManager;
        private readonly IInputBindingService _inputBindingService;
        private readonly IFlagVariableService _flagVariableService;
        private readonly ICameraService _cameraService;
        private readonly GraphicsDevice _graphicsDevice;
        private readonly SpriteBatch _spriteBatch;
        private readonly ILogger _logger;

        private bool _disposed = false;

        // Track active message box scene entity (enforce single message box)
        private Entity? _activeMessageBoxSceneEntity;

        // Cached QueryDescription (created in constructor, never in hot paths)
        private readonly QueryDescription _messageBoxScenesQuery;
        private readonly QueryDescription _cameraQuery;

        // Texture cache for message box tilesheet
        private Texture2D? _messageBoxTexture;

        /// <summary>
        /// Gets the execution priority for this system.
        /// </summary>
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
        /// <param name="cameraService">The camera service for querying active camera. Required.</param>
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
            ICameraService cameraService,
            GraphicsDevice graphicsDevice,
            SpriteBatch spriteBatch,
            ILogger logger
        )
            : base(world)
        {
            _sceneManager = sceneManager ?? throw new ArgumentNullException(nameof(sceneManager));
            _fontService = fontService ?? throw new ArgumentNullException(nameof(fontService));
            _modManager = modManager ?? throw new ArgumentNullException(nameof(modManager));
            _inputBindingService =
                inputBindingService ?? throw new ArgumentNullException(nameof(inputBindingService));
            _flagVariableService =
                flagVariableService ?? throw new ArgumentNullException(nameof(flagVariableService));
            _cameraService =
                cameraService ?? throw new ArgumentNullException(nameof(cameraService));
            _graphicsDevice =
                graphicsDevice ?? throw new ArgumentNullException(nameof(graphicsDevice));
            _spriteBatch = spriteBatch ?? throw new ArgumentNullException(nameof(spriteBatch));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            // Cache QueryDescription in constructor (never create in Update/Render)
            _messageBoxScenesQuery = new QueryDescription().WithAll<
                SceneComponent,
                MessageBoxSceneComponent,
                MessageBoxComponent
            >();
            // Camera query only needed for SceneCamera mode (querying by entity ID)
            _cameraQuery = new QueryDescription().WithAll<CameraComponent>();

            // Subscribe to events (must unsubscribe in Dispose)
            EventBus.Subscribe<MessageBoxShowEvent>(OnMessageBoxShow);
            EventBus.Subscribe<MessageBoxHideEvent>(OnMessageBoxHide);
            EventBus.Subscribe<MessageBoxTextAdvanceEvent>(OnMessageBoxTextAdvance);
        }

        /// <summary>
        /// Validates and retrieves a font system by font ID.
        /// </summary>
        /// <param name="fontId">The font ID to validate and retrieve.</param>
        /// <returns>The font system if found.</returns>
        /// <exception cref="InvalidOperationException">Thrown if font is not found.</exception>
        private FontStashSharp.FontSystem ValidateAndGetFont(string fontId)
        {
            var fontSystem = _fontService.GetFontSystem(fontId);
            if (fontSystem == null)
            {
                throw new InvalidOperationException(
                    $"Font '{fontId}' not found. Cannot create message box without valid font. "
                        + $"Font must exist in mod registry."
                );
            }
            return fontSystem;
        }

        /// <summary>
        /// Validates and retrieves the message box tilesheet definition.
        /// </summary>
        /// <returns>The tilesheet definition if found.</returns>
        /// <exception cref="InvalidOperationException">Thrown if tilesheet is not found.</exception>
        private PopupOutlineDefinition ValidateAndGetTilesheet()
        {
            var tilesheet = _modManager.GetDefinition<PopupOutlineDefinition>(
                MessageBoxConstants.MessageBoxTilesheetId
            );
            if (tilesheet == null)
            {
                throw new InvalidOperationException(
                    $"Tilesheet '{MessageBoxConstants.MessageBoxTilesheetId}' not found. "
                        + $"Cannot create message box without tilesheet. Tilesheet must exist in mod registry."
                );
            }
            return tilesheet;
        }

        /// <summary>
        /// Updates a specific message box scene entity.
        /// Implements ISceneSystem interface.
        /// </summary>
        /// <param name="sceneEntity">The scene entity to update.</param>
        /// <param name="deltaTime">The elapsed time since last update.</param>
        /// <remarks>
        /// Per-scene updates are handled by ProcessInternal() which queries for all message box scenes.
        /// This method exists to satisfy ISceneSystem interface but doesn't need per-scene logic.
        /// </remarks>
        public void Update(Entity sceneEntity, float deltaTime)
        {
            // Per-scene update not needed - ProcessInternal() handles all message box scenes via query
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
                    $"Scene entity {sceneEntity.Id} does not have MessageBoxComponent. "
                        + "Cannot render message box without component."
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
        /// Updates message box text printing state machine, handles input, and processes character-by-character printing.
        /// Overrides BaseSystem.Update() to follow standard Arch ECS pattern.
        /// </summary>
        /// <param name="deltaTime">The elapsed time since last update.</param>
        public override void Update(in float deltaTime)
        {
            // Copy in parameter to local variable for use in lambda (cannot capture in parameters)
            float dt = deltaTime;

            // Query for active message box scenes
            World.Query(
                in _messageBoxScenesQuery,
                (Entity sceneEntity, ref MessageBoxComponent msgBox) =>
                {
                    // Validate entity is still alive (prevent stale references)
                    if (!World.IsAlive(sceneEntity))
                    {
                        return;
                    }

                    if (msgBox.State == MessageBoxRenderState.Hidden)
                        return;

                    // Handle input FIRST for speed-up (before state processing)
                    // This prevents race conditions where text finishes and closes in same frame
                    HandleInput(sceneEntity, ref msgBox);

                    // Handle different states
                    switch (msgBox.State)
                    {
                        case MessageBoxRenderState.Wait:
                        case MessageBoxRenderState.WaitForScroll:
                            // Waiting for input - don't process characters
                            // Input handling above already checked for advancement
                            return;

                        case MessageBoxRenderState.Scrolling:
                            // Animate scroll (like pokeemerald-expansion RENDER_STATE_SCROLL)
                            // ScrollSpeed is in pixels per second for frame-rate independence
                            if (msgBox.ScrollDistanceRemaining > 0)
                            {
                                float scrollAmount = msgBox.ScrollSpeed * dt;
                                if (scrollAmount > msgBox.ScrollDistanceRemaining)
                                {
                                    scrollAmount = msgBox.ScrollDistanceRemaining;
                                }
                                msgBox.ScrollOffset += scrollAmount;
                                msgBox.ScrollDistanceRemaining -= scrollAmount;
                            }

                            // Check if scroll animation is complete
                            if (msgBox.ScrollDistanceRemaining <= 0)
                            {
                                // Scroll complete - advance PageStartLine and reset offset
                                msgBox.PageStartLine++;
                                msgBox.ScrollOffset = 0;
                                msgBox.ScrollDistanceRemaining = 0;
                                msgBox.State = MessageBoxRenderState.HandleChar;
                            }
                            return;

                        case MessageBoxRenderState.Paused:
                            // Paused - decrement delay counter, then resume when it reaches 0
                            if (msgBox.DelayCounter > 0)
                            {
                                msgBox.DelayCounter -= dt;
                                return;
                            }
                            // Delay finished, resume printing
                            msgBox.State = MessageBoxRenderState.HandleChar;
                            break;

                        case MessageBoxRenderState.HandleChar:
                            // Update delay counter (time-based, subtract deltaTime)
                            if (msgBox.DelayCounter > 0)
                            {
                                msgBox.DelayCounter -= dt;
                                return; // Wait for delay
                            }
                            // Delay finished, process next character/token
                            // Only process one token per frame to prevent infinite loops
                            ProcessCharacter(sceneEntity, ref msgBox);

                            // If state changed to Finished, Wait, or WaitForScroll, stop processing this frame
                            if (
                                msgBox.State == MessageBoxRenderState.Finished
                                || msgBox.State == MessageBoxRenderState.Wait
                                || msgBox.State == MessageBoxRenderState.WaitForScroll
                            )
                            {
                                return;
                            }
                            // If state changed to Paused, stop processing (will resume when delay finishes)
                            if (msgBox.State == MessageBoxRenderState.Paused)
                            {
                                return;
                            }
                            // Otherwise, continue processing if delay is still 0 (speed-up mode)
                            // But limit to prevent infinite loops - only process once per frame
                            break;

                        case MessageBoxRenderState.Finished:
                            // Finished - input already handled above, just wait
                            return;

                        default:
                            // Unknown state - reset to HandleChar
                            msgBox.State = MessageBoxRenderState.HandleChar;
                            break;
                    }
                }
            );
        }

        /// <summary>
        /// Performs internal processing that needs to run every frame.
        /// Implements ISceneSystem interface.
        /// Delegates to Update() to follow the same pattern as other scene systems.
        /// </summary>
        /// <param name="deltaTime">The elapsed time since last update.</param>
        public void ProcessInternal(float deltaTime)
        {
            // Delegate to the internal Update method that processes message box state machine
            Update(in deltaTime);
        }

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
            // Use direct scene destruction instead of event to avoid circular event dependency
            if (_activeMessageBoxSceneEntity.HasValue)
            {
                _logger.Debug("Destroying existing message box before showing new one");
                var existingSceneEntity = _activeMessageBoxSceneEntity.Value;

                // Validate entity is still alive before destroying
                if (World.IsAlive(existingSceneEntity))
                {
                    _sceneManager.DestroyScene(existingSceneEntity);
                }

                _activeMessageBoxSceneEntity = null;
            }

            // Get player text speed preference (with fallback to default)
            float textSpeed = evt.TextSpeedOverride ?? GetPlayerTextSpeed();

            // Validate font exists (fail fast, no fallback)
            string fontId = evt.FontId ?? MessageBoxConstants.DefaultFontId;
            var fontSystem = ValidateAndGetFont(fontId);

            // Validate tilesheet exists (fail fast, no fallback)
            var tilesheet = ValidateAndGetTilesheet();

            // Create SceneComponent object
            var sceneComponent = new SceneComponent
            {
                SceneId = $"messagebox:{Guid.NewGuid()}",
                Priority = ScenePriorities.GameScene + MessageBoxConstants.ScenePriorityOffset, // 70
                CameraMode = SceneCameraMode.GameCamera,
                BlocksUpdate = true,
                BlocksDraw = false,
                BlocksInput = true, // Block input to prevent player movement while message box is open
                IsActive = true,
                IsPaused = false,
                BackgroundColor = Color.Transparent, // Message box is transparent overlay
            };

            // Create MessageBoxSceneComponent marker
            var messageBoxSceneComponent = new MessageBoxSceneComponent();

            // Create scene via _sceneManager.CreateScene
            Entity sceneEntity;
            try
            {
                sceneEntity = _sceneManager.CreateScene(sceneComponent, messageBoxSceneComponent);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to create message box scene");
                return;
            }

            // Pre-parse control codes and wrap text (performance optimization)
            var parsedText = ParseControlCodes(evt.Text);
            var wrappedLines = WrapText(parsedText, fontId);

            // Calculate initial colors (for both current and default)
            // Pokeemerald-expansion text colors from text_pal1.pal:
            // Index 2 (DARK_GRAY) = 98, 98, 98 - text foreground
            // Index 3 (LIGHT_GRAY) = 213, 213, 205 - shadow
            Color initialTextColor = evt.TextColor ?? new Color(98, 98, 98, 255); // Dark gray text
            Color initialShadowColor = evt.ShadowColor ?? new Color(213, 213, 205, 255); // Light gray shadow

            // Add MessageBoxComponent with text and state
            World.Add<MessageBoxComponent>(
                sceneEntity,
                new MessageBoxComponent
                {
                    Text = evt.Text, // Original text (for reference)
                    ParsedText = parsedText, // Pre-parsed tokens
                    WrappedLines = wrappedLines, // Pre-wrapped lines
                    CurrentTokenIndex = 0, // Start at first token
                    CurrentCharIndex = 0, // Start at first character, for rendering reference
                    TextSpeed = textSpeed,
                    DelayCounter = textSpeed,
                    State = MessageBoxRenderState.HandleChar,
                    CanSpeedUpWithButton = evt.CanSpeedUpWithButton,
                    AutoScroll = evt.AutoScroll,
                    HasBeenSpedUp = false,
                    WindowId = 0, // Single message box always uses ID 0
                    TextColor = initialTextColor,
                    BackgroundColor = evt.BackgroundColor ?? Color.Transparent,
                    ShadowColor = initialShadowColor,
                    // Store defaults for {RESET} control code
                    DefaultTextColor = initialTextColor,
                    DefaultShadowColor = initialShadowColor,
                    DefaultTextSpeed = textSpeed,
                    FontId = fontId, // Use validated fontId
                    CurrentX = 0,
                    CurrentY = 0,
                    StartX = 0,
                    StartY = 0,
                    LetterSpacing = 0,
                    LineSpacing = MessageBoxConstants.DefaultLineSpacing,
                    IsVisible = true,
                    IsWaitingForInput = false,
                    PageStartLine = 0, // Start at first line (first page)
                    // Scroll animation fields (initialized, but not active until Scrolling state)
                    ScrollOffset = 0,
                    ScrollDistanceRemaining = 0,
                    ScrollSpeed = GetScrollSpeed(textSpeed),
                }
            );

            // Track active scene entity
            _activeMessageBoxSceneEntity = sceneEntity;

            _logger.Debug(
                "Created message box scene {SceneId} with text: {Text}",
                sceneEntity.Id,
                evt.Text
            );
        }

        /// <summary>
        /// Handles MessageBoxHideEvent by destroying the message box scene.
        /// Made idempotent to prevent race conditions from multiple rapid event calls.
        /// </summary>
        /// <param name="evt">The hide event containing window ID.</param>
        private void OnMessageBoxHide(ref MessageBoxHideEvent evt)
        {
            if (evt.WindowId == 0)
            {
                // Hide all (single message box policy)
                // Capture entity atomically and clear immediately to prevent race conditions
                // This makes the handler idempotent - safe to call multiple times
                Entity? sceneEntityToDestroy = null;
                if (_activeMessageBoxSceneEntity.HasValue)
                {
                    sceneEntityToDestroy = _activeMessageBoxSceneEntity.Value;
                    _activeMessageBoxSceneEntity = null; // Clear immediately to prevent double-destruction
                }

                // Only destroy if we captured an entity and it's still alive
                if (sceneEntityToDestroy.HasValue)
                {
                    var entity = sceneEntityToDestroy.Value;

                    // Validate entity is still alive before destroying (defensive check)
                    if (World.IsAlive(entity))
                    {
                        _sceneManager.DestroyScene(entity);
                        _logger.Debug("Destroyed message box scene {SceneId}", entity.Id);
                    }
                    else
                    {
                        _logger.Debug(
                            "MessageBoxHideEvent: Entity {EntityId} was already destroyed, ignoring",
                            entity.Id
                        );
                    }
                }
            }
            else
            {
                // Hide specific window (future: multiple message boxes)
                _logger.Warning(
                    "MessageBoxHideEvent with WindowId {WindowId} not supported (single message box policy)",
                    evt.WindowId
                );
            }
        }

        /// <summary>
        /// Handles MessageBoxTextAdvanceEvent when player presses A/B to advance text.
        /// </summary>
        /// <param name="evt">The advance event.</param>
        private void OnMessageBoxTextAdvance(ref MessageBoxTextAdvanceEvent evt)
        {
            if (!_activeMessageBoxSceneEntity.HasValue)
            {
                return;
            }

            var sceneEntity = _activeMessageBoxSceneEntity.Value;

            // Validate entity is still alive
            if (!World.IsAlive(sceneEntity))
            {
                _activeMessageBoxSceneEntity = null;
                return;
            }

            if (!World.Has<MessageBoxComponent>(sceneEntity))
            {
                return;
            }

            ref var msgBox = ref World.Get<MessageBoxComponent>(sceneEntity);

            // Check if waiting for input
            if (msgBox.IsWaitingForInput)
            {
                if (msgBox.State == MessageBoxRenderState.Wait)
                {
                    // Page break: advance to fresh page
                    AdvanceToNextPage(ref msgBox);
                }
                else if (msgBox.State == MessageBoxRenderState.WaitForScroll)
                {
                    // Scroll: only animate if we've filled the visible area.
                    StartScrollAnimation(ref msgBox);
                }
            }
            // Note: Finished state is handled in HandleInput() when player presses A/B
        }

        /// <summary>
        /// Gets the player's text speed preference from global variables.
        /// </summary>
        /// <returns>The text speed in seconds per character (matching pokeemerald-expansion timing).</returns>
        private float GetPlayerTextSpeed()
        {
            string? speedStr = _flagVariableService.GetVariable<string>(
                MessageBoxConstants.TextSpeedVariableName
            );

            if (string.IsNullOrEmpty(speedStr))
            {
                speedStr = MessageBoxConstants.DefaultTextSpeed;
            }

            return speedStr.ToLowerInvariant() switch
            {
                "slow" => MessageBoxConstants.TextSpeedSlowSeconds,
                "medium" => MessageBoxConstants.TextSpeedMediumSeconds,
                "fast" => MessageBoxConstants.TextSpeedFastSeconds,
                "instant" => MessageBoxConstants.TextSpeedInstantSeconds,
                _ => MessageBoxConstants.TextSpeedMediumSeconds, // Default fallback
            };
        }

        /// <summary>
        /// Advances to the next page by resetting PageStartLine to CurrentY.
        /// </summary>
        /// <param name="msgBox">The message box component to update.</param>
        private void AdvanceToNextPage(ref MessageBoxComponent msgBox)
        {
            msgBox.PageStartLine = msgBox.CurrentY;
            msgBox.IsWaitingForInput = false;
            msgBox.State = MessageBoxRenderState.HandleChar;
            msgBox.HasBeenSpedUp = false; // Reset speed-up so user can speed up next page
        }

        /// <summary>
        /// Starts scroll animation if visible area is filled, otherwise continues printing.
        /// </summary>
        /// <param name="msgBox">The message box component to update.</param>
        private void StartScrollAnimation(ref MessageBoxComponent msgBox)
        {
            int visibleLineCount = msgBox.CurrentY - msgBox.PageStartLine;
            if (visibleLineCount >= MessageBoxConstants.MaxVisibleLines)
            {
                // Start scroll animation (like pokeemerald-expansion RENDER_STATE_SCROLL)
                // ScrollDistanceRemaining = font height + line spacing
                msgBox.ScrollDistanceRemaining =
                    MessageBoxConstants.DefaultScrollDistance + msgBox.LineSpacing;
                msgBox.ScrollOffset = 0;
                msgBox.IsWaitingForInput = false;
                msgBox.State = MessageBoxRenderState.Scrolling;
            }
            else
            {
                // Still have room, no animation needed
                msgBox.IsWaitingForInput = false;
                msgBox.State = MessageBoxRenderState.HandleChar;
            }
            msgBox.HasBeenSpedUp = false; // Reset speed-up so user can speed up next page
        }

        /// <summary>
        /// Gets the scroll speed in pixels per second based on text speed setting.
        /// Time-based for consistent behavior across different frame rates.
        /// </summary>
        /// <param name="textSpeed">The text speed in seconds per character.</param>
        /// <returns>Scroll speed in pixels per second.</returns>
        private float GetScrollSpeed(float textSpeed)
        {
            // Validate text speed is non-negative (defensive check)
            if (textSpeed < 0)
            {
                _logger.Warning(
                    "GetScrollSpeed: Invalid negative textSpeed {TextSpeed}. Using instant speed.",
                    textSpeed
                );
                return MessageBoxConstants.ScrollSpeedInstantPixelsPerSecond;
            }

            // Map text speed to scroll speed (converted from pokeemerald-expansion frame-based values)
            if (textSpeed >= MessageBoxConstants.TextSpeedSlowSeconds)
            {
                return MessageBoxConstants.ScrollSpeedSlowPixelsPerSecond;
            }
            else if (textSpeed >= MessageBoxConstants.TextSpeedMediumSeconds)
            {
                return MessageBoxConstants.ScrollSpeedMediumPixelsPerSecond;
            }
            else if (textSpeed >= MessageBoxConstants.TextSpeedFastSeconds)
            {
                return MessageBoxConstants.ScrollSpeedFastPixelsPerSecond;
            }
            else
            {
                return MessageBoxConstants.ScrollSpeedInstantPixelsPerSecond;
            }
        }

        /// <summary>
        /// Handles input for message box (A/B button presses).
        /// </summary>
        /// <param name="sceneEntity">The scene entity.</param>
        /// <param name="msgBox">The message box component.</param>
        private void HandleInput(Entity sceneEntity, ref MessageBoxComponent msgBox)
        {
            // Check if Interact button (A/B) is just pressed (not held)
            bool interactJustPressed = _inputBindingService.IsActionJustPressed(
                InputAction.Interact
            );
            bool interactPressed = _inputBindingService.IsActionPressed(InputAction.Interact);

            // Handle button presses
            if (interactJustPressed)
            {
                // CRITICAL: Check state BEFORE processing to prevent race conditions
                // If we're printing (HandleChar), speed up takes priority
                if (
                    msgBox.State == MessageBoxRenderState.HandleChar
                    && msgBox.CanSpeedUpWithButton
                    && !msgBox.HasBeenSpedUp
                )
                {
                    // Speed up printing - set delay to 0 so next character processes immediately
                    msgBox.DelayCounter = 0.0f;
                    msgBox.HasBeenSpedUp = true;
                    // Don't check for input waiting - speed up takes priority
                    return;
                }

                // Only check for input waiting if we're NOT actively printing
                // This prevents closing when button is pressed during printing
                if (msgBox.IsWaitingForInput && msgBox.State != MessageBoxRenderState.HandleChar)
                {
                    if (msgBox.State == MessageBoxRenderState.Wait)
                    {
                        // Page break: advance to fresh page (clear visible lines)
                        // PageStartLine jumps to CurrentY so only new lines are visible
                        AdvanceToNextPage(ref msgBox);
                    }
                    else if (msgBox.State == MessageBoxRenderState.WaitForScroll)
                    {
                        // Scroll: only animate if we've filled the visible area.
                        // This ensures "keep previous line visible" behavior like Pokemon's \l.
                        StartScrollAnimation(ref msgBox);
                    }
                    else if (msgBox.State == MessageBoxRenderState.Finished)
                    {
                        // Close message box when finished (only if not printing)
                        var hideEvent = new MessageBoxHideEvent { WindowId = 0 };
                        EventBus.Send(ref hideEvent);
                    }
                }
            }
            else if (interactPressed && msgBox.State == MessageBoxRenderState.HandleChar)
            {
                // If button is held (not just pressed) and we're printing, continue to speed up
                // This matches pokeemerald-expansion behavior: held button keeps delay at 0
                if (msgBox.CanSpeedUpWithButton && msgBox.HasBeenSpedUp)
                {
                    msgBox.DelayCounter = 0.0f; // Keep delay at 0 while held
                }
            }
        }

        /// <summary>
        /// Processes the current character/token in the text printing state machine.
        /// </summary>
        /// <param name="sceneEntity">The scene entity.</param>
        /// <param name="msgBox">The message box component.</param>
        private void ProcessCharacter(Entity sceneEntity, ref MessageBoxComponent msgBox)
        {
            // Check if all tokens processed
            if (msgBox.ParsedText == null || msgBox.CurrentTokenIndex >= msgBox.ParsedText.Count)
            {
                // All text finished - wait for input before closing
                msgBox.State = MessageBoxRenderState.Finished;
                msgBox.IsWaitingForInput = true; // Wait for player to press A/B to close
                var finishedEvent = new MessageBoxTextFinishedEvent { WindowId = msgBox.WindowId };
                EventBus.Send(ref finishedEvent);

                _logger.Debug(
                    "Message box text finished. CurrentTokenIndex: {TokenIndex}, TotalTokens: {TotalTokens}, CurrentCharIndex: {CharIndex}",
                    msgBox.CurrentTokenIndex,
                    msgBox.ParsedText?.Count ?? 0,
                    msgBox.CurrentCharIndex
                );
                return;
            }

            // Defensive bounds check before accessing token (should never happen, but prevents crashes)
            if (msgBox.CurrentTokenIndex < 0 || msgBox.CurrentTokenIndex >= msgBox.ParsedText.Count)
            {
                _logger.Warning(
                    "ProcessCharacter: CurrentTokenIndex {TokenIndex} is out of bounds for ParsedText count {Count}. Resetting to finished state.",
                    msgBox.CurrentTokenIndex,
                    msgBox.ParsedText.Count
                );
                msgBox.State = MessageBoxRenderState.Finished;
                msgBox.IsWaitingForInput = true;
                return;
            }

            // Get current token
            var token = msgBox.ParsedText[msgBox.CurrentTokenIndex];

            _logger.Debug(
                "Processing token {TokenIndex}/{TotalTokens}: Type={TokenType}, CurrentCharIndex={CharIndex}",
                msgBox.CurrentTokenIndex + 1,
                msgBox.ParsedText.Count,
                token.TokenType,
                msgBox.CurrentCharIndex
            );

            // Handle token types
            switch (token.TokenType)
            {
                case TextTokenType.Char:
                    // Advance to next character
                    msgBox.CurrentTokenIndex++;
                    msgBox.CurrentCharIndex++;
                    msgBox.DelayCounter = msgBox.TextSpeed; // Reset delay for next character
                    break;

                case TextTokenType.Newline:
                    // Move to next line
                    msgBox.CurrentTokenIndex++;
                    msgBox.CurrentCharIndex++; // Advance character index (treat newline as part of character stream for rendering)
                    msgBox.CurrentY++; // Move down one line
                    msgBox.CurrentX = msgBox.StartX; // Reset X position

                    // Check if we've exceeded the visible area (pagination)
                    // CurrentY is the line number we're about to render TO (0-indexed from PageStartLine)
                    int visibleLineCount = msgBox.CurrentY - msgBox.PageStartLine;
                    if (visibleLineCount >= MessageBoxConstants.MaxVisibleLines)
                    {
                        // We've filled the visible area - wait for input before continuing
                        msgBox.State = MessageBoxRenderState.Wait;
                        msgBox.IsWaitingForInput = true;
                        _logger.Debug(
                            "Message box pagination: filled {VisibleLines} lines, waiting for input. CurrentY={CurrentY}, PageStartLine={PageStartLine}",
                            visibleLineCount,
                            msgBox.CurrentY,
                            msgBox.PageStartLine
                        );
                        return; // Don't set delay - waiting for input
                    }

                    msgBox.DelayCounter = msgBox.TextSpeed;
                    break;

                case TextTokenType.PageBreak:
                    // Page break: newline + forced wait (like Pokemon's \p)
                    // Used for single-line pages or dramatic pauses
                    // On resume, clears the box and starts fresh
                    msgBox.CurrentTokenIndex++;
                    msgBox.CurrentCharIndex++;
                    msgBox.CurrentY++;
                    msgBox.CurrentX = msgBox.StartX;

                    // Wait for input - DON'T advance PageStartLine yet!
                    // PageStartLine will be set to CurrentY in HandleInput (fresh page).
                    msgBox.State = MessageBoxRenderState.Wait;
                    msgBox.IsWaitingForInput = true;
                    _logger.Debug(
                        "Message box page break: forced wait at CurrentY={CurrentY}, PageStartLine={PageStartLine}",
                        msgBox.CurrentY,
                        msgBox.PageStartLine
                    );
                    return; // Don't set delay - waiting for input

                case TextTokenType.Scroll:
                    // Scroll: newline + wait for input + scroll up (like Pokemon's \l)
                    // Keeps previous line visible while adding new line at bottom
                    msgBox.CurrentTokenIndex++;
                    msgBox.CurrentCharIndex++;
                    msgBox.CurrentY++;
                    msgBox.CurrentX = msgBox.StartX;

                    // Wait for input with scroll behavior
                    // PageStartLine will be incremented by 1 in HandleInput (scroll, not clear)
                    msgBox.State = MessageBoxRenderState.WaitForScroll;
                    msgBox.IsWaitingForInput = true;
                    _logger.Debug(
                        "Message box scroll: wait then scroll at CurrentY={CurrentY}, PageStartLine={PageStartLine}",
                        msgBox.CurrentY,
                        msgBox.PageStartLine
                    );
                    return; // Don't set delay - waiting for input

                case TextTokenType.Pause:
                    // Pause for specified time (already in seconds for frame-rate independence)
                    msgBox.CurrentTokenIndex++;
                    float? pauseSeconds = token.GetPauseSeconds();
                    msgBox.DelayCounter = pauseSeconds ?? 0.0f;
                    msgBox.State = MessageBoxRenderState.Paused;
                    break;

                case TextTokenType.PauseUntilPress:
                    // Wait for button press
                    msgBox.CurrentTokenIndex++;
                    msgBox.State = MessageBoxRenderState.Wait;
                    msgBox.IsWaitingForInput = true;
                    // Don't reset delay counter - wait for input
                    break;

                case TextTokenType.Color:
                    // Change text color
                    msgBox.CurrentTokenIndex++;
                    Color? newColor = token.GetColor();
                    if (newColor.HasValue)
                    {
                        msgBox.TextColor = newColor.Value;
                    }
                    msgBox.DelayCounter = msgBox.TextSpeed;
                    break;

                case TextTokenType.Shadow:
                    // Change shadow color
                    msgBox.CurrentTokenIndex++;
                    Color? newShadowColor = token.GetShadowColor();
                    if (newShadowColor.HasValue)
                    {
                        msgBox.ShadowColor = newShadowColor.Value;
                    }
                    msgBox.DelayCounter = msgBox.TextSpeed;
                    break;

                case TextTokenType.Speed:
                    // Change text speed (convert frames to seconds)
                    msgBox.CurrentTokenIndex++;
                    int? newSpeedFrames = token.GetSpeed();
                    if (newSpeedFrames.HasValue)
                    {
                        // Convert frames to seconds (GBA runs at 60 FPS)
                        msgBox.TextSpeed = newSpeedFrames.Value / MessageBoxConstants.GbaFrameRate;
                    }
                    msgBox.DelayCounter = msgBox.TextSpeed;
                    break;

                case TextTokenType.Clear:
                    // Clear current page
                    msgBox.CurrentTokenIndex++;
                    msgBox.CurrentX = msgBox.StartX;
                    msgBox.CurrentY = msgBox.StartY;
                    msgBox.DelayCounter = msgBox.TextSpeed;
                    break;

                case TextTokenType.Reset:
                    // Reset color, shadow, and speed to their initial defaults
                    msgBox.CurrentTokenIndex++;
                    msgBox.TextColor = msgBox.DefaultTextColor;
                    msgBox.ShadowColor = msgBox.DefaultShadowColor;
                    msgBox.TextSpeed = msgBox.DefaultTextSpeed;
                    msgBox.ScrollSpeed = GetScrollSpeed(msgBox.DefaultTextSpeed);
                    msgBox.DelayCounter = msgBox.TextSpeed;
                    break;
            }
        }

        /// <summary>
        /// Parses control codes from text string into tokens.
        /// </summary>
        /// <param name="text">The text to parse.</param>
        /// <returns>List of parsed tokens.</returns>
        /// <exception cref="ArgumentException">Thrown for malformed control codes.</exception>
        /// <exception cref="FormatException">Thrown for invalid parameter formats.</exception>
        private System.Collections.Generic.List<TextToken> ParseControlCodes(string text)
        {
            var tokens = new System.Collections.Generic.List<TextToken>();
            if (string.IsNullOrEmpty(text))
            {
                return tokens;
            }

            int i = 0;
            while (i < text.Length)
            {
                char c = text[i];
                int originalPosition = i;

                // Handle actual newline characters (ASCII 10, 13) FIRST
                // These are different from escape sequences like \n in source code
                if (c == '\n')
                {
                    tokens.Add(
                        new TextToken
                        {
                            TokenType = TextTokenType.Newline,
                            Value = null,
                            OriginalPosition = originalPosition,
                        }
                    );
                    i++;
                    continue;
                }
                if (c == '\r')
                {
                    // Handle \r\n (Windows) or standalone \r (old Mac)
                    if (i + 1 < text.Length && text[i + 1] == '\n')
                    {
                        // Skip \r, the \n will create the Newline token next iteration
                        i++;
                        continue;
                    }
                    // Standalone \r - treat as newline
                    tokens.Add(
                        new TextToken
                        {
                            TokenType = TextTokenType.Newline,
                            Value = null,
                            OriginalPosition = originalPosition,
                        }
                    );
                    i++;
                    continue;
                }

                // Handle escape sequences (backslash followed by character)
                if (c == '\\' && i + 1 < text.Length)
                {
                    char next = text[i + 1];
                    if (next == '{')
                    {
                        // Escaped brace - create Char token with literal '{'
                        tokens.Add(
                            new TextToken
                            {
                                TokenType = TextTokenType.Char,
                                Value = '{',
                                OriginalPosition = originalPosition,
                            }
                        );
                        i += 2; // Skip both '\' and '{'
                        continue;
                    }
                    else if (next == 'n')
                    {
                        // Newline
                        tokens.Add(
                            new TextToken
                            {
                                TokenType = TextTokenType.Newline,
                                Value = null,
                                OriginalPosition = originalPosition,
                            }
                        );
                        i += 2; // Skip both '\' and 'n'
                        continue;
                    }
                    else if (next == 'r')
                    {
                        // Carriage return (treat as newline)
                        tokens.Add(
                            new TextToken
                            {
                                TokenType = TextTokenType.Newline,
                                Value = null,
                                OriginalPosition = originalPosition,
                            }
                        );
                        i += 2; // Skip both '\' and 'r'
                        continue;
                    }
                    else if (next == 'p')
                    {
                        // Page break (newline + wait for input, like Pokemon's \p)
                        // Clears the box and starts fresh
                        tokens.Add(
                            new TextToken
                            {
                                TokenType = TextTokenType.PageBreak,
                                Value = null,
                                OriginalPosition = originalPosition,
                            }
                        );
                        i += 2; // Skip both '\' and 'p'
                        continue;
                    }
                    else if (next == 'l')
                    {
                        // Scroll (newline + wait for input + scroll up, like Pokemon's \l)
                        // Keeps previous line visible while adding new line at bottom
                        tokens.Add(
                            new TextToken
                            {
                                TokenType = TextTokenType.Scroll,
                                Value = null,
                                OriginalPosition = originalPosition,
                            }
                        );
                        i += 2; // Skip both '\' and 'l'
                        continue;
                    }
                    // Otherwise, treat '\' as regular character
                }

                // Handle control codes (start with '{')
                if (c == '{')
                {
                    int startPos = i;
                    i++; // Skip '{'

                    // Find closing '}'
                    int endPos = text.IndexOf('}', i);
                    if (endPos == -1)
                    {
                        throw new ArgumentException(
                            $"Unclosed control code at position {startPos}: '{text.Substring(startPos, Math.Min(20, text.Length - startPos))}...'"
                        );
                    }

                    string controlCode = text.Substring(i, endPos - i);
                    i = endPos + 1; // Skip '}'

                    // Parse control code using strategy pattern (extensible, follows Open/Closed Principle)
                    if (
                        ControlCodeParsers.TryParse(
                            controlCode,
                            originalPosition,
                            out TextToken token
                        )
                    )
                    {
                        tokens.Add(token);
                    }
                    else
                    {
                        throw new ArgumentException(
                            $"Unknown control code at position {startPos}: '{controlCode}'"
                        );
                    }
                }
                else
                {
                    // Regular character
                    tokens.Add(
                        new TextToken
                        {
                            TokenType = TextTokenType.Char,
                            Value = c,
                            OriginalPosition = originalPosition,
                        }
                    );
                    i++;
                }
            }

            return tokens;
        }

        /// <summary>
        /// Wraps text into lines based on message box width.
        /// </summary>
        /// <param name="parsedText">The parsed text tokens.</param>
        /// <param name="fontId">The font ID to use for measurement.</param>
        /// <returns>List of wrapped lines.</returns>
        /// <exception cref="InvalidOperationException">Thrown if font not found.</exception>
        private System.Collections.Generic.List<WrappedLine> WrapText(
            System.Collections.Generic.List<TextToken> parsedText,
            string fontId
        )
        {
            var wrappedLines = new System.Collections.Generic.List<WrappedLine>();
            if (parsedText == null || parsedText.Count == 0)
            {
                return wrappedLines;
            }

            // Validate font exists (fail fast, no fallback)
            var fontSystem = ValidateAndGetFont(fontId);

            // Get font for measurement (use default size from constants)
            // FontDefinition could be loaded via IModManager for custom sizes, but for now use constant
            int fontSize = MessageBoxConstants.DefaultFontSize;
            var font = fontSystem.GetFont(fontSize);

            float maxWidth =
                MessageBoxConstants.MessageBoxInteriorWidth
                - (MessageBoxConstants.TextPaddingX * 2); // Account for horizontal padding on both sides
            float currentWidth = 0;
            int lineStartIndex = 0;
            System.Text.StringBuilder currentLine = new System.Text.StringBuilder();
            int charIndex = 0; // Track character index in original text

            for (int i = 0; i < parsedText.Count; i++)
            {
                var token = parsedText[i];

                if (token.TokenType == TextTokenType.Char)
                {
                    char ch = (char)token.Value!;
                    string charStr = ch.ToString();

                    // Measure character width
                    var bounds = font.MeasureString(charStr);
                    float charWidth = bounds.X;

                    // Check if adding this character would exceed line width
                    if (currentWidth + charWidth > maxWidth && currentLine.Length > 0)
                    {
                        // Create wrapped line
                        wrappedLines.Add(
                            new WrappedLine
                            {
                                Text = currentLine.ToString(),
                                StartIndex = lineStartIndex,
                                EndIndex = charIndex,
                                Width = currentWidth,
                            }
                        );

                        // Start new line
                        currentLine.Clear();
                        currentWidth = 0;
                        lineStartIndex = charIndex;
                    }

                    currentLine.Append(ch);
                    currentWidth += charWidth;
                    charIndex++;
                }
                else if (
                    token.TokenType == TextTokenType.Newline
                    || token.TokenType == TextTokenType.PageBreak
                    || token.TokenType == TextTokenType.Scroll
                )
                {
                    // Force line break - always add a line (even if empty) to preserve newlines/page breaks/scrolls
                    // IMPORTANT: EndIndex must include the newline/page break character itself so that
                    // RenderText can correctly determine when a line is complete.
                    // The newline/page break counts as part of the character stream for CurrentCharIndex tracking.
                    wrappedLines.Add(
                        new WrappedLine
                        {
                            Text = currentLine.ToString(), // Empty string if no text
                            StartIndex = lineStartIndex,
                            EndIndex = charIndex + 1, // Include the newline/page break character in this line's range
                            Width = currentWidth,
                        }
                    );

                    // Increment charIndex for newline/page break AFTER setting EndIndex
                    charIndex++;

                    // Start new line
                    currentLine.Clear();
                    currentWidth = 0;
                    lineStartIndex = charIndex; // New line starts after the newline/page break character
                }
                else if (token.TokenType == TextTokenType.Clear)
                {
                    // Clear forces new line/page
                    if (currentLine.Length > 0)
                    {
                        wrappedLines.Add(
                            new WrappedLine
                            {
                                Text = currentLine.ToString(),
                                StartIndex = lineStartIndex,
                                EndIndex = charIndex,
                                Width = currentWidth,
                            }
                        );
                    }

                    // Start new line
                    currentLine.Clear();
                    currentWidth = 0;
                    lineStartIndex = charIndex;
                    // Don't increment charIndex for Clear (it's not a visible character)
                }
                // Control codes (Pause, PauseUntilPress, Color, Speed) don't affect wrapping
                // They're preserved in the token list but don't contribute to line width
            }

            // Add final line if there's remaining text
            if (currentLine.Length > 0)
            {
                wrappedLines.Add(
                    new WrappedLine
                    {
                        Text = currentLine.ToString(),
                        StartIndex = lineStartIndex,
                        EndIndex = charIndex,
                        Width = currentWidth,
                    }
                );
            }

            return wrappedLines;
        }

        /// <summary>
        /// Renders the message box frame and text.
        /// </summary>
        /// <param name="sceneEntity">The scene entity.</param>
        /// <param name="msgBox">The message box component.</param>
        /// <param name="gameTime">The game time.</param>
        private void RenderMessageBox(
            Entity sceneEntity,
            ref MessageBoxComponent msgBox,
            GameTime gameTime
        )
        {
            // Get scene component for camera mode
            if (!World.Has<SceneComponent>(sceneEntity))
            {
                return;
            }

            ref var scene = ref World.Get<SceneComponent>(sceneEntity);

            // Get camera based on CameraMode
            CameraComponent? camera = null;
            switch (scene.CameraMode)
            {
                case SceneCameraMode.GameCamera:
                    camera = _cameraService.GetActiveCamera();
                    break;

                case SceneCameraMode.SceneCamera:
                    if (scene.CameraEntityId.HasValue)
                    {
                        int cameraEntityId = scene.CameraEntityId.Value;
                        bool foundCamera = false;
                        World.Query(
                            in _cameraQuery,
                            (Entity entity, ref CameraComponent cam) =>
                            {
                                if (entity.Id == cameraEntityId)
                                {
                                    camera = cam;
                                    foundCamera = true;
                                }
                            }
                        );
                        if (!foundCamera)
                        {
                            _logger.Warning(
                                "MessageBoxScene '{SceneId}' specified SceneCamera mode but camera entity {CameraEntityId} is not found",
                                scene.SceneId,
                                cameraEntityId
                            );
                            return;
                        }
                    }
                    else
                    {
                        _logger.Warning(
                            "MessageBoxScene '{SceneId}' specified SceneCamera mode but CameraEntityId is null",
                            scene.SceneId
                        );
                        return;
                    }
                    break;

                case SceneCameraMode.ScreenCamera:
                    // ScreenCamera not supported for message box (needs viewport for scaling)
                    _logger.Warning(
                        "MessageBoxScene '{SceneId}' requires a camera for viewport. Use GameCamera or SceneCamera mode.",
                        scene.SceneId
                    );
                    return;
            }

            if (!camera.HasValue)
            {
                _logger.Warning(
                    "MessageBoxScene '{SceneId}' requires camera but none was found. Scene will not render.",
                    scene.SceneId
                );
                return;
            }

            // Save original viewport
            var savedViewport = _graphicsDevice.Viewport;

            try
            {
                // Set viewport to camera's virtual viewport (if available) or regular viewport
                if (camera.Value.VirtualViewport != Rectangle.Empty)
                {
                    _graphicsDevice.Viewport = new Viewport(camera.Value.VirtualViewport);
                }

                // Calculate viewport scale factor
                int currentScale = CameraTransformUtility.GetViewportScale(
                    camera.Value,
                    GameConstants.GbaReferenceWidth
                );

                // Render in screen space (no camera transform)
                _spriteBatch.Begin(
                    SpriteSortMode.Deferred,
                    BlendState.AlphaBlend,
                    SamplerState.PointClamp,
                    DepthStencilState.None,
                    RasterizerState.CullCounterClockwise,
                    null,
                    Matrix.Identity // Screen space - no camera transform
                );

                // Load tilesheet texture (cached)
                var tilesheetTexture = LoadMessageBoxTexture();
                if (tilesheetTexture == null)
                {
                    _logger.Warning(
                        "Failed to load message box tilesheet texture. Message box will not render."
                    );
                    return;
                }

                // Get tilesheet definition for tile dimensions
                // Use PopupOutlineDefinition as it has Tiles array with X, Y, Width, Height
                PopupOutlineDefinition tilesheetDef;
                try
                {
                    tilesheetDef = ValidateAndGetTilesheet();
                }
                catch (InvalidOperationException ex)
                {
                    _logger.Warning(
                        ex,
                        "Message box tilesheet definition not found: {TilesheetId}",
                        MessageBoxConstants.MessageBoxTilesheetId
                    );
                    return;
                }

                // Calculate message box dimensions and position (matching pokeemerald-expansion exactly)
                // Window template: tilemapLeft=2, tilemapTop=15, width=27, height=4
                // GBA screen: 240x160 pixels, tiles are 8x8
                int tileSize = tilesheetDef.TileWidth * currentScale;
                int msgBoxInteriorWidth =
                    MessageBoxConstants.MessageBoxInteriorWidth * currentScale;
                int msgBoxInteriorHeight =
                    MessageBoxConstants.MessageBoxInteriorHeight * currentScale;

                // Position based on GBA tile coordinates, scaled to viewport
                // tilemapLeft=2 means 2 tiles = 16 pixels from left (GBA reference)
                // tilemapTop=15 means 15 tiles = 120 pixels from top (GBA reference)
                // Scale these positions to match the current viewport
                int viewportWidth = _graphicsDevice.Viewport.Width;
                int viewportHeight = _graphicsDevice.Viewport.Height;
                const int gbaReferenceWidth = 240;
                const int gbaReferenceHeight = 160;

                // Calculate position in GBA reference space, then scale
                int gbaInteriorX =
                    MessageBoxConstants.MessageBoxInteriorTileX * tilesheetDef.TileWidth;
                int gbaInteriorY =
                    MessageBoxConstants.MessageBoxInteriorTileY * tilesheetDef.TileHeight;

                // Scale to viewport (maintain aspect ratio)
                float scaleX = (float)viewportWidth / gbaReferenceWidth;
                float scaleY = (float)viewportHeight / gbaReferenceHeight;
                int msgBoxInteriorX = (int)(gbaInteriorX * scaleX);
                int msgBoxInteriorY = (int)(gbaInteriorY * scaleY);

                // Render dialogue frame (matches pokeemerald-expansion DrawDialogueFrame)
                DrawDialogueFrame(
                    tilesheetTexture,
                    tilesheetDef,
                    msgBoxInteriorX,
                    msgBoxInteriorY,
                    msgBoxInteriorWidth,
                    msgBoxInteriorHeight,
                    tileSize
                );

                // Render text (inside the interior area)
                RenderText(
                    ref msgBox,
                    msgBoxInteriorX,
                    msgBoxInteriorY,
                    msgBoxInteriorWidth,
                    msgBoxInteriorHeight,
                    currentScale
                );

                // Render down arrow indicator if waiting for input
                if (msgBox.IsWaitingForInput)
                {
                    RenderDownArrow(
                        tilesheetTexture,
                        tilesheetDef,
                        msgBoxInteriorX,
                        msgBoxInteriorY,
                        msgBoxInteriorWidth,
                        msgBoxInteriorHeight,
                        tileSize,
                        gameTime
                    );
                }

                _spriteBatch.End();
            }
            finally
            {
                // Always restore viewport, even if rendering fails
                _graphicsDevice.Viewport = savedViewport;
            }
        }

        /// <summary>
        /// Loads the message box tilesheet texture, caching it for future use.
        /// </summary>
        /// <returns>The loaded texture, or null if loading failed.</returns>
        private Texture2D? LoadMessageBoxTexture()
        {
            // Check cache first
            if (_messageBoxTexture != null)
            {
                return _messageBoxTexture;
            }

            // Get tilesheet definition
            // Use PopupOutlineDefinition as it has Tiles array with X, Y, Width, Height
            PopupOutlineDefinition tilesheetDef;
            try
            {
                tilesheetDef = ValidateAndGetTilesheet();
            }
            catch (InvalidOperationException ex)
            {
                _logger.Warning(
                    ex,
                    "Message box tilesheet definition not found: {TilesheetId}",
                    MessageBoxConstants.MessageBoxTilesheetId
                );
                return null;
            }

            // Load texture using same pattern as MapPopupSceneSystem
            return LoadTextureFromDefinition(
                MessageBoxConstants.MessageBoxTilesheetId,
                tilesheetDef.TexturePath
            );
        }

        /// <summary>
        /// Loads a texture from a texture path, resolving it through mod manifests.
        /// </summary>
        /// <param name="definitionId">The definition ID (for logging and caching).</param>
        /// <param name="texturePath">The texture path relative to mod root.</param>
        /// <returns>The loaded texture, or null if loading failed.</returns>
        private Texture2D? LoadTextureFromDefinition(string definitionId, string texturePath)
        {
            if (string.IsNullOrEmpty(texturePath))
            {
                _logger.Warning("Definition {DefinitionId} has no TexturePath", definitionId);
                return null;
            }

            // Get metadata
            var metadata = _modManager.GetDefinitionMetadata(definitionId);
            if (metadata == null)
            {
                _logger.Warning("Metadata not found for {DefinitionId}", definitionId);
                return null;
            }

            // Get mod manifest
            var modManifest = _modManager.GetModManifest(metadata.OriginalModId);
            if (modManifest == null)
            {
                _logger.Warning(
                    "Mod manifest not found for {DefinitionId} (mod: {ModId})",
                    definitionId,
                    metadata.OriginalModId
                );
                return null;
            }

            // Resolve texture path
            string fullTexturePath = System.IO.Path.Combine(modManifest.ModDirectory, texturePath);
            fullTexturePath = System.IO.Path.GetFullPath(fullTexturePath);

            if (!System.IO.File.Exists(fullTexturePath))
            {
                _logger.Warning(
                    "Texture file not found: {TexturePath} (definition: {DefinitionId})",
                    fullTexturePath,
                    definitionId
                );
                return null;
            }

            try
            {
                // Load texture from file system
                var texture = Texture2D.FromFile(_graphicsDevice, fullTexturePath);
                if (definitionId == MessageBoxConstants.MessageBoxTilesheetId)
                {
                    _messageBoxTexture = texture; // Cache message box texture
                }
                _logger.Debug(
                    "Loaded texture: {DefinitionId} from {TexturePath}",
                    definitionId,
                    fullTexturePath
                );
                return texture;
            }
            catch (Exception ex)
            {
                _logger.Error(
                    ex,
                    "Failed to load texture: {DefinitionId} from {TexturePath}",
                    definitionId,
                    fullTexturePath
                );
                return null;
            }
        }

        /// <summary>
        /// Draws the dialogue frame for the message box, matching pokeemerald-expansion's DrawDialogueFrame.
        /// This uses a custom decorative frame pattern, not a standard 9-slice.
        /// </summary>
        /// <param name="texture">The tilesheet texture.</param>
        /// <param name="tilesheetDef">The tilesheet definition.</param>
        /// <param name="x">The X position of the message box interior (where text will be).</param>
        /// <param name="y">The Y position of the message box interior (where text will be).</param>
        /// <param name="width">The width of the message box interior.</param>
        /// <param name="height">The height of the message box interior.</param>
        /// <param name="tileSize">The scaled tile size.</param>
        private void DrawDialogueFrame(
            Texture2D texture,
            PopupOutlineDefinition tilesheetDef,
            int x,
            int y,
            int width,
            int height,
            int tileSize
        )
        {
            if (tilesheetDef.Tiles == null || tilesheetDef.Tiles.Count == 0)
            {
                _logger.Warning("Tilesheet has no tiles array");
                return;
            }

            // Build tile lookup by index
            var tileLookup = new System.Collections.Generic.Dictionary<int, PopupTileDefinition>();
            foreach (var tile in tilesheetDef.Tiles)
            {
                if (tile != null && !tileLookup.ContainsKey(tile.Index))
                {
                    tileLookup[tile.Index] = tile;
                }
            }

            // Helper to get source rectangle for a tile by index
            Rectangle GetTileSourceRect(int tileIndex)
            {
                if (tileLookup.TryGetValue(tileIndex, out var tile))
                {
                    return new Rectangle(tile.X, tile.Y, tile.Width, tile.Height);
                }
                else
                {
                    // Calculate from grid position if not in Tiles array
                    int tileWidth = tilesheetDef.TileWidth;
                    int tileHeight = tilesheetDef.TileHeight;
                    int columns =
                        tilesheetDef.TileCount > 0
                            ? (int)Math.Sqrt(tilesheetDef.TileCount)
                            : MessageBoxConstants.DefaultTilesheetColumns;
                    int row = tileIndex / columns;
                    int col = tileIndex % columns;
                    int tileX = col * tileWidth;
                    int tileY = row * tileHeight;
                    return new Rectangle(tileX, tileY, tileWidth, tileHeight);
                }
            }

            // Helper to draw a tile by index
            void DrawTile(int tileIndex, int destX, int destY)
            {
                var srcRect = GetTileSourceRect(tileIndex);
                var destRect = new Rectangle(destX, destY, tileSize, tileSize);
                _spriteBatch.Draw(texture, destRect, srcRect, Color.White);
            }

            // Helper to draw a tile flipped vertically (for bottom row)
            // Note: MonoGame SpriteBatch doesn't support negative height, so we use SpriteEffects
            void DrawTileFlippedV(int tileIndex, int destX, int destY)
            {
                var srcRect = GetTileSourceRect(tileIndex);
                var destRect = new Rectangle(destX, destY, tileSize, tileSize);
                _spriteBatch.Draw(
                    texture,
                    destRect,
                    srcRect,
                    Color.White,
                    0f,
                    Vector2.Zero,
                    SpriteEffects.FlipVertically,
                    0f
                );
            }

            // Match pokeemerald-expansion's DrawDialogueFrame pattern exactly
            // Frame extends outside the interior bounds (x-2*tileSize, x-tileSize, x+width, etc.)
            // Top row: tiles 1, 3, 4 (repeated), 5, 6
            // Middle: tiles 7, 9 (repeated), 10
            // Bottom row: tiles 1, 3, 4 (repeated), 5, 6 (vertically flipped)

            int interiorX = x;
            int interiorY = y;
            int interiorWidth = width;
            int interiorHeight = height;

            // Top row (at y - tileSize)
            int topY = interiorY - tileSize;
            DrawTile(1, interiorX - (2 * tileSize), topY); // Left decorative corner
            DrawTile(3, interiorX - tileSize, topY); // Left edge
            // Top edge tile 4 repeated for width-1
            for (int i = 0; i < interiorWidth - tileSize; i += tileSize)
            {
                DrawTile(4, interiorX + i, topY);
            }
            DrawTile(5, interiorX + interiorWidth - tileSize, topY); // Right edge
            DrawTile(6, interiorX + interiorWidth, topY); // Right decorative corner

            // Middle section (at y, height matches interior height)
            // Note: In pokeemerald-expansion, the middle section is drawn with height=5 in FillBgTilemapBufferRect,
            // but the actual visible height should match the interior (4 tiles) because the bottom row
            // is drawn at tilemapTop + height (4 tiles), which overwrites the 5th tile.
            // To avoid rendering issues, we draw the middle section only as tall as the interior,
            // then draw the bottom row immediately after.
            int middleHeight = interiorHeight; // Match interior height (4 tiles), not 5
            // Left edge tile 7
            for (int i = 0; i < middleHeight; i += tileSize)
            {
                DrawTile(7, interiorX - (2 * tileSize), interiorY + i);
            }
            // Center fill tile 9 repeated
            for (int i = 0; i < interiorWidth + tileSize; i += tileSize)
            {
                for (int j = 0; j < middleHeight; j += tileSize)
                {
                    DrawTile(9, interiorX - tileSize + i, interiorY + j);
                }
            }
            // Right edge tile 10
            for (int i = 0; i < middleHeight; i += tileSize)
            {
                DrawTile(10, interiorX + interiorWidth, interiorY + i);
            }

            // Bottom row (at tilemapTop + height, where height=4 is the interior height)
            // This matches pokeemerald-expansion exactly: bottom row is drawn at tilemapTop + height (4 tiles)
            int bottomY = interiorY + interiorHeight;
            DrawTileFlippedV(1, interiorX - (2 * tileSize), bottomY); // Left decorative corner (flipped)
            DrawTileFlippedV(3, interiorX - tileSize, bottomY); // Left edge (flipped)
            // Bottom edge tile 4 repeated for width-1 (flipped)
            for (int i = 0; i < interiorWidth - tileSize; i += tileSize)
            {
                DrawTileFlippedV(4, interiorX + i, bottomY);
            }
            DrawTileFlippedV(5, interiorX + interiorWidth - tileSize, bottomY); // Right edge (flipped)
            DrawTileFlippedV(6, interiorX + interiorWidth, bottomY); // Right decorative corner (flipped)

            // Fill interior with tile 0 (background fill, matching rbox_fill_rectangle)
            // This fills the entire interior area where text will be rendered
            for (int i = 0; i < interiorWidth; i += tileSize)
            {
                for (int j = 0; j < interiorHeight; j += tileSize)
                {
                    DrawTile(0, interiorX + i, interiorY + j);
                }
            }
        }

        /// <summary>
        /// Renders the text content of the message box.
        /// </summary>
        /// <param name="msgBox">The message box component.</param>
        /// <param name="msgBoxX">The X position of the message box.</param>
        /// <param name="msgBoxY">The Y position of the message box.</param>
        /// <param name="msgBoxWidth">The width of the message box.</param>
        /// <param name="msgBoxHeight">The height of the message box.</param>
        /// <param name="scale">The viewport scale factor.</param>
        private void RenderText(
            ref MessageBoxComponent msgBox,
            int msgBoxX,
            int msgBoxY,
            int msgBoxWidth,
            int msgBoxHeight,
            int scale
        )
        {
            // Get font
            var fontSystem = _fontService.GetFontSystem(msgBox.FontId);
            if (fontSystem == null)
            {
                _logger.Warning("Font '{FontId}' not found, cannot render text", msgBox.FontId);
                return;
            }

            // Use scaled font size
            int fontSize = MessageBoxConstants.DefaultFontSize * scale;
            var font = fontSystem.GetFont(fontSize);
            if (font == null)
            {
                _logger.Warning("Failed to get scaled font, cannot render text");
                return;
            }

            // Text padding from frame edges (Pokemon uses 1px top, 0px horizontal)
            int textPaddingX = MessageBoxConstants.TextPaddingX * scale;
            int textPaddingY = MessageBoxConstants.TextPaddingTop * scale;
            int textStartX = msgBoxX + textPaddingX;
            int textStartY = msgBoxY + textPaddingY;

            // Render wrapped lines up to CurrentCharIndex, starting from PageStartLine
            if (msgBox.WrappedLines == null || msgBox.WrappedLines.Count == 0)
            {
                return;
            }

            // Calculate scroll offset in scaled pixels (applied during scroll animation)
            int scrollOffsetPixels = (int)(msgBox.ScrollOffset * scale);

            int lineY = textStartY - scrollOffsetPixels; // Apply scroll offset
            // Line advance = extra spacing + font height (fontSize is already scaled)
            int lineSpacing = (msgBox.LineSpacing * scale) + fontSize;
            int lineIndex = 0; // Track which line number we're on
            int linesRendered = 0; // Track how many lines we've rendered on this page

            // Calculate visible bounds for clipping during scroll animation
            int visibleTop = msgBoxY;
            int visibleBottom = msgBoxY + msgBoxHeight;

            // During scroll animation, render one extra line that's scrolling into view
            int maxLinesToRender = MessageBoxConstants.MaxVisibleLines;
            if (msgBox.State == MessageBoxRenderState.Scrolling)
            {
                maxLinesToRender++; // Render one extra line scrolling in from bottom
            }

            foreach (var line in msgBox.WrappedLines)
            {
                // Skip lines before PageStartLine (previous pages)
                if (lineIndex < msgBox.PageStartLine)
                {
                    lineIndex++;
                    continue;
                }

                // Stop if we've rendered max lines for current state
                if (linesRendered >= maxLinesToRender)
                {
                    break;
                }

                // Rendering logic:
                // - Lines that end with newline have EndIndex = last char index + 1 (includes newline)
                // - The last line (no trailing newline) has EndIndex = last char index
                // - CurrentCharIndex advances for both Char and Newline tokens

                if (msgBox.CurrentCharIndex >= line.EndIndex)
                {
                    // Render complete line (processed all visible characters in this line)
                    // Only render if line is within visible bounds (for scroll clipping)
                    bool lineVisible = lineY >= visibleTop && lineY < visibleBottom;
                    if (!string.IsNullOrEmpty(line.Text) && lineVisible)
                    {
                        RenderTextLine(
                            font,
                            line.Text,
                            textStartX,
                            lineY,
                            msgBox.TextColor,
                            msgBox.ShadowColor,
                            scale
                        );
                    }
                    // Move to next line position (even for empty/clipped lines - preserves spacing)
                    lineY += lineSpacing;
                    lineIndex++;
                    linesRendered++;
                }
                else if (msgBox.CurrentCharIndex > line.StartIndex)
                {
                    // Render partial line (currently being printed)
                    // Only render if line is within visible bounds (for scroll clipping)
                    bool lineVisible = lineY >= visibleTop && lineY < visibleBottom;
                    int substringLength = msgBox.CurrentCharIndex - line.StartIndex;
                    substringLength = Math.Min(substringLength, line.Text.Length);
                    if (substringLength > 0 && lineVisible)
                    {
                        string partialText = line.Text.Substring(0, substringLength);
                        RenderTextLine(
                            font,
                            partialText,
                            textStartX,
                            lineY,
                            msgBox.TextColor,
                            msgBox.ShadowColor,
                            scale
                        );
                    }
                    // Don't render lines after this one (they haven't been reached yet)
                    break;
                }
                else if (
                    msgBox.CurrentCharIndex == line.StartIndex
                    && string.IsNullOrEmpty(line.Text)
                )
                {
                    // Empty line at exact current position (consecutive newlines)
                    lineY += lineSpacing;
                    lineIndex++;
                    linesRendered++;
                    continue;
                }
                else
                {
                    // Line hasn't been reached yet - stop rendering
                    break;
                }
            }
        }

        /// <summary>
        /// Renders a single line of text with shadow.
        /// </summary>
        /// <param name="font">The font to use.</param>
        /// <param name="text">The text to render.</param>
        /// <param name="x">The X position.</param>
        /// <param name="y">The Y position.</param>
        /// <param name="textColor">The text color.</param>
        /// <param name="shadowColor">The shadow color.</param>
        /// <param name="scale">The viewport scale for shadow offset.</param>
        private void RenderTextLine(
            FontStashSharp.DynamicSpriteFont font,
            string text,
            int x,
            int y,
            Color textColor,
            Color shadowColor,
            int scale
        )
        {
            // Render shadow first (offset scales with viewport, matching map popup style)
            int shadowOffset = GameConstants.PopupShadowOffsetY * scale;
            font.DrawText(
                _spriteBatch,
                text,
                new Vector2(x + shadowOffset, y + shadowOffset),
                shadowColor
            );

            // Render main text on top
            font.DrawText(_spriteBatch, text, new Vector2(x, y), textColor);
        }

        /// <summary>
        /// Renders the down arrow indicator when waiting for input.
        /// </summary>
        /// <param name="texture">The tilesheet texture.</param>
        /// <param name="tilesheetDef">The tilesheet definition.</param>
        /// <param name="msgBoxX">The X position of the message box.</param>
        /// <param name="msgBoxY">The Y position of the message box.</param>
        /// <param name="msgBoxWidth">The width of the message box.</param>
        /// <param name="msgBoxHeight">The height of the message box.</param>
        /// <param name="tileSize">The scaled tile size.</param>
        /// <param name="gameTime">The game time for animation.</param>
        private void RenderDownArrow(
            Texture2D texture,
            PopupOutlineDefinition tilesheetDef,
            int msgBoxX,
            int msgBoxY,
            int msgBoxWidth,
            int msgBoxHeight,
            int tileSize,
            GameTime gameTime
        )
        {
            // Use tile 9 for down arrow (or first available tile after frame tiles)
            int arrowTileIndex = 9; // Default to tile 9

            // Get source rectangle for arrow tile
            Rectangle GetArrowTileSourceRect(int tileIndex)
            {
                if (tilesheetDef.Tiles != null)
                {
                    foreach (var tile in tilesheetDef.Tiles)
                    {
                        if (tile != null && tile.Index == tileIndex)
                        {
                            return new Rectangle(tile.X, tile.Y, tile.Width, tile.Height);
                        }
                    }
                }
                // Calculate from grid position if not in Tiles array
                int tileWidth = tilesheetDef.TileWidth;
                int tileHeight = tilesheetDef.TileHeight;
                int columns =
                    tilesheetDef.TileCount > 0
                        ? (int)Math.Sqrt(tilesheetDef.TileCount)
                        : MessageBoxConstants.DefaultTilesheetColumns;
                int row = tileIndex / columns;
                int col = tileIndex % columns;
                int tileX = col * tileWidth;
                int tileY = row * tileHeight;
                return new Rectangle(tileX, tileY, tileWidth, tileHeight);
            }

            var arrowSrcRect = GetArrowTileSourceRect(arrowTileIndex);

            // Animate arrow: blink every 0.5 seconds (time-based for frame-rate independence)
            // Convert frames to seconds: ArrowBlinkFrames (30) / 60 FPS = 0.5 seconds
            double blinkIntervalSeconds =
                MessageBoxConstants.ArrowBlinkFrames / MessageBoxConstants.GbaFrameRate;
            bool isVisible =
                ((int)(gameTime.TotalGameTime.TotalSeconds / blinkIntervalSeconds)) % 2 == 0;
            if (!isVisible)
            {
                return; // Don't render during "off" phase
            }

            // Position at bottom-right of message box frame (inside frame, padded from edges)
            int arrowPadding =
                MessageBoxConstants.TextPaddingTop * (tileSize / tilesheetDef.TileWidth); // Scale padding
            int arrowX = msgBoxX + msgBoxWidth - tileSize - arrowPadding;
            int arrowY = msgBoxY + msgBoxHeight - tileSize - arrowPadding;

            var destRect = new Rectangle(arrowX, arrowY, tileSize, tileSize);
            _spriteBatch.Draw(texture, destRect, arrowSrcRect, Color.White);
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

                // Dispose cached texture to prevent memory leak
                _messageBoxTexture?.Dispose();
                _messageBoxTexture = null;

                // Clear tracked scene entity
                _activeMessageBoxSceneEntity = null;
            }
            _disposed = true;
        }
    }
}
