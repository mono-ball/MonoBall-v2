# Behavior Definition Chain Analysis

## Current State

### Path Flow (What Should Happen)
```
Map NPC Definition
  └─> behaviorId: "base:behavior:movement/wander"
       └─> BehaviorDefinition
            └─> scriptId: "base:script:movement/wander"
                 └─> ScriptDefinition
                      └─> scriptPath: "Scripts/Behaviors/wander_behavior.csx"
```

### Actual Current Flow (What Actually Happens)
```
Map NPC Definition
  └─> behaviorId: "base:behavior:movement/wander"
       └─> [SKIPS BehaviorDefinition]
            └─> Tries to look up as ScriptDefinition directly ❌
                 └─> Fails or finds wrong definition
```

## Critical Issues Found

### Issue 1: Missing BehaviorDefinition Layer

**Problem**: The `MapLoaderSystem` code (lines 989-1022) directly looks up `npcDef.BehaviorId` as a `ScriptDefinition`:

```csharp
var scriptDef = _registry.GetById<Mods.Definitions.ScriptDefinition>(
    npcDef.BehaviorId  // This is "base:behavior:movement/wander"
);
```

**Expected**: Should look up `BehaviorDefinition` first, then get `scriptId` from it.

**Impact**: 
- Behavior definitions are completely bypassed
- Parameter overrides from behavior definitions are ignored
- Behavior-specific metadata (defaultSpeed, pauseAtWaypoint, etc.) is lost
- The redesign architecture is not implemented

### Issue 2: NPC Definition Has Unused Fields

**Problem**: `NpcDefinition` class has `RangeX` and `RangeY` properties (lines 218-225 in MapDefinition.cs), and map JSON files include these fields, but they are **completely ignored** by `MapLoaderSystem`:

```json
{
  "npcId": "base:npc:hoenn/littleroot_town/localid_littleroot_twin",
  "behaviorId": "base:behavior:movement/wander",
  "rangeX": 1,
  "rangeY": 2
}
```

**Expected**: These should be passed as parameter overrides through the BehaviorDefinition system to the script.

**Impact**:
- NPC-specific parameter customization is impossible
- Range constraints defined in maps are completely ignored
- All NPCs with the same behavior get identical parameters
- The `NpcDefinition.RangeX` and `NpcDefinition.RangeY` properties exist but are never read

### Issue 3: No BehaviorDefinition Class

**Problem**: The redesign document specifies a `BehaviorDefinition` class, but it doesn't exist in the codebase.

**Expected**: Should have:
```csharp
public class BehaviorDefinition
{
    public string Id { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    public string ScriptId { get; set; }  // References ScriptDefinition
    public float DefaultSpeed { get; set; }
    public float PauseAtWaypoint { get; set; }
    public bool AllowInteractionWhileMoving { get; set; }
    public Dictionary<string, object> ParameterOverrides { get; set; }
}
```

**Impact**:
- Cannot implement the redesign architecture
- Behavior definitions exist as JSON but aren't loaded
- No way to resolve behavior → script chain

### Issue 4: Parameter Resolution Incomplete

**Problem**: `ScriptLifecycleSystem.BuildScriptParameters()` (line 156) only merges:
1. ScriptDefinition default parameters
2. EntityVariablesComponent overrides

**Missing**:
- BehaviorDefinition parameter overrides
- NPC definition parameter overrides (rangeX, rangeY, etc.)

**Expected Resolution Order**:
1. ScriptDefinition defaults
2. BehaviorDefinition parameterOverrides
3. NPC definition overrides (from map)
4. EntityVariablesComponent overrides (runtime)

### Issue 5: Behavior Definition Files Not Loaded

**Problem**: `ModLoader` doesn't load behavior definitions from `Definitions/Behaviors/`.

**Expected**: Should load behavior definitions similar to how script definitions are loaded.

**Current**: Only script definitions are loaded from `Definitions/Scripts/`.

