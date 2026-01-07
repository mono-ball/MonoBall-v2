using System;
using System.Collections.Generic;
using System.Linq;
using Arch.Core;
using MonoBall.Core.ECS.Components;
using MonoBall.Core.Mods.Definitions;
using MonoBall.Core.Scripting.Utilities;

namespace MonoBall.Core.Scripting.Services;

/// <summary>
///     Service for resolving script parameters from multiple sources.
///     Handles parameter merging, conversion, and validation.
/// </summary>
public static class ScriptParameterResolver
{
    /// <summary>
    ///     Gets default parameter values from a ScriptDefinition.
    /// </summary>
    /// <param name="scriptDef">The script definition.</param>
    /// <returns>Dictionary of parameter names to default values.</returns>
    public static Dictionary<string, object> GetDefaults(ScriptDefinition scriptDef)
    {
        var parameters = new Dictionary<string, object>();

        if (scriptDef.Parameters != null)
            foreach (var paramDef in scriptDef.Parameters)
                if (paramDef.DefaultValue != null)
                    parameters[paramDef.Name] = paramDef.DefaultValue;

        return parameters;
    }

    /// <summary>
    ///     Applies parameter overrides from a dictionary, converting values as needed.
    /// </summary>
    /// <param name="parameters">The current parameters dictionary (will be modified).</param>
    /// <param name="overrides">The overrides to apply.</param>
    /// <param name="scriptDef">The script definition (for type information).</param>
    public static void ApplyOverrides(
        Dictionary<string, object> parameters,
        Dictionary<string, object> overrides,
        ScriptDefinition scriptDef
    )
    {
        if (overrides == null || scriptDef.Parameters == null)
            return;

        foreach (var kvp in overrides)
        {
            var paramDef = scriptDef.Parameters.FirstOrDefault(p => p.Name == kvp.Key);
            var convertedValue = ScriptParameterConverter.ConvertParameterValue(
                kvp.Value,
                paramDef,
                kvp.Key,
                scriptDef.Id
            );
            parameters[kvp.Key] = convertedValue;
        }
    }

    /// <summary>
    ///     Applies parameter overrides from EntityVariablesComponent.
    /// </summary>
    /// <param name="parameters">The current parameters dictionary (will be modified).</param>
    /// <param name="entity">The entity with EntityVariablesComponent.</param>
    /// <param name="world">The ECS world.</param>
    /// <param name="scriptDef">The script definition.</param>
    public static void ApplyEntityVariableOverrides(
        Dictionary<string, object> parameters,
        Entity entity,
        World world,
        ScriptDefinition scriptDef
    )
    {
        if (!world.Has<EntityVariablesComponent>(entity) || scriptDef.Parameters == null)
            return;

        ref var variables = ref world.Get<EntityVariablesComponent>(entity);
        if (variables.Variables == null || variables.VariableTypes == null)
            return;

        foreach (var paramDef in scriptDef.Parameters)
        {
            var overrideKey = ScriptStateKeys.GetParameterKey(scriptDef.Id, paramDef.Name);
            if (variables.Variables.TryGetValue(overrideKey, out var overrideValue))
                // Deserialize based on type
                try
                {
                    var paramType = paramDef.Type.ToLowerInvariant();
                    object? deserializedValue = paramType switch
                    {
                        "string" => overrideValue,
                        "int" => int.Parse(overrideValue),
                        "float" => float.Parse(overrideValue),
                        "bool" => bool.Parse(overrideValue),
                        "vector2" => Vector2Parser.Parse(overrideValue),
                        _ => overrideValue, // Default: keep as string
                    };

                    if (deserializedValue != null)
                        parameters[paramDef.Name] = deserializedValue;
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException(
                        $"Failed to deserialize parameter override '{paramDef.Name}' for script '{scriptDef.Id}'. "
                            + $"Expected type: {paramDef.Type}. Error: {ex.Message}",
                        ex
                    );
                }
        }
    }

    /// <summary>
    ///     Validates parameters against ScriptDefinition constraints (type, min, max).
    /// </summary>
    /// <param name="parameters">The parameters to validate.</param>
    /// <param name="scriptDef">The script definition.</param>
    /// <exception cref="InvalidOperationException">Thrown if validation fails.</exception>
    public static void ValidateParameters(
        Dictionary<string, object> parameters,
        ScriptDefinition scriptDef
    )
    {
        if (scriptDef.Parameters == null)
            return;

        foreach (var paramDef in scriptDef.Parameters)
        {
            if (!parameters.TryGetValue(paramDef.Name, out var paramValue))
                continue; // Parameter not set, skip validation

            // Validate min/max bounds (if value is numeric)
            var numericValue = paramValue switch
            {
                int i => (double)i,
                float f => (double)f,
                double d => d,
                _ => (double?)null,
            };

            if (numericValue != null)
            {
                if (paramDef.Min != null && numericValue < paramDef.Min)
                    throw new InvalidOperationException(
                        $"Parameter '{paramDef.Name}' value '{paramValue}' is below minimum '{paramDef.Min}' for script '{scriptDef.Id}'. "
                            + $"Value must be >= {paramDef.Min}."
                    );

                if (paramDef.Max != null && numericValue > paramDef.Max)
                    throw new InvalidOperationException(
                        $"Parameter '{paramDef.Name}' value '{paramValue}' is above maximum '{paramDef.Max}' for script '{scriptDef.Id}'. "
                            + $"Value must be <= {paramDef.Max}."
                    );
            }
        }
    }

    /// <summary>
    ///     Validates parameter type matches stored type in EntityVariablesComponent.
    /// </summary>
    /// <param name="entity">The entity with EntityVariablesComponent.</param>
    /// <param name="world">The ECS world.</param>
    /// <param name="scriptDef">The script definition.</param>
    /// <exception cref="InvalidOperationException">Thrown if type mismatch is found.</exception>
    public static void ValidateEntityVariableTypes(
        Entity entity,
        World world,
        ScriptDefinition scriptDef
    )
    {
        if (!world.Has<EntityVariablesComponent>(entity) || scriptDef.Parameters == null)
            return;

        ref var variables = ref world.Get<EntityVariablesComponent>(entity);
        if (variables.Variables == null || variables.VariableTypes == null)
            return;

        foreach (var paramDef in scriptDef.Parameters)
        {
            var overrideKey = ScriptStateKeys.GetParameterKey(scriptDef.Id, paramDef.Name);
            if (variables.Variables.TryGetValue(overrideKey, out var overrideValue))
            {
                // Validate parameter type matches
                var expectedType = paramDef.Type.ToLowerInvariant();
                var actualType = variables.VariableTypes.TryGetValue(
                    overrideKey,
                    out var storedType
                )
                    ? storedType?.ToLowerInvariant()
                    : null;

                if (actualType != null && actualType != expectedType)
                    throw new InvalidOperationException(
                        $"Parameter '{paramDef.Name}' type mismatch for script '{scriptDef.Id}': "
                            + $"expected '{expectedType}', got '{actualType}'. "
                            + "Runtime parameter overrides must match ScriptDefinition parameter types."
                    );
            }
        }
    }
}
