namespace MonoBall.Core.TextEffects;

/// <summary>
///     Determines how ColorCycle interacts with manual {COLOR} tags.
/// </summary>
public enum ColorEffectMode
{
    /// <summary>
    ///     ColorCycle completely replaces any manual {COLOR} setting.
    ///     The cycled color is used regardless of any prior color tags.
    /// </summary>
    Override,

    /// <summary>
    ///     ColorCycle multiplies with {COLOR} (tinting effect).
    ///     Result = cycleColor * manualColor / 255.
    ///     Allows cycling through shades of a base color.
    /// </summary>
    Tint,

    /// <summary>
    ///     ColorCycle is ignored if {COLOR} was explicitly set.
    ///     Only applies to characters without manual color.
    ///     Allows selective color override.
    /// </summary>
    Preserve,
}
