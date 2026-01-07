# Data Structure Clarity Analysis

## Problem Statement

The current data structure is unclear and ambiguous:
1. **NPC Definition**: Can't tell what are behavior parameters vs NPC properties
2. **Behavior Definition**: Has fields that seem arbitrary and may not be used
3. **Field Duplication**: Same concept appears in multiple places with unclear precedence

## Current Structure Analysis

### NPC Definition Fields (from littleroot_town)

```json
{
  "npcId": "...",                    // ✅ Clear: NPC identifier
  "name": "...",                     // ✅ Clear: NPC display name
  "x": 256,                          // ✅ Clear: NPC position (NPC property)
  "y": 160,                          // ✅ Clear: NPC position (NPC property)
  "spriteId": "...",                 // ✅ Clear: NPC sprite (NPC property)
  "behaviorId": "...",               // ✅ Clear: References behavior
  "interactionScript": "...",        // ✅ Clear: NPC interaction (NPC property)
  "visibilityFlag": "...",           // ✅ Clear: NPC visibility (NPC property)
  "direction": "up",                 // ✅ Clear: Initial facing (NPC property)
  "rangeX": 1,                       // ❓ AMBIGUOUS: Behavior parameter or NPC property?
  "rangeY": 2,                       // ❓ AMBIGUOUS: Behavior parameter or NPC property?
  "elevation": 3                     // ✅ Clear: Render order (NPC property)
}
```

**Issues:**
- `rangeX`/`rangeY` are **never used** by code (grep found no usage)
- Unclear if they're NPC properties or behavior parameters
- Stationary NPCs have `rangeX: 0, rangeY: 0` - why? They don't move!

### Behavior Definition Fields

```json
{
  "id": "...",                       // ✅ Clear: Behavior identifier
  "name": "...",                     // ✅ Clear: Display name
  "description": "...",              // ✅ Clear: Description
  "scriptId": "...",                 // ✅ Clear: References script
  "defaultSpeed": 1.5,               // ❓ UNUSED: Not found in codebase
  "pauseAtWaypoint": 1.0,           // ❓ AMBIGUOUS: Also in parameterOverrides!
  "allowInteractionWhileMoving": true, // ❓ UNUSED: Not found in codebase
  "parameterOverrides": {            // ✅ Clear: Script parameter overrides
    "pauseAtWaypoint": 1.0           // ❓ DUPLICATE: Same as field above!
  }
}
```

**Issues:**
- `defaultSpeed` - **NOT USED** anywhere in codebase
- `pauseAtWaypoint` - **NOT USED** as a field, but appears in `parameterOverrides` too
- `allowInteractionWhileMoving` - **NOT USED** anywhere in codebase
- `pauseAtWaypoint` appears **twice** in patrol.json (field + parameterOverride) - which one wins?

### Field Usage Analysis

| Field | Location | Used in Code? | Purpose | Status |
|-------|----------|---------------|---------|--------|
| `rangeX` | NPC Definition | ❌ NO | Unknown | **DEAD FIELD** |
| `rangeY` | NPC Definition | ❌ NO | Unknown | **DEAD FIELD** |
| `defaultSpeed` | Behavior Definition | ❌ NO | Unknown | **DEAD FIELD** |
| `pauseAtWaypoint` | Behavior Definition (field) | ❌ NO | Unknown | **DEAD FIELD** |
| `pauseAtWaypoint` | Behavior Definition (parameterOverride) | ✅ YES | Script parameter | **USED** |
| `allowInteractionWhileMoving` | Behavior Definition | ❌ NO | Unknown | **DEAD FIELD** |

## Structural Problems

### Problem 1: Ambiguous Field Ownership

**NPC Definition:**
- `rangeX`/`rangeY` - Are these NPC properties or behavior parameters?
- Currently: Neither! They're completely ignored.

**Should be:**
- If NPC property: Store in `NpcComponent` or `EntityVariablesComponent`
- If behavior parameter: Pass through `parameterOverrides` system

### Problem 2: Unused Fields from Legacy System

**Behavior Definition:**
- `defaultSpeed`, `pauseAtWaypoint`, `allowInteractionWhileMoving` appear to be from oldmonoball's `BehaviorDefinition`
- These fields are **never read** by the new system
- They're just taking up space and causing confusion

**Evidence:**
- `oldmonoball/MonoBallFramework.Game/Engine/Core/Types/BehaviorDefinition.cs` has these exact fields
- New system doesn't use them
- They're leftover from conversion

### Problem 3: Field Duplication

**Example from patrol.json:**
```json
{
  "pauseAtWaypoint": 1.0,           // Field (unused)
  "parameterOverrides": {
    "pauseAtWaypoint": 1.0          // Parameter (used)
  }
}
```

**Problem:**
- Same value in two places
- Which one is authoritative?
- Confusing for modders

### Problem 4: No Clear Separation of Concerns

