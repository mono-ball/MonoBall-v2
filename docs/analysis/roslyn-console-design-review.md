# Roslyn Console Integration Design Review

**Reviewed**: 2026-01-06
**Status**: Issues Identified
**Reviewer**: Hive Mind Analysis

---

## Executive Summary

The proposed Roslyn console integration design has **17 significant issues** across architecture, ECS patterns, SOLID principles, and .cursorrules violations. This document details each issue with severity, location, and recommended fixes.

---

## Issue Severity Legend

| Severity | Description |
|----------|-------------|
| 🔴 **CRITICAL** | Violates .cursorrules, will cause bugs or architectural problems |
| 🟠 **HIGH** | Significant design flaw, should fix before implementation |
| 🟡 **MEDIUM** | Improvement recommended, can defer |
| 🟢 **LOW** | Minor suggestion |

---

## 1. Architecture Issues

### 🔴 ISSUE-1: RoslynGlobals Has Multiple Responsibilities (God Class)

**Location**: Design Section 3.2.2 - RoslynGlobals

**Problem**: RoslynGlobals is designed to do too much:
- Output methods (Print, Log, Error, Dump)
- Entity queries (FindEntity, FindEntities, GetPlayer)
- Component access (Get, Set, Has)
- Entity lifecycle (CreateEntity, DestroyEntity)
- Event publishing (Send, SendRef)
- Inspection utilities (Inspect, ListComponents, ListEntities)
- API shortcuts (Player, Map, Camera, etc.)

**Violation**:
- .cursorrules: "Single Responsibility: Each class should have one reason to change"
- Anti-pattern: "God classes - classes that do too much"

**Recommendation**: Split into focused helper classes:
```
RoslynGlobals (minimal, delegates to helpers)
├── ReplOutputHelper (Print, Log, Error, Dump)
├── ReplEntityHelper (Find, Create, Destroy, Inspect)
├── ReplComponentHelper (Get, Set, Has, ListComponents)
└── ReplEventHelper (Send, SendRef)
```

---

### 🔴 ISSUE-2: Missing Interface for RoslynReplService

**Location**: Design Section 3.2.1 - RoslynReplService

**Problem**: Design shows `IRoslynReplService` interface but implementation discussion focuses on concrete `RoslynReplService`. No clear interface contract defined.

**Violation**:
- .cursorrules: "Dependency Inversion: Depend on abstractions, not concretions"
- .cursorrules: "Service Interfaces: Define clear contracts"

**Recommendation**: Define `IRoslynReplService` interface first:
```csharp
public interface IRoslynReplService : IDisposable
{
    bool IsInitialized { get; }
    void Initialize();
    void Reset();
    Task<ReplResult> EvaluateAsync(string code, CancellationToken ct = default);
    Task<IReadOnlyList<ReplCompletionItem>> GetCompletionsAsync(
        string code, int position, CancellationToken ct = default);
    IReadOnlyList<ReplVariable> GetVariables();
}
```

---

### 🟠 ISSUE-3: ConsoleService Becomes Bloated with REPL Routing

**Location**: Design Section 3.1 - High-Level Design

**Problem**: Design adds REPL routing logic directly to ConsoleService, violating SRP. ConsoleService already handles:
- Visibility toggling
- Command execution
- Output buffering
- Completion routing
- History management

Adding "detect input type and route" makes it do too much.

**Recommendation**: Create `ConsoleInputRouter` to handle input classification:
```csharp
public interface IConsoleInputRouter
{
    ConsoleInputType ClassifyInput(string input);
    // Returns: Command, CSharpCode, or Unknown
}
```

---

### 🟠 ISSUE-4: Circular Dependency Risk

**Location**: Design Section 3.2.2 - RoslynGlobals

**Problem**: RoslynGlobals takes `IScriptApiProvider` which provides access to game systems. If scripts can call `Apis.Player.GetPlayerEntity()` and the PlayerApi creates queries, we risk:
1. Scripts creating ECS queries in hot paths
2. Potential circular references if scripts trigger events that re-enter the console

**Recommendation**:
1. Document clearly that script evaluation is NOT a hot path
2. Add re-entrancy guard to prevent recursive evaluation
3. Consider making APIs return snapshots/copies, not live references

