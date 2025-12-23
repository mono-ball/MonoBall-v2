namespace MonoBall.Core.ECS.Services
{
    /// <summary>
    /// Validator for flag IDs and variable keys.
    /// Ensures flags/variables follow naming conventions and are valid.
    /// </summary>
    public interface IFlagVariableValidator
    {
        /// <summary>
        /// Validates a flag ID.
        /// </summary>
        /// <param name="flagId">The flag identifier to validate.</param>
        /// <returns>True if the flag ID is valid, false otherwise.</returns>
        bool IsValidFlagId(string flagId);

        /// <summary>
        /// Validates a variable key.
        /// </summary>
        /// <param name="key">The variable key to validate.</param>
        /// <returns>True if the variable key is valid, false otherwise.</returns>
        bool IsValidVariableKey(string key);

        /// <summary>
        /// Gets validation error message for an invalid flag ID.
        /// </summary>
        /// <param name="flagId">The invalid flag identifier.</param>
        /// <returns>Error message explaining why the flag ID is invalid.</returns>
        string GetFlagIdValidationError(string flagId);

        /// <summary>
        /// Gets validation error message for an invalid variable key.
        /// </summary>
        /// <param name="key">The invalid variable key.</param>
        /// <returns>Error message explaining why the variable key is invalid.</returns>
        string GetVariableKeyValidationError(string key);
    }
}
