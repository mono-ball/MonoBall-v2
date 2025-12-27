# Plan .cursorrules Violations Analysis

## Overview

This document analyzes the implementation plan for violations of `.cursorrules` principles, specifically:
1. **NO BACKWARD COMPATIBILITY** - Refactor APIs freely, break existing code if needed, update all call sites
2. **NO FALLBACK CODE** - Fail fast with clear exceptions, never silently degrade or use default values for required dependencies

## Critical Violations Found

### Violation 1: Backward Compatibility Fallback

**Location**: Step 3, Error Handling section (line 143)

**Current Plan**:
```csharp
// If behaviorDef == null:
// Try backward compatibility fallback: Look up as ScriptDefinition directly
// If ScriptDefinition found: Log deprecation warning, use it directly
```

**Violation**: 
- `.cursorrules` states: "NEVER maintain backward compatibility - refactor APIs freely when improvements are needed"
- `.cursorrules` states: "Break existing code if necessary - update all call sites to use new APIs"
- `.cursorrules` states: "Remove deprecated code immediately - don't keep old implementations alongside new ones"

**Problem**:
- Plan includes fallback to old behavior (direct ScriptDefinition lookup)
- This maintains backward compatibility, which is explicitly forbidden
- Should fail fast instead

**Fix Required**:
- Remove backward compatibility fallback
- If BehaviorDefinition not found, throw `InvalidOperationException` with clear message
- Update all call sites (map JSONs) to use BehaviorDefinition IDs instead of ScriptDefinition IDs

### Violation 2: Silent Degradation on Missing ScriptDefinition

**Location**: Step 3, Error Handling section (line 149)

**Current Plan**:
```csharp
if (scriptDef == null)
{
    _logger.Warning(...);
    return; // Skip script attachment, continue NPC creation
}
```

**Violation**:
- `.cursorrules` states: "NEVER introduce fallback code - code should fail fast with clear errors rather than silently degrade"
- `.cursorrules` states: "No default values for critical dependencies - don't use optional parameters or null defaults for required services"

**Problem**:
- Plan silently skips script attachment and continues NPC creation
- This is silent degradation - NPC is created without its behavior script
- Should fail fast with exception

**Fix Required**:
- Throw `InvalidOperationException` if ScriptDefinition not found
- Fail fast with clear error message
- Don't create NPC if required script is missing

### Violation 3: Silent Degradation on Invalid Parameters

**Location**: Step 3, Parameter Type Conversion (line 294) and Validation (lines 336, 341, 346, 351)

**Current Plan**:
```csharp
catch (Exception ex)
{
    _logger.Warning(...);
    return null; // Use ScriptDefinition default instead
}
```

And:
```csharp
_logger.Warning(...);
mergedParameters[paramDef.Name] = paramDef.DefaultValue;
```

**Violation**:
- `.cursorrules` states: "NEVER introduce fallback code - code should fail fast with clear errors rather than silently degrade"
- `.cursorrules` states: "No default values for critical dependencies"

**Problem**:
- Plan uses ScriptDefinition defaults when parameters are invalid
- This is fallback code - silently degrades by using defaults
- Should fail fast instead

**Fix Required**:
- Throw `InvalidOperationException` for invalid parameters
- Don't use defaults as fallback
- Fail fast with clear error message

### Violation 4: Backward Compatibility in Migration Notes

**Location**: Migration Notes section (line 639)

**Current Plan**:
```
- Keep `RangeX`/`RangeY` properties in `NpcDefinition` class for backward compatibility during transition
```

**Violation**:
- `.cursorrules` states: "NEVER maintain backward compatibility"
- `.cursorrules` states: "Remove deprecated code immediately"

**Problem**:
- Plan keeps deprecated properties for backward compatibility
- Should remove them immediately

**Fix Required**:
- Remove `RangeX` and `RangeY` properties from `NpcDefinition` class
- Update all map JSONs to use `behaviorParameters` instead
- No transition period - break existing code, update all call sites