---

### 🟡 ISSUE-5: No Clear Ownership of RoslynReplService Lifecycle

**Location**: Design Section 3.2.1

**Problem**: Design doesn't specify who creates, owns, and disposes `RoslynReplService`. Is it:
- Created per ConsoleService?
- Singleton?
- Created on-demand?

**Recommendation**: Specify lifecycle:
- Create in `ConsoleService` constructor
- Store as `private readonly IRoslynReplService _replService`
- Dispose in `ConsoleService.Dispose()`

---

## 2. Arch ECS / Event Issues

### 🔴 ISSUE-6: QueryDescription Created in Methods

**Location**: Design Section 3.2.2 - Entity Query Helpers

**Problem**: Design shows `FindEntity<T>()` creating QueryDescription inline:
```csharp
public Entity? FindEntity<T>() where T : struct
{
    var query = new QueryDescription().WithAll<T>();  // ❌ ALLOCATION!
    World.Query(in query, ...);
}
```

**Violation**: .cursorrules: "NEVER create QueryDescription in Update/Render methods - always cache them"

**Clarification**: While REPL isn't a "hot path", this sets a bad pattern and could cause issues if users call `FindEntity` in a loop.

**Recommendation**: Use query caching pattern:
```csharp
private static readonly ConcurrentDictionary<Type, QueryDescription> _queryCache = new();

public Entity? FindEntity<T>() where T : struct
{
    var query = _queryCache.GetOrAdd(typeof(T), _ => new QueryDescription().WithAll<T>());
    // ...
}
```

---

### 🔴 ISSUE-7: Event Sending Without Proper Ref Semantics

**Location**: Design Section 3.2.2 - Event Helpers

**Problem**: Design shows:
```csharp
public void Send<T>(T evt) where T : struct
{
    EventBus.Send(ref evt);  // evt is already a copy!
}
```

The `evt` parameter is passed by value, then passed by ref to EventBus. This means:
1. Event is copied when calling `Send<T>(evt)`
2. Any modifications by ref handlers won't affect caller's copy

**Violation**: .cursorrules: "Use `EventBus.Send(ref evt)` to broadcast events (takes ref parameter)"

**Recommendation**: Either:
1. Only provide `SendRef<T>(ref T evt)` method
2. Or document clearly that `Send<T>` creates a copy

---

### 🟠 ISSUE-8: Direct World Access Bypasses ECS Patterns

**Location**: Design Section 3.2.2 - RoslynGlobals

**Problem**: Exposing `World` directly allows scripts to:
- Create entities without proper initialization
- Destroy entities without cleanup
- Modify components without triggering events
- Bypass system-managed state

**Recommendation**: Consider:
1. Expose `IWorldReader` for read-only operations
2. Route mutations through specific helper methods that can validate/log
3. Add `[Obsolete("Use helper methods")]` to `World` property with warning

---

### 🟡 ISSUE-9: No Event Subscription Support

**Location**: Design Section 3.2.2 - Event Helpers

**Problem**: Design only supports sending events, not subscribing. Scripts can't react to game events.

**Consideration**: This might be intentional (scripts are one-shot evaluations), but for debugging it's useful to:
- Subscribe to events temporarily
- Log events matching a pattern
- Count event occurrences

**Recommendation**: Add optional subscription support with automatic cleanup:
```csharp
public IDisposable OnEvent<T>(Action<T> handler) where T : struct
{
    var sub = EventBus.Subscribe(handler);
    _activeSubscriptions.Add(sub);  // Cleaned up on Reset()
    return sub;
}
```

---

## 3. SOLID / DRY / SRP Issues

### 🔴 ISSUE-10: ReplResult Mixes Concerns

**Location**: Design Section 3.2.1 - ReplResult Structure

**Problem**: `ReplResult` contains:
- Success/failure state
- Return value
- Error message
- Diagnostics
- Execution time
- IsStatement flag

This mixes execution outcome, compilation diagnostics, and performance metrics.

