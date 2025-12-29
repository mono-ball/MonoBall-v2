using System;
using System.Collections.Generic;
using MonoBall.Core.ECS.Components;
using Serilog;

namespace MonoBall.Core.ECS.Services;

/// <summary>
///     Circular buffer for input commands, allowing buffering of inputs like Pokemon games.
///     Stores recent inputs for a short time window (typically 200ms) so players can
///     queue up the next movement before the current movement completes.
/// </summary>
/// <remarks>
///     Pokemon games use input buffering to make movement feel more responsive.
///     If a player presses a direction key slightly before movement completes,
///     the input is buffered and consumed when ready, eliminating the need for
///     precise timing.
///     Matches MonoBall's InputBuffer behavior.
/// </remarks>
public class InputBuffer
{
    private readonly Queue<InputCommand> _buffer;
    private readonly float _bufferTimeoutSeconds;
    private readonly ILogger _logger;
    private readonly int _maxBufferSize;

    /// <summary>
    ///     Initializes a new instance of the InputBuffer class.
    /// </summary>
    /// <param name="logger">The logger for logging operations.</param>
    /// <param name="maxSize">Maximum number of inputs to buffer (default: 5).</param>
    /// <param name="timeoutSeconds">How long inputs remain valid in seconds (default: 0.2s = 200ms).</param>
    public InputBuffer(ILogger logger, int maxSize = 5, float timeoutSeconds = 0.2f)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _buffer = new Queue<InputCommand>(maxSize);
        _maxBufferSize = maxSize;
        _bufferTimeoutSeconds = timeoutSeconds;
    }

    /// <summary>
    ///     Gets the number of inputs currently in the buffer.
    /// </summary>
    public int Count => _buffer.Count;

    /// <summary>
    ///     Adds an input command to the buffer if space is available.
    ///     Automatically removes expired inputs before adding.
    /// </summary>
    /// <param name="direction">The direction to buffer.</param>
    /// <param name="currentTime">Current game time in seconds.</param>
    /// <returns>True if the input was added; false if buffer is full or direction is None.</returns>
    public bool AddInput(Direction direction, float currentTime)
    {
        // Ignore None direction
        if (direction == Direction.None)
            return false;

        // Remove expired inputs
        RemoveExpiredInputs(currentTime);

        // Check if buffer has space
        if (_buffer.Count >= _maxBufferSize)
        {
            _logger.Debug(
                "InputBuffer overflow: buffer is full ({BufferSize}/{MaxSize}), dropping input {Direction}",
                _buffer.Count,
                _maxBufferSize,
                direction
            );
            return false;
        }

        // Add new input
        var command = new InputCommand(direction, currentTime);
        _buffer.Enqueue(command);
        return true;
    }

    /// <summary>
    ///     Attempts to consume the oldest input from the buffer.
    ///     Automatically removes expired inputs before consuming.
    /// </summary>
    /// <param name="currentTime">Current game time in seconds.</param>
    /// <param name="direction">The direction from the consumed input, or None if buffer is empty.</param>
    /// <returns>True if an input was consumed; false if buffer is empty.</returns>
    public bool TryConsumeInput(float currentTime, out Direction direction)
    {
        // Remove expired inputs
        RemoveExpiredInputs(currentTime);

        // Try to consume oldest input
        if (_buffer.Count > 0)
        {
            var command = _buffer.Dequeue();
            direction = command.Direction;
            return true;
        }

        direction = Direction.None;
        return false;
    }

    /// <summary>
    ///     Peeks at the oldest input without consuming it.
    ///     Automatically removes expired inputs before peeking.
    /// </summary>
    /// <param name="currentTime">Current game time in seconds.</param>
    /// <param name="direction">The direction of the oldest input, or None if buffer is empty.</param>
    /// <returns>True if there is a buffered input; false if buffer is empty.</returns>
    public bool TryPeekInput(float currentTime, out Direction direction)
    {
        // Remove expired inputs
        RemoveExpiredInputs(currentTime);

        // Try to peek at oldest input
        if (_buffer.Count > 0)
        {
            var command = _buffer.Peek();
            direction = command.Direction;
            return true;
        }

        direction = Direction.None;
        return false;
    }

    /// <summary>
    ///     Clears all buffered inputs.
    /// </summary>
    public void Clear()
    {
        _buffer.Clear();
    }

    /// <summary>
    ///     Removes all expired inputs from the buffer based on the timeout setting.
    /// </summary>
    /// <param name="currentTime">Current game time in seconds.</param>
    private void RemoveExpiredInputs(float currentTime)
    {
        while (_buffer.Count > 0)
        {
            var command = _buffer.Peek();
            var age = currentTime - command.Timestamp;

            if (age > _bufferTimeoutSeconds)
                _buffer.Dequeue(); // Remove expired input
            else
                break; // Queue is ordered by time, so we can stop here
        }
    }
}
