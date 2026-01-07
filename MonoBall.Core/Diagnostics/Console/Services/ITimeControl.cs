namespace MonoBall.Core.Diagnostics.Console.Services;

/// <summary>
/// Interface for controlling game time from console commands.
/// </summary>
public interface ITimeControl
{
    /// <summary>
    /// Gets whether the game is currently paused.
    /// </summary>
    bool IsPaused { get; }

    /// <summary>
    /// Gets or sets the time scale (1.0 = normal, 0.5 = half speed, 2.0 = double).
    /// </summary>
    float TimeScale { get; set; }

    /// <summary>
    /// Gets the number of pending step frames (when stepping while paused).
    /// </summary>
    int PendingStepFrames { get; }

    /// <summary>
    /// Pauses the game.
    /// </summary>
    void Pause();

    /// <summary>
    /// Resumes the game from paused state.
    /// </summary>
    void Resume();

    /// <summary>
    /// Toggles between paused and resumed state.
    /// </summary>
    /// <returns>True if now paused, false if resumed.</returns>
    bool Toggle();

    /// <summary>
    /// Steps forward the specified number of frames while paused.
    /// </summary>
    /// <param name="frames">Number of frames to step (default 1).</param>
    void Step(int frames = 1);

    /// <summary>
    /// Consumes one step frame. Called by the game loop when stepping.
    /// </summary>
    /// <returns>True if a step frame was consumed, false if none pending.</returns>
    bool ConsumeStepFrame();
}
