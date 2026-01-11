# Animation and Movement Profile System - Implementation Analysis

## Executive Summary

This analysis evaluates the implemented animation and movement profile system for:
1. **Architecture Issues**: Service patterns, dependency injection, initialization order
2. **.cursorrules Compliance**: Fail-fast behavior, nullable types, exception handling, no backward compatibility
3. **Design Discrepancies**: Comparison against original design document and architecture analysis

**Overall Assessment**: The implementation is **architecturally sound** and **mostly compliant** with .cursorrules, but has several **critical missing validations** and **design discrepancies** that need to be addressed.

---

## 1. Architecture Issues

### ✅ COMPLIANT: Service Initialization Order

**Status**: ✅ **FULLY COMPLIANT**

The implementation correctly initializes services in the required order:
1. `ModManager` (loads all definitions)
2. `ProfileService` (loads and validates profiles)
3. `ResourceManager` (depends on ProfileService for animation duration calculation)
4. `ConstantsService` (loads constants)
5. ECS Systems (depend on ResourceManager and ProfileService)

**Evidence**:
- `MonoBallGame.LoadModsSynchronously()` creates ProfileService after ModManager, before ResourceManager (line ~355)
- `GameServices.LoadMods()` also creates ProfileService before ResourceManager
- `ResourceManager` constructor requires `IProfileService` (enforces dependency)

### ✅ COMPLIANT: Profile Definition Classes

**Status**: ✅ **FULLY COMPLIANT**

All required profile definition classes are implemented:
- `MovementProfileDefinition` with `Speeds` dictionary mapping movement types to `SpeedDefinition`
- `AnimationProfileDefinition` with `Animations` dictionary mapping animation types to `AnimationDefinition`
- `SpeedDefinition` includes both `Speed` and `AnimationType` (correct structure per architecture analysis)
- `AnimationDefinition` includes `Duration`, optional `FrameSequence`, and `Description`

**Matches Design**: ✅ Structure matches the required design from architecture analysis (movement types map to animation types).

### ❌ CRITICAL: Missing Cross-Profile Validation

**Issue**: The implementation does **not validate** that movement profile `animationType` values actually exist in the referenced animation profiles.

**Current Implementation** (`ProfileService.ValidateLoadedProfiles()`):
```csharp
// Validates movement profile structure (defaultSpeed exists, speeds have AnimationType)
// Validates animation profile structure (defaultAnimation exists, durations are positive)
// BUT: Does NOT validate that movement profile animationType values exist in animation profiles
```

**Architecture Analysis Requirement**:
> **Required Fix**: Validate that movement profile `animationType` values exist in animation profiles.
> This prevents runtime errors when sprite definitions use movement types that reference non-existent animation types.

**Example Problem**:
```json
// Movement profile references "go_fast" animation type
{
  "id": "base:profile:movement/player",
  "speeds": {
    "run": { "speed": 8.0, "animationType": "go_fast" }
  }
}

// But animation profile doesn't have "go_fast"
{
  "id": "base:profile:animation/standard",
  "animations": {
    "go": { "duration": 0.133 },
    "face": { "duration": 0.266 }
    // Missing "go_fast"!
  }
}
```

**Result**: Runtime error when sprite tries to use "run" movement type, which references "go_fast" animation that doesn't exist.

**Required Fix**: Add cross-profile validation after loading all profiles:
```csharp
private void ValidateLoadedProfiles()
{
    // ... existing validation ...
    
    // NEW: Cross-profile validation
    foreach (var (movementProfileId, movementProfile) in _movementProfiles)
    {
        // Check if movement profile references animation profile IDs (but we don't know which animation profile to check!)
        // PROBLEM: Movement profiles don't specify which animation profile they use
        // SOLUTION: This validation must happen at sprite definition validation time (when we know which animation profile is used)
    }
}
```

**Alternative**: Validate at sprite definition validation time (when `SpriteDefinition` specifies both `movementProfileId` and `animationProfileId`):
- Add validation in `ResourceManager.PrecomputeAnimationFrames()` to check that all animation types referenced by movement profile exist in animation profile.
- Or add post-load validation that validates all sprite definitions after mod loading completes.

