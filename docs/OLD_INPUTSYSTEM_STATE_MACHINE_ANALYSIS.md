# Old MonoBall InputSystem - Complete State Machine Analysis

**File Analyzed**: `/mnt/c/Users/nate0/RiderProjects/MonoBall/oldmonoball/MonoBallFramework.Game/Engine/Input/Systems/InputSystem.cs`

## Executive Summary

The old InputSystem implements a Pokemon Emerald-inspired state machine with three states (NotMoving, TurnDirection, Moving) and uses a 200ms input buffer for responsive controls. The system is highly optimized and correctly manages the separation between input handling, state transitions, and movement execution.

---

## 1. RunningState Transitions - Complete State Machine

### Three States (Pokemon Emerald Style)

```
enum RunningState {
    NotMoving = 0,      // Standing idle, no input detected
    TurnDirection = 1,  // Turning in place (animation playing)
    Moving = 2          // Actively moving between tiles
}
```

### State Transition Diagram

```
┌─────────────────────────────────────────────────────────────────┐
│                     INPUT SYSTEM                                 │
└─────────────────────────────────────────────────────────────────┘
                              │
                              ▼
              ┌───────────────────────────────┐
              │   Poll Input Once Per Frame   │
              │   (Keyboard + GamePad)        │
              └───────────────────────────────┘
                              │
                              ▼
              ┌───────────────────────────────┐
              │   Get Direction or None       │
              └───────────────────────────────┘
                              │
                    ┌─────────┴─────────┐
                    ▼                   ▼
          ┌─────────────────┐    ┌──────────────────┐
          │ Direction.None  │    │ Has Direction    │
          └─────────────────┘    └──────────────────┘
                    │                        │
                    ▼                        ▼
     ┌──────────────────────────┐   ┌──────────────────────────┐
     │ Lines 101-109:           │   │ Lines 112-176:           │
     │ if (dir == None)         │   │ Update Direction comp    │
     │   if (!IsMoving &&       │   │ Check turn needed?       │
     │       State != Turn)     │   │                          │
     │     State = NotMoving    │   └──────────┬───────────────┘
     └──────────────────────────┘              │
                                    ┌───────────┴───────────┐
                                    ▼                       ▼
                    ┌────────────────────────┐   ┌─────────────────────┐
                    │ Turn Needed?           │   │ No Turn Needed      │
                    │ Lines 124-142:         │   │ Lines 143-176:      │
                    │ if (dir !=             │   │                     │
                    │    MovementDirection   │   │                     │
                    │    && dir !=           │   │                     │
                    │    FacingDirection     │   │                     │
                    │    && State != Moving  │   │                     │
                    │    && !IsMoving)       │   │                     │
                    └────────┬───────────────┘   └──────┬──────────────┘
                             │                          │
                             ▼                          ▼
              ┌────────────────────────┐    ┌──────────────────────────┐
              │ START TURN IN PLACE    │    │ State = Moving           │
              │ State = TurnDirection  │    │ Buffer Input?            │
              │ FacingDir = dir        │    │ Lines 147-173            │
              │ DON'T update           │    └──────────┬───────────────┘
              │   MovementDirection    │               │
              └────────────────────────┘               │
                                                       ▼
                                        ┌─────────────────────────────┐
                                        │ Should Buffer?              │
                                        │ 1. !IsMoving OR             │
                                        │ 2. Dir changed              │
                                        │ AND not buffered recently   │
                                        └──────────┬──────────────────┘
                                                   │
                                        ┌──────────┴──────────┐
                                        ▼                     ▼
                           ┌────────────────────┐  ┌──────────────────┐
                           │ Buffer Input       │  │ Don't Buffer     │
                           │ (200ms timeout)    │  │ (duplicate)      │
                           └────────────────────┘  └──────────────────┘
                                        │
                                        ▼
                           ┌────────────────────────────────┐
                           │ Try Consume Buffer             │
                           │ Lines 187-218:                 │
                           │ if (!IsMoving &&               │
                           │     buffer has input)          │
                           │   Create MovementRequest       │
                           └────────────────────────────────┘
```

---

## 2. When RunningState is Set

### NotMoving (Lines 101-109)

**Condition**:
```csharp
if (currentDirection == Direction.None)
{
    if (!movement.IsMoving && movement.RunningState != RunningState.TurnDirection)
    {
        movement.RunningState = RunningState.NotMoving;
    }
}
```

