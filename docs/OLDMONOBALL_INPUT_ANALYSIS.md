# Old MonoBall Input Handling System - Complete Analysis

## Research Summary

This document contains the EXACT logic extracted from the oldmonoball implementation for input handling, movement request creation, turn-in-place behavior, and RunningState management.

## File Locations

- **InputSystem**: `/oldmonoball/MonoBallFramework.Game/Engine/Input/Systems/InputSystem.cs`
- **MovementSystem**: `/oldmonoball/MonoBallFramework.Game/GameSystems/Movement/MovementSystem.cs`
- **GridMovement**: `/oldmonoball/MonoBallFramework.Game/Ecs/Components/Movement/GridMovement.cs`
- **MovementRequest**: `/oldmonoball/MonoBallFramework.Game/Ecs/Components/Movement/MovementRequest.cs`
- **InputBuffer**: `/oldmonoball/MonoBallFramework.Game/Engine/Input/Services/InputBuffer.cs`
- **InputState**: `/oldmonoball/MonoBallFramework.Game/Engine/Input/Components/InputState.cs`

---

## 1. Input Processing Flow (InputSystem.cs)

### Key Components

**Query Structure** (Lines 35-41):
```csharp
private readonly QueryDescription _playerQuery = QueryCache.Get<
    Player,
    Position,
    GridMovement,
    InputState,
    Direction
>();
```

**Update Loop** (Lines 63-221):
```csharp
public override void Update(World world, float deltaTime)
{
    _totalTime += deltaTime;

    // Check if input is blocked
    if (_inputBlocker?.IsInputBlocked == true)
    {
        _prevKeyboardState = _keyboardState;
        return;
    }

    // Poll input once per frame
    _prevKeyboardState = _keyboardState;
    _keyboardState = Keyboard.GetState();
    _gamepadState = GamePad.GetState(PlayerIndex.One);

    // Process input for all players
    world.Query(in _playerQuery, (Entity entity, ref Position position,
                                   ref GridMovement movement, ref InputState input) =>
    {
        // ... input processing logic
    });
}
```

---

## 2. Turn-in-Place vs Immediate Movement Logic

### Critical Comparison (Lines 118-176)

**The EXACT logic from oldmonoball:**

```csharp
// Get current input direction
Direction currentDirection = GetInputDirection(_keyboardState, _gamepadState);

// Pokemon Emerald-style running state logic
if (currentDirection == Direction.None)
{
    // No input - set to not moving (only if not mid-movement and not turning in place)
    // Don't cancel turn-in-place when key is released - let the animation complete
    if (!movement.IsMoving && movement.RunningState != RunningState.TurnDirection)
    {
        movement.RunningState = RunningState.NotMoving;
    }
}
else
{
    input.PressedDirection = currentDirection;

    // Synchronize Direction component with input direction
    ref Direction direction = ref entity.Get<Direction>();
    direction = currentDirection;

    // Check if we need to turn in place first (pokeemerald behavior)
    // CRITICAL: Compare against MovementDirection (last actual movement), not FacingDirection
    // This matches pokeemerald/src/field_player_avatar.c:588 behavior:
    //   direction != GetPlayerMovementDirection() && runningState != MOVING
    // MovementDirection only updates when starting actual movement, not during turn-in-place
    // Exception: If already facing the input direction, allow immediate movement (no turn needed)
    if (
        currentDirection != movement.MovementDirection
        && currentDirection != movement.FacingDirection // Already facing this direction - no turn needed
        && movement.RunningState != RunningState.Moving
        && !movement.IsMoving
    )
    {
        // Turn in place - start turn animation
        // DON'T buffer input here - only move if key is still held when turn completes
        // This allows tapping to just face a direction without moving
        movement.StartTurnInPlace(currentDirection);
        _logger?.LogTrace(
            "Turning in place from movement direction {From} to {To} (facing: {Facing})",
            movement.MovementDirection,
            currentDirection,
            movement.FacingDirection
        );
    }
    else if (movement.RunningState != RunningState.TurnDirection)
    {
        // Either already facing correct direction or already moving - allow movement
        // BUT only if not currently in turn-in-place state (wait for turn to complete)
        movement.RunningState = RunningState.Moving;

        // Buffer input if:
        // 1. Not currently moving (allows holding keys for continuous movement), OR
        // 2. Direction changed (allows queuing direction changes during movement)
        // But only if we haven't buffered this exact direction very recently (prevents duplicates)
        bool shouldBuffer =
            !movement.IsMoving || currentDirection != _lastBufferedDirection;

        // Also prevent buffering the same direction multiple times per frame
        bool isDifferentTiming =
            _totalTime != _lastBufferTime
            || currentDirection != _lastBufferedDirection;

        if (shouldBuffer && isDifferentTiming)
        {
            if (_inputBuffer.AddInput(currentDirection, _totalTime))
            {
                _lastBufferedDirection = currentDirection;
                _lastBufferTime = _totalTime;
                _logger?.LogTrace("Buffered input direction: {Direction}", currentDirection);
            }
        }
    }
    // else: RunningState == TurnDirection - wait for turn animation to complete
    // MovementSystem will set RunningState = NotMoving when turn completes
}
```

