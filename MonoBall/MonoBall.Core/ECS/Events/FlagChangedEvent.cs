using Arch.Core;

namespace MonoBall.Core.ECS.Events
{
    /// <summary>
    /// Event fired when a flag value changes.
    /// </summary>
    public struct FlagChangedEvent
    {
        /// <summary>
        /// The flag identifier that changed.
        /// </summary>
        public string FlagId { get; set; }

        /// <summary>
        /// The entity this flag belongs to. Null if this is a global flag.
        /// </summary>
        public Entity? Entity { get; set; }

        /// <summary>
        /// The previous value of the flag.
        /// </summary>
        public bool OldValue { get; set; }

        /// <summary>
        /// The new value of the flag.
        /// </summary>
        public bool NewValue { get; set; }
    }
}
