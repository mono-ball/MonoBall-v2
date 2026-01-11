# NPC Animation Direction Issue - Analysis

## Problem Statement
NPC behavior scripts move NPCs in different directions, but the animation doesn't change to match the direction.

## Current Implementation Flow

### 1. Script Calls Movement API
Behavior scripts (wander, patrol, etc.) call:
- `Api.Movement.Move(direction)` → Creates `MovementRequest` component
- `Api.Npc.SetFacingDirection(direction)` → Uses `StartTurnInPlace()` for stationary NPCs

### 2. MovementSystem Processing Order
MovementSystem.Update() processes in this order:
1. **ProcessMovementRequests()** - Handles MovementRequest components (line 123)
2. **UpdateMovements()** - Updates existing movements and animations (line 127)

### 3. MovementRequest Processing
When MovementRequest is processed (line 237):
```csharp
movement.StartMovement(startPosition, targetPosition, request.Direction);
```
- `StartMovement()` updates `FacingDirection = direction` (GridMovement.cs line 101) ✅
- Also updates `MovementDirection = direction` (line 102)
- Sets `IsMoving = true`

### 4. Animation Update During Movement
In `ProcessMovementWithAnimation()` (line 383-389):
```csharp
MovementAnimationHelper.OnMovementInProgress(
    ref animation,
    ref movement,
    spriteId,
    _profileService,
    _resourceManager
);
```

`OnMovementInProgress()` (MovementAnimationHelper.cs line 125):
```csharp
var directionSuffix = movement.FacingDirection.ToAnimationSuffix();
var expectedAnimation = $"{animationType}_{directionSuffix}";
```

**This SHOULD work correctly** - uses `FacingDirection` which is updated by `StartMovement()`.

## Root Cause Analysis

### ✅ Verification: StartMovement Updates FacingDirection
**Confirmed**: `GridMovement.StartMovement()` (line 101) DOES update `FacingDirection = direction`.

### ✅ Verification: Animation System Uses FacingDirection
**Confirmed**: `OnMovementInProgress()` (line 125) uses `movement.FacingDirection.ToAnimationSuffix()`.

### Potential Issues

#### Issue 1: Timing - Same Frame Processing
**Flow**:
1. `ProcessMovementRequests()` calls `StartMovement()` → Updates `FacingDirection`
2. `UpdateMovements()` runs **immediately after** in same frame
3. `OnMovementInProgress()` reads `FacingDirection` → Should see updated value

**Verdict**: ✅ **Should work** - `FacingDirection` is updated before animation check.

#### Issue 2: Animation Name Format Mismatch
**Current Code** (line 125-126):
```csharp
var directionSuffix = movement.FacingDirection.ToAnimationSuffix();
var expectedAnimation = $"{animationType}_{directionSuffix}";
```

**Questions**:
- What does `ToAnimationSuffix()` return? (e.g., "south", "north", "east", "west")
- What format are sprite animation names? (e.g., "go_south", "go_north")
- Are they matching correctly?

**Potential Issue**: If animation names use different format (e.g., "go_s" instead of "go_south"), they won't match.

#### Issue 3: Animation Change Check Logic
**Current Code** (line 129):
```csharp
if (animation.CurrentAnimationName != expectedAnimation)
    ChangeAnimation(ref animation, expectedAnimation);
```

**Potential Issue**: 
- If `CurrentAnimationName` format differs from `expectedAnimation` format
- Or if animation is already playing the "wrong" direction and needs to be forced to change

#### Issue 4: First Frame Animation Selection
**Potential Issue**: When movement starts:
- `StartMovement()` updates `FacingDirection`
- Same frame: `OnMovementInProgress()` is called
- But is `CurrentAnimationName` already set to something else?
- Does it properly detect the change?

#### Issue 5: Profile System Integration
**New Addition**: Profile system determines `animationType` from `movement.CurrentMovementType` (line 102).

**Potential Issue**:
- Does `CurrentMovementType` match what's in the profile?
- If `CurrentMovementType` is wrong, `animationType` would be wrong
- But direction suffix should still be correct

## Key Questions to Answer

1. **What does `Direction.ToAnimationSuffix()` return?**
   - Need to verify: Returns "south", "north", "east", "west"?

2. **What format are sprite animation names?**
   - Check actual sprite definition JSON files
   - Format: "{animationType}_{direction}" (e.g., "go_south", "go_north")?

