using Arch.Core;

namespace MonoBall.Core.ECS
{
    /// <summary>
    /// Service wrapper for ECS world integration with MonoGame's Game.Services.
    /// </summary>
    public class EcsService : IEcsService
    {
        /// <summary>
        /// Gets the ECS world instance.
        /// </summary>
        public World World => EcsWorld.Instance;
    }
}