**Recommendation**: Add validation in `ResourceManager.PrecomputeAnimationFrames()` (already has access to both profiles) AND add post-load validation that validates all sprite definitions before ResourceManager starts loading sprites (better error messages).

### ⚠️ WARNING: Missing Post-Load Sprite Validation

**Issue**: The implementation does **not validate** sprite profile references before ResourceManager starts loading sprites. This means invalid profile references are only caught when sprites are actually loaded (lazy validation).

**Architecture Analysis Requirement**:
> **Required Fix**: Validate all sprite profile references after mod loading completes, but before ResourceManager starts loading sprites. This follows the existing pattern where validation happens post-load.

**Current Behavior**:
- Sprite definitions reference profiles via `movementProfileId` and `animationProfileId`
- Validation happens **lazily** in `ResourceManager.PrecomputeAnimationFrames()` when sprite is loaded
- **Result**: Runtime errors only occur when sprites are actually used, not during mod loading

**Required Fix**: Add post-load validation (before ResourceManager initialization):
```csharp
// In MonoBallGame.LoadModsSynchronously() or GameServices.LoadMods()
// After ProfileService is initialized, before ResourceManager:

var spriteDefIds = modManager.Registry.GetByType("SpriteDefinition").ToList();
var validationIssues = new List<ValidationIssue>();

foreach (var spriteId in spriteDefIds)
{
    var spriteDef = modManager.GetDefinition<SpriteDefinition>(spriteId);
    if (spriteDef == null) continue;
    
    // Validate movement profile reference
    if (string.IsNullOrWhiteSpace(spriteDef.MovementProfileId))
    {
        validationIssues.Add(new ValidationIssue(ValidationSeverity.Error,
            $"Sprite '{spriteId}' missing required 'movementProfileId' field."));
        continue;
    }
    
    if (!profileService.HasMovementProfile(spriteDef.MovementProfileId))
    {
        validationIssues.Add(new ValidationIssue(ValidationSeverity.Error,
            $"Sprite '{spriteId}' references movement profile '{spriteDef.MovementProfileId}' which does not exist."));
        continue;
    }
    
    // Validate animation profile reference
    if (string.IsNullOrWhiteSpace(spriteDef.AnimationProfileId))
    {
        validationIssues.Add(new ValidationIssue(ValidationSeverity.Error,
            $"Sprite '{spriteId}' missing required 'animationProfileId' field."));
        continue;
    }
    
    if (!profileService.HasAnimationProfile(spriteDef.AnimationProfileId))
    {
        validationIssues.Add(new ValidationIssue(ValidationSeverity.Error,
            $"Sprite '{spriteId}' references animation profile '{spriteDef.AnimationProfileId}' which does not exist."));
        continue;
    }
    
    // Cross-profile validation: Check that all animation types referenced by movement profile exist in animation profile
    var movementProfile = profileService.GetMovementProfile(spriteDef.MovementProfileId);
    var animationProfile = profileService.GetAnimationProfile(spriteDef.AnimationProfileId);
    
    foreach (var (movementType, speedDef) in movementProfile.Speeds)
    {
        if (!animationProfile.Animations.ContainsKey(speedDef.AnimationType))
        {
            validationIssues.Add(new ValidationIssue(ValidationSeverity.Error,
                $"Sprite '{spriteId}' movement profile '{spriteDef.MovementProfileId}' references animation type '{speedDef.AnimationType}' " +
                $"for movement type '{movementType}', but this animation type doesn't exist in animation profile '{spriteDef.AnimationProfileId}'. " +
                $"Available animation types: {string.Join(", ", animationProfile.Animations.Keys)}"));
        }
    }
}

if (validationIssues.Count > 0)
{
    foreach (var issue in validationIssues)
        _logger.Error("Sprite validation error: {Message}", issue.Message);
    
    throw new InvalidOperationException(
        $"Sprite validation failed with {validationIssues.Count} errors. " +
        "Fix sprite definition profile references before continuing.");
}
```

**Impact**: This validation should be added as part of TODO `phase5-2` (pending).

### ⚠️ WARNING: ProfileService Missing IDisposable (Per Plan, But Architecture Analysis Says Not Needed)