**Recommendation**: Split into focused types:
```csharp
public record ReplExecutionResult
{
    public bool Success { get; init; }
    public object? ReturnValue { get; init; }
    public Type? ReturnType { get; init; }
    public bool IsStatement { get; init; }
}

public record ReplDiagnostics
{
    public string? Error { get; init; }
    public IReadOnlyList<string> Warnings { get; init; }
}

public record ReplMetrics
{
    public double ExecutionTimeMs { get; init; }
    public long MemoryAllocated { get; init; }
}
```

Or keep as one but document it's a "result envelope" pattern.

---

### 🟠 ISSUE-11: DRY Violation - Duplicate Component Access Patterns

**Location**: Design Section 3.2.2

**Problem**: Design has overlapping methods:
- `Get<T>(Entity entity)` and `Get<T>(int entityId)` - same logic, different input
- `Inspect(Entity entity)` and `Inspect(int entityId)` - same logic, different input

**Recommendation**: Use single method with overload that converts:
```csharp
public T Get<T>(Entity entity) where T : struct => World.Get<T>(entity);
public T Get<T>(int entityId) where T : struct => Get<T>(GetEntity(entityId));
```

---

### 🟠 ISSUE-12: Open/Closed Principle Violation in Input Detection

**Location**: Design Section 3.2.3 - Input Detection Strategy

**Problem**: Input detection logic is hardcoded with if/else chains. Adding new input types (e.g., Lua, special commands) requires modifying the detector.

**Recommendation**: Use strategy pattern:
```csharp
public interface IInputClassifier
{
    int Priority { get; }
    bool CanClassify(string input);
    ConsoleInputType Classify(string input);
}

// Implementations:
// - CommandClassifier (checks registry)
// - CSharpPrefixClassifier (checks for ">")
// - CSharpFallbackClassifier (attempts parse)
```

---

### 🟡 ISSUE-13: Magic Strings/Characters

**Location**: Design Section 3.2.3

**Problem**: Design uses magic characters:
- `>` for C# prefix
- `#` for alternative prefix

**Violation**: .cursorrules: "Magic numbers and strings - use constants or configuration"

**Recommendation**: Define constants:
```csharp
public static class ConsoleConstants
{
    public const char CSharpPrefix = '>';
    public const char AlternativePrefix = '#';
    public const int DefaultCompletionLimit = 50;
    public const int DefaultEvaluationTimeoutMs = 5000;
}
```

---

## 4. .cursorrules Specific Violations

### 🔴 ISSUE-14: Missing XML Documentation

**Location**: Entire Design

**Problem**: Design doesn't emphasize XML documentation requirements. All public APIs must have:
- `<summary>`
- `<param>`
- `<returns>`
- `<exception>`

**Violation**: .cursorrules: "Document all public APIs with XML comments"

**Recommendation**: Add documentation requirements to implementation phases.

---

### 🔴 ISSUE-15: No IDisposable Implementation Details

**Location**: Design Section 3.2.1

**Problem**: Design mentions `IDisposable` but doesn't follow .cursorrules dispose pattern:
- No `Dispose(bool disposing)` method
- No `_disposed` flag
- No `GC.SuppressFinalize(this)`

**Violation**: .cursorrules: "Use standard dispose pattern with protected Dispose(bool disposing) method"

**Recommendation**: Specify full dispose pattern:
```csharp
public sealed class RoslynReplService : IRoslynReplService
{
    private bool _disposed;

    public void Dispose() => Dispose(true);

    private void Dispose(bool disposing)
    {
        if (!_disposed && disposing)
        {
            _scriptState = null;
            _globals = null;
            // Clean up subscriptions if any
        }
        _disposed = true;
    }
}
```

---

### 🟠 ISSUE-16: Fallback Behavior in Error Handling

**Location**: Design Section 5 - Safety Considerations

**Problem**: Design mentions "restricted operations" that would be blocked in release builds, but doesn't specify what happens when blocked - this could lead to silent failures.

**Violation**: .cursorrules: "NEVER introduce fallback code - code should fail fast with clear errors"

**Recommendation**: Specify fail-fast behavior:
```csharp
#if !DEBUG
throw new InvalidOperationException(
    "C# script evaluation is disabled in release builds. " +
    "Use predefined console commands instead.");
#endif
```

---

### 🟡 ISSUE-17: Missing Namespace Specification

