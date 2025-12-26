using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using Arch.Core;
using MonoBall.Core.ECS;
using MonoBall.Core.ECS.Components;
using MonoBall.Core.ECS.Events;
using MonoBall.Core.ECS.Services;
using MonoBall.Core.Scripting.Utilities;
using ScriptTimerData = MonoBall.Core.ECS.Components.ScriptTimerData;

namespace MonoBall.Core.Scripting.Runtime
{
    /// <summary>
    /// Base class for all MonoBall scripts.
    /// Provides event-driven architecture with automatic subscription cleanup.
    /// </summary>
    public abstract class ScriptBase : IDisposable
    {
        private readonly List<IDisposable> _subscriptions = new();
        private string? _scriptDefinitionId;
        private static readonly ConcurrentDictionary<Type, PropertyInfo?> _entityPropertyCache =
            new();

        /// <summary>
        /// Gets the script execution context (set during initialization).
        /// </summary>
        protected ScriptContext Context { get; private set; } = null!;

        /// <summary>
        /// Called once when the script is initialized.
        /// Override to set up initial state.
        /// </summary>
        /// <param name="context">The script execution context.</param>
        public virtual void Initialize(ScriptContext context)
        {
            Context = context ?? throw new ArgumentNullException(nameof(context));
            _scriptDefinitionId = context.ScriptDefinitionId;
        }

        /// <summary>
        /// Called after Initialize to register event handlers.
        /// Override to subscribe to game events.
        /// </summary>
        /// <param name="context">The script execution context.</param>
        public virtual void RegisterEventHandlers(ScriptContext context)
        {
            // Default: no event handlers
        }

        /// <summary>
        /// Called when the script is unloaded or entity is destroyed.
        /// Automatically cleans up event subscriptions.
        /// </summary>
        public virtual void OnUnload()
        {
            // Cleanup all event subscriptions
            foreach (var subscription in _subscriptions)
            {
                subscription.Dispose();
            }
            _subscriptions.Clear();
        }

        /// <summary>
        /// Subscribes to an event with automatic cleanup.
        /// </summary>
        /// <typeparam name="TEvent">The event type.</typeparam>
        /// <param name="handler">The event handler.</param>
        protected void On<TEvent>(Action<TEvent> handler)
            where TEvent : struct
        {
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            if (_scriptDefinitionId == null)
            {
                throw new InvalidOperationException(
                    "Script must be initialized before subscribing to events"
                );
            }

            var subscription = new EventSubscription<TEvent>(
                handler,
                _scriptDefinitionId,
                Context?.Entity
            );
            _subscriptions.Add(subscription);
        }

        /// <summary>
        /// Subscribes to an event with ref parameter support and automatic cleanup.
        /// </summary>
        /// <typeparam name="TEvent">The event type.</typeparam>
        /// <param name="handler">The ref event handler.</param>
        protected void On<TEvent>(EventBus.RefAction<TEvent> handler)
            where TEvent : struct
        {
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            if (_scriptDefinitionId == null)
            {
                throw new InvalidOperationException(
                    "Script must be initialized before subscribing to events"
                );
            }

            var subscription = new RefEventSubscription<TEvent>(
                handler,
                _scriptDefinitionId,
                Context?.Entity
            );
            _subscriptions.Add(subscription);
        }

        /// <summary>
        /// Gets a state value that persists across hot-reloads.
        /// State is stored in EntityVariablesComponent.
        /// </summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="key">The state key.</param>
        /// <param name="defaultValue">The default value if not found.</param>
        /// <returns>The state value, or defaultValue if not found.</returns>
        protected T Get<T>(string key, T defaultValue = default!)
        {
            if (Context == null)
            {
                return defaultValue;
            }

            if (Context.Entity == null)
            {
                // Plugin scripts - use global variables
                return Context.Apis.Flags.GetVariable<T>(GetStateKey(key)) ?? defaultValue;
            }

            // Entity scripts - use entity variables
            return Context.Apis.Flags.GetEntityVariable<T>(Context.Entity.Value, GetStateKey(key))
                ?? defaultValue;
        }

