namespace MonoBall.Core.Scripting.Utilities
{
    /// <summary>
    /// Utility class for generating consistent state keys for scripts.
    /// Provides centralized key format management to avoid duplication.
    /// </summary>
    public static class ScriptStateKeys
    {
        /// <summary>
        /// Gets the state key for a script state value.
        /// Format: "script:{scriptDefinitionId}:{key}"
        /// </summary>
        /// <param name="scriptDefinitionId">The script definition ID.</param>
        /// <param name="key">The state key.</param>
        /// <returns>The full state key.</returns>
        public static string GetStateKey(string scriptDefinitionId, string key)
        {
            return $"script:{scriptDefinitionId}:{key}";
        }

        /// <summary>
        /// Gets the parameter override key for a script parameter.
        /// Format: "script:{scriptDefinitionId}:param:{paramName}"
        /// </summary>
        /// <param name="scriptDefinitionId">The script definition ID.</param>
        /// <param name="paramName">The parameter name.</param>
        /// <returns>The full parameter override key.</returns>
        public static string GetParameterKey(string scriptDefinitionId, string paramName)
        {
            return $"script:{scriptDefinitionId}:param:{paramName}";
        }
    }
}
