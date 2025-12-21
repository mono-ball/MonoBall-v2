using Arch.Core;

namespace MonoBall.Core.ECS.Events
{
    /// <summary>
    /// Event fired when an NPC's animation changes.
    /// </summary>
    public struct NpcAnimationChangedEvent
    {
        /// <summary>
        /// The entity reference for the NPC.
        /// </summary>
        public Entity NpcEntity { get; set; }

        /// <summary>
        /// The NPC definition ID.
        /// </summary>
        public string NpcId { get; set; }

        /// <summary>
        /// The name of the previous animation.
        /// </summary>
        public string OldAnimationName { get; set; }

        /// <summary>
        /// The name of the new animation.
        /// </summary>
        public string NewAnimationName { get; set; }
    }
}
