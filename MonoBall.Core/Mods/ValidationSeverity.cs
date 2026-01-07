namespace MonoBall.Core.Mods;

/// <summary>
///     Severity level of a validation issue.
/// </summary>
public enum ValidationSeverity
{
    /// <summary>
    ///     Informational message.
    /// </summary>
    Info,

    /// <summary>
    ///     Warning that should be addressed but won't prevent loading.
    /// </summary>
    Warning,

    /// <summary>
    ///     Error that may prevent proper mod loading.
    /// </summary>
    Error,
}
