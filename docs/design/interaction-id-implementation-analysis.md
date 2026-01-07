# InteractionId Implementation Analysis

**Date**: 2025-01-XX  
**Purpose**: Analyze the implemented interactionId system for architecture issues, Arch ECS/event issues, SOLID/DRY violations, .cursorrule violations, and inconsistencies with behavior handling.

---

## 🔴 Critical Issues

### 1. **Player Elevation Hardcoded to 0**

**Location**: `InteractionSystem.cs`, line 100

**Problem**:
```csharp
// Get elevation from PositionComponent if available, otherwise default to 0
// Note: Elevation might be stored elsewhere, but for now we'll use 0 as default
playerElevation = 0;
```

**Issues**:
- **.cursorrule violation**: Hardcoded default value violates "fail-fast" principle
- **Architecture issue**: Player elevation is not stored anywhere - NPCs have `Elevation` in `NpcComponent`, but player doesn't
- **Functional bug**: Player on elevation 3 cannot interact with NPCs on elevation 3 (will always fail elevation check)
- **Inconsistency**: NPCs have elevation stored, player doesn't

**Impact**: 
- Interactions will fail when player and NPC are on different elevations (even if both are non-zero)
- No way to verify player elevation matches interaction elevation

**Solution**:
1. **Option A**: Add `Elevation` property to `PlayerComponent` (matches NPC pattern)
2. **Option B**: Create `ElevationComponent` for all entities (more consistent)
3. **Option C**: Get elevation from `RenderableComponent.RenderOrder` if it matches elevation

**Recommended**: Option A - Add `Elevation` to `PlayerComponent` to match NPC pattern.

---

### 2. **Inefficient Script Pausing/Resuming**

**Location**: `InteractionSystem.cs`, lines 296-365

**Problem**:
```csharp
private bool PauseScript(Entity entity, string scriptDefinitionId)
{
    // Query all ScriptAttachmentComponents (Arch ECS allows multiple of same type)
    // Filter by entity ID and script definition ID
    bool found = false;
    World.Query(
        in _scriptAttachmentQuery,
        (Entity e, ref ScriptAttachmentComponent attachment) =>
        {
            // Early exit if not our entity
            if (e.Id != entity.Id)
            {
                return;
            }
            // ... rest of logic
        }
    );
}
```

**Issues**:
- **Performance**: Queries ALL entities with `ScriptAttachmentComponent` globally, then filters by entity ID
- **O(n) complexity**: Where n = total entities with scripts (could be hundreds)
- **Called frequently**: Every interaction start/end
- **Arch ECS pattern**: This matches `ScriptLifecycleSystem` pattern, but it's still inefficient

**Impact**:
- Performance bottleneck when many NPCs have scripts
- Unnecessary iteration over unrelated entities

**Solution**:
Since Arch ECS doesn't support efficient querying of multiple components of the same type on one entity, we have two options:

1. **Accept the inefficiency** (matches existing pattern in `ScriptLifecycleSystem`)
2. **Add ScriptType enum** to `ScriptAttachmentComponent` to enable filtering:
   ```csharp
   public enum ScriptType { Behavior, Interaction, Other }
   public struct ScriptAttachmentComponent {
       public ScriptType Type { get; set; } // NEW
       // ... rest
   }
   ```
   Then query with `Type == ScriptType.Behavior` filter (but Arch ECS still queries all)

**Note**: This matches the pattern used by `ScriptLifecycleSystem`, so it's consistent but not optimal.

---

### 3. **InteractionComponent Width/Height Unused**

**Location**: `InteractionComponent.cs`, `InteractionSystem.cs`

**Problem**:
- `Width` and `Height` are set to 1 tile in `MapLoaderSystem` (lines 1218-1219)
- These values are never used in `InteractionSystem` distance calculation
- Distance calculation only checks if player is 1 tile away (Manhattan distance == 1)

**Issues**:
- **DRY violation**: Unused properties add confusion
- **Design inconsistency**: Properties suggest interaction area can vary, but implementation doesn't support it
- **Future-proofing**: If we want larger interaction areas later, we need to implement this

