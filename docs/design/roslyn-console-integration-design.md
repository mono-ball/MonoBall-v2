# Roslyn Console Integration Design

**Created**: 2026-01-06
**Status**: Revised (v3.1)
**Objective**: Integrate Roslyn scripting into the debug console for runtime C# evaluation
**Last Updated**: 2026-01-06
**Review Status**: All review issues addressed

---

## 1. Executive Summary

This design integrates Roslyn-based C# scripting into the MonoBall debug console by **reusing the existing scripting infrastructure** (`ScriptContext`, `ScriptApiProvider`). The REPL adds only a thin wrapper for interactive evaluation.

### Key Principle: Composition Over Duplication

**REUSE existing services:**
- `ScriptContext` - Query caching, component access, entity queries
- `ScriptApiProvider` - Player, Map, Movement, Camera, Npc, Shader, MessageBox, Flags APIs

**ADD only what's new:**
- `IRoslynReplService` - CSharpScript evaluation with state persistence
- `ReplGlobals` - Facade exposing ScriptContext + console output (intentional design)
- `IConsoleInputRouter` - Input classification

### Thread Safety Note

The `RoslynReplService` is **NOT thread-safe**. It is designed for single-threaded UI usage (console input). Do not call `EvaluateAsync` from multiple threads concurrently.

---

## 2. Existing Infrastructure Analysis

### 2.1 ScriptContext (REUSE)

**Location**: `MonoBall.Core/Scripting/Runtime/ScriptContext.cs`

**Already provides:**
```csharp
// Query caching (exactly what we need!)
private static readonly ConcurrentDictionary<Type, QueryDescription> _queryCache1 = new();
private static readonly ConcurrentDictionary<(Type, Type), QueryDescription> _queryCache2 = new();
private static readonly ConcurrentDictionary<(Type, Type, Type), QueryDescription> _queryCache3 = new();

// Entity access (supports null entity for "plugin scripts")
public Entity? Entity { get; }
public bool IsPluginScript => Entity == null;

// Component access
public T GetComponent<T>() where T : struct;
public void SetComponent<T>(T component) where T : struct;
public bool HasComponent<T>() where T : struct;

// Entity queries with caching
public void Query<T1>(QueryAction<T1> action) where T1 : struct;
public void Query<T1, T2>(QueryAction<T1, T2> action) where T1, T2 : struct;
public void Query<T1, T2, T3>(QueryAction<T1, T2, T3> action) where T1, T2, T3 : struct;

// Entity lifecycle
public Entity CreateEntity(params object[] components);
public void DestroyEntity(Entity entity);

// API access
public IScriptApiProvider Apis { get; }
public ILogger Logger { get; }
```

**Key insight**: ScriptContext already supports `Entity? entity = null` for "plugin scripts". We use this for REPL!

### 2.2 ScriptApiProvider (REUSE)

**Location**: `MonoBall.Core/Scripting/ScriptApiProvider.cs`

**Already provides all game APIs:**
```csharp
public IPlayerApi Player { get; }      // GetPlayerEntity, GetPlayerPosition, etc.
public IMapApi Map { get; }            // LoadMap, UnloadMap, IsMapLoaded, etc.
public IMovementApi Movement { get; }  // RequestMovement, IsMoving, Lock/Unlock
public ICameraApi Camera { get; }      // GetActiveCamera, GetCameraPosition
public INpcApi Npc { get; }            // FaceDirection, FaceEntity, GetPosition
public IMessageBoxApi MessageBox { get; }
public IShaderApi Shader { get; }
public IFlagVariableService Flags { get; }
public DefinitionRegistry Definitions { get; }
```

---

## 3. Proposed Architecture

### 3.1 What We Actually Need to Create

```
MonoBall.Core/Diagnostics/Console/Scripting/
├── Interfaces/
│   ├── IRoslynReplService.cs       # REPL evaluation contract
│   └── IConsoleInputRouter.cs      # Input classification
├── Services/
│   ├── RoslynReplService.cs        # CSharpScript evaluation
│   └── ConsoleInputRouter.cs       # Routes commands vs C#
├── Context/
│   └── ReplGlobals.cs              # Facade (ScriptContext + console output)
├── Models/
│   ├── ReplResult.cs               # Evaluation result
│   ├── ReplVariable.cs             # Variable inspection
│   └── ReplCompletionItem.cs       # Completion item
└── Constants/
    └── ReplConstants.cs            # Configuration
```