**Current Structure:**
```
NPC Definition
  ├─ NPC Properties (x, y, spriteId, etc.)
  ├─ Behavior Reference (behaviorId)
  └─ ??? (rangeX, rangeY) ← What are these?

Behavior Definition
  ├─ Behavior Metadata (id, name, description)
  ├─ Script Reference (scriptId)
  ├─ ??? (defaultSpeed, pauseAtWaypoint, allowInteractionWhileMoving) ← Unused?
  └─ Parameter Overrides (parameterOverrides)
```

**Issues:**
- No clear boundary between NPC properties and behavior parameters
- Behavior definition mixes metadata with unused legacy fields
- Parameter overrides are clear, but other fields are not

## Proposed Clean Structure

### Option 1: Explicit Parameter Overrides in NPC

```json
// NPC Definition
{
  "npcId": "...",
  "name": "...",
  "x": 256,
  "y": 160,
  "spriteId": "...",
  "behaviorId": "base:behavior:movement/wander",
  "interactionScript": "...",
  "visibilityFlag": "...",
  "direction": "up",
  "elevation": 3,
  "behaviorParameters": {           // ← NEW: Explicit behavior parameter overrides
    "rangeX": 1,
    "rangeY": 2
  }
}
```

**Benefits:**
- Clear separation: NPC properties vs behavior parameters
- Explicit `behaviorParameters` field makes intent obvious
- Removes ambiguity about `rangeX`/`rangeY`

### Option 2: Remove Unused Fields

```json
// Behavior Definition (cleaned)
{
  "id": "base:behavior:movement/wander",
  "name": "Wander Behavior",
  "description": "NPC wanders randomly around the map",
  "scriptId": "base:script:movement/wander",
  "parameterOverrides": {           // ← ONLY this, remove unused fields
    "minWaitTime": 1.0,
    "maxWaitTime": 4.0
  }
}
```

**Removed:**
- `defaultSpeed` - not used
- `pauseAtWaypoint` (as field) - not used, only in parameterOverrides
- `allowInteractionWhileMoving` - not used

**Benefits:**
- Cleaner structure
- No confusion about unused fields
- Only fields that are actually used

### Option 3: Unified Structure (Recommended)

```json
// NPC Definition
{
  "npcId": "...",
  "name": "...",
  "x": 256,
  "y": 160,
  "spriteId": "...",
  "behaviorId": "base:behavior:movement/wander",
  "behaviorParameters": {          // ← Explicit NPC-specific parameter overrides
    "rangeX": 1,
    "rangeY": 2
  },
  "interactionScript": "...",
  "visibilityFlag": "...",
  "direction": "up",
  "elevation": 3
}

// Behavior Definition (cleaned)
{
  "id": "base:behavior:movement/wander",
  "name": "Wander Behavior",
  "description": "NPC wanders randomly around the map",
  "scriptId": "base:script:movement/wander",
  "parameterOverrides": {           // ← Behavior-level parameter defaults/overrides
    "minWaitTime": 1.0,
    "maxWaitTime": 4.0
  }
}
```

**Parameter Resolution Order:**
1. ScriptDefinition defaults
2. BehaviorDefinition.parameterOverrides
3. NPCDefinition.behaviorParameters ← NEW
4. EntityVariablesComponent (runtime)

**Benefits:**
- Clear separation of concerns
- Explicit parameter override chain
- No unused fields
- Easy to understand

## Recommendations

### Immediate Actions

1. **Remove unused fields from BehaviorDefinition:**
   - Remove `defaultSpeed` (not used)
   - Remove `pauseAtWaypoint` as field (only keep in parameterOverrides)
   - Remove `allowInteractionWhileMoving` (not used)

2. **Clarify NPC Definition structure:**
   - Add `behaviorParameters` field for explicit parameter overrides
   - Remove `rangeX`/`rangeY` as top-level fields
   - Move them to `behaviorParameters` if needed

3. **Document field purposes:**
   - Clearly mark what are NPC properties vs behavior parameters
   - Document parameter resolution order

### Long-term Improvements

1. **Validation:**
   - Validate that `behaviorParameters` only contain parameters defined in ScriptDefinition
   - Warn about unknown parameters

2. **Migration:**
   - Create migration script to move `rangeX`/`rangeY` to `behaviorParameters`
   - Remove unused fields from behavior definitions

3. **Type Safety:**
   - Consider strongly-typed parameter definitions
   - Validate parameter types match ScriptDefinition

## Questions to Resolve

1. **What is `defaultSpeed` supposed to do?**
   - If it's for movement speed, should it be a script parameter?
   - If it's legacy, should it be removed?

2. **What is `allowInteractionWhileMoving` supposed to do?**
   - Is this a script parameter or NPC property?
   - Should it be in `behaviorParameters` or `NpcComponent`?

3. **Should `rangeX`/`rangeY` be:**
   - Removed entirely (if not needed)?
   - Moved to `behaviorParameters` (if they're script parameters)?
   - Moved to `NpcComponent` (if they're NPC properties)?

4. **Why does `pauseAtWaypoint` appear twice in patrol.json?**
   - Remove the field version, keep only in parameterOverrides?


