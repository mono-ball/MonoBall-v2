# Roslyn Console Integration Design Review (v3.0)

**Reviewed**: 2026-01-06
**Status**: Issues Identified
**Reviewer**: Architecture Analysis
**Design Version**: 3.0

---

## Executive Summary

The v3.0 design significantly improves upon v2.0 by composing with existing `ScriptContext` and `ScriptApiProvider` instead of duplicating functionality. However, **11 issues** remain across architecture, ECS patterns, SOLID principles, and .cursorrules compliance.

The design is **much improved** but needs refinement before implementation.

---

## Issue Severity Legend

| Severity | Description |
|----------|-------------|
| 🔴 **CRITICAL** | Will cause compile errors or runtime bugs |
| 🟠 **HIGH** | Significant design flaw, fix before implementation |
| 🟡 **MEDIUM** | Improvement recommended, can defer |
| 🟢 **LOW** | Minor suggestion |

---

## 1. Architecture Issues

### 🔴 ISSUE-1: IConsoleContext.WriteInfo Does Not Exist (Compile Error)

**Location**: Design Section 4.3 - ReplGlobals, line 314

**Problem**: The design shows:
```csharp
public void Log(string text) => _console.WriteInfo(text);
```

But `IConsoleContext` does NOT have a `WriteInfo` method. Available methods are:
- `WriteLine(string, ConsoleOutputLevel)`
- `WriteSuccess(string)`
- `WriteWarning(string)`
- `WriteError(string)`
- `WriteSystem(string)`
- `Clear()`

**Impact**: Code will not compile.

**Recommendation**: Change to:
```csharp
public void Log(string text) => _console.WriteSystem(text);
```

---

### 🟠 ISSUE-2: Missing Interface and Model Definitions

**Location**: Design Section 3.1, 4.2

**Problem**: The design lists 8 files to create but only shows implementation for:
- `ReplConstants.cs` ✅
- `IRoslynReplService.cs` ✅
- `ReplGlobals.cs` ✅
- `RoslynReplService.cs` ✅

Missing definitions:
- `IConsoleInputRouter.cs` ❌
- `ConsoleInputRouter.cs` ❌
- `ReplResult.cs` ❌
- `ReplVariable.cs` ❌
- `ReplCompletionItem.cs` (referenced but not listed) ❌

**Impact**: Incomplete design - implementers must guess at these contracts.

**Recommendation**: Add complete interface and model definitions:

```csharp
// IConsoleInputRouter.cs
public interface IConsoleInputRouter
{
    ConsoleInputType ClassifyInput(string input);
    string StripPrefix(string input);
}

public enum ConsoleInputType
{
    Command,
    CSharpCode,
    Unknown
}

// ReplResult.cs
public sealed class ReplResult
{
    public bool Success { get; init; }
    public object? ReturnValue { get; init; }
    public Type? ReturnType { get; init; }
    public bool IsStatement { get; init; }
    public string? ErrorMessage { get; init; }
    public IReadOnlyList<string> Diagnostics { get; init; } = Array.Empty<string>();
    public double ExecutionTimeMs { get; init; }

    public static ReplResult Ok(object? value, Type? type, double timeMs) => new()
    {
        Success = true,
        ReturnValue = value,
        ReturnType = type,
        ExecutionTimeMs = timeMs
    };

    public static ReplResult Statement(double timeMs) => new()
    {
        Success = true,
        IsStatement = true,
        ExecutionTimeMs = timeMs
    };

    public static ReplResult Fail(string error, IReadOnlyList<string>? diagnostics = null) => new()
    {
        Success = false,
        ErrorMessage = error,
        Diagnostics = diagnostics ?? Array.Empty<string>()
    };
}

// ReplVariable.cs
public sealed record ReplVariable(string Name, Type Type, object? Value);

// ReplCompletionItem.cs
public sealed record ReplCompletionItem(
    string DisplayText,
    string InsertText,
    string Kind,
    string? Description = null);
```

---

### 🟠 ISSUE-3: Inspect Method is Incomplete Placeholder

**Location**: Design Section 4.3 - ReplGlobals, lines 377-389

**Problem**: The `Inspect(Entity entity)` method doesn't actually inspect components:
```csharp
public void Inspect(Entity entity)
{
    _console.WriteLine($"Entity {entity.Id}:");
    // Implementation depends on how we want to expose component iteration
    // Option 1: Add GetAllComponents to ScriptContext
    // Option 2: Use reflection on known component types
    _console.WriteLine("  (Use Context.Query<T>() to inspect specific components)");
}
```

**Impact**: Feature advertised in design but not implemented.

**Recommendation**: Either:
1. Remove `Inspect()` from design and document as "future enhancement"
2. Or implement with known component types:

