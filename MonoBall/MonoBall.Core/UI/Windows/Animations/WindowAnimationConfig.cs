using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace MonoBall.Core.UI.Windows.Animations
{
    /// <summary>
    /// Configuration for window animations.
    /// Defines animation sequence, timing, and parameters.
    /// </summary>
    public struct WindowAnimationConfig
    {
        /// <summary>
        /// Gets or sets the animation sequence (list of animation phases).
        /// Must be initialized (non-null) when component is created.
        /// </summary>
        public List<WindowAnimationPhase> Phases { get; set; }

        /// <summary>
        /// Gets or sets the window dimensions (used for position calculations).
        /// </summary>
        public Vector2 WindowSize { get; set; }

        /// <summary>
        /// Gets or sets the initial window position (before animation).
        /// </summary>
        public Vector2 InitialPosition { get; set; }

        /// <summary>
        /// Gets or sets whether the animation loops.
        /// </summary>
        public bool Loop { get; set; }

        /// <summary>
        /// Gets or sets whether to destroy the window entity when animation completes.
        /// </summary>
        public bool DestroyOnComplete { get; set; }
    }
}
