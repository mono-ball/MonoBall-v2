# MonoBall Implementation Analysis - Verified from Source Code

## Executive Summary

After examining the MonoBall repository source code, I've identified **critical differences** between our design and MonoBall's actual implementation. This document details the exact MonoBall behavior that we must replicate.

---

## Key Findings

### ✅ What We Got Right

1. **Tile-based movement** - Confirmed ✓
2. **Direction enum** - Confirmed ✓ (North/South/East/West)
3. **Animation naming** - Confirmed ✓ (`face_*` and `go_*`)
4. **Event-driven architecture** - Confirmed ✓

### ❌ Critical Differences Found

1. **Input System Architecture** - MonoBall uses **InputBuffer** for queuing
2. **Movement Component** - MonoBall uses **GridMovement** (not VelocityComponent)
3. **Movement Request Pattern** - MonoBall uses **MovementRequest component** (not direct input)
4. **Turn-in-Place Behavior** - MonoBall has **RunningState.TurnDirection** state
5. **Position Component** - MonoBall uses **Position** with grid + pixel coordinates
6. **Animation Integration** - Animations are updated **inside MovementSystem**, not separate system

---

## Detailed MonoBall Implementation

### 1. Input System (`InputSystem.cs`)

#### Architecture
- **Location**: `Engine/Input/Systems/InputSystem.cs`
- **Update Priority**: `SystemPriority.Input` (0 - executes first)
- **Input Blocking**: Uses `IInputBlocker` service (checks `IsInputBlocked`)

#### Key Features

**Input Buffering**:
- Uses `InputBuffer` class for queuing inputs (200ms timeout, max 5 inputs)
- Buffers inputs when:
  - Not currently moving, OR
  - Direction changed (allows queuing direction changes during movement)
- Prevents duplicate buffering of same direction in same frame

**Input Processing Flow**:
1. Check if input blocked (`IInputBlocker.IsInputBlocked`)
2. Poll keyboard/gamepad state once per frame (cached)
3. Query for `Player + Position + GridMovement + InputState + Direction`
4. Get current input direction from keys/gamepad
5. Handle **turn-in-place** logic (Pokemon Emerald behavior)
6. Buffer input if appropriate
7. Consume buffered input → create `MovementRequest` component

**Turn-in-Place Logic** (CRITICAL):
```csharp
// If input direction != MovementDirection AND != FacingDirection AND not moving
if (currentDirection != movement.MovementDirection 
    && currentDirection != movement.FacingDirection
    && movement.RunningState != RunningState.Moving
    && !movement.IsMoving)
{
    // Start turn-in-place animation
    movement.StartTurnInPlace(currentDirection);
}
```

**Key Mappings**:
- North: Up/W
- South: Down/S
- East: Right/D
- West: Left/A
- Action: Space/Enter/Z or Gamepad A

**InputState Component**:
```csharp
public struct InputState
{
    public Direction PressedDirection { get; set; }
    public bool ActionPressed { get; set; }
    public float InputBufferTime { get; set; }
    public bool InputEnabled { get; set; }
}
```

---

### 2. Movement System (`MovementSystem.cs`)

#### Architecture
- **Location**: `GameSystems/Movement/MovementSystem.cs`
- **Update Priority**: 90 (before MapStreaming at 100)
- **Handles**: Movement requests, movement interpolation, animation updates

#### Key Features

**Movement Request Pattern**:
- `InputSystem` creates `MovementRequest` component
- `MovementSystem` processes `MovementRequest` components
- Uses **component pooling** - marks inactive instead of removing (performance optimization)
- Only processes if: `request.Active && !IsMoving && !MovementLocked && RunningState != TurnDirection`

**GridMovement Component**:
```csharp
public struct GridMovement
{
    public bool IsMoving { get; set; }
    public Vector2 StartPosition { get; set; }      // Pixel position
    public Vector2 TargetPosition { get; set; }     // Pixel position
    public float MovementProgress { get; set; }      // 0.0 to 1.0
    public float MovementSpeed { get; set; }        // Tiles per second (default 4.0)
    public Direction FacingDirection { get; set; }  // Which way sprite faces
    public Direction MovementDirection { get; set; } // Last actual movement direction
    public bool MovementLocked { get; set; }
    public RunningState RunningState { get; set; }
}
```