```csharp
public void Inspect(Entity entity)
{
    _console.WriteLine($"Entity {entity.Id}:");

    // Check common component types
    TryPrintComponent<PositionComponent>(entity, "Position");
    TryPrintComponent<GridMovement>(entity, "GridMovement");
    TryPrintComponent<SpriteSheetComponent>(entity, "SpriteSheet");
    TryPrintComponent<PlayerComponent>(entity, "Player");
    TryPrintComponent<NpcComponent>(entity, "NPC");
    // ... other common types
}

private void TryPrintComponent<T>(Entity entity, string name) where T : struct
{
    // Would need World access or ScriptContext extension
}
```

---

### 🟡 ISSUE-4: GetCompletionsAsync Not Implemented

**Location**: Design Section 4.4 - RoslynReplService, lines 605-611

**Problem**: Method returns empty array with TODO comment:
```csharp
public async Task<IReadOnlyList<ReplCompletionItem>> GetCompletionsAsync(
    string code, int position, CancellationToken ct = default)
{
    // TODO: Implement with Roslyn CompletionService
    await Task.CompletedTask;
    return Array.Empty<ReplCompletionItem>();
}
```

**Impact**: Completions won't work. Users may expect this feature.

**Recommendation**: Either:
1. Remove from interface and mark as "Phase 2" feature
2. Or document as "stub - not implemented in v1.0"

---

## 2. Arch ECS / Event Issues

### 🟡 ISSUE-5: FindEntities Allocates New List Each Call

**Location**: Design Section 4.3 - ReplGlobals, lines 364-372

**Problem**:
```csharp
public List<Entity> FindEntities<T>() where T : struct
{
    var results = new List<Entity>();  // ALLOCATION
    Context.Query<T>((Entity e, ref T _) =>
    {
        results.Add(e);
    });
    return results;
}
```

**Violation**: .cursorrules: "Avoid allocations in Update/Draw loops" and "Reuse collections in hot paths"

**Clarification**: While REPL is not a hot path, this sets a pattern that could be misused. Also, if users call this in a loop, it could cause GC pressure.

**Recommendation**: Document that this method allocates and should not be used in loops. Alternatively, provide a cached version:

```csharp
private readonly List<Entity> _findEntitiesCache = new();

/// <summary>
/// Finds all entities with component type.
/// WARNING: Returns new list - avoid calling in loops.
/// </summary>
public List<Entity> FindEntities<T>() where T : struct
{
    var results = new List<Entity>();
    Context.Query<T>((Entity e, ref T _) => results.Add(e));
    return results;
}
```

---

### 🟢 ISSUE-6: SendRef Console Output After Event Send

**Location**: Design Section 4.3 - ReplGlobals, lines 396-400

**Problem**:
```csharp
public void SendRef<T>(ref T evt) where T : struct
{
    EventBus.Send(ref evt);
    _console.WriteLine($"Event sent: {typeof(T).Name}");  // After send
}
```

If event handlers modify the event, the console message is accurate (shows what was sent), but if handlers throw, the message is never written.

**Recommendation**: Consider try/finally:
```csharp
public void SendRef<T>(ref T evt) where T : struct
{
    try
    {
        EventBus.Send(ref evt);
        _console.WriteLine($"Event sent: {typeof(T).Name}");
    }
    catch (Exception ex)
    {
        _console.WriteError($"Event {typeof(T).Name} failed: {ex.Message}");
        throw;
    }
}
```

---

## 3. SOLID / DRY / SRP Issues

### 🟠 ISSUE-7: ReplGlobals Still Has Multiple Responsibilities

**Location**: Design Section 4.3

**Problem**: Even as a "thin wrapper", ReplGlobals has 5 distinct responsibility areas:
1. Context delegation (Context property)
2. API property forwarding (9 properties: Player, Map, Movement, etc.)
3. Console output (Print, Log, Error, Dump)
4. Entity helpers (FindEntity, FindEntities, GetPlayer, Inspect)
5. Event management (SendRef, OnEvent, ClearSubscriptions)

**Violation**: .cursorrules: "Single Responsibility: Each class should have one reason to change"

**Impact**: Class has multiple reasons to change. However, this is a valid tradeoff for REPL ergonomics.

**Recommendation**: Document this as an intentional "Facade pattern" for REPL convenience. Consider adding extension methods in future if class grows further.

---

### 🟠 ISSUE-8: Bare Catch in Dump Method

**Location**: Design Section 4.3 - ReplGlobals, lines 337-340

**Problem**:
```csharp
catch
{
    _console.WriteLine($"  {prop.Name}: <error>");
}
```

**Violation**: .cursorrules: "Catch specific exceptions, not Exception unless absolutely necessary"

**Recommendation**: Catch specific exceptions:
```csharp
catch (TargetInvocationException)
{
    _console.WriteLine($"  {prop.Name}: <error reading>");
}
catch (Exception ex) when (ex is not OutOfMemoryException)
{
    _console.WriteLine($"  {prop.Name}: <error: {ex.GetType().Name}>");
}
```

