using Microsoft.Xna.Framework;

namespace MonoBall.Core.Scenes.Systems;

/// <summary>
///     Theme colors and styling constants for the loading screen.
///     Centralizes color definitions for maintainability and consistency.
/// </summary>
public static class LoadingScreenTheme
{
    /// <summary>
    ///     Background color for the loading screen (light gray theme).
    /// </summary>
    public static readonly Color BackgroundColor = new(234, 234, 233);

    /// <summary>
    ///     Background color for the progress bar (very light gray).
    /// </summary>
    public static readonly Color ProgressBarBackgroundColor = new(245, 245, 243);

    /// <summary>
    ///     Fill color for the progress bar (Pokéball red).
    /// </summary>
    public static readonly Color ProgressBarFillColor = new(235, 72, 60);

    /// <summary>
    ///     Highlight color for progress bar gradient effect (lighter red).
    /// </summary>
    public static readonly Color ProgressBarFillHighlight = new(255, 95, 82);

    /// <summary>
    ///     Border color for the progress bar (darker gray).
    /// </summary>
    public static readonly Color ProgressBarBorderColor = new(180, 180, 178);

    /// <summary>
    ///     Shadow color for progress bar (subtle gray with transparency).
    /// </summary>
    public static readonly Color ProgressBarShadowColor = new(200, 200, 198, 100);

    /// <summary>
    ///     Primary text color (dark for good contrast).
    /// </summary>
    public static readonly Color TextColor = new(30, 30, 33);

    /// <summary>
    ///     Secondary text color (medium gray).
    /// </summary>
    public static readonly Color TextSecondaryColor = new(70, 70, 75);

    /// <summary>
    ///     Shadow color for title text (gray with transparency).
    /// </summary>
    public static readonly Color TitleShadowColor = new(150, 150, 148, 120);

    /// <summary>
    ///     Error text color (Pokéball red).
    /// </summary>
    public static readonly Color ErrorColor = new(235, 72, 60);

    /// <summary>
    ///     Background color for error messages (light red).
    /// </summary>
    public static readonly Color ErrorBackgroundColor = new(255, 235, 235);
}
