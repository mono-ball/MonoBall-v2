using System;
using System.Text.Json;
using MonoBall.Core.Mods.Definitions;
using MonoBall.Core.Scripting.Utilities;

namespace MonoBall.Core.Scripting.Services;

/// <summary>
///     Utility class for converting script parameter values to their expected types.
///     Provides consistent, fail-fast conversion with clear error messages.
/// </summary>
public static class ScriptParameterConverter
{
    /// <summary>
    ///     Converts a JSON parameter value to the expected C# type based on ScriptParameterDefinition.
    ///     Throws exceptions on conversion failure (fail fast).
    /// </summary>
    /// <param name="jsonValue">The JSON value to convert (can be JsonElement or already-deserialized).</param>
    /// <param name="paramDef">The parameter definition (null if parameter doesn't exist).</param>
    /// <param name="paramName">The parameter name (for error messages).</param>
    /// <param name="scriptId">The script definition ID (for error messages).</param>
    /// <returns>The converted value.</returns>
    /// <exception cref="InvalidOperationException">Thrown if paramDef is null or conversion fails.</exception>
    public static object ConvertParameterValue(
        object jsonValue,
        ScriptParameterDefinition? paramDef,
        string paramName,
        string scriptId
    )
    {
        if (paramDef == null)
            throw new InvalidOperationException(
                $"Parameter '{paramName}' not found in ScriptDefinition '{scriptId}'. "
                    + "Cannot convert parameter value - parameter does not exist."
            );

        try
        {
            var paramType = paramDef.Type.ToLowerInvariant();

            // Handle JsonElement from JSON deserialization
            if (jsonValue is JsonElement jsonElement)
                return ConvertJsonElement(jsonElement, paramType, paramName, scriptId);

            // Handle already-deserialized types
            return ConvertDeserializedValue(jsonValue, paramType, paramName, scriptId);
        }
        catch (Exception ex) when (!(ex is InvalidOperationException))
        {
            throw new InvalidOperationException(
                $"Failed to convert parameter '{paramName}' value '{jsonValue}' to type '{paramDef.Type}' for script '{scriptId}'. "
                    + $"Expected type: {paramDef.Type}. Error: {ex.Message}",
                ex
            );
        }
    }

    /// <summary>
    ///     Converts a JsonElement to the target type.
    /// </summary>
    private static object ConvertJsonElement(
        JsonElement jsonElement,
        string paramType,
        string paramName,
        string scriptId
    )
    {
        return paramType switch
        {
            "int" => jsonElement.ValueKind switch
            {
                JsonValueKind.Number => jsonElement.GetInt32(),
                JsonValueKind.String => int.Parse(
                    jsonElement.GetString()
                        ?? throw new FormatException("Cannot parse empty string as int")
                ),
                _ => throw new InvalidCastException(
                    $"Cannot convert JsonElement with ValueKind {jsonElement.ValueKind} to int for parameter '{paramName}' in script '{scriptId}'"
                ),
            },
            "float" => jsonElement.ValueKind switch
            {
                JsonValueKind.Number => jsonElement.GetSingle(),
                JsonValueKind.String => float.Parse(
                    jsonElement.GetString()
                        ?? throw new FormatException("Cannot parse empty string as float")
                ),
                _ => throw new InvalidCastException(
                    $"Cannot convert JsonElement with ValueKind {jsonElement.ValueKind} to float for parameter '{paramName}' in script '{scriptId}'"
                ),
            },
            "bool" => jsonElement.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.String => bool.Parse(
                    jsonElement.GetString()
                        ?? throw new FormatException("Cannot parse empty string as bool")
                ),
                _ => throw new InvalidCastException(
                    $"Cannot convert JsonElement with ValueKind {jsonElement.ValueKind} to bool for parameter '{paramName}' in script '{scriptId}'"
                ),
            },
            "string" => jsonElement.GetString() ?? string.Empty,
            "vector2" => jsonElement.ValueKind switch
            {
                JsonValueKind.String => Vector2Parser.Parse(
                    jsonElement.GetString()
                        ?? throw new FormatException("Cannot parse empty string as Vector2")
                ),
                _ => throw new InvalidCastException(
                    $"Cannot convert JsonElement with ValueKind {jsonElement.ValueKind} to Vector2 (must be string) for parameter '{paramName}' in script '{scriptId}'"
                ),
            },
            _ => throw new InvalidOperationException(
                $"Unknown parameter type '{paramType}' for parameter '{paramName}' in script '{scriptId}'"
            ),
        };
    }

    /// <summary>
    ///     Converts an already-deserialized value to the target type.
    /// </summary>
    private static object ConvertDeserializedValue(
        object jsonValue,
        string paramType,
        string paramName,
        string scriptId
    )
    {
        return paramType switch
        {
            "int" => jsonValue switch
            {
                int i => i,
                long l => (int)l,
                double d => (int)d,
                float f => (int)f,
                string s => int.Parse(s),
                _ => throw new InvalidCastException(
                    $"Cannot convert {jsonValue.GetType()} to int for parameter '{paramName}' in script '{scriptId}'"
                ),
            },
            "float" => jsonValue switch
            {
                float f => f,
                double d => (float)d,
                int i => i,
                long l => l,
                string s => float.Parse(s),
                _ => throw new InvalidCastException(
                    $"Cannot convert {jsonValue.GetType()} to float for parameter '{paramName}' in script '{scriptId}'"
                ),
            },
            "bool" => jsonValue switch
            {
                bool b => b,
                string s => bool.Parse(s),
                _ => throw new InvalidCastException(
                    $"Cannot convert {jsonValue.GetType()} to bool for parameter '{paramName}' in script '{scriptId}'"
                ),
            },
            "string" => jsonValue?.ToString() ?? string.Empty,
            "vector2" => jsonValue switch
            {
                string s => Vector2Parser.Parse(s),
                _ => throw new InvalidCastException(
                    $"Vector2 must be string format 'X,Y' for parameter '{paramName}' in script '{scriptId}'"
                ),
            },
            _ => throw new InvalidOperationException(
                $"Unknown parameter type '{paramType}' for parameter '{paramName}' in script '{scriptId}'"
            ),
        };
    }
}
