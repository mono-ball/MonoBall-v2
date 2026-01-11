# Animation and Movement Profile System - Architecture Analysis

## Executive Summary

This analysis evaluates the animation and movement profile system design for:
1. **Architecture Issues**: Definition loading, service patterns, dependency injection
2. **Arch ECS/Event Issues**: Component design, event-driven patterns, system integration
3. **Moddability Issues**: Definition discovery, validation, override operations, mod dependencies
4. **Pokemon-Style Game Issues**: Movement/animation synchronization, frame timing, multiple movement types

**Overall Assessment**: The design has a solid foundation but needs several architectural improvements for proper integration with the existing MonoBall architecture.

---

## 1. Architecture Issues

### ❌ CRITICAL: Profile Loading Order Dependency

**Issue**: Profile definitions must be loaded **before** sprite definitions that reference them, but the current design doesn't specify when profile validation occurs.

**Current Problem**:
- Sprite definitions reference profiles via `movementProfileId` and `animationProfileId`
- These references are validated when sprite definitions are loaded
- But profile definitions are loaded via convention-based discovery in no guaranteed order
- **Result**: Sprite definitions may fail to load if their referenced profiles haven't been discovered yet

**Existing Pattern** (Constants System):
```csharp
// Constants are loaded via convention-based discovery
// ConstantsService validates and loads them during initialization
// Other systems depend on ConstantsService being initialized first
```

**Required Fix**: Profile definitions must be validated and loaded **during mod initialization**, before sprite definitions are processed. Two options:

**Option A: Post-Load Validation** (Recommended):
```csharp
// In ModLoader or ProfileService initialization
// After all definitions are discovered, validate profile references
foreach (var spriteDef in GetAllSpriteDefinitions())
{
    ValidateProfileReferences(spriteDef); // Fail-fast if profiles don't exist
}
```

**Option B: Dependency-Based Loading Order**:
```csharp
// Load profiles first, then sprites
LoadProfileDefinitions(mod);
LoadSpriteDefinitions(mod); // Profiles are guaranteed to exist
```

**Recommendation**: Use **Option A** - validate all profile references after mod loading completes, but before sprite definitions are used. This follows the existing pattern where validation happens post-load.

### ⚠️ WARNING: Profile Service Initialization Timing

**Issue**: `ProfileService` must be initialized **before** `ResourceManager` loads sprite definitions, because sprite loading will pre-calculate durations using profiles.

**Current Problem**:
- `ResourceManager.PrecomputeAnimationFrames()` is called when sprite definitions are loaded
- This method needs `IProfileService` to calculate durations from profiles
- But the design doesn't specify when `ProfileService` is initialized

**Existing Pattern**:
```csharp
// GameServices.Initialize() initializes services in order:
1. ModManager (loads all definitions)
2. ConstantsService (depends on ModManager)
3. ProfileService (should depend on ModManager, initialize before ResourceManager)
4. ResourceManager (depends on ModManager and ProfileService)
5. ECS Systems (depend on ResourceManager)
```

**Required Fix**: Specify initialization order in `GameServices`:
1. ModManager loads all definitions (profiles, sprites, etc.)
2. ProfileService validates and caches all profiles
3. ResourceManager loads sprites (pre-calculates durations using ProfileService)
4. ECS Systems initialize

### ⚠️ WARNING: Definition Type Inference for Profiles

**Issue**: Profile definitions need to be discovered via convention-based discovery, but the design doesn't specify the path pattern.

**Existing Pattern** (from convention-based discovery):
```csharp
// Definitions are inferred from file paths:
// - "Definitions/Constants/player.json" → "ConstantsDefinitions"
// - "Definitions/Assets/Sprites/player.json" → "SpriteDefinition"
// - "Definitions/Profiles/movement/player.json" → ???
```

**Required Fix**: Add path inference pattern for profiles:
```csharp
// In ModLoader.InferDefinitionType():
if (normalizedPath.Contains("/Profiles/Movement/", StringComparison.OrdinalIgnoreCase) ||
    normalizedPath.Contains("/Profiles/movement/", StringComparison.OrdinalIgnoreCase))
{
    return "MovementProfile";
}
if (normalizedPath.Contains("/Profiles/Animation/", StringComparison.OrdinalIgnoreCase) ||
    normalizedPath.Contains("/Profiles/animation/", StringComparison.OrdinalIgnoreCase))
{
    return "AnimationProfile";
}
```

**Alternative**: Use explicit `definitionType` field in JSON (already supported):
```json
{
  "id": "base:profile:movement/player",
  "definitionType": "MovementProfile",
  ...
}
```

**Recommendation**: Support both path-based inference AND explicit `definitionType` field (more flexible for mods).

### ⚠️ WARNING: Profile Definition Classes Missing

**Issue**: The design references `MovementProfileDefinition` and `AnimationProfileDefinition` classes, but doesn't show their structure or how they're deserialized from JSON.

**Existing Pattern** (SpriteDefinition):
```csharp
public class SpriteDefinition
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;
    
    [JsonPropertyName("animations")]
    public List<SpriteAnimation> Animations { get; set; } = new();
    ...
}
```

**Required Fix**: Define profile definition classes matching the JSON schema:
```csharp
namespace MonoBall.Core.Profiles;

/// <summary>
/// Definition for a movement profile loaded from JSON.
/// </summary>
public class MovementProfileDefinition
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;
    
    [JsonPropertyName("definitionType")]
    public string DefinitionType { get; set; } = "MovementProfile";
    
    [JsonPropertyName("speeds")]
    public Dictionary<string, float> Speeds { get; set; } = new();
    
    [JsonPropertyName("defaultSpeed")]
    public string DefaultSpeed { get; set; } = string.Empty;
    
    [JsonPropertyName("validationRules")]
    public Dictionary<string, ValidationRule>? ValidationRules { get; set; }
}

/// <summary>
/// Definition for an animation profile loaded from JSON.
/// </summary>
public class AnimationProfileDefinition
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;
    
    [JsonPropertyName("definitionType")]
    public string DefinitionType { get; set; } = "AnimationProfile";
    
    [JsonPropertyName("animations")]
    public Dictionary<string, AnimationDefinition> Animations { get; set; } = new();
    
    [JsonPropertyName("defaultAnimation")]
    public string DefaultAnimation { get; set; } = string.Empty;
}
```

