# InteractionId Design - API Location Analysis

## Executive Summary

This document analyzes the proposed helper methods in the interaction design against established API design principles and .cursorrules compliance.

---

## Established Rules (from `script-api-design.md`)

### ScriptBase Rules
- ✅ **Should be in ScriptBase**: Operates on script's own entity, convenience wrappers, requires context
- ❌ **Should NOT be in ScriptBase**: Operates on other entities, game system operations, pure functions

### API Rules
- ✅ **Should be in API**: Operates on any entity (passed as parameter), game systems, cross-entity operations
- ❌ **Should NOT be in API**: Script-specific convenience wrapper, pure function

### Utilities Rules
- ✅ **Should be in Utilities**: Pure functions, no side effects, no context needed
- ❌ **Should NOT be in Utilities**: Requires script context, operates on entities

---

## Proposed Methods Analysis

### 1. `OnInteraction(Action<InteractionTriggeredEvent>)`

**Proposed Location**: ScriptBase  
**Analysis**: ✅ **CORRECT**

**Rationale**:
- Operates on script's own entity (filters events by entity)
- Convenience wrapper for common pattern
- Requires script context (Context.Entity)
- Reduces boilerplate (entity filtering)

**Compliance**: ✅ Follows ScriptBase rules

---

### 2. `FacePlayer(Entity playerEntity)`

**Proposed Location**: ScriptBase  
**Analysis**: ⚠️ **VIOLATES RULES**

**Issue**: 
- Operates on **other entity** (playerEntity parameter)
- Cross-entity operation (NPC → Player)
- According to rules: "❌ Operates on other entities" → Should be in API

**Current API**: `INpcApi.FaceEntity(Entity npc, Entity target)` already exists!

**Correct Implementation**:
```csharp
// In ScriptBase - convenience wrapper that delegates to API
protected void FacePlayer(Entity playerEntity)
{
    RequireEntity();
    Context.Apis.Npc.FaceEntity(Context.Entity.Value, playerEntity);
}
```

**Compliance**: ⚠️ **PARTIALLY CORRECT** - Should delegate to API, not implement directly

---

### 3. `GetPlayerEntity()`

**Proposed Location**: ScriptBase  
**Analysis**: ⚠️ **VIOLATES RULES**

**Issue**:
- Queries game world for player entity
- Game system operation (not script-specific)
- According to rules: "❌ Game system operation" → Should be in API

**Correct Location**: `IPlayerApi.GetPlayerEntity()` or `IWorldApi.GetPlayerEntity()`

**Compliance**: ❌ **INCORRECT LOCATION** - Should be in API

---

### 4. `GetTilePosition()` (no parameters)

**Proposed Location**: ScriptBase  
**Analysis**: ✅ **CORRECT**

**Rationale**:
- Operates on script's own entity (`Context.Entity`)
- Convenience wrapper
- Requires script context

**Compliance**: ✅ Follows ScriptBase rules

---

### 5. `GetTilePosition(Entity entity)` (with parameter)

**Proposed Location**: ScriptBase  
**Analysis**: ⚠️ **VIOLATES RULES**

**Issue**:
- Operates on **any entity** (passed as parameter)
- According to rules: "❌ Operates on other entities" → Should be in API

**Current API**: `INpcApi.GetPosition(Entity npc)` already exists! (returns `PositionComponent`)

**Correct Implementation**:
```csharp
// In ScriptBase - convenience wrapper for own entity only
protected (int X, int Y)? GetTilePosition()
{
    RequireEntity();
    var pos = Context.Apis.Npc.GetPosition(Context.Entity.Value);
    if (pos == null) return null;
    return (pos.Value.X, pos.Value.Y);
}

// For other entities, use API directly:
// var pos = Context.Apis.Npc.GetPosition(otherEntity);
```

**Compliance**: ⚠️ **PARTIALLY CORRECT** - Overload with parameter should be removed, use API instead

---

### 6. `GetInteractionCount()` / `IncrementInteractionCount()`