**Impact**:
- Confusing API - why have Width/Height if they're not used?
- Cannot support larger interaction areas (e.g., 2x2 tile signs)

**Solution**:
1. **Option A**: Remove `Width` and `Height` (simplify, but loses future flexibility)
2. **Option B**: Implement width/height support in distance calculation:
   ```csharp
   // Check if player is within interaction area bounds
   int minX = interactionTileX - (interaction.Width / 2);
   int maxX = interactionTileX + (interaction.Width / 2);
   int minY = interactionTileY - (interaction.Height / 2);
   int maxY = interactionTileY + (interaction.Height / 2);
   
   if (playerTileX < minX || playerTileX > maxX || 
       playerTileY < minY || playerTileY > maxY)
   {
       return; // Outside interaction area
   }
   ```

**Recommended**: Option B - Implement width/height support for consistency and future flexibility.

---

## 🟡 Medium Priority Issues

### 4. **Duplicate Code in MapLoaderSystem**

**Location**: `MapLoaderSystem.cs`, lines 1114-1141 (behavior) vs 1225-1243 (interaction)

**Problem**:
Both behavior script attachment and interaction script attachment have nearly identical code:
- Getting script definition metadata
- Checking for null
- Creating `ScriptAttachmentComponent` with same fields
- Error messages follow same pattern

**Issues**:
- **DRY violation**: Code duplication
- **Maintenance burden**: Changes to script attachment logic must be made in two places
- **Inconsistency risk**: Two code paths might diverge over time

**Solution**:
Extract common logic into helper method:
```csharp
private ScriptAttachmentComponent CreateScriptAttachmentComponent(
    string scriptDefinitionId,
    string npcId,
    string context) // "behavior" or "interaction"
{
    var scriptDef = _registry.GetById<ScriptDefinition>(scriptDefinitionId);
    if (scriptDef == null)
    {
        throw new InvalidOperationException(
            $"ScriptDefinition '{scriptDefinitionId}' not found for NPC '{npcId}' {context}. " +
            "Ensure the script definition exists and is loaded."
        );
    }
    
    var scriptDefMetadata = _registry.GetById(scriptDef.Id);
    if (scriptDefMetadata == null)
    {
        throw new InvalidOperationException(
            $"Script definition metadata not found for {context} script definition {scriptDef.Id}. " +
            $"Cannot attach {context} script to NPC {npcId}."
        );
    }
    
    return new ScriptAttachmentComponent
    {
        ScriptDefinitionId = scriptDef.Id,
        Priority = scriptDef.Priority,
        IsActive = true,
        ModId = scriptDefMetadata.OriginalModId,
        IsInitialized = false,
    };
}
```

---

### 5. **Inconsistent Script Attachment Timing**

**Location**: `MapLoaderSystem.cs`

**Problem**:
- **Behavior scripts**: Attached during entity creation (line 1125-1134, added to `components` list)
- **Interaction scripts**: Attached AFTER entity creation (line 1236, `World.Add()`)

**Issues**:
- **Inconsistency**: Different timing for similar operations
- **Architecture**: Behavior scripts are part of entity creation, interaction scripts are post-creation
- **Potential issues**: Entity might be in inconsistent state between creation and interaction script attachment

**Impact**:
- Confusing code flow
- If entity creation fails partway through, interaction scripts might not be attached
- Harder to reason about entity state

**Solution**:
**Option A**: Attach interaction scripts during entity creation (add to `components` list)
- **Pros**: Consistent timing, atomic entity creation
- **Cons**: Requires refactoring entity creation logic to handle variable component count

**Option B**: Keep current approach but document why
- **Pros**: No code changes needed
- **Cons**: Still inconsistent

**Recommended**: Option A - Attach interaction scripts during entity creation for consistency.

---

### 6. **Missing Elevation Validation**

**Location**: `InteractionSystem.cs`, line 130

**Problem**:
```csharp
// Check elevation match
if (interaction.Elevation != playerElevation)
{
    return; // Different elevation
}
```