### ✅ COMPLIANT: Service Location and Namespace

**Status**: ✅ **COMPLIANT**

The design correctly places `ProfileService` in `MonoBall.Core/Profiles/`, matching the `Constants/` pattern. Namespace matches directory structure as required.

---

## 2. Arch ECS/Event Issues

### ❌ CRITICAL: Missing Event for Profile Discovery

**Issue**: When profiles are discovered during mod loading, systems that depend on profiles should be notified via events.

**Existing Pattern**:
```csharp
// ModLoader fires DefinitionDiscoveredEvent when definitions are loaded
var discoveredEvent = new DefinitionDiscoveredEvent
{
    DefinitionType = definitionType,
    DefinitionId = metadata.Id,
    ...
};
EventBus.Send(ref discoveredEvent);
```

**Problem**: Systems that need to react to profile discovery (e.g., pre-validating sprite definitions) have no way to know when profiles are loaded.

**Required Fix**: Add event handling for profile discovery:
```csharp
// In ProfileService or a ProfileValidationSystem
public class ProfileValidationSystem : BaseSystem<World, float>, IDisposable
{
    private readonly List<IDisposable> _subscriptions = new();
    
    public ProfileValidationSystem(World world, IProfileService profileService) : base(world)
    {
        _subscriptions.Add(EventBus.Subscribe<DefinitionDiscoveredEvent>(OnDefinitionDiscovered));
    }
    
    private void OnDefinitionDiscovered(DefinitionDiscoveredEvent evt)
    {
        if (evt.DefinitionType == "MovementProfile" || evt.DefinitionType == "AnimationProfile")
        {
            // Profile discovered - validate it immediately
            ValidateProfile(evt.DefinitionId, evt.DefinitionType);
        }
    }
}
```

**Alternative**: Validate profiles during `ProfileService.LoadProfiles()` (simpler, but less event-driven).

**Recommendation**: Use post-load validation (simpler) but fire events for profile loading completion.

### ⚠️ WARNING: Profile Cache Invalidation on Mod Reload

**Issue**: If mods are hot-reloaded, profile caches need to be invalidated, but the design doesn't address this.

**Existing Pattern**: 
- Mods can be reloaded during development
- DefinitionRegistry supports definition updates (Create/Modify/Extend/Replace)
- But ProfileService caches profiles in dictionaries - these need to be refreshed

**Required Fix**: Add cache invalidation:
```csharp
public class ProfileService : IProfileService, IDisposable
{
    private readonly List<IDisposable> _subscriptions = new();
    
    public ProfileService(IDefinitionRegistry definitionRegistry)
    {
        // Subscribe to definition updates
        _subscriptions.Add(EventBus.Subscribe<DefinitionDiscoveredEvent>(OnDefinitionDiscovered));
    }
    
    private void OnDefinitionDiscovered(DefinitionDiscoveredEvent evt)
    {
        if (evt.DefinitionType == "MovementProfile")
        {
            // Reload movement profile from registry
            ReloadMovementProfile(evt.DefinitionId);
        }
        else if (evt.DefinitionType == "AnimationProfile")
        {
            // Reload animation profile from registry
            ReloadAnimationProfile(evt.DefinitionId);
        }
    }
}
```

### ✅ COMPLIANT: Component Design (GridMovement)

**Status**: ✅ **COMPLIANT**

The design correctly stores `MovementSpeed` in the `GridMovement` component (value type, data-only). This follows ECS patterns correctly:
- Speed is stored in component (not in system)
- Component is a struct (value type)
- System reads from component and updates position

### ⚠️ WARNING: Missing Component for Animation Profile Reference

**Issue**: The design stores `animationProfileId` in `SpriteDefinition`, but entities don't have direct access to their animation profile. Animation durations are pre-calculated and stored in `SpriteAnimationFrame`, but the profile reference is lost.

**Current Approach** (from design):
- Durations are pre-calculated at sprite load time
- Stored in `SpriteAnimationFrame.DurationSeconds`
- Profile reference is lost after pre-calculation

**Problem**: If profiles are hot-reloaded, sprite definitions need to be re-processed, but there's no way to know which sprites use which profiles.

**Required Fix**: Store profile references for debugging/hot-reload:
```csharp
// In SpriteDefinition (already in design, but clarify purpose)
public class SpriteDefinition
{
    [JsonPropertyName("movementProfileId")]
    public string MovementProfileId { get; set; } = string.Empty; // Required
    
    [JsonPropertyName("animationProfileId")]
    public string AnimationProfileId { get; set; } = string.Empty; // Required
    
    // Store profile references for hot-reload invalidation
    // This allows ResourceManager to re-process sprites when profiles change
}
```

**Alternative**: Store inverse mapping in ProfileService (which sprites use each profile) for hot-reload invalidation.

**Recommendation**: Store profile references in `SpriteDefinition` (already in design, just clarify this use case).

---

## 3. Moddability Issues

### ❌ CRITICAL: Missing Profile Override/Extend/Replace Operations

**Issue**: The design doesn't specify how mods can override/extend/replace profiles using the existing `$operation` system.

**Existing Pattern** (from DefinitionOperation):
```json
{
  "id": "base:constants:player",
  "$operation": "Modify",
  "constants": {
    "PlayerMovementSpeed": 6.0  // Override existing value
  }
}
```

**Problem**: Profiles are dictionaries of speeds/animations - how do mods modify individual entries?

