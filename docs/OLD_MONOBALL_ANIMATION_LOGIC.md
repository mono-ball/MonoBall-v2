# Old MonoBall Animation State Transition Logic - EXACT BEHAVIOR

## Research Analysis Date: 2025-12-22

This document extracts the **EXACT** animation state transition logic from the old MonoBall implementation to ensure pixel-perfect recreation of Pokemon Emerald movement behavior.

---

## 1. Animation State Transitions - When and How

### 1.1 During Movement (IsMoving = true)

**Location:** `MovementSystem.cs:208-314`

```csharp
if (movement.IsMoving)
{
    movement.MovementProgress += movement.MovementSpeed * deltaTime;

    if (movement.MovementProgress >= 1.0f)
    {
        // MOVEMENT COMPLETE
        movement.MovementProgress = 1.0f;
        position.PixelX = movement.TargetPosition.X;
        position.PixelY = movement.TargetPosition.Y;

        // Recalculate grid coordinates
        position.X = (int)((position.PixelX - mapOffset.X) / tileSize);
        position.Y = (int)((position.PixelY - mapOffset.Y) / tileSize);

        movement.CompleteMovement();

        // CRITICAL: Check if there's a pending movement request
        bool hasNextMovement = world.Has<MovementRequest>(entity);

        if (!hasNextMovement)
        {
            // No more movement - switch to IDLE
            animation.ChangeAnimation(movement.FacingDirection.ToIdleAnimation());
        }
        // else: Keep walking animation playing for continuous movement
    }
    else
    {
        // INTERPOLATING
        position.PixelX = MathHelper.Lerp(
            movement.StartPosition.X,
            movement.TargetPosition.X,
            movement.MovementProgress
        );

        position.PixelY = MathHelper.Lerp(
            movement.StartPosition.Y,
            movement.TargetPosition.Y,
            movement.MovementProgress
        );

        // Ensure WALK animation is playing
        string expectedAnimation = movement.FacingDirection.ToWalkAnimation();
        if (animation.CurrentAnimation != expectedAnimation)
        {
            // Changing animation (e.g., from idle or different direction)
            // Don't force restart - continue from current frame
            animation.ChangeAnimation(expectedAnimation);
        }
    }
}
```

**Key Insight:** The system only switches to idle when movement completes AND no next movement request exists. This prevents animation reset during continuous walking.

---

### 1.2 When NOT Moving (IsMoving = false)

**Location:** `MovementSystem.cs:316-357`

```csharp
else // !movement.IsMoving
{
    // Ensure pixel position matches grid position
    if (position.MapId != null)
    {
        int tileSize = GetTileSize(world, new GameMapId(position.MapId.Value));
        Vector2 mapOffset = GetMapWorldOffset(world, new GameMapId(position.MapId.Value));
        position.PixelX = (position.X * tileSize) + mapOffset.X;
        position.PixelY = (position.Y * tileSize) + mapOffset.Y;
    }

    // TURN-IN-PLACE STATE
    if (movement.RunningState == RunningState.TurnDirection)
    {
        // Play TURN animation (walk in place) with PlayOnce=true
        string turnAnimation = movement.FacingDirection.ToTurnAnimation();
        if (animation.CurrentAnimation != turnAnimation || !animation.PlayOnce)
        {
            animation.ChangeAnimation(turnAnimation, forceRestart: true, playOnce: true);
        }

        // Check if turn animation has COMPLETED
        if (animation.IsComplete)
        {
            // Turn complete - allow movement on next input
            movement.RunningState = RunningState.NotMoving;
            // Transition to IDLE animation
            animation.ChangeAnimation(movement.FacingDirection.ToIdleAnimation());
        }
    }
    else
    {
        // NORMAL IDLE STATE
        string expectedAnimation = movement.FacingDirection.ToIdleAnimation();
        if (animation.CurrentAnimation != expectedAnimation)
        {
            animation.ChangeAnimation(expectedAnimation);
        }
    }
}
```

