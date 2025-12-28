using Arch.Core;
using MonoBall.Core.ECS.Components;

namespace MonoBall.Core.ECS.Events
{
    /// <summary>
    /// Event fired when a player enters a shader region on a map.
    /// Subscribe via EventBus.Subscribe&lt;ShaderRegionEnteredEvent&gt;(handler).
    /// </summary>
    public struct ShaderRegionEnteredEvent
    {
        /// <summary>
        /// The region entity that was entered.
        /// </summary>
        public Entity RegionEntity { get; set; }

        /// <summary>
        /// The player entity that entered the region.
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
        /// The shader ID being applied.
        /// </summary>
        public string? ShaderId { get; set; }

        /// <summary>
        /// The layer the shader is applied to.
        /// </summary>
        public ShaderLayer Layer { get; set; }
    }
}
