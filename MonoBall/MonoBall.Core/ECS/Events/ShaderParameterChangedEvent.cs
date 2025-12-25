using Arch.Core;
using MonoBall.Core.ECS.Components;

namespace MonoBall.Core.ECS.Events
{
    /// <summary>
    /// Event fired when shader parameters are updated.
    /// </summary>
    public struct ShaderParameterChangedEvent
    {
        /// <summary>
        /// The layer affected by the parameter change.
        /// </summary>
        public ShaderLayer Layer { get; set; }

        /// <summary>
        /// The shader ID whose parameter changed.
        /// </summary>
        public string ShaderId { get; set; }

        /// <summary>
        /// The name of the parameter that changed.
        /// </summary>
        public string ParameterName { get; set; }

        /// <summary>
        /// The old parameter value (null if parameter was just added).
        /// </summary>
        public object? OldValue { get; set; }

        /// <summary>
        /// The new parameter value.
        /// </summary>
        public object? NewValue { get; set; }

        /// <summary>
        /// The entity that owns the RenderingShaderComponent.
        /// </summary>
        public Entity ShaderEntity { get; set; }
    }
}
