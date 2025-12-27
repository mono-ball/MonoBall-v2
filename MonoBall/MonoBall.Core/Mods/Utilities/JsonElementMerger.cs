using System;
using System.Collections.Generic;
using System.Text.Json;

namespace MonoBall.Core.Mods.Utilities;

/// <summary>
/// Utility class for merging JsonElement objects.
/// </summary>
internal static class JsonElementMerger
{
    /// <summary>
    /// Merges two JSON elements, with the second taking precedence for overlapping properties.
    /// </summary>
    /// <param name="baseElement">The base JSON element.</param>
    /// <param name="overrideElement">The override JSON element.</param>
    /// <param name="extend">If true, merges nested objects recursively. If false, replaces them.</param>
    /// <returns>The merged JSON element.</returns>
    public static JsonElement Merge(
        JsonElement baseElement,
        JsonElement overrideElement,
        bool extend
    )
    {
        if (
            baseElement.ValueKind != JsonValueKind.Object
            || overrideElement.ValueKind != JsonValueKind.Object
        )
        {
            return overrideElement; // Can't merge non-objects, use override
        }

        var merged = new Dictionary<string, JsonElement>();

        // Add all base properties
        foreach (var prop in baseElement.EnumerateObject())
        {
            if (prop.Name != "$operation") // Skip operation metadata
            {
                merged[prop.Name] = prop.Value.Clone(); // Clone to avoid disposal issues
            }
        }

        // Override or extend with new properties
        foreach (var prop in overrideElement.EnumerateObject())
        {
            if (prop.Name == "$operation")
            {
                continue; // Skip operation metadata
            }

            if (merged.ContainsKey(prop.Name) && extend)
            {
                // For extend, try to merge nested objects
                if (
                    merged[prop.Name].ValueKind == JsonValueKind.Object
                    && prop.Value.ValueKind == JsonValueKind.Object
                )
                {
                    merged[prop.Name] = Merge(merged[prop.Name], prop.Value, true);
                }
                else
                {
                    merged[prop.Name] = prop.Value.Clone(); // Override for non-objects
                }
            }
            else if (
                merged.ContainsKey(prop.Name)
                && merged[prop.Name].ValueKind == JsonValueKind.Object
                && prop.Value.ValueKind == JsonValueKind.Object
            )
            {
                // For modify operation (extend=false), merge nested objects recursively
                merged[prop.Name] = Merge(merged[prop.Name], prop.Value, false);
            }
            else
            {
                merged[prop.Name] = prop.Value.Clone();
            }
        }

        // Convert back to JsonElement
        var jsonString = JsonSerializer.Serialize(merged);
        using var doc = JsonDocument.Parse(jsonString);
        return doc.RootElement.Clone();
    }
}