**Total new files: 9** (down from 16 in v2.0)

### 3.2 High-Level Design

```
┌─────────────────────────────────────────────────────────────────┐
│                        ConsolePanel (UI)                        │
└─────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│                      ConsoleService                             │
│  - Delegates to IConsoleInputRouter                             │
└─────────────────────────────────────────────────────────────────┘
                              │
              ┌───────────────┴───────────────┐
              ▼                               ▼
┌─────────────────────────┐     ┌─────────────────────────────────┐
│  IConsoleCommand        │     │      IRoslynReplService         │
│  (existing commands)    │     │  - EvaluateAsync(code)          │
└─────────────────────────┘     │  - Uses CSharpScript            │
                                └─────────────────────────────────┘
                                              │
                                              ▼
                                ┌─────────────────────────────────┐
                                │         ReplGlobals             │
                                │  (FACADE PATTERN)               │
                                │                                 │
                                │  ScriptContext Context ←─REUSE  │
                                │  IConsoleContext Console ←─NEW  │
                                └─────────────────────────────────┘
                                              │
                          ┌───────────────────┼───────────────────┐
                          ▼                   ▼                   ▼
              ┌─────────────────┐ ┌─────────────────┐ ┌─────────────────┐
              │ ScriptContext   │ │ScriptApiProvider│ │ IConsoleContext │
              │ (EXISTING)      │ │ (EXISTING)      │ │ (EXISTING)      │
              │ - Query<T>()    │ │ - Player        │ │ - WriteLine()   │
              │ - CreateEntity()│ │ - Map           │ │ - WriteError()  │
              │ - Get/Set/Has   │ │ - Movement      │ │                 │
              │ - Query caching │ │ - Camera        │ │                 │
              └─────────────────┘ │ - Npc           │ └─────────────────┘
                                  │ - Shader        │
                                  │ - MessageBox    │
                                  │ - Flags         │
                                  └─────────────────┘
```

---

## 4. Component Design

### 4.1 Constants

**File**: `Constants/ReplConstants.cs`

```csharp
namespace MonoBall.Core.Diagnostics.Console.Scripting.Constants;

/// <summary>
/// Constants for REPL configuration.
/// </summary>
public static class ReplConstants
{
    /// <summary>Prefix character to force C# evaluation.</summary>
    public const char CSharpPrefix = '>';

    /// <summary>Alternative prefix for C# evaluation.</summary>
    public const char AlternativePrefix = '#';

    /// <summary>Default timeout for script evaluation in milliseconds.</summary>
    public const int DefaultEvaluationTimeoutMs = 5000;

    /// <summary>Default maximum completions to return.</summary>
    public const int DefaultCompletionLimit = 50;

    /// <summary>Number of evaluations before warning about memory usage.</summary>
    public const int MaxEvaluationsBeforeResetWarning = 100;

    /// <summary>Script definition ID for REPL context.</summary>
    public const string ReplScriptId = "__repl__";

    /// <summary>Logger source context name for REPL.</summary>
    public const string LogSourceContext = "REPL";
}
```

### 4.2 IRoslynReplService

**File**: `Interfaces/IRoslynReplService.cs`

```csharp
namespace MonoBall.Core.Diagnostics.Console.Scripting.Interfaces;

/// <summary>
/// Service contract for Roslyn-based REPL execution.
/// </summary>
/// <remarks>
/// This service is NOT thread-safe. It is designed for single-threaded
/// UI usage (console input). Do not call EvaluateAsync from multiple threads.
/// </remarks>
public interface IRoslynReplService : IDisposable
{
    /// <summary>Gets whether the service has been initialized.</summary>
    bool IsInitialized { get; }

    /// <summary>Gets the number of evaluations since last reset.</summary>
    int EvaluationCount { get; }

    /// <summary>Initializes the REPL service. Must be called before evaluation.</summary>
    /// <exception cref="InvalidOperationException">Thrown if already initialized.</exception>
    void Initialize();

    /// <summary>Resets the REPL state, clearing variables and subscriptions.</summary>
    void Reset();

    /// <summary>Evaluates C# code and returns the result.</summary>
    /// <param name="code">The C# code to evaluate.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The evaluation result.</returns>
    /// <exception cref="InvalidOperationException">Thrown if not initialized or re-entrant call.</exception>
    Task<ReplResult> EvaluateAsync(string code, CancellationToken ct = default);

    /// <summary>
    /// Gets code completions at the specified position.
    /// NOTE: Not implemented in v1.0. Returns empty list.
    /// </summary>
    /// <param name="code">The code being edited.</param>
    /// <param name="position">The cursor position.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of completion items (empty in v1.0).</returns>
    Task<IReadOnlyList<ReplCompletionItem>> GetCompletionsAsync(
        string code, int position, CancellationToken ct = default);

    /// <summary>Gets all variables defined in the current session.</summary>
    /// <returns>List of variable information.</returns>
    IReadOnlyList<ReplVariable> GetVariables();
}
```