**Location**: Entire Design

**Problem**: Design doesn't specify namespaces for new classes.

**Violation**: .cursorrules: "Match namespace to folder structure"

**Recommendation**: Specify namespaces:
```
MonoBall.Core.Diagnostics.Console.Scripting/
├── IRoslynReplService.cs
├── RoslynReplService.cs
├── RoslynGlobals.cs
├── ReplResult.cs
├── ReplCompletionItem.cs
└── Helpers/
    ├── ReplOutputHelper.cs
    ├── ReplEntityHelper.cs
    └── ReplEventHelper.cs
```

---

## 5. Additional Concerns

### 🟡 CONCERN-1: Thread Safety

**Location**: Design Section 3.2.1

**Question**: Is `RoslynReplService` thread-safe? Can two evaluations run concurrently?

**Recommendation**: Either:
1. Make service explicitly single-threaded with lock
2. Or support concurrent evaluations with proper state isolation

---

### 🟡 CONCERN-2: Memory Growth

**Location**: Design Section 3.2.1

**Question**: `ScriptState` grows with each evaluation as variables accumulate. What's the cleanup strategy?

**Recommendation**: Add `Reset()` call after N evaluations or on user command.

---

### 🟡 CONCERN-3: Assembly Loading

**Location**: Design dependencies

**Question**: Each Roslyn script compilation creates a new assembly. These can't be unloaded in .NET (without AssemblyLoadContext). Could cause memory growth over time.

**Recommendation**:
1. Use `AssemblyLoadContext.Unload()` if possible
2. Or document limitation and recommend periodic restart
3. Or use expression evaluation mode for simple expressions

---

## 6. Summary of Required Changes

### Before Implementation (CRITICAL)

| Issue | Fix |
|-------|-----|
| ISSUE-1 | Split RoslynGlobals into focused helpers |
| ISSUE-2 | Define IRoslynReplService interface |
| ISSUE-6 | Add query caching |
| ISSUE-7 | Fix event ref semantics |
| ISSUE-14 | Add XML documentation requirements |
| ISSUE-15 | Specify dispose pattern |

### High Priority (Before PR)

| Issue | Fix |
|-------|-----|
| ISSUE-3 | Create ConsoleInputRouter |
| ISSUE-4 | Add re-entrancy guard |
| ISSUE-8 | Consider read-only World access |
| ISSUE-10 | Document ReplResult as envelope pattern |
| ISSUE-16 | Specify fail-fast for release builds |

### Medium Priority (Can Defer)

| Issue | Fix |
|-------|-----|
| ISSUE-5 | Document lifecycle ownership |
| ISSUE-9 | Consider event subscription support |
| ISSUE-11 | Consolidate duplicate methods |
| ISSUE-12 | Consider strategy pattern for classification |
| ISSUE-13 | Extract constants |
| ISSUE-17 | Add namespace specifications |

---

## 7. Recommended Revised Architecture

```
Console/Scripting/
├── Interfaces/
│   ├── IRoslynReplService.cs
│   ├── IConsoleInputRouter.cs
│   └── IReplContextProvider.cs
├── Services/
│   ├── RoslynReplService.cs          # Implements IRoslynReplService
│   └── ConsoleInputRouter.cs         # Implements IConsoleInputRouter
├── Context/
│   ├── RoslynGlobals.cs              # Minimal, composes helpers
│   ├── ReplOutputHelper.cs           # Print, Log, Error, Dump
│   ├── ReplEntityHelper.cs           # FindEntity, Inspect, List
│   ├── ReplComponentHelper.cs        # Get, Set, Has
│   └── ReplEventHelper.cs            # Send events
├── Models/
│   ├── ReplResult.cs                 # Execution result envelope
│   ├── ReplCompletionItem.cs         # Completion data
│   └── ReplVariable.cs               # Variable info
└── Constants/
    └── ReplConstants.cs              # Magic strings/numbers
```

---

## 8. Next Steps

1. **Update design document** with fixes for CRITICAL issues
2. **Create interface definitions** before implementation
3. **Review revised design** with team
4. **Proceed with phased implementation**

---

**Version**: 1.0
**Last Updated**: 2026-01-06