**Proposed Location**: ScriptBase  
**Analysis**: ✅ **CORRECT**

**Rationale**:
- Operates on script's own entity (uses `Get<T>()` / `Set<T>()` which are entity-specific)
- Convenience wrapper for common pattern
- Requires script context (state management)
- Reduces boilerplate

**Compliance**: ✅ Follows ScriptBase rules

---

### 7. `ShowDialogue(string message)`

**Proposed Location**: ScriptBase  
**Analysis**: ⚠️ **QUESTIONABLE**

**Issue**:
- Thin wrapper around `MessageBox.ShowMessage()`
- Game system operation (message box system)
- According to rules: "❌ Game system operation" → Should be in API

**Current API**: `IMessageBoxApi.ShowMessage(string message)` already exists!

**Options**:
1. **Remove wrapper** - Use `Context.Apis.MessageBox.ShowMessage()` directly
2. **Keep as convenience** - Very thin wrapper, but violates "game system operation" rule

**Recommendation**: Remove wrapper - it's too thin to justify violating the rule.

**Compliance**: ⚠️ **QUESTIONABLE** - Thin wrapper, but violates "game system operation" rule

---

### 8. `ShowDialogueByCount(string first, string second, string default)`

**Proposed Location**: ScriptBase  
**Analysis**: ✅ **CORRECT**

**Rationale**:
- Combines multiple operations (state tracking + message display)
- Convenience wrapper for common pattern
- Requires script context (interaction count state)
- Reduces significant boilerplate

**Implementation**:
```csharp
protected void ShowDialogueByCount(string first, string second, string default)
{
    var count = IncrementInteractionCount();
    string message = count == 1 ? first : count == 2 ? second : default;
    Context.Apis.MessageBox.ShowMessage(message);
}
```

**Compliance**: ✅ Follows ScriptBase rules (combines entity-specific state with API call)

---

## Summary of Issues

### ❌ Critical Issues

1. **`GetPlayerEntity()`** - Should be in API, not ScriptBase
   - **Fix**: Add to `IPlayerApi` or create `IWorldApi`

2. **`GetTilePosition(Entity entity)`** - Should use API, not ScriptBase
   - **Fix**: Remove overload, use `Context.Apis.Npc.GetPosition(entity)` directly

3. **`ShowDialogue(string)`** - Thin wrapper violates rules
   - **Fix**: Remove wrapper, use `Context.Apis.MessageBox.ShowMessage()` directly

### ⚠️ Partial Issues

4. **`FacePlayer(Entity playerEntity)`** - Should delegate to API
   - **Fix**: Implement as convenience wrapper that calls `Context.Apis.Npc.FaceEntity()`

---

## Corrected API Design

### ScriptBase Methods (Entity-Specific Convenience)

```csharp
// ✅ CORRECT - Event subscription with entity filtering
protected void OnInteraction(Action<InteractionTriggeredEvent> handler)

// ✅ CORRECT - Own entity position
protected (int X, int Y)? GetTilePosition()

// ✅ CORRECT - State tracking for own entity
protected int GetInteractionCount()
protected int IncrementInteractionCount()

// ✅ CORRECT - Convenience wrapper delegating to API
protected void FacePlayer(Entity playerEntity)
{
    RequireEntity();
    Context.Apis.Npc.FaceEntity(Context.Entity.Value, playerEntity);
}

// ✅ CORRECT - Combines state tracking + API call
protected void ShowDialogueByCount(string first, string second, string default)
{
    var count = IncrementInteractionCount();
    string message = count == 1 ? first : count == 2 ? second : default;
    Context.Apis.MessageBox.ShowMessage(message);
}
```

### API Methods (Cross-Entity Operations)

```csharp
// Add to IPlayerApi or create IWorldApi
public interface IPlayerApi
{
    /// <summary>
    /// Gets the player entity from the world.
    /// </summary>
    /// <returns>The player entity, or null if not found.</returns>
    Entity? GetPlayerEntity();
}

// Already exists in INpcApi:
// - FaceEntity(Entity npc, Entity target) ✅
// - GetPosition(Entity npc) ✅

// Already exists in IMessageBoxApi:
// - ShowMessage(string message) ✅
```