### 4.3 IConsoleInputRouter

**File**: `Interfaces/IConsoleInputRouter.cs`

```csharp
namespace MonoBall.Core.Diagnostics.Console.Scripting.Interfaces;

/// <summary>
/// Classifies console input as commands or C# code.
/// </summary>
public interface IConsoleInputRouter
{
    /// <summary>Classifies the input type.</summary>
    /// <param name="input">The raw input string.</param>
    /// <returns>The classification result.</returns>
    ConsoleInputType ClassifyInput(string input);

    /// <summary>Strips the C# prefix from input if present.</summary>
    /// <param name="input">The input string.</param>
    /// <returns>The input without prefix.</returns>
    string StripPrefix(string input);
}

/// <summary>
/// Type of console input.
/// </summary>
public enum ConsoleInputType
{
    /// <summary>Input is a registered console command.</summary>
    Command,

    /// <summary>Input is C# code for REPL evaluation.</summary>
    CSharpCode,

    /// <summary>Input type could not be determined.</summary>
    Unknown
}
```

### 4.4 Models

**File**: `Models/ReplResult.cs`

```csharp
namespace MonoBall.Core.Diagnostics.Console.Scripting.Models;

/// <summary>
/// Result of a REPL evaluation.
/// </summary>
public sealed class ReplResult
{
    /// <summary>Gets whether the evaluation succeeded.</summary>
    public bool Success { get; init; }

    /// <summary>Gets the return value, if any.</summary>
    public object? ReturnValue { get; init; }

    /// <summary>Gets the return type, if any.</summary>
    public Type? ReturnType { get; init; }

    /// <summary>Gets whether this was a statement (no return value).</summary>
    public bool IsStatement { get; init; }

    /// <summary>Gets the error message, if evaluation failed.</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>Gets compilation diagnostics, if any.</summary>
    public IReadOnlyList<string> Diagnostics { get; init; } = Array.Empty<string>();

    /// <summary>Gets the execution time in milliseconds.</summary>
    public double ExecutionTimeMs { get; init; }

    /// <summary>Creates a successful result with a return value.</summary>
    public static ReplResult Ok(object? value, Type? type, double timeMs) => new()
    {
        Success = true,
        ReturnValue = value,
        ReturnType = type,
        ExecutionTimeMs = timeMs
    };

    /// <summary>Creates a successful result for a statement (no return value).</summary>
    public static ReplResult Statement(double timeMs) => new()
    {
        Success = true,
        IsStatement = true,
        ExecutionTimeMs = timeMs
    };

    /// <summary>Creates a failed result with an error message.</summary>
    public static ReplResult Fail(string error, IReadOnlyList<string>? diagnostics = null) => new()
    {
        Success = false,
        ErrorMessage = error,
        Diagnostics = diagnostics ?? Array.Empty<string>()
    };
}
```

**File**: `Models/ReplVariable.cs`

```csharp
namespace MonoBall.Core.Diagnostics.Console.Scripting.Models;

/// <summary>
/// Information about a variable defined in a REPL session.
/// </summary>
/// <param name="Name">The variable name.</param>
/// <param name="Type">The variable type.</param>
/// <param name="Value">The current value.</param>
public sealed record ReplVariable(string Name, Type Type, object? Value);
```

**File**: `Models/ReplCompletionItem.cs`

```csharp
namespace MonoBall.Core.Diagnostics.Console.Scripting.Models;

/// <summary>
/// A code completion suggestion.
/// </summary>
/// <param name="DisplayText">Text shown in completion list.</param>
/// <param name="InsertText">Text inserted when selected.</param>
/// <param name="Kind">Completion kind (Method, Property, etc.).</param>
/// <param name="Description">Optional description/documentation.</param>
public sealed record ReplCompletionItem(
    string DisplayText,
    string InsertText,
    string Kind,
    string? Description = null);
```

