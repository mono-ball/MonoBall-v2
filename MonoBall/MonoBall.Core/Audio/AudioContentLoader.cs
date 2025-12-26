using System;
using System.Collections.Generic;
using System.IO;
using MonoBall.Core.Audio.Core;
using MonoBall.Core.Mods;
using Serilog;

namespace MonoBall.Core.Audio
{
    /// <summary>
    /// Service for loading audio assets from mod content directories.
    /// </summary>
    public class AudioContentLoader : IAudioContentLoader
    {
        private readonly IModManager _modManager;
        private readonly ILogger _logger;

        /// <summary>
        /// Initializes a new instance of the AudioContentLoader.
        /// </summary>
        /// <param name="modManager">The mod manager for accessing definitions and mod directories.</param>
        /// <param name="logger">The logger for logging operations.</param>
        public AudioContentLoader(IModManager modManager, ILogger logger)
        {
            _modManager = modManager ?? throw new ArgumentNullException(nameof(modManager));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Creates a VorbisReader for the specified audio definition.
        /// </summary>
        /// <param name="audioId">The audio definition ID.</param>
        /// <param name="definition">The audio definition (obtained from DefinitionRegistry).</param>
        /// <param name="modManifest">The mod manifest (obtained from IModManager).</param>
        /// <returns>The VorbisReader, or null if file not found.</returns>
        public VorbisReader? CreateVorbisReader(
            string audioId,
            AudioDefinition definition,
            ModManifest modManifest
        )
        {
            if (string.IsNullOrEmpty(audioId))
            {
                throw new ArgumentException("Audio ID cannot be null or empty.", nameof(audioId));
            }

            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            if (modManifest == null)
            {
                throw new ArgumentNullException(nameof(modManifest));
            }

            // Resolve audio file path (following SpriteLoaderService pattern)
            string audioPath = Path.Combine(modManifest.ModDirectory, definition.AudioPath);
            audioPath = Path.GetFullPath(audioPath);

            if (!File.Exists(audioPath))
            {
                _logger.Warning(
                    "Audio file not found: {AudioPath} (audio: {AudioId})",
                    audioPath,
                    audioId
                );
                return null; // Return null, don't throw (matches existing pattern)
            }

            try
            {
                // Create a new reader each time
                // Note: VorbisReader wraps NVorbis.VorbisReader which is relatively lightweight.
                // Caching could be added in the future if needed for performance optimization.
                return new VorbisReader(audioPath);
            }
            catch (Exception ex)
            {
                _logger.Error(
                    ex,
                    "Failed to create VorbisReader for {AudioId} from {AudioPath}",
                    audioId,
                    audioPath
                );
                return null;
            }
        }

        /// <summary>
        /// Unloads cached metadata for the specified audio.
        /// </summary>
        /// <param name="audioId">The audio definition ID.</param>
        /// <remarks>
        /// Currently a no-op as caching is not implemented. Reserved for future use.
        /// </remarks>
        public void Unload(string audioId)
        {
            // Caching not yet implemented - reserved for future use
            // When caching is added, this method will dispose cached readers
        }
    }
}