**Triggers**:
- No input detected (all keys released)
- Entity is NOT mid-movement
- Entity is NOT currently turning in place

**Critical Insight**: Turn-in-place animations are NOT cancelled when the key is released. The animation must complete naturally.

### TurnDirection (Lines 124-142)

**Condition**:
```csharp
if (currentDirection != movement.MovementDirection &&
    currentDirection != movement.FacingDirection &&
    movement.RunningState != RunningState.Moving &&
    !movement.IsMoving)
{
    movement.StartTurnInPlace(currentDirection);
    // Inside StartTurnInPlace:
    //   RunningState = RunningState.TurnDirection;
    //   FacingDirection = direction;
    //   DON'T update MovementDirection
}
```

**Triggers**:
- Input direction differs from **MovementDirection** (last actual movement)
- Input direction differs from **FacingDirection** (current facing)
- NOT currently moving
- NOT already in Moving state

**Critical Design**: Compares against `MovementDirection` (not `FacingDirection`). This matches Pokemon Emerald behavior where `movementDirection` only updates when starting actual movement.

**Exception**: If already facing the input direction, skip turn and allow immediate movement.

### Moving (Lines 143-147)

**Condition**:
```csharp
else if (movement.RunningState != RunningState.TurnDirection)
{
    movement.RunningState = RunningState.Moving;
    // Then buffer input...
}
```

**Triggers**:
- Either already facing correct direction, OR
- Already moving in that direction
- NOT currently in turn-in-place state

**Critical**: The state is set to Moving BEFORE input is buffered. This allows continuous walking without repeated turn-in-place animations.

---

## 3. Input Buffering with State Machine

### Buffer Management (Lines 149-173)

```csharp
// Buffer input if:
// 1. Not currently moving (allows holding keys for continuous movement), OR
// 2. Direction changed (allows queuing direction changes during movement)
// But only if we haven't buffered this exact direction very recently

bool shouldBuffer = !movement.IsMoving || currentDirection != _lastBufferedDirection;

// Prevent buffering same direction multiple times per frame
bool isDifferentTiming = _totalTime != _lastBufferTime ||
                        currentDirection != _lastBufferedDirection;

if (shouldBuffer && isDifferentTiming)
{
    if (_inputBuffer.AddInput(currentDirection, _totalTime))
    {
        _lastBufferedDirection = currentDirection;
        _lastBufferTime = _totalTime;
    }
}
```

### Buffer Consumption (Lines 185-219)

```csharp
// Try to consume buffered input if not currently moving
if (!movement.IsMoving &&
    _inputBuffer.TryConsumeInput(_totalTime, out Direction bufferedDirection))
{
    // Reuse or create MovementRequest component
    if (entity.Has<MovementRequest>())
    {
        ref MovementRequest request = ref entity.Get<MovementRequest>();
        if (!request.Active)
        {
            request.Direction = bufferedDirection;
            request.Active = true;
        }
    }
    else
    {
        world.Add(entity, new MovementRequest(bufferedDirection));
    }
}
```

### InputBuffer Service

**Configuration**:
- Max buffer size: 5 inputs
- Buffer timeout: 0.2 seconds (200ms, Pokemon-style)

**Key Features**:
- Circular FIFO queue
- Automatic expiration of old inputs
- No duplicate prevention at buffer level (handled by InputSystem)

---

## 4. Relationship: IsMoving, RunningState, MovementRequest

### Critical Separation of Concerns

```
┌─────────────────────────────────────────────────────────────────┐
│                        INPUT SYSTEM                              │
│  - Polls input                                                   │
│  - Updates RunningState (NotMoving/TurnDirection/Moving)        │
│  - Buffers input                                                 │
│  - Creates MovementRequest when ready                            │
└─────────────────────────────┬───────────────────────────────────┘
                              │
                              ▼ MovementRequest (Active=true)
┌─────────────────────────────────────────────────────────────────┐
│                       MOVEMENT SYSTEM                            │
│  - Validates MovementRequest                                     │
│  - Checks collision                                              │
│  - Sets IsMoving = true when movement starts                     │
│  - Updates MovementProgress                                      │
│  - Sets IsMoving = false when complete                           │
│  - Manages turn-in-place completion (State = NotMoving)         │
└─────────────────────────────────────────────────────────────────┘
```

