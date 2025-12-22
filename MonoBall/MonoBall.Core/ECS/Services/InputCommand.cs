using MonoBall.Core.ECS.Components;

namespace MonoBall.Core.ECS.Services
{
    /// <summary>
    /// Represents an input command with direction and timestamp for input buffering.
    /// Used by InputBuffer service to queue input commands.
    /// </summary>
    public struct InputCommand
    {
        /// <summary>
        /// Gets or sets the direction of the input command.
        /// </summary>
        public Direction Direction { get; set; }

        /// <summary>
        /// Gets or sets the timestamp when this command was added to the buffer.
        /// </summary>
        public float Timestamp { get; set; }

        /// <summary>
        /// Initializes a new instance of the InputCommand struct.
        /// </summary>
        /// <param name="direction">The direction of the input command.</param>
        /// <param name="timestamp">The timestamp when the command was added.</param>
        public InputCommand(Direction direction, float timestamp)
        {
            Direction = direction;
            Timestamp = timestamp;
        }
    }
}
