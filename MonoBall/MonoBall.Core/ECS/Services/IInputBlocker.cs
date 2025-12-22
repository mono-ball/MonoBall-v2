namespace MonoBall.Core.ECS.Services
{
    /// <summary>
    /// Interface for blocking input processing.
    /// Used by InputSystem to check if input should be processed.
    /// </summary>
    public interface IInputBlocker
    {
        /// <summary>
        /// Gets whether input is currently blocked.
        /// </summary>
        bool IsInputBlocked { get; }
    }
}
