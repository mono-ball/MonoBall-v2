# Animation and Movement Profile System Design - .cursorrules Compliance Analysis

## Issues Found

### ❌ CRITICAL: Backward Compatibility Violation

**Location**: Multiple sections (lines 13, 459-467, 802-808, 849)

**Issue**: The design document extensively discusses "backward compatibility" and "migration path" with fallback support, which directly violates the `.cursorrules` CRITICAL RULE #1: **NO BACKWARD COMPATIBILITY**.

**Rule Violated**:
```
1. **NO BACKWARD COMPATIBILITY** - Refactor APIs freely, break existing code if needed, update all call sites
```

**Specific Violations**:
- Line 13: "Backward Compatibility: Support existing hard-coded values as defaults during migration"
- Lines 459-467: "Backward Compatibility" section describing dual format support
- Lines 802-808: "Default Fallbacks" section with silent degradation behavior
- Line 849: "Mixed old/new format support" in testing strategy

**Required Fix**: Remove all backward compatibility discussions. Instead, design for immediate refactoring:
- Update all sprite definitions in one pass
- Update all systems in one pass
- Remove old format support immediately
- Break existing code if necessary - update all call sites

### ⚠️ WARNING: Fallback Code Pattern

**Location**: Lines 672-674, 687-690, 733-750

**Issue**: The design uses fallback/default profile IDs and optional null checks, which could be interpreted as "fallback code" violating CRITICAL RULE #2: **NO FALLBACK CODE**.

**Rule Violated**:
```
2. **NO FALLBACK CODE** - Fail fast with clear exceptions, never silently degrade or use default values for required dependencies
```

**Specific Violations**:
- Line 672: `?? "base:profile:movement/player"` - default profile fallback
- Line 687: `spriteDef?.MovementProfileId ?? "base:profile:movement/npc"` - null coalescing fallback
- Lines 733-750: Conditional logic for old vs new format with fallback behavior

**Required Fix**: 
- Make profile references required (non-nullable) in sprite definitions
- Throw `ArgumentNullException` or `InvalidOperationException` for missing profiles
- Fail fast with clear error messages
- Remove default profile fallbacks - require explicit profile references

**Exception**: Default profiles in JSON definitions are acceptable as they're configuration data, not code fallbacks. However, the code should not silently fall back to defaults if a profile is missing.

### ⚠️ WARNING: Namespace Location Ambiguity

**Location**: Line 474, 536

**Issue**: The design specifies `namespace MonoBall.Core.Profiles` but doesn't clearly specify where the directory should be located in the file structure.

**Rule Reference**:
```
Namespace: Match folder structure, root is `MonoBall.Core`
```

**Required Fix**: Specify exact directory location:
- Should it be `MonoBall.Core/Profiles/` (like `Constants/`)?
- Or `MonoBall.Core/Mods/Profiles/` (if mod-related)?
- Or `MonoBall.Core/ECS/Services/Profiles/` (if ECS-related)?

**Recommendation**: Since profiles are used by both mod definitions and ECS systems, follow the `Constants/` pattern and place at root: `MonoBall.Core/Profiles/`

### ⚠️ WARNING: Performance - Array Allocations

**Location**: Lines 598-646

**Issue**: The `GetAnimationDurations` method creates new arrays (`durations`, `frameDurationsSeconds`) on every call, which could cause allocations in hot paths.

**Rule Reference**:
```
- **Avoid allocations** in Update/Draw loops (object pooling for frequently created objects)
- **Pre-size collections** when size is known (`new List<T>(capacity)`)
```

**Required Fix**: 
- Pre-calculate durations at sprite load time (not during animation playback)
- Cache duration arrays in `SpriteAnimation` or component
- Use `ArrayPool<T>` if dynamic allocation is unavoidable
- Document that durations should be pre-calculated, not computed on-demand

### ✅ COMPLIANT: Service Naming

**Location**: Lines 474-532