**RunningState Enum**:
```csharp
public enum RunningState
{
    NotMoving = 0,      // No input, not moving
    TurnDirection = 1,  // Turning in place (turn animation playing)
    Moving = 2         // Actively moving between tiles
}
```

**Movement Speed**:
- Default: **4.0 tiles per second**
- Stored in `GridMovement.MovementSpeed`
- Used for interpolation: `MovementProgress += MovementSpeed * deltaTime`

**Position Component**:
```csharp
public struct Position
{
    public int X { get; set; }           // Grid X coordinate
    public int Y { get; set; }           // Grid Y coordinate
    public float PixelX { get; set; }   // Interpolated pixel X
    public float PixelY { get; set; }   // Interpolated pixel Y
    public GameMapId? MapId { get; set; }
}
```

**Movement Processing**:
1. Process `MovementRequest` components first
2. Query for `Position + GridMovement` (with optional `Animation`)
3. If `IsMoving`:
   - Update `MovementProgress += MovementSpeed * deltaTime`
   - Interpolate pixel position: `Lerp(StartPosition, TargetPosition, Progress)`
   - If `Progress >= 1.0`: Snap to target, update grid coords, complete movement
   - Update animation: `go_{direction}` while moving
4. If not moving:
   - Sync pixel position to grid position
   - Handle turn-in-place state
   - Update animation: `face_{direction}` when idle

**Animation Updates** (CRITICAL):
- **Animations are updated INSIDE MovementSystem**, not separate system
- While moving: `go_{direction}` animation
- When idle: `face_{direction}` animation
- Turn-in-place: `go_fast_{direction}` with `PlayOnce=true`

**Turn-in-Place Handling**:
```csharp
if (movement.RunningState == RunningState.TurnDirection)
{
    // Play turn animation (go_fast_*) with PlayOnce=true
    animation.ChangeAnimation(turnAnimation, true, true);
    
    // When animation completes (IsComplete), transition to idle
    if (animation.IsComplete)
    {
        movement.RunningState = RunningState.NotMoving;
        animation.ChangeAnimation(movement.FacingDirection.ToIdleAnimation());
    }
}
```

---

### 3. Direction Component (`Direction.cs`)

#### Implementation
- **Location**: `Ecs/Components/Movement/Direction.cs`
- **Enum Values**: `None = -1`, `South = 0`, `West = 1`, `East = 2`, `North = 3`

#### Extension Methods (CRITICAL):
```csharp
public static class DirectionExtensions
{
    // Convert to tile delta
    public static (int deltaX, int deltaY) ToTileDelta(this Direction direction)
    
    // Animation name helpers
    public static string ToAnimationSuffix(this Direction direction)  // "north", "south", etc.
    public static string ToWalkAnimation(this Direction direction)     // "go_north"
    public static string ToIdleAnimation(this Direction direction)    // "face_north"
    public static string ToTurnAnimation(this Direction direction)    // "go_fast_north"
    
    // Utility
    public static Direction Opposite(this Direction direction)
}
```

---

### 4. Animation Component (`Animation.cs`)

#### Implementation
- **Location**: `Ecs/Components/Rendering/Animation.cs`
- **Name**: `Animation` (not `SpriteAnimationComponent`)

#### Structure:
```csharp
public struct Animation
{
    public string CurrentAnimation { get; set; }  // e.g., "face_south", "go_north"
    public int CurrentFrame { get; set; }
    public float FrameTimer { get; set; }
    public bool IsPlaying { get; set; }
    public bool IsComplete { get; set; }
    public bool PlayOnce { get; set; }  // For turn-in-place animations
    public ulong TriggeredEventFrames { get; set; }
}
```

#### Key Methods:
- `ChangeAnimation(string name, bool forceRestart, bool playOnce)`
- `Reset()`, `Pause()`, `Resume()`, `Stop()`

---

### 5. Player Component (`Player.cs`)

#### Implementation
- **Location**: `Ecs/Components/Player/Player.cs`
- **Type**: Tag component (empty struct)
- **Usage**: Used for queries to find player entity

```csharp
public struct Player;  // Tag component only
```

---

## Critical Behavior Details

### Turn-in-Place Behavior (Pokemon Emerald)

