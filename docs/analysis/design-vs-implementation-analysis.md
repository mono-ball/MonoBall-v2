# Design vs Implementation Analysis

## Overview

This document analyzes the `script-definition-redesign.md` design document against the actual implementation and identifies gaps, inconsistencies, and issues.

## Critical Design Issues

### Issue 1: Design Specifies Unused Fields

**Design Document Says:**
```json
{
  "id": "base:behavior:wander",
  "scriptId": "base:script:movement/wander",
  "category": "movement",              // ← Design includes this
  "defaultSpeed": 1.5,                 // ← Design includes this
  "pauseAtWaypoint": 0.0,              // ← Design includes this
  "allowInteractionWhileMoving": true, // ← Design includes this
  "parameterOverrides": {...}
}
```

**Reality:**
- `category` - **REMOVED** (we just removed it from all behavior definitions)
- `defaultSpeed` - **NOT USED** anywhere in codebase
- `pauseAtWaypoint` (as field) - **NOT USED**, only in parameterOverrides
- `allowInteractionWhileMoving` - **NOT USED** anywhere in codebase

**Problem:**
- Design document specifies fields that are legacy/unused
- Design doesn't distinguish between "used" and "unused" fields
- Creates confusion about what should be in BehaviorDefinition

**Recommendation:**
- Update design to remove unused fields
- Only specify fields that are actually used
- Document which fields are legacy (if keeping for migration)

### Issue 2: Design Missing NPC-Level Parameter Overrides

**Design Document Says:**
```json
// NPC Definition (in Map JSON)
{
  "npcId": "npc_001",
  "behaviorId": "base:behavior:wander"
  // ← No mention of NPC-level parameter overrides
}
```

**Reality:**
- NPC definitions have `rangeX` and `rangeY` fields
- These are behavior parameters, not NPC properties
- Design doesn't account for NPC-specific parameter customization

**Problem:**
- Design doesn't specify how NPCs can override behavior parameters
- No `behaviorParameters` field in design
- Design's parameter resolution chain is incomplete

**Design's Parameter Resolution (Incomplete):**
1. ScriptDefinition defaults
2. BehaviorDefinition parameterOverrides
3. ~~NPC definition overrides~~ ← MISSING
4. EntityVariablesComponent overrides

**Should Be:**
1. ScriptDefinition defaults
2. BehaviorDefinition parameterOverrides
3. **NPCDefinition.behaviorParameters** ← ADD THIS
4. EntityVariablesComponent overrides

### Issue 3: Design Doesn't Address Field Ambiguity

**Design Document Says:**
- BehaviorDefinition has `defaultSpeed`, `pauseAtWaypoint`, `allowInteractionWhileMoving`
- These are "behavior-specific metadata"

**Reality:**
- These fields are **never used**
- `pauseAtWaypoint` appears in both field AND parameterOverrides (duplication)
- Unclear what "behavior-specific metadata" means vs script parameters

**Problem:**
- Design doesn't clarify:
  - What is metadata vs parameter?
  - Where should metadata be stored?
  - How should metadata be used?

**Recommendation:**
- Remove unused metadata fields from design
- If metadata is needed, specify:
  - What it's for
  - Where it's stored (EntityVariablesComponent? Component?)
  - How it's accessed

### Issue 4: Design Inconsistent with Actual Structure

**Design Example:**
```json
{
  "id": "base:behavior:wander",  // ← Design uses this format
  "scriptId": "base:script:movement/wander"
}
```

**Actual Behavior Definitions:**
```json
{
  "id": "base:behavior:movement/wander",  // ← Actual uses "movement" in ID
  "scriptId": "base:script:movement/wander"
}
```

**Problem:**
- Design uses `"base:behavior:wander"` (no category)
- Actual uses `"base:behavior:movement/wander"` (with category)
- Inconsistent ID format

**Recommendation:**
- Align design with actual ID format
- Or: Standardize on one format

### Issue 5: Design Missing Parameter Validation

**Design Document Says:**
- "Parameter Validation: Should BehaviorDefinition validate parameter overrides against ScriptDefinition?"
- **Decision**: Yes, validate at load time, log warnings for invalid parameters

**Reality:**
- No validation implemented
- No validation in ModLoader
- No validation in MapLoaderSystem
- Invalid parameters silently ignored

**Problem:**
- Design specifies validation but it's not implemented
- No mechanism to validate parameter types, names, or ranges

### Issue 6: Design Doesn't Address Metadata Storage

**Design Document Says:**
- Behavior metadata (speed, flags, etc.) should be stored separately
- Recommends EntityVariablesComponent (Option 1)

**Reality:**
- Metadata is **never stored anywhere**
- No implementation of metadata storage
- Metadata fields exist but are ignored

