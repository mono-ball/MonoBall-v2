# Plan vs Design Analysis

## Overview

This document analyzes the implementation plan against the design document to identify gaps, inconsistencies, and missing elements.

## Critical Issues

### Issue 1: Missing Validation in ModLoader

**Design Says:**
> **Validation Points:**
> 1. **ModLoader**: When loading BehaviorDefinition, validate `parameterOverrides` against referenced ScriptDefinition
> 2. **MapLoaderSystem**: When creating NPC, validate `behaviorParameters` against ScriptDefinition
> 3. **ScriptLifecycleSystem**: When building parameters, validate EntityVariablesComponent overrides

**Plan Says:**
- Only mentions validation in MapLoaderSystem (step 3)
- Does not mention validation in ModLoader
- Does not mention validation in ScriptLifecycleSystem

**Impact:**
- Invalid parameter overrides in BehaviorDefinition JSON files won't be caught until runtime
- No early validation during mod loading

**Fix Required:**
- Add validation step in ModLoader when loading BehaviorDefinition
- Validate that `parameterOverrides` keys exist in referenced ScriptDefinition
- Validate parameter types match ScriptDefinition
- Log warnings for invalid parameters

### Issue 2: Missing EntityVariablesComponent Creation

**Design Says:**
> Store merged parameters in `EntityVariablesComponent` with keys like `"script:{scriptId}:param:{paramName}"`

**Plan Says:**
> Store merged parameters in EntityVariablesComponent with keys: `"script:{scriptId}:param:{paramName}"`

**Missing:**
- Plan doesn't specify when/how EntityVariablesComponent is created
- Plan doesn't specify if EntityVariablesComponent already exists on entity or needs to be created
- Plan doesn't specify if EntityVariablesComponent should be added to entity components list

**Impact:**
- Unclear implementation details
- May fail if EntityVariablesComponent doesn't exist on entity

**Fix Required:**
- Specify that EntityVariablesComponent should be created if it doesn't exist
- Add EntityVariablesComponent to entity components list in MapLoaderSystem
- Or: Check if entity already has EntityVariablesComponent and add to it

### Issue 3: Missing Error Handling for Missing BehaviorDefinition

**Design Says:**
> Look up `BehaviorDefinition` (not ScriptDefinition directly)

**Plan Says:**
> Look up `BehaviorDefinition` first, then get `scriptId` from it

**Missing:**
- What happens if BehaviorDefinition is not found?
- Should it fall back to old behavior (direct ScriptDefinition lookup)?
- Should it log error and skip script attachment?
- Should it throw exception?

**Impact:**
- Unclear error handling behavior
- May cause runtime failures or silent failures

**Fix Required:**
- Specify error handling: Log warning, skip script attachment, continue NPC creation
- Or: Support backward compatibility fallback to direct ScriptDefinition lookup (with deprecation warning)

### Issue 4: Missing Parameter Type Conversion Details

**Design Says:**
> Validate parameter types match ScriptDefinition

**Plan Says:**
> Validate parameter types match ScriptDefinition

**Missing:**
- How to convert JSON types (number, string, boolean) to C# types (int, float, bool, string, Vector2)?
- JSON numbers can be int or float - how to distinguish?
- How to handle Vector2 parameters (string format "X,Y" vs object)?

**Impact:**
- Type conversion may fail or be incorrect
- Parameters may not be correctly deserialized

**Fix Required:**
- Specify type conversion logic:
  - JSON number → int if ScriptDefinition type is "int"
  - JSON number → float if ScriptDefinition type is "float"
  - JSON string → string (or parse as Vector2 if type is "vector2")
  - JSON boolean → bool
- Use same conversion logic as ScriptLifecycleSystem.ParseVector2() for Vector2

### Issue 5: Missing ScriptLifecycleSystem Validation

**Design Says:**
> 3. **ScriptLifecycleSystem**: When building parameters, validate EntityVariablesComponent overrides

**Plan Says:**
> The method should already work correctly since MapLoaderSystem will store merged parameters in EntityVariablesComponent. However, add validation and logging to ensure parameters are correctly resolved.

**Missing:**
- Plan doesn't specify what validation to add
- Plan doesn't specify how to validate parameters in ScriptLifecycleSystem

**Impact:**
- Runtime parameter overrides in EntityVariablesComponent won't be validated
- Invalid parameters may cause script errors

**Fix Required:**
- Add validation in ScriptLifecycleSystem.BuildScriptParameters():
  - Validate parameter names exist in ScriptDefinition
  - Validate parameter types match ScriptDefinition
  - Validate parameter values are within min/max bounds
  - Log warnings for invalid parameters

### Issue 6: Parameter Storage Key Format Mismatch

**Design Says:**
> Store merged parameters in `EntityVariablesComponent` with keys like `"script:{scriptId}:param:{paramName}"`

**Plan Says:**
> Store merged parameters in EntityVariablesComponent with keys: `"script:{scriptId}:param:{paramName}"`

**Current ScriptLifecycleSystem:**
- Uses key format: `"script:{scriptId}:param:{paramName}"` (line 251)
- This matches the design, so this is correct

**Status:** ✅ No issue - format matches

### Issue 7: Missing TileBehaviorDefinition Implementation

**Design Says:**
> 2. **Create TileBehaviorDefinition class**
>    - Fields: `id`, `name`, `description`, `scriptId`, `flags`, `parameterOverrides`
>    - Load from `Definitions/TileBehaviors/*.json`