### 4.5 ReplGlobals (Facade Pattern)

**File**: `Context/ReplGlobals.cs`

This class intentionally uses the **Facade pattern** to provide a convenient API for REPL users. It has multiple responsibility areas (context, APIs, output, helpers, events) for ergonomic scripting. This is a deliberate design tradeoff.

```csharp
namespace MonoBall.Core.Diagnostics.Console.Scripting.Context;

using System.Reflection;

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
    public void Query<T>(IEntityQuery.QueryAction<T> action) where T : struct
        => Context.Query(action);

    /// <summary>
    /// Queries entities with two components. Uses cached QueryDescription.
    /// </summary>
    /// <typeparam name="T1">First component type.</typeparam>
    /// <typeparam name="T2">Second component type.</typeparam>
    /// <param name="action">Action to execute for each matching entity.</param>
    public void Query<T1, T2>(IEntityQuery.QueryAction<T1, T2> action)
        where T1 : struct where T2 : struct
        => Context.Query(action);

    /// <summary>
    /// Creates a new entity with components.
    /// </summary>
    /// <param name="components">Components to add to the entity.</param>
    /// <returns>The created entity.</returns>
    public Entity CreateEntity(params object[] components)
        => Context.CreateEntity(components);

    /// <summary>
    /// Destroys an entity.
    /// </summary>
    /// <param name="entity">The entity to destroy.</param>
    public void DestroyEntity(Entity entity)
        => Context.DestroyEntity(entity);

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
    public Entity? FindEntity<T>() where T : struct
    {
        Entity? result = null;
        Context.Query<T>((Entity e, ref T _) =>
        {
            result ??= e;
        });
        return result;
    }

    /// <summary>
    /// Finds all entities with component type.
    /// WARNING: Allocates a new List. Avoid calling in loops.
    /// </summary>
    /// <typeparam name="T">The component type to search for.</typeparam>
    /// <returns>List of all matching entities.</returns>
    public List<Entity> FindEntities<T>() where T : struct
    {
        var results = new List<Entity>();
        Context.Query<T>((Entity e, ref T _) =>
        {
            results.Add(e);
        });
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
    public void SendRef<T>(ref T evt) where T : struct
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
    public IDisposable OnEvent<T>(Action<T> handler) where T : struct
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
    }

    private void Dispose(bool disposing)
    {
        if (!_disposed && disposing)
        {
            ClearSubscriptions();
        }
        _disposed = true;
    }
}
```

### 4.6 ConsoleInputRouter

**File**: `Services/ConsoleInputRouter.cs`

```csharp
namespace MonoBall.Core.Diagnostics.Console.Scripting.Services;

/// <summary>
/// Routes console input to commands or REPL based on classification.
/// </summary>
public sealed class ConsoleInputRouter : IConsoleInputRouter
{
    private readonly IConsoleCommandRegistry _commandRegistry;

    /// <summary>
    /// Initializes a new instance of the <see cref="ConsoleInputRouter"/> class.
    /// </summary>
    /// <param name="commandRegistry">The command registry to check for commands.</param>
    /// <exception cref="ArgumentNullException">Thrown if commandRegistry is null.</exception>
    public ConsoleInputRouter(IConsoleCommandRegistry commandRegistry)
    {
        _commandRegistry = commandRegistry ?? throw new ArgumentNullException(nameof(commandRegistry));
    }

    /// <inheritdoc/>
    public ConsoleInputType ClassifyInput(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return ConsoleInputType.Unknown;

        var trimmed = input.TrimStart();

        // Check for explicit C# prefix
        if (trimmed.Length > 0 &&
            (trimmed[0] == ReplConstants.CSharpPrefix ||
             trimmed[0] == ReplConstants.AlternativePrefix))
        {
            return ConsoleInputType.CSharpCode;
        }

        // Check if it's a registered command
        var firstWord = GetFirstWord(trimmed);
        if (_commandRegistry.HasCommand(firstWord))
        {
            return ConsoleInputType.Command;
        }

        // Default to C# code
        return ConsoleInputType.CSharpCode;
    }

    /// <inheritdoc/>
    public string StripPrefix(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return input;

        var trimmed = input.TrimStart();
        if (trimmed.Length > 0 &&
            (trimmed[0] == ReplConstants.CSharpPrefix ||
             trimmed[0] == ReplConstants.AlternativePrefix))
        {
            return trimmed.Substring(1).TrimStart();
        }

        return input;
    }

    private static string GetFirstWord(string input)
    {
        var spaceIndex = input.IndexOf(' ');
        return spaceIndex > 0 ? input.Substring(0, spaceIndex) : input;
    }
}
```

