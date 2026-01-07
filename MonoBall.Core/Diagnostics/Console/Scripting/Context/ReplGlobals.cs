namespace MonoBall.Core.Diagnostics.Console.Scripting.Context;

using System;
using System.Collections.Generic;
using System.Reflection;
using Arch.Core;
using Console.Services;
using MonoBall.Core.ECS;
using MonoBall.Core.ECS.Services;
using MonoBall.Core.Mods;
using MonoBall.Core.Scripting;
using MonoBall.Core.Scripting.Api;
using MonoBall.Core.Scripting.Runtime;
using Serilog;

/// <summary>
/// Global context for Roslyn REPL scripts.
/// Facade composing ScriptContext, APIs, and console output.
/// </summary>
/// <remarks>
/// <para>This class uses the Facade pattern to provide convenient REPL access.</para>
/// <para>It intentionally combines multiple concerns for scripting ergonomics:</para>
/// <list type="bullet">
///   <item>ScriptContext delegation (ECS access)</item>
///   <item>API property forwarding (game systems)</item>
///   <item>Console output methods</item>
///   <item>Entity finder helpers</item>
///   <item>Event subscription management</item>
/// </list>
/// <para>REUSES existing infrastructure:</para>
/// <para>- ScriptContext for ECS access (query caching, component access)</para>
/// <para>- ScriptApiProvider for game APIs (Player, Map, etc.)</para>
/// <para>ADDS only console output and convenience helpers.</para>
/// </remarks>
public sealed class ReplGlobals : IDisposable
{
    private readonly IConsoleContext _console;
    private readonly List<IDisposable> _eventSubscriptions = new();
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="ReplGlobals"/> class.
    /// </summary>
    /// <param name="context">The script context for ECS access.</param>
    /// <param name="console">The console context for output.</param>
    /// <exception cref="ArgumentNullException">Thrown if any parameter is null.</exception>
    public ReplGlobals(ScriptContext context, IConsoleContext console)
    {
        Context = context ?? throw new ArgumentNullException(nameof(context));
        _console = console ?? throw new ArgumentNullException(nameof(console));
    }

    // ===== REUSED: ScriptContext =====

    /// <summary>
    /// Script execution context with ECS access.
    /// Provides query caching, component access, entity queries.
    /// </summary>
    public ScriptContext Context { get; }

    // ===== REUSED: ScriptApiProvider (via Context.Apis) =====

    /// <summary>All game APIs.</summary>
    public IScriptApiProvider Apis => Context.Apis;

    /// <summary>Player API.</summary>
    public IPlayerApi Player => Apis.Player;

    /// <summary>Map API.</summary>
    public IMapApi Map => Apis.Map;

    /// <summary>Movement API.</summary>
    public IMovementApi Movement => Apis.Movement;

    /// <summary>Camera API.</summary>
    public ICameraApi Camera => Apis.Camera;

    /// <summary>NPC API.</summary>
    public INpcApi Npc => Apis.Npc;

    /// <summary>Shader API.</summary>
    public IShaderApi Shader => Apis.Shader;

    /// <summary>MessageBox API.</summary>
    public IMessageBoxApi MessageBox => Apis.MessageBox;

    /// <summary>Flag variables service.</summary>
    public IFlagVariableService Flags => Apis.Flags;

    /// <summary>Definition registry.</summary>
    public DefinitionRegistry Definitions => Apis.Definitions;

    /// <summary>Logger.</summary>
    public ILogger Logger => Context.Logger;

    // ===== REUSED: ScriptContext Entity Queries =====

    /// <summary>
    /// Queries entities with component. Uses cached QueryDescription.
    /// </summary>
    /// <typeparam name="T">The component type.</typeparam>
    /// <param name="action">Action to execute for each matching entity.</param>
    public void Query<T>(IEntityQuery.QueryAction<T> action)
        where T : struct => Context.Query(action);

    /// <summary>
    /// Queries entities with two components. Uses cached QueryDescription.
    /// </summary>
    /// <typeparam name="T1">First component type.</typeparam>
    /// <typeparam name="T2">Second component type.</typeparam>
    /// <param name="action">Action to execute for each matching entity.</param>
    public void Query<T1, T2>(IEntityQuery.QueryAction<T1, T2> action)
        where T1 : struct
        where T2 : struct => Context.Query(action);