        /// <summary>
        /// Sets a state value that persists across hot-reloads.
        /// State is stored in EntityVariablesComponent.
        /// </summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="key">The state key.</param>
        /// <param name="value">The value to store.</param>
        protected void Set<T>(string key, T value)
        {
            if (Context == null)
            {
                return;
            }

            var stateKey = GetStateKey(key);

            if (Context.Entity == null)
            {
                // Plugin scripts - use global variables
                Context.Apis.Flags.SetVariable(stateKey, value);
            }
            else
            {
                // Entity scripts - use entity variables
                Context.Apis.Flags.SetEntityVariable(Context.Entity.Value, stateKey, value);
            }
        }

        /// <summary>
        /// Publishes an event to the event bus.
        /// </summary>
        /// <typeparam name="TEvent">The event type.</typeparam>
        /// <param name="evt">The event data (passed by ref).</param>
        protected void Publish<TEvent>(ref TEvent evt)
            where TEvent : struct
        {
            EventBus.Send(ref evt);
        }

        /// <summary>
        /// Tries to get a component value from the attached entity.
        /// Returns false if the component doesn't exist or this is a plugin script.
        /// </summary>
        /// <typeparam name="T">The component type.</typeparam>
        /// <param name="component">When this method returns, contains the component value if found; otherwise, the default value.</param>
        /// <returns>True if the component was found, false otherwise.</returns>
        protected bool TryGetComponent<T>(out T component)
            where T : struct
        {
            if (Context == null || Context.Entity == null || !Context.HasComponent<T>())
            {
                component = default;
                return false;
            }

            component = Context.GetComponent<T>();
            return true;
        }

        /// <summary>
        /// Requires that this script is attached to an entity.
        /// Throws an exception if this is a plugin script (no entity).
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown if this is a plugin script (no entity).</exception>
        protected void RequireEntity()
        {
            if (Context == null || !Context.Entity.HasValue)
            {
                throw new InvalidOperationException(
                    "This operation requires an entity-attached script."
                );
            }
        }

        /// <summary>
        /// Checks if an event belongs to this script's entity.
        /// Uses cached reflection to check for Entity property.
        /// </summary>
        /// <typeparam name="TEvent">The event type.</typeparam>
        /// <param name="evt">The event to check.</param>
        /// <returns>True if the event belongs to this entity, false otherwise.</returns>
        protected bool IsEventForThisEntity<TEvent>(TEvent evt)
            where TEvent : struct
        {
            if (!Context.Entity.HasValue)
                return false;

            var eventType = typeof(TEvent);
            var entityProp = _entityPropertyCache.GetOrAdd(
                eventType,
                t => t.GetProperty("Entity", BindingFlags.Public | BindingFlags.Instance)
            );

            if (entityProp == null)
                return false;

            var eventEntityValue = entityProp.GetValue(evt);
            if (eventEntityValue == null)
                return false;

            var eventEntity = (Entity)eventEntityValue;
            return eventEntity.Id == Context.Entity.Value.Id;
        }

        /// <summary>
        /// Checks if an event (passed by ref) belongs to this script's entity.
        /// Uses cached reflection to check for Entity property.
        /// </summary>
        /// <typeparam name="TEvent">The event type.</typeparam>
        /// <param name="evt">The event to check (passed by ref).</param>
        /// <returns>True if the event belongs to this entity, false otherwise.</returns>
        protected bool IsEventForThisEntity<TEvent>(ref TEvent evt)
            where TEvent : struct
        {
            if (!Context.Entity.HasValue)
                return false;

            var eventType = typeof(TEvent);
            var entityProp = _entityPropertyCache.GetOrAdd(
                eventType,
                t => t.GetProperty("Entity", BindingFlags.Public | BindingFlags.Instance)
            );

            if (entityProp == null)
                return false;

            // Copy the struct so we can use reflection (can't use reflection directly on ref parameters)
            TEvent evtCopy = evt;
            object boxedEvt = evtCopy;
            var eventEntityValue = entityProp.GetValue(boxedEvt);
            if (eventEntityValue == null)
                return false;

            var eventEntity = (Entity)eventEntityValue;
            return eventEntity.Id == Context.Entity.Value.Id;
        }

