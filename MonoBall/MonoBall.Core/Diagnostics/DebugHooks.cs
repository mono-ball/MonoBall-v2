namespace MonoBall.Core.Diagnostics;

using System;
using System.Collections.Generic;

/// <summary>
/// Subscription-based debug hook for system profiling.
/// Follows IDisposable pattern per .cursorrules requirements.
/// </summary>
public sealed class SystemTimingHook : IDisposable
{
    private static readonly List<SystemTimingHook> ActiveHooks = new();
    private readonly Action<string, double> _callback;
    private bool _disposed;

    private SystemTimingHook(Action<string, double> callback)
    {
        _callback = callback ?? throw new ArgumentNullException(nameof(callback));
    }

    /// <summary>
    /// Subscribes to system timing events.
    /// </summary>
    /// <param name="callback">Callback receiving system name and elapsed milliseconds.</param>
    /// <returns>Disposable subscription - MUST be disposed when done.</returns>
    public static SystemTimingHook Subscribe(Action<string, double> callback)
    {
        var hook = new SystemTimingHook(callback);
        lock (ActiveHooks)
        {
            ActiveHooks.Add(hook);
        }
        return hook;
    }

    /// <summary>
    /// Notifies all active hooks of a timing measurement.
    /// Called by SystemProfiler.
    /// </summary>
    internal static void Notify(string systemName, double elapsedMs)
    {
        lock (ActiveHooks)
        {
            foreach (var hook in ActiveHooks)
            {
                if (!hook._disposed)
                {
                    hook._callback(systemName, elapsedMs);
                }
            }
        }
    }

    /// <summary>
    /// Returns true if any hooks are subscribed (for performance - skip timing if no listeners).
    /// </summary>
    internal static bool HasSubscribers
    {
        get
        {
            lock (ActiveHooks)
            {
                return ActiveHooks.Count > 0;
            }
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        lock (ActiveHooks)
        {
            ActiveHooks.Remove(this);
        }
    }
}

/// <summary>
/// Subscription-based debug hook for event dispatch profiling.
/// Follows IDisposable pattern per .cursorrules requirements.
/// </summary>
public sealed class EventDispatchHook : IDisposable
{
    private static readonly List<EventDispatchHook> ActiveHooks = new();
    private readonly Action<string, int, double> _callback;
    private bool _disposed;

    private EventDispatchHook(Action<string, int, double> callback)
    {
        _callback = callback ?? throw new ArgumentNullException(nameof(callback));
    }

    /// <summary>
    /// Subscribes to event dispatch notifications.
    /// </summary>
    /// <param name="callback">Callback receiving event type, subscriber count, and elapsed milliseconds.</param>
    /// <returns>Disposable subscription - MUST be disposed when done.</returns>
    public static EventDispatchHook Subscribe(Action<string, int, double> callback)
    {
        var hook = new EventDispatchHook(callback);
        lock (ActiveHooks)
        {
            ActiveHooks.Add(hook);
        }
        return hook;
    }

    /// <summary>
    /// Notifies all active hooks of an event dispatch.
    /// Called by EventBus.
    /// </summary>
    internal static void Notify(string eventType, int subscriberCount, double elapsedMs)
    {
        lock (ActiveHooks)
        {
            foreach (var hook in ActiveHooks)
            {
                if (!hook._disposed)
                {
                    hook._callback(eventType, subscriberCount, elapsedMs);
                }
            }
        }
    }

    /// <summary>
    /// Returns true if any hooks are subscribed (for performance - skip timing if no listeners).
    /// </summary>
    internal static bool HasSubscribers
    {
        get
        {
            lock (ActiveHooks)
            {
                return ActiveHooks.Count > 0;
            }
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        lock (ActiveHooks)
        {
            ActiveHooks.Remove(this);
        }
    }
}