### 4.7 RoslynReplService

**File**: `Services/RoslynReplService.cs`

```csharp
namespace MonoBall.Core.Diagnostics.Console.Scripting.Services;

/// <summary>
/// Roslyn-based REPL service implementation.
/// Creates ScriptContext for REPL and manages CSharpScript state.
/// </summary>
/// <remarks>
/// This service is NOT thread-safe. It is designed for single-threaded
/// UI usage (console input). Do not call EvaluateAsync from multiple threads.
/// </remarks>
public sealed class RoslynReplService : IRoslynReplService
{
    private readonly World _world;
    private readonly IScriptApiProvider _apis;
    private readonly IConsoleContext _console;
    private readonly ILogger _logger;

    private ScriptState<object>? _scriptState;
    private ReplGlobals? _globals;
    private ScriptContext? _replContext;
    private bool _disposed;
    private bool _isEvaluating;

    /// <inheritdoc/>
    public bool IsInitialized => _globals != null;

    /// <inheritdoc/>
    public int EvaluationCount { get; private set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="RoslynReplService"/> class.
    /// </summary>
    /// <param name="world">The ECS world.</param>
    /// <param name="apis">The script API provider.</param>
    /// <param name="console">The console context for output.</param>
    /// <param name="logger">The logger.</param>
    /// <exception cref="ArgumentNullException">Thrown if any parameter is null.</exception>
    public RoslynReplService(
        World world,
        IScriptApiProvider apis,
        IConsoleContext console,
        ILogger logger)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _apis = apis ?? throw new ArgumentNullException(nameof(apis));
        _console = console ?? throw new ArgumentNullException(nameof(console));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public void Initialize()
    {
        ThrowIfDisposed();

        if (IsInitialized)
        {
            throw new InvalidOperationException(
                "RoslynReplService is already initialized. Call Reset() to reinitialize.");
        }

        // Create ScriptContext for REPL (no entity - like a "plugin script")
        _replContext = new ScriptContext(
            world: _world,
            entity: null,  // No attached entity - REPL is global
            logger: _logger.ForContext("SourceContext", ReplConstants.LogSourceContext),
            apis: _apis,
            scriptDefinitionId: ReplConstants.ReplScriptId,
            parameters: new Dictionary<string, object>()
        );

        _globals = new ReplGlobals(_replContext, _console);
        EvaluationCount = 0;

        _logger.Information("RoslynReplService initialized");
    }

    /// <inheritdoc/>
    public void Reset()
    {
        ThrowIfDisposed();

        _globals?.ClearSubscriptions();
        _globals?.Dispose();

        // Recreate context and globals
        _replContext = new ScriptContext(
            world: _world,
            entity: null,
            logger: _logger.ForContext("SourceContext", ReplConstants.LogSourceContext),
            apis: _apis,
            scriptDefinitionId: ReplConstants.ReplScriptId,
            parameters: new Dictionary<string, object>()
        );

        _globals = new ReplGlobals(_replContext, _console);
        _scriptState = null;
        EvaluationCount = 0;

        _logger.Information("RoslynReplService reset");
    }

    /// <inheritdoc/>
    public async Task<ReplResult> EvaluateAsync(string code, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ThrowIfNotInitialized();

        if (_isEvaluating)
        {
            throw new InvalidOperationException(
                "Re-entrancy detected. Cannot evaluate while another evaluation is in progress.");
        }

        if (string.IsNullOrWhiteSpace(code))
        {
            return ReplResult.Statement(0);
        }

        _isEvaluating = true;
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var options = ScriptOptions.Default
                .AddReferences(GetAssemblyReferences())
                .AddImports(GetDefaultImports());

            if (_scriptState == null)
            {
                _scriptState = await CSharpScript.RunAsync<object>(
                    code,
                    options,
                    _globals,
                    typeof(ReplGlobals),
                    ct);
            }
            else
            {
                _scriptState = await _scriptState.ContinueWithAsync<object>(
                    code,
                    options,
                    ct);
            }

            stopwatch.Stop();
            EvaluationCount++;

            if (EvaluationCount == ReplConstants.MaxEvaluationsBeforeResetWarning)
            {
                _console.WriteWarning(
                    $"Reached {EvaluationCount} evaluations. Consider Reset() to free memory.");
            }

            var returnValue = _scriptState.ReturnValue;
            var returnType = returnValue?.GetType();

            return returnValue == null
                ? ReplResult.Statement(stopwatch.Elapsed.TotalMilliseconds)
                : ReplResult.Ok(returnValue, returnType, stopwatch.Elapsed.TotalMilliseconds);
        }
        catch (CompilationErrorException ex)
        {
            stopwatch.Stop();
            return ReplResult.Fail(ex.Message, ex.Diagnostics.Select(d => d.ToString()).ToList());
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            stopwatch.Stop();
            _logger.Error(ex, "REPL evaluation error");
            return ReplResult.Fail($"Runtime error: {ex.Message}");
        }
        finally
        {
            _isEvaluating = false;
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// NOTE: Code completion is not implemented in v1.0.
    /// This is planned for a future release.
    /// </remarks>
    public async Task<IReadOnlyList<ReplCompletionItem>> GetCompletionsAsync(
        string code, int position, CancellationToken ct = default)
    {
        // Phase 2 feature - not implemented in v1.0
        await Task.CompletedTask;
        return Array.Empty<ReplCompletionItem>();
    }

    /// <inheritdoc/>
    public IReadOnlyList<ReplVariable> GetVariables()
    {
        if (_scriptState == null) return Array.Empty<ReplVariable>();
        return _scriptState.Variables
            .Select(v => new ReplVariable(v.Name, v.Type, v.Value))
            .ToList();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        Dispose(true);
    }

    private void Dispose(bool disposing)
    {
        if (!_disposed && disposing)
        {
            _globals?.Dispose();
            _globals = null;
            _replContext = null;
            _scriptState = null;
        }
        _disposed = true;
    }

    /// <summary>Throws ObjectDisposedException if disposed.</summary>
    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(RoslynReplService));
    }

    /// <summary>Throws InvalidOperationException if not initialized.</summary>
    private void ThrowIfNotInitialized()
    {
        if (!IsInitialized)
            throw new InvalidOperationException("Call Initialize() first.");
    }

    /// <summary>Gets assembly references for Roslyn script compilation.</summary>
    private static IEnumerable<Assembly> GetAssemblyReferences()
    {
        return new[]
        {
            typeof(object).Assembly,
            typeof(Enumerable).Assembly,
            typeof(Entity).Assembly,
            typeof(ReplGlobals).Assembly,
            typeof(Microsoft.Xna.Framework.Vector2).Assembly,
        };
    }

    /// <summary>Gets default namespace imports for Roslyn scripts.</summary>
    private static IEnumerable<string> GetDefaultImports()
    {
        return new[]
        {
            "System",
            "System.Collections.Generic",
            "System.Linq",
            "Arch.Core",
            "MonoBall.Core.ECS.Components",
            "MonoBall.Core.ECS.Events",
        };
    }
}
```