**Required Fix**: Support profile operations:
```json
// Mod A: Create base profile
{
  "id": "base:profile:movement/player",
  "definitionType": "MovementProfile",
  "speeds": {
    "walk": 4.0,
    "run": 8.0
  },
  "defaultSpeed": "walk"
}

// Mod B: Modify profile (override walk speed)
{
  "id": "base:profile:movement/player",
  "$operation": "Modify",
  "speeds": {
    "walk": 5.0  // Override existing walk speed
    // run: 8.0 is unchanged (preserved)
  }
}

// Mod C: Extend profile (add bike speed)
{
  "id": "base:profile:movement/player",
  "$operation": "Extend",
  "speeds": {
    "bike": 12.0  // Add new speed type
  }
}
```

**Implementation**: ProfileService must merge operations during `LoadProfiles()`:
```csharp
private void LoadProfiles()
{
    var movementProfiles = _definitionRegistry.GetAll<MovementProfileDefinition>();
    
    // Group by ID (multiple definitions can modify same profile)
    var profileGroups = movementProfiles.GroupBy(p => p.Id);
    
    foreach (var group in profileGroups)
    {
        var mergedProfile = MergeProfileOperations(group);
        _movementProfiles[group.Key] = mergedProfile;
    }
}
```

**Recommendation**: Add profile operation support following existing definition operation patterns.

### ⚠️ WARNING: Profile Validation During Mod Loading

**Issue**: Profile definitions should be validated during mod loading, not just when used.

**Existing Pattern** (ModValidator):
```csharp
// ModValidator validates definitions during mod loading
// Invalid definitions cause validation errors but don't prevent mod loading
// Validation errors are collected and reported
```

**Required Fix**: Add profile validation to `ModValidator` or create `ProfileValidator`:
```csharp
public class ProfileValidator
{
    public ValidationIssue[] ValidateMovementProfile(MovementProfileDefinition profile)
    {
        var issues = new List<ValidationIssue>();
        
        if (string.IsNullOrWhiteSpace(profile.Id))
            issues.Add(new ValidationIssue(ValidationSeverity.Error, "Profile ID is required"));
        
        if (profile.Speeds.Count == 0)
            issues.Add(new ValidationIssue(ValidationSeverity.Error, "Profile must have at least one speed type"));
        
        if (!profile.Speeds.ContainsKey(profile.DefaultSpeed))
            issues.Add(new ValidationIssue(ValidationSeverity.Error, 
                $"DefaultSpeed '{profile.DefaultSpeed}' not found in speeds dictionary"));
        
        // Validate speed bounds
        foreach (var (type, speed) in profile.Speeds)
        {
            if (speed < 0.1f || speed > 100.0f)
                issues.Add(new ValidationIssue(ValidationSeverity.Warning,
                    $"Speed '{type}' ({speed}) is outside recommended range (0.1-100.0)"));
        }
        
        return issues.ToArray();
    }
}
```

### ⚠️ WARNING: Mod Dependencies for Profiles

**Issue**: If Mod B's sprite definitions reference profiles from Mod A, Mod B must depend on Mod A, but the design doesn't enforce this.

**Existing Pattern** (mod.json dependencies):
```json
{
  "id": "my-mod",
  "dependencies": ["base:mod"],
  ...
}
```

**Required Fix**: Add dependency validation:
```csharp
// In ModValidator
public ValidationIssue[] ValidateSpriteProfileDependencies(ModManifest mod)
{
    var issues = new List<ValidationIssue>();
    
    // Check if sprite definitions reference profiles from other mods
    var spriteDefs = GetSpriteDefinitions(mod);
    foreach (var spriteDef in spriteDefs)
    {
        if (!string.IsNullOrWhiteSpace(spriteDef.MovementProfileId))
        {
            var profileMod = GetProfileModId(spriteDef.MovementProfileId);
            if (profileMod != mod.Id && !mod.Dependencies.Contains(profileMod))
            {
                issues.Add(new ValidationIssue(ValidationSeverity.Error,
                    $"Sprite '{spriteDef.Id}' references profile '{spriteDef.MovementProfileId}' from mod '{profileMod}', " +
                    "but mod dependency is missing. Add dependency in mod.json."));
            }
        }
    }
    
    return issues.ToArray();
}
```

**Recommendation**: Add dependency validation to prevent runtime errors.

### ✅ COMPLIANT: Profile Discovery and Loading

**Status**: ✅ **COMPLIANT**

Profiles are loaded via convention-based discovery, matching existing definition patterns. Path-based inference is supported (with explicit `definitionType` field as fallback).

---

## 4. Pokemon-Style Game Issues

### ❌ CRITICAL: Movement Speed vs Animation Speed Synchronization

**Issue**: In Pokemon games, movement speed and animation speed are **synchronized** - faster movement uses faster animations. The design treats them as separate, which can cause desynchronization.

**Current Problem**:
- Movement speed: `4.0 tiles/second` (from movement profile)
- Animation speed: `go` animation with `8 ticks` duration (from animation profile)
- These are independent - changing movement speed doesn't change animation speed

**Pokemon Emerald Pattern**:
- Walk: `4.0 tiles/sec` movement, `go` animation (`8 ticks`)
- Run: `8.0 tiles/sec` movement, `go_fast` animation (`4 ticks`)
- **Movement and animation are linked** - running uses different animation type

**Required Fix**: Link movement types to animation types:
```json
{
  "id": "base:profile:movement/player",
  "speeds": {
    "walk": {
      "speed": 4.0,
      "animationType": "go"  // Use "go" animation for walk movement
    },
    "run": {
      "speed": 8.0,
      "animationType": "go_fast"  // Use "go_fast" animation for run movement
    }
  },
  "defaultSpeed": "walk"
}
```

**Alternative**: Keep separate but add validation:
```csharp
// Validate that movement types have corresponding animation types
if (movementProfile.Speeds.ContainsKey("run"))
{
    if (!animationProfile.Animations.ContainsKey("go_fast"))
    {
        throw new InvalidOperationException(
            "Movement profile has 'run' speed but animation profile missing 'go_fast' animation");
    }
}
```

