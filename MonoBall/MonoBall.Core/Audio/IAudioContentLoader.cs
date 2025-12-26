using MonoBall.Core.Audio.Core;
using MonoBall.Core.Mods;

namespace MonoBall.Core.Audio
{
    /// <summary>
    /// Interface for loading audio assets from mod content directories.
    /// </summary>
    public interface IAudioContentLoader
    {
        /// <summary>
        /// Creates a VorbisReader for the specified audio definition.
        /// </summary>
        /// <param name="audioId">The audio definition ID.</param>
        /// <param name="definition">The audio definition (obtained from DefinitionRegistry).</param>
        /// <param name="modManifest">The mod manifest (obtained from IModManager).</param>
        /// <returns>The VorbisReader, or null if file not found.</returns>
        VorbisReader? CreateVorbisReader(
            string audioId,
            AudioDefinition definition,
            ModManifest modManifest
        );

        /// <summary>
        /// Unloads cached metadata for the specified audio.
        /// </summary>
        /// <param name="audioId">The audio definition ID.</param>
        void Unload(string audioId);
    }
}
