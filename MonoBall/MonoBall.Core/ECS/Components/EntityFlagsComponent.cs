using System.Collections.Generic;

namespace MonoBall.Core.ECS.Components
{
    /// <summary>
    /// Component that stores boolean flags for a specific entity.
    /// Similar to FlagsComponent but scoped to individual entities.
    /// This component is pure data - all logic is handled by FlagVariableService.
    /// </summary>
    public struct EntityFlagsComponent
    {
        /// <summary>
        /// Bitfield storage for entity flags. Each bit represents one flag.
        /// </summary>
        public byte[] Flags { get; set; }

        /// <summary>
        /// Mapping from flag ID string to bit index.
        /// </summary>
        public Dictionary<string, int> FlagIndices { get; set; }

        /// <summary>
        /// Reverse mapping from bit index to flag ID.
        /// </summary>
        public Dictionary<int, string> IndexToFlagId { get; set; }

        /// <summary>
        /// Next available bit index for new flags.
        /// </summary>
        public int NextIndex { get; set; }
    }
}
