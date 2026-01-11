# Implementation Plan Analysis: Animation and Movement Profile System

This document analyzes the implementation plan against the design document to identify issues, discrepancies, and missing elements.

## Analysis Summary

**Total Issues Found**: 18 issues (7 Critical, 3 Medium, 8 Low Priority/Clarifications)

## Critical Issues

### 1. Service Initialization Order Error

**Issue**: Plan states ProfileService should be created "After ConstantsService creation (line ~386), before ResourceManager (line ~356)". This is **backwards** - ResourceManager is created at line 356, ConstantsService at line 386.

**Correct Order** (from MonoBallGame.cs LoadModsSynchronously):
1. ModManager (line ~330)
2. ProfileService (should be created **before** ResourceManager, around line ~355)
3. ResourceManager (line ~356)
4. ShaderService (line ~366)
5. ConstantsService (line ~386)

**Fix Required**: Update Phase 1.5 to create ProfileService **before** ResourceManager (around line 355), not after ConstantsService.

### 2. Missing InputSystem Integration

**Issue**: Design document shows InputSystem handling run key presses to update movement speed and CurrentMovementType (design doc lines 2184-2202), but this is **completely missing** from the plan.

**Design Requirement**:
- InputSystem needs IProfileService and IResourceManager injected
- When run button is pressed, InputSystem should:
  - Get sprite definition from ResourceManager
  - Get run speed from ProfileService
  - Update GridMovement.MovementSpeed and CurrentMovementType

**Fix Required**: Add Phase 2.8 - Update InputSystem to handle run button presses and update movement speed/type from profiles.

### 3. Multiple ResourceManager Creation Sites Not Addressed

**Issue**: ResourceManager is created in **TWO locations**:
- `MonoBallGame.LoadModsSynchronously()` (line ~356)
- `GameServices.LoadMods()` (line ~170)

**Plan Only Mentions**: MonoBallGame.cs

**Fix Required**: Update Phase 1.5 to include both locations. Also need to handle the case where ResourceManager already exists (GameServices checks for existing ResourceManager).

### 4. ProfileService IDisposable Missing

**Issue**: Design document shows ProfileService should implement IDisposable for hot-reload support (subscribing to DefinitionDiscoveredEvent, design doc lines 2215-2256), but this is **not in the plan**.

**Design Requirement**:
- ProfileService should subscribe to DefinitionDiscoveredEvent in constructor
- Implement IDisposable pattern with protected Dispose(bool disposing)
- Unsubscribe from events in Dispose()

**Fix Required**: Add to Phase 1.2 - ProfileService must implement IDisposable with event subscription cleanup.

### 5. Post-Load Validation Timing Issue

**Issue**: Plan puts validation in Phase 5, but ProfileService should validate profiles during initialization (Phase 1), and cross-profile validation should happen after ProfileService is created but **before** ResourceManager loads sprites.

**Design Requirement**:
- ProfileService.ValidateLoadedProfiles() called during LoadProfiles() (Phase 1)
- Cross-profile validation (sprite references) should happen after ProfileService initialization, before ResourceManager starts loading (Phase 1 or early Phase 2)

**Fix Required**: 
- Move profile structure validation to Phase 1 (during ProfileService initialization)
- Add cross-profile validation after ProfileService creation, before ResourceManager loads sprites (Phase 1.5 or Phase 2.0)

### 6. Missing IProfileService Methods in Plan

**Issue**: Plan's "Key Methods Required" list is missing some methods from design document's IProfileService interface (lines 790-877):

**Methods in Design but Missing from Plan**:
- `HasMovementProfile(profileId)` - bool check (line 849)
- `HasAnimationProfile(profileId)` - bool check (line 856)
- `GetMovementProfile(profileId)` - Returns full MovementProfileDefinition (line 866)
- `GetAnimationProfile(profileId)` - Returns full AnimationProfileDefinition (line 876)

**Methods in Plan**: All other methods (GetMovementSpeed, GetAnimationTypeForMovementType, GetMovementTypeForSpeed, GetDefaultMovementSpeed, CalculateAnimationDurations) are correctly listed.

