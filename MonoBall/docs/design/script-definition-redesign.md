# Script Definition System Redesign

## Current State Analysis

### Current Problems

1. **Dual Definition Systems**
   - `Behaviors` (NPC behaviors) defined in `Definitions/Behaviors/*.json`
   - `TileBehaviors` (tile behaviors) defined in `Definitions/TileBehaviors/*.json`
   - `ScriptDefinition` (unified system) defined in `Definitions/Scripts/*.json`
   - Three separate systems that should be unified

2. **Tight Coupling**
   - Behavior definitions embed script paths directly (`"behaviorScript": "Scripts/Behaviors/wander_behavior.csx"`)
   - Script path is part of behavior metadata, making it hard to reuse scripts
   - Can't easily swap scripts or use same script with different configurations

3. **Mixed Concerns**
   - Behavior-specific metadata mixed with script metadata
   - Script path embedded in behavior definition
   - Hard to separate "what the behavior does" from "how it's implemented"

4. **Inconsistent Reference System**
   - NPCs reference behaviors via `BehaviorId` (e.g., `"base:behavior:movement/wander"`)
   - Behaviors then reference scripts via embedded path
   - Should be: NPCs → Behaviors → Scripts (clear chain)

5. **No Script Reuse**
   - Same script logic can't be reused with different parameters
   - Each behavior must have its own script file even if logic is similar

6. **Ambiguous Parameter Overrides**
   - NPC definitions have fields like `rangeX`/`rangeY` that are behavior parameters, not NPC properties
   - No clear way to pass NPC-specific parameter overrides
   - Unclear separation between NPC properties and behavior parameters

## Design Goals

1. **Unified Script System**: All scripts use `ScriptDefinition` regardless of use case
2. **Separation of Concerns**: Behavior configuration separate from script definitions
3. **Script Reusability**: Same script can be used by multiple behaviors with different parameters
4. **Clear Reference Chain**: NPCs → BehaviorDefinitions → ScriptDefinitions
5. **ECS/Event-First**: Scripts react to events, behaviors configure script parameters
6. **Parameter Flexibility**: Support parameter overrides at multiple levels (Script → Behavior → NPC → Runtime)
7. **Clear Field Ownership**: Explicit separation of NPC properties vs behavior parameters

## Proposed Design

### Architecture Overview

```
NPC Definition (in Map)
  └─> behaviorId: "base:behavior:movement/wander"
       └─> BehaviorDefinition
            └─> scriptId: "base:script:movement/wander"
                 └─> ScriptDefinition
                      └─> scriptPath: "Scripts/Behaviors/wander_behavior.csx"
```

### Core Concepts

1. **ScriptDefinition** (already exists)
   - Defines the script implementation (`.csx` file)
   - Contains script metadata (name, description, parameters)
   - Reusable across multiple behaviors
   - Defines default parameter values

2. **BehaviorDefinition** (new)
   - Defines NPC behavior configuration
   - References a `ScriptDefinition` by ID
   - Can override script parameters via `parameterOverrides`
   - Multiple behaviors can reference the same script with different parameters

3. **TileBehaviorDefinition** (new)
   - Defines tile behavior configuration
   - References a `ScriptDefinition` by ID
   - Contains tile-specific metadata (flags, movement rules, etc.)
   - Can override script parameters

### Definition Structures

#### ScriptDefinition (Enhanced)

```json
{
  "id": "base:script:movement/wander",
  "name": "Wander Movement Script",
  "description": "Implements wandering movement logic",
  "scriptPath": "Scripts/Behaviors/wander_behavior.csx",
  "category": "movement",
  "priority": 500,
  "parameters": [
    {
      "name": "minWaitTime",
      "type": "float",
      "defaultValue": 1.0,
      "min": 0.0,
      "max": 10.0,
      "description": "Minimum time to wait before next movement (seconds)"
    },
    {
      "name": "maxWaitTime",
      "type": "float",
      "defaultValue": 4.0,
      "min": 0.0,
      "max": 10.0,
      "description": "Maximum time to wait before next movement (seconds)"
    },
    {
      "name": "rangeX",
      "type": "int",
      "defaultValue": 0,
      "min": 0,
      "description": "Maximum horizontal wander range (0 = unlimited)"
    },
    {
      "name": "rangeY",
      "type": "int",
      "defaultValue": 0,
      "min": 0,
      "description": "Maximum vertical wander range (0 = unlimited)"
    }
  ]
}
```

**Key Points:**
- Scripts are generic and reusable
- Parameters define what can be configured
- Default values provide sensible defaults
- No behavior-specific metadata