**Issue**: The implementation plan specified that `ProfileService` should implement `IDisposable` for hot-reload, but the architecture analysis clarified that hot-reloading is **not yet supported**, so `IDisposable` is not needed.

**Plan Requirement**:
> TODO `phase1-2`: "MUST implement IDisposable for hot-reload and include ILogger parameter"

**Architecture Analysis Clarification**:
> Hot-reloading is explicitly **not supported yet**, so ProfileService does not implement `IDisposable` based on the plan.

**Current Implementation**: `ProfileService` does **not** implement `IDisposable` (correct per architecture analysis).

**Status**: ✅ **COMPLIANT** - Implementation correctly does not implement `IDisposable` since hot-reload is not supported yet. However, the plan TODO should be updated to reflect this decision.

### ✅ COMPLIANT: Path-Based Discovery

**Status**: ✅ **FULLY COMPLIANT**

Profile definitions are discovered via convention-based discovery:
- `KnownPathMappings.cs` includes mappings for `MovementProfile` and `AnimationProfile`
- Paths: `Definitions/Profiles/movement/*` → `MovementProfile`, `Definitions/Profiles/animation/*` → `AnimationProfile`
- Supports both path-based inference and explicit `definitionType` field (flexible for mods)

**Evidence**: `MonoBall.Core/Mods/TypeInference/KnownPathMappings.cs` includes profile mappings.

### ✅ COMPLIANT: Profile Service Pattern

**Status**: ✅ **FULLY COMPLIANT**

`ProfileService` follows the same factory pattern as `ConstantsService`:
- `ProfileServiceFactory` static factory class
- Simplified pattern: direct creation and registration (no "GetOr" logic)
- Registered in `Game.Services` as `IProfileService`

**Matches Design**: ✅ Both `ProfileServiceFactory` and `ConstantsServiceFactory` follow the same simplified pattern.

---

## 2. .cursorrules Compliance Issues

### ✅ COMPLIANT: Fail-Fast Behavior

**Status**: ✅ **FULLY COMPLIANT**

All methods fail-fast with clear exceptions:
- `ProfileService.GetMovementSpeed()` throws `ProfileNotFoundException` or `KeyNotFoundException`
- `ProfileService.CalculateAnimationDurations()` throws `ArgumentException`, `ProfileNotFoundException`, or `KeyNotFoundException`
- `ResourceManager.PrecomputeAnimationFrames()` throws `InvalidOperationException` for missing fields
- `MovementAnimationHelper.OnMovementInProgress()` validates arguments and throws exceptions

**Evidence**: All methods validate arguments at the beginning and throw exceptions immediately if invalid.

### ❌ CRITICAL: InputSystem Logs Warning Instead of Failing Fast

**Issue**: `InputSystem.HandleRunButtonPressed()` catches exceptions and logs warnings instead of failing fast, violating .cursorrules "NO FALLBACK CODE" rule.

**Current Implementation** (`InputSystem.cs:317-337`):
```csharp
try
{
    targetSpeed = _profileService.GetMovementSpeed(spriteDef.MovementProfileId, targetMovementType);
}
catch (ProfileNotFoundException ex)
{
    _logger.Warning(...); // ❌ FALLBACK: Logs warning, continues execution
    return; // Silently fails - player can't run
}
catch (KeyNotFoundException ex)
{
    _logger.Warning(...); // ❌ FALLBACK: Logs warning, continues execution
    return; // Silently fails - player can't run
}
```

**.cursorrules Requirement**:
> **NO FALLBACK CODE** - Fail fast with clear errors rather than silently degrade.
> Use `InvalidOperationException` or `ArgumentNullException` with clear messages.

**Required Fix**: Remove try-catch and let exceptions propagate (fail-fast):
```csharp
// Get movement speed from profile for target movement type (fail-fast)
// No try-catch - let exceptions propagate (fail-fast behavior)
var targetSpeed = _profileService.GetMovementSpeed(spriteDef.MovementProfileId, targetMovementType);

// Update movement speed and type
movement.MovementSpeed = targetSpeed;
movement.CurrentMovementType = targetMovementType;
```

