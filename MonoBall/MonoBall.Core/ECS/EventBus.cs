using System;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading;

namespace MonoBall.Core.ECS;

/// <summary>
///     High-performance, thread-safe event bus for decoupled event communication.
/// </summary>
/// <remarks>
///     <para>
///         <b>Thread Safety:</b> This class is fully thread-safe using lock-free patterns:
///     </para>
///     <list type="bullet">
///         <item>ConcurrentDictionary for handler storage - thread-safe registration</item>
///         <item>Cached handler arrays - zero-allocation, lock-free publish path</item>
///         <item>Copy-on-write - subscriptions rebuild cache, publishes iterate snapshot</item>
///         <item>Main thread queue - safe cross-thread event dispatch</item>
///     </list>
///     <para>
///         <b>Performance Characteristics:</b>
///     </para>
///     <list type="bullet">
///         <item>Publish: O(n) handlers, zero allocations (hot path optimized)</item>
///         <item>Subscribe/Unsubscribe: O(n) cache rebuild (cold path, rare operation)</item>
///         <item>Cross-thread publish: O(1) queue, deferred execution on main thread</item>
///     </list>
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

    private static readonly ConcurrentDictionary<
        Type,
        ConcurrentDictionary<int, HandlerEntry>
    > _handlers = new();
    private static readonly ConcurrentDictionary<Type, HandlerCache> _cache = new();
    private static readonly ConcurrentQueue<Action> _mainThreadQueue = new();

    private static Thread? _mainThread;
    private static int _nextHandlerId;
    private static Action<string, Exception>? _errorHandler;

    /// <summary>
    ///     Initializes the event bus with the main thread reference.
    ///     Call this once from the main game thread during startup.
    /// </summary>
    public static void Initialize()
    {
        _mainThread = Thread.CurrentThread;
    }

    /// <summary>
    ///     Sets a custom error handler for exceptions in event handlers.
    ///     If not set, exceptions are written to Console.Error.
    /// </summary>
    /// <param name="handler">The error handler that receives event type name and exception.</param>
    public static void SetErrorHandler(Action<string, Exception> handler)
    {
        _errorHandler = handler;
    }

    /// <summary>
    ///     Subscribes to events of type T with a copy handler.
    /// </summary>
    /// <typeparam name="T">The event type.</typeparam>
    /// <param name="handler">The event handler that receives a copy of the event.</param>
    /// <returns>A disposable subscription that unsubscribes when disposed.</returns>
    public static IDisposable Subscribe<T>(Action<T> handler)
        where T : struct
    {
        ArgumentNullException.ThrowIfNull(handler);
        return SubscribeInternal<T>(new HandlerEntry(handler, isRef: false));
    }

    /// <summary>
    ///     Subscribes to events of type T with a ref handler.
    /// </summary>
    /// <typeparam name="T">The event type.</typeparam>
    /// <param name="handler">The event handler that receives the event by reference.</param>
    /// <returns>A disposable subscription that unsubscribes when disposed.</returns>
    public static IDisposable Subscribe<T>(RefAction<T> handler)
        where T : struct
    {
        ArgumentNullException.ThrowIfNull(handler);
        return SubscribeInternal<T>(new HandlerEntry(handler, isRef: true));
    }

    /// <summary>
    ///     Legacy subscribe method for compatibility. Prefer the IDisposable overload.
    /// </summary>
    [Obsolete("Use Subscribe<T>(Action<T>) which returns IDisposable for proper cleanup.")]
    public static void Subscribe<T>(Action<T> handler, out int handlerId)
        where T : struct
    {
        ArgumentNullException.ThrowIfNull(handler);
        var subscription = (Subscription)
            SubscribeInternal<T>(new HandlerEntry(handler, isRef: false));
        handlerId = subscription.HandlerId;
    }

    /// <summary>
    ///     Unsubscribes from events of type T using a handler reference.
    ///     Note: Prefer using the IDisposable returned from Subscribe for cleaner code.
    /// </summary>
    /// <typeparam name="T">The event type.</typeparam>
    /// <param name="handler">The event handler to remove.</param>
    public static void Unsubscribe<T>(Action<T> handler)
        where T : struct
    {
        UnsubscribeByDelegate<T>(handler);
    }

    /// <summary>
    ///     Unsubscribes from events of type T using a ref handler reference.
    /// </summary>
    /// <typeparam name="T">The event type.</typeparam>
    /// <param name="handler">The ref event handler to remove.</param>
    public static void Unsubscribe<T>(RefAction<T> handler)
        where T : struct
    {
        UnsubscribeByDelegate<T>(handler);
    }

    /// <summary>
    ///     Internal method to unsubscribe by delegate reference.
    /// </summary>
    private static void UnsubscribeByDelegate<T>(Delegate handler)
        where T : struct
    {
        var eventType = typeof(T);
        if (!_handlers.TryGetValue(eventType, out var handlers))
            return;

        foreach (var kvp in handlers)
        {
            if (ReferenceEquals(kvp.Value.Handler, handler))
            {
                handlers.TryRemove(kvp.Key, out _);
                InvalidateCache(eventType);
                return;
            }
        }
    }

    /// <summary>
    ///     Sends an event to all subscribers immediately on the current thread.
    ///     Thread-safe: can be called from any thread.
    /// </summary>
    /// <typeparam name="T">The event type.</typeparam>
    /// <param name="eventData">The event data passed by reference.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Send<T>(ref T eventData)
        where T : struct
    {
        var eventType = typeof(T);

        // Fast path: check cache for handlers
        if (!_cache.TryGetValue(eventType, out var cache) || cache.IsEmpty)
            return;

        // Create a copy for non-ref handlers
        var eventCopy = eventData;
        var handlers = cache.Handlers;

        // Iterate cached snapshot - safe even if subscriptions change
        for (var i = 0; i < handlers.Length; i++)
        {
            ref readonly var entry = ref handlers[i];
            try
            {
                if (entry.IsRef)
                {
                    ((RefAction<T>)entry.Handler)(ref eventData);
                }
                else
                {
                    ((Action<T>)entry.Handler)(eventCopy);
                }
            }
            catch (Exception ex)
            {
                LogHandlerError(eventType.Name, ex);
            }
        }
    }

    /// <summary>
    ///     Queues an event to be sent on the main thread.
    ///     Safe to call from any thread (background workers, async tasks, etc.).
    /// </summary>
    /// <typeparam name="T">The event type.</typeparam>
    /// <param name="eventData">The event data.</param>
    public static void SendOnMainThread<T>(T eventData)
        where T : struct
    {
        if (_mainThread != null && Thread.CurrentThread == _mainThread)
        {
            // Already on main thread - send immediately
            Send(ref eventData);
        }
        else
        {
            // Queue for main thread execution
            var captured = eventData;
            _mainThreadQueue.Enqueue(() =>
            {
                var data = captured;
                Send(ref data);
            });
        }
    }

    /// <summary>
    ///     Processes all queued main thread events.
    ///     Call this once per frame from your main game loop Update method.
    /// </summary>
    public static void ProcessMainThreadQueue()
    {
        while (_mainThreadQueue.TryDequeue(out var action))
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                LogHandlerError("MainThreadQueue", ex);
            }
        }
    }

    /// <summary>
    ///     Gets the number of subscribers for an event type.
    /// </summary>
    /// <typeparam name="T">The event type.</typeparam>
    /// <returns>The number of active subscribers.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int GetSubscriberCount<T>()
        where T : struct
    {
        if (_cache.TryGetValue(typeof(T), out var cache))
            return cache.Count;
        return 0;
    }

    /// <summary>
    ///     Clears all subscriptions for a specific event type.
    /// </summary>
    /// <typeparam name="T">The event type.</typeparam>
    public static void ClearSubscriptions<T>()
        where T : struct
    {
        var eventType = typeof(T);
        _handlers.TryRemove(eventType, out _);
        _cache.TryRemove(eventType, out _);
    }

    /// <summary>
    ///     Clears all subscriptions for all event types.
    ///     Use with caution - typically only for testing or shutdown.
    /// </summary>
    public static void ClearAllSubscriptions()
    {
        _handlers.Clear();
        _cache.Clear();
    }

    /// <summary>
    ///     Gets the number of pending main thread queue items.
    /// </summary>
    public static int MainThreadQueueCount => _mainThreadQueue.Count;

    private static IDisposable SubscribeInternal<T>(HandlerEntry entry)
        where T : struct
    {
        var eventType = typeof(T);
        var handlers = _handlers.GetOrAdd(
            eventType,
            _ => new ConcurrentDictionary<int, HandlerEntry>()
        );

        var handlerId = Interlocked.Increment(ref _nextHandlerId);
        handlers[handlerId] = entry;

        InvalidateCache(eventType);

        return new Subscription(eventType, handlerId);
    }

    internal static void Unsubscribe(Type eventType, int handlerId)
    {
        if (_handlers.TryGetValue(eventType, out var handlers))
        {
            handlers.TryRemove(handlerId, out _);
            InvalidateCache(eventType);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void InvalidateCache(Type eventType)
    {
        if (_handlers.TryGetValue(eventType, out var handlers))
        {
            // Build new cached array from current handlers
            var handlerArray = new HandlerEntry[handlers.Count];
            var index = 0;
            foreach (var kvp in handlers)
            {
                if (index < handlerArray.Length)
                    handlerArray[index++] = kvp.Value;
            }

            // Trim if concurrent modification changed count
            if (index < handlerArray.Length)
                Array.Resize(ref handlerArray, index);

            _cache[eventType] = new HandlerCache(handlerArray);
        }
        else
        {
            _cache.TryRemove(eventType, out _);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void LogHandlerError(string eventTypeName, Exception ex)
    {
        if (_errorHandler != null)
        {
            _errorHandler(eventTypeName, ex);
        }
        else
        {
            Console.Error.WriteLine($"[EventBus] Handler error for {eventTypeName}: {ex.Message}");
        }
    }

    /// <summary>
    ///     Internal handler entry storing delegate and type info.
    /// </summary>
    private readonly struct HandlerEntry
    {
        public readonly Delegate Handler;
        public readonly bool IsRef;

        public HandlerEntry(Delegate handler, bool isRef)
        {
            Handler = handler;
            IsRef = isRef;
        }
    }

    /// <summary>
    ///     Cached handler array with metadata for fast iteration.
    /// </summary>
    private sealed class HandlerCache
    {
        public readonly HandlerEntry[] Handlers;
        public readonly int Count;
        public readonly bool IsEmpty;

        public HandlerCache(HandlerEntry[] handlers)
        {
            Handlers = handlers;
            Count = handlers.Length;
            IsEmpty = Count == 0;
        }
    }

    /// <summary>
    ///     Disposable subscription handle for automatic cleanup.
    /// </summary>
    private sealed class Subscription : IDisposable
    {
        private readonly Type _eventType;
        internal readonly int HandlerId;
        private int _disposed;

        public Subscription(Type eventType, int handlerId)
        {
            _eventType = eventType;
            HandlerId = handlerId;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                EventBus.Unsubscribe(_eventType, HandlerId);
            }
        }
    }
}
