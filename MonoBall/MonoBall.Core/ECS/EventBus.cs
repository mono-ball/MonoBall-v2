using System;
using System.Collections.Generic;

namespace MonoBall.Core.ECS;

/// <summary>
///     Simple event bus for decoupled event communication.
///     This is a temporary implementation until Arch.EventBus is properly integrated.
/// </summary>
/// <remarks>
///     <para>
///         <b>Thread Safety:</b> This class is <b>not thread-safe</b> and is designed for single-threaded use.
///         MonoGame's game loop runs on a single thread, so thread safety is not required for normal operation.
///         If you need to publish events from multiple threads (e.g., background workers or async tasks),
///         you must ensure proper synchronization externally or use a thread-safe implementation.
///     </para>
///     <para>
///         The internal dictionary operations (Subscribe, Unsubscribe, Send) are not synchronized and may
///         cause data corruption if called concurrently from multiple threads.
///     </para>
/// </remarks>
public static class EventBus
{
    /// <summary>
    ///     Delegate type for event handlers that accept ref parameters.
    /// </summary>
    /// <typeparam name="T">The event type.</typeparam>
    /// <param name="eventData">The event data passed by reference.</param>
    public delegate void RefAction<T>(ref T eventData)
        where T : struct;

    private static readonly Dictionary<Type, List<Delegate>> _subscribers = new();

    /// <summary>
    ///     Subscribes to events of type T.
    /// </summary>
    /// <typeparam name="T">The event type.</typeparam>
    /// <param name="handler">The event handler.</param>
    public static void Subscribe<T>(Action<T> handler)
        where T : struct
    {
        var eventType = typeof(T);
        if (!_subscribers.ContainsKey(eventType))
            _subscribers[eventType] = new List<Delegate>();

        _subscribers[eventType].Add(handler);
    }

    /// <summary>
    ///     Subscribes to events of type T with ref parameter support.
    /// </summary>
    /// <typeparam name="T">The event type.</typeparam>
    /// <param name="handler">The event handler that accepts a ref parameter.</param>
    public static void Subscribe<T>(RefAction<T> handler)
        where T : struct
    {
        var eventType = typeof(T);
        if (!_subscribers.ContainsKey(eventType))
            _subscribers[eventType] = new List<Delegate>();

        _subscribers[eventType].Add(handler);
    }

    /// <summary>
    ///     Unsubscribes from events of type T.
    /// </summary>
    /// <typeparam name="T">The event type.</typeparam>
    /// <param name="handler">The event handler to remove.</param>
    public static void Unsubscribe<T>(Action<T> handler)
        where T : struct
    {
        var eventType = typeof(T);
        if (_subscribers.TryGetValue(eventType, out var handlers))
        {
            handlers.Remove(handler);
            if (handlers.Count == 0)
                _subscribers.Remove(eventType);
        }
    }

    /// <summary>
    ///     Unsubscribes from events of type T with ref parameter support.
    /// </summary>
    /// <typeparam name="T">The event type.</typeparam>
    /// <param name="handler">The event handler to remove.</param>
    public static void Unsubscribe<T>(RefAction<T> handler)
        where T : struct
    {
        var eventType = typeof(T);
        if (_subscribers.TryGetValue(eventType, out var handlers))
        {
            handlers.Remove(handler);
            if (handlers.Count == 0)
                _subscribers.Remove(eventType);
        }
    }

    /// <summary>
    ///     Sends an event to all subscribers.
    /// </summary>
    /// <typeparam name="T">The event type.</typeparam>
    /// <param name="eventData">The event data (passed by ref for struct events).</param>
    public static void Send<T>(ref T eventData)
        where T : struct
    {
        var eventType = typeof(T);
        if (!_subscribers.TryGetValue(eventType, out var handlers))
            return;

        // Create a copy for non-ref handlers
        var eventCopy = eventData;

        foreach (var handler in handlers)
            if (handler is RefAction<T> refHandler)
                refHandler(ref eventData);
            else if (handler is Action<T> actionHandler)
                actionHandler(eventCopy);
    }
}
