using System;

namespace MonoBall.Core.Mods.TypeInference;

/// <summary>
/// Strategy 2: Directory name inference (fast - no I/O).
/// Stateless singleton pattern for memory efficiency.
/// </summary>
public class DirectoryNameInferenceStrategy : ITypeInferenceStrategy
{
    /// <summary>
    /// Singleton instance (strategies are stateless, safe to reuse).
    /// </summary>
    public static readonly DirectoryNameInferenceStrategy Instance = new();

    private DirectoryNameInferenceStrategy()
    {
        // Private constructor for singleton pattern
    }

    /// <inheritdoc />
    public string? InferType(TypeInferenceContext context)
    {
        var normalizedPath = context.NormalizedPath;

        // Use Span-based parsing to avoid allocations
        if (normalizedPath.StartsWith("Definitions/Assets/", StringComparison.Ordinal))
        {
            var typeName = ExtractTypeNameFromPath(normalizedPath, startIndex: 19);
            if (typeName != null)
            {
                var inferredType = SingularizeTypeName(typeName) + "Asset";
                context.Logger.Debug(
                    "Definition type inferred from Assets directory: {Type} for {Path}",
                    inferredType,
                    context.FilePath
                );
                return inferredType;
            }
        }
        else if (
            normalizedPath.StartsWith("Definitions/Entities/", StringComparison.OrdinalIgnoreCase)
        )
        {
            var typeName = ExtractTypeNameFromPath(normalizedPath, startIndex: 20);
            if (typeName != null)
            {
                // Check for nested structure (e.g., Text/TextEffects)
                var nestedTypeName = ExtractNestedTypeName(normalizedPath, startIndex: 20);
                var inferredType = SingularizeTypeName(nestedTypeName ?? typeName);

                context.Logger.Debug(
                    "Definition type inferred from Entities directory: {Type} for {Path}",
                    inferredType,
                    context.FilePath
                );
                return inferredType;
            }
        }
        else if (normalizedPath.StartsWith("Definitions/", StringComparison.OrdinalIgnoreCase))
        {
            var typeName = ExtractTypeNameFromPath(normalizedPath, startIndex: 12);
            if (typeName != null)
            {
                var inferredType = SingularizeTypeName(typeName);
                context.Logger.Debug(
                    "Definition type inferred from directory: {Type} for {Path}",
                    inferredType,
                    context.FilePath
                );
                return inferredType;
            }
        }

        return null;
    }

    /// <summary>
    /// Extracts type name from path using Span-based parsing (no allocations).
    /// </summary>
    private static string? ExtractTypeNameFromPath(string path, int startIndex)
    {
        var span = path.AsSpan(startIndex);
        var slashIndex = span.IndexOf('/');
        if (slashIndex < 0)
            return null;

        return span.Slice(0, slashIndex).ToString();
    }

    /// <summary>
    /// Extracts nested type name (e.g., "TextEffects" from "Definitions/Entities/Text/TextEffects/...").
    /// </summary>
    private static string? ExtractNestedTypeName(string path, int startIndex)
    {
        var span = path.AsSpan(startIndex);
        var firstSlash = span.IndexOf('/');
        if (firstSlash < 0)
            return null;

        var secondSlash = span.Slice(firstSlash + 1).IndexOf('/');
        if (secondSlash < 0)
            return null;

        return span.Slice(firstSlash + 1, secondSlash).ToString();
    }

    /// <summary>
    /// Singularizes common plural type names for consistency.
    /// Kept as private static method per design document.
    /// </summary>
    private static string SingularizeTypeName(string typeName)
    {
        return typeName switch
        {
            "Quests" => "Quest",
            "Achievements" => "Achievement",
            "TextEffects" => "TextEffect",
            "ColorPalettes" => "ColorPalette",
            "WeatherEffects" => "WeatherEffect",
            _ => typeName,
        };
    }
}