        /// <summary>
        /// Checks if a timer event belongs to this entity and matches the specified timer ID.
        /// </summary>
        /// <param name="timerId">The timer ID to check.</param>
        /// <param name="evt">The timer elapsed event.</param>
        /// <returns>True if the event is for this entity and matches the timer ID, false otherwise.</returns>
        protected bool IsTimerEvent(string timerId, TimerElapsedEvent evt)
        {
            return IsEventForThisEntity(evt) && evt.TimerId == timerId;
        }

        /// <summary>
        /// Gets the facing direction from the attached entity's GridMovement component.
        /// Delegates to INpcApi.GetFacingDirection.
        /// </summary>
        /// <returns>The facing direction.</returns>
        /// <exception cref="InvalidOperationException">Thrown if this is a plugin script or entity doesn't have GridMovement component.</exception>
        protected Direction GetFacingDirection()
        {
            RequireEntity();
            var direction = Context.Apis.Npc.GetFacingDirection(Context.Entity.Value);
            if (!direction.HasValue)
            {
                throw new InvalidOperationException("Entity does not have GridMovement component.");
            }

            return direction.Value;
        }

        /// <summary>
        /// Tries to get the facing direction from the attached entity's GridMovement component.
        /// Delegates to INpcApi.GetFacingDirection.
        /// </summary>
        /// <returns>The facing direction, or null if no entity or component missing.</returns>
        protected Direction? TryGetFacingDirection()
        {
            if (!Context.Entity.HasValue)
                return null;

            return Context.Apis.Npc.GetFacingDirection(Context.Entity.Value);
        }

        /// <summary>
        /// Sets the facing direction on the attached entity's GridMovement component.
        /// Delegates to INpcApi.FaceDirection.
        /// </summary>
        /// <param name="direction">The direction to face.</param>
        /// <exception cref="InvalidOperationException">Thrown if this is a plugin script or entity doesn't have GridMovement component.</exception>
        protected void SetFacingDirection(Direction direction)
        {
            RequireEntity();
            Context.Apis.Npc.FaceDirection(Context.Entity.Value, direction);
        }

        /// <summary>
        /// Gets the position from the attached entity's PositionComponent.
        /// Delegates to INpcApi.GetPosition.
        /// </summary>
        /// <returns>A tuple containing (X, Y) coordinates.</returns>
        /// <exception cref="InvalidOperationException">Thrown if this is a plugin script or entity doesn't have PositionComponent.</exception>
        protected (int X, int Y) GetPosition()
        {
            RequireEntity();
            var position = Context.Apis.Npc.GetPosition(Context.Entity.Value);
            if (position == null)
            {
                throw new InvalidOperationException("Entity does not have PositionComponent.");
            }

            return (position.Value.X, position.Value.Y);
        }

        /// <summary>
        /// Starts a timer that will fire TimerElapsedEvent when it expires.
        /// Only works for entity-attached scripts (not plugin scripts).
        /// </summary>
        /// <param name="timerId">Unique identifier for this timer (scoped to the entity).</param>
        /// <param name="duration">Duration in seconds.</param>
        /// <param name="isRepeating">Whether the timer should repeat after expiring (default: false).</param>
        /// <exception cref="InvalidOperationException">Thrown if this is a plugin script (no entity).</exception>
        protected void StartTimer(string timerId, float duration, bool isRepeating = false)
        {
            if (Context == null)
            {
                throw new InvalidOperationException(
                    "Script must be initialized before starting timers"
                );
            }

            if (Context.Entity == null)
            {
                throw new InvalidOperationException(
                    "Cannot start timer on plugin script (no entity)"
                );
            }

            if (string.IsNullOrWhiteSpace(timerId))
            {
                throw new ArgumentException("Timer ID cannot be null or empty", nameof(timerId));
            }

            if (duration <= 0)
            {
                throw new ArgumentException(
                    "Timer duration must be greater than 0",
                    nameof(duration)
                );
            }

            // Get or create timers component
            ScriptTimersComponent timers;
            if (Context.HasComponent<ScriptTimersComponent>())
            {
                timers = Context.GetComponent<ScriptTimersComponent>();
            }
            else
            {
                timers = new ScriptTimersComponent();
            }

            // Add or update timer (constructor ensures Timers is initialized)
            timers.Timers[timerId] = new ScriptTimerData(duration, isRepeating);
            Context.SetComponent(timers);
        }