#### BehaviorDefinition (New)

```json
{
  "id": "base:behavior:movement/wander",
  "name": "Wander Behavior",
  "description": "NPC wanders randomly around the map, changing direction occasionally",
  "scriptId": "base:script:movement/wander",
  "parameterOverrides": {
    "minWaitTime": 1.0,
    "maxWaitTime": 4.0
  }
}
```

**Key Points:**
- References a `ScriptDefinition` by `scriptId`
- Can override script parameters via `parameterOverrides`
- Multiple behaviors can reference the same script with different parameters
- **No unused metadata fields** (removed: category, defaultSpeed, pauseAtWaypoint, allowInteractionWhileMoving)

**Fields:**
- `id` (required): Unique identifier (e.g., `"base:behavior:movement/wander"`)
- `name` (required): Display name
- `description` (optional): Description of the behavior
- `scriptId` (required): Reference to ScriptDefinition ID
- `parameterOverrides` (optional): Dictionary of parameter name → value overrides

#### NPC Definition (in Map JSON)

```json
{
  "npcId": "base:npc:hoenn/littleroot_town/localid_littleroot_twin",
  "name": "LOCALID_LITTLEROOT_TWIN",
  "x": 256,
  "y": 160,
  "spriteId": "base:sprite:npcs/generic/twin",
  "behaviorId": "base:behavior:movement/wander",
  "behaviorParameters": {
    "rangeX": 1,
    "rangeY": 2
  },
  "interactionScript": "base:behavior:interaction/littleroot_town_event_script_twin",
  "visibilityFlag": null,
  "direction": null,
  "elevation": 3
}
```

**Key Points:**
- **NPC Properties**: `npcId`, `name`, `x`, `y`, `spriteId`, `interactionScript`, `visibilityFlag`, `direction`, `elevation`
- **Behavior Reference**: `behaviorId` references a BehaviorDefinition
- **Behavior Parameters**: `behaviorParameters` (optional) provides NPC-specific parameter overrides
- Clear separation between NPC properties and behavior parameters

**Fields:**
- NPC Properties (stored in NPC components):
  - `npcId`, `name`, `x`, `y`, `spriteId`, `interactionScript`, `visibilityFlag`, `direction`, `elevation`
- Behavior Configuration:
  - `behaviorId` (optional): Reference to BehaviorDefinition
  - `behaviorParameters` (optional): NPC-specific parameter overrides

#### TileBehaviorDefinition (New)

```json
{
  "id": "base:tile_behavior:ice",
  "name": "Ice Tile",
  "description": "Forces sliding movement on ice tiles",
  "scriptId": "base:script:tile/slide",
  "flags": ["ForcesMovement", "DisablesRunning"],
  "parameterOverrides": {
    "slideDistance": 2,
    "slideSpeed": 2.0
  }
}
```

**Key Points:**
- References a `ScriptDefinition` by `scriptId`
- Contains tile-specific metadata (flags)
- Can override script parameters
- Flags are for fast lookup without script execution

### Reference Chain

1. **NPC Definition** (in Map JSON):
   ```json
   {
     "npcId": "base:npc:hoenn/littleroot_town/localid_littleroot_twin",
     "behaviorId": "base:behavior:movement/wander",
     "behaviorParameters": {
       "rangeX": 1,
       "rangeY": 2
     }
   }
   ```

2. **BehaviorDefinition** resolves:
   - Looks up `base:behavior:movement/wander`
   - Gets `scriptId: "base:script:movement/wander"`
   - Gets `parameterOverrides` from BehaviorDefinition

3. **ScriptDefinition** resolves:
   - Looks up `base:script:movement/wander`
   - Gets `scriptPath: "Scripts/Behaviors/wander_behavior.csx"`
   - Gets default parameters

4. **Script Attachment**:
   - `MapLoaderSystem` creates `ScriptAttachmentComponent` with:
     - `ScriptDefinitionId`: `"base:script:movement/wander"`
     - Parameters merged from all layers (see Parameter Resolution below)

### Parameter Resolution

When attaching a script to an entity, parameters are resolved in the following order (later layers override earlier ones):

1. **ScriptDefinition defaults**
   - Default values from `ScriptDefinition.parameters[].defaultValue`

2. **BehaviorDefinition parameterOverrides**
   - Overrides from `BehaviorDefinition.parameterOverrides`
   - Applied when behavior is resolved

3. **NPCDefinition behaviorParameters**
   - NPC-specific overrides from `NPCDefinition.behaviorParameters`
   - Applied when NPC is created from map