**When it happens**:
- Input direction differs from `MovementDirection` (last actual movement)
- Input direction differs from `FacingDirection` (current facing)
- Not currently moving
- Not already in turn state

**What happens**:
1. `RunningState = TurnDirection`
2. `FacingDirection = input direction` (updated immediately)
3. `MovementDirection` stays unchanged (last actual movement)
4. Animation changes to `go_fast_{direction}` with `PlayOnce=true`
5. When animation completes (`IsComplete`):
   - `RunningState = NotMoving`
   - Animation changes to `face_{direction}` (idle)

**Why it exists**:
- Matches Pokemon Emerald's `WALK_IN_PLACE_FAST` behavior
- Allows player to face direction without moving (tap key)
- Prevents immediate movement when changing direction

### Input Buffering

**Purpose**: Make movement feel responsive (Pokemon-style)

**How it works**:
- Inputs are buffered for 200ms
- Max 5 inputs in buffer
- Consumed when: `!IsMoving` (not currently moving)
- Allows queuing next movement before current completes

**When buffered**:
- Not currently moving, OR
- Direction changed (allows queuing direction changes)

**When consumed**:
- Creates `MovementRequest` component
- Only if not moving and not locked

### Movement Speed

- **Default**: 4.0 tiles per second
- **Calculation**: `MovementProgress += MovementSpeed * deltaTime`
- **Completion**: When `MovementProgress >= 1.0`

### Animation Synchronization

