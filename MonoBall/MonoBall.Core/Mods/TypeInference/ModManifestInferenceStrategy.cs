using MonoBall.Core.Mods.Utilities;

namespace MonoBall.Core.Mods.TypeInference;

/// <summary>
/// Strategy 4: Mod manifest custom types (validation/documentation).
/// Stateless singleton pattern for memory efficiency.
/// </summary>
public class ModManifestInferenceStrategy : ITypeInferenceStrategy
{
    /// <summary>
    /// Singleton instance (strategies are stateless, safe to reuse).
    /// </summary>
    public static readonly ModManifestInferenceStrategy Instance = new();

    private ModManifestInferenceStrategy()
    {
        // Private constructor for singleton pattern
    }

    /// <inheritdoc />
    public string? InferType(TypeInferenceContext context)
    {
        if (context.Mod.CustomDefinitionTypes == null)
            return null;

        foreach (var (type, declaredPath) in context.Mod.CustomDefinitionTypes)
        {
            var normalizedDeclaredPath = ModPathNormalizer.Normalize(declaredPath);
            if (PathMatcher.MatchesPath(context.NormalizedPath, normalizedDeclaredPath))
            {
                context.Logger.Debug(
                    "Definition type inferred from mod.json customDefinitionTypes: {Type} for {Path}",
                    type,
                    context.FilePath
                );
                return type;
            }
        }

        return null;
    }
}
