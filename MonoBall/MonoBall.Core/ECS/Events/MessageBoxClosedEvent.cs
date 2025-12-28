using Arch.Core;

namespace MonoBall.Core.ECS.Events
{
    /// <summary>
    /// Event fired when a message box closes.
    /// Used by InteractionSystem to resume behavior scripts after interactions complete.
    /// This event is fired when the message box scene is destroyed.
    /// </summary>
    public struct MessageBoxClosedEvent
    {
        /// <summary>
        /// Gets or sets the message box entity that was closed.
        /// </summary>
        public Entity MessageBoxEntity { get; set; }
    }
}
