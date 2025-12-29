using System;
using System.Runtime.InteropServices;
using PortAudioSharp;
using Stream = PortAudioSharp.Stream;

namespace MonoBall.Core.Audio.Core;

/// <summary>
///     PortAudio-based audio output for cross-platform audio playback.
///     Manages a PortAudio stream and pulls samples from an ISampleProvider.
/// </summary>
public class PortAudioOutput : IDisposable
{
    private const int MaxDeviceErrorsBeforeRecovery = 5;
    private static bool _portAudioInitialized;
    private static int _portAudioRefCount;
    private static readonly object _initLock = new();
    private readonly int _bufferSizeFrames;
    private readonly AudioFormat _format;

    private readonly ISampleProvider _source;
    private readonly object _streamLock = new();
    private float[]? _callbackBuffer;
    private int _deviceErrorCount;
    private bool _disposed;
    private volatile bool _isPaused;
    private volatile bool _isPlaying;
    private Stream? _stream;

    /// <summary>
    ///     Creates a new PortAudio output.
    /// </summary>
    /// <param name="source">The sample provider to read audio from.</param>
    /// <param name="bufferSizeFrames">Buffer size in frames (default: 1024). Lower = less latency, higher = more stable.</param>
    public PortAudioOutput(ISampleProvider source, int bufferSizeFrames = 1024)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _format = source.Format;
        _bufferSizeFrames = bufferSizeFrames;

