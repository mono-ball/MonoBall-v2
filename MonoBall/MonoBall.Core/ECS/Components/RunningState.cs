namespace MonoBall.Core.ECS.Components
{
    /// <summary>
    /// Player running states matching Pokemon Emerald's behavior.
    /// Used by GridMovement component to track movement state.
    /// </summary>
    public enum RunningState
    {
        /// <summary>
        /// Player is not moving and no input detected.
        /// </summary>
        NotMoving = 0,

        /// <summary>
        /// Player is turning in place to face a new direction.
        /// This happens when input direction differs from facing direction.
        /// Movement won't start until the turn completes and input is still held.
        /// </summary>
        TurnDirection = 1,

        /// <summary>
        /// Player is actively moving between tiles.
        /// </summary>
        Moving = 2,
    }
}