**Problem:**
- Design specifies storage mechanism but it's not implemented
- Unclear if metadata storage is actually needed
- If not needed, should be removed from design

### Issue 7: Design Missing Implementation Details

**Design Document Says:**
- "Update MapLoaderSystem: When creating NPC with behaviorId: Look up BehaviorDefinition, Get scriptId, Get parameter overrides, Create ScriptAttachmentComponent"

**Reality:**
- MapLoaderSystem doesn't do this
- It tries to look up ScriptDefinition directly
- BehaviorDefinition layer is completely skipped

**Problem:**
- Design specifies what should happen but implementation doesn't match
- Missing critical implementation step

### Issue 8: Design Parameter Resolution Incomplete

**Design Document Says:**
```
1. Load ScriptDefinition → Get default parameters
2. Load BehaviorDefinition (if behaviorId provided) → Get parameter overrides
3. Merge parameters:
   - Start with ScriptDefinition defaults
   - Apply BehaviorDefinition overrides
   - Apply entity-specific overrides (from EntityVariablesComponent)
4. Pass to script via ScriptContext.Parameters
```

**Missing:**
- NPC definition parameter overrides (rangeX, rangeY, etc.)
- No mention of how NPC-specific parameters are passed

**Should Be:**
```
1. Load ScriptDefinition → Get default parameters
2. Load BehaviorDefinition (if behaviorId provided) → Get parameter overrides
3. Get NPC definition parameter overrides (if any) → Get behaviorParameters
4. Merge parameters:
   - Start with ScriptDefinition defaults
   - Apply BehaviorDefinition.parameterOverrides
   - Apply NPCDefinition.behaviorParameters ← ADD THIS
   - Apply entity-specific overrides (from EntityVariablesComponent)
5. Pass to script via ScriptContext.Parameters
```

## Structural Design Issues

### Issue 9: Design Doesn't Clarify Field Ownership

**Design Document:**
- Doesn't clearly separate NPC properties from behavior parameters
- Doesn't specify what fields belong where

**Reality:**
- NPC definitions have ambiguous fields (rangeX, rangeY)
- Unclear if they're NPC properties or behavior parameters

**Recommendation:**
- Design should explicitly specify:
  - NPC Definition: Only NPC properties (x, y, spriteId, etc.)
  - NPC Definition: `behaviorParameters` field for parameter overrides
  - Behavior Definition: Only behavior metadata and parameterOverrides

### Issue 10: Design Includes Category But We Removed It

**Design Document:**
- BehaviorDefinition includes `"category": "movement"`

**Reality:**
- We just removed `category` from all behavior definitions
- Design is out of sync with implementation

**Recommendation:**
- Update design to remove category field
- Or: Document why category was removed

## Implementation Gaps

### Gap 1: BehaviorDefinition Class Not Created

**Design Says:** "Create BehaviorDefinition class"

**Reality:** Class doesn't exist

### Gap 2: Behavior Definitions Not Loaded

**Design Says:** "Load BehaviorDefinitions from Definitions/Behaviors/"

**Reality:** ModLoader doesn't load behavior definitions

### Gap 3: MapLoaderSystem Doesn't Resolve Through BehaviorDefinition

**Design Says:** "Look up BehaviorDefinition, Get scriptId from BehaviorDefinition"

**Reality:** MapLoaderSystem tries to look up ScriptDefinition directly

### Gap 4: Parameter Resolution Missing BehaviorDefinition Layer

**Design Says:** "ScriptDefinition defaults + BehaviorDefinition overrides + entity overrides"

**Reality:** Only ScriptDefinition defaults + EntityVariablesComponent overrides

### Gap 5: No Metadata Storage

**Design Says:** "Store behavior metadata in EntityVariablesComponent"

**Reality:** Metadata is never stored

## Recommendations

### Immediate Design Updates