### Component Responsibilities

#### IsMoving (GridMovement.IsMoving)
- **Owner**: MovementSystem
- **Purpose**: Indicates entity is actively interpolating between tiles
- **Set to true**: When MovementSystem starts movement (line 111 in GridMovement.StartMovement)
- **Set to false**: When MovementSystem completes movement (line 138 in GridMovement.CompleteMovement)

#### RunningState (GridMovement.RunningState)
- **Owner**: InputSystem (primary) + MovementSystem (turn completion)
- **Purpose**: Pokemon-style state machine for input/animation coordination
- **Values**:
  - `NotMoving`: Set by InputSystem when no input detected
  - `TurnDirection`: Set by InputSystem when turn-in-place needed
  - `Moving`: Set by InputSystem when movement should occur
- **Turn completion**: MovementSystem sets to `NotMoving` when turn animation completes (line 343)

#### MovementRequest (Component)
- **Owner**: InputSystem (creates), MovementSystem (consumes)
- **Purpose**: Queue movement intent for validation
- **Active flag**: Acts as component pooling to avoid expensive ECS structural changes
- **Lifecycle**:
  1. InputSystem creates with `Active=true` when input buffered and entity not moving
  2. MovementSystem processes if `Active && !IsMoving && !MovementLocked && State != TurnDirection`
  3. MovementSystem sets `Active=false` after processing (component reuse)

---

## 5. State Machine Flow - Concrete Examples

### Example 1: Simple North Movement

```
Frame 1: Player presses UP
  - currentDirection = North
  - FacingDirection = South (default)
  - MovementDirection = South (default)
  - RunningState = NotMoving
  - IsMoving = false

  → Turn needed (North != South for both Facing and Movement)
  → RunningState = TurnDirection
  → FacingDirection = North
  → NO buffer input (turn-in-place doesn't buffer)

Frame 2-4: Turn animation playing
  - currentDirection = North (still held)
  - RunningState = TurnDirection
  - IsMoving = false

  → Turn still active, skip input processing

  [MovementSystem]: Turn animation completes
  → RunningState = NotMoving
  → Animation = idle_north

Frame 5: Still holding UP
  - currentDirection = North
  - FacingDirection = North
  - MovementDirection = South
  - RunningState = NotMoving
  - IsMoving = false

  → Turn NOT needed (North == FacingDirection, even though != MovementDirection)
  → RunningState = Moving
  → Buffer input: North (shouldBuffer=true: !IsMoving)
  → Consume buffer: Create MovementRequest(North)

Frame 6: MovementSystem processes
  - MovementRequest.Active = true
  - RunningState = Moving (blocks new turn-in-place)
  - IsMoving = false

  → Validate collision
  → StartMovement(North)
  → IsMoving = true
  → MovementDirection = North (updated when movement starts)
  → MovementRequest.Active = false

Frame 7-10: Movement interpolating
  - currentDirection = North (still held)
  - IsMoving = true
  - RunningState = Moving

  → Buffer input: North (shouldBuffer=false: direction unchanged)
  → NO consume (IsMoving=true blocks consumption)

Frame 11: Movement completes
  - MovementProgress >= 1.0

  [MovementSystem]:
  → IsMoving = false
  → Check for hasNextMovement (MovementRequest exists?)
  → If yes: Keep walk animation
  → If no: Switch to idle animation

Frame 12: Still holding UP
  - IsMoving = false
  - RunningState = Moving (still set)

  → Buffer input: North (shouldBuffer=true: !IsMoving)
  → Consume buffer: Create MovementRequest(North)
  → LOOP back to Frame 6
```

### Example 2: Direction Change During Movement