4. **EntityVariablesComponent overrides** (runtime)
   - Runtime overrides stored in `EntityVariablesComponent`
   - Applied when script is initialized

**Example Resolution:**

```json
// ScriptDefinition defaults
{
  "minWaitTime": 1.0,
  "maxWaitTime": 4.0,
  "rangeX": 0,
  "rangeY": 0
}

// BehaviorDefinition.parameterOverrides
{
  "minWaitTime": 1.5  // Overrides default
}

// NPCDefinition.behaviorParameters
{
  "rangeX": 1,        // Overrides default
  "rangeY": 2        // Overrides default
}

// Final merged parameters passed to script
{
  "minWaitTime": 1.5,  // From BehaviorDefinition
  "maxWaitTime": 4.0,   // From ScriptDefinition (not overridden)
  "rangeX": 1,          // From NPCDefinition
  "rangeY": 2           // From NPCDefinition
}
```

**Implementation:**
- `MapLoaderSystem` merges ScriptDefinition + BehaviorDefinition + NPCDefinition parameters
- Stores merged parameters in `EntityVariablesComponent` with keys like `"script:{scriptId}:param:{paramName}"`
- `ScriptLifecycleSystem.BuildScriptParameters()` reads from `EntityVariablesComponent` and applies final overrides

### Parameter Validation

Parameter overrides should be validated to ensure:
- Parameter names exist in ScriptDefinition
- Parameter types match ScriptDefinition
- Parameter values are within min/max bounds (if specified)

**Validation Points:**
1. **ModLoader**: When loading BehaviorDefinition, validate `parameterOverrides` against referenced ScriptDefinition
2. **MapLoaderSystem**: When creating NPC, validate `behaviorParameters` against ScriptDefinition
3. **ScriptLifecycleSystem**: When building parameters, validate EntityVariablesComponent overrides