**Recommendation**: Link movement types to animation types in movement profiles (more explicit, easier to validate).

### ✅ COMPLIANT: Frame Timing with Movement Interpolation (Intentionally Independent)

**Status**: ✅ **COMPLIANT** (but needs documentation)

**Current Design**: 
- Animation durations come from pokeemerald-expansion (authentic timing)
- Movement speed is independent (configurable)
- Animation loops independently during movement

**Pokemon Pattern**:
- Movement and animation are **intentionally not synchronized** (animation loops independently)
- Animation frame durations are chosen for visual feel, not movement speed matching
- Faster movement types use faster animation types (`go_fast` vs `go`), but timing isn't precisely matched
- Example: Walk (`4.0 tiles/sec`) uses `go` animation (`8 ticks`), Run (`8.0 tiles/sec`) uses `go_fast` animation (`4 ticks`)
- Animation cycles complete independently of tile movements - this is **correct behavior**

**Required Documentation**: Add clarification to design document:
```markdown
## Animation and Movement Synchronization

In Pokemon-style games, animation and movement are **intentionally independent**:
- Animation loops continuously during movement (not synchronized to tile boundaries)
- Frame durations are chosen for visual feel based on pokeemerald-expansion patterns
- Faster movement types use faster animation types (go_fast vs go), but timing isn't precisely matched
- This creates natural-looking movement where animation cycles don't align with tile boundaries

**Example**:
- Walk: `4.0 tiles/sec` movement (0.25s per tile) uses `go` animation (0.133s per frame, 0.532s per cycle)
- Run: `8.0 tiles/sec` movement (0.125s per tile) uses `go_fast` animation (0.067s per frame, 0.268s per cycle)
- Animation cycles complete independently - this is **correct Pokemon-style behavior**
```

### ❌ CRITICAL: Missing Movement Type to Animation Type Mapping

**Issue**: The design doesn't specify how the system selects between `go_*` (walk) and `go_fast_*` (run) animations based on movement speed or running state.

**Current Problem**:
- `MovementAnimationHelper.OnMovementInProgress()` uses `ToWalkAnimation()` which always returns `"go_*"` (hard-coded)
- No logic exists to select `"go_fast_*"` when running
- Movement speed is stored in `GridMovement.MovementSpeed`, but there's no mapping from speed → animation type
- Pokemon games need: walk speed → `go_*`, run speed → `go_fast_*` or `run_*`

**Existing Code** (MovementAnimationHelper.cs:65):
```csharp
// Always uses "go_*" - no support for "go_fast_*" or "run_*"
var expectedAnimation = movement.FacingDirection.ToWalkAnimation(); // Returns "go_south"
```

**Pokemon Emerald Pattern**:
- **Walk**: `4.0 tiles/sec` → `go_*` animation (8 ticks)
- **Run**: `8.0 tiles/sec` → `go_fast_*` animation (4 ticks) OR `run_*` animation (custom frame sequence)
- **Bike**: `12.0 tiles/sec` → `go_fastest_*` animation (1-2 ticks) OR custom bike animation

**Required Fix**: Update movement profile structure to link speeds to animation types:
```json
{
  "id": "base:profile:movement/player",
  "speeds": {
    "walk": {
      "speed": 4.0,
      "animationType": "go"  // Use "go" animation type
    },
    "run": {
      "speed": 8.0,
      "animationType": "go_fast"  // Use "go_fast" animation type
    },
    "bike": {
      "speed": 12.0,
      "animationType": "go_fastest"  // Use "go_fastest" animation type
    }
  },
  "defaultSpeed": "walk"
}
```

**Update MovementAnimationHelper**:
```csharp
public static class MovementAnimationHelper
{
    private readonly IProfileService _profileService;
    private readonly IDefinitionRegistry _definitionRegistry;
    
    // Determine animation type from current movement speed
    private string GetAnimationTypeFromSpeed(float currentSpeed, string movementProfileId)
    {
        var profile = _profileService.GetMovementProfile(movementProfileId);
        
        // Find movement type that matches current speed (within tolerance)
        foreach (var (type, speedDef) in profile.Speeds)
        {
            if (Math.Abs(speedDef.Speed - currentSpeed) < 0.1f)
            {
                return speedDef.AnimationType; // e.g., "go", "go_fast", "run"
            }
        }
        
        // Fallback: use default speed's animation type
        return profile.Speeds[profile.DefaultSpeed].AnimationType;
    }
    
    public void OnMovementInProgress(
        ref SpriteAnimationComponent animation,
        ref GridMovement movement,
        string spriteId  // NEW: Need sprite ID to get movement profile
    )
    {
        // Get sprite definition to find movement profile
        var spriteDef = _definitionRegistry.Get<SpriteDefinition>(spriteId);
        if (string.IsNullOrWhiteSpace(spriteDef?.MovementProfileId))
        {
            throw new InvalidOperationException(
                $"Sprite '{spriteId}' missing MovementProfileId. Cannot determine animation type.");
        }
        
        // Determine animation type from current movement speed
        var animationType = GetAnimationTypeFromSpeed(movement.MovementSpeed, spriteDef.MovementProfileId);
        
        // Build animation name: "{animationType}_{direction}"
        var expectedAnimation = $"{animationType}_{movement.FacingDirection.ToAnimationSuffix()}";
        
        if (animation.CurrentAnimationName != expectedAnimation)
            ChangeAnimation(ref animation, expectedAnimation);
    }
}
```

**Alternative Approach**: Store `CurrentMovementType` in `GridMovement` component (simpler, more explicit):
```csharp
public struct GridMovement
{
    public string CurrentMovementType { get; set; } // "walk", "run", "bike" - set when speed changes
    
    // When MovementSpeed changes, update CurrentMovementType based on profile
    public void UpdateMovementType(float newSpeed, IProfileService profileService, string profileId)
    {
        MovementSpeed = newSpeed;
        CurrentMovementType = profileService.GetMovementTypeForSpeed(profileId, newSpeed);
    }
}
```

