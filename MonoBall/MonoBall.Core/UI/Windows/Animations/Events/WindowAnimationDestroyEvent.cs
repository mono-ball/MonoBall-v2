using Arch.Core;

namespace MonoBall.Core.UI.Windows.Animations.Events
{
    /// <summary>
    /// Event fired when a window should be destroyed (after animation completes).
    /// </summary>
    public struct WindowAnimationDestroyEvent
    {
        /// <summary>
        /// Gets or sets the animation entity.
        /// </summary>
        public Entity Entity { get; set; }

        /// <summary>
        /// Gets or sets the window entity to destroy.
        /// </summary>
        public Entity WindowEntity { get; set; }
    }
}
