# Input/Movement/Animation Bug Analysis

## Comparison: Old MonoBall vs Current Implementation

### Bug 1: Input Buffering Lost During Turn-in-Place

**Old Implementation (oldmonoball):**
- When `RunningState == TurnDirection`, input buffering is skipped (line 143: `else if (movement.RunningState != RunningState.TurnDirection)`)
- BUT: Input consumption still happens (lines 185-218) - can consume previously buffered input
- Action button checking still happens (lines 179-183)

**Current Implementation:**
- When `RunningState == TurnDirection`, early return (line 133) skips ALL input processing
- **BUG**: Input buffering, action button checking, and buffered input consumption are all skipped
- **Impact**: If player presses direction during turn, input is completely lost

**Fix**: Remove early return, only skip buffering (like old implementation)

---

### Bug 2: RunningState Set to NotMoving Too Early

**Old Implementation:**
- When movement completes, checks for next movement request BEFORE switching to idle animation (line 242)
- Only switches to idle if `!hasNextMovement`
- RunningState stays as Moving until next movement starts or idle animation is set

**Current Implementation:**
- MovementSystem immediately sets `RunningState = NotMoving` when movement completes (line 224)
- PlayerMovementAnimationSystem checks for pending movement in event handler (lines 275-316)
- **BUG**: RunningState is set to NotMoving even if MovementRequest is pending
- **Impact**: Animation flicker - idle animation might play briefly before next movement starts

**Fix**: Check for pending MovementRequest before setting RunningState to NotMoving

---

### Bug 3: Animation State Race Condition

**Old Implementation:**
- MovementSystem directly manages animations synchronously
- Checks `hasNextMovement` before switching to idle (line 242)
- Animation state is consistent with movement state

**Current Implementation:**
- MovementSystem publishes MovementCompletedEvent
- PlayerMovementAnimationSystem handles animation asynchronously via event
- **POTENTIAL BUG**: MovementSystem sets RunningState to NotMoving, then event fires later
- **Impact**: Brief inconsistency between RunningState and animation state

**Fix**: Check for pending movement in MovementSystem before setting RunningState

---

### Bug 4: Turn-in-Place Input Handling

**Old Implementation:**
- When turning, doesn't buffer NEW input (correct - allows tap-to-turn)
- But still processes action button and consumes buffered input
- After turn completes, if input still held, movement starts immediately

**Current Implementation:**
- Early return skips everything when turning
- **BUG**: Can't consume buffered input during turn, can't check action button
- **Impact**: Input feels unresponsive during turn animations

**Fix**: Only skip input buffering during turn, not all input processing

---

## Summary of Required Fixes

1. **InputSystem.cs**: Remove early return when turning - only skip buffering, not all input processing
2. **MovementSystem.cs**: Check for pending MovementRequest before setting RunningState to NotMoving
3. **PlayerMovementAnimationSystem.cs**: Ensure animation state matches movement state (should be fine after MovementSystem fix)


