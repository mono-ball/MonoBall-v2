using System.Collections.Generic;

namespace MonoBall.Core.ECS.Components
{
    /// <summary>
    /// Component that stores multiple timers for scripts.
    /// Scripts can create multiple timers that fire TimerElapsedEvent when they expire.
    /// Each timer is identified by a unique TimerId.
    /// </summary>
    public struct ScriptTimersComponent
    {
        /// <summary>
        /// Dictionary of timers keyed by timer ID.
        /// </summary>
        public Dictionary<string, ScriptTimerData> Timers { get; set; }

        /// <summary>
        /// Initializes a new instance of the ScriptTimersComponent struct.
        /// </summary>
        public ScriptTimersComponent()
        {
            Timers = new Dictionary<string, ScriptTimerData>();
        }
    }

    /// <summary>
    /// Data for a single script timer.
    /// </summary>
    public struct ScriptTimerData
    {
        /// <summary>
        /// Total duration of the timer in seconds.
        /// </summary>
        public float Duration { get; set; }

        /// <summary>
        /// Elapsed time in seconds (incremented each frame).
        /// </summary>
        public float ElapsedTime { get; set; }

        /// <summary>
        /// Whether this timer is active and should be processed.
        /// Inactive timers are ignored and can be removed.
        /// </summary>
        public bool IsActive { get; set; }

        /// <summary>
        /// Whether this timer should repeat (restart after expiring).
        /// </summary>
        public bool IsRepeating { get; set; }

        /// <summary>
        /// Initializes a new instance of the ScriptTimerData struct.
        /// </summary>
        /// <param name="duration">Duration in seconds.</param>
        /// <param name="isRepeating">Whether the timer should repeat.</param>
        public ScriptTimerData(float duration, bool isRepeating = false)
        {
            Duration = duration;
            ElapsedTime = 0f;
            IsActive = true;
            IsRepeating = isRepeating;
        }
    }
}
