namespace MonoBall.Core.Scenes
{
    /// <summary>
    /// Constants for scene priority values.
    /// Higher priority values render on top (rendered last in reverse iteration).
    /// </summary>
    public static class ScenePriorities
    {
        /// <summary>
        /// Priority for debug/overlay scenes that should appear on top of everything.
        /// </summary>
        public const int DebugOverlay = 100;

        /// <summary>
        /// Priority for the main game scene.
        /// </summary>
        public const int GameScene = 50;

        /// <summary>
        /// Priority for background scenes.
        /// </summary>
        public const int Background = 0;
    }
}
