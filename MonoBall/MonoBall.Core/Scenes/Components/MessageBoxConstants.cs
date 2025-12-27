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
        /// GBA reference frame rate (frames per second).
        /// Used to convert frame-based delays to time-based delays.
        /// </summary>
        public const float GbaFrameRate = 60.0f;

        /// <summary>
        /// Text speed delay values in seconds per character (matching pokeemerald-expansion).
        /// Converted from frame delays: SLOW=8 frames, MID=4 frames, FAST=1 frame, INSTANT=0 frames.
        /// </summary>
        public const float TextSpeedSlowSeconds = 8.0f / GbaFrameRate; // 8/60 = 0.133 seconds
        public const float TextSpeedMediumSeconds = 4.0f / GbaFrameRate; // 4/60 = 0.067 seconds
        public const float TextSpeedFastSeconds = 1.0f / GbaFrameRate; // 1/60 = 0.017 seconds
        public const float TextSpeedInstantSeconds = 0.0f; // Instant (0 seconds)

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

        /// <summary>
        /// Message box interior width in pixels (GBA-accurate: 27 tiles * 8 pixels = 216).
        /// </summary>
        public const int MessageBoxInteriorWidth = 216;

        /// <summary>
        /// Message box interior height in pixels (GBA-accurate: 4 tiles * 8 pixels = 32).
        /// </summary>
        public const int MessageBoxInteriorHeight = 32;

        /// <summary>
        /// Message box interior X position in tiles (GBA-accurate: tilemapLeft = 2).
        /// </summary>
        public const int MessageBoxInteriorTileX = 2;

        /// <summary>
        /// Message box interior Y position in tiles (GBA-accurate: tilemapTop = 15).
        /// </summary>
        public const int MessageBoxInteriorTileY = 15;

        /// <summary>
        /// Default font size for message box text (can be overridden by FontDefinition).
        /// </summary>
        public const int DefaultFontSize = 16;

        /// <summary>
        /// Text padding from message box top edge (in pixels).
        /// Pokemon uses printer.y = 1 (1 pixel from top of window interior).
        /// </summary>
        public const int TextPaddingTop = 1;

        /// <summary>
        /// Horizontal text padding from message box frame edges (in pixels).
        /// </summary>
        public const int TextPaddingX = 0;

        /// <summary>
        /// Default line spacing (in pixels, added to font size).
        /// Pokemon uses 0 line spacing to maximize text area.
        /// </summary>
        public const int DefaultLineSpacing = 0;

        /// <summary>
        /// Arrow indicator blink rate in frames (30 frames = 0.5 seconds at 60 FPS).
        /// </summary>
        public const int ArrowBlinkFrames = 30;

        /// <summary>
        /// Default tilesheet columns for calculating tile positions (fallback).
        /// </summary>
        public const int DefaultTilesheetColumns = 7;

        /// <summary>
        /// Maximum number of visible lines in the message box.
        /// With 32px interior, 1px top padding, 16px font, 0 line spacing:
        /// Line 1: Y=1 to Y=17, Line 2: Y=17 to Y=33 (fits in 32px).
        /// Matches Pokemon GBA message box behavior.
        /// </summary>
        public const int MaxVisibleLines = 2;

        /// <summary>
        /// Scroll speed in pixels per second for each text speed setting.
        /// Converted from pokeemerald-expansion sTextScrollSpeeds (pixels per frame at 60 FPS).
        /// SLOW=1*60=60, MEDIUM=2*60=120, FAST=4*60=240, INSTANT=6*60=360
        /// </summary>
        public const float ScrollSpeedSlowPixelsPerSecond = 60.0f;
        public const float ScrollSpeedMediumPixelsPerSecond = 120.0f;
        public const float ScrollSpeedFastPixelsPerSecond = 240.0f;
        public const float ScrollSpeedInstantPixelsPerSecond = 360.0f;

        /// <summary>
        /// Default scroll distance in pixels (font height + line spacing).
        /// For 16px font with 0 line spacing = 16 pixels.
        /// </summary>
        public const int DefaultScrollDistance = DefaultFontSize + DefaultLineSpacing;
    }
}
