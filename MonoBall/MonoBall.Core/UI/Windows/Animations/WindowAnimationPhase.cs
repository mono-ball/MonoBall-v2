using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace MonoBall.Core.UI.Windows.Animations
{
    /// <summary>
    /// Represents a single phase in a window animation sequence.
    /// </summary>
    public struct WindowAnimationPhase
    {
        /// <summary>
        /// Gets or sets the animation type for this phase.
        /// </summary>
        public WindowAnimationType Type { get; set; }

        /// <summary>
        /// Gets or sets the duration of this phase in seconds.
        /// </summary>
        public float Duration { get; set; }

        /// <summary>
        /// Gets or sets the easing function for this phase.
        /// </summary>
        public WindowEasingType Easing { get; set; }

        /// <summary>
        /// Gets or sets the start value for this phase.
        /// Field usage depends on animation type:
        /// - Slide: X, Y = position offset, Z = unused
        /// - Fade: Z = opacity (0.0-1.0), X, Y = unused
        /// - Scale: Z = scale factor (1.0 = normal), X, Y = unused
        /// - SlideFade: X, Y = position offset, Z = opacity (0.0-1.0)
        /// - SlideScale: X, Y = position offset, Z = scale factor
        /// - Pause: X, Y, Z = unused
        /// </summary>
        public Vector3 StartValue { get; set; }

        /// <summary>
        /// Gets or sets the end value for this phase.
        /// Field usage matches StartValue based on animation type.
        /// </summary>
        public Vector3 EndValue { get; set; }

        /// <summary>
        /// Gets or sets optional parameters specific to animation type.
        /// </summary>
        public Dictionary<string, object>? Parameters { get; set; }
    }
}
