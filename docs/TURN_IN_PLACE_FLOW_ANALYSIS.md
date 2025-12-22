# Turn-in-Place Flow Analysis - Pokemon Emerald Style

**Source:** oldmonoball codebase at `/mnt/c/Users/nate0/RiderProjects/MonoBall/oldmonoball/`

## Executive Summary

This document traces the COMPLETE turn-in-place behavior from the oldmonoball implementation, answering:
1. **When does a turn-in-place animation play?** When input direction differs from `MovementDirection` (not `FacingDirection`)
2. **When does it NOT play?** When already moving, already facing the correct direction, or during active movement
3. **How is turn completion detected?** Via `Animation.IsComplete` flag set by SpriteAnimationSystem
4. **What happens after turn completes?** Transitions to idle animation, waits for player to provide new input

---

## Key Files Analyzed

1. **InputSystem** (`/Engine/Input/Systems/InputSystem.cs`) - Detects when to initiate turn
2. **MovementSystem** (`/GameSystems/Movement/MovementSystem.cs`) - Handles turn animation playback and completion
3. **GridMovement** (`/Ecs/Components/Movement/GridMovement.cs`) - Stores `RunningState` and direction tracking
4. **Animation** (`/Ecs/Components/Rendering/Animation.cs`) - Provides `PlayOnce` and `IsComplete` flags
5. **SpriteAnimationSystem** (`/Systems/Rendering/SpriteAnimationSystem.cs`) - Sets `IsComplete` when animation finishes
6. **Direction Extensions** (`/Ecs/Components/Movement/Direction.cs`) - Defines turn animation as `go_fast_*`

---

## The Turn-in-Place State Machine

### State: RunningState Enum
```csharp
public enum RunningState
{
    NotMoving = 0,      // Idle, no input
    TurnDirection = 1,  // Turning in place (animation playing)
    Moving = 2          // Actively moving between tiles
}
```

### Critical Direction Tracking
```csharp
public struct GridMovement
{
    // CURRENT facing (updated during turn-in-place)
    public Direction FacingDirection { get; set; }

    // LAST ACTUAL MOVEMENT direction (only updated when StartMovement() is called)
    // This is what turn detection compares against!
    public Direction MovementDirection { get; set; }

    public RunningState RunningState { get; set; }
}
```

**Key Insight:** Turn detection uses `MovementDirection`, not `FacingDirection`. This matches pokeemerald's behavior where `ObjectEvent.movementDirection` tracks the last actual movement.

---

## Flow 1: When Turn Animation PLAYS

### Trigger Conditions (InputSystem.cs lines 118-141)

```csharp
// Turn in place happens when ALL these conditions are met:
if (currentDirection != movement.MovementDirection  // Input differs from LAST movement
    && currentDirection != movement.FacingDirection  // NOT already facing input direction
    && movement.RunningState != RunningState.Moving  // Not currently moving
    && !movement.IsMoving)                           // Not mid-movement
{
    // Start turn animation
    movement.StartTurnInPlace(currentDirection);
}
```

### Turn Initiation (GridMovement.cs lines 151-157)

```csharp
public void StartTurnInPlace(Direction direction)
{
    RunningState = RunningState.TurnDirection;
    FacingDirection = direction;  // Update facing
    // DON'T update MovementDirection - keeps last actual movement direction
}
```

### Animation Setup (MovementSystem.cs lines 328-337)

```csharp
if (movement.RunningState == RunningState.TurnDirection)
{
    // Play turn animation (walk in place) with PlayOnce=true
    string turnAnimation = movement.FacingDirection.ToTurnAnimation(); // "go_fast_south"
    if (animation.CurrentAnimation != turnAnimation || !animation.PlayOnce)
    {
        animation.ChangeAnimation(turnAnimation, playOnce: true, forceRestart: true);
    }
}
```

**Animation Name:** `go_fast_*` (e.g., `go_fast_south`, `go_fast_north`)
- Pokemon Emerald uses `WALK_IN_PLACE_FAST` → `ANIM_STD_GO_FAST_*`
- Plays for 8 frames at 60fps = ~133ms
- Uses 4-frame animation with `PlayOnce=true`

---

## Flow 2: When Turn Animation DOES NOT Play

### Case 1: Already Facing Correct Direction
```csharp
// InputSystem.cs line 127
if (currentDirection != movement.FacingDirection)
    // ^ If already facing the input direction, skip turn check entirely
```

**Example:** Player is facing North, taps Up → No turn, immediate movement attempt

### Case 2: Already Moving
```csharp
// InputSystem.cs line 128
if (movement.RunningState != RunningState.Moving && !movement.IsMoving)
    // ^ If already in Moving state, skip turn-in-place
```

**Example:** Player is walking North, holds Down → Queues movement, no turn-in-place when movement starts