### Key Conditions for Turn-in-Place

**Trigger Conditions** (Line 124-130):
```csharp
if (
    currentDirection != movement.MovementDirection       // Input differs from last movement
    && currentDirection != movement.FacingDirection      // Not already facing that way
    && movement.RunningState != RunningState.Moving      // Not already moving
    && !movement.IsMoving                                 // Not in movement animation
)
{
    movement.StartTurnInPlace(currentDirection);
}
```

**Important Notes:**
- Compares against `MovementDirection`, NOT `FacingDirection`
- `MovementDirection` only updates when actual movement starts (Line 116 in GridMovement.cs)
- This matches Pokemon Emerald's `GetPlayerMovementDirection()` behavior

---

## 3. MovementRequest Creation (Lines 185-218)

### When MovementRequest is Created

**EXACT logic from InputSystem.cs:**

```csharp
// Try to consume buffered input if not currently moving
// Check if there's buffered input and no active movement request
if (
    !movement.IsMoving
    && _inputBuffer.TryConsumeInput(_totalTime, out Direction bufferedDirection)
)
{
    // Use component pooling: reuse existing component or add new one
    if (entity.Has<MovementRequest>())
    {
        ref MovementRequest request = ref entity.Get<MovementRequest>();
        if (!request.Active)
        {
            request.Direction = bufferedDirection;
            request.Active = true;
            _inputEventsProcessed++;
            _logger?.LogTrace("Consumed buffered input: {Direction}", bufferedDirection);
            _lastBufferedDirection = Direction.None;
        }
    }
    else
    {
        world.Add(entity, new MovementRequest(bufferedDirection));
        _inputEventsProcessed++;
        _logger?.LogTrace("Consumed buffered input: {Direction}", bufferedDirection);
        _lastBufferedDirection = Direction.None;
    }
}
```

**Key Points:**
- MovementRequest ONLY created when `!movement.IsMoving`
- Consumes from input buffer (not direct input)
- Uses component pooling (reuses existing component if present)
- Marks request as `Active = true`

---

## 4. RunningState Management

### RunningState Enum (GridMovement.cs Lines 9-27)

```csharp
public enum RunningState
{
    /// Player is not moving and no input detected.
    NotMoving = 0,

    /// Player is turning in place to face a new direction.
    /// This happens when input direction differs from facing direction.
    /// Movement won't start until the turn completes and input is still held.
    TurnDirection = 1,

    /// Player is actively moving between tiles.
    Moving = 2
}
```

### State Transitions

**InputSystem Sets:**

1. **NotMoving** (Line 107):
   ```csharp
   if (!movement.IsMoving && movement.RunningState != RunningState.TurnDirection)
   {
       movement.RunningState = RunningState.NotMoving;
   }
   ```

2. **TurnDirection** (Line 135 via StartTurnInPlace):
   ```csharp
   movement.StartTurnInPlace(currentDirection);
   // Inside StartTurnInPlace (GridMovement.cs Line 153):
   RunningState = RunningState.TurnDirection;
   FacingDirection = direction;
   ```

3. **Moving** (Line 147):
   ```csharp
   else if (movement.RunningState != RunningState.TurnDirection)
   {
       movement.RunningState = RunningState.Moving;
   }
   ```

**MovementSystem Sets:**

**NotMoving** (After turn-in-place completes, MovementSystem.cs Line 343):
```csharp
if (movement.RunningState == RunningState.TurnDirection)
{
    // Play turn animation (walk in place) with PlayOnce=true
    string turnAnimation = movement.FacingDirection.ToTurnAnimation();
    if (animation.CurrentAnimation != turnAnimation || !animation.PlayOnce)
    {
        animation.ChangeAnimation(turnAnimation, true, true);
    }

    // Check if turn animation has completed
    if (animation.IsComplete)
    {
        // Turn complete - allow movement on next input
        movement.RunningState = RunningState.NotMoving;
        // Transition to idle animation
        animation.ChangeAnimation(movement.FacingDirection.ToIdleAnimation());
    }
}
```

---

## 5. Input Buffering System

