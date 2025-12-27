namespace MonoBall.Core.ECS.Events
{
    /// <summary>
    /// Event fired when a message box should be hidden.
    /// </summary>
    public struct MessageBoxHideEvent
    {
        /// <summary>
        /// Window ID of the message box to hide (0 = hide all).
        /// </summary>
        public int WindowId { get; set; }
    }
}
