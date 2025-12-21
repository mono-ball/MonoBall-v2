using Arch.Core;

namespace MonoBall.Core.ECS
{
    /// <summary>
    /// Interface for ECS world service.
    /// Provides access to the ECS world instance.
    /// </summary>
    public interface IEcsService
    {
        /// <summary>
        /// Gets the ECS world instance.
        /// </summary>
        World World { get; }
    }
}