**Note**: There is no `GetDefaultMovementType()` method in design document - the default movement type is retrieved via `GetDefaultMovementSpeed()` which uses the profile's `defaultSpeed` field, then you can use `GetMovementTypeForSpeed()` to determine type from speed.

**Fix Required**: Update Phase 1.2 "Key Methods Required" to include HasMovementProfile, HasAnimationProfile, GetMovementProfile, GetAnimationProfile.

### 7. Missing ProfileService Logger Dependency

**Issue**: Plan doesn't mention ILogger parameter for ProfileService, but design document shows ProfileService uses `_logger` in implementation (line 921) even though constructor signature (line 902) doesn't include ILogger parameter. This is a **design document error** that needs fixing.

**Design Example** (line ~902-905, 921):
```csharp
public ProfileService(IModManager modManager)  // Missing ILogger parameter
{
    _modManager = modManager ?? throw new ArgumentNullException(nameof(modManager));
    LoadProfiles();
}
// But implementation uses _logger (line 921):
_logger.Warning("Failed to load movement profile '{ProfileId}'...", profileId);  // _logger not defined!
```

**Fix Required**: 
- Add ILogger parameter to ProfileService constructor in Phase 1.2
- Fix design document to include ILogger in constructor signature (follow ConstantsService pattern)
- Add `private readonly ILogger _logger;` field to ProfileService class

### 8. ProfileServiceFactory Pattern Not Addressed

**Issue**: ConstantsService uses factory pattern (`ConstantsServiceFactory.GetOrCreateConstantsService`), but plan doesn't mention whether ProfileService should use a factory pattern.

**Options**:
- Follow ConstantsService pattern (create ProfileServiceFactory)
- Use direct instantiation (simpler, no reuse needed)

**Recommendation**: Direct instantiation is acceptable since ProfileService doesn't need reuse logic like ConstantsService (which can be reused). But should be explicitly addressed.

**Fix Required**: Explicitly state in Phase 1.2 whether ProfileService uses factory or direct instantiation.

### 9. ValidationIssue Class Already Exists ✓

**Status**: Verified - `ValidationIssue` and `ValidationSeverity` already exist in `MonoBall.Core.Mods` namespace.

**Existing Classes**:
- `MonoBall.Core.Mods.ValidationIssue` (has Severity, Message, ModId, FilePath properties)
- `MonoBall.Core.Mods.ValidationSeverity` (enum: Info, Warning, Error)

**Fix Required**: 
- Use existing `ValidationIssue` class from `MonoBall.Core.Mods` namespace in ProfileValidator
- No need to create new ValidationIssue class
- Update Phase 5.1 to reference existing ValidationIssue class

### 10. PrecomputeAnimationFrames Called from GetSpriteDefinition

**Issue**: Plan doesn't account for the fact that `PrecomputeAnimationFrames()` is called **from within** `GetSpriteDefinition()` (ResourceManager.cs line 406), not separately. This means validation and profile usage happens during sprite definition loading, not at a separate step.

**Current Flow**:
```
GetSpriteDefinition() called
  → Loads sprite definition from registry
  → Caches definition
  → Calls PrecomputeAnimationFrames()  ← ProfileService needed HERE
```

**Implication**: ProfileService must be available **before any sprite definitions are loaded**, which reinforces the initialization order requirement.

**Fix Required**: Note in Phase 2.3 that PrecomputeAnimationFrames is called from GetSpriteDefinition, so ProfileService must be available before any sprite loading occurs.

## Medium Priority Issues

### 11. SpriteDefinition.frameSequence Type Mismatch

**Issue**: Plan says `frameSequence` should be `double[]` (seconds), but needs to verify SpriteDefinition JSON deserialization supports this.

**Design Requirement**: frameSequence should be `double[]` in seconds (design doc line 732 shows `double[]`)

**Fix Required**: Verify in Phase 2.2 that SpriteDefinition.SpriteAnimation.frameSequence is `double[]`, not `int[]`.

### 12. Missing MovementAnimationHelper.OnIdle Update

**Issue**: Plan updates `OnMovementInProgress()` but doesn't mention other MovementAnimationHelper methods. However, design document only shows OnMovementInProgress() needing updates, so this may be intentional.

**Verification Needed**: Confirm OnIdle() and OnTurnInPlace() don't need changes (design suggests only OnMovementInProgress needs update).

