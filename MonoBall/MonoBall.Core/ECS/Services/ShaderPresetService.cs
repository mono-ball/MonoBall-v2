using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Microsoft.Xna.Framework;
using MonoBall.Core.Mods;
using MonoBall.Core.Mods.Definitions;
using Serilog;

namespace MonoBall.Core.ECS.Services
{
    /// <summary>
    /// Implementation of IShaderPresetService.
    /// Provides data access for shader presets loaded from mod definitions.
    /// </summary>
    public class ShaderPresetService : IShaderPresetService
    {
        private readonly DefinitionRegistry _definitionRegistry;
        private readonly ILogger _logger;

        /// <summary>
        /// The definition type for shader presets in the registry.
        /// </summary>
        private const string ShaderPresetDefinitionType = "shaderpreset";

        /// <summary>
        /// Creates a new shader preset service.
        /// </summary>
        /// <param name="definitionRegistry">The definition registry containing preset definitions.</param>
        /// <param name="logger">The logger for logging operations.</param>
        public ShaderPresetService(DefinitionRegistry definitionRegistry, ILogger logger)
        {
            _definitionRegistry =
                definitionRegistry ?? throw new ArgumentNullException(nameof(definitionRegistry));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc />
        public ShaderPresetDefinition? GetPreset(string presetId)
        {
            if (string.IsNullOrEmpty(presetId))
                return null;

            return _definitionRegistry.GetById<ShaderPresetDefinition>(presetId);
        }

        /// <inheritdoc />
        public Dictionary<string, object> ResolveParameters(string presetId)
        {
            var result = new Dictionary<string, object>();

            var preset = GetPreset(presetId);
            if (preset?.Parameters == null)
                return result;

            foreach (var kvp in preset.Parameters)
            {
                var resolved = ResolveParameterValue(kvp.Value, kvp.Key);
                if (resolved != null)
                {
                    result[kvp.Key] = resolved;
                }
            }

            return result;
        }

        /// <inheritdoc />
        public IEnumerable<string> GetAllPresetIds()
        {
            return _definitionRegistry.GetByType(ShaderPresetDefinitionType);
        }

        /// <inheritdoc />
        public IEnumerable<string> GetPresetsForShader(string shaderId)
        {
            if (string.IsNullOrEmpty(shaderId))
                return Enumerable.Empty<string>();

            return GetAllPresetIds()
                .Where(id =>
                {
                    var preset = GetPreset(id);
                    return preset?.ShaderId == shaderId;
                });
        }

        /// <inheritdoc />
        public IEnumerable<string> GetPresetsByTag(string tag)
        {
            if (string.IsNullOrEmpty(tag))
                return Enumerable.Empty<string>();

            return GetAllPresetIds()
                .Where(id =>
                {
                    var preset = GetPreset(id);
                    return preset?.Tags?.Contains(tag, StringComparer.OrdinalIgnoreCase) == true;
                });
        }

        /// <inheritdoc />
        public bool PresetExists(string presetId)
        {
            return GetPreset(presetId) != null;
        }

        /// <summary>
        /// Resolves a JSON parameter value to the appropriate runtime type.
        /// </summary>
        private object? ResolveParameterValue(object value, string paramName)
        {
            try
            {
                // Handle JsonElement from System.Text.Json
                if (value is JsonElement jsonElement)
                {
                    return ResolveJsonElement(jsonElement, paramName);
                }

                // Already resolved value
                return value;
            }
            catch (Exception ex)
            {
                _logger.Warning(
                    ex,
                    "Failed to resolve parameter {ParamName} with value {Value}",
                    paramName,
                    value
                );
                return null;
            }
        }

        /// <summary>
        /// Resolves a JsonElement to the appropriate runtime type.
        /// </summary>
        private object? ResolveJsonElement(JsonElement element, string paramName)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Number:
                    return element.GetSingle();

                case JsonValueKind.Array:
                    var array = element.EnumerateArray().ToArray();
                    if (array.Length == 2)
                    {
                        return new Vector2(array[0].GetSingle(), array[1].GetSingle());
                    }
                    else if (array.Length == 3)
                    {
                        return new Vector3(
                            array[0].GetSingle(),
                            array[1].GetSingle(),
                            array[2].GetSingle()
                        );
                    }
                    else if (array.Length == 4)
                    {
                        // Check if values are 0-1 (color components) or 0-255 (byte components)
                        float r = array[0].GetSingle();
                        float g = array[1].GetSingle();
                        float b = array[2].GetSingle();
                        float a = array[3].GetSingle();

                        if (r <= 1.0f && g <= 1.0f && b <= 1.0f && a <= 1.0f)
                        {
                            // Interpret as Vector4 (normalized color or generic 4D vector)
                            return new Vector4(r, g, b, a);
                        }
                        else
                        {
                            // Interpret as byte Color (0-255)
                            return new Color((int)r, (int)g, (int)b, (int)a);
                        }
                    }
                    _logger.Warning(
                        "Unsupported array length {Length} for parameter {ParamName}",
                        array.Length,
                        paramName
                    );
                    return null;

                case JsonValueKind.Object:
                    // Could be a Color object with named properties
                    if (
                        element.TryGetProperty("r", out var rProp)
                        && element.TryGetProperty("g", out var gProp)
                        && element.TryGetProperty("b", out var bProp)
                    )
                    {
                        float r = rProp.GetSingle();
                        float g = gProp.GetSingle();
                        float b = bProp.GetSingle();
                        float a = element.TryGetProperty("a", out var aProp)
                            ? aProp.GetSingle()
                            : 1.0f;
                        return new Vector4(r, g, b, a);
                    }
                    return null;

                case JsonValueKind.True:
                    return 1.0f;

                case JsonValueKind.False:
                    return 0.0f;

                default:
                    return null;
            }
        }
    }
}
