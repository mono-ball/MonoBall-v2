namespace MonoBall.Core.Mods
{
    /// <summary>
    /// Defines how a definition should be processed when it conflicts with an existing definition.
    /// </summary>
    public enum DefinitionOperation
    {
        /// <summary>
        /// Create a new definition (default if ID doesn't exist).
        /// </summary>
        Create,

        /// <summary>
        /// Modify existing properties of a definition, keeping unspecified properties unchanged.
        /// </summary>
        Modify,

        /// <summary>
        /// Extend a definition by adding new properties while keeping all existing properties.
        /// </summary>
        Extend,

        /// <summary>
        /// Completely replace the existing definition with this one.
        /// </summary>
        Replace,
    }
}
