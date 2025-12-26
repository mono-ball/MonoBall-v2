namespace MonoBall.Core.ECS.Components.Audio
{
    /// <summary>
    /// Component for entities that emit ambient/looping sounds.
    /// The system manages sound instances separately (components are pure data).
    /// </summary>
    public struct AmbientSoundComponent
    {
        /// <summary>
        /// The audio definition ID for the ambient sound.
        /// </summary>
        public string AudioId { get; set; }

        /// <summary>
        /// Volume override (0-1, or -1 to use definition default).
        /// </summary>
        public float Volume { get; set; }

        /// <summary>
        /// Pitch adjustment (-1 to 1).
        /// </summary>
        public float Pitch { get; set; }

        /// <summary>
        /// Pan adjustment (-1 left to 1 right).
        /// </summary>
        public float Pan { get; set; }
    }
}
