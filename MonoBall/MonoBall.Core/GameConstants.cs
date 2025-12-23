namespace MonoBall.Core
{
    /// <summary>
    /// Centralized game constants to avoid magic numbers throughout the codebase.
    /// </summary>
    public static class GameConstants
    {
        /// <summary>
        /// Tile chunk size in tiles (16x16 tiles per chunk).
        /// </summary>
        public const int TileChunkSize = 16;

        /// <summary>
        /// Game Boy Advance (GBA) reference width in pixels.
        /// Used for maintaining aspect ratio and pixel-perfect scaling.
        /// </summary>
        public const int GbaReferenceWidth = 240;

        /// <summary>
        /// Game Boy Advance (GBA) reference height in pixels.
        /// Used for maintaining aspect ratio and pixel-perfect scaling.
        /// </summary>
        public const int GbaReferenceHeight = 160;

        /// <summary>
        /// Default camera smoothing speed (0.1 = responsive, higher = smoother).
        /// </summary>
        public const float DefaultCameraSmoothingSpeed = 0.1f;

        /// <summary>
        /// Default camera zoom level (1.0 = normal).
        /// </summary>
        public const float DefaultCameraZoom = 1.0f;

        /// <summary>
        /// Default camera rotation in radians (0 = no rotation).
        /// </summary>
        public const float DefaultCameraRotation = 0.0f;

        /// <summary>
        /// Default player sprite sheet ID.
        /// </summary>
        public const string DefaultPlayerSpriteSheetId = "base:sprite:players/may/normal";

        /// <summary>
        /// Default player initial animation name.
        /// </summary>
        public const string DefaultPlayerInitialAnimation = "face_south";

        /// <summary>
        /// Default player movement speed in tiles per second.
        /// Matches MonoBall's default: 4.0 tiles per second.
        /// </summary>
        public const float DefaultPlayerMovementSpeed = 4.0f;

        /// <summary>
        /// Input buffer timeout in seconds.
        /// Inputs expire after this time (default: 200ms).
        /// Matches MonoBall's default: 0.2f.
        /// </summary>
        public const float InputBufferTimeoutSeconds = 0.2f;

        /// <summary>
        /// Maximum number of inputs in buffer.
        /// Prevents buffer overflow (default: 5).
        /// Matches MonoBall's default: 5.
        /// </summary>
        public const int InputBufferMaxSize = 5;

        // === Map Popup Constants (GBA-accurate pokeemerald dimensions) ===

        /// <summary>
        /// Map popup background width in pixels at 1x GBA scale (pokeemerald accurate).
        /// </summary>
        public const int PopupBackgroundWidth = 80;

        /// <summary>
        /// Map popup background height in pixels at 1x GBA scale (pokeemerald accurate).
        /// </summary>
        public const int PopupBackgroundHeight = 24;

        /// <summary>
        /// Map popup base font size at 1x GBA scale (pokeemerald accurate).
        /// </summary>
        public const int PopupBaseFontSize = 12;

        /// <summary>
        /// Map popup text Y offset from window top at 1x GBA scale (pokeemerald accurate).
        /// </summary>
        public const int PopupTextOffsetY = 3;

        /// <summary>
        /// Map popup text padding from edges at 1x GBA scale (pokeemerald accurate).
        /// </summary>
        public const int PopupTextPadding = 4;

        /// <summary>
        /// Map popup text shadow X offset at 1x GBA scale (pokeemerald accurate).
        /// </summary>
        public const int PopupShadowOffsetX = 1;

        /// <summary>
        /// Map popup text shadow Y offset at 1x GBA scale (pokeemerald accurate).
        /// </summary>
        public const int PopupShadowOffsetY = 1;

        /// <summary>
        /// Map popup interior tiles width (background width / tile size = 80 / 8).
        /// </summary>
        public const int PopupInteriorTilesX = 10;

        /// <summary>
        /// Map popup interior tiles height (background height / tile size = 24 / 8).
        /// </summary>
        public const int PopupInteriorTilesY = 3;

        /// <summary>
        /// Map popup screen padding from edges at 1x GBA scale (pokeemerald accurate: 0).
        /// </summary>
        public const int PopupScreenPadding = 0;
    }
}
