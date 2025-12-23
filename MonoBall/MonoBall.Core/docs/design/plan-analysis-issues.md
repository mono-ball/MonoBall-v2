# Plan Analysis: Issues and Discrepancies

**Date:** 2025-01-XX  
**Plan:** Flags and Variables System Implementation  
**Status:** Issues Identified

---

## Critical Issues

### 1. QueryDescription Missing FlagVariableMetadataComponent

**Issue**: The `GameStateQuery` in the design only includes `FlagsComponent` and `VariablesComponent`, but the singleton entity is created with three components including `FlagVariableMetadataComponent`.

**Design Document** (Line 407-408):
```csharp
private static readonly QueryDescription GameStateQuery = new QueryDescription()
    .WithAll<FlagsComponent, VariablesComponent>();
```

**But Entity Creation** (Line 438-442):
```csharp
_gameStateEntity = _world.Create(
    CreateFlagsComponent(),
    CreateVariablesComponent(),
    CreateMetadataComponent()  // ⚠️ This component is created but not in query
);
```

**Problem**: 
- Query won't find the singleton entity if it has all three components
- Metadata operations assume the component exists but query doesn't verify it

**Fix Required**:
```csharp
private static readonly QueryDescription GameStateQuery = new QueryDescription()
    .WithAll<FlagsComponent, VariablesComponent, FlagVariableMetadataComponent>();
```

**Impact**: **HIGH** - Singleton entity lookup will fail, breaking the entire system.

---

### 2. VisibilityFlagSystem Query Missing RenderableComponent

**Issue**: The `VisibilityFlagSystem` query only includes `NpcComponent`, but it needs to update `RenderableComponent.IsVisible`.

**Design Document** (Line 1048-1049):
```csharp
_queryDescription = new QueryDescription()
    .WithAll<NpcComponent>();
```

**But Update Method** (Line 1058-1066):
```csharp
World.Query(in _queryDescription, (ref NpcComponent npc) =>
{
    // ... 
    // Update RenderableComponent visibility based on flag
    // ⚠️ But RenderableComponent is not in query!
});
```

**Problem**: 
- Cannot access `RenderableComponent` in the query
- Cannot update `IsVisible` property
- System won't function

**Fix Required**:
```csharp
_queryDescription = new QueryDescription()
    .WithAll<NpcComponent, RenderableComponent>();
```

**And Update Method**:
```csharp
World.Query(in _queryDescription, (Entity entity, ref NpcComponent npc, ref RenderableComponent render) =>
{
    if (string.IsNullOrWhiteSpace(npc.VisibilityFlag))
        return;

    bool flagValue = _flagVariableService.GetFlag(npc.VisibilityFlag);
    render.IsVisible = flagValue;  // ✅ Now we can update visibility
});
```

**Impact**: **HIGH** - VisibilityFlagSystem won't work at all.

---

### 3. MapLoaderSystem Integration Incomplete