**Key Insight:** Turn-in-place uses the animation system's `IsComplete` flag to determine when the turn finishes, then transitions to idle.

---

## 2. Turn-In-Place State Handling

### 2.1 When Turn-In-Place is Triggered

**Location:** `InputSystem.cs:118-142`

```csharp
if (
    currentDirection != movement.MovementDirection  // Compare against LAST MOVEMENT
    && currentDirection != movement.FacingDirection // Already facing - no turn needed
    && movement.RunningState != RunningState.Moving
    && !movement.IsMoving
)
{
    // TRIGGER TURN IN PLACE
    movement.StartTurnInPlace(currentDirection);
    _logger?.LogTrace(
        "Turning in place from movement direction {From} to {To} (facing: {Facing})",
        movement.MovementDirection,
        currentDirection,
        movement.FacingDirection
    );
}
```

**Location:** `GridMovement.cs:150-157` (`StartTurnInPlace` method)

```csharp
public void StartTurnInPlace(Direction direction)
{
    RunningState = RunningState.TurnDirection;
    FacingDirection = direction;
    // DON'T update MovementDirection here - it stays as the last actual movement direction
    // This matches pokeemerald behavior where movementDirection != facingDirection during turn-in-place
}
```

**Critical Rule:** Turn-in-place compares input against `MovementDirection` (last actual movement), NOT `FacingDirection`.

---

### 2.2 Turn-In-Place Animation Details

**Animation Used:** `go_fast_{direction}` (e.g., "go_fast_south")

**Location:** `Direction.cs:107-114`

```csharp
public static string ToTurnAnimation(this Direction direction)
{
    // Pokemon Emerald's WALK_IN_PLACE_FAST uses GetMoveDirectionFastAnimNum()
    // which returns ANIM_STD_GO_FAST_* (the "go_fast" animations, not "go_faster")
    // It plays for 8 frames at 60fps = 0.133s
    // We use go_fast_* to match the same animation visually
    return $"go_fast_{direction.ToAnimationSuffix()}";
}
```

**Playback Mode:** `PlayOnce = true`

**Location:** `MovementSystem.cs:331-337`

```csharp
// Play turn animation (walk in place) with PlayOnce=true
string turnAnimation = movement.FacingDirection.ToTurnAnimation();
if (animation.CurrentAnimation != turnAnimation || !animation.PlayOnce)
{
    animation.ChangeAnimation(turnAnimation, forceRestart: true, playOnce: true);
}
```

**Completion Detection:** Uses `animation.IsComplete` flag

---

### 2.3 Turn-In-Place Blocking Behavior

**Location:** `MovementSystem.cs:495-510` (ProcessMovementRequests)

```csharp
if (
    request.Active
    && !movement.IsMoving
    && !movement.MovementLocked
    && movement.RunningState != RunningState.TurnDirection  // BLOCKS MOVEMENT
)
{
    // Process the movement request
    TryStartMovement(world, entity, ref position, ref movement, request.Direction);

    // Mark as inactive (component pooling - no removal!)
    request.Active = false;
}
```

**Critical Rule:** Movement requests are BLOCKED while `RunningState == TurnDirection`. The system waits for the turn animation to complete.

---

## 3. What Triggers Turn Animation vs Immediate Movement?

### 3.1 Turn Animation is Triggered When:

**Location:** `InputSystem.cs:118-142`

```csharp
if (
    currentDirection != movement.MovementDirection  // Input differs from LAST MOVEMENT
    && currentDirection != movement.FacingDirection // NOT already facing that direction
    && movement.RunningState != RunningState.Moving
    && !movement.IsMoving
)
{
    movement.StartTurnInPlace(currentDirection);
}
```

**Conditions:**
1. Input direction ≠ `MovementDirection` (last actual movement)
2. Input direction ≠ `FacingDirection` (not already facing it)
3. `RunningState` ≠ `Moving`
4. Not currently moving (`!IsMoving`)

---

### 3.2 Immediate Movement is Allowed When:

**Location:** `InputSystem.cs:143-173`

```csharp
else if (movement.RunningState != RunningState.TurnDirection)
{
    // Either already facing correct direction or already moving - allow movement
    // BUT only if not currently in turn-in-place state (wait for turn to complete)
    movement.RunningState = RunningState.Moving;

    // Buffer input for continuous movement
    // ...
}
```

**Conditions:**
1. Already facing the input direction (`FacingDirection == currentDirection`), OR
2. Direction matches last movement (`MovementDirection == currentDirection`), OR
3. Already moving (allows direction changes during movement)
4. AND `RunningState` ≠ `TurnDirection`

---

## 4. RunningState Flow Through the System

### 4.1 State Machine Diagram

```
NotMoving
    |
    | Input detected (different from MovementDirection AND FacingDirection)
    v
TurnDirection (turn-in-place animation playing)
    |
    | Animation.IsComplete = true
    v
NotMoving (idle animation)
    |
    | Input detected (same as FacingDirection OR MovementDirection)
    v
Moving (movement allowed, input buffered)
    |
    | Movement completes, no pending input
    v
NotMoving (idle animation)
```

### 4.2 State Transitions

**NotMoving → TurnDirection:**
- **Trigger:** `InputSystem.cs:135` - `movement.StartTurnInPlace(currentDirection)`
- **Condition:** Input direction differs from both `MovementDirection` and `FacingDirection`

**TurnDirection → NotMoving:**
- **Trigger:** `MovementSystem.cs:340-345` - `animation.IsComplete` check
- **Condition:** Turn animation completes (PlayOnce cycle finishes)

**NotMoving → Moving:**
- **Trigger:** `InputSystem.cs:147` - `movement.RunningState = RunningState.Moving`
- **Condition:** Input matches `FacingDirection` or `MovementDirection`, not in turn state

**Moving → NotMoving:**
- **Trigger:** `InputSystem.cs:105-108` - No input detected
- **Condition:** `currentDirection == Direction.None` AND not mid-movement AND not turning

**Key Insight:** `RunningState` is managed by `InputSystem` (state changes) and `MovementSystem` (turn completion). `CompleteMovement()` does NOT reset `RunningState` to allow continuous walking.

---

## 5. Relationship Between FacingDirection, MovementDirection, and Animation

### 5.1 Component Definitions

**Location:** `GridMovement.cs:60-72`

```csharp
/// <summary>
/// Gets or sets the current facing direction.
/// This is which way the sprite is facing and can change during turn-in-place.
/// </summary>
public Direction FacingDirection { get; set; }

/// <summary>
/// Gets or sets the direction of the last actual movement.
/// This is used for turn detection - only updated when starting actual movement (not turn-in-place).
/// In pokeemerald, this corresponds to ObjectEvent.movementDirection.
/// </summary>
public Direction MovementDirection { get; set; }
```

---

### 5.2 When Each Direction is Updated

**FacingDirection Updates:**

1. **During Turn-In-Place:** `GridMovement.cs:154`
   ```csharp
   FacingDirection = direction;  // Updates immediately
   ```

2. **During Movement Start:** `GridMovement.cs:115`
   ```csharp
   FacingDirection = direction;  // Updates when movement starts
   ```

**MovementDirection Updates:**

1. **ONLY During Actual Movement Start:** `GridMovement.cs:116`
   ```csharp
   MovementDirection = direction;  // ONLY updates here
   ```

2. **NOT During Turn-In-Place:** `GridMovement.cs:155` (comment)
   ```csharp
   // DON'T update MovementDirection here - it stays as the last actual movement direction
   ```

---

### 5.3 Animation Selection Logic

**Walk Animation:**
```csharp
string expectedAnimation = movement.FacingDirection.ToWalkAnimation();
// Result: "go_south", "go_north", "go_west", "go_east"
```

**Idle Animation:**
```csharp
string expectedAnimation = movement.FacingDirection.ToIdleAnimation();
// Result: "face_south", "face_north", "face_west", "face_east"
```