**Then MovementAnimationHelper uses CurrentMovementType**:
```csharp
public static void OnMovementInProgress(
    ref SpriteAnimationComponent animation,
    ref GridMovement movement,
    string spriteId,
    IProfileService profileService
)
{
    // Get sprite definition to find animation profile
    var spriteDef = _definitionRegistry.Get<SpriteDefinition>(spriteId);
    if (string.IsNullOrWhiteSpace(spriteDef?.MovementProfileId))
    {
        throw new InvalidOperationException($"Sprite '{spriteId}' missing MovementProfileId.");
    }
    
    // Get animation type from movement profile for current movement type
    var movementProfile = profileService.GetMovementProfile(spriteDef.MovementProfileId);
    var speedDef = movementProfile.Speeds[movement.CurrentMovementType];
    var animationType = speedDef.AnimationType; // e.g., "go", "go_fast", "run"
    
    // Build animation name
    var expectedAnimation = $"{animationType}_{movement.FacingDirection.ToAnimationSuffix()}";
    
    if (animation.CurrentAnimationName != expectedAnimation)
        ChangeAnimation(ref animation, expectedAnimation);
}
```

**Recommendation**: Use **Alternative Approach** (CurrentMovementType in component) because:
1. Simpler: No need to look up speed → type mapping during movement
2. More explicit: Movement type is stored in component (matches Pokemon game state)
3. Better performance: No profile lookups in hot path (Update method)
4. Matches existing patterns: Components store data, systems use it

**Important**: `MovementAnimationHelper` is currently a **static class**, but to access sprite definitions it needs access to services (`IDefinitionRegistry`, `IProfileService`). Two options:

**Option A**: Pass services as parameters (keep static):
```csharp
public static void OnMovementInProgress(
    ref SpriteAnimationComponent animation,
    ref GridMovement movement,
    string spriteId,  // From SpriteComponent.SpriteId
    IProfileService profileService,
    IDefinitionRegistry definitionRegistry
)
```

**Option B**: Convert to instance class in MovementSystem:
```csharp
// In MovementSystem
private readonly MovementAnimationHelper _animationHelper;

public MovementSystem(World world, IProfileService profileService, IDefinitionRegistry registry) : base(world)
{
    _animationHelper = new MovementAnimationHelper(profileService, registry);
}

// Then call instance methods
_animationHelper.OnMovementInProgress(ref animation, ref movement, spriteComponent.SpriteId);
```

**Recommendation**: Use **Option A** (keep static, pass services) because:
- Keeps helper class stateless (current design)
- Simpler: No need to convert to instance class
- Services are passed from MovementSystem (which has them injected)
- Matches existing static helper pattern

**Update Required**: Modify `MovementAnimationHelper` method signatures to accept services:
```csharp
public static void OnMovementInProgress(
    ref SpriteAnimationComponent animation,
    ref GridMovement movement,
    string spriteId,  // From SpriteComponent.SpriteId (must be passed from MovementSystem)
    IProfileService profileService,
    IDefinitionRegistry definitionRegistry
)
```

**MovementSystem Integration**:
```csharp
// In MovementSystem.ProcessMovementWithAnimation()
if (World.Has<SpriteComponent>(entity))
{
    ref var sprite = ref World.Get<SpriteComponent>(entity);
    MovementAnimationHelper.OnMovementInProgress(
        ref animation,
        ref movement,
        sprite.SpriteId,  // Pass sprite ID to helper
        _profileService,  // Pass service from MovementSystem
        _definitionRegistry  // Pass registry from MovementSystem
    );
}
```

**Update Required**:
1. Update movement profile JSON structure to include `animationType` per speed
2. Add `CurrentMovementType` field to `GridMovement` component
3. Update `MovementSystem` to set `CurrentMovementType` when speed changes
4. Update `MovementAnimationHelper` to use `CurrentMovementType` to determine animation type
5. Update `DirectionExtensions` to support dynamic animation type selection (not just hard-coded "go")

### ❌ CRITICAL: PrecomputeAnimationFrames Integration Breaking Change

**Issue**: `ResourceManager.PrecomputeAnimationFrames()` currently expects `animation.FrameDurations` to be populated from JSON, but the new design removes this field and uses `animationType` instead. This is a **breaking change** that requires updating `PrecomputeAnimationFrames()`.

**Current Code** (ResourceManager.cs:1147-1154):
```csharp
if (animation.FrameIndices == null || animation.FrameDurations == null)
    continue;

for (var i = 0; i < animation.FrameIndices.Count; i++)
{
    var frameIndex = animation.FrameIndices[i];
    var frameDuration = i < animation.FrameDurations.Count ? animation.FrameDurations[i] : 0.0;
    // Uses frameDuration directly from JSON
}
```

**Problem**: 
- Old format: `animation.FrameDurations` exists in JSON, `PrecomputeAnimationFrames()` uses it directly
- New format: `animation.AnimationType` exists, `FrameDurations` must be calculated from profile
- **Result**: `PrecomputeAnimationFrames()` will fail because `animation.FrameDurations == null`

