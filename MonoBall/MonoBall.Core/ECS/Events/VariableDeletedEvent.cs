using Arch.Core;

namespace MonoBall.Core.ECS.Events
{
    /// <summary>
    /// Event fired when a variable is deleted.
    /// </summary>
    public struct VariableDeletedEvent
    {
        /// <summary>
        /// The variable key that was deleted.
        /// </summary>
        public string Key { get; set; }

        /// <summary>
        /// The entity this variable belonged to. Null if this was a global variable.
        /// </summary>
        public Entity? Entity { get; set; }

        /// <summary>
        /// The previous value as a serialized string representation.
        /// </summary>
        public string? OldValue { get; set; }

        /// <summary>
        /// The type name of the deleted value (for deserialization).
        /// </summary>
        public string? OldType { get; set; }
    }
}