**Turn Animation:**
```csharp
string turnAnimation = movement.FacingDirection.ToTurnAnimation();
// Result: "go_fast_south", "go_fast_north", "go_fast_west", "go_fast_east"
```

**Critical Insight:** All animations use `FacingDirection`, which represents the visual direction the sprite is facing. `MovementDirection` is used ONLY for turn detection logic.

---

## 6. Complete Animation Flow Example

### Scenario: Player facing South, presses North

```
Initial State:
  FacingDirection: South
  MovementDirection: South
  RunningState: NotMoving
  Animation: "face_south"

Frame 1: North key pressed
  InputSystem.cs:118-142 evaluates:
    currentDirection (North) != MovementDirection (South) ✓
    currentDirection (North) != FacingDirection (South) ✓
    RunningState != Moving ✓
    !IsMoving ✓

  ACTION: movement.StartTurnInPlace(North)
    FacingDirection = North
    MovementDirection = South (UNCHANGED)
    RunningState = TurnDirection

  MovementSystem.cs:329-346 (same frame):
    RunningState == TurnDirection ✓
    ACTION: animation.ChangeAnimation("go_fast_north", forceRestart: true, playOnce: true)

Frames 2-N: Turn animation playing
  Animation: "go_fast_north" (PlayOnce mode)
  animation.IsComplete = false
  RunningState: TurnDirection (blocks movement requests)

Frame N+1: Turn animation completes
  MovementSystem.cs:340-345:
    animation.IsComplete = true ✓
    ACTION: movement.RunningState = NotMoving
    ACTION: animation.ChangeAnimation("face_north")

  State After Turn:
    FacingDirection: North
    MovementDirection: South (still unchanged)
    RunningState: NotMoving
    Animation: "face_north"

Frame N+2: North key still held (or pressed again)
  InputSystem.cs:118-142 evaluates:
    currentDirection (North) != MovementDirection (South) ✓
    currentDirection (North) != FacingDirection (North) ✗
    Condition fails - skip turn-in-place

  InputSystem.cs:143-173 evaluates:
    RunningState != TurnDirection ✓
    ACTION: movement.RunningState = Moving
    ACTION: Buffer input for movement

  MovementSystem.cs:518-836 (TryStartMovement):
    Movement request processed
    ACTION: movement.StartMovement(start, target, North)
      FacingDirection = North
      MovementDirection = North (NOW UPDATED)
      IsMoving = true

  MovementSystem.cs:289-314:
    IsMoving = true ✓
    ACTION: animation.ChangeAnimation("go_north")

Frames N+3 onwards: Walking North
  Animation: "go_north" (looping)
  MovementProgress: 0.0 → 1.0
  Interpolating pixel position
```

---

## 7. Critical Implementation Details

### 7.1 Animation Component Write-Back

**CRITICAL FIX:** `MovementSystem.cs:124-138`

```csharp
if (world.TryGet(entity, out Animation animation))
{
    ProcessMovementWithAnimation(
        world,
        entity,
        ref position,
        ref movement,
        ref animation,  // Passed by ref
        deltaTime
    );

    // CRITICAL FIX: Write modified animation back to entity
    // TryGet returns a COPY of the struct, so changes must be written back
    world.Set(entity, animation);
}
```

**Why:** `TryGet` returns a COPY of the struct. Changes to `animation` must be written back with `world.Set()`.

---

### 7.2 Continuous Walking Without Animation Reset

**Location:** `MovementSystem.cs:239-248`

```csharp
// CRITICAL FIX: Don't switch to idle if player will continue moving
bool hasNextMovement = world.Has<MovementRequest>(entity);

if (!hasNextMovement)
{
    // No more movement - switch to idle
    animation.ChangeAnimation(movement.FacingDirection.ToIdleAnimation());
}
// else: Keep walk animation playing for continuous movement
```

**Why:** Prevents animation reset between consecutive tile movements (Pokemon Emerald behavior).

---

