using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Serilog;

namespace MonoBall.Core.Rendering
{
    /// <summary>
    /// Utility class for applying shader parameters to effects.
    /// Shared implementation to avoid code duplication.
    /// </summary>
    public static class ShaderParameterApplier
    {
        /// <summary>
        /// Applies a parameter value to an effect.
        /// </summary>
        /// <param name="effect">The shader effect.</param>
        /// <param name="paramName">The parameter name.</param>
        /// <param name="value">The parameter value.</param>
        /// <param name="logger">Optional logger for warnings/errors.</param>
        /// <exception cref="InvalidOperationException">Thrown when parameter cannot be set (type mismatch, etc.).</exception>
        public static void ApplyParameter(
            Effect effect,
            string paramName,
            object value,
            ILogger? logger = null
        )
        {
            if (effect == null)
                throw new ArgumentNullException(nameof(effect));
            if (string.IsNullOrEmpty(paramName))
                throw new ArgumentNullException(nameof(paramName));
            if (value == null)
                throw new ArgumentNullException(nameof(value));

            EffectParameter? param = null;
            try
            {
                param = effect.Parameters[paramName];
            }
            catch (KeyNotFoundException)
            {
                // Parameter doesn't exist - log and return (parameter is optional)
                logger?.Warning("Parameter {ParamName} not found in shader", paramName);
                return;
            }

            if (param == null)
            {
                logger?.Warning("Parameter {ParamName} is null in shader", paramName);
                return;
            }

            try
            {
                switch (value)
                {
                    case float f:
                        if (param.ParameterType == EffectParameterType.Single)
                            param.SetValue(f);
                        else
                            throw new InvalidOperationException(
                                $"Parameter '{paramName}' is not a Single type, cannot set float value"
                            );
                        break;
                    case Vector2 v2:
                        if (
                            param.ParameterClass == EffectParameterClass.Vector
                            && param.ColumnCount == 2
                        )
                            param.SetValue(v2);
                        else
                            throw new InvalidOperationException(
                                $"Parameter '{paramName}' is not a Vector2 type (Class: {param.ParameterClass}, Columns: {param.ColumnCount})"
                            );
                        break;
                    case Vector3 v3:
                        if (
                            param.ParameterClass == EffectParameterClass.Vector
                            && param.ColumnCount == 3
                        )
                            param.SetValue(v3);
                        else
                            throw new InvalidOperationException(
                                $"Parameter '{paramName}' is not a Vector3 type (Class: {param.ParameterClass}, Columns: {param.ColumnCount})"
                            );
                        break;
                    case Vector4 v4:
                        if (
                            param.ParameterClass == EffectParameterClass.Vector
                            && param.ColumnCount == 4
                        )
                            param.SetValue(v4);
                        else
                            throw new InvalidOperationException(
                                $"Parameter '{paramName}' is not a Vector4 type (Class: {param.ParameterClass}, Columns: {param.ColumnCount})"
                            );
                        break;
                    case Color color:
                        if (
                            param.ParameterClass == EffectParameterClass.Vector
                            && param.ColumnCount == 4
                        )
                            param.SetValue(color.ToVector4());
                        else
                            throw new InvalidOperationException(
                                $"Parameter '{paramName}' is not a Color/Vector4 type (Class: {param.ParameterClass}, Columns: {param.ColumnCount})"
                            );
                        break;
                    case Texture2D texture:
                        if (param.ParameterClass == EffectParameterClass.Object)
                            param.SetValue(texture);
                        else
                            throw new InvalidOperationException(
                                $"Parameter '{paramName}' is not a Texture2D/Object type (Class: {param.ParameterClass})"
                            );
                        break;
                    case Matrix matrix:
                        if (param.ParameterClass == EffectParameterClass.Matrix)
                            param.SetValue(matrix);
                        else
                            throw new InvalidOperationException(
                                $"Parameter '{paramName}' is not a Matrix type (Class: {param.ParameterClass})"
                            );
                        break;
                    default:
                        throw new InvalidOperationException(
                            $"Unsupported parameter type {value.GetType().Name} for parameter '{paramName}'"
                        );
                }
            }
            catch (InvalidOperationException)
            {
                // Re-throw InvalidOperationException (these are our validation errors)
                throw;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Failed to set shader parameter '{paramName}': {ex.Message}",
                    ex
                );
            }
        }

        /// <summary>
        /// Applies multiple parameters to an effect.
        /// </summary>
        /// <param name="effect">The shader effect.</param>
        /// <param name="parameters">The parameters dictionary.</param>
        /// <param name="logger">Optional logger for warnings/errors.</param>
        public static void ApplyParameters(
            Effect effect,
            Dictionary<string, object> parameters,
            ILogger? logger = null
        )
        {
            if (effect == null)
                throw new ArgumentNullException(nameof(effect));
            if (parameters == null)
                throw new ArgumentNullException(nameof(parameters));

            foreach (var (paramName, value) in parameters)
            {
                try
                {
                    ApplyParameter(effect, paramName, value, logger);
                }
                catch (InvalidOperationException ex)
                {
                    // Log and continue - one parameter failure shouldn't stop others
                    logger?.Warning(ex, "Failed to apply parameter {ParamName}", paramName);
                }
            }
        }

        /// <summary>
        /// Ensures that the effect has a CurrentTechnique set.
        /// Sets the first technique if CurrentTechnique is null.
        /// </summary>
        /// <param name="effect">The shader effect.</param>
        /// <param name="logger">Optional logger for debugging.</param>
        public static void EnsureCurrentTechnique(Effect effect, ILogger? logger = null)
        {
            if (effect == null)
                throw new ArgumentNullException(nameof(effect));

            if (effect.CurrentTechnique == null && effect.Techniques.Count > 0)
            {
                effect.CurrentTechnique = effect.Techniques[0];
                logger?.Debug(
                    "Set CurrentTechnique to {TechniqueName} for shader {ShaderName}",
                    effect.CurrentTechnique.Name,
                    effect.Name
                );
            }
        }
    }
}