### Case 3: Currently Turning
```csharp
// MovementSystem.cs line 501
if (movement.RunningState != RunningState.TurnDirection)
    // Movement requests are blocked during turn-in-place
```

**Example:** Player is mid-turn animation → Input is ignored until turn completes

### Case 4: Continuous Movement in Same Direction
```csharp
// MovementSystem.cs line 243
bool hasNextMovement = world.Has<MovementRequest>(entity);
if (!hasNextMovement)
{
    animation.ChangeAnimation(movement.FacingDirection.ToIdleAnimation());
}
```

**Example:** Player holds Right while walking Right → Walk animation continues, no idle/turn interruption

---

## Flow 3: Turn Completion Detection

### Step 1: SpriteAnimationSystem Marks Completion (SpriteAnimationSystem.cs lines 153-165)

```csharp
// When animation reaches end of frames
if (animation.CurrentFrame >= animData.FrameIndices.Count)
{
    // PlayOnce overrides Loop setting
    if (animData.Loop && !animation.PlayOnce)
    {
        animation.CurrentFrame = 0; // Loop
    }
    else
    {
        // Non-looping or PlayOnce completed
        animation.CurrentFrame = animData.FrameIndices.Count - 1;
        animation.IsComplete = true;  // ← CRITICAL FLAG
        animation.IsPlaying = false;
    }
}
```

**Timing:**
- `go_fast_*` animation has 4 frames
- Default frame duration: 0.125s (1/8 second)
- Total turn duration: 4 × 0.125s = **0.5 seconds**
- Pokemon Emerald: 8 frames at 60fps = **0.133 seconds** (faster!)

### Step 2: MovementSystem Detects Completion (MovementSystem.cs lines 339-346)

```csharp
// Check if turn animation has completed
if (animation.IsComplete)
{
    // Turn complete - allow movement on next input
    movement.RunningState = RunningState.NotMoving;
    // Transition to idle animation
    animation.ChangeAnimation(movement.FacingDirection.ToIdleAnimation());
}
```

---

## Flow 4: After Turn Completion

### Immediate Actions (MovementSystem.cs line 343)
```csharp
movement.RunningState = RunningState.NotMoving; // Reset state
animation.ChangeAnimation(movement.FacingDirection.ToIdleAnimation()); // Play "face_*"
```

### Next Frame Behavior (InputSystem.cs lines 143-173)

**If Player Releases Key During Turn:**
```csharp
if (currentDirection == Direction.None)
{
    // No input - stays in NotMoving state
    movement.RunningState = RunningState.NotMoving;
}
// Result: Character faces new direction, stands idle
```

**If Player Holds Key Through Turn:**
```csharp
else if (movement.RunningState != RunningState.TurnDirection)
{
    // Turn completed, input still held
    movement.RunningState = RunningState.Moving;
    _inputBuffer.AddInput(currentDirection, _totalTime);
}
// Result: Movement request created on next frame
```

**Critical Timing:**
- Turn completes → `RunningState = NotMoving`
- Next InputSystem update → Detects held input + NotMoving state
- Creates `MovementRequest` → MovementSystem processes it
- Character moves in new direction WITHOUT another turn

---

## The Pokemon Emerald Tap vs Hold Behavior

### Tap Direction (Quick Press/Release)
1. **Frame 1:** Input detected → `StartTurnInPlace(newDirection)`
2. **Frames 2-30:** Turn animation plays (`go_fast_*` with `PlayOnce=true`)
3. **Frame 31:** Animation completes → `IsComplete=true`
4. **Frame 32:** MovementSystem sets `RunningState=NotMoving`, switches to idle
5. **Frame 33+:** No input detected → Character remains idle, facing new direction

**Result:** Character turns to face direction, does NOT move

### Hold Direction (Press and Hold)
1. **Frame 1:** Input detected → `StartTurnInPlace(newDirection)`
2. **Frames 2-30:** Turn animation plays, input still buffered
3. **Frame 31:** Animation completes → `IsComplete=true`
4. **Frame 32:** MovementSystem sets `RunningState=NotMoving`
5. **Frame 33:** InputSystem sees held input + NotMoving → Buffers movement
6. **Frame 34:** MovementSystem processes `MovementRequest` → Starts movement

**Result:** Character turns, then immediately walks in new direction

---

## Critical Implementation Details

### 1. Direction Comparison Logic
```csharp
// CORRECT (matches oldmonoball)
if (currentDirection != movement.MovementDirection  // Compare to LAST MOVEMENT
    && currentDirection != movement.FacingDirection) // Unless already facing
{
    StartTurnInPlace(currentDirection);
}

// WRONG (common mistake)
if (currentDirection != movement.FacingDirection) // Only compare to facing
{
    // This would cause turn-in-place even when already facing the direction
    // after completing one movement tile
}
```

