using Arch.Core;
using MonoBall.Core.ECS.Components;

namespace MonoBall.Core.ECS.Events
{
    /// <summary>
    /// Event fired when a player exits a shader region on a map.
    /// Subscribe via EventBus.Subscribe&lt;ShaderRegionExitedEvent&gt;(handler).
    /// </summary>
    public struct ShaderRegionExitedEvent
    {
        /// <summary>
        /// The region entity that was exited.
        /// </summary>
        public Entity RegionEntity { get; set; }

        /// <summary>
        /// The player entity that exited the region.
        /// </summary>
        public Entity PlayerEntity { get; set; }

        /// <summary>
        /// The map ID the region is on.
        /// </summary>
        public string MapId { get; set; }

        /// <summary>
        /// The region identifier.
        /// </summary>
        public string RegionId { get; set; }

        /// <summary>
        /// The shader ID that was being applied (now reverting).
        /// </summary>
        public string? ShaderId { get; set; }

        /// <summary>
        /// The layer the shader was applied to.
        /// </summary>
        public ShaderLayer Layer { get; set; }
    }
}
