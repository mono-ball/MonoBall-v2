# Old MovementSystem Complete Analysis

## File: `/oldmonoball/MonoBallFramework.Game/GameSystems/Movement/MovementSystem.cs`

---

## COMPLETE MOVEMENT LIFECYCLE

### 1. REQUEST PHASE (Lines 483-512)

**ProcessMovementRequests()**
- Queries all entities with `MovementRequest` component
- Only processes if:
  - `request.Active == true`
  - `!movement.IsMoving`
  - `!movement.MovementLocked`
  - `movement.RunningState != RunningState.TurnDirection` (must wait for turn to complete)
- Calls `TryStartMovement()`
- Marks request as inactive: `request.Active = false`
- **CRITICAL**: Component is NOT removed, just marked inactive (component pooling pattern)

### 2. VALIDATION PHASE (Lines 514-836)

**TryStartMovement()**

#### A. Pre-Validation Event (Lines 567-631)
1. Publishes `MovementStartedEvent` BEFORE validation
2. Event can be cancelled by handlers (`startEvent.IsCancelled`)
3. If cancelled, publishes `MovementBlockedEvent` and returns

#### B. Boundary Check (Lines 633-664)
- `IsWithinMapBounds()` allows +1 tile outside bounds for map transitions
- If out of bounds, publishes `MovementBlockedEvent` and returns

#### C. Forced Movement Check (Lines 672-723)
- Checks current tile for `TileBehavior` component
- Can override direction with forced movement (e.g., ice tiles)
- Recalculates target position if direction overridden

#### D. Jump Behavior Check (Lines 725-814)
- Queries collision info once: `GetTileCollisionInfo()`
- Returns: `(isJumpTile, allowedJumpDir, isTargetWalkable)`
- If jump tile:
  - Only allows jump in specified direction
  - Calculates landing position (2 tiles in jump direction)
  - Validates landing position is walkable
  - Starts movement with `movement.StartMovement(jumpStart, jumpEnd, direction)`
  - **Updates grid position IMMEDIATELY**: `position.X = jumpLandX; position.Y = jumpLandY`
  - Returns (blocks all other directions)

#### E. Collision Check (Lines 816-822)
- Uses cached walkability from earlier `GetTileCollisionInfo()` call
- If blocked, returns

#### F. Start Movement (Lines 824-836)
- Calls `movement.StartMovement(startPixels, targetPixels, direction)`
- **Updates grid position IMMEDIATELY**: `position.X = targetX; position.Y = targetY`

### 3. MOVEMENT EXECUTION PHASE (Lines 199-358 with animation, 360-475 without)

**ProcessMovementWithAnimation()** - Main Update Loop

#### A. While Moving (`movement.IsMoving == true`)

**During Interpolation** (Lines 288-314):
1. Increments `movement.MovementProgress += movement.MovementSpeed * deltaTime`
2. Interpolates pixel position:
   ```csharp
   position.PixelX = MathHelper.Lerp(movement.StartPosition.X, movement.TargetPosition.X, movement.MovementProgress)
   position.PixelY = MathHelper.Lerp(movement.StartPosition.Y, movement.TargetPosition.Y, movement.MovementProgress)
   ```
