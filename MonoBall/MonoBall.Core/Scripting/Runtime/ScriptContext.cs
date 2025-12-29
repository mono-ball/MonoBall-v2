using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Arch.Core;
using Microsoft.Xna.Framework;
using MonoBall.Core.Scripting.Utilities;
using Serilog;

namespace MonoBall.Core.Scripting.Runtime;

/// <summary>
///     Execution context for scripts, providing access to ECS world, entity, game APIs, and script parameters.
///     World access is encapsulated - scripts use safe wrapper methods.
///     Implements focused interfaces for component access, entity querying, entity creation, and parameter access.
/// </summary>
public class ScriptContext : IComponentAccessor, IEntityQuery, IEntityFactory, IScriptParameters
{
    // Cache QueryDescription instances to avoid allocations in hot paths
    private static readonly ConcurrentDictionary<Type, QueryDescription> _queryCache1 = new();

    private static readonly ConcurrentDictionary<(Type, Type), QueryDescription> _queryCache2 =
        new();

    private static readonly ConcurrentDictionary<
        (Type, Type, Type),
        QueryDescription
    > _queryCache3 = new();

    private readonly World _world;

    /// <summary>
    ///     Initializes a new instance of the ScriptContext class.
    /// </summary>
    /// <param name="world">The ECS world.</param>
    /// <param name="entity">The entity this script is attached to (null for plugin scripts).</param>
    /// <param name="logger">Logger for script-specific logging.</param>
    /// <param name="apis">Script API provider for accessing game systems.</param>
    /// <param name="scriptDefinitionId">The script definition ID (for state key generation).</param>
    /// <param name="parameters">Script parameters dictionary.</param>
    public ScriptContext(
        World world,
        Entity? entity,
        ILogger logger,
        IScriptApiProvider apis,
        string scriptDefinitionId,
        Dictionary<string, object> parameters
    )
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        Entity = entity;
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        Apis = apis ?? throw new ArgumentNullException(nameof(apis));
        ScriptDefinitionId =
            scriptDefinitionId ?? throw new ArgumentNullException(nameof(scriptDefinitionId));
        // Create read-only copy of parameters dictionary
        Parameters = new Dictionary<string, object>(parameters ?? new Dictionary<string, object>());
    }

    /// <summary>
    ///     The entity this script is attached to (null for plugin scripts).
    /// </summary>
    public Entity? Entity { get; }

    /// <summary>
    ///     Logger for script-specific logging.
    /// </summary>
    public ILogger Logger { get; }

    /// <summary>
    ///     Script API provider for accessing game systems.
    /// </summary>
    public IScriptApiProvider Apis { get; }

    /// <summary>
    ///     The script definition ID (for state key generation).
    /// </summary>
    public string ScriptDefinitionId { get; }

    /// <summary>
    ///     Script parameters dictionary (read-only).
    /// </summary>
    public IReadOnlyDictionary<string, object> Parameters { get; }

    /// <summary>
    ///     Checks if this is a plugin script (no attached entity).
    /// </summary>
    public bool IsPluginScript => Entity == null;

    /// <summary>
    ///     Checks if this is an entity-attached script.
    /// </summary>
    public bool IsEntityScript => Entity != null;

    /// <summary>
    ///     Gets a component value from the attached entity.
    ///     Throws if this is a plugin script (no entity).
    /// </summary>
    /// <typeparam name="T">The component type.</typeparam>
    /// <returns>The component value.</returns>
    /// <exception cref="InvalidOperationException">Thrown if this is a plugin script (no entity).</exception>
    public T GetComponent<T>()
        where T : struct
    {
        if (Entity == null)
            throw new InvalidOperationException(
                "Cannot get component on plugin script (no entity)"
            );
        return _world.Get<T>(Entity.Value);
    }

    /// <summary>
    ///     Sets a component value on the attached entity.
    ///     Adds the component if it doesn't exist, updates it if it does.
    ///     Throws if this is a plugin script (no entity).
    /// </summary>
    /// <typeparam name="T">The component type.</typeparam>
    /// <param name="component">The component value to set.</param>
    /// <exception cref="InvalidOperationException">Thrown if this is a plugin script (no entity).</exception>
    public void SetComponent<T>(T component)
        where T : struct
    {
        if (Entity == null)
            throw new InvalidOperationException(
                "Cannot set component on plugin script (no entity)"
            );

        // World.Set() requires the component to exist, World.Add() adds it if missing
        // Use Add() which handles both cases (adds if missing, replaces if exists)
        if (_world.Has<T>(Entity.Value))
            _world.Set(Entity.Value, component);
        else
            _world.Add(Entity.Value, component);
    }

    /// <summary>
    ///     Checks if the attached entity has a component.
    ///     Returns false for plugin scripts (no entity).
    /// </summary>
    /// <typeparam name="T">The component type.</typeparam>
    /// <returns>True if the entity has the component, false otherwise.</returns>
    public bool HasComponent<T>()
        where T : struct
    {
        if (Entity == null)
            return false;
        return _world.Has<T>(Entity.Value);
    }

    /// <summary>
    ///     Creates a new entity with the specified components.
    ///     Available to plugin scripts for creating entities dynamically.
    /// </summary>
    /// <param name="components">The components to add to the entity (must be struct components).</param>
    /// <returns>The created entity.</returns>
    /// <exception cref="ArgumentException">Thrown if any component is not a value type (struct).</exception>
    public Entity CreateEntity(params object[] components)
    {
        if (components == null)
            throw new ArgumentNullException(nameof(components));

        // Validate that all components are structs (value types)
        for (var i = 0; i < components.Length; i++)
        {
            if (components[i] == null)
                throw new ArgumentException(
                    $"Component at index {i} is null. Components must be struct instances.",
                    nameof(components)
                );

            var componentType = components[i].GetType();
            if (!componentType.IsValueType)
                throw new ArgumentException(
                    $"Component at index {i} is of type {componentType.Name}, which is not a value type (struct). "
                        + "Only struct components are allowed.",
                    nameof(components)
                );
        }

        return _world.Create(components);
    }

    /// <summary>
    ///     Destroys an entity.
    ///     Available to plugin scripts for removing entities.
    /// </summary>
    /// <param name="entity">The entity to destroy.</param>
    /// <exception cref="InvalidOperationException">Thrown if the entity is not alive.</exception>
    public void DestroyEntity(Entity entity)
    {
        if (!_world.IsAlive(entity))
            throw new InvalidOperationException($"Entity {entity.Id} is not alive");
        _world.Destroy(entity);
    }

    // QueryAction delegates are defined in IEntityQuery interface

    /// <summary>
    ///     Queries entities with a single component type.
    ///     Available to plugin scripts for querying entities.
    /// </summary>
    /// <typeparam name="T1">The component type.</typeparam>
    /// <param name="action">The action to execute for each matching entity.</param>
    /// <exception cref="ArgumentNullException">Thrown if action is null.</exception>
    public void Query<T1>(IEntityQuery.QueryAction<T1> action)
        where T1 : struct
    {
        if (action == null)
            throw new ArgumentNullException(nameof(action));

        var query = _queryCache1.GetOrAdd(typeof(T1), _ => new QueryDescription().WithAll<T1>());
        _world.Query(in query, (Entity e, ref T1 c1) => action(e, ref c1));
    }

    /// <summary>
    ///     Queries entities with multiple components.
    /// </summary>
    /// <typeparam name="T1">The first component type.</typeparam>
    /// <typeparam name="T2">The second component type.</typeparam>
    /// <param name="action">The action to execute for each matching entity.</param>
    /// <exception cref="ArgumentNullException">Thrown if action is null.</exception>
    public void Query<T1, T2>(IEntityQuery.QueryAction<T1, T2> action)
        where T1 : struct
        where T2 : struct
    {
        if (action == null)
            throw new ArgumentNullException(nameof(action));

        var query = _queryCache2.GetOrAdd(
            (typeof(T1), typeof(T2)),
            _ => new QueryDescription().WithAll<T1, T2>()
        );
        _world.Query(in query, (Entity e, ref T1 c1, ref T2 c2) => action(e, ref c1, ref c2));
    }

    /// <summary>
    ///     Queries entities with three components.
    /// </summary>
    /// <typeparam name="T1">The first component type.</typeparam>
    /// <typeparam name="T2">The second component type.</typeparam>
    /// <typeparam name="T3">The third component type.</typeparam>
    /// <param name="action">The action to execute for each matching entity.</param>
    /// <exception cref="ArgumentNullException">Thrown if action is null.</exception>
    public void Query<T1, T2, T3>(IEntityQuery.QueryAction<T1, T2, T3> action)
        where T1 : struct
        where T2 : struct
        where T3 : struct
    {
        if (action == null)
            throw new ArgumentNullException(nameof(action));

        var query = _queryCache3.GetOrAdd(
            (typeof(T1), typeof(T2), typeof(T3)),
            _ => new QueryDescription().WithAll<T1, T2, T3>()
        );
        _world.Query(
            in query,
            (Entity e, ref T1 c1, ref T2 c2, ref T3 c3) => action(e, ref c1, ref c2, ref c3)
        );
    }

    /// <summary>
    ///     Gets a script parameter value by name.
    /// </summary>
    /// <typeparam name="T">The type to deserialize to.</typeparam>
    /// <param name="name">The parameter name.</param>
    /// <param name="defaultValue">Optional default value if parameter not found.</param>
    /// <returns>The parameter value, or defaultValue if not found.</returns>
    public T GetParameter<T>(string name, T? defaultValue = default)
    {
        if (Parameters.TryGetValue(name, out var value))
        {
            if (value is T typedValue)
                return typedValue;

            // Special handling for Vector2
            if (typeof(T) == typeof(Vector2) && value is string vectorStr)
                return (T)(object)Vector2Parser.Parse(vectorStr);

            // Try to convert for primitive types
            try
            {
                if (value is T directValue)
                    return directValue;

                return (T)Convert.ChangeType(value, typeof(T));
            }
            catch (InvalidCastException ex)
            {
                Logger.Warning(
                    ex,
                    "Failed to convert parameter '{ParameterName}' from {SourceType} to {TargetType}. Using default value.",
                    name,
                    value?.GetType()?.Name ?? "null",
                    typeof(T).Name
                );
                return defaultValue ?? default(T)!;
            }
            catch (FormatException ex)
            {
                Logger.Warning(
                    ex,
                    "Failed to parse parameter '{ParameterName}' as {TargetType}. Using default value.",
                    name,
                    typeof(T).Name
                );
                return defaultValue ?? default(T)!;
            }
            catch (Exception ex)
            {
                Logger.Warning(
                    ex,
                    "Unexpected error converting parameter '{ParameterName}' to {TargetType}. Using default value.",
                    name,
                    typeof(T).Name
                );
                return defaultValue ?? default(T)!;
            }
        }

        return defaultValue ?? default(T)!;
    }

    /// <summary>
    ///     Gets all script parameters as a read-only dictionary.
    /// </summary>
    /// <returns>Dictionary of parameter names to values.</returns>
    public IReadOnlyDictionary<string, object> GetParameters()
    {
        return Parameters;
    }
}