**On Validation Failure:**
- Log warning with parameter name, expected type/range, and actual value
- Use ScriptDefinition default value instead
- Continue execution (don't fail)

### Tile Behavior Reference

Tiles reference tile behaviors via tile properties (from Tiled map editor):

**Current:**
- Tile property: `"behavior_type": "base:tile_behavior:movement/ice"`

**New** (same):
- Tile property: `"behavior_type": "base:tile_behavior:ice"`
- System resolves: `TileBehaviorDefinition` → `ScriptDefinition`

### ID Format Standardization

**Standard Format:**
- Behavior IDs: `"base:behavior:movement/wander"` (category in path)
- Script IDs: `"base:script:movement/wander"` (category in path)
- Tile Behavior IDs: `"base:tile_behavior:ice"` (no category needed)

**Pattern:**
- `{modId}:{type}:{category}/{name}` for behaviors and scripts
- `{modId}:{type}:{name}` for tile behaviors (no category)

### File Organization

```
Mods/
  {mod-id}/
    Definitions/
      Scripts/              # Unified script definitions
        Behaviors/
          wander.json
          patrol.json
        tile/
          ice.json
          jump.json
      Behaviors/            # NPC behavior definitions
        movement/
          wander.json
          patrol.json
      TileBehaviors/        # Tile behavior definitions
        ice.json
        jump_south.json
    Scripts/                # Actual script files
      Behaviors/
        wander_behavior.csx
        patrol_behavior.csx
      tile/
        ice.csx
        jump.csx
```

### Benefits

1. **Script Reusability**: Same script can be used by multiple behaviors
2. **Separation of Concerns**: Behavior config separate from script implementation
3. **Parameter Flexibility**: Easy to create variations with different parameters at multiple levels
4. **Unified System**: All scripts use same ScriptDefinition structure
5. **Clear Dependencies**: Explicit reference chain (NPC → Behavior → Script)
6. **Easier Testing**: Can test scripts independently of behaviors
7. **Better Organization**: Scripts organized by category, behaviors by use case
8. **Clear Field Ownership**: Explicit separation of NPC properties vs behavior parameters
9. **NPC-Specific Customization**: Each NPC can override behavior parameters individually

### Implementation Plan

1. **Create BehaviorDefinition class**
   - Fields: `id`, `name`, `description`, `scriptId`, `parameterOverrides`
   - Load from `Definitions/Behaviors/*.json`
   - **No unused metadata fields** (removed: category, defaultSpeed, pauseAtWaypoint, allowInteractionWhileMoving)

2. **Create TileBehaviorDefinition class**
   - Fields: `id`, `name`, `description`, `scriptId`, `flags`, `parameterOverrides`
   - Load from `Definitions/TileBehaviors/*.json`

3. **Update ModLoader**
   - Load BehaviorDefinitions from `Definitions/Behaviors/`
   - Load TileBehaviorDefinitions from `Definitions/TileBehaviors/`
   - Register in DefinitionRegistry with type "Behavior" and "TileBehavior"
   - Validate parameter overrides against ScriptDefinition at load time

4. **Update MapLoaderSystem**
   - When creating NPC with `behaviorId`:
     - Look up `BehaviorDefinition` (not ScriptDefinition directly)
     - Get `scriptId` from BehaviorDefinition
     - Get `parameterOverrides` from BehaviorDefinition
     - Get `behaviorParameters` from NPCDefinition
     - Merge parameters: ScriptDefinition defaults + BehaviorDefinition overrides + NPCDefinition overrides
     - Validate `behaviorParameters` against ScriptDefinition
     - Store merged parameters in `EntityVariablesComponent`
     - Create `ScriptAttachmentComponent` with `ScriptDefinitionId`

5. **Update ScriptLifecycleSystem**
   - Update `BuildScriptParameters()` to:
     - Start with ScriptDefinition defaults
     - Apply BehaviorDefinition overrides (from EntityVariablesComponent)
     - Apply NPCDefinition overrides (from EntityVariablesComponent)
     - Apply EntityVariablesComponent runtime overrides
   - Maintain resolution order: Script defaults → Behavior overrides → NPC overrides → Entity overrides

6. **Update NpcDefinition class**
   - Add `BehaviorParameters` property (Dictionary<string, object>)
   - Document that this is for behavior parameter overrides, not NPC properties
   - Remove or deprecate `RangeX`/`RangeY` properties (move to `behaviorParameters`)

7. **Update tile behavior system**
   - When processing tile with `behavior_type`:
     - Look up `TileBehaviorDefinition`
     - Get `scriptId` from TileBehaviorDefinition
     - Get parameter overrides from TileBehaviorDefinition
     - Attach script to tile entity

### Migration Strategy

1. **Phase 1: Create New Structure**
   - Create BehaviorDefinition and TileBehaviorDefinition classes
   - Update ModLoader to load new definitions
   - Keep old system working alongside new system

2. **Phase 2: Update MapLoaderSystem**
   - Update to resolve through BehaviorDefinition
   - Support both old format (direct ScriptDefinition lookup) and new format
   - Log warnings when old format is used

3. **Phase 3: Convert Definitions**
   - Create migration tool to convert old behaviors to new format
   - Extract script paths to separate ScriptDefinitions
   - Create BehaviorDefinitions that reference ScriptDefinitions
   - Update map JSONs to use `behaviorParameters` instead of top-level `rangeX`/`rangeY`

4. **Phase 4: Deprecation**
   - Mark old format as deprecated
   - Update documentation
   - Remove old format support in future version

### Open Questions (Resolved)

1. **Behavior Metadata Storage**: Use EntityVariablesComponent or dedicated component?
   - **Decision**: Not needed - removed unused metadata fields. If needed in future, use EntityVariablesComponent.

2. **Backward Compatibility**: How long to support old format?
   - **Decision**: Support for at least one major version, then deprecate

3. **Script Categories**: Should we enforce categories or make them optional?
   - **Decision**: Optional but recommended for organization (kept in ScriptDefinition, removed from BehaviorDefinition)

4. **Parameter Validation**: Should BehaviorDefinition validate parameter overrides against ScriptDefinition?
   - **Decision**: Yes, validate at load time (ModLoader) and at runtime (MapLoaderSystem), log warnings for invalid parameters

5. **Flags System**: Keep tile behavior flags for fast lookup?
   - **Decision**: Yes, flags are useful for performance (avoid script execution for simple checks)

6. **NPC-Level Parameter Overrides**: How should NPCs override behavior parameters?
   - **Decision**: Add `behaviorParameters` field to NPC Definition, merge into parameter resolution chain

### Design Changes Summary

**Removed from BehaviorDefinition:**
- `category` - removed (not needed, category is in ScriptDefinition)
- `defaultSpeed` - removed (unused, not stored anywhere)
- `pauseAtWaypoint` (as field) - removed (only in parameterOverrides if needed)
- `allowInteractionWhileMoving` - removed (unused, not stored anywhere)

**Added to NPC Definition:**
- `behaviorParameters` - new field for NPC-specific parameter overrides

**Updated Parameter Resolution:**
- Added NPC-level parameter overrides to resolution chain
- Complete chain: ScriptDefinition → BehaviorDefinition → NPCDefinition → EntityVariablesComponent

**Fixed ID Format:**
- Standardized on format with category in path: `"base:behavior:movement/wander"`
