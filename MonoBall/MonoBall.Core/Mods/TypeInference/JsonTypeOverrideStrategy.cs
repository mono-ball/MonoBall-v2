namespace MonoBall.Core.Mods.TypeInference;

/// <summary>
/// Strategy 3: JSON $type field override (lazy parsing - only when needed).
/// Stateless singleton pattern for memory efficiency.
/// </summary>
public class JsonTypeOverrideStrategy : ITypeInferenceStrategy
{
    /// <summary>
    /// Singleton instance (strategies are stateless, safe to reuse).
    /// </summary>
    public static readonly JsonTypeOverrideStrategy Instance = new();

    private JsonTypeOverrideStrategy()
    {
        // Private constructor for singleton pattern
    }

    /// <inheritdoc />
    public string? InferType(TypeInferenceContext context)
    {
        var jsonDoc = context.JsonDocument;
        if (jsonDoc == null)
            return null; // JSON not available - skip this strategy

        if (jsonDoc.RootElement.TryGetProperty("$type", out var typeElement))
        {
            var explicitType = typeElement.GetString();
            if (!string.IsNullOrEmpty(explicitType))
            {
                context.Logger.Debug(
                    "Definition type inferred from JSON $type field: {Type} for {Path}",
                    explicitType,
                    context.FilePath
                );
                return explicitType;
            }
        }

        return null;
    }
}