### Violation 5: Backward Compatibility Section

**Location**: Backward Compatibility section (lines 645-660)

**Current Plan**:
```
## Backward Compatibility

**BehaviorDefinition Lookup Fallback:**
- If BehaviorDefinition not found, try direct ScriptDefinition lookup (backward compatibility)
- Log deprecation warning when old format is used
- Old format: `behaviorId` directly references ScriptDefinition ID
- New format: `behaviorId` references BehaviorDefinition ID, which references ScriptDefinition
- Both formats supported during migration period
```

**Violation**:
- Entire section violates "NO BACKWARD COMPATIBILITY" rule
- Plan explicitly supports both old and new formats
- Should only support new format

**Fix Required**:
- Remove entire "Backward Compatibility" section
- Remove fallback logic
- Only support new format (BehaviorDefinition lookup)
- Update all map JSONs to use new format

### Violation 6: Silent Degradation in Error Handling

**Location**: Error Handling section (line 659)

**Current Plan**:
```
- Invalid parameters: Log warning, use ScriptDefinition default, continue execution
- All errors are non-fatal - NPC creation always succeeds (with or without script)
```

**Violation**:
- `.cursorrules` states: "NEVER introduce fallback code - code should fail fast with clear errors rather than silently degrade"
- `.cursorrules` states: "No default values for critical dependencies"

**Problem**:
- Plan makes all errors non-fatal
- NPC creation succeeds even when script is missing
- Should fail fast instead

**Fix Required**:
- Make errors fatal - throw exceptions
- NPC creation should fail if required script is missing
- Fail fast with clear error messages

## Summary of Required Changes

### Remove:
1. ❌ Backward compatibility fallback to ScriptDefinition lookup
2. ❌ Silent degradation (skip script attachment, continue NPC creation)
3. ❌ Using ScriptDefinition defaults as fallback for invalid parameters
4. ❌ Keeping `RangeX`/`RangeY` properties for backward compatibility
5. ❌ Entire "Backward Compatibility" section
6. ❌ Non-fatal error handling

### Add:
1. ✅ Fail fast with `InvalidOperationException` when BehaviorDefinition not found
2. ✅ Fail fast with `InvalidOperationException` when ScriptDefinition not found
3. ✅ Fail fast with `InvalidOperationException` for invalid parameters
4. ✅ Remove `RangeX`/`RangeY` properties immediately
5. ✅ Update all map JSONs to use `behaviorParameters` instead
6. ✅ Make all errors fatal - don't create NPC if script is missing

## Updated Error Handling Strategy

**Fail Fast Approach**:
- Missing BehaviorDefinition: Throw `InvalidOperationException` with message: `"BehaviorDefinition '{behaviorId}' not found for NPC '{npcId}'. Ensure the behavior definition exists and is loaded."`
- Missing ScriptDefinition: Throw `InvalidOperationException` with message: `"ScriptDefinition '{scriptId}' not found for BehaviorDefinition '{behaviorId}'. Ensure the script definition exists and is loaded."`
- Invalid parameters: Throw `InvalidOperationException` with message: `"Invalid parameter '{paramName}' for script '{scriptId}': {reason}"`
- Empty scriptId: Throw `InvalidOperationException` with message: `"BehaviorDefinition '{behaviorId}' has empty scriptId"`

**No Fallbacks**:
- Don't try ScriptDefinition lookup if BehaviorDefinition not found
- Don't skip script attachment and continue NPC creation
- Don't use ScriptDefinition defaults for invalid parameters
- Don't create NPC without required script

## Migration Strategy (Updated)

**No Transition Period**:
- Remove `RangeX`/`RangeY` properties immediately
- Update all map JSONs immediately
- Break existing code - update all call sites
- No backward compatibility support

**Update All Call Sites**:
- Update all map JSONs to use `behaviorParameters` instead of `rangeX`/`rangeY`
- Update all map JSONs to use BehaviorDefinition IDs instead of ScriptDefinition IDs
- No gradual migration - all at once