---

## 5. What Scripts Can Do

### 5.1 Via Existing ScriptApiProvider

```csharp
// Player API
> Player.GetPlayerEntity()
> Player.GetPlayerPosition()
> Player.GetPlayerMapId()

// Map API
> Map.LoadMap("cave_1")
> Map.UnloadMap("overworld")
> Map.IsMapLoaded("town_square")
> Map.GetLoadedMapIds()

// Movement API
> Movement.RequestMovement(entity, Direction.North)
> Movement.IsMoving(entity)
> Movement.LockMovement(entity)

// Camera API
> Camera.GetCameraPosition()
> Camera.GetActiveCamera()

// NPC API
> Npc.FaceDirection(entity, Direction.South)
> Npc.FaceEntity(npc, player)
> Npc.GetPosition(entity)

// Flags/Variables
> Flags.SetVariable("quest_started", true)
> Flags.GetVariable<bool>("quest_started")
```

### 5.2 Via Existing ScriptContext

```csharp
// Entity queries (with cached QueryDescription!)
> Context.Query<PlayerComponent>((e, ref player) => Print($"Player: {e.Id}"))
> Context.Query<PositionComponent, GridMovement>((e, ref pos, ref mov) => { ... })

// Entity lifecycle
> var enemy = CreateEntity(new PositionComponent(10, 10), new EnemyComponent())
> DestroyEntity(enemy)
```

