namespace MonoBall.Core.Diagnostics.UI;

using System;

/// <summary>
/// Base class for tracking metrics with circular history buffer.
/// Eliminates duplicate code between SystemProfiler and EventInspector.
/// </summary>
public class MetricsTracker
{
    private readonly double[] _history;
    private int _historyIndex;
    private int _historyCount;
    private float _timeSinceActive;

    /// <summary>
    /// Last recorded value.
    /// </summary>
    public double LastMs { get; private set; }

    /// <summary>
    /// Average value over history.
    /// </summary>
    public double AvgMs { get; private set; }

    /// <summary>
    /// Maximum value in history.
    /// </summary>
    public double MaxMs { get; private set; }

    /// <summary>
    /// Whether this tracker has received data recently.
    /// </summary>
    public bool IsActive => _timeSinceActive < 1.0f;

    /// <summary>
    /// Creates a new metrics tracker with specified history size.
    /// </summary>
    /// <param name="historySize">Number of samples to keep in history.</param>
    public MetricsTracker(int historySize = 60)
    {
        if (historySize <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(historySize),
                "History size must be positive."
            );

        _history = new double[historySize];
    }

    /// <summary>
    /// Records a timing measurement.
    /// </summary>
    /// <param name="elapsedMs">Elapsed time in milliseconds.</param>
    public void RecordTiming(double elapsedMs)
    {
        LastMs = elapsedMs;
        _timeSinceActive = 0;
        _history[_historyIndex] = elapsedMs;
        _historyIndex = (_historyIndex + 1) % _history.Length;
        _historyCount = Math.Min(_historyCount + 1, _history.Length);

        RecalculateStats();
    }

    /// <summary>
    /// Updates the activity timer. Call each frame.
    /// </summary>
    /// <param name="deltaTime">Time since last update.</param>
    public void UpdateActivityTimer(float deltaTime)
    {
        _timeSinceActive += deltaTime;
    }

    /// <summary>
    /// Resets all metrics and history.
    /// </summary>
    public void Reset()
    {
        Array.Clear(_history, 0, _history.Length);
        _historyIndex = 0;
        _historyCount = 0;
        _timeSinceActive = float.MaxValue;
        LastMs = 0;
        AvgMs = 0;
        MaxMs = 0;
    }

    private void RecalculateStats()
    {
        var sum = 0.0;
        MaxMs = 0;
        for (var i = 0; i < _historyCount; i++)
        {
            sum += _history[i];
            MaxMs = Math.Max(MaxMs, _history[i]);
        }
        AvgMs = _historyCount > 0 ? sum / _historyCount : 0;
    }
}
