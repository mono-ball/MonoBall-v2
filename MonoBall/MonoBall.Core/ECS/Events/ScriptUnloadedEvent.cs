using System;
using Arch.Core;

namespace MonoBall.Core.ECS.Events
{
    /// <summary>
    /// Event fired when a script is unloaded.
    /// </summary>
    public struct ScriptUnloadedEvent
    {
        /// <summary>
        /// The entity the script was attached to.
        /// </summary>
        public Entity Entity { get; set; }

        /// <summary>
        /// The script definition ID.
        /// </summary>
        public string ScriptDefinitionId { get; set; }

        /// <summary>
        /// Timestamp when the script was unloaded.
        /// </summary>
        public DateTime UnloadedAt { get; set; }
    }
}