**Reason**: If a sprite definition has an invalid profile reference, this is a **configuration error** that should be caught during mod loading validation (TODO `phase5-2`), not silently ignored at runtime. If it still occurs at runtime, it's a critical error that should crash immediately with a clear error message, not degrade silently.

**Alternative** (if validation is added in `phase5-2`): Keep try-catch but throw `InvalidOperationException` instead of returning:
```csharp
catch (ProfileNotFoundException ex)
{
    throw new InvalidOperationException(
        $"Sprite definition '{spriteId}' references movement profile '{spriteDef.MovementProfileId}' which does not exist. " +
        "This should have been caught during mod loading validation.",
        ex);
}
```

**Recommendation**: Remove try-catch (fail-fast) since validation should prevent this error from occurring.

### ✅ COMPLIANT: Nullable Reference Types

**Status**: ✅ **FULLY COMPLIANT**

All nullable types are properly annotated:
- `ProfileService` constructor parameters are non-nullable (throw `ArgumentNullException`)
- Optional parameters use `?` (e.g., `frameSequenceOverride?: double[]?`)
- Return types are non-nullable (fail-fast exceptions instead of returning null)

**Evidence**: All method signatures use proper nullable annotations and throw exceptions for null values.

### ✅ COMPLIANT: XML Documentation

**Status**: ✅ **FULLY COMPLIANT**

All public APIs have XML comments:
- `IProfileService` interface has complete XML documentation with `<param>`, `<returns>`, `<exception>` tags
- `ProfileService` class has XML documentation
- All public methods document exceptions

**Evidence**: All interface and class methods have comprehensive XML documentation.

### ✅ COMPLIANT: No Backward Compatibility

**Status**: ✅ **FULLY COMPLIANT**

The implementation correctly breaks backward compatibility:
- Removed `PlayerMovementSpeed` constant from validation list in `MonoBallGame.cs`
- Removed `PlayerMovementSpeed` from `Mods/core/Definitions/Constants/Player.json`
- `SpriteAnimation` removed `FrameDurations` field (breaking change)
- All sprite definitions must now include `movementProfileId` and `animationProfileId` (breaking change)

**Evidence**: Old constants removed, old JSON structure removed, new required fields enforced.

### ⚠️ WARNING: Exception Handling in ProfileService.GetMovementTypeForSpeed()

**Issue**: `ProfileService.GetMovementTypeForSpeed()` catches `KeyNotFoundException` and wraps it in `InvalidOperationException`, which is correct, but the exception handling could be improved.

**Current Implementation** (`ProfileService.cs:261-290`):
```csharp
public float GetDefaultMovementSpeed(string profileId)
{
    // ...
    try
    {
        return GetMovementSpeed(profileId, movementProfile.DefaultSpeed);
    }
    catch (KeyNotFoundException ex)
    {
        throw new InvalidOperationException(
            $"Movement profile '{profileId}' specifies DefaultSpeed '{movementProfile.DefaultSpeed}', but this type doesn't exist in the profile.",
            ex);
    }
}
```

**Status**: ✅ **COMPLIANT** - This is correct exception handling (wraps specific exception with more context). However, this error should be caught during `ValidateLoadedProfiles()`, so this catch should never execute (defensive programming is fine).

### ✅ COMPLIANT: Argument Validation

**Status**: ✅ **FULLY COMPLIANT**

All methods validate arguments at the beginning:
- `GetMovementSpeed()` validates `profileId` and `movementType` (throws `ArgumentException`)
- `CalculateAnimationDurations()` validates `profileId`, `animationType`, `frameCount` (throws `ArgumentException`)
- All methods throw `ArgumentNullException` for null arguments

**Evidence**: All methods start with argument validation checks.

---

## 3. Design Discrepancies

### ✅ COMPLIANT: Movement Profile Structure

**Status**: ✅ **FULLY COMPLIANT**

The implementation matches the required design from architecture analysis:
- `MovementProfileDefinition.Speeds` is `Dictionary<string, SpeedDefinition>` (correct)
- `SpeedDefinition` includes both `Speed` and `AnimationType` (correct)
- Matches the required structure for Pokemon-style walk/run/bike animation selection

