using System;
using Arch.Core;
using MonoBall.Core.ECS;
using MonoBall.Core.ECS.Events;

namespace MonoBall.Core.Scripting.Runtime
{
    /// <summary>
    /// Wrapper for event subscriptions that implements IDisposable for cleanup.
    /// Handles error catching and ScriptErrorEvent firing.
    /// </summary>
    internal class EventSubscription<T> : IDisposable
        where T : struct
    {
        private readonly Action<T> _handler;
        private readonly string _scriptDefinitionId;
        private readonly Entity? _entity;
        private bool _disposed;

        /// <summary>
        /// Initializes a new instance of the EventSubscription class.
        /// </summary>
        /// <param name="handler">The event handler to wrap.</param>
        /// <param name="scriptDefinitionId">The script definition ID for error reporting.</param>
        /// <param name="entity">The entity this script is attached to (null for plugin scripts).</param>
        public EventSubscription(
            Action<T> handler,
            string scriptDefinitionId,
            Entity? entity = null
        )
        {
            _handler = handler ?? throw new ArgumentNullException(nameof(handler));
            _scriptDefinitionId =
                scriptDefinitionId ?? throw new ArgumentNullException(nameof(scriptDefinitionId));
            _entity = entity;
            EventBus.Subscribe<T>(OnEvent);
        }

        private void OnEvent(T eventData)
        {
            try
            {
                _handler(eventData);
            }
            catch (Exception ex)
            {
                // Fire ScriptErrorEvent
                var errorEvent = new ScriptErrorEvent
                {
                    Entity = _entity,
                    ScriptDefinitionId = _scriptDefinitionId,
                    Exception = ex,
                    ErrorMessage = ex.Message,
                    ErrorAt = DateTime.UtcNow,
                };
                EventBus.Send(ref errorEvent);
            }
        }

        /// <summary>
        /// Disposes the subscription and unsubscribes from the event bus.
        /// </summary>
        public void Dispose()
        {
            if (!_disposed)
            {
                EventBus.Unsubscribe<T>(OnEvent);
                _disposed = true;
            }
        }
    }

    /// <summary>
    /// Wrapper for ref event subscriptions that implements IDisposable for cleanup.
    /// Handles error catching and ScriptErrorEvent firing.
    /// </summary>
    internal class RefEventSubscription<T> : IDisposable
        where T : struct
    {
        private readonly EventBus.RefAction<T> _handler;
        private readonly string _scriptDefinitionId;
        private readonly Entity? _entity;
        private bool _disposed;

        /// <summary>
        /// Initializes a new instance of the RefEventSubscription class.
        /// </summary>
        /// <param name="handler">The ref event handler to wrap.</param>
        /// <param name="scriptDefinitionId">The script definition ID for error reporting.</param>
        /// <param name="entity">The entity this script is attached to (null for plugin scripts).</param>
        public RefEventSubscription(
            EventBus.RefAction<T> handler,
            string scriptDefinitionId,
            Entity? entity = null
        )
        {
            _handler = handler ?? throw new ArgumentNullException(nameof(handler));
            _scriptDefinitionId =
                scriptDefinitionId ?? throw new ArgumentNullException(nameof(scriptDefinitionId));
            _entity = entity;
            EventBus.Subscribe<T>(OnEvent);
        }

        private void OnEvent(ref T eventData)
        {
            try
            {
                _handler(ref eventData);
            }
            catch (Exception ex)
            {
                // Fire ScriptErrorEvent
                var errorEvent = new ScriptErrorEvent
                {
                    Entity = _entity,
                    ScriptDefinitionId = _scriptDefinitionId,
                    Exception = ex,
                    ErrorMessage = ex.Message,
                    ErrorAt = DateTime.UtcNow,
                };
                EventBus.Send(ref errorEvent);
            }
        }

        /// <summary>
        /// Disposes the subscription and unsubscribes from the event bus.
        /// </summary>
        public void Dispose()
        {
            if (!_disposed)
            {
                EventBus.Unsubscribe<T>(OnEvent);
                _disposed = true;
            }
        }
    }
}