### 2. Input Blocking During Turn
```csharp
// MovementSystem.cs line 501
if (request.Active
    && !movement.IsMoving
    && !movement.MovementLocked
    && movement.RunningState != RunningState.TurnDirection) // ← BLOCKS during turn
{
    TryStartMovement(world, entity, ref position, ref movement, request.Direction);
}
```

### 3. Movement Direction Only Updates on Actual Movement
```csharp
// GridMovement.cs line 116
public void StartMovement(Vector2 start, Vector2 target, Direction direction)
{
    MovementDirection = direction; // ← Only updated here, not during turn!
}
```

### 4. Animation PlayOnce Behavior
```csharp
// Animation.cs lines 71-91
public void ChangeAnimation(string animationName, bool forceRestart = false, bool playOnce = false)
{
    if (CurrentAnimation != animationName || forceRestart)
    {
        PlayOnce = playOnce; // ← Set PlayOnce flag
    }
}

// SpriteAnimationSystem.cs line 154
if (animData.Loop && !animation.PlayOnce) // ← PlayOnce overrides manifest Loop
```

---

## Differences from Pokemon Emerald

### Timing Differences
| Aspect | Pokemon Emerald | oldmonoball |
|--------|----------------|-------------|
| Turn Animation | 8 frames @ 60fps = 133ms | 4 frames @ 8fps = 500ms |
| Frame Duration | 16.67ms (60fps) | 125ms (8fps) |
| Total Turn Time | **0.133s** | **0.5s** |

**Recommendation:** Adjust frame durations to match Emerald's snappier feel:
```csharp
// For go_fast_* animations
FrameDurations = [0.033f, 0.033f, 0.033f, 0.033f] // 4 frames @ 30fps = 133ms
```

### Movement Direction Tracking
**Pokemon Emerald:** Uses `ObjectEvent.movementDirection` (pokeemerald/src/field_player_avatar.c:588)
**oldmonoball:** Uses `GridMovement.MovementDirection` - **MATCHES!**

### Input Buffering
**Pokemon Emerald:** No input buffering during turn (immediate input check after turn completes)
**oldmonoball:** Has InputBuffer system - can queue input during turn for more responsive feel

---

## Summary: When EXACTLY Should Turn Animation Play?

### YES - Play Turn Animation:
✅ Player taps a direction different from `MovementDirection` (last actual movement)
✅ Player is standing idle (`RunningState == NotMoving`)
✅ Player is not mid-movement (`!IsMoving`)
✅ Player is not already facing that direction (`FacingDirection != inputDirection`)

### NO - Skip Turn Animation:
❌ Player is already facing the input direction (`FacingDirection == inputDirection`)
❌ Player is currently moving (`IsMoving == true` or `RunningState == Moving`)
❌ Player is mid-turn animation (`RunningState == TurnDirection`)
❌ Player changes direction while walking continuously (turn happens instantly via `FacingDirection` update during `StartMovement()`)

### Edge Cases:
1. **Walking North, tap South:** Turn-in-place plays (opposite direction)
2. **Walking North, release key, tap North:** No turn (already facing North)
3. **Walking North, release key, tap East:** Turn-in-place plays (different from MovementDirection)
4. **Walking North, tap East while moving:** Movement queued, no turn-in-place (continuous movement)

---

## File References

All file paths relative to `/mnt/c/Users/nate0/RiderProjects/MonoBall/oldmonoball/`:

- **InputSystem**: `MonoBallFramework.Game/Engine/Input/Systems/InputSystem.cs` (lines 118-176)
- **MovementSystem**: `MonoBallFramework.Game/GameSystems/Movement/MovementSystem.cs` (lines 328-357, 497-511)
- **GridMovement**: `MonoBallFramework.Game/Ecs/Components/Movement/GridMovement.cs` (lines 151-157)
- **Animation**: `MonoBallFramework.Game/Ecs/Components/Rendering/Animation.cs` (lines 71-91)
- **SpriteAnimationSystem**: `MonoBallFramework.Game/Systems/Rendering/SpriteAnimationSystem.cs` (lines 153-165)
- **Direction Extensions**: `MonoBallFramework.Game/Ecs/Components/Movement/Direction.cs` (lines 107-114)

---

## Recommended Next Steps

1. **Timing Adjustment:** Update `go_fast_*` animation frame durations to 33ms (4 frames × 33ms = 132ms, matching Emerald)
2. **Animation Verification:** Ensure sprite manifests have `go_fast_*` animations defined
3. **Testing Scenarios:**
   - Tap direction → Turn only, no movement
   - Hold direction → Turn + immediate movement
   - Walk North, release, tap East → Turn to East
   - Walk North, tap East while moving → Queue East movement, no turn-in-place