### Removed Methods

- ❌ `GetPlayerEntity()` from ScriptBase → Use `Context.Apis.Player.GetPlayerEntity()`
- ❌ `GetTilePosition(Entity entity)` from ScriptBase → Use `Context.Apis.Npc.GetPosition(entity)`
- ❌ `ShowDialogue(string)` from ScriptBase → Use `Context.Apis.MessageBox.ShowMessage(message)`

---

## Updated Example Scripts

### Before (Violates Rules)

```csharp
private void OnInteractionTriggered(InteractionTriggeredEvent evt)
{
    // ❌ GetPlayerEntity() should be in API
    var player = GetPlayerEntity();
    
    // ❌ FacePlayer() should delegate to API
    FacePlayer(evt.PlayerEntity);
    
    // ❌ ShowDialogue() is thin wrapper, should use API directly
    ShowDialogue("Hello!");
}
```

### After (Compliant)

```csharp
private void OnInteractionTriggered(InteractionTriggeredEvent evt)
{
    // ✅ Use API for cross-entity operations
    var player = Context.Apis.Player.GetPlayerEntity();
    
    // ✅ FacePlayer() delegates to API (convenience wrapper)
    FacePlayer(evt.PlayerEntity);
    
    // ✅ Use API directly for game system operations
    Context.Apis.MessageBox.ShowMessage("Hello!");
    
    // ✅ Or use convenience wrapper that combines operations
    ShowDialogueByCount("First", "Second", "Default");
}
```

---

## .cursorrules Compliance

### Check Against Repository Rules

Based on the repository structure and existing patterns:

1. ✅ **No Backward Compatibility** - New methods, no breaking changes
2. ✅ **No Fallback Code** - Methods throw exceptions for invalid state
3. ✅ **XML Documentation** - All methods documented
4. ✅ **Namespace** - Methods in correct namespaces
5. ✅ **File Organization** - Methods in appropriate classes

### Potential Issues

1. ⚠️ **DRY Violation** - Some methods duplicate API functionality
   - **Fix**: Ensure ScriptBase methods delegate to APIs, don't duplicate logic

2. ⚠️ **API Design Consistency** - Need to ensure all APIs follow same patterns
   - **Fix**: Review all proposed APIs against existing API patterns

---

## Recommendations

### High Priority

1. **Move `GetPlayerEntity()` to API**
   - Add to `IPlayerApi` or create `IWorldApi`
   - Update design document

2. **Remove `GetTilePosition(Entity entity)` overload**
   - Use `Context.Apis.Npc.GetPosition(entity)` directly
   - Keep only `GetTilePosition()` for own entity

3. **Remove `ShowDialogue(string)` wrapper**
   - Use `Context.Apis.MessageBox.ShowMessage()` directly
   - Keep only `ShowDialogueByCount()` which combines operations

### Medium Priority

4. **Ensure `FacePlayer()` delegates to API**
   - Verify implementation delegates to `Context.Apis.Npc.FaceEntity()`
   - Document that it's a convenience wrapper

5. **Add `GetPlayerEntity()` to API**
   - Implement in `PlayerApi` or create `WorldApi`
   - Update API provider

---

## Conclusion

The proposed helper methods have **4 violations** of established API design rules:

1. ❌ `GetPlayerEntity()` - Should be in API
2. ❌ `GetTilePosition(Entity entity)` - Should use API
3. ❌ `ShowDialogue(string)` - Thin wrapper, should use API directly
4. ⚠️ `FacePlayer()` - Should delegate to API (may already do this)

**Corrected design**:
- Keep entity-specific convenience methods in ScriptBase
- Move cross-entity operations to APIs
- Remove thin wrappers that don't add value
- Ensure ScriptBase methods delegate to APIs when appropriate