3. Ensures walk animation is playing:
   - Gets expected animation: `movement.FacingDirection.ToWalkAnimation()`
   - Only changes if different: `if (animation.CurrentAnimation != expectedAnimation)`
   - Calls `animation.ChangeAnimation(expectedAnimation)` (doesn't force restart)

**On Completion** (`MovementProgress >= 1.0f`, Lines 213-287):
1. Stores old position: `(int oldX, int oldY) = (position.X, position.Y)`
2. Snaps to target:
   ```csharp
   movement.MovementProgress = 1.0f
   position.PixelX = movement.TargetPosition.X
   position.PixelY = movement.TargetPosition.Y
   ```
3. Recalculates grid coordinates from pixels (handles map boundary crossing):
   ```csharp
   position.X = (int)((position.PixelX - mapOffset.X) / tileSize)
   position.Y = (int)((position.PixelY - mapOffset.Y) / tileSize)
   ```
4. Calls `movement.CompleteMovement()` (see GridMovement.cs line 136-142):
   ```csharp
   IsMoving = false
   MovementProgress = 0f
   // RunningState is NOT reset - InputSystem manages it
   ```
5. **CRITICAL ANIMATION LOGIC** (Lines 239-248):
   - Checks if there's a pending `MovementRequest`: `hasNextMovement = world.Has<MovementRequest>(entity)`
   - If NO next movement: switches to idle `animation.ChangeAnimation(movement.FacingDirection.ToIdleAnimation())`
   - If next movement exists: **KEEPS WALK ANIMATION PLAYING** (prevents animation reset between consecutive tiles)
6. Publishes `MovementCompletedEvent` (Lines 250-286):
   - Uses pooled event from `_completedEventPool.Rent()`
   - Sets: Entity, OldPosition, NewPosition, Direction, MapId, MovementTime
   - Returns to pool: `_completedEventPool.Return(completedEvent)`

#### B. While Not Moving (`movement.IsMoving == false`, Lines 316-357)

**Idle State Management**:
1. Ensures pixel position matches grid position:
   ```csharp
   position.PixelX = (position.X * tileSize) + mapOffset.X
   position.PixelY = (position.Y * tileSize) + mapOffset.Y
   ```

2. **Turn-in-Place State** (Lines 329-346):
   - If `movement.RunningState == RunningState.TurnDirection`:
     - Plays turn animation: `movement.FacingDirection.ToTurnAnimation()`
     - Sets `PlayOnce=true`: `animation.ChangeAnimation(turnAnimation, true, true)`
     - Waits for animation completion: `if (animation.IsComplete)`
     - When complete:
       - Sets `movement.RunningState = RunningState.NotMoving`
       - Switches to idle: `animation.ChangeAnimation(movement.FacingDirection.ToIdleAnimation())`

3. **Normal Idle** (Lines 348-356):
   - Ensures idle animation is playing
   - Gets expected: `movement.FacingDirection.ToIdleAnimation()`
   - Changes if different: `animation.ChangeAnimation(expectedAnimation)`

---

## RUNNINGSTATE MODIFICATION

### Where RunningState is Set:

1. **GridMovement.CompleteMovement()** (GridMovement.cs:136-142):
   - Does NOT reset RunningState
   - Comment: "Don't reset RunningState - if input is still held, we want to skip turn-in-place"

2. **GridMovement.StartTurnInPlace()** (GridMovement.cs:151-157):
   - Sets `RunningState = RunningState.TurnDirection`
   - Sets `FacingDirection = direction`
   - Does NOT update MovementDirection (stays as last actual movement)

3. **MovementSystem Turn Complete** (MovementSystem.cs:341-345):
   - When turn animation completes
   - Sets `movement.RunningState = RunningState.NotMoving`

### Where RunningState is Checked:

1. **ProcessMovementRequests()** (Line 501):
   - Blocks movement if `movement.RunningState == RunningState.TurnDirection`
   - Must wait for turn animation to complete

2. **ProcessMovementWithAnimation()** (Line 329):
   - Special handling for `RunningState.TurnDirection`

3. **ProcessMovementNoAnimation()** (Line 470):
   - If turning without animation, completes immediately: `movement.RunningState = RunningState.NotMoving`

---

## MOVEMENTREQUEST LIFECYCLE

### Component Structure (MovementRequest.cs):
```csharp
public struct MovementRequest
{
    public Direction Direction { get; set; }
    public bool Active { get; set; }  // Pooling flag
}
```

### Lifecycle:
1. **Created**: By InputSystem (or other system) with `Active=true`
2. **Processed**: MovementSystem checks `Active` flag
3. **Consumed**: Set to `Active=false` after processing
4. **Pooled**: Component stays on entity for reuse (NOT removed)

### Performance Benefit:
- Comment (Line 481-482): "Uses component pooling - marks requests inactive instead of removing them. This eliminates expensive ECS archetype transitions that caused 186ms spikes."

---

## EVENT FIRING TIMELINE

### Event 1: MovementStartedEvent
- **When**: BEFORE validation (Line 567)
- **Purpose**: Allow handlers to cancel movement (cutscenes, menus, mods)
- **Can Cancel**: Yes (`ICancellableEvent`)
- **Data**: Entity, TargetPosition, StartPosition, Direction
- **Pooled**: Yes (`_startedEventPool`)

### Event 2: MovementBlockedEvent
- **When**: On any validation failure:
  - Event handler cancellation (Line 601)
  - Map boundary violation (Line 639)
  - Collision detected (implicit, via return)
- **Data**: Entity, BlockReason, TargetPosition, Direction, MapId
- **Pooled**: Yes (`_blockedEventPool`)

### Event 3: MovementCompletedEvent
- **When**: AFTER movement completes (Lines 250-286 and 402-438)
- **Purpose**: Notify that entity reached target position
- **Data**: Entity, OldPosition, NewPosition, Direction, MapId, MovementTime
- **Pooled**: Yes (`_completedEventPool`)
- **Note**: Published in BOTH `ProcessMovementWithAnimation()` and `ProcessMovementNoAnimation()`

---

## ANIMATION UPDATE MECHANISM

### Direct Component Modification (NOT via events)

**Critical Pattern** (Lines 124-138):
```csharp
if (world.TryGet(entity, out Animation animation))
{
    ProcessMovementWithAnimation(world, entity, ref position, ref movement, ref animation, deltaTime);

    // CRITICAL: Write modified animation back to entity
    // TryGet returns a COPY of the struct, so changes must be written back
    world.Set(entity, animation);
}
```

### Animation State Changes:

1. **During Movement** (Line 307-313):
   - `animation.ChangeAnimation(movement.FacingDirection.ToWalkAnimation())`
   - Only if different from current
   - Does NOT force restart (allows continuous walk animation)

2. **On Movement Complete** (Line 247):
   - If no next movement: `animation.ChangeAnimation(movement.FacingDirection.ToIdleAnimation())`
   - If next movement exists: KEEPS WALK ANIMATION

3. **Turn-in-Place** (Line 336):
   - `animation.ChangeAnimation(turnAnimation, forceRestart=true, playOnce=true)`
   - Waits for `animation.IsComplete`

4. **Idle** (Line 354):
   - `animation.ChangeAnimation(movement.FacingDirection.ToIdleAnimation())`
   - Only if different from current

### Animation Extensions (Assumed):
- `Direction.ToWalkAnimation()` - e.g., "go_north", "go_south"
- `Direction.ToIdleAnimation()` - e.g., "face_north", "face_south"
- `Direction.ToTurnAnimation()` - e.g., "go_north" with PlayOnce (walk-in-place)

---

## PERFORMANCE OPTIMIZATIONS

### 1. Component Pooling (Lines 481-482)
- MovementRequest stays on entity, just toggled Active flag
- Avoids expensive archetype transitions (186ms spikes eliminated)

### 2. Event Pooling (Lines 46-53)
- Cached static event pools: `EventPool<T>.Shared`
- Rent/Return pattern prevents allocations
- Critical for 100+ NPCs moving simultaneously

### 3. Single Query Pattern (Lines 119-150)
- Uses `TryGet()` for optional Animation component
- Replaced 2 separate queries (WITH/WITHOUT animation)
- 2x performance improvement from eliminating duplicate query setup

### 4. Map Offset Caching (Lines 158-174)
- `_mapWorldOffsetCache` - stable during gameplay
- NOT cleared per-frame
- Call `InvalidateMapWorldOffset()` when maps load/unload

### 5. Tile Size Caching (Lines 67, 845-868)
- `_tileSizeCache` per map
- Eliminates redundant MapInfo queries

### 6. Collision Query Optimization (Lines 725-736)
- Single `GetTileCollisionInfo()` call returns all data
- Returns tuple: `(isJumpTile, allowedJumpDir, isTargetWalkable)`
- Eliminates 2-3 separate spatial hash queries (6.25ms → 1.5ms, 75% reduction)

---

## KEY DESIGN PATTERNS

### 1. Immediate Grid Update, Smooth Pixel Interpolation
- Grid position updated IMMEDIATELY on movement start (Line 832)
- Pixel position interpolates smoothly for rendering (Lines 291-301)
- Prevents entities from passing through each other
- Rendering uses pixel position, collision uses grid position

### 2. Pokemon Emerald Turn-in-Place Behavior
- Uses `RunningState.TurnDirection`
- Plays walk animation once (`PlayOnce=true`)
- Blocks movement until turn completes
- Matches pokeemerald's WALK_IN_PLACE_FAST

### 3. Continuous Walk Animation
- Checks for pending MovementRequest before switching to idle (Line 242)
- If next movement exists, keeps walk animation playing
- Prevents animation reset between consecutive tiles
- Smoother visual experience

### 4. Separation of Concerns
- MovementRequest: Input/AI intent
- GridMovement: Movement state
- Position: Spatial data
- Animation: Visual state
- MovementSystem: Orchestration

### 5. Multi-Map Support
- All pixel calculations use map world offset
- Allows movement across map boundaries
- Map streaming handles boundary crossing

---

## COMPARISON WITH NEW SYSTEM

### What the Old System Does Well:

1. **Clear Event Timeline**: StartedEvent → CompletedEvent/BlockedEvent
2. **Animation Continuity**: Checks for next movement to keep walk animation
3. **Turn-in-Place Blocking**: Waits for turn animation to complete
4. **Component Pooling**: MovementRequest stays on entity
5. **Event Pooling**: Zero-allocation event publishing
6. **Immediate Grid Update**: Prevents collision issues
7. **Single Collision Query**: Optimized data fetching

### Potential Issues to Avoid:

1. **RunningState Management**: Must be set correctly
   - CompleteMovement() does NOT reset it
   - InputSystem is responsible for setting to NotMoving
   - Turn completion sets to NotMoving

2. **Animation Struct Copy**: Must write back after TryGet (Line 137)
   - `world.Set(entity, animation)` required
   - Forgot this = animation changes lost

3. **Next Movement Check**: Critical for continuous walking (Line 242)
   - Without this, animation resets between tiles
   - Causes jittery walk animation

4. **Turn State Blocking**: ProcessMovementRequests must check (Line 501)
   - If not checked, movement starts during turn
   - Causes animation conflicts

---

## SUMMARY

The old MovementSystem implements a complete Pokemon-style movement pipeline with:

1. **Request → Validation → Execution** lifecycle
2. **RunningState** managed across movement completion and turn-in-place
3. **MovementRequest** pooling for performance
4. **Event-based** notification with pooling
5. **Direct animation modification** via component writes
6. **Continuous walk animation** via next-movement detection
7. **Turn-in-place blocking** via RunningState check
8. **Optimized queries** and caching for performance

The system handles movement completion by:
- Setting `IsMoving = false`
- Keeping `RunningState` unchanged (InputSystem manages it)
- Checking for next movement to preserve walk animation
- Publishing `MovementCompletedEvent` for external systems
- Writing animation changes back to ECS world
