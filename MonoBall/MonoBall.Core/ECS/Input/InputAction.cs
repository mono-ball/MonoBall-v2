namespace MonoBall.Core.ECS.Input
{
    /// <summary>
    /// Named input actions for abstracting keyboard/gamepad input.
    /// Architecture improvement over direct key mapping for better customizability.
    /// </summary>
    public enum InputAction
    {
        /// <summary>
        /// Move north (up).
        /// </summary>
        MoveNorth,

        /// <summary>
        /// Move south (down).
        /// </summary>
        MoveSouth,

        /// <summary>
        /// Move east (right).
        /// </summary>
        MoveEast,

        /// <summary>
        /// Move west (left).
        /// </summary>
        MoveWest,

        /// <summary>
        /// Interaction/Action button.
        /// </summary>
        Interact,

        /// <summary>
        /// Pause menu.
        /// </summary>
        Pause,

        /// <summary>
        /// Menu (if different from Pause).
        /// </summary>
        Menu,
    }
}