### InputBuffer.cs Key Methods

**AddInput** (Lines 46-67):
```csharp
public bool AddInput(Direction direction, float currentTime)
{
    // Ignore None direction
    if (direction == Direction.None)
        return false;

    // Remove expired inputs
    RemoveExpiredInputs(currentTime);

    // Check if buffer has space
    if (_buffer.Count >= _maxBufferSize)
        return false;

    // Add new input
    var command = new InputCommand(direction, currentTime);
    _buffer.Enqueue(command);
    return true;
}
```

**TryConsumeInput** (Lines 76-91):
```csharp
public bool TryConsumeInput(float currentTime, out Direction direction)
{
    // Remove expired inputs
    RemoveExpiredInputs(currentTime);

    // Try to consume oldest input
    if (_buffer.Count > 0)
    {
        InputCommand command = _buffer.Dequeue();
        direction = command.Direction;
        return true;
    }

    direction = Direction.None;
    return false;
}
```

**Buffer Settings** (Lines 27-32):
```csharp
public InputBuffer(int maxSize = 5, float timeoutSeconds = 0.2f)
{
    _buffer = new Queue<InputCommand>(maxSize);
    _maxBufferSize = maxSize;
    _bufferTimeoutSeconds = timeoutSeconds;
}
```

**Default Values:**
- `maxBufferSize = 5`
- `bufferTimeout = 0.2f` seconds (200ms)

---

## 6. MovementSystem Processing (MovementSystem.cs)

### ProcessMovementRequests (Lines 483-512)

```csharp
private void ProcessMovementRequests(World world)
{
    // Process all active movement requests
    world.Query(
        in EcsQueries.MovementRequests,
        (Entity entity, ref Position position, ref GridMovement movement, ref MovementRequest request) =>
        {
            // Only process active requests for entities that aren't already moving, aren't locked,
            // and aren't currently turning in place (Pokemon Emerald: wait for turn to complete)
            if (
                request.Active
                && !movement.IsMoving
                && !movement.MovementLocked
                && movement.RunningState != RunningState.TurnDirection
            )
            {
                // Process the movement request
                TryStartMovement(world, entity, ref position, ref movement, request.Direction);

                // Mark as inactive (component pooling - no removal!)
                request.Active = false;
            }
        }
    );
}
```

**Key Validation:**
- `request.Active` - Request must be active
- `!movement.IsMoving` - Not already moving
- `!movement.MovementLocked` - Not locked (cutscenes, dialogue)
- `movement.RunningState != RunningState.TurnDirection` - **NOT turning in place**

---

## 7. Critical Implementation Details

### FacingDirection vs MovementDirection

**GridMovement.cs Documentation (Lines 61-72):**

```csharp
/// Gets or sets the current facing direction.
/// This is which way the sprite is facing and can change during turn-in-place.
public Direction FacingDirection { get; set; }

/// Gets or sets the direction of the last actual movement.
/// This is used for turn detection - only updated when starting actual movement (not turn-in-place).
/// In pokeemerald, this corresponds to ObjectEvent.movementDirection.
/// See pokeemerald/src/field_player_avatar.c:588 - compares against GetPlayerMovementDirection().
public Direction MovementDirection { get; set; }
```

**StartMovement Updates Both** (Lines 109-117):
```csharp
public void StartMovement(Vector2 start, Vector2 target, Direction direction)
{
    IsMoving = true;
    StartPosition = start;
    TargetPosition = target;
    MovementProgress = 0f;
    FacingDirection = direction;
    MovementDirection = direction; // Update movement direction when starting actual movement
}
```

**StartTurnInPlace Updates Only FacingDirection** (Lines 151-157):
```csharp
public void StartTurnInPlace(Direction direction)
{
    RunningState = RunningState.TurnDirection;
    FacingDirection = direction;
    // DON'T update MovementDirection here - it stays as the last actual movement direction
    // This matches pokeemerald behavior where movementDirection != facingDirection during turn-in-place
}
```

---

## 8. Turn-in-Place Animation Handling (MovementSystem.cs)

### Animation Logic When Not Moving (Lines 328-357)