        EnsurePortAudioInitialized();
    }

    /// <summary>
    ///     Gets the current playback state.
    /// </summary>
    public PlaybackState PlaybackState
    {
        get
        {
            if (_disposed)
                return PlaybackState.Stopped;

            if (_isPaused)
                return PlaybackState.Paused;

            if (_isPlaying)
                return PlaybackState.Playing;

            return PlaybackState.Stopped;
        }
    }

    /// <summary>
    ///     Gets whether the current audio device is valid and functioning properly.
    /// </summary>
    public bool IsDeviceValid
    {
        get
        {
            lock (_streamLock)
            {
                if (_disposed || _stream == null)
                    return false;

                try
                {
                    // Check if stream is still active and device is still available
                    var defaultDevice = PortAudio.DefaultOutputDevice;
                    return defaultDevice != -1 && _deviceErrorCount < MaxDeviceErrorsBeforeRecovery;
                }
                catch
                {
                    return false;
                }
            }
        }
    }

    /// <summary>
    ///     Disposes the PortAudio output and releases resources.
    /// </summary>
    public void Dispose()
    {
        lock (_streamLock)
        {
            if (_disposed)
                return;

            _disposed = true;
            StopInternal(null);
            _callbackBuffer = null;
        }

        lock (_initLock)
        {
            _portAudioRefCount--;
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>
    ///     Event raised when playback stops (either normally or due to error).
    /// </summary>
    public event EventHandler<PlaybackStoppedEventArgs>? PlaybackStopped;

    /// <summary>
    ///     Event raised when a device error is detected (device disconnected, buffer issues, etc.).
    /// </summary>
    public event EventHandler<DeviceErrorEventArgs>? DeviceError;

    /// <summary>
    ///     Initializes the PortAudio stream and starts playback.
    /// </summary>
    public void Play()
    {
        lock (_streamLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_isPlaying && !_isPaused)
                return;

            if (_isPaused && _stream != null)
            {
                // Resume from pause
                _stream.Start();
                _isPaused = false;
                return;
            }

            // Create new stream
            var outputParams = new StreamParameters
            {
                device = PortAudio.DefaultOutputDevice,
                channelCount = _format.Channels,
                sampleFormat = SampleFormat.Float32,
                suggestedLatency = PortAudio
                    .GetDeviceInfo(PortAudio.DefaultOutputDevice)
                    .defaultLowOutputLatency,
            };

            // Pre-allocate callback buffer to avoid GC pressure
            var maxSamples = _bufferSizeFrames * _format.Channels;
            _callbackBuffer = new float[maxSamples];

            _stream = new Stream(
                null,
                outputParams,
                _format.SampleRate,
                (uint)_bufferSizeFrames,
                StreamFlags.ClipOff,
                AudioCallback,
                IntPtr.Zero
            );

            _stream.Start();
            _isPlaying = true;
            _isPaused = false;
        }
    }

    /// <summary>
    ///     Pauses playback without closing the stream.
    /// </summary>
    public void Pause()
    {
        lock (_streamLock)
        {
            if (_disposed || !_isPlaying || _isPaused)
                return;

            _stream?.Stop();
            _isPaused = true;
        }
    }

    /// <summary>
    ///     Stops playback and closes the stream.
    /// </summary>
    public void Stop()
    {
        lock (_streamLock)
        {
            StopInternal(null);
        }
    }

    private void StopInternal(Exception? exception)
    {
        if (!_isPlaying && !_isPaused)
            return;

        try
        {
            _stream?.Stop();
            _stream?.Dispose();
        }
        catch
        {
            // Ignore errors during cleanup
        }

        _stream = null;
        _isPlaying = false;
        _isPaused = false;

        PlaybackStopped?.Invoke(this, new PlaybackStoppedEventArgs(exception));
    }

    private StreamCallbackResult AudioCallback(
        IntPtr input,
        IntPtr output,
        uint frameCount,
        ref StreamCallbackTimeInfo timeInfo,
        StreamCallbackFlags statusFlags,
        IntPtr userData
    )
    {
        // CA1031: Audio callbacks can throw many exception types; recovering gracefully is critical
        // to avoid crashing the audio thread
#pragma warning disable CA1031
        try
        {
            // Check for device issues via status flags
            if (
                statusFlags.HasFlag(StreamCallbackFlags.OutputUnderflow)
                || statusFlags.HasFlag(StreamCallbackFlags.OutputOverflow)
            )
            {
                _deviceErrorCount++;

                var errorType = statusFlags.HasFlag(StreamCallbackFlags.OutputUnderflow)
                    ? DeviceErrorType.BufferUnderrun
                    : DeviceErrorType.BufferOverrun;

                DeviceError?.Invoke(this, new DeviceErrorEventArgs(errorType, _deviceErrorCount));

                // Attempt recovery if error count exceeds threshold
                if (_deviceErrorCount >= MaxDeviceErrorsBeforeRecovery)
                    // Signal recovery needed - will be handled outside callback
                    return StreamCallbackResult.Abort;
            }
            else
            {
                // Reset error count on successful callback
                if (_deviceErrorCount > 0)
                    _deviceErrorCount = Math.Max(0, _deviceErrorCount - 1);
            }

            var samplesNeeded = (int)frameCount * _format.Channels;

            // Reuse pre-allocated buffer to avoid GC pressure
            var buffer = _callbackBuffer;
            if (buffer == null || buffer.Length < samplesNeeded)
                // Fallback: allocate if buffer not initialized or too small
                buffer = new float[samplesNeeded];

            var samplesRead = _source.Read(buffer, 0, samplesNeeded);

            // Fill any remaining samples with silence
            if (samplesRead < samplesNeeded)
                Array.Clear(buffer, samplesRead, samplesNeeded - samplesRead);

            // Copy to output buffer using marshalling (safe code)
            Marshal.Copy(buffer, 0, output, samplesNeeded);

            return StreamCallbackResult.Continue;
        }
        catch (Exception)
        {
            _deviceErrorCount++;
            DeviceError?.Invoke(
                this,
                new DeviceErrorEventArgs(DeviceErrorType.Unknown, _deviceErrorCount)
            );
            return StreamCallbackResult.Abort;
        }
#pragma warning restore CA1031
    }

    /// <summary>
    ///     Attempts to recover the audio stream by recreating it on the current default device.
    /// </summary>
    /// <returns>True if recovery was successful, false otherwise.</returns>
    public bool TryRecoverStream()
    {
        lock (_streamLock)
        {
            if (_disposed)
                return false;

            try
            {
                var wasPlaying = _isPlaying;

                // Stop current stream
                StopInternal(null);

                // Reset error counter
                _deviceErrorCount = 0;

                // Verify a valid output device exists
                var defaultDevice = PortAudio.DefaultOutputDevice;
                if (defaultDevice == -1)
                {
                    DeviceError?.Invoke(
                        this,
                        new DeviceErrorEventArgs(DeviceErrorType.DeviceNotFound, 0)
                    );
                    return false;
                }

                // Restart if we were playing
                if (wasPlaying)
                {
                    Play();
                    return true;
                }

                return true;
            }
            catch (Exception ex)
            {
                DeviceError?.Invoke(
                    this,
                    new DeviceErrorEventArgs(DeviceErrorType.RecoveryFailed, _deviceErrorCount, ex)
                );
                return false;
            }
        }
    }

    private static void EnsurePortAudioInitialized()
    {
        lock (_initLock)
        {
            if (!_portAudioInitialized)
            {
                PortAudio.Initialize();
                _portAudioInitialized = true;
                _portAudioRefCount = 0;

                // Register for cleanup on process exit and domain unload
                EventHandler cleanupHandler = (s, e) =>
                {
                    lock (_initLock)
                    {
                        if (_portAudioInitialized)
                            try
                            {
                                PortAudio.Terminate();
                                _portAudioInitialized = false;
                            }
                            catch
                            {
                                // Ignore errors during shutdown
                            }
                    }
                };

                AppDomain.CurrentDomain.ProcessExit += cleanupHandler;
                AppDomain.CurrentDomain.DomainUnload += cleanupHandler;
            }

            _portAudioRefCount++;
        }
    }
}

