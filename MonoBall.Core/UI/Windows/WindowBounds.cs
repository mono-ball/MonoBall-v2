namespace MonoBall.Core.UI.Windows;

/// <summary>
///     Represents the bounds of a UI window, including outer bounds and interior bounds.
///     All coordinates are in screen pixels (scaling is handled by the caller).
/// </summary>
public struct WindowBounds
{
    /// <summary>
    ///     Gets the outer X position (including border).
    /// </summary>
    public int OuterX { get; }

    /// <summary>
    ///     Gets the outer Y position (including border).
    /// </summary>
    public int OuterY { get; }

    /// <summary>
    ///     Gets the outer width (including border on both sides).
    /// </summary>
    public int OuterWidth { get; }

    /// <summary>
    ///     Gets the outer height (including border on top and bottom).
    /// </summary>
    public int OuterHeight { get; }

    /// <summary>
    ///     Gets the interior X position (content area, excluding border).
    /// </summary>
    public int InteriorX { get; }

    /// <summary>
    ///     Gets the interior Y position (content area, excluding border).
    /// </summary>
    public int InteriorY { get; }

    /// <summary>
    ///     Gets the interior width (content area width).
    /// </summary>
    public int InteriorWidth { get; }

    /// <summary>
    ///     Gets the interior height (content area height).
    /// </summary>
    public int InteriorHeight { get; }

    /// <summary>
    ///     Initializes a new instance of the WindowBounds structure.
    /// </summary>
    /// <param name="outerX">The outer X position (including border), in screen pixels.</param>
    /// <param name="outerY">The outer Y position (including border), in screen pixels.</param>
    /// <param name="outerWidth">The outer width (including border), in screen pixels.</param>
    /// <param name="outerHeight">The outer height (including border), in screen pixels.</param>
    /// <param name="interiorX">The interior X position (content area, excluding border), in screen pixels.</param>
    /// <param name="interiorY">The interior Y position (content area, excluding border), in screen pixels.</param>
    /// <param name="interiorWidth">The interior width (content area), in screen pixels.</param>
    /// <param name="interiorHeight">The interior height (content area), in screen pixels.</param>
    /// <remarks>
    ///     Both outer and interior coordinates are provided directly. This allows for non-uniform borders
    ///     (e.g., MessageBox has 2 tiles on left, 1 tile elsewhere). The caller is responsible for
    ///     calculating both coordinate sets based on their specific border requirements.
    /// </remarks>
    public WindowBounds(
        int outerX,
        int outerY,
        int outerWidth,
        int outerHeight,
        int interiorX,
        int interiorY,
        int interiorWidth,
        int interiorHeight
    )
    {
        OuterX = outerX;
        OuterY = outerY;
        OuterWidth = outerWidth;
        OuterHeight = outerHeight;
        InteriorX = interiorX;
        InteriorY = interiorY;
        InteriorWidth = interiorWidth;
        InteriorHeight = interiorHeight;
    }
}