```csharp
// Handle turn-in-place state (Pokemon Emerald behavior)
if (movement.RunningState == RunningState.TurnDirection)
{
    // Play turn animation (walk in place) with PlayOnce=true
    // Pokemon Emerald uses WALK_IN_PLACE_FAST which plays walk animation for one cycle
    string turnAnimation = movement.FacingDirection.ToTurnAnimation();
    if (animation.CurrentAnimation != turnAnimation || !animation.PlayOnce)
    {
        animation.ChangeAnimation(turnAnimation, true, true);
    }

    // Check if turn animation has completed (uses animation framework's timing)
    if (animation.IsComplete)
    {
        // Turn complete - allow movement on next input
        movement.RunningState = RunningState.NotMoving;
        // Transition to idle animation
        animation.ChangeAnimation(movement.FacingDirection.ToIdleAnimation());
    }
}
else
{
    // Ensure idle animation is playing
    string expectedAnimation = movement.FacingDirection.ToIdleAnimation();
    if (animation.CurrentAnimation != expectedAnimation)
    {
        animation.ChangeAnimation(expectedAnimation);
    }
}
```

**Key Points:**
- Turn animation plays ONCE (`PlayOnce=true`)
- After animation completes, sets `RunningState = NotMoving`
- Transitions to idle animation
- Only then can movement start (if input still held)

---

## 9. Input Blocking During Turn-in-Place

### Three Layers of Blocking

**Layer 1: InputSystem doesn't buffer during turn** (Line 143):
```csharp
else if (movement.RunningState != RunningState.TurnDirection)
{
    movement.RunningState = RunningState.Moving;
    // ... buffer input ...
}
// else: RunningState == TurnDirection - wait for turn animation to complete
```

**Layer 2: MovementSystem doesn't consume requests during turn** (Line 501):
```csharp
if (
    request.Active
    && !movement.IsMoving
    && !movement.MovementLocked
    && movement.RunningState != RunningState.TurnDirection  // ← CRITICAL CHECK
)
{
    TryStartMovement(world, entity, ref position, ref movement, request.Direction);
    request.Active = false;
}
```

**Layer 3: Turn animation must complete** (MovementSystem.cs Line 340):
```csharp
if (animation.IsComplete)
{
    movement.RunningState = RunningState.NotMoving;
}
```

---

## 10. Complete State Machine

### State Flow Diagram

```
[NotMoving]
    |
    | Input detected, direction != MovementDirection
    v
[TurnDirection]
    |
    | Animation completes (animation.IsComplete)
    v
[NotMoving]
    |
    | Input still held, direction == FacingDirection
    v
[Moving]
    |
    | Movement completes, no buffered input
    v
[NotMoving]
```

### Transition Conditions

**NotMoving → TurnDirection:**
- Input direction ≠ MovementDirection
- Input direction ≠ FacingDirection (skip if already facing)
- RunningState ≠ Moving
- !IsMoving

**TurnDirection → NotMoving:**
- animation.IsComplete == true (in MovementSystem)

**NotMoving → Moving:**
- Input direction == FacingDirection OR RunningState already Moving
- RunningState ≠ TurnDirection

**Moving → NotMoving:**
- No input detected (in InputSystem, Line 107)
- !IsMoving
- RunningState ≠ TurnDirection

---

## 11. Key Differences from Current Implementation

### Missing in Current MonoBall.Core

1. **InputBuffer class** - Current implementation doesn't have the circular queue buffering
2. **Component pooling** - MovementRequest is not pooled (Active flag pattern)
3. **Turn animation completion check** - No `animation.IsComplete` tracking
4. **Three-layer blocking** - Missing comprehensive turn-in-place blocking
5. **MovementDirection tracking** - Only FacingDirection is used for comparison
6. **Precise buffer timing** - No duplicate prevention logic

### Critical Logic to Port

1. **Turn-in-place condition** (InputSystem.cs Line 124):
   ```csharp
   currentDirection != movement.MovementDirection
   && currentDirection != movement.FacingDirection
   && movement.RunningState != RunningState.Moving
   && !movement.IsMoving
   ```

2. **MovementRequest blocking** (MovementSystem.cs Line 501):
   ```csharp
   movement.RunningState != RunningState.TurnDirection
   ```

3. **Turn completion** (MovementSystem.cs Line 340):
   ```csharp
   if (animation.IsComplete)
       movement.RunningState = RunningState.NotMoving;
   ```

---

## Summary

The oldmonoball input system implements a sophisticated state machine that:

1. **Distinguishes between FacingDirection and MovementDirection** for accurate turn detection
2. **Uses RunningState enum** to track NotMoving/TurnDirection/Moving states
3. **Buffers input** through InputBuffer with 200ms timeout and 5-input capacity
4. **Blocks movement during turn-in-place** at three different layers
5. **Waits for animation completion** before allowing movement after turn
6. **Pools MovementRequest components** for performance
7. **Prevents duplicate buffering** with timing and direction checks

The EXACT comparison logic is:
```csharp
currentDirection != movement.MovementDirection  // NOT FacingDirection!
```

This is the critical difference that enables proper Pokemon-style turn-in-place behavior.