**Issue**: The plan says to "check visibility flags when loading NPCs" but doesn't specify:
- When to check (before or after entity creation)
- What to do if flag is false (don't add RenderableComponent vs add with IsVisible=false)
- How to handle flag changes after NPC is loaded

**Current MapLoaderSystem** (Line 841-846):
```csharp
new Components.RenderableComponent
{
    IsVisible = true,  // ⚠️ Always true, ignores flags
    RenderOrder = npcDef.Elevation,
    Opacity = 1.0f,
}
```

**Design Document** (Line 974-983):
```csharp
// In MapLoaderSystem when creating NPC entities
if (!string.IsNullOrWhiteSpace(npcDef.VisibilityFlag))
{
    bool isVisible = _flagVariableService.GetFlag(npcDef.VisibilityFlag);
    if (!isVisible)
    {
        // Don't add RenderableComponent, or add with IsVisible = false
        // ⚠️ Ambiguous - which approach?
    }
}
```

**Problem**: 
- Design is ambiguous about approach
- Need to decide: always add RenderableComponent with IsVisible=false, or conditionally add it
- VisibilityFlagSystem expects RenderableComponent to exist

**Fix Required**: 
- Always add `RenderableComponent` but set `IsVisible` based on flag value
- This allows VisibilityFlagSystem to update it later
- Document the decision clearly

**Impact**: **MEDIUM** - NPCs may not respect visibility flags on initial load.

---

### 4. Component Registration Location Unspecified

**Issue**: Plan says "location TBD - may need to check where Arch.Persistence is initialized" but doesn't provide guidance.

**Plan** (Line 200):
```
- Update component registration (location TBD - may need to check where Arch.Persistence is initialized)
```

**Problem**: 
- No clear place to register components
- Arch.Persistence may not be used yet in codebase
- Components need registration for serialization to work

**Fix Required**: 
- Check if Arch.Persistence is currently used
- If not, document that components should be registered when persistence is implemented
- If yes, find registration location and specify it
- Alternative: Register components in `GameServices.Initialize()` or `SystemManager.Initialize()`

**Impact**: **MEDIUM** - Save/load won't work until registration is done.

---

### 5. Missing Using Statements in Service Implementation

**Issue**: The plan doesn't specify required using statements for `FlagVariableService`.

**Design Document** shows:
```csharp
using System;
using System.Collections.Generic;
using Arch.Core;
using MonoBall.Core.ECS.Components;
using MonoBall.Core.ECS.Events;
using MonoBall.Core.Logging;
```

**But Plan Doesn't Mention**: Required using statements for:
- `Arch.Core` (Entity, World, QueryDescription)
- `MonoBall.Core.ECS.EventBus` (static EventBus class)
- `System.Text.Json` (for variable serialization)

**Fix Required**: Add explicit using statement requirements to plan.

**Impact**: **LOW** - Easy to fix during implementation, but should be documented.

---

### 6. Entity Flag/Variable Events Not Documented

**Issue**: The design shows entity flag changes fire `FlagChangedEvent`, but this may cause confusion.

**Design Document** (Line 823-829):
```csharp
if (oldValue != value)
{
    var flagChangedEvent = new FlagChangedEvent
    {
        FlagId = flagId,
        OldValue = oldValue,
        NewValue = value
    };
    EventBus.Send(ref flagChangedEvent);
}
```

**Problem**: 
- Entity flag changes fire the same event as global flag changes
- Systems listening to `FlagChangedEvent` can't distinguish between global and entity flags
- May cause unnecessary updates

**Consideration**: 
- This might be intentional (unified event system)
- Or might need separate events: `EntityFlagChangedEvent`
- Document the decision

**Impact**: **LOW** - Design decision, but should be explicit.

---

### 7. VisibilityFlagSystem Update Logic Incomplete

**Issue**: The design shows incomplete implementation with comments like "Implementation depends on how visibility is managed".

**Design Document** (Line 1055-1066):
```csharp
public override void Update(in float deltaTime)
{
    // Check all NPCs with visibility flags on each update
    World.Query(in _queryDescription, (ref NpcComponent npc) =>
    {
        if (string.IsNullOrWhiteSpace(npc.VisibilityFlag))
            return;

        bool flagValue = _flagVariableService.GetFlag(npc.VisibilityFlag);
        // Update RenderableComponent visibility based on flag
        // (Implementation depends on how visibility is managed)
    });
}
```

**Problem**: 
- Update method doesn't actually update anything
- Comment suggests uncertainty about implementation
- Need concrete implementation

**Fix Required**: 
- Query must include `RenderableComponent`
- Set `render.IsVisible = flagValue`
- Document that this runs every frame (may want to optimize later)

**Impact**: **HIGH** - System won't function without complete implementation.

---

### 8. Missing Null Checks in Service Implementation

**Issue**: The service implementation assumes components exist but doesn't always verify.

**Design Document** (Line 911):
```csharp
ref FlagVariableMetadataComponent metadataComponent = ref _world.Get<FlagVariableMetadataComponent>(_gameStateEntity);
metadataComponent.FlagMetadata ??= new Dictionary<string, FlagMetadata>();
```

**Problem**: 
- `_world.Get<T>()` will throw if component doesn't exist
- Should use `_world.Has<T>()` check first, or ensure component always exists
- Entity flag/variable operations check `Has<T>()` but global operations assume singleton exists

**Fix Required**: 
- Ensure singleton entity always has all three components
- Document that `EnsureInitialized()` guarantees component existence
- Add defensive checks or document assumptions

**Impact**: **MEDIUM** - Could cause runtime exceptions if initialization fails.

---

## Minor Issues

### 9. Plan Doesn't Specify Namespace for Metadata Structs

**Issue**: `FlagMetadata` and `VariableMetadata` are shown as separate files but plan doesn't specify if they're in the same namespace as components.

**Design Document**: Shows them as part of `FlagVariableMetadataComponent.cs` file, but plan lists them as separate files.

**Fix Required**: Clarify if they're:
- Separate files in `MonoBall.Core.ECS.Components` namespace
- Or nested in `FlagVariableMetadataComponent.cs` file

**Impact**: **LOW** - File organization issue.

---

### 10. Missing Error Handling Documentation

**Issue**: Plan doesn't specify error handling approach for:
- Invalid flag/variable IDs
- Missing components on entities
- Serialization failures

**Fix Required**: Document error handling strategy:
- Validation errors throw `ArgumentException`
- Missing components return default values (for Get operations)
- Serialization failures return default (for GetVariable)

**Impact**: **LOW** - Implementation detail, but should be consistent.

---

### 11. Plan Doesn't Address Entity Component Addition Pattern

**Issue**: Entity flag/variable operations use `World.Add()` if component doesn't exist, but this pattern isn't documented in the plan.

**Design Document** (Line 789-791):
```csharp
if (!_world.Has<EntityFlagsComponent>(entity))
{
    _world.Add(entity, CreateEntityFlagsComponent());
}
```

**Problem**: 
- Plan doesn't mention this pattern
- Should document that entity components are added lazily
- May need to handle edge cases (entity destroyed, etc.)

**Impact**: **LOW** - Implementation detail, but good to document.

---

## Summary of Required Fixes

### Must Fix Before Implementation:

1. ✅ **Fix GameStateQuery** - Include `FlagVariableMetadataComponent` in query
2. ✅ **Fix VisibilityFlagSystem Query** - Include `RenderableComponent` in query
3. ✅ **Complete VisibilityFlagSystem Implementation** - Actually update `IsVisible` property
4. ✅ **Specify MapLoaderSystem Integration** - Always add RenderableComponent, set IsVisible based on flag

### Should Fix:

5. ⚠️ **Find Component Registration Location** - Determine where Arch.Persistence registration happens
6. ⚠️ **Add Using Statements** - Document required imports
7. ⚠️ **Document Entity Event Behavior** - Clarify if entity flags use same events

### Nice to Have:

8. 💡 **Clarify Metadata Struct Organization** - Separate files vs nested
9. 💡 **Document Error Handling** - Consistent error handling strategy
10. 💡 **Document Entity Component Pattern** - Lazy addition pattern

---

## Recommended Plan Updates

1. Update Phase 4.2 to include correct `GameStateQuery` with all three components
2. Update Phase 5.1 to include `RenderableComponent` in query and complete implementation
3. Update Phase 6.4 to specify: always add RenderableComponent, set IsVisible from flag
4. Update Phase 6.1 to either find registration location or document as future work
5. Add Phase 4.3: Document required using statements and dependencies