1. **Remove Unused Fields from Design:**
   - Remove `category` from BehaviorDefinition
   - Remove `defaultSpeed` (or document it's legacy/unused)
   - Remove `pauseAtWaypoint` as field (only in parameterOverrides)
   - Remove `allowInteractionWhileMoving` (or document it's legacy/unused)

2. **Add NPC-Level Parameter Overrides:**
   - Add `behaviorParameters` field to NPC Definition in design
   - Update parameter resolution chain to include NPC overrides
   - Document parameter resolution order clearly

3. **Clarify Field Ownership:**
   - Explicitly separate NPC properties from behavior parameters
   - Document what fields belong in each definition type

4. **Update Examples:**
   - Use actual ID format (`base:behavior:movement/wander` not `base:behavior:wander`)
   - Include `behaviorParameters` in NPC definition examples
   - Remove unused fields from examples

### Implementation Priorities

1. **Create BehaviorDefinition Class**
   - Match cleaned design (no unused fields)
   - Include only: id, name, description, scriptId, parameterOverrides

2. **Load Behavior Definitions**
   - Update ModLoader to load from `Definitions/Behaviors/`
   - Register with type "Behavior"

3. **Fix MapLoaderSystem**
   - Resolve: BehaviorDefinition → ScriptDefinition
   - Extract parameter overrides from both layers

4. **Update Parameter Resolution**
   - Add NPC-level parameter extraction
   - Implement full resolution chain

5. **Add Validation**
   - Validate parameter names against ScriptDefinition
   - Validate parameter types
   - Log warnings for invalid parameters

## Design-Implementation Mismatches Summary

| Design Specifies | Implementation Status | Issue |
|------------------|----------------------|-------|
| BehaviorDefinition class | ❌ Not created | **CRITICAL GAP** |
| Load from Definitions/Behaviors/ | ❌ Not loaded | **CRITICAL GAP** |
| Resolve BehaviorDefinition → ScriptDefinition | ❌ Skips BehaviorDefinition | **CRITICAL GAP** |
| Parameter resolution includes BehaviorDefinition | ❌ Missing BehaviorDefinition layer | **CRITICAL GAP** |
| Store behavior metadata | ❌ Never stored | **CRITICAL GAP** |
| Parameter validation | ❌ Not implemented | **GAP** |
| NPC-level parameter overrides | ❌ Not in design, not implemented | **MISSING FROM DESIGN** |
| Category field | ✅ Removed (but design still has it) | **DESIGN OUT OF SYNC** |
| defaultSpeed field | ❌ Unused (but design includes it) | **DESIGN INCLUDES DEAD FIELD** |
| pauseAtWaypoint field | ❌ Unused (but design includes it) | **DESIGN INCLUDES DEAD FIELD** |
| allowInteractionWhileMoving | ❌ Unused (but design includes it) | **DESIGN INCLUDES DEAD FIELD** |

## Questions for Design Clarification

1. **Are `defaultSpeed`, `pauseAtWaypoint`, `allowInteractionWhileMoving` needed?**
   - If yes: Where are they stored? How are they used?
   - If no: Remove from design

2. **Should NPC definitions have `behaviorParameters` field?**
   - Design doesn't specify this
   - Implementation needs it for NPC-specific customization
   - **RECOMMENDATION**: Add to design

3. **What is the purpose of behavior metadata?**
   - Design says "behavior-specific metadata" but doesn't specify purpose
   - If unused, should be removed
   - **RECOMMENDATION**: Remove from design if unused

4. **Should category be in BehaviorDefinition?**
   - We removed it from implementation
   - Design still includes it
   - **RECOMMENDATION**: Remove from design

5. **How should parameter validation work?**
   - Design says "validate at load time"
   - Need to specify: Where? How? What happens on failure?
   - **RECOMMENDATION**: Specify validation in ModLoader or MapLoaderSystem

6. **How should NPC-level parameters be passed?**
   - Design doesn't specify
   - Implementation has `rangeX`/`rangeY` but they're unused
   - **RECOMMENDATION**: Add `behaviorParameters` field to NPC Definition in design

## Recommended Design Updates

### Update 1: Clean BehaviorDefinition Structure

**Remove:**
- `category` (removed from implementation)
- `defaultSpeed` (unused)
- `pauseAtWaypoint` as field (only in parameterOverrides)
- `allowInteractionWhileMoving` (unused)

**Keep:**
- `id`, `name`, `description` (metadata)
- `scriptId` (reference to ScriptDefinition)
- `parameterOverrides` (script parameter overrides)

### Update 2: Add NPC-Level Parameter Overrides

**Add to NPC Definition:**
```json
{
  "npcId": "...",
  "behaviorId": "...",
  "behaviorParameters": {    // ← NEW
    "rangeX": 1,
    "rangeY": 2
  }
}
```

### Update 3: Complete Parameter Resolution Chain

**Update design to specify:**
1. ScriptDefinition defaults
2. BehaviorDefinition.parameterOverrides
3. **NPCDefinition.behaviorParameters** ← ADD THIS
4. EntityVariablesComponent overrides

### Update 4: Remove Metadata Storage (If Unused)

**If metadata is not needed:**
- Remove metadata fields from BehaviorDefinition
- Remove metadata storage section from design
- Simplify design to focus on parameters only

**If metadata is needed:**
- Specify what it's for
- Specify where it's stored
- Specify how it's accessed
- Implement it

### Update 5: Fix ID Format Consistency

**Standardize on:**
- Behavior IDs: `"base:behavior:movement/wander"` (with category in path)
- Script IDs: `"base:script:movement/wander"` (with category in path)

**Or:**
- Behavior IDs: `"base:behavior:wander"` (no category)
- Script IDs: `"base:script:movement/wander"` (with category)

**Recommendation:** Use format with category in path (current implementation format)