**While Moving**:
- Animation: `go_{direction}` (e.g., `go_north`)
- Only changes if direction changes (doesn't reset between consecutive tiles)

**When Idle**:
- Animation: `face_{direction}` (e.g., `face_north`)
- Changes when facing direction changes

**Turn-in-Place**:
- Animation: `go_fast_{direction}` with `PlayOnce=true`
- Transitions to `face_{direction}` when complete

---

## System Update Order

Based on MonoBall source code:

1. **InputSystem** (Priority 0) - Processes input, creates MovementRequest
2. **MovementSystem** (Priority 90) - Processes MovementRequest, updates movement, updates animations
3. **MapStreamingSystem** (Priority 100) - Handles map boundaries

**Note**: There is NO separate `PlayerMovementAnimationSystem` - animations are updated inside `MovementSystem`.

---

## Events Used

### MovementStartedEvent
- Published BEFORE movement validation
- Can be cancelled by handlers
- Contains: Entity, StartPosition, TargetPosition, Direction

### MovementCompletedEvent
- Published AFTER successful movement
- Contains: Entity, OldPosition, NewPosition, Direction, MapId, MovementTime

### MovementBlockedEvent
- Published when movement is blocked
- Contains: Entity, BlockReason, TargetPosition, Direction, MapId

**Event Pooling**: MonoBall uses `EventPool<T>` for performance (eliminates allocations)

---

## Component Structure Summary

### Player Entity Components:
- `Player` (tag)
- `Position` (grid + pixel coordinates)
- `GridMovement` (movement state, speed, progress)
- `InputState` (input state, buffering)
- `Direction` (current facing direction)
- `Animation` (optional - animation state)
- `Sprite` (optional - sprite rendering)
- `MovementRequest` (optional - pending movement request)

---

## What Our Design Needs to Change

### 1. ❌ Remove VelocityComponent
**Replace with**: `GridMovement` component (matches MonoBall exactly)

### 2. ❌ Remove PlayerMovementAnimationSystem
**Replace with**: Animation updates inside MovementSystem (matches MonoBall)

### 3. ❌ Change InputComponent
**Replace with**: `InputState` component (matches MonoBall naming and structure)

### 4. ❌ Add InputBuffer
**Add**: InputBuffer service for queuing inputs (200ms timeout, max 5)

### 5. ❌ Add MovementRequest Component
**Add**: `MovementRequest` component pattern (InputSystem creates, MovementSystem processes)

### 6. ❌ Add RunningState Enum
**Add**: `RunningState` enum (NotMoving, TurnDirection, Moving)

### 7. ❌ Change PositionComponent
**Replace with**: `Position` component with grid + pixel coordinates

### 8. ❌ Add Turn-in-Place Logic
**Add**: Turn-in-place behavior matching Pokemon Emerald

### 9. ❌ Add Direction Extensions
**Add**: Extension methods for animation name generation

### 10. ❌ Change Animation Component Name
**Rename**: `SpriteAnimationComponent` → `Animation` (matches MonoBall)

---

## Updated Design Requirements

### Components Needed:

1. **InputState** (not InputComponent)
   - `PressedDirection: Direction`
   - `ActionPressed: bool`
   - `InputBufferTime: float`
   - `InputEnabled: bool`

2. **GridMovement** (not VelocityComponent)
   - `IsMoving: bool`
   - `StartPosition: Vector2`
   - `TargetPosition: Vector2`
   - `MovementProgress: float`
   - `MovementSpeed: float` (default 4.0 tiles/second)
   - `FacingDirection: Direction`
   - `MovementDirection: Direction`
   - `MovementLocked: bool`
   - `RunningState: RunningState`

3. **Position** (not PositionComponent)
   - `X: int` (grid)
   - `Y: int` (grid)
   - `PixelX: float`
   - `PixelY: float`
   - `MapId: GameMapId?`

4. **MovementRequest**
   - `Direction: Direction`
   - `Active: bool`

5. **Direction** enum
   - `None = -1`
   - `South = 0`
   - `West = 1`
   - `East = 2`
   - `North = 3`
   - Extension methods for animation names

6. **RunningState** enum
   - `NotMoving = 0`
   - `TurnDirection = 1`
   - `Moving = 2`

7. **Animation** (not SpriteAnimationComponent)
   - `CurrentAnimation: string`
   - `CurrentFrame: int`
   - `FrameTimer: float`
   - `IsPlaying: bool`
   - `IsComplete: bool`
   - `PlayOnce: bool`

### Systems Needed:

1. **InputSystem**
   - Priority: 0 (first)
   - Uses InputBuffer for queuing
   - Handles turn-in-place logic
   - Creates MovementRequest components
   - Checks IInputBlocker for input blocking

2. **MovementSystem**
   - Priority: 90
   - Processes MovementRequest components
   - Updates movement interpolation
   - **Updates animations** (not separate system)
   - Handles turn-in-place completion
   - Publishes movement events

### Services Needed:

1. **InputBuffer**
   - Queues inputs for 200ms
   - Max 5 inputs
   - Methods: `AddInput()`, `TryConsumeInput()`, `TryPeekInput()`

2. **IInputBlocker**
   - Interface for checking if input is blocked
   - Used by InputSystem

---

## Animation Naming Convention (Verified)

- **Idle**: `face_{direction}` (e.g., `face_north`, `face_south`)
- **Walking**: `go_{direction}` (e.g., `go_north`, `go_south`)
- **Turn-in-place**: `go_fast_{direction}` (e.g., `go_fast_north`)

**Direction suffixes**: `north`, `south`, `east`, `west` (lowercase)

---

## Movement Speed (Verified)

- **Default**: 4.0 tiles per second
- **Calculation**: `MovementProgress += MovementSpeed * deltaTime`
- **Completion**: When `MovementProgress >= 1.0`

---

## Tile Size (Verified)

- **Default**: 16x16 pixels per tile
- Stored in `MapInfo.TileSize` component
- Used for grid ↔ pixel coordinate conversion

---

## Next Steps

1. **Update Design Document** with verified MonoBall implementation details
2. **Create Components** matching MonoBall exactly:
   - InputState
   - GridMovement
   - Position
   - MovementRequest
   - RunningState enum
   - Direction enum with extensions
3. **Create Systems** matching MonoBall:
   - InputSystem with InputBuffer
   - MovementSystem with animation updates
4. **Remove** incorrect components/systems:
   - VelocityComponent
   - PlayerMovementAnimationSystem
   - InputComponent (rename to InputState)
5. **Implement** turn-in-place behavior
6. **Implement** input buffering

---

## Conclusion

MonoBall's implementation is **significantly different** from our initial design assumptions. We must update our design to match MonoBall's exact behavior, especially:

1. **GridMovement** instead of VelocityComponent
2. **MovementRequest** component pattern
3. **InputBuffer** for queuing
4. **Turn-in-place** behavior
5. **Animation updates** inside MovementSystem
6. **RunningState** state machine

This ensures we replicate MonoBall's exact behavior while maintaining our improved architecture patterns.


