using System.Text.RegularExpressions;

namespace Porycon3.Services;

/// <summary>
/// Transforms pokeemerald IDs to PokeSharp unified format.
/// Format: {namespace}:{type}:{category}/{name} or {namespace}:{type}:{category}/{subcategory}/{name}
/// </summary>
public static partial class IdTransformer
{
    private static string _namespace = "base";
    private const string DefaultRegion = "hoenn";

    /// <summary>
    /// The namespace prefix for all generated IDs (e.g., "base", "emerald-audio").
    /// Default is "base".
    /// </summary>
    public static string Namespace
    {
        get => _namespace;
        set => _namespace = value ?? "base";
    }

    /// <summary>
    /// Default region for region-scoped IDs.
    /// </summary>
    internal static string Region => DefaultRegion;

    /// <summary>
    /// Normalize a string to lowercase with underscores.
    /// Converts CamelCase to snake_case, handles floor suffixes.
    /// </summary>
    public static string Normalize(string value)
    {
        if (string.IsNullOrEmpty(value))
            return "";

        // Convert CamelCase to snake_case
        var s1 = Regex.Replace(value, "(.)([A-Z][a-z]+)", "$1_$2");
        var s2 = Regex.Replace(s1, "([a-z0-9])([A-Z])", "$1_$2");

        // Replace spaces and hyphens with underscores
        var s3 = Regex.Replace(s2, @"[\s\-]+", "_");

        // Remove non-alphanumeric except underscore
        var s4 = Regex.Replace(s3.ToLowerInvariant(), @"[^a-z0-9_]", "");

        // Collapse multiple underscores
        var s5 = Regex.Replace(s4, @"_+", "_");

        // Remove leading/trailing underscores
        var s6 = s5.Trim('_');

        // Fix floor suffixes: _1_f -> _1f, _b1_f -> _b1f
        var s7 = Regex.Replace(s6, @"_(\d+)_([fr])($|_)", "_$1$2$3");
        var s8 = Regex.Replace(s7, @"_b(\d+)_([fr])($|_)", "_b$1$2$3");

        return s8;
    }

    internal static string CreateId(string entityType, string category, string name, string? subcategory = null)
    {
        entityType = Normalize(entityType);
        category = Normalize(category);
        name = Normalize(name);

        if (!string.IsNullOrEmpty(subcategory))
        {
            subcategory = Normalize(subcategory);
            return $"{Namespace}:{entityType}:{category}/{subcategory}/{name}";
        }

        return $"{Namespace}:{entityType}:{category}/{name}";
    }

    /// <summary>
    /// Convert a string to PascalCase.
    /// Handles snake_case (ever_grande -> EverGrande), kebab-case (ever-grande -> EverGrande),
    /// space-separated (ever grande -> EverGrande), and already PascalCase names.
    /// </summary>
    public static string ToPascalCase(string value)
    {
        if (string.IsNullOrEmpty(value))
            return "";

        // If already PascalCase (starts with uppercase, no underscores/hyphens/spaces), return as-is
        if (char.IsUpper(value[0]) && !value.Contains('_') && !value.Contains('-') && !value.Contains(' '))
            return value;

        // Split on underscores, hyphens, and spaces, then capitalize each word
        var parts = value.Split(new[] { '_', '-', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        var result = new System.Text.StringBuilder();

        foreach (var part in parts)
        {
            if (part.Length > 0)
            {
                result.Append(char.ToUpperInvariant(part[0]));
                if (part.Length > 1)
                    result.Append(part.Substring(1).ToLowerInvariant());
            }
        }

        return result.ToString();
    }

    /// <summary>
    /// Convert a string to snake_case.
    /// Handles PascalCase (EverGrande -> ever_grande) and already snake_case names.
    /// </summary>
    public static string ToSnakeCase(string value)
    {
        if (string.IsNullOrEmpty(value))
            return "";

        // If already snake_case (lowercase with underscores or single word), return as-is
        if (value == value.ToLowerInvariant())
            return value;

        // Convert PascalCase to snake_case
        var result = new System.Text.StringBuilder();
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (i > 0 && char.IsUpper(c))
                result.Append('_');
            result.Append(char.ToLowerInvariant(c));
        }

        return result.ToString();
    }
}