**Status**: ✅ **COMPLIANT**

- `IProfileService` - Interface with `I` prefix
- `ProfileService` - Class ends with `Service` suffix
- Matches existing patterns (e.g., `IConstantsService`, `ConstantsService`)

### ✅ COMPLIANT: XML Documentation

**Location**: Lines 476-530

**Status**: ✅ **COMPLIANT**

- All public methods have XML comments
- `<summary>`, `<param>`, `<returns>`, `<exception>` tags are used
- Matches `.cursorrules` documentation standards

### ✅ COMPLIANT: Dependency Injection

**Location**: Lines 549-553

**Status**: ✅ **COMPLIANT**

- Constructor injection with required dependencies
- `ArgumentNullException` thrown for null `definitionRegistry`
- Matches `.cursorrules` DI patterns

### ✅ COMPLIANT: Error Handling

**Location**: Lines 572-646

**Status**: ✅ **COMPLIANT** (mostly)

- Uses `ProfileNotFoundException` with clear error messages
- Fails fast instead of silent degradation
- **Exception**: The default fallback behavior in integration examples violates fail-fast principle

### ⚠️ WARNING: Exception Type

**Location**: Line 576, 581, 611, 616

**Issue**: Uses custom `ProfileNotFoundException` which is not defined in the design.

**Required Fix**: Either:
1. Define the exception class in the design document
2. Use standard exceptions (`InvalidOperationException`, `KeyNotFoundException`)
3. Follow existing project patterns for custom exceptions

## Required Design Changes

### 1. Remove Backward Compatibility Sections

**Change**: Remove all backward compatibility discussions and migration phases that involve dual-format support.

**Replace with**: Single-phase refactoring approach:
- Update all sprite definitions in one pass
- Update all systems in one pass  
- Remove old format immediately
- All call sites must be updated

### 2. Make Profiles Required (Fail-Fast)

**Change**: Make profile references required (non-nullable) in sprite definitions.

**Before**:
```csharp
var movementProfileId = _spriteDefinition.MovementProfileId ?? "base:profile:movement/player";
```

**After**:
```csharp
if (string.IsNullOrWhiteSpace(_spriteDefinition.MovementProfileId))
{
    throw new InvalidOperationException(
        $"Sprite definition '{_spriteDefinition.Id}' must specify a MovementProfileId. " +
        "Add 'movementProfileId' field to sprite definition JSON.");
}
var movementProfileId = _spriteDefinition.MovementProfileId;
```

### 3. Specify Directory Structure

**Change**: Add explicit directory structure to the design.

**Add to Architecture section**:
```
MonoBall.Core/
└── Profiles/              # Profile system (like Constants/)
    ├── IProfileService.cs
    ├── ProfileService.cs
    ├── MovementProfileDefinition.cs
    └── AnimationProfileDefinition.cs
```

### 4. Pre-Calculate Durations

**Change**: Move duration calculation to sprite load time, not animation playback time.

**Update Integration section**:
- Calculate durations when sprite definition is loaded
- Store durations in `SpriteAnimation` component or `SpriteDefinition`
- `GetAnimationDurations` should only retrieve pre-calculated values, not compute them

### 5. Define Custom Exception

**Change**: Either define `ProfileNotFoundException` or use standard exceptions.

**Option A - Use Standard Exception**:
```csharp
throw new KeyNotFoundException($"Movement profile '{profileId}' not found.");
```

**Option B - Define Custom Exception**:
```csharp
/// <summary>
/// Exception thrown when a profile is not found in the registry.
/// </summary>
public class ProfileNotFoundException : InvalidOperationException
{
    public string ProfileId { get; }
    
    public ProfileNotFoundException(string profileId)
        : base($"Profile '{profileId}' not found in registry.")
    {
        ProfileId = profileId;
    }
}
```

## Recommended Design Updates

### Update Migration Strategy Section

**Replace "Migration Strategy" with "Implementation Strategy"**:

