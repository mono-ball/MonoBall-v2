namespace MonoBall.Core.Scripting.Api
{
    /// <summary>
    /// API for showing message boxes from scripts.
    /// </summary>
    public interface IMessageBoxApi
    {
        /// <summary>
        /// Shows a message box with the specified text.
        /// </summary>
        /// <param name="text">The text to display.</param>
        /// <param name="textSpeedOverride">Optional text speed override in seconds per character (null = use player preference).</param>
        void ShowMessage(string text, float? textSpeedOverride = null);

        /// <summary>
        /// Hides the current message box.
        /// </summary>
        void HideMessage();

        /// <summary>
        /// Checks if a message box is currently visible.
        /// </summary>
        /// <returns>True if a message box is visible, false otherwise.</returns>
        bool IsMessageBoxVisible();
    }
}
