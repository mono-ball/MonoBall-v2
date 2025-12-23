using System.Collections.Generic;

namespace MonoBall.Core.ECS.Components
{
    /// <summary>
    /// Component that stores boolean game flags using bitfield compression.
    /// Flags are identified by string IDs (e.g., "base:flag:visibility/npc_birch").
    /// This component is pure data - all logic is handled by FlagVariableService.
    /// </summary>
    public struct FlagsComponent
    {
        /// <summary>
        /// Bitfield storage for flags. Each bit represents one flag.
        /// Size: 313 bytes = 2504 flags (expandable).
        /// Must be initialized (non-null) when component is created.
        /// </summary>
        public byte[] Flags { get; set; }

        /// <summary>
        /// Mapping from flag ID string to bit index.
        /// Populated lazily as flags are accessed.
        /// Must be initialized (non-null) when component is created.
        /// </summary>
        public Dictionary<string, int> FlagIndices { get; set; }

        /// <summary>
        /// Reverse mapping from bit index to flag ID.
        /// Used for serialization and debugging.
        /// Must be initialized (non-null) when component is created.
        /// </summary>
        public Dictionary<int, string> IndexToFlagId { get; set; }

        /// <summary>
        /// Next available bit index for new flags.
        /// </summary>
        public int NextIndex { get; set; }
    }
}
