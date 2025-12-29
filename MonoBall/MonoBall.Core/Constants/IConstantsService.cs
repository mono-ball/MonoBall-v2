using System.Collections.Generic;

namespace MonoBall.Core.Constants;

/// <summary>
///     Service for accessing game constants loaded from mods.
/// </summary>
public interface IConstantsService
{
    /// <summary>
    ///     Gets a constant value by key, throwing if not found.
    /// </summary>
    /// <typeparam name="T">The expected type of the constant.</typeparam>
    /// <param name="key">The constant key (e.g., "TileChunkSize").</param>
    /// <returns>The constant value.</returns>
    /// <exception cref="System.KeyNotFoundException">Thrown if the constant is not found.</exception>
    /// <exception cref="System.InvalidCastException">Thrown if the constant cannot be converted to T.</exception>
    T Get<T>(string key)
        where T : struct;

    /// <summary>
    ///     Gets a string constant value, throwing if not found.
    /// </summary>
    /// <param name="key">The constant key.</param>
    /// <returns>The constant value.</returns>
    /// <exception cref="System.KeyNotFoundException">Thrown if the constant is not found.</exception>
    string GetString(string key);

    /// <summary>
    ///     Tries to get a constant value, returning false if not found.
    /// </summary>
    /// <typeparam name="T">The expected type of the constant.</typeparam>
    /// <param name="key">The constant key.</param>
    /// <param name="value">The constant value if found.</param>
    /// <returns>True if the constant was found, false otherwise.</returns>
    bool TryGet<T>(string key, out T value)
        where T : struct;

    /// <summary>
    ///     Tries to get a string constant value, returning false if not found.
    /// </summary>
    /// <param name="key">The constant key.</param>
    /// <param name="value">The constant value if found.</param>
    /// <returns>True if the constant was found, false otherwise.</returns>
    bool TryGetString(string key, out string? value);

    /// <summary>
    ///     Checks if a constant exists.
    /// </summary>
    /// <param name="key">The constant key.</param>
    /// <returns>True if the constant exists, false otherwise.</returns>
    bool Contains(string key);

    /// <summary>
    ///     Validates that required constants exist. Call after service creation to fail-fast.
    /// </summary>
    /// <param name="requiredKeys">The keys that must exist.</param>
    /// <exception cref="System.InvalidOperationException">Thrown if any required constants are missing.</exception>
    void ValidateRequiredConstants(IEnumerable<string> requiredKeys);

    /// <summary>
    ///     Validates all constants against their validation rules (if defined).
    ///     Call after service creation to fail-fast on invalid values.
    ///     Validates the final merged constants (after all mod overrides), not individual definitions.
    /// </summary>
    /// <exception cref="System.InvalidOperationException">Thrown if any constants fail validation.</exception>
    void ValidateConstants();
}
