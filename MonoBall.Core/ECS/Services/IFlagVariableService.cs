using System.Collections.Generic;
using Arch.Core;
using MonoBall.Core.ECS.Components;

namespace MonoBall.Core.ECS.Services;

/// <summary>
///     Service for managing game flags and variables.
///     Provides a clean API for setting, getting, and querying flags/variables.
/// </summary>
public interface IFlagVariableService
{
    #region Flags

    /// <summary>
    ///     Gets the value of a flag.
    /// </summary>
    /// <param name="flagId">The flag identifier.</param>
    /// <returns>True if the flag is set, false otherwise.</returns>
    bool GetFlag(string flagId);

    /// <summary>
    ///     Sets the value of a flag.
    /// </summary>
    /// <param name="flagId">The flag identifier.</param>
    /// <param name="value">The value to set.</param>
    void SetFlag(string flagId, bool value);

    /// <summary>
    ///     Checks if a flag exists (has been set at least once).
    /// </summary>
    /// <param name="flagId">The flag identifier.</param>
    /// <returns>True if the flag exists.</returns>
    bool FlagExists(string flagId);

    /// <summary>
    ///     Gets all flag IDs that are currently set to true.
    /// </summary>
    /// <returns>Collection of active flag IDs.</returns>
    IEnumerable<string> GetActiveFlags();

    #endregion

    #region Variables

    /// <summary>
    ///     Gets a variable value of the specified type.
    /// </summary>
    /// <typeparam name="T">The type to deserialize to.</typeparam>
    /// <param name="key">The variable key.</param>
    /// <returns>The variable value, or default(T) if not found.</returns>
    T? GetVariable<T>(string key);

    /// <summary>
    ///     Sets a variable value.
    /// </summary>
    /// <typeparam name="T">The type of the value.</typeparam>
    /// <param name="key">The variable key.</param>
    /// <param name="value">The value to store.</param>
    void SetVariable<T>(string key, T value);

    /// <summary>
    ///     Checks if a variable exists.
    /// </summary>
    /// <param name="key">The variable key.</param>
    /// <returns>True if the variable exists.</returns>
    bool VariableExists(string key);

    /// <summary>
    ///     Deletes a variable.
    /// </summary>
    /// <param name="key">The variable key.</param>
    void DeleteVariable(string key);

    /// <summary>
    ///     Gets all variable keys.
    /// </summary>
    /// <returns>Collection of variable keys.</returns>
    IEnumerable<string> GetVariableKeys();

    #endregion

    #region Bulk Operations

    /// <summary>
    ///     Sets multiple flags at once.
    /// </summary>
    /// <param name="flags">Dictionary of flag IDs to values.</param>
    void SetFlags(Dictionary<string, bool> flags);

    /// <summary>
    ///     Sets multiple variables at once.
    /// </summary>
    /// <param name="variables">Dictionary of variable keys to values.</param>
    void SetVariables<T>(Dictionary<string, T> variables);

    #endregion

    #region Entity-Specific Operations

    /// <summary>
    ///     Gets a flag value for a specific entity.
    /// </summary>
    /// <param name="entity">The entity.</param>
    /// <param name="flagId">The flag identifier.</param>
    /// <returns>True if the flag is set, false otherwise.</returns>
    bool GetEntityFlag(Entity entity, string flagId);

    /// <summary>
    ///     Sets a flag value for a specific entity.
    /// </summary>
    /// <param name="entity">The entity.</param>
    /// <param name="flagId">The flag identifier.</param>
    /// <param name="value">The value to set.</param>
    void SetEntityFlag(Entity entity, string flagId, bool value);

    /// <summary>
    ///     Gets a variable value for a specific entity.
    /// </summary>
    /// <typeparam name="T">The type to deserialize to.</typeparam>
    /// <param name="entity">The entity.</param>
    /// <param name="key">The variable key.</param>
    /// <returns>The variable value, or default(T) if not found.</returns>
    T? GetEntityVariable<T>(Entity entity, string key);

    /// <summary>
    ///     Sets a variable value for a specific entity.
    /// </summary>
    /// <typeparam name="T">The type of the value.</typeparam>
    /// <param name="entity">The entity.</param>
    /// <param name="key">The variable key.</param>
    /// <param name="value">The value to store.</param>
    void SetEntityVariable<T>(Entity entity, string key, T value);

    #endregion

    #region Metadata

    /// <summary>
    ///     Registers metadata for a flag.
    /// </summary>
    /// <param name="metadata">The flag metadata.</param>
    void RegisterFlagMetadata(FlagMetadata metadata);

    /// <summary>
    ///     Registers metadata for a variable.
    /// </summary>
    /// <param name="metadata">The variable metadata.</param>
    void RegisterVariableMetadata(VariableMetadata metadata);

    /// <summary>
    ///     Gets metadata for a flag.
    /// </summary>
    /// <param name="flagId">The flag identifier.</param>
    /// <returns>The flag metadata, or null if not found.</returns>
    FlagMetadata? GetFlagMetadata(string flagId);

    /// <summary>
    ///     Gets metadata for a variable.
    /// </summary>
    /// <param name="key">The variable key.</param>
    /// <returns>The variable metadata, or null if not found.</returns>
    VariableMetadata? GetVariableMetadata(string key);

    #endregion
}