**Issues**:
- **.cursorrule violation**: No validation that player elevation is valid
- **Fail-fast**: Should throw exception if player elevation cannot be determined
- **Silent failure**: Returns early without logging why interaction failed

**Solution**:
```csharp
// Get player elevation (fail fast if cannot determine)
int playerElevation = GetPlayerElevation(playerEntity);
if (interaction.Elevation != playerElevation)
{
    _logger.Debug(
        "Interaction blocked: elevation mismatch (player={PlayerElevation}, interaction={InteractionElevation})",
        playerElevation,
        interaction.Elevation
    );
    return;
}

private int GetPlayerElevation(Entity playerEntity)
{
    // Try PlayerComponent first (if we add Elevation property)
    if (World.Has<PlayerComponent>(playerEntity))
    {
        var playerComponent = World.Get<PlayerComponent>(playerEntity);
        // If PlayerComponent has Elevation, return it
        // Otherwise, default to 0 for now (but log warning)
    }
    
    // Fallback: check RenderableComponent.RenderOrder if it matches elevation pattern
    // Otherwise, throw exception (fail fast)
    throw new InvalidOperationException(
        "Cannot determine player elevation. PlayerComponent must have Elevation property."
    );
}
```

---

### 7. **InteractionStateComponent Property Naming Confusion**

**Location**: `InteractionStateComponent.cs`, line 20

**Problem**:
```csharp
/// <summary>
/// Gets or sets the behavior definition ID that was paused for this interaction.
/// Note: This stores the BehaviorId (behavior definition ID), not the ScriptId.
/// </summary>
public string BehaviorScriptId { get; set; }
```

**Issues**:
- **Naming confusion**: Property name suggests it stores ScriptId, but it actually stores BehaviorId
- **Misleading**: "BehaviorScriptId" implies script ID, not behavior definition ID
- **Documentation required**: Needs XML comment to clarify (which we have, but name is still confusing)

**Solution**:
Rename to `BehaviorId` to match actual usage:
```csharp
/// <summary>
/// Gets or sets the behavior definition ID that was paused for this interaction.
/// Used to look up BehaviorDefinition to get ScriptId for resuming the behavior script.
/// </summary>
public string BehaviorId { get; set; }
```

**Impact**: Requires updating `InteractionSystem.cs` line 206 and `OnMessageBoxClosed` line 258.

---

## 🟢 Low Priority Issues

### 8. **QueryDescription Not Cached for Player State Query**

**Location**: `InteractionSystem.cs`, line 253

**Problem**:
```csharp
World.Query(
    new QueryDescription().WithAll<PlayerComponent, InteractionStateComponent>(),
    // ...
);
```

**Issues**:
- **.cursorrule violation**: QueryDescription created in Update method, not cached
- **Performance**: Minor - creates new QueryDescription every frame
- **Inconsistency**: Other queries are cached

**Solution**:
Already cached as `_playerStateQuery` (line 63), but not used. Use cached query:
```csharp
World.Query(
    in _playerStateQuery,
    // ...
);
```

---

### 9. **Event Handler Parameter Types**

**Location**: `InteractionSystem.cs`, lines 241, 250

**Problem**:
- `OnInteractionEnded(InteractionEndedEvent evt)` - uses value parameter
- `OnMessageBoxClosed(MessageBoxClosedEvent evt)` - uses value parameter
- But events are structs, so ref parameters would be more efficient
- Other systems use ref parameters (e.g., `OnMapLoaded(ref MapLoadedEvent evt)`)

**Issues**:
- **Performance**: Minor - struct copying on event handler calls
- **Consistency**: Other event handlers in codebase use ref parameters (`RefAction<T>`)

**Solution**:
Change to ref parameters:
```csharp
private void OnInteractionEnded(ref InteractionEndedEvent evt)
private void OnMessageBoxClosed(ref MessageBoxClosedEvent evt)
```