        /// <summary>
        /// Updates an existing timer's duration without resetting its elapsed time.
        /// Useful for changing repeating timer intervals without cancel/restart pattern.
        /// </summary>
        /// <param name="timerId">Unique identifier for this timer (scoped to the entity).</param>
        /// <param name="newDuration">New duration in seconds.</param>
        /// <exception cref="InvalidOperationException">Thrown if this is a plugin script (no entity) or timer doesn't exist.</exception>
        /// <exception cref="ArgumentException">Thrown if newDuration is invalid.</exception>
        protected void UpdateTimer(string timerId, float newDuration)
        {
            if (Context == null)
            {
                throw new InvalidOperationException(
                    "Script must be initialized before updating timers"
                );
            }

            if (Context.Entity == null)
            {
                throw new InvalidOperationException(
                    "Cannot update timer on plugin script (no entity)"
                );
            }

            if (string.IsNullOrWhiteSpace(timerId))
            {
                throw new ArgumentException("Timer ID cannot be null or empty", nameof(timerId));
            }

            if (newDuration <= 0)
            {
                throw new ArgumentException(
                    "Timer duration must be greater than 0",
                    nameof(newDuration)
                );
            }

            if (!Context.HasComponent<ScriptTimersComponent>())
            {
                throw new InvalidOperationException(
                    $"Timer '{timerId}' does not exist. Use StartTimer() to create a new timer."
                );
            }

            var timers = Context.GetComponent<ScriptTimersComponent>();
            if (!timers.Timers.TryGetValue(timerId, out var timer))
            {
                throw new InvalidOperationException(
                    $"Timer '{timerId}' does not exist. Use StartTimer() to create a new timer."
                );
            }

            // Update duration without resetting elapsed time
            timer.Duration = newDuration;
            timers.Timers[timerId] = timer;
            Context.SetComponent(timers);
        }

        /// <summary>
        /// Cancels a timer by removing it from the timers component.
        /// </summary>
        /// <param name="timerId">The timer ID to cancel.</param>
        /// <exception cref="InvalidOperationException">Thrown if this is a plugin script (no entity).</exception>
        protected void CancelTimer(string timerId)
        {
            if (Context == null)
            {
                throw new InvalidOperationException(
                    "Script must be initialized before canceling timers"
                );
            }

            if (Context.Entity == null)
            {
                throw new InvalidOperationException(
                    "Cannot cancel timer on plugin script (no entity)"
                );
            }

            if (string.IsNullOrWhiteSpace(timerId))
            {
                return; // Nothing to cancel
            }

            if (Context.HasComponent<ScriptTimersComponent>())
            {
                var timers = Context.GetComponent<ScriptTimersComponent>();
                if (timers.Timers.ContainsKey(timerId))
                {
                    timers.Timers.Remove(timerId);
                    Context.SetComponent(timers);
                }
            }
        }

        /// <summary>
        /// Checks if a timer exists and is active.
        /// </summary>
        /// <param name="timerId">The timer ID to check.</param>
        /// <returns>True if the timer exists and is active, false otherwise.</returns>
        protected bool HasTimer(string timerId)
        {
            if (Context == null || Context.Entity == null)
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(timerId))
            {
                return false;
            }

            if (!Context.HasComponent<ScriptTimersComponent>())
            {
                return false;
            }

            var timers = Context.GetComponent<ScriptTimersComponent>();
            return timers.Timers.TryGetValue(timerId, out var timer) && timer.IsActive;
        }