**Required Fix**: Update `PrecomputeAnimationFrames()` to use ProfileService:
```csharp
private void PrecomputeAnimationFrames(string spriteId, SpriteDefinition definition)
{
    if (definition.Animations == null || definition.Frames == null)
        return;

    // Validate required profile references (fail-fast)
    if (string.IsNullOrWhiteSpace(definition.AnimationProfileId))
    {
        throw new InvalidOperationException(
            $"Sprite definition '{definition.Id}' must specify an AnimationProfileId. " +
            "Add 'animationProfileId' field to sprite definition JSON.");
    }

    foreach (var animation in definition.Animations)
    {
        var frameList = new List<SpriteAnimationFrame>();

        if (animation.FrameIndices == null)
            continue;

        // NEW: Validate required animationType (fail-fast)
        if (string.IsNullOrWhiteSpace(animation.AnimationType))
        {
            throw new InvalidOperationException(
                $"Animation '{animation.Name}' in sprite '{definition.Id}' must specify an AnimationType. " +
                "Add 'animationType' field to animation definition JSON.");
        }

        // NEW: Calculate durations from profile (not from JSON)
        double[] frameDurations;
        try
        {
            frameDurations = _profileService.CalculateAnimationDurations(
                definition.AnimationProfileId,
                animation.AnimationType,
                animation.FrameIndices.Count,
                animation.FrameSequence // Optional override from animation definition
            );
        }
        catch (ProfileNotFoundException ex)
        {
            throw new InvalidOperationException(
                $"Failed to calculate animation durations for sprite '{definition.Id}', animation '{animation.Name}'. " +
                $"Animation profile '{definition.AnimationProfileId}' not found.",
                ex);
        }
        catch (KeyNotFoundException ex)
        {
            throw new InvalidOperationException(
                $"Failed to calculate animation durations for sprite '{definition.Id}', animation '{animation.Name}'. " +
                $"Animation type '{animation.AnimationType}' not found in profile '{definition.AnimationProfileId}'.",
                ex);
        }

        // Use calculated durations (same loop structure as before)
        for (var i = 0; i < animation.FrameIndices.Count; i++)
        {
            var frameIndex = animation.FrameIndices[i];
            var frameDuration = i < frameDurations.Length ? frameDurations[i] : 0.0;

            // Find the frame definition
            var frameDef = definition.Frames.FirstOrDefault(f => f.Index == frameIndex);
            if (frameDef != null)
            {
                var animationFrame = new SpriteAnimationFrame
                {
                    SourceRectangle = new Rectangle(
                        frameDef.X,
                        frameDef.Y,
                        frameDef.Width,
                        frameDef.Height
                    ),
                    DurationSeconds = (float)frameDuration,
                    FrameIndex = frameDef.Index,
                };
                frameList.Add(animationFrame);
            }
        }

        if (frameList.Count > 0)
        {
            var key = (spriteId, animation.Name);
            _animationFrameCache[key] = frameList;
        }
    }
}
```

**Required Changes**:
1. Inject `IProfileService` into `ResourceManager` constructor
2. Update `PrecomputeAnimationFrames()` to use `ProfileService.CalculateAnimationDurations()`
3. Remove dependency on `animation.FrameDurations` (no longer in JSON)
4. Add validation for required `animationType` and `animationProfileId` fields

**Backward Compatibility**: **None** - this is a breaking change that requires all sprite definitions to be updated (per NO BACKWARD COMPATIBILITY rule).

### ⚠️ WARNING: ResourceManager Constructor Dependency

**Issue**: `ResourceManager` needs `IProfileService` injected, but it's not currently in the constructor.

**Current Constructor**:
```csharp
public ResourceManager(
    GraphicsDevice graphicsDevice,
    IModManager modManager,
    IResourcePathResolver pathResolver,
    ILogger logger,
    IVariableSpriteResolver? variableSpriteResolver = null
)
```

**Required Fix**: Add `IProfileService` parameter:
```csharp
public ResourceManager(
    GraphicsDevice graphicsDevice,
    IModManager modManager,
    IResourcePathResolver pathResolver,
    ILogger logger,
    IProfileService profileService,  // NEW: Required for animation duration calculation
    IVariableSpriteResolver? variableSpriteResolver = null
)
```

**Update GameServices**: Add ProfileService initialization before ResourceManager:
```csharp
// In GameServices.InitializeServices()
var profileService = new ProfileService(modManager.Registry);
_game.Services.AddService<IProfileService>(profileService);

var resourceManager = new ResourceManager(
    _graphicsDevice,
    modManager,
    resourcePathResolver,
    _logger,
    profileService,  // NEW: Inject ProfileService
    variableSpriteResolver
);
```

### ⚠️ WARNING: Turn-in-Place Animation Profile Dependency

**Issue**: Turn-in-place animations currently use hard-coded `go_fast_*` animation type, but this should be clarified in the design as a special case.

**Current Code** (Direction.cs:107-109):
```csharp
public static string ToTurnAnimation(this Direction direction)
{
    return $"go_fast_{direction.ToAnimationSuffix()}"; // HARD-CODED - matches Pokemon Emerald
}
```

**Pokemon Emerald Pattern**:
- Turn-in-place uses `WALK_IN_PLACE_FAST` which uses `ANIM_STD_GO_FAST_*` (8 frames at 60fps)
- This is standardized across all characters (always uses `go_fast` animation type)
- Not configurable - always the same regardless of movement type

**Design Decision**:
- **Keep turn animation hard-coded** as `go_fast_*` (matches Pokemon Emerald exactly)
- This is a special case, not a regular movement type
- Document in design that turn-in-place always uses `go_fast` animation type
- No profile lookup needed for turn-in-place (simpler, better performance)

**Required Documentation**: Add clarification to design:
```markdown
## Turn-in-Place Animation

Turn-in-place animations are a **special case** that always use the `go_fast_*` animation type:
- Turn-in-place: Always uses `go_fast_{direction}` animation (e.g., "go_fast_south")
- This matches Pokemon Emerald's `WALK_IN_PLACE_FAST` behavior
- Not configurable via profiles - standardized across all characters
- Played with `PlayOnce=true` to detect turn completion
```

### ✅ COMPLIANT: Animation Naming Conventions

**Status**: ✅ **COMPLIANT**

The design correctly follows pokeemerald-expansion animation naming:
- `face_south`, `face_north`, `face_west`, `face_east`
- `go_south`, `go_north`, `go_west`, `go_east`
- `go_fast_south`, `go_fast_north`, etc. (also used for turn-in-place)
- `run_south`, `run_north`, etc.

