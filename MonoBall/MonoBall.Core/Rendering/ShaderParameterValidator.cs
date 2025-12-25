using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Serilog;

namespace MonoBall.Core.Rendering
{
    /// <summary>
    /// Service for validating shader parameters before application.
    /// Validates parameter existence, types, and values by querying Effect.Parameters at runtime.
    /// </summary>
    public class ShaderParameterValidator : IShaderParameterValidator, IDisposable
    {
        private readonly IShaderService _shaderService;
        private readonly ILogger _logger;
        private bool _disposed = false;

        /// <summary>
        /// Initializes a new instance of the ShaderParameterValidator.
        /// </summary>
        /// <param name="shaderService">The shader service for accessing shader effects.</param>
        /// <param name="logger">The logger for logging operations.</param>
        public ShaderParameterValidator(IShaderService shaderService, ILogger logger)
        {
            _shaderService =
                shaderService ?? throw new ArgumentNullException(nameof(shaderService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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

            // Get shader to validate parameter exists
            Effect? effect = _shaderService.GetShader(shaderId);
            if (effect == null)
            {
                error = $"Shader '{shaderId}' not found.";
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
