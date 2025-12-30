namespace Porycon3.Infrastructure;

/// <summary>
/// Shared string utility methods for name formatting and transformation.
/// </summary>
public static class StringUtilities
{
    /// <summary>
    /// Converts an underscore_separated name to PascalCase.
    /// Example: "tall_grass" -> "TallGrass"
    /// </summary>
    public static string ToPascalCase(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;
        return string.Concat(name.Split('_').Select(w =>
            w.Length > 0 ? char.ToUpper(w[0]) + w[1..].ToLower() : w));
    }

    /// <summary>
    /// Formats an underscore_separated name for display with spaces.
    /// Example: "tall_grass" -> "Tall Grass"
    /// </summary>
    public static string FormatDisplayName(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;
        return string.Join(" ", name.Split('_').Select(w =>
            w.Length > 0 ? char.ToUpper(w[0]) + w[1..].ToLower() : w));
    }

    /// <summary>
    /// Converts a SCREAMING_SNAKE_CASE constant to a normalized lowercase form.
    /// Example: "WEATHER_SUNNY" -> "sunny"
    /// </summary>
    public static string NormalizeConstant(string constant, string? prefix = null)
    {
        if (string.IsNullOrEmpty(constant)) return constant;

        var name = constant;
        if (prefix != null && name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            name = name[prefix.Length..];

        return name.ToLowerInvariant().TrimStart('_');
    }
}
