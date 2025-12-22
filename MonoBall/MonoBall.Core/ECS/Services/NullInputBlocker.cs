namespace MonoBall.Core.ECS.Services
{
    /// <summary>
    /// Placeholder implementation of IInputBlocker that never blocks input.
    /// Used as a default implementation until proper input blocking is implemented
    /// (e.g., scene system can implement IInputBlocker to block input during menus).
    /// </summary>
    public class NullInputBlocker : IInputBlocker
    {
        /// <summary>
        /// Gets whether input is currently blocked (always returns false).
        /// </summary>
        public bool IsInputBlocked => false;
    }
}
