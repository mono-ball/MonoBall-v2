using Arch.Core;

namespace MonoBall.Core.ECS.Events
{
    /// <summary>
    /// Event fired when a script timer expires.
    /// Scripts subscribe to this event to know when their timers complete.
    /// </summary>
    public struct TimerElapsedEvent
    {
        /// <summary>
        /// The entity that owns the timer.
        /// </summary>
        public Entity Entity { get; set; }

        /// <summary>
        /// The timer ID that expired (as specified when creating the timer).
        /// </summary>
        public string TimerId { get; set; }

        /// <summary>
        /// Whether this timer will repeat (restart automatically).
        /// </summary>
        public bool IsRepeating { get; set; }
    }
}
