using System.Collections.Generic;
using MonoBall.Core.ECS.Input;

namespace MonoBall.Core.ECS.Components
{
    /// <summary>
    /// Component tracking input state and buffering for responsive controls.
    /// Matches MonoBall's InputState component structure with architecture improvements.
    /// </summary>
    public struct InputState
    {
        /// <summary>
        /// Gets or sets the currently pressed direction.
        /// Matches MonoBall behavior.
        /// </summary>
        public Direction PressedDirection { get; set; }

        /// <summary>
        /// Gets or sets whether the action button is pressed.
        /// Matches MonoBall behavior.
        /// </summary>
        public bool ActionPressed { get; set; }

        /// <summary>
        /// Gets or sets the remaining time for input buffering in seconds.
        /// Matches MonoBall behavior.
        /// </summary>
        public float InputBufferTime { get; set; }

        /// <summary>
        /// Gets or sets whether input is currently enabled.
        /// Matches MonoBall behavior.
        /// </summary>
        public bool InputEnabled { get; set; }

        /// <summary>
        /// Gets or sets the currently pressed input actions (architecture improvement).
        /// Uses named input actions for better extensibility and customizability.
        /// MonoBall doesn't have this - uses direct key mapping.
        /// </summary>
        public HashSet<InputAction> PressedActions { get; set; }

        /// <summary>
        /// Gets or sets actions that were just pressed this frame (architecture improvement).
        /// Useful for detecting single-frame input events.
        /// </summary>
        public HashSet<InputAction> JustPressedActions { get; set; }

        /// <summary>
        /// Gets or sets actions that were just released this frame (architecture improvement).
        /// Useful for detecting single-frame input events.
        /// </summary>
        public HashSet<InputAction> JustReleasedActions { get; set; }

        /// <summary>
        /// Initializes a new instance of the InputState struct with default values.
        /// </summary>
        /// <remarks>
        /// HashSets must be initialized when creating the component to avoid NullReferenceException.
        /// </remarks>
        public InputState()
        {
            PressedDirection = Direction.None;
            ActionPressed = false;
            InputBufferTime = 0f;
            InputEnabled = true;
            PressedActions = new HashSet<InputAction>();
            JustPressedActions = new HashSet<InputAction>();
            JustReleasedActions = new HashSet<InputAction>();
        }
    }
}
