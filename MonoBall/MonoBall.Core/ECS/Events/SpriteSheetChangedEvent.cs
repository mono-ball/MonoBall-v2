using Arch.Core;

namespace MonoBall.Core.ECS.Events
{
    /// <summary>
    /// Event fired when an entity's sprite sheet has been successfully changed.
    /// </summary>
    public struct SpriteSheetChangedEvent
    {
        /// <summary>
        /// The entity that changed sprite sheet.
        /// </summary>
        public Entity Entity { get; set; }

        /// <summary>
        /// The previous sprite sheet ID.
        /// </summary>
        public string OldSpriteSheetId { get; set; }

        /// <summary>
        /// The new sprite sheet ID.
        /// </summary>
        public string NewSpriteSheetId { get; set; }
    }
}
