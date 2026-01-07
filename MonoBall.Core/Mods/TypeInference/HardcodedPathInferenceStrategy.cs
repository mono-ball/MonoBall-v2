using MonoBall.Core.Mods.Utilities;

namespace MonoBall.Core.Mods.TypeInference;

/// <summary>
/// Strategy 1: Hardcoded path mappings (fastest - no I/O).
/// Stateless singleton pattern for memory efficiency.
/// </summary>
public class HardcodedPathInferenceStrategy : ITypeInferenceStrategy
{
    /// <summary>
    /// Singleton instance (strategies are stateless, safe to reuse).
    /// </summary>
    public static readonly HardcodedPathInferenceStrategy Instance = new();

    private HardcodedPathInferenceStrategy()
    {
        // Private constructor for singleton pattern
    }

    /// <inheritdoc />
    public string? InferType(TypeInferenceContext context)
    {
        foreach (var (pathPattern, type) in KnownPathMappings.SortedMappings)
        {
            if (PathMatcher.MatchesPath(context.NormalizedPath, pathPattern))
            {
                context.Logger.Debug(
                    "Definition type inferred from hardcoded mapping: {Type} for {Path}",
                    type,
                    context.FilePath
                );
                return type;
            }
        }
        return null;
    }
}
