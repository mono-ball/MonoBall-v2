namespace MonoBall.Core.ECS
{
    /// <summary>
    /// Defines system execution priorities.
    /// Lower numbers execute first.
    ///
    /// Priority Organization:
    /// - 0-20: World/Map creation and management (must run first)
    /// - 25-35: Entity initialization (scripts, player)
    /// - 40-50: Input processing
    /// - 100-200: Entity processing (movement, camera, animation, visibility)
    /// - 300-420: Scene management and UI
    /// - 500-510: Shader effects
    /// - 600: Audio systems
    /// - 700+: Cleanup and management (runs last to avoid race conditions)
    /// </summary>
    public static class SystemPriority
    {
        // ============================================
        // Phase 1: World/Map Creation (0-20)
        // ============================================
        // These systems create entities and must run first

        /// <summary>
        /// Map loader system - creates map entities, chunks, NPCs, connections.
        /// Must run first to create world entities.
        /// </summary>
        public const int MapLoader = 0;

        /// <summary>
        /// Map connection system - processes map connections.
        /// Runs after MapLoader to process connections between maps.
        /// </summary>
        public const int MapConnection = 10;

        // ============================================
        // Phase 2: Entity Initialization (25-35)
        // ============================================
        // These systems initialize entities after they're created

        /// <summary>
        /// Script lifecycle system - initializes and manages script components.
        /// Runs after map management to initialize scripts on entities.
        /// </summary>
        public const int ScriptLifecycle = 25;

        /// <summary>
        /// Script timer system - processes script timers.
        /// Runs after ScriptLifecycle to process timers on initialized scripts.
        /// </summary>
        public const int ScriptTimer = 26;

        /// <summary>
        /// Player system - creates and manages player entity.
        /// Runs after scripts to ensure player scripts are initialized.
        /// </summary>
        public const int Player = 30;

        // ============================================
        // Phase 3: Input Processing (40-50)
        // ============================================
        // These systems process user input

        /// <summary>
        /// Input system - processes keyboard/gamepad input and creates movement requests.
        /// Runs early to capture input before movement processing.
        /// </summary>
        public const int Input = 40;

        /// <summary>
        /// Interaction system - handles player interactions with NPCs and objects.
        /// Runs after Input to process interaction button presses.
        /// </summary>
        public const int Interaction = 50;

        // ============================================
        // Phase 4: Entity Processing (100-200)
        // ============================================
        // These systems process entities after they're fully initialized
        // Movement runs later to avoid race conditions with entity creation

        /// <summary>
        /// Movement system - processes movement requests and updates movement interpolation.
        /// Runs after entities are fully initialized to avoid race conditions.
        /// Processes entities with ActiveMapEntity tag for performance.
        /// </summary>
        public const int Movement = 100;

        /// <summary>
        /// Map transition detection - detects when player crosses map boundaries.
        /// Runs after Movement to detect transitions after movement completes.
        /// </summary>
        public const int MapTransitionDetection = 110;

        /// <summary>
        /// Camera system - updates camera position based on player/movement.
        /// Runs after Movement to follow entities after they've moved.
        /// </summary>
        public const int Camera = 120;

        /// <summary>
        /// Camera viewport system - updates camera viewport.
        /// Runs after Camera to update viewport after camera position changes.
        /// </summary>
        public const int CameraViewport = 125;

        /// <summary>
        /// Animated tile system - updates animated tile animations.
        /// Runs after movement/camera to update tile animations.
        /// </summary>
        public const int AnimatedTile = 130;

        /// <summary>
        /// Sprite animation system - updates sprite animations.
        /// Runs after Movement (which handles animation state changes) to update animations.
        /// </summary>
        public const int SpriteAnimation = 140;

        /// <summary>
        /// Sprite sheet system - manages sprite sheet components.
        /// Runs after SpriteAnimation to update sprite sheets.
        /// </summary>
        public const int SpriteSheet = 150;

        /// <summary>
        /// Window animation system - updates window animations.
        /// Runs after sprite animations to update UI window animations.
        /// </summary>
        public const int WindowAnimation = 155;

        /// <summary>
        /// Visibility flag system - updates entity visibility based on flags.
        /// Runs after entities are initialized to update visibility flags.
        /// </summary>
        public const int VisibilityFlag = 160;

        /// <summary>
        /// Performance stats system - tracks performance metrics.
        /// Runs after other systems to measure their performance.
        /// </summary>
        public const int PerformanceStats = 200;

        // ============================================
        // Phase 5: Scene Management and UI (300-420)
        // ============================================

        /// <summary>
        /// Scene system - manages scene lifecycle.
        /// Runs after entity processing to coordinate scene updates.
        /// </summary>
        public const int Scene = 300;

        /// <summary>
        /// Scene input system - processes scene-specific input.
        /// Runs after Scene to handle scene input.
        /// </summary>
        public const int SceneInput = 310;

        /// <summary>
        /// Game scene system - manages game scene rendering.
        /// Runs after Scene to render game scene.
        /// </summary>
        public const int GameScene = 320;

        /// <summary>
        /// Loading scene system - manages loading scene.
        /// Runs after Scene to handle loading scene.
        /// </summary>
        public const int LoadingScene = 330;

        /// <summary>
        /// Debug bar scene system - manages debug bar scene.
        /// Runs after Scene to render debug bar.
        /// </summary>
        public const int DebugBarScene = 340;

        /// <summary>
        /// Map popup scene system - manages map popup scenes.
        /// Runs after Scene to render map popups.
        /// </summary>
        public const int MapPopupScene = 350;

        /// <summary>
        /// Message box scene system - manages message box scenes.
        /// Runs after Scene to render message boxes.
        /// </summary>
        public const int MessageBoxScene = 360;

        /// <summary>
        /// Map popup orchestrator - coordinates map popup lifecycle.
        /// Runs after scene systems to coordinate popups.
        /// </summary>
        public const int MapPopupOrchestrator = 400;

        /// <summary>
        /// Map popup system - deprecated, use MapPopupScene instead.
        /// </summary>
        public const int MapPopup = 410; // Deprecated - use MapPopupScene instead

        /// <summary>
        /// Debug bar toggle system - handles debug bar toggle input.
        /// Runs after UI systems to handle debug bar toggling.
        /// </summary>
        public const int DebugBarToggle = 420;

        // ============================================
        // Phase 6: Shader Effects (500-510)
        // ============================================

        /// <summary>
        /// Shader parameter animation system - animates shader parameters.
        /// Runs after rendering systems to animate shader effects.
        /// </summary>
        public const int ShaderParameterAnimation = 500;

        /// <summary>
        /// Shader cycle system - cycles through shader effects.
        /// Runs after ShaderParameterAnimation to cycle shaders.
        /// </summary>
        public const int ShaderCycle = 510;

        // ============================================
        // Phase 7: Audio Systems (600)
        // ============================================

        /// <summary>
        /// Audio systems - processes audio playback.
        /// Runs after other systems to play audio based on game state.
        /// </summary>
        public const int Audio = 600;

        // ============================================
        // Phase 8: Cleanup and Management (700+)
        // ============================================
        // These systems run last to avoid race conditions with entity creation/modification

        /// <summary>
        /// Active map management system - manages ActiveMapEntity tag component lifecycle.
        /// Runs last to avoid race conditions with entity creation/modification.
        /// Processes entities after they've been fully initialized by other systems.
        /// </summary>
        public const int ActiveMapManagement = 700;
    }
}