**Matches Design**: ✅ Structure matches architecture analysis requirement exactly.

### ✅ COMPLIANT: GridMovement Component

**Status**: ✅ **FULLY COMPLIANT**

`GridMovement` component includes `CurrentMovementType` property as required:
- Property name: `CurrentMovementType` (string)
- Stored in component (value type, data-only)
- Used by `MovementAnimationHelper` to determine animation type

**Matches Design**: ✅ Matches architecture analysis requirement exactly.

### ✅ COMPLIANT: MovementAnimationHelper Integration

**Status**: ✅ **FULLY COMPLIANT**

`MovementAnimationHelper.OnMovementInProgress()` correctly uses profiles:
- Accepts `spriteId`, `IProfileService`, and `IResourceManager` as parameters
- Gets sprite definition to access `MovementProfileId`
- Uses `CurrentMovementType` to determine animation type from movement profile
- Builds animation name: `"{animationType}_{direction}"` (e.g., "go_fast_south")

**Matches Design**: ✅ Matches architecture analysis requirement exactly (Option A: keep static, pass services as parameters).

### ✅ COMPLIANT: ResourceManager PrecomputeAnimationFrames Integration

**Status**: ✅ **FULLY COMPLIANT**

`ResourceManager.PrecomputeAnimationFrames()` correctly uses `ProfileService`:
- Validates required `animationProfileId` and `animationType` fields (fail-fast)
- Uses `_profileService.CalculateAnimationDurations()` instead of `animation.FrameDurations`
- Handles `frameSequenceOverride` from animation definition
- Throws `InvalidOperationException` with clear error messages

**Matches Design**: ✅ Matches architecture analysis requirement exactly (breaking change correctly implemented).

### ❌ CRITICAL: Missing Profile Operation Support

**Issue**: The implementation does **not support** profile override/extend/replace operations (`$operation` field) as specified in the architecture analysis.

**Architecture Analysis Requirement**:
> **Required Fix**: Support profile operations:
> - `$operation: "Modify"` for overriding individual speed values
> - `$operation: "Extend"` for adding new speed types
> - `$operation: "Replace"` for replacing entire profile
> - Merge operations during `ProfileService.LoadProfiles()`

**Current Implementation** (`ProfileService.LoadProfiles()`):
```csharp
// Note: Operation merging (Modify/Extend/Replace) is handled by ModLoader during definition loading
// By the time definitions reach ProfileService, they're already merged at JSON level
// So we just deserialize the final merged definitions
```

**Status**: ⚠️ **UNCLEAR** - The comment suggests that `ModLoader` handles operation merging, but this needs to be verified. If `ModLoader` doesn't handle dictionary merging for profiles, then profile operations won't work.

**Verification Needed**: Check if `ModLoader` supports dictionary merging for profile definitions (similar to how constants are merged). If not, profile operations need to be implemented.

**Impact**: Mods cannot override/extend profiles using the existing `$operation` system (limits moddability).

### ❌ CRITICAL: Missing ProfileValidator Class

**Issue**: The implementation does **not include** a `ProfileValidator` class as specified in the plan TODO `phase5-1`.

**Plan Requirement**:
> TODO `phase5-1`: "Create ProfileValidator class with validation methods for profile structure and cross-profile references - use existing MonoBall.Core.Mods.ValidationIssue class"

**Current Implementation**: Validation is done inline in `ProfileService.ValidateLoadedProfiles()`, not in a separate `ProfileValidator` class.

**Impact**: Validation logic is not reusable and not integrated with `ModValidator` for mod loading validation.

**Required Fix**: Extract validation logic to `ProfileValidator` class and integrate with `ModValidator`:
```csharp
public class ProfileValidator
{
    public ValidationIssue[] ValidateMovementProfile(MovementProfileDefinition profile)
    {
        var issues = new List<ValidationIssue>();
        // ... validation logic ...
        return issues.ToArray();
    }
    
    public ValidationIssue[] ValidateAnimationProfile(AnimationProfileDefinition profile)
    {
        // ... validation logic ...
    }
    
    public ValidationIssue[] ValidateCrossProfileReferences(
        MovementProfileDefinition movementProfile,
        AnimationProfileDefinition animationProfile)
    {
        // ... cross-profile validation ...
    }
}
```

