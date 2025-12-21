namespace MonoBall.Core.Mods;

/// <summary>
/// Represents a validation issue found during mod validation.
/// </summary>
public class ValidationIssue
{
    /// <summary>
    /// Gets or sets the severity of the issue.
    /// </summary>
    public ValidationSeverity Severity { get; set; }

    /// <summary>
    /// Gets or sets the validation message.
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the mod ID this issue relates to, if applicable.
    /// </summary>
    public string ModId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the file path this issue relates to, if applicable.
    /// </summary>
    public string FilePath { get; set; } = string.Empty;
}