And update event subscriptions to use `RefAction<T>`:
```csharp
EventBus.Subscribe<InteractionEndedEvent>(OnInteractionEnded);
EventBus.Subscribe<MessageBoxClosedEvent>(OnMessageBoxClosed);
```

**Note**: EventBus supports both `Action<T>` and `RefAction<T>`, but ref is preferred for struct events.

---

### 10. **Missing Validation in TriggerInteraction**

**Location**: `InteractionSystem.cs`, line 178

**Problem**:
```csharp
private void TriggerInteraction(Entity interactionEntity, Entity playerEntity, string interactionId, string? scriptDefinitionId)
{
    // Mark as active to prevent duplicate events
    _activeInteractions.Add(interactionEntity);
    // ... rest of logic
}
```

**Issues**:
- **Fail-fast**: No validation that `interactionId` is not null/empty
- **Fail-fast**: No validation that entities are still alive
- **Error handling**: Should validate inputs before proceeding

**Solution**:
```csharp
private void TriggerInteraction(Entity interactionEntity, Entity playerEntity, string interactionId, string? scriptDefinitionId)
{
    if (string.IsNullOrEmpty(interactionId))
    {
        throw new ArgumentException("InteractionId cannot be null or empty.", nameof(interactionId));
    }
    
    if (!World.IsAlive(interactionEntity))
    {
        throw new InvalidOperationException("Interaction entity is not alive.");
    }
    
    if (!World.IsAlive(playerEntity))
    {
        throw new InvalidOperationException("Player entity is not alive.");
    }
    
    // ... rest of logic
}
```

---

## 🔵 Inconsistencies with Behavior Handling

### 11. **Different Script Definition Lookup Patterns**

**Location**: `MapLoaderSystem.cs`

**Problem**:
- **Behavior scripts**: Lookup chain: `BehaviorId` → `BehaviorDefinition` → `ScriptDefinition.ScriptId` → `ScriptDefinition`
- **Interaction scripts**: Direct lookup: `InteractionId` → `ScriptDefinition`

**Issues**:
- **Inconsistency**: Two different patterns for similar operations
- **Design**: Behavior scripts go through BehaviorDefinition layer, interaction scripts don't
- **Flexibility**: BehaviorDefinition allows parameter overrides, interaction scripts don't

**Analysis**:
This is **intentional design** - interactions are simpler and don't need the BehaviorDefinition abstraction layer. However, it creates inconsistency.

**Recommendation**: 
- **Keep as-is** if interactions don't need parameter overrides
- **Add InteractionDefinition** if we want parameter overrides for interactions (but this adds complexity)

---

### 12. **Script Attachment Component Count Logic**

**Location**: `MapLoaderSystem.cs`, lines 1147-1169

**Problem**:
```csharp
Entity npcEntity;
if (components.Count == 7) // Has ScriptAttachmentComponent
{
    npcEntity = World.Create(
        npcComponent,
        (Components.SpriteAnimationComponent)components[1],
        // ... 7 components
    );
}
else // No ScriptAttachmentComponent
{
    npcEntity = World.Create(
        // ... 6 components
    );
}
```

**Issues**:
- **Magic number**: Hardcoded `7` and `6` component counts
- **Brittle**: Adding/removing components breaks this logic
- **Inconsistency**: Interaction scripts added after entity creation, so count doesn't include them
- **Maintenance**: Hard to maintain as component structure changes

**Impact**:
- If we add interaction scripts during entity creation, component count changes
- Code breaks if component order/structure changes

**Solution**:
Use dynamic component list or refactor to always add ScriptAttachmentComponent (even if null/empty):
```csharp
// Always create entity with all possible components
// Use a helper method to build component array dynamically
var entityComponents = BuildComponentArray(npcComponent, /* ... */, hasBehaviorScript, hasInteractionScript);
npcEntity = World.Create(entityComponents);
```

Or use `World.Create()` with individual components in correct order, checking for null/empty.

---

### 13. **Missing Parameter Override Support for Interaction Scripts**

**Location**: `MapLoaderSystem.cs`