**Recommendation**: Create `ProfileValidator` class as specified in plan TODO `phase5-1`.

---

## Summary of Issues

### Critical Issues (Must Fix)

1. **Missing Cross-Profile Validation** ❌
   - **Issue**: Movement profile `animationType` values are not validated against animation profiles
   - **Impact**: Runtime errors when sprite definitions use movement types that reference non-existent animation types
   - **Fix**: Add validation in `ResourceManager.PrecomputeAnimationFrames()` AND post-load sprite validation (TODO `phase5-2`)

2. **InputSystem Violates Fail-Fast Rule** ❌
   - **Issue**: `InputSystem.HandleRunButtonPressed()` logs warnings instead of failing fast
   - **Impact**: Violates .cursorrules "NO FALLBACK CODE" rule, silently degrades instead of crashing
   - **Fix**: Remove try-catch, let exceptions propagate (or throw `InvalidOperationException` if keeping try-catch)

3. **Missing Profile Operation Support** ❌
   - **Issue**: Profile override/extend/replace operations not implemented
   - **Impact**: Mods cannot customize profiles using `$operation` system (limits moddability)
   - **Fix**: Implement dictionary merging in `ProfileService.LoadProfiles()` OR verify that `ModLoader` handles it

### High Priority Issues (Should Fix)

4. **Missing Post-Load Sprite Validation** ⚠️
   - **Issue**: Sprite profile references not validated before ResourceManager starts loading sprites
   - **Impact**: Runtime errors only occur when sprites are used, not during mod loading
   - **Fix**: Add post-load validation (TODO `phase5-2`)

5. **Missing ProfileValidator Class** ⚠️
   - **Issue**: Validation logic not extracted to reusable `ProfileValidator` class
   - **Impact**: Validation not integrated with `ModValidator`, not reusable
   - **Fix**: Create `ProfileValidator` class (TODO `phase5-1`)

### Minor Issues

6. **Plan TODO Inconsistency** ⚠️
   - **Issue**: Plan TODO `phase1-2` says "MUST implement IDisposable", but architecture analysis says it's not needed (hot-reload not supported)
   - **Impact**: Confusion about requirements
   - **Fix**: Update plan TODO to reflect that IDisposable is not needed (hot-reload not supported yet)

---

## Recommendations

### Immediate Actions (Before Next Release)

1. **Fix InputSystem Fail-Fast Violation**: Remove try-catch in `HandleRunButtonPressed()`, let exceptions propagate
2. **Add Cross-Profile Validation**: Validate that movement profile `animationType` values exist in animation profiles (in `ResourceManager.PrecomputeAnimationFrames()`)
3. **Verify Profile Operation Support**: Check if `ModLoader` handles dictionary merging for profiles. If not, implement it.

### Next Sprint (TODO phase5-1, phase5-2)

4. **Create ProfileValidator Class**: Extract validation logic to reusable class, integrate with `ModValidator`
5. **Add Post-Load Sprite Validation**: Validate all sprite profile references after mod loading, before ResourceManager starts

### Future Enhancements

6. **Profile Hot-Reload Support**: When hot-reload is implemented, add `IDisposable` to `ProfileService` and implement cache invalidation
7. **Profile Dependency Validation**: Validate that sprite definitions don't reference profiles from other mods without dependencies

---

## Conclusion

The implementation is **architecturally sound** and **mostly compliant** with .cursorrules, with the following assessment:

**Strengths**:
- ✅ Correct service initialization order
- ✅ Proper fail-fast behavior (except InputSystem)
- ✅ Correct profile structure matching design
- ✅ Proper integration with ResourceManager and systems
- ✅ No backward compatibility maintained (correct)

**Critical Gaps**:
- ❌ Missing cross-profile validation (runtime errors possible)
- ❌ InputSystem violates fail-fast rule (silent degradation)
- ❌ Missing profile operation support (limits moddability)

**Recommendation**: Fix critical issues (1-3) before next release, then address high-priority issues (4-5) in next sprint.
