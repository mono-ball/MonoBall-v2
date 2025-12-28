namespace MonoBall.Core.ECS.Components
{
    /// <summary>
    /// Component for sequenced shader animation chains.
    /// Phases and animations are stored externally in ShaderAnimationChainSystem
    /// to avoid List&lt;T&gt; allocations in ECS components (per Arch ECS best practices).
    /// </summary>
    public struct ShaderAnimationChainComponent
    {
        /// <summary>
        /// Index of the currently executing phase (0-based).
        /// </summary>
        public int CurrentPhaseIndex { get; set; }

        /// <summary>
        /// Elapsed time within the current phase (including delay).
        /// </summary>
        public float PhaseElapsedTime { get; set; }

        /// <summary>
        /// Current state of the animation chain.
        /// </summary>
        public ShaderAnimationChainState State { get; set; }

        /// <summary>
        /// Whether to loop the chain from the beginning when complete.
        /// </summary>
        public bool IsLooping { get; set; }

        /// <summary>
        /// Whether the animation chain is enabled.
        /// </summary>
        public bool IsEnabled { get; set; }

        /// <summary>
        /// Creates a new animation chain component.
        /// </summary>
        public static ShaderAnimationChainComponent Create(bool isLooping = false)
        {
            return new ShaderAnimationChainComponent
            {
                CurrentPhaseIndex = 0,
                PhaseElapsedTime = 0f,
                State = ShaderAnimationChainState.NotStarted,
                IsLooping = isLooping,
                IsEnabled = true,
            };
        }
    }

    /// <summary>
    /// States for shader animation chain.
    /// </summary>
    public enum ShaderAnimationChainState
    {
        /// <summary>
        /// Chain has not yet started.
        /// </summary>
        NotStarted,

        /// <summary>
        /// Chain is currently playing.
        /// </summary>
        Playing,

        /// <summary>
        /// Chain is paused.
        /// </summary>
        Paused,

        /// <summary>
        /// Chain completed all phases.
        /// </summary>
        Completed,

        /// <summary>
        /// Chain was stopped/cancelled.
        /// </summary>
        Stopped,
    }
}