---

## 4. .cursorrules Specific Violations

### 🟡 ISSUE-9: Missing XML Documentation

**Location**: Design Section 4.4 - RoslynReplService

**Problem**: Several private methods lack XML documentation:
- `ThrowIfDisposed()` (line 638-641)
- `ThrowIfNotInitialized()` (line 643-646)
- `GetAssemblyReferences()` (line 648-659)
- `GetDefaultImports()` (line 661-672)

**Violation**: While .cursorrules says "Document all public APIs", these are private. However, complex private methods benefit from documentation.

**Recommendation**: Add brief XML comments:
```csharp
/// <summary>Throws ObjectDisposedException if disposed.</summary>
private void ThrowIfDisposed()

/// <summary>Throws InvalidOperationException if not initialized.</summary>
private void ThrowIfNotInitialized()

/// <summary>Gets assembly references for Roslyn script compilation.</summary>
private static IEnumerable<Assembly> GetAssemblyReferences()

/// <summary>Gets default namespace imports for Roslyn scripts.</summary>
private static IEnumerable<string> GetDefaultImports()
```

---

### 🟡 ISSUE-10: Magic String "REPL"

**Location**: Design Section 4.4 - RoslynReplService, line 494

**Problem**:
```csharp
logger: _logger.ForContext("SourceContext", "REPL"),
```

**Violation**: .cursorrules: "Magic numbers and strings - use constants or configuration"

**Recommendation**: Use existing constant:
```csharp
logger: _logger.ForContext("SourceContext", ReplConstants.ReplScriptId),
```

Or add a new constant:
```csharp
// In ReplConstants.cs
public const string LogContextName = "REPL";
```

---

### 🟡 ISSUE-11: Thread Safety Concern

**Location**: Design Section 4.4 - RoslynReplService, lines 535-539

**Problem**: Re-entrancy check uses simple flag without synchronization:
```csharp
if (_isEvaluating)
{
    throw new InvalidOperationException(
        "Re-entrancy detected. Cannot evaluate while another evaluation is in progress.");
}
_isEvaluating = true;
```

If `EvaluateAsync` is called from multiple threads, there's a race condition.

**Recommendation**: Either:
1. Document that the service is NOT thread-safe (acceptable for UI-bound REPL)
2. Or add synchronization:

```csharp
private readonly object _evaluationLock = new();
private bool _isEvaluating;

public async Task<ReplResult> EvaluateAsync(string code, CancellationToken ct = default)
{
    lock (_evaluationLock)
    {
        if (_isEvaluating)
            throw new InvalidOperationException(...);
        _isEvaluating = true;
    }

    try { ... }
    finally
    {
        lock (_evaluationLock) { _isEvaluating = false; }
    }
}
```

---

## 5. Summary of Required Changes

### Before Implementation (CRITICAL)

| Issue | Fix |
|-------|-----|
| ISSUE-1 | Change `WriteInfo` to `WriteSystem` |
| ISSUE-2 | Add missing interface and model definitions |

### High Priority (Before PR)

| Issue | Fix |
|-------|-----|
| ISSUE-3 | Remove or properly implement `Inspect()` |
| ISSUE-7 | Document as intentional Facade pattern |
| ISSUE-8 | Fix bare catch to use specific exceptions |

### Medium Priority (Can Defer)

| Issue | Fix |
|-------|-----|
| ISSUE-4 | Document completions as Phase 2 feature |
| ISSUE-5 | Add documentation warning about allocation |
| ISSUE-6 | Consider try/finally for error handling |
| ISSUE-9 | Add XML docs to private methods |
| ISSUE-10 | Replace magic string with constant |
| ISSUE-11 | Document thread-safety constraints |

---

## 6. Comparison to v2.0 Design

| Aspect | v2.0 | v3.0 | Verdict |
|--------|------|------|---------|
| Total Files | 16 | 8 | ✅ Improved |
| Code Duplication | High | Low | ✅ Improved |
| Query Caching | Duplicated | Reused | ✅ Improved |
| API Access | Duplicated | Reused | ✅ Improved |
| SRP Compliance | Poor | Acceptable | ✅ Improved |
| Missing Definitions | N/A | 5 files | ⚠️ Needs fix |
| Compile Errors | None known | 1 (WriteInfo) | ⚠️ Needs fix |

---

## 7. Recommendation

**Proceed with implementation after addressing:**

1. ✅ Fix `WriteInfo` → `WriteSystem` (CRITICAL)
2. ✅ Add missing interface/model definitions (CRITICAL)
3. ✅ Fix bare catch in Dump method (HIGH)

The v3.0 design is significantly better than v2.0 and properly reuses existing infrastructure. The remaining issues are minor and can be fixed during implementation.

---

**Version**: 1.0
**Last Updated**: 2026-01-06
