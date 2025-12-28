using Arch.Core;

namespace MonoBall.Core.ECS.Events
{
    /// <summary>
    /// Event fired when an entire shader animation chain completes (non-looping chains only).
    /// Subscribe via EventBus.Subscribe&lt;ShaderAnimationChainCompletedEvent&gt;(handler).
    /// </summary>
    public struct ShaderAnimationChainCompletedEvent
    {
        /// <summary>
        /// The entity whose animation chain completed.
        /// </summary>
        public Entity Entity { get; set; }

        /// <summary>
        /// The shader ID that was being animated.
        /// </summary>
        public string ShaderId { get; set; }

        /// <summary>
        /// The total number of phases that were executed.
        /// </summary>
        public int TotalPhasesExecuted { get; set; }

        /// <summary>
        /// Whether the chain was set to loop (if true, this event indicates
        /// the chain was manually stopped rather than completing naturally).
        /// </summary>
        public bool WasLooping { get; set; }
    }
}
