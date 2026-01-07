namespace MonoBall.Core.Mods.TypeInference;

/// <summary>
/// Strategy interface for type inference using Chain of Responsibility pattern.
/// </summary>
public interface ITypeInferenceStrategy
{
    /// <summary>
    /// Attempts to infer the definition type. Returns null if inference fails.
    /// </summary>
    string? InferType(TypeInferenceContext context);
}
