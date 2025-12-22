namespace MonoBall.Core
{
    /// <summary>
    /// Centralized game constants to avoid magic numbers throughout the codebase.
    /// </summary>
    public static class GameConstants
    {
        /// <summary>
        /// Default tile width in pixels (standard for tile-based games).
        /// </summary>
        public const int DefaultTileWidth = 16;

        /// <summary>
        /// Default tile height in pixels (standard for tile-based games).
        /// </summary>
        public const int DefaultTileHeight = 16;

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
        /// Tile size in pixels (matches DefaultTileWidth/Height).
        /// Used for tile-based movement calculations.
        /// </summary>
        public const int TileSize = 16;

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
    }
}