3. **Is `CurrentAnimationName` being set correctly?**
   - Check if animation name matches expected format
   - Debug: Log `expectedAnimation` vs `CurrentAnimationName`

4. **Does animation actually change, or is it just not visible?**
   - Is animation changing but sprite sheet doesn't have directional frames?
   - Or is animation name not changing at all?

## Solution Approach

### Recommended: Debug/Verify First
Before implementing a solution, need to verify:

1. **Log Animation Names**:
   - Add logging in `OnMovementInProgress()` to see:
     - `expectedAnimation` value
     - `CurrentAnimationName` value
     - `FacingDirection` value
     - `directionSuffix` value

2. **Check Sprite Definitions**:
   - Verify sprite JSON files have animations like:
     - "go_south", "go_north", "go_east", "go_west"
     - Or different format?

3. **Check ToAnimationSuffix() Implementation**:
   - Verify it returns the correct suffix format
   - Should match sprite animation naming convention

### Potential Solutions

#### Solution 1: Ensure Animation Name Format Matches
**If format mismatch**:
- Verify `ToAnimationSuffix()` returns format matching sprite definitions
- Update if needed to match actual animation names

#### Solution 2: Force Animation Change
**If animation doesn't detect change**:
- Always update animation when movement starts
- Or check both `FacingDirection` AND `MovementDirection` changed

#### Solution 3: Use MovementDirection for Moving Entities
**If FacingDirection vs MovementDirection confusion**:
- During movement: Use `MovementDirection` (direction actually moving)
- When idle: Use `FacingDirection` (direction entity is facing)
- Update `OnMovementInProgress()` to use `MovementDirection` instead

**However**: Both are updated to same value in `StartMovement()` (line 101-102), so shouldn't matter.

#### Solution 4: Immediate Animation Update on Movement Start
**If timing issue**:
- After `StartMovement()` in `ProcessMovementRequests()`, immediately update animation
- Or ensure animation is checked in same frame with correct direction

**However**: Current implementation should already do this.

## Verification Results

### ✅ Animation Name Format - CORRECT
**Verified**: 
- `ToAnimationSuffix()` returns "south", "north", "west", "east" (Direction.cs line 69-72) ✅
- Sprite definitions use format "go_south", "go_north", etc. ✅
- `OnMovementInProgress()` builds: `"{animationType}_{directionSuffix}"` = "go_south" ✅
- Format matches sprite definition format ✅

### ✅ FacingDirection Update - CORRECT
**Verified**:
- `StartMovement()` updates `FacingDirection = direction` (GridMovement.cs line 101) ✅
- `OnMovementInProgress()` uses `movement.FacingDirection.ToAnimationSuffix()` ✅
- Should work correctly ✅

## Most Likely Issue

Since format matches and FacingDirection is updated correctly, **the issue is likely one of these**:

### Hypothesis 1: Animation Change Detection Not Working
**Issue**: Animation name is built correctly, but animation component doesn't detect change

**Current Code** (line 129):
```csharp
if (animation.CurrentAnimationName != expectedAnimation)
    ChangeAnimation(ref animation, expectedAnimation);
```

**Potential Problem**:
- `CurrentAnimationName` might not be updated correctly
- Or animation is already "go_south" when moving south again (no change detected)
- But when direction changes, it SHOULD detect the change

### Hypothesis 2: Profile Animation Type Mapping Issue
**Issue**: Profile system returns wrong `animationType` from `CurrentMovementType`

**Current Code** (line 102-105):
```csharp
animationType = profileService.GetAnimationTypeForMovementType(
    spriteDef.MovementProfileId,
    movement.CurrentMovementType
);
```

**Potential Problem**:
- If `CurrentMovementType` is wrong (e.g., null, empty, or wrong value)
- Or if profile mapping is wrong
- `animationType` would be wrong, but direction suffix would still be correct
- Result: "wrongtype_south" instead of "go_south" - animation wouldn't exist!

**Most Likely**: ✅ **This is the issue!**

### Hypothesis 3: Movement Type Not Set for NPCs
**Issue**: NPCs might not have `CurrentMovementType` set correctly

**When is CurrentMovementType set?**:
- Player: Set by InputSystem based on input
- NPCs: Should be set from movement profile `DefaultSpeed`
- But is it set when NPC is created?