This matches existing `DirectionExtensions.ToWalkAnimation()`, `ToIdleAnimation()`, and `ToTurnAnimation()` methods.

---

## Summary of Required Fixes

### Critical Issues (Must Fix - Blocking Implementation)

1. **PrecomputeAnimationFrames Integration** ⚠️ **BREAKING CHANGE**
   - Update `ResourceManager.PrecomputeAnimationFrames()` to use `ProfileService.CalculateAnimationDurations()`
   - Remove dependency on `animation.FrameDurations` from JSON (no longer exists)
   - Add validation for required `animationType` and `animationProfileId` fields
   - **Impact**: All sprite definitions must be updated, old format removed

2. **ResourceManager Constructor Dependency**
   - Add `IProfileService` parameter to `ResourceManager` constructor (required, not optional)
   - Update `GameServices` to initialize `ProfileService` before `ResourceManager`
   - **Impact**: Changes initialization order, affects all systems using ResourceManager

3. **Movement Type to Animation Type Mapping** ⚠️ **MISSING FUNCTIONALITY**
   - Update movement profile structure to include `animationType` for each speed type
   - Add `CurrentMovementType` field to `GridMovement` component (stores "walk", "run", "bike")
   - Update `MovementAnimationHelper` to use `CurrentMovementType` to determine animation type
   - **Impact**: Enables walk/run/bike animation selection (currently always uses "go")

4. **Profile Loading Order & Validation**
   - Validate profile references after mod loading completes (before sprite usage)
   - Fail-fast if sprite definitions reference non-existent profiles
   - **Impact**: Prevents runtime errors from invalid profile references

5. **Profile Service Initialization Timing**
   - Specify initialization order: ModManager → ProfileService → ResourceManager → ECS Systems
   - Document in `GameServices.InitializeServices()` method
   - **Impact**: Ensures profiles are available when sprites are loaded

### High Priority (Should Fix - Core Functionality)

6. **Definition Type Inference for Profiles**
   - Add path patterns to `KnownPathMappings.cs` for profile discovery:
     - `"Definitions/Profiles/Movement"` → `"MovementProfile"`
     - `"Definitions/Profiles/Animation"` → `"AnimationProfile"`
   - **Impact**: Enables convention-based discovery of profiles

7. **Profile Definition Classes Missing**
   - Define `MovementProfileDefinition` class matching JSON schema
   - Define `AnimationProfileDefinition` class matching JSON schema
   - Define nested `SpeedDefinition` class with `speed` and `animationType` fields
   - Define nested `AnimationDefinition` class for animation profile entries
   - **Impact**: Enables type-safe deserialization of profile definitions

8. **SpriteDefinition Structure Updates**
   - Add required `movementProfileId` field (non-nullable string)
   - Add required `animationProfileId` field (non-nullable string)
   - Add validation to fail-fast if fields are missing
   - **Impact**: Breaking change - all sprite definitions must be updated

9. **SpriteAnimation Structure Updates**
   - Add required `animationType` field (non-nullable string)
   - Remove `frameDurations` field (replaced by profile-based calculation)
   - Add optional `frameSequence` field (overrides profile frame sequence)
   - **Impact**: Breaking change - all animation definitions must be updated

10. **Profile Override Operations**
    - Support `$operation: "Modify"` for overriding individual speed values
    - Support `$operation: "Extend"` for adding new speed types
    - Support `$operation: "Replace"` for replacing entire profile
    - Merge operations during `ProfileService.LoadProfiles()`
    - **Impact**: Enables mods to customize profiles without replacing entire profile

### Medium Priority (Consider Fixing - Quality of Life)

11. **Missing Event for Profile Discovery**
    - Add event handling for `DefinitionDiscoveredEvent` with profile types
    - Or use post-load validation approach (simpler, recommended)
    - **Impact**: Better integration with event-driven systems

12. **Profile Validation During Mod Loading**
    - Add `ProfileValidator` class with validation methods
    - Integrate with `ModValidator` to validate profiles during mod loading
    - Validate profile structure, speed bounds, animation type existence
    - **Impact**: Catch profile errors early (during mod loading, not runtime)

13. **Mod Dependencies for Profile References**
    - Validate that sprite definitions don't reference profiles from other mods without dependencies
    - Add validation in `ModValidator` to check profile dependencies
    - **Impact**: Prevents runtime errors from missing profile dependencies

14. **Profile Cache Invalidation (Hot-Reload)**
    - Add event handling for profile updates during development
    - Invalidate sprite caches when profiles change
    - Re-process sprite definitions that use changed profiles
    - **Impact**: Better developer experience during mod development

15. **Frame Timing Documentation**
    - Document that animation and movement are intentionally independent
    - Clarify that animation loops don't align with tile boundaries
    - Explain Pokemon-style animation behavior
    - **Impact**: Prevents confusion about animation/movement synchronization

16. **Turn-in-Place Documentation**
    - Document that turn-in-place always uses `go_fast_*` animation type
    - Clarify this is a special case, not configurable via profiles
    - **Impact**: Prevents confusion about turn animation configuration

---

## Recommended Next Steps

### Phase 1: Core Infrastructure (Week 1)
1. **Define Profile Classes**: Create `MovementProfileDefinition` and `AnimationProfileDefinition` classes matching JSON schema
2. **Add Path Inference**: Add profile path patterns to `KnownPathMappings.cs` for convention-based discovery
3. **Create ProfileService**: Implement `IProfileService` and `ProfileService` with fail-fast validation
4. **Update ResourceManager**: Add `IProfileService` dependency and update `PrecomputeAnimationFrames()` to use profiles
5. **Update GameServices**: Add ProfileService initialization before ResourceManager

