namespace MonoBall.Core.ECS.Components
{
    /// <summary>
    /// Component that attaches a script to an entity.
    /// Multiple ScriptAttachmentComponent instances can exist on the same entity for composition.
    /// Pure value type - script instances stored in ScriptLifecycleSystem.
    /// </summary>
    public struct ScriptAttachmentComponent
    {
        /// <summary>
        /// The script definition ID (e.g., "base:script:behavior/stationary").
        /// References a ScriptDefinition in the DefinitionRegistry.
        /// </summary>
        public string ScriptDefinitionId { get; set; }

        /// <summary>
        /// Execution priority (higher = executes first).
        /// Defaults to priority from ScriptDefinition if not set.
        /// </summary>
        public int Priority { get; set; }

        /// <summary>
        /// Whether this script is active. Inactive scripts are skipped.
        /// </summary>
        public bool IsActive { get; set; }

        /// <summary>
        /// The mod ID that owns this script. Used for script resolution.
        /// </summary>
        public string ModId { get; set; }

        /// <summary>
        /// Internal: Whether the script has been initialized.
        /// Used by ScriptLifecycleSystem to track initialization state.
        /// </summary>
        internal bool IsInitialized { get; set; }
    }
}
