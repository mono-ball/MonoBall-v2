using System.Collections.Generic;
using MonoBall.Core.Mods.Definitions;

namespace MonoBall.Core.ECS.Services
{
    /// <summary>
    /// Service for accessing shader preset definitions.
    /// Data-only service per SRP - mutation is handled by IShaderAnimationApi.ApplyPreset().
    /// </summary>
    public interface IShaderPresetService
    {
        /// <summary>
        /// Gets a shader preset definition by ID.
        /// </summary>
        /// <param name="presetId">The preset ID (e.g., "base:shaderpreset:spooky_night").</param>
        /// <returns>The preset definition, or null if not found.</returns>
        ShaderPresetDefinition? GetPreset(string presetId);

        /// <summary>
        /// Resolves preset parameters to a dictionary of parameter names and values.
        /// Converts JSON values to appropriate types (float, Vector2, Vector3, Color, etc.).
        /// </summary>
        /// <param name="presetId">The preset ID.</param>
        /// <returns>Dictionary of resolved parameters, or empty if preset not found.</returns>
        Dictionary<string, object> ResolveParameters(string presetId);

        /// <summary>
        /// Gets all registered preset IDs.
        /// </summary>
        /// <returns>Collection of all preset IDs.</returns>
        IEnumerable<string> GetAllPresetIds();

        /// <summary>
        /// Gets all preset IDs for a specific shader.
        /// </summary>
        /// <param name="shaderId">The shader ID to filter by.</param>
        /// <returns>Collection of preset IDs for the shader.</returns>
        IEnumerable<string> GetPresetsForShader(string shaderId);

        /// <summary>
        /// Gets all preset IDs with a specific tag.
        /// </summary>
        /// <param name="tag">The tag to filter by.</param>
        /// <returns>Collection of preset IDs with the tag.</returns>
        IEnumerable<string> GetPresetsByTag(string tag);

        /// <summary>
        /// Checks if a preset exists.
        /// </summary>
        /// <param name="presetId">The preset ID to check.</param>
        /// <returns>True if preset exists, false otherwise.</returns>
        bool PresetExists(string presetId);
    }
}
