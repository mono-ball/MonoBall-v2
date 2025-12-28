namespace MonoBall.Core.ECS.Components
{
    /// <summary>
    /// Component for animating multiple shader parameters simultaneously.
    /// Animations are stored externally in ShaderMultiParameterAnimationSystem
    /// to avoid List&lt;T&gt; allocations in ECS components (per Arch ECS best practices).
    /// </summary>
    public struct ShaderMultiParameterAnimationComponent
    {
        /// <summary>
        /// Whether the multi-parameter animation is enabled.
        /// </summary>
        public bool IsEnabled { get; set; }

        /// <summary>
        /// Optional group identifier for filtering animations.
        /// </summary>
        public string? GroupId { get; set; }

        /// <summary>
        /// Creates a new multi-parameter animation component.
        /// </summary>
        public static ShaderMultiParameterAnimationComponent Create(string? groupId = null)
        {
            return new ShaderMultiParameterAnimationComponent
            {
                IsEnabled = true,
                GroupId = groupId,
            };
        }
    }
}