```markdown
## Implementation Strategy

### Phase 1: Infrastructure (Week 1)
1. Create profile definition structures (`MovementProfileDefinition`, `AnimationProfileDefinition`)
2. Create profile service interface and implementation in `MonoBall.Core/Profiles/`
3. Create default profile JSON files in `Mods/core/Definitions/Profiles/`
4. Update definition loader to support profile definitions
5. Add profile service to dependency injection

### Phase 2: Refactoring (Week 2)
1. Update `SpriteDefinition` to include required `movementProfileId` and `animationProfileId` fields
2. Update `SpriteAnimation` to include required `animationType` field
3. Update all sprite definitions in mods to include profile references (breaking change)
4. Update `SpriteAnimationSystem` to use profile service for duration retrieval (pre-calculated at load time)
5. Update `PlayerSystem` to use movement profiles (fail-fast if missing)
6. Update `MapLoaderSystem` to use movement profiles for NPCs (fail-fast if missing)

### Phase 3: Porycon3 Update (Week 3)
1. Update `SpriteExtractor` to generate animations with `animationType` instead of `frameDurations`
2. Update `AnimationParser` to map pokeemerald animation constants to profile animation types
3. Remove hard-coded duration calculations from `GenerateDefaultAnimations()`
4. All generated sprite definitions must include profile references

### Phase 4: Cleanup (Week 4)
1. Remove hard-coded movement speed constants from code
2. Remove hard-coded animation duration calculations
3. Update documentation
4. Add validation: sprite definitions without profiles will fail to load
```

### Update Error Handling Section

**Replace "Default Fallbacks" with "Required Fields"**:

```markdown
### Required Fields and Validation

- **Required fields**: Profile IDs must be specified in sprite definitions (no defaults)
- **Speed validation**: Movement speeds must be within reasonable bounds (0.1 - 100.0 tiles/second)
- **Animation validation**: Animation durations must be positive
- **Reference validation**: Sprite definitions must reference valid profile IDs (fail-fast)

### Error Handling

- **Missing profiles**: Throw `InvalidOperationException` with clear error message listing available profiles
- **Missing movement types**: Throw `KeyNotFoundException` with suggestions for available types
- **Missing animation types**: Throw `KeyNotFoundException` with suggestions for available types
- **Invalid references**: Fail-fast during mod loading, do not allow invalid references
- **Missing profile fields**: Throw `InvalidOperationException` during sprite definition loading
```

## Compliance Summary

| Area | Status | Issues | Priority |
|------|--------|--------|----------|
| Backward Compatibility | ❌ **VIOLATION** | Multiple sections violate NO BACKWARD COMPATIBILITY rule | **CRITICAL** |
| Fallback Code | ⚠️ **WARNING** | Default profile fallbacks could be seen as fallback code | **HIGH** |
| Namespace Location | ⚠️ **WARNING** | Directory structure not specified | **MEDIUM** |
| Performance | ⚠️ **WARNING** | Array allocations in hot path (if not pre-calculated) | **MEDIUM** |
| Service Naming | ✅ **COMPLIANT** | Follows naming conventions | - |
| XML Documentation | ✅ **COMPLIANT** | Complete XML docs on all public APIs | - |
| Dependency Injection | ✅ **COMPLIANT** | Proper constructor injection with null checks | - |
| Error Handling | ⚠️ **PARTIAL** | Good exceptions but needs fail-fast enforcement | **HIGH** |

## Action Items

1. **CRITICAL**: Remove all backward compatibility sections and replace with refactoring approach
2. **HIGH**: Remove default profile fallbacks, make profiles required with fail-fast validation
3. **HIGH**: Update error handling to enforce fail-fast behavior
4. **MEDIUM**: Specify exact directory structure for Profiles namespace
5. **MEDIUM**: Clarify that duration calculation happens at load time, not runtime
6. **LOW**: Define custom exception or use standard exceptions consistently