**If CurrentMovementType is null/empty**:
- Profile lookup would fail or return wrong value
- Animation type would be wrong

### Hypothesis 4: First Frame Timing Issue
**Issue**: Animation checked before movement starts

**Current Flow**:
1. `ProcessMovementRequests()` - Starts movement, updates FacingDirection
2. `UpdateMovements()` - Checks animation

**Potential Problem**:
- If animation check happens in wrong order
- But both happen in same frame, so should be OK

**Less Likely**: ❌ Timing shouldn't be an issue

## Current State Verification

### ✅ CurrentMovementType Initialization - CORRECT
**Verified** (MapLoaderSystem.cs line 1066):
- NPCs created with `GridMovement(movementSpeed, defaultMovementType)`
- `defaultMovementType` comes from profile's `DefaultSpeed` (line 996)
- `CurrentMovementType` is set correctly ✅

### ✅ Animation Name Format - CORRECT
**Verified**:
- `ToAnimationSuffix()` returns "south", "north", "west", "east" (Direction.cs line 69-72) ✅
- Sprite definitions use format "go_south", "go_north", "go_west", "go_east" ✅
- `OnMovementInProgress()` builds: `"{animationType}_{directionSuffix}"` = "go_south" ✅
- Format matches perfectly ✅

### ✅ FacingDirection Update - CORRECT
**Verified**:
- `StartMovement()` updates `FacingDirection = direction` (GridMovement.cs line 101) ✅
- `OnMovementInProgress()` uses `movement.FacingDirection.ToAnimationSuffix()` ✅
- Should work correctly ✅

### ✅ MovementSystem Processing - CORRECT
**Verified**:
- `ProcessMovementRequests()` updates `FacingDirection` via `StartMovement()`
- `UpdateMovements()` runs after in same frame
- `OnMovementInProgress()` should see updated `FacingDirection`
- Timing is correct ✅

## Conclusion: Code Appears Correct

**All code paths appear correct**:
1. ✅ `FacingDirection` is updated when movement starts
2. ✅ Animation helper uses `FacingDirection` correctly
3. ✅ Animation name format matches sprite definitions
4. ✅ `CurrentMovementType` is initialized from profile

## Remaining Questions

Given that code appears correct, need to understand the **actual observed behavior**:

1. **What exactly is happening?**
   - Animation doesn't change at all when direction changes?
   - Animation changes but shows wrong direction (e.g., shows south when moving north)?
   - Animation stays as first direction and never updates?

2. **Profile System Verification**
   - Does `GetAnimationTypeForMovementType()` return correct animation type?
   - Does profile map "walk" -> "go" correctly?
   - What does `CurrentMovementType` actually contain during movement?

3. **Debug Verification Needed**
   - Add logging to see:
     - `movement.FacingDirection` value when movement starts
     - `animationType` value from profile
     - `expectedAnimation` value built
     - `CurrentAnimationName` value before/after change
     - Does `ChangeAnimation()` actually get called?

## Recommended Next Steps

1. **Add Debug Logging** to verify actual values:
   - Log in `OnMovementInProgress()`: FacingDirection, animationType, expectedAnimation
   - Log in `StartMovement()`: direction being set
   - Log in `ChangeAnimation()`: animation name being set

2. **Verify Profile Mapping**:
   - Check movement profile JSON files
   - Verify DefaultSpeed value
   - Verify animation type mapping (e.g., "walk" -> "go")

3. **Test Simple Case**:
   - Single NPC with wander script
   - Log when it moves north/south/east/west
   - Verify animation names match expectations

4. **Check SpriteAnimationSystem**:
   - Does it properly resolve animation names to actual animations?
   - Are animation names case-sensitive?
   - Does animation lookup fail silently?

## Hypothesis: Profile Mapping Issue

**Most Likely**: Profile system returns wrong `animationType`, but direction is correct.

**Example**:
- `CurrentMovementType` = "walk"
- Profile should map "walk" -> "go"
- But if profile returns wrong value (e.g., "walk" instead of "go")
- Result: "walk_south" animation doesn't exist, so animation fails silently or falls back
- Direction suffix is correct, but animation type is wrong

**Next**: Verify profile system's `GetAnimationTypeForMovementType()` implementation and profile JSON structure.