### 13. Cross-Profile Validation Implementation Details

**Issue**: Plan mentions cross-profile validation in Phase 5, but doesn't specify where it happens (ModLoader, ProfileService initialization, or separate validation step).

**Design Requirement**: Validate that movement profile animationType values exist in animation profiles (design doc line 1910, 2007-2028)

**Fix Required**: Clarify in Phase 1 or Phase 2 where cross-profile validation occurs (likely during ProfileService initialization or as post-load validation before ResourceManager starts).

### 14. GameInitializationHelper Integration Not Detailed

**Issue**: Plan mentions updating InitializeEcsSystems() in GameInitializationHelper.cs (Phase 2.8), but doesn't show how SystemManager gets ProfileService dependency.

**Current Code**: GameInitializationHelper.InitializeEcsSystems() gets services from Game.Services (line ~150-178), doesn't create ProfileService.

**Fix Required**: 
- ProfileService must be registered in Game.Services before SystemManager creation
- GameInitializationHelper.InitializeEcsSystems() should get ProfileService from Game.Services and pass to SystemManager constructor
- Update SystemManager constructor signature to accept IProfileService

## Low Priority Issues / Clarifications

### 15. Hot-Reload Support Implementation

**Issue**: Design document shows ProfileService subscribing to DefinitionDiscoveredEvent for hot-reload (design doc lines 2215-2256), but this is marked as "Development Mode" feature. Plan doesn't explicitly mention this.

**Fix Required**: Add to Phase 1.2 or Phase 5 as optional development feature, or explicitly state it's out of scope for initial implementation.

### 16. ProfileServiceFactory Decision Needed

**Issue**: ConstantsService uses factory pattern, but plan doesn't state whether ProfileService should follow same pattern.

**Recommendation**: Direct instantiation is acceptable (simpler, no reuse needed).

**Fix Required**: Explicitly document decision in Phase 1.2.

### 17. ValidationIssue Class Location ✓

**Status**: Verified - `ValidationIssue` already exists in `MonoBall.Core.Mods` namespace.

**Existing Class**: `MonoBall.Core.Mods.ValidationIssue` (already used by `ModValidator`)

**Fix Required**: 
- Use existing `MonoBall.Core.Mods.ValidationIssue` in ProfileValidator (Phase 5.1)
- No new class creation needed

### 18. Missing ILogger in ProfileService Examples (Same as Issue #7)

**Issue**: Same as Critical Issue #7 - design document has discrepancy between constructor signature (no ILogger) and implementation (uses _logger).

**Fix Required**: 
- Fix design document first to include ILogger in ProfileService constructor
- Then add ILogger parameter to plan's Phase 1.2 implementation
- Follow ConstantsService pattern which correctly includes ILogger in constructor

## Summary of Required Fixes

### Critical (Must Fix):
1. Fix service initialization order - ProfileService before ResourceManager, not after ConstantsService
2. Add InputSystem integration for run button handling (new Phase 2.8)
3. Update both ResourceManager creation sites (MonoBallGame.cs and GameServices.cs)
4. Add IDisposable implementation to ProfileService (with event subscription cleanup)
5. Move profile structure validation to Phase 1 (during ProfileService initialization)
6. Add cross-profile validation after ProfileService creation, before ResourceManager loads sprites
7. Add missing IProfileService methods (GetMovementProfile, GetAnimationProfile, HasMovementProfile, HasAnimationProfile, GetDefaultMovementType) - actually these ARE in design, just need to add to plan's method list
8. Add ILogger parameter to ProfileService (and fix design document discrepancy)

### Medium Priority:
8. Verify frameSequence type (double[] vs int[])
9. Clarify cross-profile validation timing and location
10. Detail SystemManager constructor update with ProfileService parameter

### Low Priority / Clarifications:
11. Document hot-reload support decision (Phase 1 vs Phase 5 vs out of scope)
12. Document ProfileServiceFactory decision (direct instantiation vs factory pattern)
13. ~~Verify/create ValidationIssue class~~ ✓ (Already exists - use `MonoBall.Core.Mods.ValidationIssue`)
14. ~~Add ILogger to all ProfileService code examples~~ (Same as Issue #7 - fix design doc first)