**Plan Says:**
- Does not include TileBehaviorDefinition
- Only focuses on BehaviorDefinition for NPCs

**Impact:**
- TileBehaviorDefinition is not implemented
- Design specifies it but plan doesn't include it

**Fix Required:**
- Either: Add TileBehaviorDefinition to plan (if needed)
- Or: Document that TileBehaviorDefinition is out of scope for this implementation

### Issue 8: Missing Backward Compatibility Strategy

**Design Says:**
> **Phase 2: Update MapLoaderSystem**
> - Support both old format (direct ScriptDefinition lookup) and new format
> - Log warnings when old format is used

**Plan Says:**
- Replace direct ScriptDefinition lookup with BehaviorDefinition lookup
- No mention of backward compatibility fallback

**Impact:**
- Existing maps/NPCs that reference ScriptDefinition directly will break
- No migration path for existing code

**Fix Required:**
- Add backward compatibility: If BehaviorDefinition not found, try direct ScriptDefinition lookup
- Log deprecation warning when old format is used
- Document that old format will be removed in future version

### Issue 9: Missing Parameter Merging Implementation Details

**Design Says:**
> Merge parameters: ScriptDefinition defaults + BehaviorDefinition overrides + NPCDefinition overrides

**Plan Says:**
> Merge parameters from all layers:
> - Start with ScriptDefinition defaults
> - Apply BehaviorDefinition.parameterOverrides
> - Apply NPCDefinition.behaviorParameters (if present)

**Missing:**
- How to handle nested dictionaries/objects in parameterOverrides?
- How to handle null/empty parameterOverrides?
- How to handle missing ScriptDefinition (should fail or skip)?
- How to handle missing parameters in ScriptDefinition (should skip or use default)?

**Impact:**
- Unclear merge logic implementation
- May have edge cases not handled

**Fix Required:**
- Specify merge algorithm:
  1. Start with empty Dictionary<string, object>
  2. If ScriptDefinition exists and has Parameters:
     - For each parameter in ScriptDefinition.Parameters:
       - Add parameter.Name → parameter.DefaultValue to dictionary
  3. If BehaviorDefinition exists and has ParameterOverrides:
     - For each key-value pair in ParameterOverrides:
       - Override dictionary[key] = value
  4. If NPCDefinition has BehaviorParameters:
     - For each key-value pair in BehaviorParameters:
       - Override dictionary[key] = value
  5. Store final dictionary in EntityVariablesComponent

### Issue 10: Missing ScriptDefinition Lookup Error Handling

**Design Says:**
> Get `scriptId` from BehaviorDefinition, then look up ScriptDefinition

**Plan Says:**
> Get `scriptId` from BehaviorDefinition, then look up ScriptDefinition

**Missing:**
- What if ScriptDefinition is not found?
- What if scriptId is null/empty?
- Should it log error and skip script attachment?

**Impact:**
- Unclear error handling for missing ScriptDefinition
- May cause runtime failures

**Fix Required:**
- Specify error handling:
  - If scriptId is null/empty: Log error, skip script attachment
  - If ScriptDefinition not found: Log error, skip script attachment
  - Continue NPC creation without script (NPC still created, just no behavior script)

## Medium Priority Issues

### Issue 11: Missing Documentation Updates

**Design Says:**
> Document field purposes, parameter resolution order

**Plan Says:**
> No mention of documentation updates

**Impact:**
- No documentation for new BehaviorDefinition system
- Users won't know how to use new system

**Fix Required:**
- Add todo: Update documentation to explain BehaviorDefinition system
- Document parameter resolution order
- Document how to create BehaviorDefinitions
- Document migration from old format

### Issue 12: Missing Test Cases

**Design Says:**
> Testing considerations listed

**Plan Says:**
> Testing considerations listed but not as implementation steps

**Impact:**
- No explicit test implementation steps
- Testing may be overlooked

**Fix Required:**
- Add todos for test implementation:
  - Test BehaviorDefinition lookup
  - Test parameter merging
  - Test validation
  - Test error handling
  - Test backward compatibility

## Summary

### Critical Issues (Must Fix)
1. ✅ Missing validation in ModLoader
2. ✅ Missing EntityVariablesComponent creation details
3. ✅ Missing error handling for missing BehaviorDefinition
4. ✅ Missing parameter type conversion details
5. ✅ Missing ScriptLifecycleSystem validation
6. ✅ Missing backward compatibility strategy
7. ✅ Missing parameter merging implementation details
8. ✅ Missing ScriptDefinition lookup error handling

### Medium Priority Issues (Should Fix)
9. Missing TileBehaviorDefinition (if needed)
10. Missing documentation updates
11. Missing test implementation steps

### Recommendations

1. **Add validation step in ModLoader** when loading BehaviorDefinitions
2. **Specify EntityVariablesComponent creation** in MapLoaderSystem
3. **Add error handling** for all lookup failures (BehaviorDefinition, ScriptDefinition)
4. **Specify parameter type conversion** logic
5. **Add backward compatibility** fallback to direct ScriptDefinition lookup
6. **Add detailed parameter merging** algorithm
7. **Add validation in ScriptLifecycleSystem** for runtime overrides
8. **Clarify TileBehaviorDefinition** scope (included or out of scope)