## Example: littleroot_town NPC

### Current State
```json
{
  "npcId": "base:npc:hoenn/littleroot_town/localid_littleroot_twin",
  "behaviorId": "base:behavior:movement/wander",
  "rangeX": 1,
  "rangeY": 2
}
```

**What Happens**:
1. MapLoaderSystem tries to find `ScriptDefinition` with ID `"base:behavior:movement/wander"`
2. This fails (because it's a behavior ID, not a script ID)
3. Script is not attached, or wrong script is attached
4. `rangeX` and `rangeY` are completely ignored

**What Should Happen**:
1. MapLoaderSystem finds `BehaviorDefinition` with ID `"base:behavior:movement/wander"`
2. Gets `scriptId: "base:script:movement/wander"` from BehaviorDefinition
3. Finds `ScriptDefinition` with that ID
4. Merges parameters:
   - ScriptDefinition defaults: `{ minWaitTime: 1.0, maxWaitTime: 4.0, maxBlockedAttempts: 4, rangeX: 0, rangeY: 0 }`
   - BehaviorDefinition overrides: `{}` (none in this case)
   - NPC definition overrides: `{ rangeX: 1, rangeY: 2 }`
5. Creates ScriptAttachmentComponent with merged parameters
6. Script receives: `{ minWaitTime: 1.0, maxWaitTime: 4.0, maxBlockedAttempts: 4, rangeX: 1, rangeY: 2 }`

## Design Inconsistencies

### Inconsistency 1: ID Naming
- Behavior IDs: `"base:behavior:movement/wander"`
- Script IDs: `"base:script:movement/wander"`
- These are different namespaces, but code treats them as the same

### Inconsistency 2: Parameter Storage
- Behavior definitions have `parameterOverrides` in JSON
- NPC definitions have `rangeX`/`rangeY` directly
- No unified way to pass NPC-specific parameters

### Inconsistency 3: Metadata Storage
- Behavior definitions have `defaultSpeed`, `pauseAtWaypoint`, `allowInteractionWhileMoving`
- These are never stored anywhere or used
- Should be stored in EntityVariablesComponent or a dedicated component

## Required Fixes

### Fix 1: Create BehaviorDefinition Class
- Create `MonoBall.Core/Mods/Definitions/BehaviorDefinition.cs`
- Match structure from redesign document
- Add JSON deserialization support

### Fix 2: Load Behavior Definitions
- Update `ModLoader` to load from `Definitions/Behaviors/`
- Register in DefinitionRegistry with type "Behavior"

### Fix 3: Update MapLoaderSystem
- Change behavior resolution to: BehaviorDefinition → ScriptDefinition
- Extract parameter overrides from BehaviorDefinition
- Extract parameter overrides from NPC definition (rangeX, rangeY, etc.)
- Store behavior metadata (defaultSpeed, etc.) in EntityVariablesComponent

### Fix 4: Update ScriptLifecycleSystem
- Update `BuildScriptParameters()` to include BehaviorDefinition overrides
- Add NPC definition parameter extraction (if needed)
- Maintain resolution order: Script defaults → Behavior overrides → NPC overrides → Entity overrides

### Fix 5: Update NpcDefinition
- Document that `rangeX`/`rangeY` are parameter overrides
- Consider adding generic `parameterOverrides` field for extensibility
- Or: Remove `rangeX`/`rangeY` and require them to be in BehaviorDefinition parameterOverrides

## Recommendations

### Short Term
1. Create `BehaviorDefinition` class
2. Load behavior definitions in `ModLoader`
3. Fix `MapLoaderSystem` to resolve through BehaviorDefinition
4. Update parameter resolution chain

### Long Term
1. Consider unified parameter override system (NPC → Behavior → Script)
2. Store behavior metadata in a dedicated component or EntityVariablesComponent
3. Add validation to ensure behavior definitions reference valid script definitions
4. Add migration tool to convert old format to new format

