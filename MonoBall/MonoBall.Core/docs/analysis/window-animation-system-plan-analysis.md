# Window Animation System Plan Analysis

## Overview

Analysis of the implementation plan against the design document to identify issues, missing details, and
inconsistencies.

---

## Issues Found

### 1. MapPopupRendererSystem Position Calculation - Incorrect Approach

**Location**: Phase 6.2, line 183

**Issue**:

- Plan says: "Update position calculation to use `WindowAnimationComponent.PositionOffset` if present"
- This is incorrect - `WindowRenderer` handles the `PositionOffset` transformation
- `MapPopupRendererSystem` should calculate base position (without animation) and pass `WindowAnimationComponent` to
  `WindowRenderer`

**Current Implementation**:

```csharp
int scaledAnimationY = (int)MathF.Round(anim.CurrentY * currentScale);
int outerY = scaledAnimationY;
```

**Correct Approach**:

- Calculate base position (without animation offset)
- Pass `WindowAnimationComponent` to `WindowRenderer.Render()`
- `WindowRenderer` applies `PositionOffset` to the bounds

**Fix**: Update plan to clarify that `MapPopupRendererSystem` calculates base position, and `WindowRenderer` applies
animation transformations.

---

### 2. MapPopupRendererSystem Query Signature Change Missing

**Location**: Phase 6.2

**Issue**:

- Plan mentions updating query but doesn't specify the lambda signature change
- Current: `(Entity entity, ref MapPopupComponent popup, ref PopupAnimationComponent anim)`
- Should be: `(Entity entity, ref MapPopupComponent popup, ref WindowAnimationComponent anim)`

**Fix**: Add explicit note about query lambda signature change in Phase 6.2.

---

### 3. MapPopupRendererSystem RenderPopup Method Signature Change Missing

**Location**: Phase 6.2

**Issue**:

- Plan mentions updating `RenderPopup()` but doesn't specify parameter type change
- Current:
  `RenderPopup(Entity entity, ref MapPopupComponent popup, ref PopupAnimationComponent anim, CameraComponent camera)`
- Should be:
  `RenderPopup(Entity entity, ref MapPopupComponent popup, ref WindowAnimationComponent anim, CameraComponent camera)`

**Fix**: Add explicit note about method signature change in Phase 6.2.

---

### 4. MapPopupSystem Query Removal Not Specified

**Location**: Phase 6.1, line 167-168

**Issue**:

- Plan says: "Remove `PopupAnimationComponent` from query description"
- But `_popupQuery` is only used in `Update()` method, which will be removed
- The query itself should be removed entirely, not just updated

**Fix**: Change to "Remove `_popupQuery` field entirely (no longer needed after removing Update() method)".

---

### 5. MapPopupSystem Update() Method Removal Clarification Needed

**Location**: Phase 6.1, line 171

**Issue**:

- Plan says: "Remove `Update()` method (animation logic moved to `WindowAnimationSystem`)"
- `MapPopupSystem` inherits from `BaseSystem<World, float>` which may require `Update()` to be implemented
- Need to clarify: remove override entirely, or keep empty override

**Fix**: Add note: "Remove `Update()` override entirely (or keep empty override if BaseSystem requires it - check
BaseSystem implementation)".

---

### 6. Missing EventBus Using Statement Specification

**Location**: Phase 3.1, line 141

**Issue**:

- Plan says: "Add required using statements"
- Doesn't specify that `WindowAnimationSystem` needs `using MonoBall.Core.ECS;` for `EventBus`
- Design document shows this is required (line 305)

**Fix**: Add explicit note: "Add `using MonoBall.Core.ECS;` for EventBus access".

---

### 7. WindowRenderer Opacity Limitation Not Documented

**Location**: Phase 5.1

**Issue**:

- Plan mentions opacity transformations but doesn't note the limitation
- Design document states: "Current renderers don't support opacity - this is a future enhancement"
- Plan should document this known limitation

**Fix**: Add note: "Note: Opacity transformations are calculated but not applied (renderer interfaces don't support
opacity yet - future enhancement)".

---

### 8. MapPopupRendererSystem CurrentY Removal Not Detailed

**Location**: Phase 6.2, line 182

**Issue**:

- Plan says: "Remove `anim.CurrentY` usage"
- Doesn't specify what replaces it
- Current code uses `anim.CurrentY` to calculate `outerY`
- After migration, base position should be calculated without animation offset

**Fix**: Add detail: "Remove `anim.CurrentY` usage - calculate base position without animation offset, let
WindowRenderer apply PositionOffset".

---

### 9. Missing: WindowAnimationComponent WindowEntity Validation

**Location**: Phase 6.2

**Issue**:

- Design document states: "WindowEntity should be validated (World.IsAlive) before use"
- Plan doesn't mention validating `WindowEntity` in `MapPopupRendererSystem` before using animation component

**Fix**: Add note: "Validate `WindowAnimationComponent.WindowEntity` is alive before using animation component (optional
safety check)".

---

### 10. Missing: MapPopupSystem Event Subscription Pattern

**Location**: Phase 6.1, line 169

**Issue**:

- Plan says: "Subscribe to events in constructor"
- Doesn't specify the subscription pattern (RefAction vs Action)
- Design shows events are structs, so likely need
  `EventBus.Subscribe<WindowAnimationCompletedEvent>(OnWindowAnimationCompleted)` with ref action

**Fix**: Add example: "Subscribe using `EventBus.Subscribe<WindowAnimationCompletedEvent>(OnWindowAnimationCompleted)`
pattern (check EventBus API for ref action support)".

---

### 11. Missing: WindowAnimationHelper Dependency on WindowAnimationConfig

**Location**: Phase 4.1, dependencies

**Issue**:

- `create-helper` todo depends on `create-config-struct`
- But `WindowAnimationHelper` methods return `WindowAnimationConfig`, so it also needs `WindowAnimationPhase` and enums
- Dependencies should include `create-phase-struct` and `create-enums` as well

**Fix**: Update `create-helper` dependencies to include `create-phase-struct` and `create-enums`.

---

### 12. Missing: WindowRenderer Animation Parameter Type

**Location**: Phase 5.1, line 157

**Issue**:

- Plan says: "Add optional `WindowAnimationComponent?` parameter"
- Should specify it's a nullable struct parameter: `WindowAnimationComponent? animation = null`

**Fix**: Add explicit parameter signature: "Add optional nullable parameter:
`WindowAnimationComponent? animation = null`".

---

## Minor Issues / Clarifications

### 13. System Registration Location Unclear

**Location**: Phase 7.1

**Issue**:

- Plan says: "`MonoBall.Core/ECS/SystemManager.cs` (or wherever systems are registered)"
- Should verify actual location

**Fix**: Verify system registration location before implementation.

---

### 14. Missing: AnimationEvent Internal Struct Implementation Details

**Location**: Phase 3.1

**Issue**:

- Design shows `AnimationEvent` internal struct with `Fire()` method
- Plan mentions event collection but doesn't detail the internal struct implementation
- Should note that this is an internal helper, not a public API

**Fix**: Add note: "Implement internal `AnimationEvent` struct with `Fire()` method for deferred event firing (internal
helper, not public API)".

---

## Summary

### Critical Issues (Must Fix)

1. MapPopupRendererSystem position calculation approach (Issue #1)
2. Query and method signature changes not specified (Issues #2, #3)
3. MapPopupSystem query removal (Issue #4)

### Important Issues (Should Fix)

4. Update() method removal clarification (Issue #5)
5. EventBus using statement (Issue #6)
6. WindowRenderer opacity limitation (Issue #7)
7. CurrentY removal details (Issue #8)

### Minor Issues (Nice to Have)

9. WindowEntity validation (Issue #9)
10. Event subscription pattern (Issue #10)
11. Helper dependencies (Issue #11)
12. Parameter type specification (Issue #12)
13. System registration location (Issue #13)
14. AnimationEvent struct details (Issue #14)

---

## Recommendations

1. **Update Phase 6.2** to clarify that `MapPopupRendererSystem` calculates base position and `WindowRenderer` applies
   animation transformations.

2. **Add explicit signature changes** for query lambdas and method parameters in Phase 6.2.

3. **Clarify Update() removal** - check if BaseSystem requires Update() override or if it can be removed entirely.

4. **Add EventBus using statement** specification in Phase 3.1.

5. **Document opacity limitation** in Phase 5.1.

6. **Update helper dependencies** to include all required types.

7. **Add event subscription example** in Phase 6.1.

