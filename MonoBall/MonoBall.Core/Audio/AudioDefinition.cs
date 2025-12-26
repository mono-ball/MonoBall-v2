using System.Text.Json.Serialization;

namespace MonoBall.Core.Audio
{
    /// <summary>
    /// Represents an audio definition loaded from mod JSON files.
    /// </summary>
    public class AudioDefinition
    {
        /// <summary>
        /// The unique identifier of the audio definition (unified ID format).
        /// </summary>
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// Human-readable display name.
        /// </summary>
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Path to audio file relative to mod directory.
        /// </summary>
        [JsonPropertyName("audioPath")]
        public string AudioPath { get; set; } = string.Empty;

        /// <summary>
        /// Default volume (0.0 - 1.0).
        /// </summary>
        [JsonPropertyName("volume")]
        public float Volume { get; set; } = 1.0f;

        /// <summary>
        /// Whether the track should loop.
        /// </summary>
        [JsonPropertyName("loop")]
        public bool Loop { get; set; } = true;

        /// <summary>
        /// Fade-in duration in seconds.
        /// </summary>
        [JsonPropertyName("fadeIn")]
        public float FadeIn { get; set; }

        /// <summary>
        /// Fade-out duration in seconds.
        /// </summary>
        [JsonPropertyName("fadeOut")]
        public float FadeOut { get; set; }

        /// <summary>
        /// Loop start position in samples (at 44100 Hz, optional).
        /// </summary>
        [JsonPropertyName("loopStartSamples")]
        public int? LoopStartSamples { get; set; }

        /// <summary>
        /// Loop length in samples (optional).
        /// </summary>
        [JsonPropertyName("loopLengthSamples")]
        public int? LoopLengthSamples { get; set; }

        /// <summary>
        /// Loop start position in seconds (optional).
        /// </summary>
        [JsonPropertyName("loopStartSec")]
        public float? LoopStartSec { get; set; }

        /// <summary>
        /// Loop end position in seconds (optional).
        /// </summary>
        [JsonPropertyName("loopEndSec")]
        public float? LoopEndSec { get; set; }
    }
}
