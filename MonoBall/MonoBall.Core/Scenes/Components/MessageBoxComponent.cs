using System.Collections.Generic;
using Microsoft.Xna.Framework;

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
        /// The current position in the token list (token index).
        /// Used to track which token is currently being processed.
        /// </summary>
        public int CurrentTokenIndex { get; set; }

        /// <summary>
        /// The current position in the text string (character index).
        /// Used for rendering reference to determine which characters to display.
        /// </summary>
        public int CurrentCharIndex { get; set; }

        /// <summary>
        /// Text speed setting in seconds per character (0 = instant, higher = slower).
        /// Maps to player text speed preference.
        /// </summary>
        public float TextSpeed { get; set; }

        /// <summary>
        /// Time delay counter in seconds (decrements by deltaTime each update, when <= 0, advance character).
        /// </summary>
        public float DelayCounter { get; set; }

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
        /// Default text color (stored at creation for {RESET} control code).
        /// </summary>
        public Color DefaultTextColor { get; set; }

        /// <summary>
        /// Default shadow color (stored at creation for {RESET} control code).
        /// </summary>
        public Color DefaultShadowColor { get; set; }

        /// <summary>
        /// Default text speed (stored at creation for {RESET} control code).
        /// </summary>
        public float DefaultTextSpeed { get; set; }

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
        /// Whether the message box is waiting for player input (A/B button press).
        /// </summary>
        public bool IsWaitingForInput { get; set; }

        /// <summary>
        /// The line number where the current page starts (for pagination/scrolling).
        /// When text fills the visible area, we wait for input then advance PageStartLine.
        /// </summary>
        public int PageStartLine { get; set; }

        /// <summary>
        /// Current scroll offset in pixels (for smooth scroll animation).
        /// Applied as negative Y offset during rendering. Increases as scroll progresses.
        /// </summary>
        public float ScrollOffset { get; set; }

        /// <summary>
        /// Remaining pixels to scroll during animation.
        /// Starts at font height + line spacing, decreases each frame until 0.
        /// </summary>
        public float ScrollDistanceRemaining { get; set; }

        /// <summary>
        /// Scroll speed in pixels per second (based on text speed setting).
        /// Time-based for consistent behavior across different frame rates.
        /// </summary>
        public float ScrollSpeed { get; set; }

        // ============================================
        // Text Effect Animation State
        // ============================================

        /// <summary>
        /// Total elapsed time for effect animations (seconds).
        /// Incremented each frame by deltaTime.
        /// </summary>
        public float EffectTime { get; set; }

        /// <summary>
        /// Cached shake offsets per character index.
        /// Regenerated when shake interval elapses.
        /// Key: character index, Value: (X, Y) offset in pixels.
        /// </summary>
        public Dictionary<int, Vector2>? ShakeOffsets { get; set; }

        /// <summary>
        /// Time of last shake offset regeneration.
        /// Used to determine when to regenerate shake offsets.
        /// </summary>
        public float LastShakeTime { get; set; }

        /// <summary>
        /// Currently active effect ID (from {FX:effectId} tag).
        /// Empty string means no effect active.
        /// </summary>
        public string CurrentEffectId { get; set; }

        /// <summary>
        /// Whether the current text color was explicitly set via {COLOR} tag.
        /// Used for "preserve" colorMode in effects.
        /// </summary>
        public bool HasManualColor { get; set; }

        /// <summary>
        /// Whether the current shadow color was explicitly set via {SHADOW} tag.
        /// Used for "preserve" shadowMode in effects.
        /// </summary>
        public bool HasManualShadow { get; set; }

        /// <summary>
        /// Time of last per-character sound playback (for throttling).
        /// Used to prevent sound spam for fast text.
        /// </summary>
        public float LastPerCharacterSoundTime { get; set; }
    }
}