```
Frame 1-5: Moving North
  - IsMoving = true
  - RunningState = Moving
  - FacingDirection = North
  - MovementDirection = North

Frame 6: Player presses RIGHT (changes direction)
  - currentDirection = East
  - IsMoving = true (still moving north)
  - RunningState = Moving

  → Turn NOT needed (RunningState == Moving, skip turn logic)
  → RunningState = Moving (already set)
  → Buffer input: East (shouldBuffer=true: direction changed)

Frame 7-10: Still moving North
  - Buffered: [East] (200ms timeout)

Frame 11: North movement completes
  [MovementSystem]:
  → IsMoving = false
  → MovementProgress = 0

Frame 12: InputSystem next update
  - currentDirection = East (still held)
  - IsMoving = false
  - Buffered: [East]

  → Consume buffer: Create MovementRequest(East)

Frame 13: MovementSystem processes
  - currentDirection = East
  - FacingDirection = North (from previous movement)
  - MovementDirection = North

  → Turn needed? NO (RunningState = Moving, turn only checks if State != Moving)
  → Actually, InputSystem already set State=Moving when buffering
  → Validate East movement
  → StartMovement(East)
  → IsMoving = true
  → MovementDirection = East
  → FacingDirection = East
```

**Critical Insight**: During continuous movement, the system skips turn-in-place checks. The turn-in-place is ONLY for when standing still and changing direction.

### Example 3: Tap Key to Face Direction (No Movement)

```
Frame 1: Player taps UP (quick press)
  - currentDirection = North
  - FacingDirection = South
  - RunningState = NotMoving

  → Turn needed
  → RunningState = TurnDirection
  → FacingDirection = North
  → NO buffer input (turn-in-place doesn't buffer)

Frame 2: Player releases UP
  - currentDirection = None
  - RunningState = TurnDirection

  → Skip state change (RunningState == TurnDirection, don't set NotMoving)
  → Turn animation continues

Frame 3-4: Turn animation playing
  - currentDirection = None
  - RunningState = TurnDirection

Frame 5: Turn animation completes
  [MovementSystem]:
  → RunningState = NotMoving
  → Animation = idle_north

Frame 6: No input
  - currentDirection = None
  - RunningState = NotMoving

  → State = NotMoving (already set)
  → No buffer consumption

Result: Player faced North without moving
```

**Critical Insight**: The system allows "tap to turn" behavior by NOT buffering input during turn-in-place. If the key is released before the turn completes, no movement occurs.

---

## 6. Key Design Decisions

### 1. MovementDirection vs FacingDirection

**MovementDirection**:
- Only updated when actual movement starts (GridMovement.StartMovement line 116)
- Used for turn-in-place detection
- Represents the last direction the entity moved

**FacingDirection**:
- Updated immediately during turn-in-place (GridMovement.StartTurnInPlace line 154)
- Used for animation selection
- Represents which way the sprite is facing

**Why Both?**: Matches Pokemon Emerald's `ObjectEvent.movementDirection` behavior. Allows detecting when player wants to move in a different direction than their last actual movement, even if they're already facing that direction.

### 2. Turn-in-Place Behavior

**Key Implementation**:
- Compares input against `MovementDirection`, not `FacingDirection`
- Exception: If already facing the direction (`currentDirection == FacingDirection`), skip turn
- Does NOT buffer input during turn
- Turn completes in MovementSystem when animation finishes

**Result**: Tapping a direction key makes you face that direction. Holding it makes you move after turning.

### 3. Component Pooling for MovementRequest

**Problem**: Adding/removing components causes ECS archetype transitions (expensive, caused 186ms spikes)

**Solution**: Keep MovementRequest component on entity, use `Active` flag
- InputSystem sets `Active=true` when creating request
- MovementSystem checks `Active` before processing
- MovementSystem sets `Active=false` after processing

**Benefit**: Eliminates structural changes, reuses memory

### 4. Input Buffer Timing

**Buffering Conditions**:
1. Not currently moving (allows holding keys for continuous movement)
2. Direction changed (allows queuing direction changes during movement)
3. Not buffered this exact direction very recently (prevents duplicates)
4. Not same frame and direction (prevents frame duplication)

**Consumption**: Only when `!IsMoving`

**Result**: Responsive controls without input spam

---

## 7. Execution Order

```
PRIORITY 0: InputSystem (SystemPriority.Input)
  - Poll input
  - Update RunningState
  - Buffer input
  - Create MovementRequest

PRIORITY 90: MovementSystem
  - Process MovementRequest
  - Validate collision
  - Update IsMoving
  - Interpolate movement
  - Complete turn-in-place
```

**Critical**: InputSystem executes BEFORE MovementSystem every frame.

---

## 8. State Machine Invariants

### Guaranteed Conditions

