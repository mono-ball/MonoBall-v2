namespace MonoBall.Core.Rendering
{
    /// <summary>
    /// Service for validating shader parameters before application.
    /// Provides runtime validation for Dictionary&lt;string, object&gt; parameters.
    /// </summary>
    public interface IShaderParameterValidator
    {
        /// <summary>
        /// Validates a shader parameter value.
        /// </summary>
        /// <param name="shaderId">The shader ID.</param>
        /// <param name="parameterName">The parameter name.</param>
        /// <param name="value">The parameter value to validate.</param>
        /// <param name="error">When this method returns, contains the error message if validation failed; otherwise, null.</param>
        /// <returns>True if the parameter is valid; otherwise, false.</returns>
        bool ValidateParameter(
            string shaderId,
            string parameterName,
            object value,
            out string? error
        );
    }
}
