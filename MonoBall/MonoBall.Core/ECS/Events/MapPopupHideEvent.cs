using Arch.Core;

namespace MonoBall.Core.ECS.Events
{
    /// <summary>
    /// Event fired when a popup animation completes and the popup should be hidden.
    /// </summary>
    public struct MapPopupHideEvent
    {
        /// <summary>
        /// Gets or sets the popup entity that finished its animation.
        /// </summary>
        public Entity PopupEntity { get; set; }
    }
}
