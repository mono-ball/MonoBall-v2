using System;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoBall.Core.Mods;
using MonoBall.Core.Mods.Definitions;
using Serilog;

namespace MonoBall.Core.Rendering
{
    /// <summary>
    /// Service for validating shader parameters before application.
    /// Validates parameter existence, types, and values by querying Effect.Parameters at runtime.
    /// Uses ShaderDefinition metadata for enhanced validation (min/max, type checking, better error messages).
    /// </summary>
    public class ShaderParameterValidator : IShaderParameterValidator, IDisposable
    {
        private readonly IShaderService _shaderService;
        private readonly IModManager? _modManager;
        private readonly ILogger _logger;
        private bool _disposed = false;

        /// <summary>
        /// Initializes a new instance of the ShaderParameterValidator.
        /// </summary>
        /// <param name="shaderService">The shader service for accessing shader effects.</param>
        /// <param name="logger">The logger for logging operations.</param>
        /// <param name="modManager">Optional mod manager for accessing shader definition metadata.</param>
        public ShaderParameterValidator(
            IShaderService shaderService,
            ILogger logger,
            IModManager? modManager = null
        )
        {
            _shaderService =
                shaderService ?? throw new ArgumentNullException(nameof(shaderService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _modManager = modManager;
        }

        /// <inheritdoc />
        public bool ValidateParameter(
            string shaderId,
            string parameterName,
            object value,
            out string? error
        )
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ShaderParameterValidator));

            error = null;

            if (string.IsNullOrEmpty(shaderId))
            {
                error = "Shader ID cannot be null or empty.";
                return false;
            }

            if (string.IsNullOrEmpty(parameterName))
            {
                error = "Parameter name cannot be null or empty.";
                return false;
            }

            if (value == null)
            {
                error = "Parameter value cannot be null.";
                return false;
            }

            // Check if shader exists first
            if (!_shaderService.HasShader(shaderId))
            {
                error = $"Shader '{shaderId}' not found.";
                return false;
            }

            // Get shader to validate parameter exists (fail fast if loading fails)
            Effect effect;
            try
            {
                effect = _shaderService.GetShader(shaderId);
            }
            catch (Exception ex)
            {
                error = $"Shader '{shaderId}' failed to load: {ex.Message}";
                return false;
            }

            // Check if parameter exists (try to get it, catch KeyNotFoundException if not found)
            EffectParameter? parameter = null;
            try
            {
                parameter = effect.Parameters[parameterName];
            }
            catch (System.Collections.Generic.KeyNotFoundException)
            {
                error = $"Parameter '{parameterName}' does not exist in shader '{shaderId}'.";
                return false;
            }
            catch (Exception ex)
            {
                // Unexpected error - fail fast per .cursorrules
                throw new InvalidOperationException(
                    $"Unexpected error accessing parameter '{parameterName}' in shader '{shaderId}': {ex.Message}",
                    ex
                );
            }

            if (parameter == null)
            {
                error = $"Parameter '{parameterName}' does not exist in shader '{shaderId}'.";
                return false;
            }

            // Validate parameter type matches value type
            Type valueType = value.GetType();
            bool isValid = parameter.ParameterType switch
            {
                EffectParameterType.Single => valueType == typeof(float),
                EffectParameterType.Bool => valueType == typeof(bool),
                EffectParameterType.Int32 => valueType == typeof(int),
                EffectParameterType.String => valueType == typeof(string),
                _ => false,
            };

            // For vector types, check the parameter class instead
            if (!isValid)
            {
                // Check if it's a vector type by checking the parameter's class
                if (
                    parameter.ParameterClass == EffectParameterClass.Vector
                    && valueType == typeof(Vector2)
                )
                {
                    isValid = parameter.ColumnCount == 2;
                }
                else if (
                    parameter.ParameterClass == EffectParameterClass.Vector
                    && valueType == typeof(Vector3)
                )
                {
                    isValid = parameter.ColumnCount == 3;
                }
                else if (
                    parameter.ParameterClass == EffectParameterClass.Vector
                    && valueType == typeof(Vector4)
                )
                {
                    isValid = parameter.ColumnCount == 4;
                }
                else if (
                    parameter.ParameterClass == EffectParameterClass.Vector
                    && valueType == typeof(Color)
                )
                {
                    isValid = parameter.ColumnCount == 4;
                }
                else if (
                    parameter.ParameterClass == EffectParameterClass.Matrix
                    && valueType == typeof(Matrix)
                )
                {
                    isValid = true;
                }
                else if (
                    parameter.ParameterClass == EffectParameterClass.Object
                    && valueType == typeof(Texture2D)
                )
                {
                    isValid = true;
                }
            }

            if (!isValid)
            {
                error =
                    $"Parameter '{parameterName}' in shader '{shaderId}' has type {parameter.ParameterType}, but value is of type {valueType.Name}.";
                return false;
            }

            // Enhanced validation using ShaderDefinition metadata
            if (_modManager != null)
            {
                var shaderDef = _modManager.GetDefinition<ShaderDefinition>(shaderId);
                if (shaderDef?.Parameters != null)
                {
                    var paramDef = shaderDef.Parameters.FirstOrDefault(p =>
                        p.Name == parameterName
                    );
                    if (paramDef != null)
                    {
                        // Validate min/max for numeric types
                        if (
                            valueType == typeof(float)
                            || valueType == typeof(double)
                            || valueType == typeof(int)
                        )
                        {
                            double numericValue = Convert.ToDouble(value);

                            if (paramDef.Min.HasValue && numericValue < paramDef.Min.Value)
                            {
                                error =
                                    $"Parameter '{parameterName}' value {numericValue} is below minimum {paramDef.Min.Value}.";
                                return false;
                            }

                            if (paramDef.Max.HasValue && numericValue > paramDef.Max.Value)
                            {
                                error =
                                    $"Parameter '{parameterName}' value {numericValue} is above maximum {paramDef.Max.Value}.";
                                return false;
                            }
                        }

                        // Validate type against definition (if type is specified)
                        if (!string.IsNullOrEmpty(paramDef.Type))
                        {
                            bool typeMatches;
                            string typeLower = paramDef.Type.ToLowerInvariant();
                            switch (typeLower)
                            {
                                case "float":
                                    typeMatches = valueType == typeof(float);
                                    break;
                                case "float2":
                                    typeMatches = valueType == typeof(Vector2);
                                    break;
                                case "float3":
                                    typeMatches = valueType == typeof(Vector3);
                                    break;
                                case "float4":
                                    typeMatches =
                                        valueType == typeof(Vector4) || valueType == typeof(Color);
                                    break;
                                case "texture2d":
                                    typeMatches = valueType == typeof(Texture2D);
                                    break;
                                default:
                                    typeMatches = true; // Unknown type - assume valid
                                    break;
                            }

                            if (!typeMatches)
                            {
                                error =
                                    $"Parameter '{parameterName}' definition specifies type '{paramDef.Type}', but value is of type {valueType.Name}.";
                                return false;
                            }
                        }
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// Disposes the validator.
        /// </summary>
        public void Dispose()
        {
            if (!_disposed)
            {
                // No managed resources to dispose
                _disposed = true;
            }
        }
    }
}