        /// <summary>
        /// Starts a timer with a random duration between min and max.
        /// </summary>
        /// <param name="timerId">Unique identifier for this timer (scoped to the entity).</param>
        /// <param name="minDuration">Minimum duration in seconds.</param>
        /// <param name="maxDuration">Maximum duration in seconds.</param>
        /// <param name="isRepeating">Whether the timer should repeat after expiring (default: false).</param>
        /// <exception cref="InvalidOperationException">Thrown if this is a plugin script (no entity).</exception>
        protected void StartRandomTimer(
            string timerId,
            float minDuration,
            float maxDuration,
            bool isRepeating = false
        )
        {
            var duration = RandomHelper.RandomFloat(minDuration, maxDuration);
            StartTimer(timerId, duration, isRepeating);
        }

        /// <summary>
        /// Updates an existing timer with a new random duration between min and max.
        /// </summary>
        /// <param name="timerId">Unique identifier for this timer (scoped to the entity).</param>
        /// <param name="minDuration">Minimum duration in seconds.</param>
        /// <param name="maxDuration">Maximum duration in seconds.</param>
        /// <exception cref="InvalidOperationException">Thrown if this is a plugin script (no entity) or timer doesn't exist.</exception>
        protected void UpdateRandomTimer(string timerId, float minDuration, float maxDuration)
        {
            var duration = RandomHelper.RandomFloat(minDuration, maxDuration);
            UpdateTimer(timerId, duration);
        }

        /// <summary>
        /// Cancels a timer if it exists.
        /// No-op if the timer doesn't exist.
        /// </summary>
        /// <param name="timerId">The timer ID to cancel.</param>
        protected void CancelTimerIfExists(string timerId)
        {
            if (HasTimer(timerId))
            {
                CancelTimer(timerId);
            }
        }

        /// <summary>
        /// Gets a script parameter value as a Direction.
        /// </summary>
        /// <param name="name">The parameter name.</param>
        /// <param name="defaultValue">The default direction if parameter not found or invalid (default: South).</param>
        /// <returns>The parsed direction value, or defaultValue if not found or invalid.</returns>
        protected Direction GetParameterAsDirection(
            string name,
            Direction defaultValue = Direction.South
        )
        {
            var str = Context.GetParameter<string>(name, null);
            return string.IsNullOrEmpty(str)
                ? defaultValue
                : DirectionParser.Parse(str, defaultValue);
        }

        /// <summary>
        /// Gets a script parameter value as an array of Directions.
        /// </summary>
        /// <param name="name">The parameter name.</param>
        /// <param name="defaultValue">The default directions if parameter not found or invalid.</param>
        /// <returns>The parsed direction array, or defaultValue if not found or invalid.</returns>
        protected Direction[] GetParameterAsDirections(
            string name,
            Direction[]? defaultValue = null
        )
        {
            var str = Context.GetParameter<string>(name, null);
            return string.IsNullOrEmpty(str)
                ? (defaultValue ?? Array.Empty<Direction>())
                : DirectionParser.ParseList(str);
        }

        /// <summary>
        /// Gets a script parameter value as a float.
        /// </summary>
        /// <param name="name">The parameter name.</param>
        /// <param name="defaultValue">The default value if parameter not found (default: 0f).</param>
        /// <returns>The parameter value, or defaultValue if not found.</returns>
        protected float GetParameterAsFloat(string name, float defaultValue = 0f)
        {
            return Context.GetParameter<float>(name, defaultValue);
        }

        /// <summary>
        /// Gets a script parameter value as an int.
        /// </summary>
        /// <param name="name">The parameter name.</param>
        /// <param name="defaultValue">The default value if parameter not found (default: 0).</param>
        /// <returns>The parameter value, or defaultValue if not found.</returns>
        protected int GetParameterAsInt(string name, int defaultValue = 0)
        {
            return Context.GetParameter<int>(name, defaultValue);
        }

