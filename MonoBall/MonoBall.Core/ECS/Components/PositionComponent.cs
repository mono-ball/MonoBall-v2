using Microsoft.Xna.Framework;

namespace MonoBall.Core.ECS.Components
{
    /// <summary>
    /// Component that stores world position for entities.
    /// </summary>
    public struct PositionComponent
    {
        /// <summary>
        /// The world position in pixels.
        /// </summary>
        public Vector2 Position { get; set; }
    }
}