1. **Turn-in-place blocks movement**: If `RunningState == TurnDirection`, MovementSystem ignores MovementRequest
2. **IsMoving blocks buffering consumption**: InputSystem only creates MovementRequest when `!IsMoving`
3. **No input = NotMoving**: Only if not mid-movement and not turning
4. **Turn animation completion**: MovementSystem sets `RunningState = NotMoving` when turn completes
5. **MovementDirection updates**: Only when actual movement starts, not during turn-in-place

### Never Possible

- `IsMoving=true` and `RunningState=NotMoving` simultaneously (movement sets State=Moving)
- `RunningState=TurnDirection` and `IsMoving=true` (turn blocks movement)
- Multiple MovementRequests active (consumption happens when !IsMoving)

---

## 9. Performance Optimizations

### Input Polling (Lines 77-80)
- Poll keyboard/gamepad ONCE per frame
- Cache states in fields
- Reuse across all player entities

### Query Caching (Lines 35-41)
```csharp
private readonly QueryDescription _playerQuery = QueryCache.Get<
    Player, Position, GridMovement, InputState, Direction
>();
```

### Component Pooling (Lines 193-210)
- Reuse MovementRequest component
- Avoid archetype transitions
- Check `Has<MovementRequest>()` before adding

### Duplicate Prevention (Lines 153-160)
- Track last buffered direction
- Track last buffer time
- Prevent same-frame duplicates

---

## 10. Critical Bugs to Avoid in New Implementation

### Bug 1: Cancelling Turn Animation on Key Release
**Wrong**:
```csharp
if (currentDirection == Direction.None && RunningState == TurnDirection)
{
    RunningState = NotMoving; // DON'T DO THIS
}
```

**Correct**: Lines 105-106
```csharp
if (!movement.IsMoving && movement.RunningState != RunningState.TurnDirection)
{
    movement.RunningState = RunningState.NotMoving;
}
```

### Bug 2: Comparing Against Wrong Direction
**Wrong**:
```csharp
if (currentDirection != FacingDirection) // Wrong comparison
{
    StartTurnInPlace();
}
```

**Correct**: Lines 125-126
```csharp
if (currentDirection != movement.MovementDirection &&
    currentDirection != movement.FacingDirection)
```

### Bug 3: Buffering During Turn-in-Place
**Wrong**:
```csharp
if (RunningState == TurnDirection)
{
    BufferInput(); // DON'T BUFFER
}
```

**Correct**: Lines 143-173 - Only buffer when `State != TurnDirection`

### Bug 4: Updating MovementDirection During Turn
**Wrong**:
```csharp
void StartTurnInPlace(Direction dir)
{
    FacingDirection = dir;
    MovementDirection = dir; // DON'T UPDATE
}
```

**Correct**: GridMovement.cs lines 151-156 - Only update FacingDirection

---

## 11. Summary State Machine Table

| State | Set By | When | IsMoving | Can Buffer? | Can Consume? |
|-------|--------|------|----------|-------------|--------------|
| **NotMoving** | InputSystem | No input detected AND not moving AND not turning | false | No | Yes |
| **TurnDirection** | InputSystem | Need to turn in place | false | No | No |
| **Moving** | InputSystem | Input detected, facing correct direction or already moving | false or true | Yes | Only if !IsMoving |

### Transitions

```
NotMoving → TurnDirection: Input != MovementDirection && != FacingDirection
TurnDirection → NotMoving: Animation completes (MovementSystem)
NotMoving → Moving: Input == FacingDirection (skip turn)
Moving → Moving: Continuous movement (hold key)
Moving → NotMoving: No input detected AND movement complete
```

---

## Conclusion

The old InputSystem is a well-designed, Pokemon Emerald-faithful implementation with:

1. **Clear state separation**: NotMoving, TurnDirection, Moving
2. **Smart buffering**: 200ms window, duplicate prevention
3. **Component pooling**: Performance optimization
4. **Turn-in-place logic**: Tap to turn, hold to move
5. **Responsive controls**: Buffer inputs during movement

The system correctly manages the relationship between:
- **IsMoving** (physics state, managed by MovementSystem)
- **RunningState** (input state, managed by InputSystem + MovementSystem)
- **MovementRequest** (intent queue, created by InputSystem, consumed by MovementSystem)

This architecture allows for smooth, responsive, Pokemon-style grid movement with proper turn-in-place behavior.