        /// <summary>
        /// Gets a script parameter value as a bool.
        /// </summary>
        /// <param name="name">The parameter name.</param>
        /// <param name="defaultValue">The default value if parameter not found (default: false).</param>
        /// <returns>The parameter value, or defaultValue if not found.</returns>
        protected bool GetParameterAsBool(string name, bool defaultValue = false)
        {
            return Context.GetParameter<bool>(name, defaultValue);
        }

        /// <summary>
        /// Gets an enum value from persisted state.
        /// </summary>
        /// <typeparam name="TEnum">The enum type.</typeparam>
        /// <param name="key">The state key.</param>
        /// <param name="defaultValue">The default value if not found or invalid.</param>
        /// <returns>The enum value, or defaultValue if not found or invalid.</returns>
        protected TEnum GetEnum<TEnum>(string key, TEnum defaultValue)
            where TEnum : struct, Enum
        {
            var str = Get<string>(key, null);
            if (string.IsNullOrEmpty(str))
                return defaultValue;

            return Enum.TryParse<TEnum>(str, out var value) ? value : defaultValue;
        }

        /// <summary>
        /// Sets an enum value in persisted state.
        /// </summary>
        /// <typeparam name="TEnum">The enum type.</typeparam>
        /// <param name="key">The state key.</param>
        /// <param name="value">The enum value to store.</param>
        protected void SetEnum<TEnum>(string key, TEnum value)
            where TEnum : struct, Enum
        {
            Set(key, value.ToString());
        }

        /// <summary>
        /// Gets a Direction value from persisted state.
        /// </summary>
        /// <param name="key">The state key.</param>
        /// <param name="defaultValue">The default direction if not found or invalid (default: South).</param>
        /// <returns>The direction value, or defaultValue if not found or invalid.</returns>
        protected Direction GetDirection(string key, Direction defaultValue = Direction.South)
        {
            return GetEnum(key, defaultValue);
        }

        /// <summary>
        /// Sets a Direction value in persisted state.
        /// </summary>
        /// <param name="key">The state key.</param>
        /// <param name="value">The direction value to store.</param>
        protected void SetDirection(string key, Direction value)
        {
            SetEnum(key, value);
        }

        /// <summary>
        /// Gets a position from persisted state (stored as two separate keys).
        /// </summary>
        /// <param name="keyX">The state key for X coordinate.</param>
        /// <param name="keyY">The state key for Y coordinate.</param>
        /// <param name="defaultX">The default X coordinate if not found (default: 0).</param>
        /// <param name="defaultY">The default Y coordinate if not found (default: 0).</param>
        /// <returns>A tuple containing (X, Y) coordinates.</returns>
        protected (int X, int Y) GetPositionState(
            string keyX,
            string keyY,
            int defaultX = 0,
            int defaultY = 0
        )
        {
            return (Get<int>(keyX, defaultX), Get<int>(keyY, defaultY));
        }

        /// <summary>
        /// Sets a position in persisted state (stored as two separate keys).
        /// </summary>
        /// <param name="keyX">The state key for X coordinate.</param>
        /// <param name="keyY">The state key for Y coordinate.</param>
        /// <param name="x">The X coordinate to store.</param>
        /// <param name="y">The Y coordinate to store.</param>
        protected void SetPositionState(string keyX, string keyY, int x, int y)
        {
            Set(keyX, x);
            Set(keyY, y);
        }

        /// <summary>
        /// Gets the state key for a given key, prefixed with script definition ID.
        /// </summary>
        /// <param name="key">The base key.</param>
        /// <returns>The full state key.</returns>
        private string GetStateKey(string key)
        {
            if (_scriptDefinitionId == null)
            {
                throw new InvalidOperationException(
                    "Script must be initialized before accessing state"
                );
            }
            return ScriptStateKeys.GetStateKey(_scriptDefinitionId, key);
        }

        /// <summary>
        /// Disposes of the script and cleans up all resources.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
        }

        /// <summary>
        /// Disposes of the script and cleans up all resources.
        /// </summary>
        /// <param name="disposing">True if called from Dispose(), false if from finalizer.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                OnUnload();
            }
        }
    }
}