### Phase 2: Data Structure Updates (Week 2)
6. **Update SpriteDefinition**: Add required `movementProfileId` and `animationProfileId` fields
7. **Update SpriteAnimation**: Add required `animationType` field, remove `frameDurations` field (breaking change)
8. **Link Movement/Animation**: Update movement profile structure to include animation types for each movement type
9. **Update GridMovement**: Add `CurrentMovementType` field to track current movement type (walk/run/bike)

### Phase 3: Profile Operations and Validation (Week 3)
10. **Add Profile Validation**: Create `ProfileValidator` and integrate with mod loading validation
11. **Support Profile Operations**: Add Modify/Extend/Replace operation support for profiles (merge operations)
12. **Mod Dependencies**: Add validation for profile dependencies between mods
13. **Create Default Profiles**: Add default movement and animation profiles to `Mods/core/Definitions/Profiles/`

### Phase 4: System Integration (Week 4)
14. **Update PlayerSystem**: Use movement profiles instead of constants (get from sprite definition)
15. **Update MapLoaderSystem**: Use movement profiles for NPCs (get from sprite definition)
16. **Update MovementSystem**: Inject `IProfileService` and `IDefinitionRegistry`, pass to `MovementAnimationHelper`
17. **Update MovementAnimationHelper**: Add service parameters, use movement type → animation type mapping from profiles
18. **Update Porycon3**: Update `SpriteExtractor` to generate animations with `animationType` instead of `frameDurations`

### Phase 5: Testing and Cleanup (Week 5)
18. **Validation Tests**: Test fail-fast behavior for missing profiles/invalid references
19. **Integration Tests**: Test sprite loading with profiles, animation playback, movement speeds
20. **Update Documentation**: Document animation/movement independence, turn-in-place behavior, and profile system
21. **Remove Hard-Coded Values**: Remove all hard-coded movement speeds and animation durations from codebase
22. **Test Movement Type Switching**: Verify walk/run/bike animation selection works correctly
23. **Test Profile Operations**: Verify Modify/Extend/Replace operations work correctly

## Additional Recommendations

### Consider Adding Profile Events
- **ProfileLoadedEvent**: Fire when profile is successfully loaded (for systems that need to react)
- **ProfileValidationFailedEvent**: Fire when profile validation fails (for error reporting)

### Consider Profile Hot-Reload Support
- During development, allow hot-reloading profiles
- Invalidate sprite caches when profiles change
- Re-process sprite definitions that use changed profiles

### Consider Profile Visualization Tools
- Debug panel showing all loaded profiles
- Validation issues panel showing profile errors
- Profile dependency graph (which sprites use which profiles)

---

## Quick Reference: Critical Architectural Decisions

### Movement Profile Structure (REQUIRED UPDATE)

**Current Design** (too simple):
```json
{
  "speeds": {
    "walk": 4.0,
    "run": 8.0
  }
}
```

**Required Structure** (with animation type mapping):
```json
{
  "speeds": {
    "walk": {
      "speed": 4.0,
      "animationType": "go"
    },
    "run": {
      "speed": 8.0,
      "animationType": "go_fast"
    },
    "bike": {
      "speed": 12.0,
      "animationType": "go_fastest"
    }
  },
  "defaultSpeed": "walk"
}
```

**Reason**: Movement types must map to animation types for Pokemon-style walk/run/bike selection.

### GridMovement Component Update (REQUIRED)

**Add to GridMovement struct**:
```csharp
public struct GridMovement
{
    // ... existing fields ...
    
    /// <summary>
    /// Gets or sets the current movement type (e.g., "walk", "run", "bike").
    /// Used to determine which animation type to use during movement.
    /// </summary>
    public string CurrentMovementType { get; set; }
}
```

**Reason**: Systems need to know current movement type to select correct animation (go vs go_fast vs run).

### PrecomputeAnimationFrames Integration (REQUIRED BREAKING CHANGE)

**Current Code** (won't work with new design):
```csharp
if (animation.FrameIndices == null || animation.FrameDurations == null)
    continue; // FrameDurations no longer exists in JSON!
```

**Required Code**:
```csharp
if (animation.FrameIndices == null || string.IsNullOrWhiteSpace(animation.AnimationType))
    throw new InvalidOperationException("Animation missing AnimationType");

var durations = _profileService.CalculateAnimationDurations(
    definition.AnimationProfileId,
    animation.AnimationType,
    animation.FrameIndices.Count,
    animation.FrameSequence
);
```

**Reason**: Durations come from profiles now, not JSON. This is a breaking change.

### MovementAnimationHelper Update (REQUIRED)

**Current Code** (always uses "go"):
```csharp
var expectedAnimation = movement.FacingDirection.ToWalkAnimation(); // Always "go_south"
```

**Required Code** (uses movement type):
```csharp
// Get animation type from movement profile based on CurrentMovementType
var movementProfile = _profileService.GetMovementProfile(spriteDef.MovementProfileId);
var speedDef = movementProfile.Speeds[movement.CurrentMovementType];
var animationType = speedDef.AnimationType; // "go", "go_fast", "run"

var expectedAnimation = $"{animationType}_{movement.FacingDirection.ToAnimationSuffix()}";
```

**Reason**: Enables walk/run/bike animation selection (currently missing functionality).

---

## Conclusion

This analysis identifies **16 architectural issues** across four categories:
- **5 Critical Issues**: Must fix before implementation (blocking issues)
- **5 High Priority Issues**: Should fix for core functionality (design improvements)
- **6 Medium Priority Issues**: Consider for quality of life (optional enhancements)

The design is **architecturally sound** but requires several updates for proper integration:
1. Update movement profile structure to include animation types
2. Add `CurrentMovementType` to `GridMovement` component
3. Update `PrecomputeAnimationFrames()` to use profiles (breaking change)
4. Add profile definition classes and path inference
5. Update systems to use movement type → animation type mapping

The most critical missing piece is the **movement type to animation type mapping**, which is required for Pokemon-style walk/run/bike animation selection. The design currently doesn't specify how systems select between `go_*`, `go_fast_*`, and `run_*` animations based on movement speed.
