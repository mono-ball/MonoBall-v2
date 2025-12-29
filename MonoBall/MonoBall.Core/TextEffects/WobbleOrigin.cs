namespace MonoBall.Core.TextEffects;

/// <summary>
///     Defines the pivot point for wobble/rotation effects.
/// </summary>
public enum WobbleOrigin
{
    /// <summary>
    ///     Rotate around the center of the character.
    /// </summary>
    Center,

    /// <summary>
    ///     Rotate around the top of the character (like a hanging sign).
    /// </summary>
    Top,

    /// <summary>
    ///     Rotate around the bottom of the character.
    /// </summary>
    Bottom,
}