    /// <summary>
    /// Creates a new entity with components.
    /// </summary>
    /// <param name="components">Components to add to the entity.</param>
    /// <returns>The created entity.</returns>
    public Entity CreateEntity(params object[] components) => Context.CreateEntity(components);

    /// <summary>
    /// Destroys an entity.
    /// </summary>
    /// <param name="entity">The entity to destroy.</param>
    public void DestroyEntity(Entity entity) => Context.DestroyEntity(entity);

    // ===== NEW: Console Output =====

    /// <summary>Prints text to console.</summary>
    /// <param name="text">The text to print.</param>
    public void Print(string text) => _console.WriteLine(text);

    /// <summary>Prints text with system/info styling.</summary>
    /// <param name="text">The text to print.</param>
    public void Log(string text) => _console.WriteSystem(text);

    /// <summary>Prints text with error styling.</summary>
    /// <param name="text">The text to print.</param>
    public void Error(string text) => _console.WriteError(text);

    /// <summary>Dumps object properties to console.</summary>
    /// <param name="obj">The object to dump.</param>
    public void Dump(object? obj)
    {
        if (obj == null)
        {
            _console.WriteLine("null");
            return;
        }

        var type = obj.GetType();
        _console.WriteLine($"{type.Name}:");

        foreach (var prop in type.GetProperties())
        {
            try
            {
                var value = prop.GetValue(obj);
                _console.WriteLine($"  {prop.Name}: {value}");
            }
            catch (TargetInvocationException)
            {
                _console.WriteLine($"  {prop.Name}: <error reading>");
            }
            catch (TargetParameterCountException)
            {
                _console.WriteLine($"  {prop.Name}: <indexed property>");
            }
        }
    }

    // ===== NEW: Convenience Entity Helpers =====

    /// <summary>
    /// Finds first entity with component type.
    /// Convenience wrapper around Query.
    /// </summary>
    /// <typeparam name="T">The component type to search for.</typeparam>
    /// <returns>The first matching entity, or null if none found.</returns>
    public Entity? FindEntity<T>()
        where T : struct
    {
        Entity? result = null;
        Context.Query<T>(
            (Entity e, ref T _) =>
            {
                result ??= e;
            }
        );
        return result;
    }

    /// <summary>
    /// Finds all entities with component type.
    /// WARNING: Allocates a new List. Avoid calling in loops.
    /// </summary>
    /// <typeparam name="T">The component type to search for.</typeparam>
    /// <returns>List of all matching entities.</returns>
    public List<Entity> FindEntities<T>()
        where T : struct
    {
        var results = new List<Entity>();
        Context.Query<T>(
            (Entity e, ref T _) =>
            {
                results.Add(e);
            }
        );
        return results;
    }

    /// <summary>Gets the player entity.</summary>
    /// <returns>The player entity, or null if not found.</returns>
    public Entity? GetPlayer() => Player.GetPlayerEntity();

    // ===== NEW: Event Helpers (with cleanup tracking) =====

    /// <summary>
    /// Sends event via EventBus (by ref for proper semantics).
    /// </summary>
    /// <typeparam name="T">The event type.</typeparam>
    /// <param name="evt">The event to send (by ref).</param>
    public void SendRef<T>(ref T evt)
        where T : struct
    {
        var typeName = typeof(T).Name;
        try
        {
            EventBus.Send(ref evt);
            _console.WriteLine($"Event sent: {typeName}");
        }
        catch (Exception ex)
        {
            _console.WriteError($"Event {typeName} failed: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Subscribes to event with automatic cleanup on Reset().
    /// </summary>
    /// <typeparam name="T">The event type.</typeparam>
    /// <param name="handler">The event handler.</param>
    /// <returns>Disposable subscription.</returns>
    public IDisposable OnEvent<T>(Action<T> handler)
        where T : struct
    {
        var subscription = EventBus.Subscribe(handler);
        _eventSubscriptions.Add(subscription);
        _console.WriteLine($"Subscribed to: {typeof(T).Name}");
        return subscription;
    }

    /// <summary>
    /// Clears all event subscriptions. Called by Reset().
    /// </summary>
    internal void ClearSubscriptions()
    {
        foreach (var sub in _eventSubscriptions)
        {
            sub.Dispose();
        }
        _eventSubscriptions.Clear();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>Disposes managed resources.</summary>
    private void Dispose(bool disposing)
    {
        if (!_disposed && disposing)
        {
            ClearSubscriptions();
        }
        _disposed = true;
    }
}