**Problem**:
- **Behavior scripts**: Support parameter overrides via `BehaviorDefinition.ParameterOverrides` and `NpcDefinition.BehaviorParameters`
- **Interaction scripts**: No parameter override support

**Issues**:
- **Inconsistency**: Behavior scripts can be customized per-NPC, interaction scripts cannot
- **Flexibility**: Cannot customize interaction script behavior per-NPC instance

**Analysis**:
This might be intentional - interactions are simpler and don't need per-instance customization. But it's inconsistent.

**Recommendation**:
- **Keep as-is** if interactions don't need customization
- **Add support** if we need per-NPC interaction customization (e.g., different dialogue based on NPC instance)

---

## Summary of Required Fixes

### Critical (Must Fix):
1. ✅ Fix player elevation hardcoded to 0 - Add `Elevation` to `PlayerComponent`
2. ✅ Implement `Width`/`Height` support in distance calculation OR remove unused properties
3. ✅ Fix `QueryDescription` not cached in `OnMessageBoxClosed` (use `_playerStateQuery`)

### Medium Priority (Should Fix):
4. ✅ Extract duplicate script attachment code in `MapLoaderSystem`
5. ✅ Rename `BehaviorScriptId` to `BehaviorId` in `InteractionStateComponent`
6. ✅ Add validation in `TriggerInteraction` method
7. ✅ Consider attaching interaction scripts during entity creation for consistency

### Low Priority (Nice to Have):
8. ✅ Change event handlers to use ref parameters
9. ✅ Add elevation validation with fail-fast
10. ✅ Refactor component count logic in entity creation

### 14. **Missing ActiveMapEntity Filter in Interaction Query**

**Location**: `InteractionSystem.cs`, line 52

**Problem**:
```csharp
_interactionQuery = new QueryDescription()
    .WithAll<InteractionComponent, PositionComponent>();
```

**Issues**:
- **Performance**: Queries interactions in ALL maps, not just active ones
- **Inconsistency**: Other systems filter by `ActiveMapEntity` (e.g., `MovementSystem`, `ScriptTimerSystem`)
- **Architecture**: Should only process interactions in loaded/active maps

**Impact**:
- Unnecessary iteration over interactions in unloaded maps
- Performance degradation as more maps are loaded

**Solution**:
Add `ActiveMapEntity` filter:
```csharp
_interactionQuery = new QueryDescription()
    .WithAll<InteractionComponent, PositionComponent, ActiveMapEntity>();
```

**Note**: This requires that `MapLoaderSystem` adds `ActiveMapEntity` to interaction entities (which it should already do for NPCs).

---

### 15. **🔴 CRITICAL BUG: Missing Entity Parameter in Query**

**Location**: `InteractionSystem.cs`, lines 115, 123

**Problem**:
```csharp
World.Query(in _interactionQuery, (ref InteractionComponent interaction, ref PositionComponent pos) =>
{
    // ...
    Entity interactionEntity = World.Reference(interaction); // ❌ BUG: World.Reference() doesn't exist
```

**Issues**:
- **Critical bug**: Query callback is missing `Entity entity` parameter
- **Compilation error**: `World.Reference()` method doesn't exist in Arch ECS
- **Arch ECS pattern violation**: Entity must be first parameter in query callback

**Impact**:
- **Code won't compile** - this is a blocking bug
- Cannot get entity reference to track active interactions
- Cannot add components or check entity state

**Solution**:
Add `Entity entity` as first parameter:
```csharp
World.Query(in _interactionQuery, (Entity entity, ref InteractionComponent interaction, ref PositionComponent pos) =>
{
    // Use 'entity' directly
    Entity interactionEntity = entity;
    if (_activeInteractions.Contains(interactionEntity))
    {
        return; // Already interacting with this
    }
    // ... rest of logic
});
```

**Note**: This matches the pattern used in `ScriptLifecycleSystem` and other systems.

---

### Design Decisions (Document, Don't Change):
11. ✅ Document why interaction scripts don't use BehaviorDefinition layer
12. ✅ Document why interaction scripts don't support parameter overrides