/// <summary>
///     Playback state enumeration.
/// </summary>
public enum PlaybackState
{
    Stopped,
    Playing,
    Paused,
}

/// <summary>
///     Event args for playback stopped events.
/// </summary>
public class PlaybackStoppedEventArgs : EventArgs
{
    public PlaybackStoppedEventArgs(Exception? exception = null)
    {
        Exception = exception;
    }

    /// <summary>
    ///     Gets the exception that caused playback to stop, or null if stopped normally.
    /// </summary>
    public Exception? Exception { get; }
}

/// <summary>
///     Event args for device error events.
/// </summary>
public class DeviceErrorEventArgs : EventArgs
{
    public DeviceErrorEventArgs(
        DeviceErrorType errorType,
        int errorCount,
        Exception? exception = null
    )
    {
        ErrorType = errorType;
        ErrorCount = errorCount;
        Exception = exception;
    }

    /// <summary>
    ///     Gets the type of device error that occurred.
    /// </summary>
    public DeviceErrorType ErrorType { get; }

    /// <summary>
    ///     Gets the consecutive error count.
    /// </summary>
    public int ErrorCount { get; }

    /// <summary>
    ///     Gets the exception associated with the error, if any.
    /// </summary>
    public Exception? Exception { get; }
}

/// <summary>
///     Types of device errors that can occur during audio playback.
/// </summary>
public enum DeviceErrorType
{
    /// <summary>
    ///     Buffer underrun - audio data not provided fast enough.
    /// </summary>
    BufferUnderrun,

    /// <summary>
    ///     Buffer overrun - audio data provided too fast.
    /// </summary>
    BufferOverrun,

    /// <summary>
    ///     No audio output device found.
    /// </summary>
    DeviceNotFound,

    /// <summary>
    ///     Stream recovery attempt failed.
    /// </summary>
    RecoveryFailed,

    /// <summary>
    ///     Unknown or unspecified error.
    /// </summary>
    Unknown,
}