### 5.3 Via New Console Output

```csharp
// Output
> Print("Hello from REPL!")
> Log("Info message")
> Error("Error message")
> Dump(Player.GetPlayerPosition())

// Convenience helpers
> var player = GetPlayer()
> var enemies = FindEntities<EnemyComponent>()  // Note: allocates new List
> FindEntity<PlayerComponent>()

// Events
> var evt = new DebugEvent { Message = "Test" };
> SendRef(ref evt)
> OnEvent<PlayerMovedEvent>(e => Print($"Player moved to {e.Position}"))
```

---

## 6. Implementation Summary

### 6.1 Files to Create (9 total)

| File | Purpose |
|------|---------|
| `Interfaces/IRoslynReplService.cs` | REPL evaluation contract |
| `Interfaces/IConsoleInputRouter.cs` | Input classification contract |
| `Services/RoslynReplService.cs` | CSharpScript evaluation |
| `Services/ConsoleInputRouter.cs` | Routes commands vs C# |
| `Context/ReplGlobals.cs` | Facade (ScriptContext + console) |
| `Models/ReplResult.cs` | Evaluation result |
| `Models/ReplVariable.cs` | Variable inspection |
| `Models/ReplCompletionItem.cs` | Completion item |
| `Constants/ReplConstants.cs` | Configuration |

### 6.2 Files to Modify

| File | Changes |
|------|---------|
| `ConsoleService.cs` | Inject IRoslynReplService, IConsoleInputRouter, add routing |

### 6.3 What We're NOT Creating (Reusing Instead)

| Component | Status |
|-----------|--------|
| Query caching | **REUSE** ScriptContext._queryCache |
| Entity queries | **REUSE** ScriptContext.Query<T>() |
| Component access | **REUSE** ScriptContext.Get/Set/Has |
| Player API | **REUSE** ScriptApiProvider.Player |
| Map API | **REUSE** ScriptApiProvider.Map |
| Movement API | **REUSE** ScriptApiProvider.Movement |
| Camera API | **REUSE** ScriptApiProvider.Camera |
| NPC API | **REUSE** ScriptApiProvider.Npc |
| Shader API | **REUSE** ScriptApiProvider.Shader |
| MessageBox API | **REUSE** ScriptApiProvider.MessageBox |
| Flags API | **REUSE** ScriptApiProvider.Flags |

---

## 7. Benefits of This Approach

1. **DRY**: No duplicated query caching, component access, or API implementations
2. **Consistency**: REPL uses same APIs as mod scripts
3. **Maintenance**: Bug fixes in ScriptContext/ScriptApiProvider benefit REPL
4. **Less Code**: 9 files instead of 16
5. **Tested**: Existing infrastructure is already battle-tested
6. **Future-proof**: New APIs added to ScriptApiProvider automatically available in REPL

---

## 8. Dependencies

### Required NuGet Packages

Already referenced:
- `Microsoft.CodeAnalysis.CSharp`

Need to add:
- `Microsoft.CodeAnalysis.CSharp.Scripting`
- `Microsoft.CodeAnalysis.Workspaces.Common` (for completions - Phase 2)

---

## 9. Success Metrics

- [ ] Can evaluate simple C# expressions
- [ ] Can use all existing ScriptApiProvider APIs (Player, Map, etc.)
- [ ] Can use ScriptContext.Query<T>() with cached queries
- [ ] Variables persist across evaluations
- [ ] Console output works (Print, Dump)
- [ ] Event subscription with auto-cleanup works
- [ ] No code duplication with existing scripting system
- [ ] Performance: <100ms for simple evaluations

---

## 10. Future Enhancements (Phase 2)

1. **Code Completion**: Implement `GetCompletionsAsync` with Roslyn CompletionService
2. **Entity Inspection**: Add `Inspect(Entity)` method with component enumeration
3. **Script File Loading**: Support `.csx` file loading
4. **History Separation**: Separate C# history from command history

---

**Version**: 3.1
**Last Updated**: 2026-01-06
**Key Changes**:
- v3.0: Refactored to compose with existing ScriptContext/ScriptApiProvider
- v3.1: Fixed all review issues (WriteInfo→WriteSystem, added missing definitions, documented Facade pattern, fixed bare catch, added XML docs)
