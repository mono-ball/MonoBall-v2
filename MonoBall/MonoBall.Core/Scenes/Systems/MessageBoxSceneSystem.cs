using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Arch.Core;
using Arch.System;
using FontStashSharp;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoBall.Core.Constants;
using MonoBall.Core.ECS;
using MonoBall.Core.ECS.Components;
using MonoBall.Core.ECS.Events;
using MonoBall.Core.ECS.Events.Audio;
using MonoBall.Core.ECS.Input;
using MonoBall.Core.ECS.Services;
using MonoBall.Core.Mods;
using MonoBall.Core.Rendering;
using MonoBall.Core.Resources;
using MonoBall.Core.Scenes.Components;
using MonoBall.Core.TextEffects;
using MonoBall.Core.UI.Windows.Backgrounds;
using MonoBall.Core.UI.Windows.Borders;
using MonoBall.Core.UI.Windows.Content;
using Serilog;

namespace MonoBall.Core.Scenes.Systems;

/// <summary>
///     System that handles lifecycle, updates, and rendering for MessageBoxScene entities.
///     Manages message box text printing, input handling, and rendering.
/// </summary>
public class MessageBoxSceneSystem
    : BaseSystem<World, float>,
        IPrioritizedSystem,
        IDisposable,
        ISceneSystem
{
    private readonly QueryDescription _cameraQuery;
    private readonly ICameraService _cameraService;
    private readonly IConstantsService _constants;
    private readonly string _defaultFontId;
    private readonly int _defaultFontSize;
    private readonly int _defaultLineSpacing;
    private readonly int _defaultScrollDistance;
    private readonly string _defaultTextSpeed;
    private readonly IFlagVariableService _flagVariableService;
    private readonly int _gbaReferenceHeight;
    private readonly int _gbaReferenceWidth;
    private readonly GraphicsDevice _graphicsDevice;
    private readonly IInputBindingService _inputBindingService;
    private readonly ILogger _logger;
    private readonly int _maxVisibleLines;
    private readonly int _messageBoxInteriorHeight;
    private readonly int _messageBoxInteriorTileX;
    private readonly int _messageBoxInteriorTileY;
    private readonly int _messageBoxInteriorWidth;

    // Cached QueryDescription (created in constructor, never in hot paths)
    private readonly QueryDescription _messageBoxScenesQuery;
    private readonly string _messageBoxTilesheetId;
    private readonly IModManager _modManager;
    private readonly IResourceManager _resourceManager;
    private readonly ISceneManager _sceneManager;

    // Cached constants (performance optimization - avoid lookups in Update/Render loops)
    private readonly int _scenePriorityOffset;
    private readonly float _scrollSpeedFastPixelsPerSecond;
    private readonly float _scrollSpeedInstantPixelsPerSecond;
    private readonly float _scrollSpeedMediumPixelsPerSecond;
    private readonly float _scrollSpeedSlowPixelsPerSecond;
    private readonly SpriteBatch _spriteBatch;
    private readonly ITextEffectCalculator _textEffectCalculator;
    private readonly int _textPaddingX;
    private readonly float _textSpeedFastSeconds;
    private readonly float _textSpeedInstantSeconds;
    private readonly float _textSpeedMediumSeconds;
    private readonly float _textSpeedSlowSeconds;
    private readonly string _textSpeedVariableName;

    // Track active message box scene entity (enforce single message box)
    private Entity? _activeMessageBoxSceneEntity;

    private bool _disposed;

    // Texture cache for message box tilesheet
    private Texture2D? _messageBoxTexture;

    /// <summary>
    ///     Initializes a new instance of the MessageBoxSceneSystem.
    /// </summary>
    /// <param name="world">The ECS world. Required.</param>
    /// <param name="sceneManager">The scene manager for creating/destroying scenes. Required.</param>
    /// <param name="modManager">The mod manager for accessing definitions. Required.</param>
    /// <param name="inputBindingService">The input binding service for button detection. Required.</param>
    /// <param name="flagVariableService">The flag/variable service for text speed preference. Required.</param>
    /// <param name="cameraService">The camera service for querying active camera. Required.</param>
    /// <param name="graphicsDevice">The graphics device. Required.</param>
    /// <param name="spriteBatch">The sprite batch for rendering. Required.</param>
    /// <param name="logger">The logger for logging operations. Required.</param>
    /// <param name="constants">The constants service for accessing game constants. Required.</param>
    /// <param name="textEffectCalculator">The text effect calculator for animated effects. Required.</param>
    /// <param name="resourceManager">The resource manager for loading textures and fonts. Required.</param>
    public MessageBoxSceneSystem(
        World world,
        ISceneManager sceneManager,
        IModManager modManager,
        IInputBindingService inputBindingService,
        IFlagVariableService flagVariableService,
        ICameraService cameraService,
        GraphicsDevice graphicsDevice,
        SpriteBatch spriteBatch,
        ILogger logger,
        IConstantsService constants,
        ITextEffectCalculator textEffectCalculator,
        IResourceManager resourceManager
    )
        : base(world)
    {
        _sceneManager = sceneManager ?? throw new ArgumentNullException(nameof(sceneManager));
        _modManager = modManager ?? throw new ArgumentNullException(nameof(modManager));
        _resourceManager =
            resourceManager ?? throw new ArgumentNullException(nameof(resourceManager));
        _inputBindingService =
            inputBindingService ?? throw new ArgumentNullException(nameof(inputBindingService));
        _flagVariableService =
            flagVariableService ?? throw new ArgumentNullException(nameof(flagVariableService));
        _cameraService = cameraService ?? throw new ArgumentNullException(nameof(cameraService));
        _graphicsDevice = graphicsDevice ?? throw new ArgumentNullException(nameof(graphicsDevice));
        _spriteBatch = spriteBatch ?? throw new ArgumentNullException(nameof(spriteBatch));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _constants = constants ?? throw new ArgumentNullException(nameof(constants));
        _textEffectCalculator =
            textEffectCalculator ?? throw new ArgumentNullException(nameof(textEffectCalculator));

        // Cache constants used in Update/Render loops (performance optimization)
        _scenePriorityOffset = _constants.Get<int>("ScenePriorityOffset");
        _defaultFontId = _constants.GetString("DefaultFontId");
        _messageBoxTilesheetId = _constants.GetString("MessageBoxTilesheetId");
        _defaultLineSpacing = _constants.Get<int>("DefaultLineSpacing");
        _textSpeedVariableName = _constants.GetString("TextSpeedVariableName");
        _defaultTextSpeed = _constants.GetString("DefaultTextSpeed");
        _textSpeedSlowSeconds = _constants.Get<float>("TextSpeedSlowSeconds");
        _textSpeedMediumSeconds = _constants.Get<float>("TextSpeedMediumSeconds");
        _textSpeedFastSeconds = _constants.Get<float>("TextSpeedFastSeconds");
        _textSpeedInstantSeconds = _constants.Get<float>("TextSpeedInstantSeconds");
        _maxVisibleLines = _constants.Get<int>("MaxVisibleLines");
        _defaultScrollDistance = _constants.Get<int>("DefaultScrollDistance");
        _scrollSpeedSlowPixelsPerSecond = _constants.Get<float>("ScrollSpeedSlowPixelsPerSecond");
        _scrollSpeedMediumPixelsPerSecond = _constants.Get<float>(
            "ScrollSpeedMediumPixelsPerSecond"
        );
        _scrollSpeedFastPixelsPerSecond = _constants.Get<float>("ScrollSpeedFastPixelsPerSecond");
        _scrollSpeedInstantPixelsPerSecond = _constants.Get<float>(
            "ScrollSpeedInstantPixelsPerSecond"
        );
        _defaultFontSize = _constants.Get<int>("DefaultFontSize");
        _messageBoxInteriorWidth = _constants.Get<int>("MessageBoxInteriorWidth");
        _messageBoxInteriorHeight = _constants.Get<int>("MessageBoxInteriorHeight");
        _textPaddingX = _constants.Get<int>("TextPaddingX");
        _messageBoxInteriorTileX = _constants.Get<int>("MessageBoxInteriorTileX");
        _messageBoxInteriorTileY = _constants.Get<int>("MessageBoxInteriorTileY");
        _gbaReferenceWidth = _constants.Get<int>("ReferenceWidth");
        _gbaReferenceHeight = _constants.Get<int>("ReferenceHeight");

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
    ///     Disposes the system and unsubscribes from events.
    /// </summary>
    public new void Dispose()
    {
        Dispose(true);
    }

    /// <summary>
    ///     Gets the execution priority for this system.
    /// </summary>
    public int Priority => SystemPriority.MessageBoxScene;

    /// <summary>
    ///     Updates a specific message box scene entity.
    ///     Implements ISceneSystem interface.
    /// </summary>
    /// <param name="sceneEntity">The scene entity to update.</param>
    /// <param name="deltaTime">The elapsed time since last update.</param>
    /// <remarks>
    ///     Per-scene updates are handled by ProcessInternal() which queries for all message box scenes.
    ///     This method exists to satisfy ISceneSystem interface but doesn't need per-scene logic.
    /// </remarks>
    public void Update(Entity sceneEntity, float deltaTime)
    {
        // Per-scene update not needed - ProcessInternal() handles all message box scenes via query
    }

    /// <summary>
    ///     Renders a specific message box scene entity.
    ///     Implements ISceneSystem interface.
    /// </summary>
    /// <param name="sceneEntity">The scene entity to render.</param>
    /// <param name="gameTime">The game time.</param>
    /// <exception cref="InvalidOperationException">Thrown if scene entity does not have MessageBoxComponent.</exception>
    public void RenderScene(Entity sceneEntity, GameTime gameTime)
    {
        if (!World.Has<MessageBoxComponent>(sceneEntity))
            throw new InvalidOperationException(
                $"Scene entity {sceneEntity.Id} does not have MessageBoxComponent. "
                    + "Cannot render message box without component."
            );

        ref var msgBox = ref World.Get<MessageBoxComponent>(sceneEntity);
        if (!msgBox.IsVisible || msgBox.State == MessageBoxRenderState.Hidden)
            return;

        RenderMessageBox(sceneEntity, ref msgBox, gameTime);
    }

    /// <summary>
    ///     Performs internal processing that needs to run every frame.
    ///     Implements ISceneSystem interface.
    ///     Delegates to Update() to follow the same pattern as other scene systems.
    /// </summary>
    /// <param name="deltaTime">The elapsed time since last update.</param>
    public void ProcessInternal(float deltaTime)
    {
        // Delegate to the internal Update method that processes message box state machine
        Update(in deltaTime);
    }

    /// <summary>
    ///     Validates and retrieves a font system by font ID.
    /// </summary>
    /// <param name="fontId">The font ID to validate and retrieve.</param>
    /// <returns>The font system if found.</returns>
    /// <exception cref="InvalidOperationException">Thrown if font is not found.</exception>
    private FontSystem ValidateAndGetFont(string fontId)
    {
        try
        {
            return _resourceManager.LoadFont(fontId);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Font '{fontId}' not found. Cannot create message box without valid font. "
                    + $"Font must exist in mod registry.",
                ex
            );
        }
    }

    /// <summary>
    ///     Validates and retrieves the message box tilesheet definition.
    /// </summary>
    /// <returns>The tilesheet definition if found.</returns>
    /// <exception cref="InvalidOperationException">Thrown if tilesheet is not found.</exception>
    private PopupOutlineDefinition ValidateAndGetTilesheet()
    {
        var tilesheet = _modManager.GetDefinition<PopupOutlineDefinition>(_messageBoxTilesheetId);
        if (tilesheet == null)
            throw new InvalidOperationException(
                $"Tilesheet '{_messageBoxTilesheetId}' not found. "
                    + $"Cannot create message box without tilesheet. Tilesheet must exist in mod registry."
            );
        return tilesheet;
    }

    /// <summary>
    ///     Updates message box text printing state machine, handles input, and processes character-by-character printing.
    ///     Overrides BaseSystem.Update() to follow standard Arch ECS pattern.
    /// </summary>
    /// <param name="deltaTime">The elapsed time since last update.</param>
    public override void Update(in float deltaTime)
    {
        // Copy in parameter to local variable for use in lambda (cannot capture in parameters)
        var dt = deltaTime;

        // Query for active message box scenes
        World.Query(
            in _messageBoxScenesQuery,
            (Entity sceneEntity, ref MessageBoxComponent msgBox) =>
            {
                // Validate entity is still alive (prevent stale references)
                if (!World.IsAlive(sceneEntity))
                    return;

                if (msgBox.State == MessageBoxRenderState.Hidden)
                    return;

                // Update effect animation time (always, for smooth animations)
                msgBox.EffectTime += dt;

                // Handle shake offset regeneration if we have an active effect with shake
                if (!string.IsNullOrEmpty(msgBox.CurrentEffectId))
                    UpdateShakeOffsets(ref msgBox);

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
                            var scrollAmount = msgBox.ScrollSpeed * dt;
                            if (scrollAmount > msgBox.ScrollDistanceRemaining)
                                scrollAmount = msgBox.ScrollDistanceRemaining;
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
                            return;
                        // If state changed to Paused, stop processing (will resume when delay finishes)
                        if (msgBox.State == MessageBoxRenderState.Paused) { }

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
    ///     Handles MessageBoxShowEvent by creating a new message box scene.
    ///     Enforces single message box policy (destroys existing message box if present).
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
                _sceneManager.DestroyScene(existingSceneEntity);

            _activeMessageBoxSceneEntity = null;
        }

        // Get player text speed preference (with fallback to default)
        var textSpeed = evt.TextSpeedOverride ?? GetPlayerTextSpeed();

        // Validate font exists (fail fast, no fallback)
        var fontId = evt.FontId ?? _defaultFontId;
        var fontSystem = ValidateAndGetFont(fontId);

        // Validate tilesheet exists (fail fast, no fallback)
        var tilesheet = ValidateAndGetTilesheet();

        // Create SceneComponent object
        var sceneComponent = new SceneComponent
        {
            SceneId = $"messagebox:{Guid.NewGuid()}",
            Priority = ScenePriorities.GameScene + _scenePriorityOffset, // 70
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
        var initialTextColor = evt.TextColor ?? new Color(98, 98, 98, 255); // Dark gray text
        var initialShadowColor = evt.ShadowColor ?? new Color(213, 213, 205, 255); // Light gray shadow

        // Add MessageBoxComponent with text and state
        World.Add(
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
                LineSpacing = _defaultLineSpacing,
                IsVisible = true,
                IsWaitingForInput = false,
                PageStartLine = 0, // Start at first line (first page)
                // Scroll animation fields (initialized, but not active until Scrolling state)
                ScrollOffset = 0,
                ScrollDistanceRemaining = 0,
                ScrollSpeed = GetScrollSpeed(textSpeed),
                // Effect animation fields (initialized for text effects)
                EffectTime = 0f,
                ShakeOffsets = null,
                LastShakeTime = 0f,
                CurrentEffectId = string.Empty,
                HasManualColor = false,
                HasManualShadow = false,
                LastPerCharacterSoundTime = 0f,
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
    ///     Handles MessageBoxHideEvent by destroying the message box scene.
    ///     Made idempotent to prevent race conditions from multiple rapid event calls.
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

                    // Fire MessageBoxClosedEvent after scene is destroyed
                    // This event is used by InteractionSystem to resume behavior scripts
                    var closedEvent = new MessageBoxClosedEvent { MessageBoxEntity = entity };
                    EventBus.Send(ref closedEvent);
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
    ///     Handles MessageBoxTextAdvanceEvent when player presses A/B to advance text.
    /// </summary>
    /// <param name="evt">The advance event.</param>
    private void OnMessageBoxTextAdvance(ref MessageBoxTextAdvanceEvent evt)
    {
        if (!_activeMessageBoxSceneEntity.HasValue)
            return;

        var sceneEntity = _activeMessageBoxSceneEntity.Value;

        // Validate entity is still alive
        if (!World.IsAlive(sceneEntity))
        {
            _activeMessageBoxSceneEntity = null;
            return;
        }

        if (!World.Has<MessageBoxComponent>(sceneEntity))
            return;

        ref var msgBox = ref World.Get<MessageBoxComponent>(sceneEntity);

        // Check if waiting for input
        if (msgBox.IsWaitingForInput)
        {
            if (msgBox.State == MessageBoxRenderState.Wait)
                // Page break: advance to fresh page
                AdvanceToNextPage(ref msgBox);
            else if (msgBox.State == MessageBoxRenderState.WaitForScroll)
                // Scroll: only animate if we've filled the visible area.
                StartScrollAnimation(ref msgBox);
        }
        // Note: Finished state is handled in HandleInput() when player presses A/B
    }

    /// <summary>
    ///     Gets the player's text speed preference from global variables.
    /// </summary>
    /// <returns>The text speed in seconds per character (matching pokeemerald-expansion timing).</returns>
    private float GetPlayerTextSpeed()
    {
        var speedStr = _flagVariableService.GetVariable<string>(_textSpeedVariableName);

        if (string.IsNullOrEmpty(speedStr))
            speedStr = _defaultTextSpeed;

        return speedStr.ToLowerInvariant() switch
        {
            "slow" => _textSpeedSlowSeconds,
            "medium" => _textSpeedMediumSeconds,
            "fast" => _textSpeedFastSeconds,
            "instant" => _textSpeedInstantSeconds,
            _ => _textSpeedMediumSeconds, // Default fallback
        };
    }

    /// <summary>
    ///     Advances to the next page by resetting PageStartLine to CurrentY.
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
    ///     Starts scroll animation if visible area is filled, otherwise continues printing.
    /// </summary>
    /// <param name="msgBox">The message box component to update.</param>
    private void StartScrollAnimation(ref MessageBoxComponent msgBox)
    {
        var visibleLineCount = msgBox.CurrentY - msgBox.PageStartLine;
        if (visibleLineCount >= _maxVisibleLines)
        {
            // Start scroll animation (like pokeemerald-expansion RENDER_STATE_SCROLL)
            // ScrollDistanceRemaining = font height + line spacing
            msgBox.ScrollDistanceRemaining = _defaultScrollDistance + msgBox.LineSpacing;
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
    ///     Initializes shake offsets for the message box using the specified effect definition.
    /// </summary>
    /// <param name="msgBox">The message box component to update.</param>
    /// <param name="effectDef">The text effect definition containing shake parameters.</param>
    /// <exception cref="InvalidOperationException">Thrown when required dependencies are null.</exception>
    private void InitializeShakeOffsets(
        ref MessageBoxComponent msgBox,
        TextEffectDefinition effectDef
    )
    {
        if (_textEffectCalculator == null)
            throw new InvalidOperationException(
                "TextEffectCalculator is required for shake effect initialization."
            );

        // Calculate actual character count from wrapped lines (more accurate than token count)
        var charCount = 0;
        if (msgBox.WrappedLines != null && msgBox.WrappedLines.Count > 0)
            // Use the maximum EndIndex from all wrapped lines as total character count
            charCount = msgBox.WrappedLines.Max(line => line.EndIndex);
        else if (msgBox.ParsedText != null)
            // Fallback: count character tokens if wrapped lines not available
            charCount = msgBox.ParsedText.Count(token => token.TokenType == TextTokenType.Char);

        // Calculate seed: use deterministic seed if configured, otherwise use time-based seed
        int seed;
        if (effectDef.DeterministicShake)
            // Use configured seed, combine with effect time for variation over time
            seed = effectDef.ShakeRandomSeed + (int)(msgBox.EffectTime * 1000);
        else
            // Use EffectTime as seed for reproducible randomness per regeneration
            seed = (int)(msgBox.EffectTime * 1000);

        msgBox.ShakeOffsets = _textEffectCalculator.GenerateShakeOffsets(
            effectDef,
            charCount,
            seed
        );
        msgBox.LastShakeTime = msgBox.EffectTime;
    }

    /// <summary>
    ///     Updates shake offsets if the shake interval has elapsed.
    /// </summary>
    /// <param name="msgBox">The message box component to update.</param>
    /// <exception cref="InvalidOperationException">Thrown when required dependencies are null.</exception>
    private void UpdateShakeOffsets(ref MessageBoxComponent msgBox)
    {
        if (_modManager == null)
            throw new InvalidOperationException("ModManager is required for shake effect updates.");
        if (_textEffectCalculator == null)
            throw new InvalidOperationException(
                "TextEffectCalculator is required for shake effect updates."
            );

        // Get effect definition to check if it has shake
        var effectDef = _modManager.GetDefinition<TextEffectDefinition>(msgBox.CurrentEffectId);
        if (effectDef == null || !effectDef.EffectTypes.HasFlag(TextEffectType.Shake))
            return; // No effect or no shake component

        // Check if we need to regenerate shake offsets using effect definition's interval
        var timeSinceLastShake = msgBox.EffectTime - msgBox.LastShakeTime;
        if (timeSinceLastShake < effectDef.ShakeIntervalSeconds)
            return; // Not time to regenerate yet

        // Regenerate shake offsets
        InitializeShakeOffsets(ref msgBox, effectDef);
    }

    /// <summary>
    ///     Gets the scroll speed in pixels per second based on text speed setting.
    ///     Time-based for consistent behavior across different frame rates.
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
            return _scrollSpeedInstantPixelsPerSecond;
        }

        // Map text speed to scroll speed (converted from pokeemerald-expansion frame-based values)
        if (textSpeed >= _textSpeedSlowSeconds)
            return _scrollSpeedSlowPixelsPerSecond;

        if (textSpeed >= _textSpeedMediumSeconds)
            return _scrollSpeedMediumPixelsPerSecond;

        if (textSpeed >= _textSpeedFastSeconds)
            return _scrollSpeedFastPixelsPerSecond;

        return _scrollSpeedInstantPixelsPerSecond;
    }

    /// <summary>
    ///     Handles input for message box (A/B button presses).
    /// </summary>
    /// <param name="sceneEntity">The scene entity.</param>
    /// <param name="msgBox">The message box component.</param>
    private void HandleInput(Entity sceneEntity, ref MessageBoxComponent msgBox)
    {
        // Check if Interact button (A/B) is just pressed (not held)
        var interactJustPressed = _inputBindingService.IsActionJustPressed(InputAction.Interact);
        var interactPressed = _inputBindingService.IsActionPressed(InputAction.Interact);

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
                msgBox.DelayCounter = 0.0f; // Keep delay at 0 while held
        }
    }

    /// <summary>
    ///     Processes the current character/token in the text printing state machine.
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

                // Play per-character sound effect if configured (with throttling)
                if (!string.IsNullOrEmpty(msgBox.CurrentEffectId))
                {
                    var effectDef = _modManager.GetDefinition<TextEffectDefinition>(
                        msgBox.CurrentEffectId
                    );
                    if (effectDef != null && !string.IsNullOrEmpty(effectDef.PerCharacterSound))
                    {
                        // Check if enough time has passed since last per-character sound
                        var timeSinceLastSound =
                            msgBox.EffectTime - msgBox.LastPerCharacterSoundTime;
                        if (timeSinceLastSound >= effectDef.PerCharacterSoundInterval)
                        {
                            var soundEvent = new PlaySoundEffectEvent
                            {
                                AudioId = effectDef.PerCharacterSound,
                                Volume = -1f, // Use definition default
                                Pitch = 0f,
                                Pan = 0f,
                            };
                            EventBus.Send(ref soundEvent);
                            msgBox.LastPerCharacterSoundTime = msgBox.EffectTime;
                        }
                    }
                }

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
                var visibleLineCount = msgBox.CurrentY - msgBox.PageStartLine;
                if (visibleLineCount >= _maxVisibleLines)
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
                var pauseSeconds = token.GetPauseSeconds();
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
                var newColor = token.GetColor();
                if (newColor.HasValue)
                {
                    msgBox.TextColor = newColor.Value;
                    msgBox.HasManualColor = true; // Mark color as manually set
                }

                msgBox.DelayCounter = msgBox.TextSpeed;
                break;

            case TextTokenType.Shadow:
                // Change shadow color
                msgBox.CurrentTokenIndex++;
                var newShadowColor = token.GetShadowColor();
                if (newShadowColor.HasValue)
                {
                    msgBox.ShadowColor = newShadowColor.Value;
                    msgBox.HasManualShadow = true; // Mark shadow as manually set
                }

                msgBox.DelayCounter = msgBox.TextSpeed;
                break;

            case TextTokenType.Speed:
                // Change text speed (convert frames to seconds)
                msgBox.CurrentTokenIndex++;
                var newSpeedFrames = token.GetSpeed();
                if (newSpeedFrames.HasValue)
                {
                    // Convert frames to seconds (GBA runs at 60 FPS)
                    const float gbaFrameRate = 60.0f;
                    msgBox.TextSpeed = newSpeedFrames.Value / gbaFrameRate;
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
                // Also reset effect and manual color/shadow flags
                msgBox.CurrentEffectId = string.Empty;
                msgBox.HasManualColor = false;
                msgBox.HasManualShadow = false;
                msgBox.DelayCounter = msgBox.TextSpeed;
                break;

            case TextTokenType.EffectStart:
                // Start applying text effect to subsequent characters
                msgBox.CurrentTokenIndex++;
                var effectId = token.GetEffectId();
                if (!string.IsNullOrEmpty(effectId))
                {
                    msgBox.CurrentEffectId = effectId;
                    // Initialize shake offsets if effect has shake component
                    var effectDef = _modManager.GetDefinition<TextEffectDefinition>(effectId);
                    if (effectDef != null)
                    {
                        if (effectDef.EffectTypes.HasFlag(TextEffectType.Shake))
                            InitializeShakeOffsets(ref msgBox, effectDef);

                        // Play on-start sound effect if configured
                        if (!string.IsNullOrEmpty(effectDef.OnStartSound))
                        {
                            var soundEvent = new PlaySoundEffectEvent
                            {
                                AudioId = effectDef.OnStartSound,
                                Volume = -1f, // Use definition default
                                Pitch = 0f,
                                Pan = 0f,
                            };
                            EventBus.Send(ref soundEvent);
                        }
                    }
                }

                msgBox.DelayCounter = msgBox.TextSpeed;
                break;

            case TextTokenType.EffectEnd:
                // Stop applying text effect
                msgBox.CurrentTokenIndex++;

                // Play on-end sound effect if configured
                if (!string.IsNullOrEmpty(msgBox.CurrentEffectId))
                {
                    var effectDef = _modManager.GetDefinition<TextEffectDefinition>(
                        msgBox.CurrentEffectId
                    );
                    if (effectDef != null && !string.IsNullOrEmpty(effectDef.OnEndSound))
                    {
                        var soundEvent = new PlaySoundEffectEvent
                        {
                            AudioId = effectDef.OnEndSound,
                            Volume = -1f, // Use definition default
                            Pitch = 0f,
                            Pan = 0f,
                        };
                        EventBus.Send(ref soundEvent);
                    }
                }

                msgBox.CurrentEffectId = string.Empty;
                msgBox.ShakeOffsets = null;
                msgBox.DelayCounter = msgBox.TextSpeed;
                break;
        }
    }

    /// <summary>
    ///     Parses control codes from text string into tokens.
    /// </summary>
    /// <param name="text">The text to parse.</param>
    /// <returns>List of parsed tokens.</returns>
    /// <exception cref="ArgumentException">Thrown for malformed control codes.</exception>
    /// <exception cref="FormatException">Thrown for invalid parameter formats.</exception>
    private List<TextToken> ParseControlCodes(string text)
    {
        var tokens = new List<TextToken>();
        if (string.IsNullOrEmpty(text))
            return tokens;

        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            var originalPosition = i;

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
                var next = text[i + 1];
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

                if (next == 'n')
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

                if (next == 'r')
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

                if (next == 'p')
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

                if (next == 'l')
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
                var startPos = i;
                i++; // Skip '{'

                // Find closing '}'
                var endPos = text.IndexOf('}', i);
                if (endPos == -1)
                    throw new ArgumentException(
                        $"Unclosed control code at position {startPos}: '{text.Substring(startPos, Math.Min(20, text.Length - startPos))}...'"
                    );

                var controlCode = text.Substring(i, endPos - i);
                i = endPos + 1; // Skip '}'

                // Parse control code using strategy pattern (extensible, follows Open/Closed Principle)
                if (ControlCodeParsers.TryParse(controlCode, originalPosition, out var token))
                    tokens.Add(token);
                else
                    throw new ArgumentException(
                        $"Unknown control code at position {startPos}: '{controlCode}'"
                    );
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
    ///     Wraps text into lines based on message box width.
    ///     Builds CharacterRenderData for lines with effects.
    /// </summary>
    /// <param name="parsedText">The parsed text tokens.</param>
    /// <param name="fontId">The font ID to use for measurement.</param>
    /// <returns>List of wrapped lines.</returns>
    /// <exception cref="InvalidOperationException">Thrown if font not found.</exception>
    private List<WrappedLine> WrapText(List<TextToken> parsedText, string fontId)
    {
        var wrappedLines = new List<WrappedLine>();
        if (parsedText == null || parsedText.Count == 0)
            return wrappedLines;

        // Validate font exists (fail fast, no fallback)
        var fontSystem = ValidateAndGetFont(fontId);

        // Get font for measurement (use default size from constants)
        // FontDefinition could be loaded via IModManager for custom sizes, but for now use constant
        var fontSize = _defaultFontSize;
        var font = fontSystem.GetFont(fontSize);

        float maxWidth = _messageBoxInteriorWidth - _textPaddingX * 2; // Account for horizontal padding on both sides
        float currentWidth = 0;
        var lineStartIndex = 0;
        var currentLine = new StringBuilder();
        var charIndex = 0; // Track character index in original text

        // Effect state tracking for CharacterRenderData
        var currentEffectId = string.Empty;
        var currentTextColor = new Color(98, 98, 98, 255); // Default dark gray
        var currentShadowColor = new Color(213, 213, 205, 255); // Default light gray
        var hasManualColor = false;
        var hasManualShadow = false;
        var lineHasEffects = false;

        // Per-line character data (only populated if line has effects)
        var currentLineCharData = new List<CharacterRenderData>();

        for (var i = 0; i < parsedText.Count; i++)
        {
            var token = parsedText[i];

            if (token.TokenType == TextTokenType.Char)
            {
                var ch = (char)token.Value!;
                var charStr = ch.ToString();

                // Measure character width
                var bounds = font.MeasureString(charStr);
                var charWidth = bounds.X;

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
                            HasEffects = lineHasEffects,
                            CharacterData = lineHasEffects
                                ? new List<CharacterRenderData>(currentLineCharData)
                                : null,
                        }
                    );

                    // Start new line
                    currentLine.Clear();
                    currentLineCharData.Clear();
                    currentWidth = 0;
                    lineStartIndex = charIndex;
                    lineHasEffects = !string.IsNullOrEmpty(currentEffectId); // New line inherits effect state
                }

                // Build character render data
                currentLineCharData.Add(
                    new CharacterRenderData
                    {
                        Character = ch,
                        BaseX = currentWidth,
                        CharIndex = charIndex,
                        TextColor = currentTextColor,
                        ShadowColor = currentShadowColor,
                        EffectId = currentEffectId,
                        HasManualColor = hasManualColor,
                        HasManualShadow = hasManualShadow,
                    }
                );

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
                        HasEffects = lineHasEffects,
                        CharacterData = lineHasEffects
                            ? new List<CharacterRenderData>(currentLineCharData)
                            : null,
                    }
                );

                // Increment charIndex for newline/page break AFTER setting EndIndex
                charIndex++;

                // Start new line
                currentLine.Clear();
                currentLineCharData.Clear();
                currentWidth = 0;
                lineStartIndex = charIndex; // New line starts after the newline/page break character
                lineHasEffects = !string.IsNullOrEmpty(currentEffectId); // New line inherits effect state
            }
            else if (token.TokenType == TextTokenType.Clear)
            {
                // Clear forces new line/page
                if (currentLine.Length > 0)
                    wrappedLines.Add(
                        new WrappedLine
                        {
                            Text = currentLine.ToString(),
                            StartIndex = lineStartIndex,
                            EndIndex = charIndex,
                            Width = currentWidth,
                            HasEffects = lineHasEffects,
                            CharacterData = lineHasEffects
                                ? new List<CharacterRenderData>(currentLineCharData)
                                : null,
                        }
                    );

                // Start new line
                currentLine.Clear();
                currentLineCharData.Clear();
                currentWidth = 0;
                lineStartIndex = charIndex;
                lineHasEffects = !string.IsNullOrEmpty(currentEffectId);
                // Don't increment charIndex for Clear (it's not a visible character)
            }
            else if (token.TokenType == TextTokenType.Color)
            {
                // Track color change for CharacterRenderData
                var newColor = token.GetColor();
                if (newColor.HasValue)
                {
                    currentTextColor = newColor.Value;
                    hasManualColor = true;
                }
            }
            else if (token.TokenType == TextTokenType.Shadow)
            {
                // Track shadow change for CharacterRenderData
                var newShadow = token.GetShadowColor();
                if (newShadow.HasValue)
                {
                    currentShadowColor = newShadow.Value;
                    hasManualShadow = true;
                }
            }
            else if (token.TokenType == TextTokenType.EffectStart)
            {
                // Track effect start
                var effectId = token.GetEffectId();
                if (!string.IsNullOrEmpty(effectId))
                {
                    currentEffectId = effectId;
                    lineHasEffects = true;
                }
            }
            else if (token.TokenType == TextTokenType.EffectEnd)
            {
                // Track effect end
                currentEffectId = string.Empty;
            }
            else if (token.TokenType == TextTokenType.Reset)
            {
                // Reset state
                currentTextColor = new Color(98, 98, 98, 255);
                currentShadowColor = new Color(213, 213, 205, 255);
                hasManualColor = false;
                hasManualShadow = false;
                currentEffectId = string.Empty;
            }
            // Other control codes (Pause, PauseUntilPress, Speed) don't affect wrapping or character data
        }

        // Add final line if there's remaining text
        if (currentLine.Length > 0)
            wrappedLines.Add(
                new WrappedLine
                {
                    Text = currentLine.ToString(),
                    StartIndex = lineStartIndex,
                    EndIndex = charIndex,
                    Width = currentWidth,
                    HasEffects = lineHasEffects,
                    CharacterData = lineHasEffects
                        ? new List<CharacterRenderData>(currentLineCharData)
                        : null,
                }
            );

        return wrappedLines;
    }

    /// <summary>
    ///     Renders the message box frame and text.
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
            return;

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
                    var cameraEntityId = scene.CameraEntityId.Value;
                    var foundCamera = false;
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
                _graphicsDevice.Viewport = new Viewport(camera.Value.VirtualViewport);

            // Calculate viewport scale factor
            var currentScale = CameraTransformUtility.GetViewportScale(
                camera.Value,
                _gbaReferenceWidth
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
                    _messageBoxTilesheetId
                );
                return;
            }

            // Calculate message box dimensions and position (matching pokeemerald-expansion exactly)
            // Window template: tilemapLeft=2, tilemapTop=15, width=27, height=4
            // GBA screen: 240x160 pixels, tiles are 8x8
            var tileSize = tilesheetDef.TileWidth * currentScale;
            var msgBoxInteriorWidth = _messageBoxInteriorWidth * currentScale;
            var msgBoxInteriorHeight = _messageBoxInteriorHeight * currentScale;

            // Position based on GBA tile coordinates, scaled to viewport
            // tilemapLeft=2 means 2 tiles = 16 pixels from left (GBA reference)
            // tilemapTop=15 means 15 tiles = 120 pixels from top (GBA reference)
            // Scale these positions to match the current viewport
            var viewportWidth = _graphicsDevice.Viewport.Width;
            var viewportHeight = _graphicsDevice.Viewport.Height;
            var gbaReferenceWidth = _gbaReferenceWidth;
            var gbaReferenceHeight = _gbaReferenceHeight;

            // Calculate position in GBA reference space, then scale
            var gbaInteriorX = _messageBoxInteriorTileX * tilesheetDef.TileWidth;
            var gbaInteriorY = _messageBoxInteriorTileY * tilesheetDef.TileHeight;

            // Scale to viewport (maintain aspect ratio)
            var scaleX = (float)viewportWidth / gbaReferenceWidth;
            var scaleY = (float)viewportHeight / gbaReferenceHeight;
            var msgBoxInteriorX = (int)(gbaInteriorX * scaleX);
            var msgBoxInteriorY = (int)(gbaInteriorY * scaleY);

            // Calculate scaled font size
            var scaledFontSize = _constants.Get<int>("DefaultFontSize") * currentScale;

            // Create renderers
            var borderRenderer = new MessageBoxDialogueFrameBorderRenderer(
                tilesheetTexture,
                tilesheetDef,
                tileSize,
                _constants,
                _logger
            );

            var backgroundRenderer = new TileSheetBackgroundRenderer(
                tilesheetTexture,
                tilesheetDef,
                tileSize,
                0, // Background tile index
                _constants
            );

            var contentRenderer = new MessageBoxContentRenderer(
                _resourceManager,
                scaledFontSize,
                currentScale,
                _constants,
                _logger,
                _textEffectCalculator,
                _modManager
            );

            // Render background
            if (backgroundRenderer != null)
                backgroundRenderer.RenderBackground(
                    _spriteBatch,
                    msgBoxInteriorX,
                    msgBoxInteriorY,
                    msgBoxInteriorWidth,
                    msgBoxInteriorHeight
                );

            // Render border around interior
            if (borderRenderer != null)
                borderRenderer.RenderBorder(
                    _spriteBatch,
                    msgBoxInteriorX,
                    msgBoxInteriorY,
                    msgBoxInteriorWidth,
                    msgBoxInteriorHeight
                );

            // Render content (message box content renderer uses specialized interface)
            contentRenderer.RenderContent(
                _spriteBatch,
                ref msgBox,
                msgBoxInteriorX,
                msgBoxInteriorY,
                msgBoxInteriorWidth,
                msgBoxInteriorHeight
            );

            // Render down arrow indicator if waiting for input
            if (msgBox.IsWaitingForInput)
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

            _spriteBatch.End();
        }
        finally
        {
            // Always restore viewport, even if rendering fails
            _graphicsDevice.Viewport = savedViewport;
        }
    }

    /// <summary>
    ///     Loads the message box tilesheet texture, caching it for future use.
    /// </summary>
    /// <returns>The loaded texture, or null if loading failed.</returns>
    private Texture2D? LoadMessageBoxTexture()
    {
        // Check cache first
        if (_messageBoxTexture != null)
            return _messageBoxTexture;

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
                _messageBoxTilesheetId
            );
            return null;
        }

        // Load texture using ResourceManager
        try
        {
            return _resourceManager.LoadTexture(_messageBoxTilesheetId);
        }
        catch (Exception ex)
        {
            _logger.Warning(
                ex,
                "Failed to load message box tilesheet texture: {TilesheetId}",
                _messageBoxTilesheetId
            );
            return null;
        }
    }

    /// <summary>
    ///     Renders the down arrow indicator when waiting for input.
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
        var arrowTileIndex = 9; // Default to tile 9

        // Get source rectangle for arrow tile
        Rectangle GetArrowTileSourceRect(int tileIndex)
        {
            if (tilesheetDef.Tiles != null)
                foreach (var tile in tilesheetDef.Tiles)
                    if (tile != null && tile.Index == tileIndex)
                        return new Rectangle(tile.X, tile.Y, tile.Width, tile.Height);

            // Calculate from grid position if not in Tiles array
            var tileWidth = tilesheetDef.TileWidth;
            var tileHeight = tilesheetDef.TileHeight;
            var columns =
                tilesheetDef.TileCount > 0
                    ? (int)Math.Sqrt(tilesheetDef.TileCount)
                    : _constants.Get<int>("DefaultTilesheetColumns");
            var row = tileIndex / columns;
            var col = tileIndex % columns;
            var tileX = col * tileWidth;
            var tileY = row * tileHeight;
            return new Rectangle(tileX, tileY, tileWidth, tileHeight);
        }

        var arrowSrcRect = GetArrowTileSourceRect(arrowTileIndex);

        // Animate arrow: blink every 0.5 seconds (time-based for frame-rate independence)
        // Convert frames to seconds: ArrowBlinkFrames (30) / 60 FPS = 0.5 seconds
        var arrowBlinkFrames = _constants.Get<int>("ArrowBlinkFrames");
        var blinkIntervalSeconds = arrowBlinkFrames / 60.0; // GBA frame rate is 60 FPS
        var isVisible = (int)(gameTime.TotalGameTime.TotalSeconds / blinkIntervalSeconds) % 2 == 0;
        if (!isVisible)
            return; // Don't render during "off" phase

        // Position at bottom-right of message box frame (inside frame, padded from edges)
        var arrowPadding =
            _constants.Get<int>("TextPaddingTop") * (tileSize / tilesheetDef.TileWidth); // Scale padding
        var arrowX = msgBoxX + msgBoxWidth - tileSize - arrowPadding;
        var arrowY = msgBoxY + msgBoxHeight - tileSize - arrowPadding;

        var destRect = new Rectangle(arrowX, arrowY, tileSize, tileSize);
        _spriteBatch.Draw(texture, destRect, arrowSrcRect, Color.White);
    }

    /// <summary>
    ///     Protected dispose method following standard dispose pattern.
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