### 7.3 Turn Detection Uses MovementDirection

**Location:** `InputSystem.cs:119-142`

```csharp
if (
    currentDirection != movement.MovementDirection  // CRITICAL: NOT FacingDirection
    && currentDirection != movement.FacingDirection
    && movement.RunningState != RunningState.Moving
    && !movement.IsMoving
)
{
    movement.StartTurnInPlace(currentDirection);
}
```

**Why:** Matches pokeemerald behavior where turn detection compares against `ObjectEvent.movementDirection`, not facing direction.

---

### 7.4 RunningState Persistence Across Movement

**Location:** `GridMovement.cs:132-142`

```csharp
public void CompleteMovement()
{
    IsMoving = false;
    MovementProgress = 0f;
    // Don't reset RunningState - if input is still held, we want to skip turn-in-place
    // InputSystem will set RunningState = NotMoving when no input is detected
}
```

**Why:** Allows continuous walking without triggering turn-in-place between tiles.

---

## 8. Animation State Summary Table

| Condition | RunningState | Animation | Notes |
|-----------|-------------|-----------|-------|
| No input, not moving | NotMoving | `face_{direction}` | Idle, facing last direction |
| Input differs from MovementDirection | TurnDirection | `go_fast_{direction}` (PlayOnce) | Turn-in-place, blocks movement |
| Turn animation completes | NotMoving → Moving (if input held) | `face_{direction}` → `go_{direction}` | Transition to walk if key still held |
| Input matches FacingDirection | Moving | `go_{direction}` | Immediate movement, no turn |
| Currently moving | Moving | `go_{direction}` | Walk animation looping |
| Movement completes, input held | Moving | `go_{direction}` (continues) | No animation reset |
| Movement completes, no input | NotMoving | `face_{direction}` | Return to idle |

---

## 9. Key Takeaways for New Implementation

### Must-Have Features:

1. **Three-way direction tracking:**
   - `FacingDirection` - visual sprite facing (updates during turn-in-place)
   - `MovementDirection` - last actual movement (only updates when movement starts)
   - `InputDirection` - current input from player

2. **Turn detection logic:**
   - Compare input against `MovementDirection`, NOT `FacingDirection`
   - Only turn if input differs from BOTH directions

3. **Turn-in-place blocking:**
   - Block movement requests while `RunningState == TurnDirection`
   - Wait for `animation.IsComplete` before allowing movement

4. **Animation continuity:**
   - Check for pending movement requests before switching to idle
   - Don't reset walk animation between consecutive tiles

5. **RunningState persistence:**
   - Don't reset `RunningState` in `CompleteMovement()`
   - Let `InputSystem` manage state based on input

6. **Animation selection:**
   - Walk: `go_{direction}`
   - Idle: `face_{direction}`
   - Turn: `go_fast_{direction}` with `PlayOnce = true`

---

## 10. File References

**Primary Files:**
- `/oldmonoball/MonoBallFramework.Game/GameSystems/Movement/MovementSystem.cs` (Lines 104-954)
- `/oldmonoball/MonoBallFramework.Game/Engine/Input/Systems/InputSystem.cs` (Lines 63-221)
- `/oldmonoball/MonoBallFramework.Game/Ecs/Components/Movement/GridMovement.cs` (Lines 1-175)
- `/oldmonoball/MonoBallFramework.Game/Ecs/Components/Movement/Direction.cs` (Lines 38-132)
- `/oldmonoball/MonoBallFramework.Game/Ecs/Components/Rendering/Animation.cs` (Lines 1-132)

**Key Extension Methods:**
- `ToWalkAnimation()` - Direction.cs:81-84
- `ToIdleAnimation()` - Direction.cs:92-95
- `ToTurnAnimation()` - Direction.cs:107-114

---

## Document Status

**Status:** ✓ Complete
**Accuracy:** Extracted directly from source code
**Purpose:** Reference for new implementation to match exact behavior
**Next Steps:** Use this document to verify new PlayerMovementAnimationSystem implementation
