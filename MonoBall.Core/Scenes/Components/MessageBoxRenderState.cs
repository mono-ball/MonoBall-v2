namespace MonoBall.Core.Scenes.Components;

/// <summary>
///     Rendering state for message box text printing.
/// </summary>
public enum MessageBoxRenderState
{
    /// <summary>
    ///     Handling current character (normal printing state).
    /// </summary>
    HandleChar,

    /// <summary>
    ///     Waiting for player input (A/B button press).
    ///     On resume, advances to next page (clears visible lines).
    /// </summary>
    Wait,

    /// <summary>
    ///     Waiting for player input, then scroll up by one line.
    ///     On resume, keeps previous line visible (scroll behavior).
    /// </summary>
    WaitForScroll,

    /// <summary>
    ///     Actively scrolling text upward (animation in progress).
    ///     Transitions to HandleChar when scroll animation completes.
    /// </summary>
    Scrolling,

    /// <summary>
    ///     Paused (for control codes like PAUSE).
    /// </summary>
    Paused,

    /// <summary>
    ///     Finished printing all text.
    /// </summary>
    Finished,

    /// <summary>
    ///     Hidden (not visible).
    /// </summary>
    Hidden,
}
