using System.Collections.Generic;

namespace MonoBall.Core.ECS.Components
{
    /// <summary>
    /// Component that stores metadata about flags and variables.
    /// Used for documentation, validation, and tooling support.
    /// </summary>
    public struct FlagVariableMetadataComponent
    {
        /// <summary>
        /// Metadata for flags. Key: flag ID, Value: metadata.
        /// </summary>
        public Dictionary<string, FlagMetadata> FlagMetadata { get; set; }

        /// <summary>
        /// Metadata for variables. Key: variable key, Value: metadata.
        /// </summary>
        public Dictionary<string, VariableMetadata> VariableMetadata { get; set; }
    }

    /// <summary>
    /// Metadata for a flag.
    /// </summary>
    public struct FlagMetadata
    {
        /// <summary>
        /// The flag identifier.
        /// </summary>
        public string FlagId { get; set; }

        /// <summary>
        /// Human-readable description of the flag.
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// Category of the flag (e.g., "visibility", "item", "quest").
        /// </summary>
        public string Category { get; set; }

        /// <summary>
        /// Whether this flag is mod-defined (true) or core-defined (false).
        /// </summary>
        public bool IsModDefined { get; set; }
    }

    /// <summary>
    /// Metadata for a variable.
    /// </summary>
    public struct VariableMetadata
    {
        /// <summary>
        /// The variable key.
        /// </summary>
        public string Key { get; set; }

        /// <summary>
        /// Human-readable description of the variable.
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// Category of the variable (e.g., "player", "quest", "map").
        /// </summary>
        public string Category { get; set; }

        /// <summary>
        /// Expected type of the variable (e.g., "string", "int", "float").
        /// </summary>
        public string ExpectedType { get; set; }

        /// <summary>
        /// Whether this variable is mod-defined (true) or core-defined (false).
        /// </summary>
        public bool IsModDefined { get; set; }
    }
}
