namespace MonoBall.Core.Diagnostics.Console.Services;

using System;
using MonoBall.Core.Scenes.Systems;
using Serilog;

/// <summary>
/// Service for controlling game time from console commands.
/// Wraps SceneSystem for pause/resume/step functionality.
/// </summary>
public sealed class TimeControlService : ITimeControl
{
    private static readonly ILogger Logger = Log.ForContext<TimeControlService>();

    private readonly SceneSystem _sceneSystem;

    private float _timeScale = 1.0f;
    private int _pendingStepFrames;

    /// <inheritdoc />
    public bool IsPaused { get; private set; }

    /// <inheritdoc />
    public float TimeScale
    {
        get => _timeScale;
        set
        {
            _timeScale = Math.Clamp(value, 0f, 10f);
            Logger.Debug("Time scale set to {TimeScale}", _timeScale);
        }
    }

    /// <inheritdoc />
    public int PendingStepFrames => _pendingStepFrames;

    /// <summary>
    /// Initializes a new time control service.
    /// </summary>
    /// <param name="sceneSystem">The scene system for pause/resume control.</param>
    public TimeControlService(SceneSystem sceneSystem)
    {
        _sceneSystem = sceneSystem ?? throw new ArgumentNullException(nameof(sceneSystem));
    }

    /// <inheritdoc />
    public void Pause()
    {
        if (IsPaused)
            return;

        // Pause all active scenes
        _sceneSystem.IterateScenes(
            (entity, scene) =>
            {
                if (scene.IsActive && !scene.IsPaused)
                {
                    _sceneSystem.SetScenePaused(scene.SceneId, true);
                }
                return true; // Continue iteration
            }
        );

        IsPaused = true;
        Logger.Information("Game paused");
    }

    /// <inheritdoc />
    public void Resume()
    {
        if (!IsPaused)
            return;

        // Resume all paused scenes
        _sceneSystem.IterateScenes(
            (entity, scene) =>
            {
                if (scene.IsPaused)
                {
                    _sceneSystem.SetScenePaused(scene.SceneId, false);
                }
                return true; // Continue iteration
            }
        );

        IsPaused = false;
        _pendingStepFrames = 0;
        Logger.Information("Game resumed");
    }

    /// <inheritdoc />
    public bool Toggle()
    {
        if (IsPaused)
        {
            Resume();
            return false;
        }
        else
        {
            Pause();
            return true;
        }
    }

    /// <inheritdoc />
    public void Step(int frames = 1)
    {
        if (frames <= 0)
            frames = 1;

        if (!IsPaused)
        {
            Pause();
        }

        _pendingStepFrames += frames;
        Logger.Debug(
            "Stepping {Frames} frame(s), total pending: {Pending}",
            frames,
            _pendingStepFrames
        );
    }

    /// <summary>
    /// Consumes one step frame. Called by the game loop when stepping.
    /// </summary>
    /// <returns>True if a step frame was consumed, false if none pending.</returns>
    public bool ConsumeStepFrame()
    {
        if (_pendingStepFrames <= 0)
            return false;

        _pendingStepFrames--;
        return true;
    }
}
