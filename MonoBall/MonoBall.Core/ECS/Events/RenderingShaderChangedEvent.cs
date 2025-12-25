using Arch.Core;
using MonoBall.Core.ECS.Components;

namespace MonoBall.Core.ECS.Events
{
    /// <summary>
    /// Event fired when a rendering shader is enabled, disabled, or changed.
    /// </summary>
    public struct RenderingShaderChangedEvent
    {
        /// <summary>
        /// The layer affected by the shader change.
        /// </summary>
        public ShaderLayer Layer { get; set; }

        /// <summary>
        /// The previous shader ID (null if no previous shader).
        /// </summary>
        public string? PreviousShaderId { get; set; }

        /// <summary>
        /// The new shader ID (null if shader was disabled).
        /// </summary>
        public string? NewShaderId { get; set; }

        /// <summary>
        /// The entity that owns the RenderingShaderComponent.
        /// </summary>
        public Entity ShaderEntity { get; set; }
    }
}
