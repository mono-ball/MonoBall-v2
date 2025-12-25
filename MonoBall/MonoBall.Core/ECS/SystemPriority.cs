namespace MonoBall.Core.ECS
{
    /// <summary>
    /// Defines system execution priorities.
    /// Lower numbers execute first.
    /// </summary>
    public static class SystemPriority
    {
        // Map and world management (must run first)
        public const int MapLoader = 0;
        public const int MapConnection = 10;
        public const int ActiveMapManagement = 20;

        // Player initialization (runs once, no per-frame updates)
        public const int Player = 30;

        // Input processing
        public const int Input = 40;

        // Movement and physics
        public const int Movement = 50;
        public const int MapTransitionDetection = 60;

        // Camera (runs after movement)
        public const int Camera = 70;
        public const int CameraViewport = 75;

        // Animation
        public const int AnimatedTile = 100;
        public const int SpriteAnimation = 110;
        public const int SpriteSheet = 120;

        // Visibility and flags
        public const int VisibilityFlag = 130;

        // Performance tracking
        public const int PerformanceStats = 200;

        // Audio systems
        public const int AudioEvent = 210;
        public const int AudioUpdate = 215;
        public const int Audio = 220;
        public const int MusicZone = 230;
        public const int AmbientSound = 240;

        // Scene management
        public const int Scene = 300;
        public const int SceneInput = 310;
        public const int GameScene = 320;
        public const int LoadingScene = 330;
        public const int DebugBarScene = 340;
        public const int MapPopupScene = 350;

        // Popups and UI
        public const int MapMusicOrchestrator = 390; // Run before popup orchestrator
        public const int MapPopupOrchestrator = 400;
        public const int MapPopup = 410; // Deprecated - use MapPopupScene instead
        public const int DebugBarToggle = 420;

        // Shader effects
        public const int ShaderParameterAnimation = 500;
        public const int ShaderCycle = 510;
        public const int PlayerShaderCycle = 520;
    }
}
