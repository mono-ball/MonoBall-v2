# Tile Interaction Script System Design

## Overview

This document describes the design for a tile interaction script system that enables Pokemon-style ledge jumps and other tile-based behaviors. The system addresses these architectural gaps:

1. **Movement Speed Configuration** - Different movement types need different speeds (e.g., ledge jumps are slower than walking). Speed is defined per-entity via named movement modes.
2. **Cross-Spritesheet Animation** - Ability to play animation X from spritesheet Y temporarily, then restore the original
3. **Time-Based Movement with Pixel Snapping** - Frame-rate independent movement with optional pixel-snapped rendering for crisp visuals
4. **Dual-Tile Collision** - Check both current tile (can exit?) and target tile (can enter?)
5. **Forced Movement** - Support for ice sliding, water currents, spin tiles, etc.

## Goals

- Enable tile scripts to control movement behavior (block, allow, modify, force)
- Support ledge jump mechanics with custom animations
- Allow temporary cross-spritesheet animation playback with automatic restoration
- **Entity movement modes** define speed/behavior (e.g., "walk", "ledgeJump") - animations are purely visual
- **Time-based movement** with configurable durations (frame-rate independent)
- **Sine-based jump easing** that closely matches Pokemon's lookup table curves
- **Dual-tile collision checking** (exit current tile + enter target tile)
- **Forced movement support** for ice, currents, spin tiles, and slopes
- Maintain compatibility with existing ECS architecture and event-driven design
- Keep implementation moddable and data-driven
- **Comply with .cursorrules**: components as structs, events for communication, DI required

## Reference: Pokemon Emerald Movement System

Our design is based on analysis of pokeemerald-expansion. Key concepts we're adopting (with adaptations for variable frame rates):

### Speed Levels & Duration (Adapted for Variable FPS)

Pokemon runs at fixed 60 FPS with discrete pixel-per-frame stepping. Since we need frame-rate independence, we convert Pokemon's frame counts to **durations in seconds**:

| Speed Level | Pokemon Frames | Duration (sec) | Equivalent Speed | Use Case |
|-------------|----------------|----------------|------------------|----------|
| NORMAL | 16 | 0.267 | 60 px/sec | Walking |
| FAST_1 | 8 | 0.133 | 120 px/sec | Running, Surfing |
| FAST_2 | 6 | 0.100 | 160 px/sec | Water current, Acro bike |
| FASTER | 4 | 0.067 | 240 px/sec | Mach bike |
| FASTEST | 2 | 0.033 | 480 px/sec | Max speed |

Each tile is 16 pixels. Movement uses **time-based interpolation** with optional **pixel snapping** for rendering.

### Dual-Tile Collision

Pokemon checks BOTH tiles when moving:
1. **Current tile**: Can entity EXIT in this direction? (`MB_IMPASSABLE_EAST` blocks eastward exit)
2. **Target tile**: Can entity ENTER from this direction? (`MB_IMPASSABLE_WEST` blocks entry from west)

### Collision Types

Pokemon returns specific collision types, not just allow/block:
- `COLLISION_NONE` - Movement allowed
- `COLLISION_LEDGE_JUMP` - Triggers jump instead of normal movement
- `COLLISION_IMPASSABLE` - Blocked by terrain
- `COLLISION_OBJECT_EVENT` - Blocked by NPC/object
- `COLLISION_ELEVATION_MISMATCH` - Different elevation layers

### Forced Movement

Pokemon supports tiles that override player input:
- **Ice**: Continue in current direction until hitting obstacle
- **Currents**: Push in specific direction regardless of input
- **Spin tiles**: Rotate player and push in rotation direction
- **Muddy slopes**: Slide down unless at max speed moving up

---

## Current Architecture Analysis

### Existing Components

| Component | Purpose | Relevant Fields |
|-----------|---------|-----------------|
| `GridMovement` | Movement state | `MovementSpeed`, `MovementProgress`, `StartPosition`, `TargetPosition` |
| `SpriteAnimationComponent` | Animation state | `CurrentAnimationName`, `IsPlaying`, `PlayOnce`, `IsComplete` |
| `SpriteSheetComponent` | Active spritesheet | `CurrentSpriteSheetId` |
| `SpriteComponent` | Sprite rendering | `SpriteId`, `FrameIndex` |

### Existing Code to Reuse

| Class | Reusable Methods |
|-------|------------------|
| `ScriptBase` | `GetParameterAsDirection()`, `GetParameterAsFloat()`, `GetParameterAsInt()`, `Context.GetParameter<T>()` |
| `EventBus` | `Send<T>()`, `Subscribe<T>()` for decoupled communication |
| `SpriteAnimationChangedEvent` | Existing event for animation changes |

### Identified Gaps

1. **No movement modes** - `GridMovement.MovementSpeed` is a fixed entity value
2. **No Pokemon-style easing** - Current system uses linear interpolation (missing jump arc curves)
3. **No dual-tile checking** - Only checks target tile, not current tile exit
4. **No collision type granularity** - Binary allow/block, no distinction between collision reasons
5. **No cross-spritesheet support** - `SpriteAnimationSystem` only reads from entity's current spritesheet
6. **No forced movement** - No mechanism for ice/current/spin tile behaviors
7. **No animation restoration** - No mechanism to restore previous animation/spritesheet after PlayOnce

---

## Proposed Solution

### Architecture Overview

```
┌─────────────────────────────────────────────────────────────────────────┐
│                           Movement Request                               │
└─────────────────────────────────────────────────────────────────────────┘
                                    │
                                    ▼
┌─────────────────────────────────────────────────────────────────────────┐
│                          MovementSystem                                  │
│  1. Check for forced movement on current tile                           │
│  2. If forced: override direction/block input                           │
│  3. Call ICollisionService.ResolveMovement()                            │
└─────────────────────────────────────────────────────────────────────────┘
                                    │
                                    ▼
┌─────────────────────────────────────────────────────────────────────────┐
│                    ICollisionService.ResolveMovement()                   │
│  ┌─────────────────────────────────────────────────────────────────┐    │
│  │ 1. Bounds check → COLLISION_OUT_OF_BOUNDS                       │    │
│  │ 2. CheckTileExit(currentTile, direction) → can exit?            │    │
│  │ 3. CheckTileEntry(targetTile, direction) → can enter?           │    │
│  │ 4. Elevation check → COLLISION_ELEVATION_MISMATCH               │    │
│  │ 5. Entity collision → COLLISION_OBJECT_EVENT                    │    │
│  │ 6. Ledge check → COLLISION_LEDGE_JUMP (special handling)        │    │
│  │ 7. Return CollisionResult with type and tile script data        │    │
│  └─────────────────────────────────────────────────────────────────┘    │
└─────────────────────────────────────────────────────────────────────────┘
                                    │
                                    ▼
┌─────────────────────────────────────────────────────────────────────────┐
│                         CollisionResult                                  │
│  - CollisionType (None, LedgeJump, Impassable, ObjectEvent, etc.)       │
│  - TileCheckResult (from target tile script)                            │
│  - ForcedMovement (from current tile - ice, current, spin)              │
│  - FinalTarget, MovementMode, Animation info                            │
└─────────────────────────────────────────────────────────────────────────┘
                                    │
                                    ▼
┌─────────────────────────────────────────────────────────────────────────┐
│                          MovementSystem                                  │
│  Based on CollisionType:                                                │
│  - NONE: Start normal movement                                          │
│  - LEDGE_JUMP: Start jump movement with arc                             │
│  - IMPASSABLE: Block, face direction                                    │
│  - Apply speed from IMovementModeService                                │
│  - Publish SetAnimationEvent                                            │
│  - Start pixel-perfect step movement                                    │
└─────────────────────────────────────────────────────────────────────────┘
                                    │
                                    ▼
┌─────────────────────────────────────────────────────────────────────────┐
│                    GridMovementSystem (Time-Based)                       │
│  - Update progress based on deltaTime and movement duration             │
│  - Interpolate position: lerp(start, target, progress)                  │
│  - If jumping: apply sine-based vertical offset                         │
│  - Optionally snap rendered position to nearest pixel                   │
│  - On completion: check for forced movement continuation                │
└─────────────────────────────────────────────────────────────────────────┘
```

---

## Component Designs

### 1. CollisionType Enum (New)

**File:** `MonoBall.Core/ECS/Components/CollisionType.cs`

**Purpose:** Granular collision results matching Pokemon's system.

```csharp
/// <summary>
/// Result of collision detection, determining how movement should be handled.
/// </summary>
public enum CollisionType : byte
{
    /// <summary>No collision, movement allowed.</summary>
    None = 0,

    /// <summary>Target is outside map bounds.</summary>
    OutOfBounds = 1,

    /// <summary>Terrain blocks movement (wall, obstacle).</summary>
    Impassable = 2,

    /// <summary>Current tile blocks exit in this direction.</summary>
    ExitBlocked = 3,

    /// <summary>Target tile blocks entry from this direction.</summary>
    EntryBlocked = 4,

    /// <summary>Ledge detected - triggers jump instead of normal movement.</summary>
    LedgeJump = 5,

    /// <summary>Blocked by another entity (NPC, object).</summary>
    ObjectEvent = 6,

    /// <summary>Elevation mismatch (different layers).</summary>
    ElevationMismatch = 7,

    /// <summary>Water tile but not surfing.</summary>
    WaterBlocked = 8,

    /// <summary>Special interaction required (door, sign, NPC talk).</summary>
    Interaction = 9
}
```

### 2. SpeedLevel Enum (New)

**File:** `MonoBall.Core/ECS/Components/SpeedLevel.cs`

**Purpose:** Discrete speed levels with associated durations for time-based movement.

```csharp
/// <summary>
/// Discrete speed levels for time-based movement.
/// Each level maps to a duration in seconds (based on Pokemon's 60 FPS timing).
/// </summary>
public enum SpeedLevel : byte
{
    /// <summary>0.267 sec per tile (Pokemon: 16 frames). Walking.</summary>
    Normal = 0,

    /// <summary>0.133 sec per tile (Pokemon: 8 frames). Running, surfing.</summary>
    Fast1 = 1,

    /// <summary>0.100 sec per tile (Pokemon: 6 frames). Currents, acro bike.</summary>
    Fast2 = 2,

    /// <summary>0.067 sec per tile (Pokemon: 4 frames). Mach bike.</summary>
    Faster = 3,

    /// <summary>0.033 sec per tile (Pokemon: 2 frames). Max speed.</summary>
    Fastest = 4
}

/// <summary>
/// Jump arc types with different peak heights.
/// Uses sine-based easing that closely matches Pokemon's lookup tables.
/// </summary>
public enum JumpType : byte
{
    /// <summary>No jump.</summary>
    None = 0,

    /// <summary>Low jump: 6px peak (small hops).</summary>
    Low = 1,

    /// <summary>Normal jump: 10px peak (ledge jumps).</summary>
    Normal = 2,

    /// <summary>High jump: 12px peak (secret base mats).</summary>
    High = 3
}

/// <summary>
/// Jump distance affecting total duration.
/// </summary>
public enum JumpDistance : byte
{
    /// <summary>Hop in place (0.267 sec, 0 tiles).</summary>
    InPlace = 0,

    /// <summary>Normal jump (0.267 sec, 1 tile).</summary>
    Normal = 1,

    /// <summary>Far jump (0.533 sec, 2 tiles).</summary>
    Far = 2
}
```

### 3. AvatarState Enum (New)

**File:** `MonoBall.Core/ECS/Components/AvatarState.cs`

**Purpose:** Tracks entity's current movement mode state (walking, biking, surfing). Based on Pokemon's `PLAYER_AVATAR_STATE` system.

```csharp
/// <summary>
/// Entity's current avatar/movement state.
/// Determines which animations and movement modes are used.
/// Based on Pokemon's PLAYER_AVATAR_FLAG system.
/// </summary>
public enum AvatarState : byte
{
    /// <summary>Walking on foot (default).</summary>
    OnFoot = 0,

    /// <summary>Riding Mach Bike (speed-focused).</summary>
    MachBike = 1,

    /// <summary>Riding Acro Bike (tricks-focused).</summary>
    AcroBike = 2,

    /// <summary>Surfing on water.</summary>
    Surfing = 3,

    /// <summary>Diving underwater.</summary>
    Underwater = 4,

    /// <summary>Using a field move (HM animation).</summary>
    FieldMove = 5
}

/// <summary>
/// Acro Bike sub-states for trick moves.
/// </summary>
public enum AcroBikeState : byte
{
    Normal = 0,
    Turning = 1,
    WheelieStanding = 2,
    BunnyHop = 3,
    WheelieMoving = 4
}
```

### 4. AvatarStateComponent (New)

**File:** `MonoBall.Core/ECS/Components/AvatarStateComponent.cs`

**Purpose:** Tracks entity's current avatar state for animation/mode resolution.

```csharp
/// <summary>
/// Tracks entity's current avatar state (walking, biking, surfing, etc.).
/// Used by IMovementBehaviorResolver to determine correct animations.
/// </summary>
public struct AvatarStateComponent
{
    /// <summary>Current avatar state.</summary>
    public AvatarState State { get; set; }

    /// <summary>Sub-state for Acro Bike tricks (only relevant when State == AcroBike).</summary>
    public AcroBikeState AcroBikeState { get; set; }

    /// <summary>Mach Bike speed level 0-2 (only relevant when State == MachBike).</summary>
    public byte MachBikeSpeedLevel { get; set; }
}
```

### 5. MovementBehavior Enum (New)

**File:** `MonoBall.Core/ECS/Components/MovementBehavior.cs`

**Purpose:** What behavior a tile requests - NOT how to animate it. The system resolves animation based on entity's AvatarState.

```csharp
/// <summary>
/// Movement behavior requested by tile scripts.
/// Tiles specify WHAT behavior, not HOW to animate it.
/// IMovementBehaviorResolver maps (AvatarState + Behavior + Direction) → (Mode, Animation).
/// </summary>
public enum MovementBehavior : byte
{
    /// <summary>Normal movement (walk/bike/surf based on avatar state).</summary>
    Normal = 0,

    /// <summary>Ledge jump - 2-tile jump with arc.</summary>
    LedgeJump = 1,

    /// <summary>Short hop - 1-tile jump with arc.</summary>
    Hop = 2,

    /// <summary>High jump - elevated arc (jump mats).</summary>
    HighJump = 3,

    /// <summary>Spin - rotate before moving.</summary>
    Spin = 4
}
```

### 6. ForcedMovementType Enum (New)

**File:** `MonoBall.Core/ECS/Components/ForcedMovementType.cs`

**Purpose:** Types of forced movement from tile effects.

```csharp
/// <summary>
/// Types of forced movement that override player input.
/// </summary>
public enum ForcedMovementType : byte
{
    /// <summary>No forced movement.</summary>
    None = 0,

    /// <summary>Ice - continue in current direction until obstacle.</summary>
    Slide = 1,

    /// <summary>Water current - push in tile's direction.</summary>
    Current = 2,

    /// <summary>Spin tile - rotate and push.</summary>
    Spin = 3,

    /// <summary>Muddy slope - slide down unless moving up at max speed.</summary>
    MuddySlope = 4,

    /// <summary>Conveyor belt - push in tile's direction.</summary>
    Conveyor = 5,

    /// <summary>
    /// Warp tile - teleport on step.
    /// NOTE: Unlike other forced movement types, Warp triggers an instant teleport
    /// via tile script's OnTileEnter handler, not a directional movement.
    /// The ForcedMovementComponent is used only to lock input during the transition.
    /// </summary>
    Warp = 6,

    /// <summary>Jump mat - automatic jump.</summary>
    JumpMat = 7
}
```

### 7. TileCheckResult (Updated)

**File:** `MonoBall.Core/Scripting/Tiles/TileCheckResult.cs`

**Purpose:** Pure data struct for tile script return values. Tiles specify BEHAVIOR, not animation - the system resolves animation based on entity's AvatarState.

```csharp
/// <summary>
/// Pure data struct - no methods per .cursorrules compliance.
/// Use TileCheckResults static class for factory methods.
///
/// IMPORTANT: Tiles specify MovementBehavior (WHAT to do), not animation details.
/// IMovementBehaviorResolver maps (AvatarState + Behavior + Direction) → animation/mode.
/// This allows bike+ledge, surf+ledge, etc. to work correctly.
/// </summary>
public struct TileCheckResult
{
    // === Movement Decision ===

    /// <summary>Whether movement onto this tile is allowed.</summary>
    public bool AllowMovement { get; set; }

    /// <summary>Whether this tile blocks EXIT in specific directions.</summary>
    public DirectionFlags BlockedExitDirections { get; set; }

    /// <summary>Whether this tile blocks ENTRY from specific directions.</summary>
    public DirectionFlags BlockedEntryDirections { get; set; }

    // === Movement Behavior (WHAT to do, not HOW to animate) ===

    /// <summary>
    /// Behavior type for this movement. The system resolves actual animation
    /// based on entity's AvatarState (walking → walk_jump, biking → bike_jump).
    /// </summary>
    public MovementBehavior Behavior { get; set; }

    /// <summary>If true, entity lands beyond the target tile (ledge jumps).</summary>
    public bool ExtendedMovement { get; set; }

    /// <summary>
    /// Final destination for extended movements (absolute grid coordinates).
    /// Use DirectionHelper.CalculateAbsolutePosition() to compute this value.
    /// </summary>
    public (int X, int Y) FinalDestination { get; set; }

    // === Forced Movement ===

    /// <summary>Type of forced movement on this tile.</summary>
    public ForcedMovementType ForcedMovement { get; set; }

    /// <summary>Direction of forced movement (for currents, conveyors).</summary>
    public Direction ForcedDirection { get; set; }

    /// <summary>If true, player cannot change direction while on this tile.</summary>
    public bool LockDirection { get; set; }

    /// <summary>If true, forced movement continues until obstacle hit.</summary>
    public bool ContinueUntilBlocked { get; set; }
}

/// <summary>
/// Flags for directional blocking.
/// </summary>
[Flags]
public enum DirectionFlags : byte
{
    None = 0,
    North = 1 << 0,
    South = 1 << 1,
    East = 1 << 2,
    West = 1 << 3,
    All = North | South | East | West
}
```

**File:** `MonoBall.Core/Scripting/Tiles/TileCheckResults.cs`

**Purpose:** Static factory class for generic TileCheckResult configurations. Game-specific behaviors (ledge jumps, ice, currents) are defined in tile scripts, not here.

```csharp
/// <summary>
/// Generic factory methods for TileCheckResult.
/// Game-specific tile behaviors are defined in tile scripts, NOT here.
/// </summary>
public static class TileCheckResults
{
    /// <summary>Allow movement with no modifications.</summary>
    public static TileCheckResult Allow() => new() { AllowMovement = true };

    /// <summary>Block movement completely.</summary>
    public static TileCheckResult Block() => new() { AllowMovement = false };

    /// <summary>Block exit in a specific direction.</summary>
    public static TileCheckResult BlockExit(Direction direction) => new()
    {
        AllowMovement = true,
        BlockedExitDirections = direction.ToFlag()
    };

    /// <summary>Block entry from a specific direction.</summary>
    public static TileCheckResult BlockEntry(Direction direction) => new()
    {
        AllowMovement = true,
        BlockedEntryDirections = direction.ToFlag()
    };
}

/// <summary>
/// Extension methods for Direction flags conversion.
/// NOTE: Other direction utilities are in DirectionExtensions (Direction.cs)
/// and DirectionHelper (Scripting/Utilities/DirectionHelper.cs). Use those:
/// - direction.Opposite() - get opposite direction (already exists)
/// - direction.ToTileDelta() - get (dx, dy) offset (already exists)
/// - DirectionHelper.GetDirectionTo() - direction from point to point (already exists)
/// - DirectionHelper.CalculateAbsolutePosition() - calculate landing position (add to existing)
/// </summary>
public static class DirectionFlagsExtensions
{
    /// <summary>
    /// Converts a Direction enum to DirectionFlags for blocking checks.
    /// </summary>
    public static DirectionFlags ToFlag(this Direction direction) => direction switch
    {
        Direction.North => DirectionFlags.North,
        Direction.South => DirectionFlags.South,
        Direction.East => DirectionFlags.East,
        Direction.West => DirectionFlags.West,
        _ => DirectionFlags.None
    };
}
```

**File:** `MonoBall.Core/Scripting/Utilities/DirectionHelper.cs` (**Update existing**)

**Purpose:** Add `CalculateAbsolutePosition()` to existing scripting utility class. This is used by tile scripts to calculate landing positions for extended movements like ledge jumps.

```csharp
// Add to existing DirectionHelper class:

/// <summary>
/// Calculates absolute destination position from a start position, direction, and distance.
/// Used by tile scripts for TileCheckResult.FinalDestination (ledge jumps, etc.).
/// </summary>
/// <param name="startPosition">Starting grid coordinates.</param>
/// <param name="direction">Movement direction.</param>
/// <param name="distance">Number of tiles to move.</param>
/// <returns>Absolute grid coordinates of destination.</returns>
public static (int X, int Y) CalculateAbsolutePosition(
    (int X, int Y) startPosition,
    Direction direction,
    int distance
)
{
    var (dx, dy) = direction.ToTileDelta();
    return (startPosition.X + dx * distance, startPosition.Y + dy * distance);
}
```

### 5. Grid Movement Components (Split for SRP)

The original monolithic GridMovement is refactored into focused, single-responsibility components per .cursorrules "Keep components small and focused".

> **⚠️ REFACTORING NOTE:** Existing `GridMovement` component (GridMovement.cs) has methods
> (`StartMovement()`, `CompleteMovement()`, `StartTurnInPlace()`) which violates .cursorrules
> "components are data-only, no methods". These methods must be moved to a static helper class
> `GridMovementHelper` or to `MovementSystem`. The existing `PositionComponent` also has
> `SyncPixelsToGrid()` method which should be moved to a helper.

**File:** `MonoBall.Core/ECS/Components/PositionComponent.cs` (**Unchanged** - keep existing)

```csharp
/// <summary>
/// Component that stores world position for entities in both grid and pixel coordinates.
/// Single responsibility: WHERE the entity currently IS.
/// </summary>
/// <remarks>
/// This component is UNCHANGED from existing implementation.
/// Movement interpolation state belongs in MovementStateComponent (SRP).
/// NOTE: SyncPixelsToGrid() method should be moved to PositionHelper static class per .cursorrules.
/// </remarks>
public struct PositionComponent
{
    /// <summary>Gets or sets the X grid coordinate (tile-based).</summary>
    public int X { get; set; }

    /// <summary>Gets or sets the Y grid coordinate (tile-based).</summary>
    public int Y { get; set; }

    /// <summary>Gets or sets the interpolated pixel X position for smooth rendering.</summary>
    public float PixelX { get; set; }

    /// <summary>Gets or sets the interpolated pixel Y position for smooth rendering.</summary>
    public float PixelY { get; set; }

    // NOTE: Existing Position property and SyncPixelsToGrid() method should be
    // moved to PositionHelper per .cursorrules (components are data-only).
}

/// <summary>
/// Static helper methods for PositionComponent operations.
/// Separated from struct to comply with .cursorrules (structs are data-only).
/// </summary>
public static class PositionHelper
{
    /// <summary>
    /// Syncs grid coordinates from pixel coordinates.
    /// Does NOT snap pixel coordinates - maintains smooth interpolation during movement.
    /// </summary>
    public static void SyncPixelsToGrid(ref PositionComponent pos, int tileWidth = 16, int tileHeight = 16)
    {
        pos.X = (int)(pos.PixelX / tileWidth);
        pos.Y = (int)(pos.PixelY / tileHeight);
    }
}
```

**File:** `MonoBall.Core/ECS/Components/MovementStateComponent.cs`

```csharp
/// <summary>
/// Movement interpolation state for grid-based movement.
/// Single responsibility: HOW the entity is moving (if at all).
/// </summary>
/// <remarks>
/// Contains all movement-related state including start/target positions.
/// PositionComponent stores WHERE the entity is; this stores movement progress.
/// </remarks>
public struct MovementStateComponent
{
    /// <summary>Whether entity is currently moving.</summary>
    public bool IsMoving { get; set; }

    /// <summary>Current movement direction.</summary>
    public Direction Direction { get; set; }

    /// <summary>Movement progress from 0.0 (start) to 1.0 (complete).</summary>
    public float Progress { get; set; }

    /// <summary>Total duration for this movement in seconds.</summary>
    public float Duration { get; set; }

    /// <summary>Current speed level (used to determine duration).</summary>
    public SpeedLevel SpeedLevel { get; set; }

    /// <summary>If true, snap rendered position to nearest pixel.</summary>
    public bool SnapToPixel { get; set; }

    // === Movement interpolation positions ===

    /// <summary>Start pixel X position (for interpolation).</summary>
    public float StartPixelX { get; set; }

    /// <summary>Start pixel Y position (for interpolation).</summary>
    public float StartPixelY { get; set; }

    /// <summary>Target grid X coordinate.</summary>
    public int TargetX { get; set; }

    /// <summary>Target grid Y coordinate.</summary>
    public int TargetY { get; set; }

    /// <summary>Target pixel X position (for interpolation).</summary>
    public float TargetPixelX { get; set; }

    /// <summary>Target pixel Y position (for interpolation).</summary>
    public float TargetPixelY { get; set; }
}

/// <summary>
/// Static helper methods for MovementStateComponent operations.
/// Separated from struct to comply with .cursorrules (structs are data-only).
/// </summary>
public static class MovementStateHelper
{
    /// <summary>
    /// Sets up movement from current position to target grid position.
    /// </summary>
    /// <param name="movement">The movement state to update.</param>
    /// <param name="position">The current position for start coordinates.</param>
    /// <param name="targetX">Target grid X coordinate.</param>
    /// <param name="targetY">Target grid Y coordinate.</param>
    /// <param name="direction">Movement direction.</param>
    /// <param name="duration">Movement duration in seconds.</param>
    /// <param name="tileWidth">Tile width from IConstantsService.</param>
    /// <param name="tileHeight">Tile height from IConstantsService.</param>
    public static void StartMovement(
        ref MovementStateComponent movement,
        ref PositionComponent position,
        int targetX,
        int targetY,
        Direction direction,
        float duration,
        int tileWidth,
        int tileHeight
    )
    {
        movement.IsMoving = true;
        movement.Direction = direction;
        movement.Progress = 0f;
        movement.Duration = duration;
        movement.StartPixelX = position.PixelX;
        movement.StartPixelY = position.PixelY;
        movement.TargetX = targetX;
        movement.TargetY = targetY;
        movement.TargetPixelX = targetX * tileWidth;
        movement.TargetPixelY = targetY * tileHeight;
    }

    /// <summary>
    /// Completes movement and resets state.
    /// </summary>
    public static void CompleteMovement(ref MovementStateComponent movement)
    {
        movement.IsMoving = false;
        movement.Progress = 0f;
    }
}
```

**File:** `MonoBall.Core/ECS/Components/ElevationComponent.cs` (**New**)

```csharp
/// <summary>
/// Elevation layer for entities (0-15).
/// Single responsibility: vertical layering for collision/rendering.
/// </summary>
/// <remarks>
/// Separated from PositionComponent for SRP - elevation affects collision
/// and rendering order, which is a different concern than 2D position.
/// </remarks>
public struct ElevationComponent
{
    /// <summary>Current elevation layer (0-15). Higher = renders on top.</summary>
    public byte Elevation { get; set; }
}
```

**File:** `MonoBall.Core/ECS/Components/JumpStateComponent.cs`

```csharp
/// <summary>
/// Jump arc state for entities performing jump movements.
/// Only add to entities during active jumps.
/// </summary>
public struct JumpStateComponent
{
    /// <summary>Jump type for sine-based vertical offset.</summary>
    public JumpType JumpType { get; set; }

    /// <summary>Jump distance affecting duration.</summary>
    public JumpDistance JumpDistance { get; set; }

    /// <summary>Peak height in pixels (cached from JumpType).</summary>
    public float PeakHeight { get; set; }
}
```

**File:** `MonoBall.Core/ECS/Components/ForcedMovementComponent.cs`

```csharp
/// <summary>
/// Forced movement state from tile effects (ice, currents, etc.).
/// Only add to entities under forced movement.
/// </summary>
public struct ForcedMovementComponent
{
    /// <summary>Active forced movement type.</summary>
    public ForcedMovementType Type { get; set; }

    /// <summary>Direction of forced movement.</summary>
    public Direction Direction { get; set; }

    /// <summary>If true, continue forced movement after current move completes.</summary>
    public bool ContinueUntilBlocked { get; set; }

    /// <summary>If true, entity cannot change facing direction.</summary>
    public bool LockDirection { get; set; }
}
```

**File:** `MonoBall.Core/ECS/Components/MapMembershipComponent.cs`

```csharp
/// <summary>
/// Identifies which map an entity belongs to.
/// Required for collision and tile script lookups.
/// </summary>
public struct MapMembershipComponent
{
    /// <summary>ID of the map this entity is on.</summary>
    public string MapId { get; set; }
}
```

**Query Examples:**

```csharp
// Moving entities (players, NPCs)
_movingQuery = new QueryDescription()
    .WithAll<GridPositionComponent, MovementStateComponent>();

// Entities currently jumping
_jumpingQuery = new QueryDescription()
    .WithAll<GridPositionComponent, MovementStateComponent, JumpStateComponent>();

// Entities under forced movement
_forcedMovementQuery = new QueryDescription()
    .WithAll<GridPositionComponent, MovementStateComponent, ForcedMovementComponent>();
```

### 6. MovementTimingService (New)

**File:** `MonoBall.Core/ECS/Services/IMovementTimingService.cs`

**Purpose:** Provides duration and easing for time-based movement.

```csharp
/// <summary>
/// Service for time-based movement timing and easing.
/// Durations derived from Pokemon Emerald's 60 FPS timing.
/// </summary>
public interface IMovementTimingService
{
    /// <summary>
    /// Gets the duration in seconds for a speed level (one tile of movement).
    /// </summary>
    float GetDuration(SpeedLevel level);

    /// <summary>
    /// Gets the duration in seconds for a jump distance.
    /// </summary>
    float GetJumpDuration(JumpDistance distance);

    /// <summary>
    /// Gets the peak height in pixels for a jump type.
    /// </summary>
    float GetJumpPeakHeight(JumpType type);

    /// <summary>
    /// Calculates vertical offset for jump arc using sine easing.
    /// </summary>
    /// <param name="progress">Movement progress from 0.0 to 1.0.</param>
    /// <param name="peakHeight">Maximum height of the jump arc.</param>
    /// <returns>Negative Y offset (upward) in pixels.</returns>
    float GetJumpOffset(float progress, float peakHeight);
}

/// <summary>
/// Implementation with Pokemon Emerald-equivalent timing.
/// </summary>
public class MovementTimingService : IMovementTimingService
{
    // Durations derived from Pokemon's 60 FPS: frames / 60 = seconds
    private static readonly float[] SpeedDurations =
    {
        16f / 60f,  // Normal:  0.267 sec
        8f / 60f,   // Fast1:   0.133 sec
        6f / 60f,   // Fast2:   0.100 sec
        4f / 60f,   // Faster:  0.067 sec
        2f / 60f    // Fastest: 0.033 sec
    };

    private static readonly float[] JumpDurations =
    {
        16f / 60f,  // InPlace: 0.267 sec
        16f / 60f,  // Normal:  0.267 sec
        32f / 60f   // Far:     0.533 sec
    };

    private static readonly float[] JumpPeakHeights =
    {
        0f,   // None
        6f,   // Low
        10f,  // Normal
        12f   // High
    };

    public float GetDuration(SpeedLevel level) => SpeedDurations[(int)level];

    public float GetJumpDuration(JumpDistance distance) => JumpDurations[(int)distance];

    public float GetJumpPeakHeight(JumpType type) => JumpPeakHeights[(int)type];

    /// <summary>
    /// Sine-based jump arc that closely matches Pokemon's lookup tables.
    /// Returns negative value (upward offset).
    /// </summary>
    public float GetJumpOffset(float progress, float peakHeight)
    {
        // Sine curve: peaks at progress=0.5, returns to 0 at progress=1.0
        // Very close match to Pokemon's hand-tuned lookup tables
        return -peakHeight * MathF.Sin(MathF.PI * progress);
    }
}
```

**Comparison: Sine vs Pokemon Lookup Tables:**

| Progress | Sine (10px) | Pokemon Normal | Difference |
|----------|-------------|----------------|------------|
| 0.0 | 0 | -2 | -2 |
| 0.125 | -3.8 | -4 | -0.2 |
| 0.25 | -7.1 | -8 | -0.9 |
| 0.375 | -9.2 | -9 | +0.2 |
| 0.5 | -10.0 | -10 | 0 |
| 0.625 | -9.2 | -9 | +0.2 |
| 0.75 | -7.1 | -6 | +1.1 |
| 0.875 | -3.8 | -2 | +1.8 |
| 1.0 | 0 | 0 | 0 |

The sine curve is very close to Pokemon's values, with maximum ~2px difference at landing phase. This is imperceptible in gameplay while being much simpler and frame-rate independent.

### 7. IMovementBehaviorResolver (New)

**File:** `MonoBall.Core/ECS/Services/IMovementBehaviorResolver.cs`

**Purpose:** Resolves tile behavior + entity state into concrete movement mode and animation. This is the key service that allows bike+ledge, surf+ledge, etc. to work correctly.

Based on Pokemon's pattern where `PlayerJumpLedge()` uses the player's current graphics (bike or walking) - the tile just signals "ledge jump" and the system figures out the right animation.

```csharp
/// <summary>
/// Result of behavior resolution - concrete mode and animation to use.
/// </summary>
public readonly struct ResolvedMovement
{
    /// <summary>Movement mode ID to use.</summary>
    public string ModeId { get; init; }

    /// <summary>Animation name to play.</summary>
    public string AnimationName { get; init; }

    /// <summary>Spritesheet ID (null = use entity's current).</summary>
    public string? SpriteSheetId { get; init; }
}

/// <summary>
/// Resolves tile behavior + entity avatar state into concrete movement/animation.
/// Tiles specify WHAT behavior (LedgeJump), this service determines HOW (bike_jump_south).
/// </summary>
public interface IMovementBehaviorResolver
{
    /// <summary>
    /// Resolves a movement behavior into concrete mode and animation.
    /// </summary>
    /// <param name="avatarState">Entity's current avatar state (walking, biking, surfing).</param>
    /// <param name="behavior">Behavior requested by tile.</param>
    /// <param name="direction">Movement direction.</param>
    /// <returns>Resolved movement mode and animation.</returns>
    ResolvedMovement Resolve(AvatarState avatarState, MovementBehavior behavior, Direction direction);

    /// <summary>
    /// Checks if a behavior is allowed for the given avatar state.
    /// Example: Surfing entities cannot perform ledge jumps.
    /// </summary>
    bool IsBehaviorAllowed(AvatarState avatarState, MovementBehavior behavior);
}
```

**Implementation (data-driven via DefinitionRegistry):**

```csharp
public class MovementBehaviorResolver : IMovementBehaviorResolver
{
    private readonly IDefinitionRegistry _definitions;

    public MovementBehaviorResolver(IDefinitionRegistry definitions)
    {
        _definitions = definitions;
    }

    public ResolvedMovement Resolve(AvatarState avatarState, MovementBehavior behavior, Direction direction)
    {
        // Build lookup key: "pokemon-emerald:behavior-mapping:onfoot-ledgejump"
        var mappingId = $"pokemon-emerald:behavior-mapping:{avatarState.ToString().ToLower()}-{behavior.ToString().ToLower()}";

        var mapping = _definitions.GetById<BehaviorMappingDefinition>(mappingId);
        if (mapping == null)
        {
            // Fallback to default walking behavior
            return GetDefaultMovement(direction);
        }

        // Resolve direction-specific animation
        var animationName = mapping.GetAnimationForDirection(direction);

        return new ResolvedMovement
        {
            ModeId = mapping.MovementModeId,
            AnimationName = animationName,
            SpriteSheetId = mapping.SpriteSheetId
        };
    }

    public bool IsBehaviorAllowed(AvatarState avatarState, MovementBehavior behavior)
    {
        // Surfing entities cannot ledge jump (would leave the water)
        if (avatarState == AvatarState.Surfing && behavior == MovementBehavior.LedgeJump)
            return false;

        // Underwater entities cannot do any surface behaviors
        if (avatarState == AvatarState.Underwater && behavior != MovementBehavior.Normal)
            return false;

        return true;
    }

    private ResolvedMovement GetDefaultMovement(Direction direction)
    {
        return new ResolvedMovement
        {
            ModeId = "base:movement-mode:walk",
            AnimationName = $"walk_{direction.ToString().ToLower()}",
            SpriteSheetId = null
        };
    }
}
```

**BehaviorMappingDefinition (JSON Definition):**

**File Location:** `Mods/pokemon-emerald/Definitions/Behaviors/BehaviorMappings/`

**Example: OnFoot + LedgeJump** (`onfoot-ledgejump.json`)
```json
{
  "id": "pokemon-emerald:behavior-mapping:onfoot-ledgejump",
  "name": "On Foot Ledge Jump",
  "description": "Ledge jump behavior when walking",
  "avatarState": "OnFoot",
  "behavior": "LedgeJump",
  "movementModeId": "pokemon-emerald:movement-mode:ledge-jump",
  "spriteSheetId": null,
  "animations": {
    "north": "jump_north",
    "south": "jump_south",
    "east": "jump_east",
    "west": "jump_west"
  }
}
```

**Example: MachBike + LedgeJump** (`machbike-ledgejump.json`)
```json
{
  "id": "pokemon-emerald:behavior-mapping:machbike-ledgejump",
  "name": "Mach Bike Ledge Jump",
  "description": "Ledge jump behavior when on Mach Bike",
  "avatarState": "MachBike",
  "behavior": "LedgeJump",
  "movementModeId": "pokemon-emerald:movement-mode:bike-ledge-jump",
  "spriteSheetId": "pokemon-emerald:spritesheet:player-mach-bike",
  "animations": {
    "north": "bike_jump_north",
    "south": "bike_jump_south",
    "east": "bike_jump_east",
    "west": "bike_jump_west"
  }
}
```

**Example: AcroBike + LedgeJump** (`acrobike-ledgejump.json`)
```json
{
  "id": "pokemon-emerald:behavior-mapping:acrobike-ledgejump",
  "name": "Acro Bike Ledge Jump",
  "description": "Ledge jump behavior when on Acro Bike (wheelie jump)",
  "avatarState": "AcroBike",
  "behavior": "LedgeJump",
  "movementModeId": "pokemon-emerald:movement-mode:acro-ledge-jump",
  "spriteSheetId": "pokemon-emerald:spritesheet:player-acro-bike",
  "animations": {
    "north": "wheelie_jump_north",
    "south": "wheelie_jump_south",
    "east": "wheelie_jump_east",
    "west": "wheelie_jump_west"
  }
}
```

**Resolution Flow:**

```
1. Tile script returns: TileCheckResult { Behavior = MovementBehavior.LedgeJump }

2. MovementSystem gets entity's AvatarStateComponent: State = MachBike

3. MovementSystem calls: resolver.Resolve(MachBike, LedgeJump, South)

4. Resolver looks up: "pokemon-emerald:behavior-mapping:machbike-ledgejump"

5. Returns: ResolvedMovement {
       ModeId = "pokemon-emerald:movement-mode:bike-ledge-jump",
       AnimationName = "bike_jump_south",
       SpriteSheetId = "pokemon-emerald:spritesheet:player-mach-bike"
   }

6. MovementSystem uses resolved values for SetAnimationEvent
```

### 8. MovementModeSettings (Updated)

**File:** `MonoBall.Core/ECS/Services/IMovementModeService.cs`

**Purpose:** Settings for time-based movement with optional duration override.

```csharp
/// <summary>
/// Settings for a movement mode.
/// Pure data struct - no methods per .cursorrules compliance.
/// Use MovementModeSettingsHelper for computed properties.
/// Use MovementModeSettingsFactory for preset configurations.
/// </summary>
public readonly struct MovementModeSettings
{
    /// <summary>Speed level (determines default duration via IMovementTimingService).</summary>
    public SpeedLevel SpeedLevel { get; init; }

    /// <summary>Optional duration override in seconds. If null, uses SpeedLevel default.</summary>
    public float? DurationOverride { get; init; }

    /// <summary>Jump type for sine-based vertical offset (None = no jump).</summary>
    public JumpType JumpType { get; init; }

    /// <summary>Jump distance affecting total duration.</summary>
    public JumpDistance JumpDistance { get; init; }

    /// <summary>If true, entity ignores collision during movement.</summary>
    public bool IgnoreCollision { get; init; }

    /// <summary>If true, snap rendered position to nearest pixel.</summary>
    public bool SnapToPixel { get; init; }
}

/// <summary>
/// Helper methods for MovementModeSettings.
/// Separated from struct to comply with .cursorrules (structs are data-only).
/// </summary>
public static class MovementModeSettingsHelper
{
    /// <summary>Whether this is a jump movement (derived from JumpType).</summary>
    public static bool IsJump(in MovementModeSettings settings) =>
        settings.JumpType != JumpType.None;
}

/// <summary>
/// Factory methods for creating preset MovementModeSettings.
/// Separated from struct to comply with .cursorrules (structs are data-only).
/// </summary>
public static class MovementModeSettingsFactory
{
    public static MovementModeSettings Default() => new()
    {
        SpeedLevel = SpeedLevel.Normal,
        JumpType = JumpType.None,
        JumpDistance = JumpDistance.Normal,
        IgnoreCollision = false,
        SnapToPixel = true  // Default to pixel-snapped rendering
    };

    public static MovementModeSettings Walk() => new()
    {
        SpeedLevel = SpeedLevel.Normal,  // 0.267 sec per tile
        JumpType = JumpType.None,
        JumpDistance = JumpDistance.Normal,
        IgnoreCollision = false,
        SnapToPixel = true
    };

    public static MovementModeSettings Run() => new()
    {
        SpeedLevel = SpeedLevel.Fast1,  // 0.133 sec per tile
        JumpType = JumpType.None,
        JumpDistance = JumpDistance.Normal,
        IgnoreCollision = false,
        SnapToPixel = true
    };

    public static MovementModeSettings LedgeJump() => new()
    {
        SpeedLevel = SpeedLevel.Fast1,
        JumpType = JumpType.Normal,      // 10px peak via sine easing
        JumpDistance = JumpDistance.Normal,  // 0.267 sec
        IgnoreCollision = true,
        SnapToPixel = true
    };

    public static MovementModeSettings HighJump() => new()
    {
        SpeedLevel = SpeedLevel.Fast1,
        JumpType = JumpType.High,        // 12px peak
        JumpDistance = JumpDistance.Normal,
        IgnoreCollision = true,
        SnapToPixel = true
    };

    public static MovementModeSettings FarJump() => new()
    {
        SpeedLevel = SpeedLevel.Fast1,
        JumpType = JumpType.Normal,
        JumpDistance = JumpDistance.Far,   // 0.533 sec, 2 tiles
        IgnoreCollision = true,
        SnapToPixel = true
    };
}
```

**JSON Configuration (Mod Definition Pattern):**

Movement modes are defined as mod definitions following the convention-based loading system.

**File Location:** `Mods/[mod-name]/Definitions/Behaviors/MovementModes/`

**Type Inference:** Directory path infers type as "MovementMode" (add to `KnownPathMappings.cs`)

**Example: Walk Mode** (`Mods/pokemon-emerald/Definitions/Behaviors/MovementModes/walk.json`)
```json
{
  "id": "pokemon-emerald:movement-mode:walk",
  "name": "Walk",
  "description": "Standard walking movement at normal speed",
  "speedLevel": "Normal",
  "jumpType": "None",
  "jumpDistance": "Normal",
  "ignoreCollision": false,
  "snapToPixel": true
}
```

**Example: Ledge Jump Mode** (`Mods/pokemon-emerald/Definitions/Behaviors/MovementModes/ledge-jump.json`)
```json
{
  "id": "pokemon-emerald:movement-mode:ledge-jump",
  "name": "Ledge Jump",
  "description": "Jump movement for ledge tiles with arc motion",
  "speedLevel": "Fast1",
  "jumpType": "Normal",
  "jumpDistance": "Normal",
  "ignoreCollision": true,
  "snapToPixel": true
}
```

**Example: Far Jump Mode** (`Mods/pokemon-emerald/Definitions/Behaviors/MovementModes/far-jump.json`)
```json
{
  "id": "pokemon-emerald:movement-mode:far-jump",
  "name": "Far Jump",
  "description": "Extended jump covering 2 tiles",
  "speedLevel": "Fast1",
  "jumpType": "Normal",
  "jumpDistance": "Far",
  "ignoreCollision": true,
  "snapToPixel": true
}
```

**Example: Custom Slow Mode** (`Mods/pokemon-emerald/Definitions/Behaviors/MovementModes/custom-slow.json`)
```json
{
  "id": "pokemon-emerald:movement-mode:custom-slow",
  "name": "Custom Slow",
  "description": "Slow movement with custom duration override",
  "speedLevel": "Normal",
  "durationOverride": 0.5,
  "jumpType": "None",
  "jumpDistance": "Normal",
  "ignoreCollision": false,
  "snapToPixel": true
}
```

**Entity Default Modes:**

Entity types reference movement modes by ID in their own definitions.

**Example: Player Entity Type** (`Mods/pokemon-emerald/Definitions/Entities/EntityTypes/player.json`)
```json
{
  "id": "pokemon-emerald:entity-type:player",
  "name": "Player",
  "description": "Controllable player character",
  "defaultMovementModeId": "pokemon-emerald:movement-mode:walk",
  "availableMovementModeIds": [
    "pokemon-emerald:movement-mode:walk",
    "pokemon-emerald:movement-mode:run",
    "pokemon-emerald:movement-mode:bike",
    "pokemon-emerald:movement-mode:surf",
    "pokemon-emerald:movement-mode:ledge-jump",
    "pokemon-emerald:movement-mode:far-jump"
  ]
}
```

**Loading via DefinitionRegistry:**

```csharp
// IMovementModeService implementation loads from DefinitionRegistry
public class MovementModeService : IMovementModeService
{
    private readonly IDefinitionRegistry _definitions;

    public MovementModeService(IDefinitionRegistry definitions)
    {
        _definitions = definitions;
    }

    public MovementModeSettings GetMode(string modeId)
    {
        var definition = _definitions.GetById<MovementModeDefinition>(modeId);
        return definition?.ToSettings() ?? MovementModeSettings.Default;
    }

    public string GetDefaultModeId(string entityTypeId)
    {
        var entityType = _definitions.GetById<EntityTypeDefinition>(entityTypeId);
        return entityType?.DefaultMovementModeId ?? "base:movement-mode:walk";
    }
}
```

**MovementModeDefinition Class:**

```csharp
/// <summary>
/// JSON definition for movement modes, loaded via mod system.
/// </summary>
public class MovementModeDefinition
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("speedLevel")]
    public SpeedLevel SpeedLevel { get; set; } = SpeedLevel.Normal;

    [JsonPropertyName("durationOverride")]
    public float? DurationOverride { get; set; }

    [JsonPropertyName("jumpType")]
    public JumpType JumpType { get; set; } = JumpType.None;

    [JsonPropertyName("jumpDistance")]
    public JumpDistance JumpDistance { get; set; } = JumpDistance.Normal;

    [JsonPropertyName("ignoreCollision")]
    public bool IgnoreCollision { get; set; }

    [JsonPropertyName("snapToPixel")]
    public bool SnapToPixel { get; set; } = true;

    public MovementModeSettings ToSettings() => new()
    {
        SpeedLevel = SpeedLevel,
        DurationOverride = DurationOverride,
        JumpType = JumpType,
        JumpDistance = JumpDistance,
        IgnoreCollision = IgnoreCollision,
        SnapToPixel = SnapToPixel
    };
}
```

**BehaviorMappingDefinition Class:**

```csharp
/// <summary>
/// JSON definition for mapping (AvatarState + Behavior + Direction) to concrete animation.
/// Loaded via mod system from Definitions/Behaviors/BehaviorMappings/.
/// </summary>
public class BehaviorMappingDefinition
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    /// <summary>Avatar state this mapping applies to (OnFoot, MachBike, etc.).</summary>
    [JsonPropertyName("avatarState")]
    public AvatarState AvatarState { get; set; } = AvatarState.OnFoot;

    /// <summary>Movement behavior this mapping handles (LedgeJump, Hop, etc.).</summary>
    [JsonPropertyName("behavior")]
    public MovementBehavior Behavior { get; set; } = MovementBehavior.Normal;

    /// <summary>Movement mode ID to use (references MovementModeDefinition).</summary>
    [JsonPropertyName("movementModeId")]
    public string MovementModeId { get; set; } = string.Empty;

    /// <summary>
    /// Animation name pattern with {direction} placeholder.
    /// Example: "jump_{direction}" becomes "jump_south" for Direction.South.
    /// </summary>
    [JsonPropertyName("animationPattern")]
    public string AnimationPattern { get; set; } = string.Empty;

    /// <summary>
    /// Optional spritesheet ID override (null = use entity's current spritesheet).
    /// Example: "pokemon-emerald:spritesheet:player-machbike" for bike animations.
    /// </summary>
    [JsonPropertyName("spriteSheetId")]
    public string? SpriteSheetId { get; set; }

    /// <summary>
    /// Whether this behavior is allowed for this avatar state.
    /// Example: Surfing + LedgeJump = false (can't jump ledges while surfing).
    /// </summary>
    [JsonPropertyName("allowed")]
    public bool Allowed { get; set; } = true;

    /// <summary>
    /// Resolves animation name by replacing {direction} placeholder.
    /// </summary>
    public string ResolveAnimationName(Direction direction)
    {
        return AnimationPattern.Replace("{direction}", direction.ToAnimationSuffix());
    }

    /// <summary>
    /// Converts to ResolvedMovement struct for use by MovementSystem.
    /// </summary>
    public ResolvedMovement ToResolvedMovement(Direction direction) => new()
    {
        ModeId = MovementModeId,
        AnimationName = ResolveAnimationName(direction),
        SpriteSheetId = SpriteSheetId
    };
}
```

### 8. ITileScript Interface (Updated)

**File:** `MonoBall.Core/Scripting/Tiles/ITileScript.cs`

**Purpose:** Updated to support both entry and exit checks.

```csharp
public interface ITileScript
{
    /// <summary>
    /// Called when entity attempts to ENTER this tile.
    /// </summary>
    TileCheckResult CheckEntry(
        Entity entity,
        (int X, int Y) currentPosition,
        (int X, int Y) targetPosition,
        Direction entryDirection,
        byte elevation
    );

    /// <summary>
    /// Called when entity attempts to EXIT this tile.
    /// </summary>
    TileCheckResult CheckExit(
        Entity entity,
        (int X, int Y) currentPosition,
        Direction exitDirection,
        byte elevation
    );

    /// <summary>
    /// Called each frame while entity is ON this tile.
    /// Used for forced movement checks.
    /// </summary>
    TileCheckResult CheckStanding(
        Entity entity,
        (int X, int Y) position,
        Direction facingDirection,
        byte elevation
    );

    /// <summary>Called after entity successfully enters tile.</summary>
    void OnTileEnter(Entity entity, (int X, int Y) position, Direction direction, byte elevation);

    /// <summary>Called when entity exits tile.</summary>
    void OnTileExit(Entity entity, (int X, int Y) position, Direction direction, byte elevation);
}
```

### 9. TileScriptBase (Updated)

**File:** `MonoBall.Core/Scripting/Tiles/TileScriptBase.cs`

**Purpose:** Base class with default implementations. Uses DirectionHelper for utilities (DRY - no duplicate methods).

```csharp
public abstract class TileScriptBase : ScriptBase, ITileScript
{
    // Default: allow all movement, no forced movement
    public virtual TileCheckResult CheckEntry(
        Entity entity,
        (int X, int Y) currentPosition,
        (int X, int Y) targetPosition,
        Direction entryDirection,
        byte elevation
    ) => TileCheckResults.Allow();

    public virtual TileCheckResult CheckExit(
        Entity entity,
        (int X, int Y) currentPosition,
        Direction exitDirection,
        byte elevation
    ) => TileCheckResults.Allow();

    public virtual TileCheckResult CheckStanding(
        Entity entity,
        (int X, int Y) position,
        Direction facingDirection,
        byte elevation
    ) => TileCheckResults.Allow();  // No forced movement by default

    public virtual void OnTileEnter(Entity entity, (int X, int Y) position, Direction direction, byte elevation) { }
    public virtual void OnTileExit(Entity entity, (int X, int Y) position, Direction direction, byte elevation) { }

    // NOTE: Use DirectionHelper for direction utilities (DRY principle):
    //   - DirectionHelper.CalculateAbsolutePosition(startPosition, direction, distance)
    //   - direction.Opposite()  // Already exists in DirectionExtensions
    //   - direction.ToFlag()
}
```

### 10. CollisionResult (New)

**File:** `MonoBall.Core/ECS/Services/CollisionResult.cs`

**Purpose:** Pure data struct for collision detection results. Factory methods in separate static class per .cursorrules.

```csharp
/// <summary>
/// Complete result from collision detection.
/// Pure data struct - no methods per .cursorrules compliance.
/// </summary>
public readonly struct CollisionResult
{
    /// <summary>Type of collision that occurred.</summary>
    public CollisionType CollisionType { get; init; }

    /// <summary>Target grid position.</summary>
    public (int X, int Y) TargetCoords { get; init; }

    /// <summary>Target pixel position.</summary>
    public (float X, float Y) TargetPixelPosition { get; init; }

    /// <summary>Final position for extended movements (ledge jumps). Absolute grid coordinates.</summary>
    public (int X, int Y) FinalCoords { get; init; }

    /// <summary>Final pixel position for extended movements.</summary>
    public (float X, float Y) FinalPixelPosition { get; init; }

    /// <summary>Forced movement from current tile.</summary>
    public ForcedMovementType ForcedMovement { get; init; }

    /// <summary>Direction of forced movement.</summary>
    public Direction ForcedDirection { get; init; }

    /// <summary>If true, forced movement continues until blocked.</summary>
    public bool ContinueUntilBlocked { get; init; }

    /// <summary>If true, direction is locked during forced movement.</summary>
    public bool LockDirection { get; init; }

    /// <summary>Movement behavior requested by tile (Normal, LedgeJump, etc.).</summary>
    public MovementBehavior Behavior { get; init; }

    /// <summary>Entity that blocked movement (if CollisionType == ObjectEvent).</summary>
    public Entity? BlockingEntity { get; init; }

    /// <summary>Jump type for arc calculation.</summary>
    public JumpType JumpType { get; init; }

    /// <summary>Jump distance for duration calculation.</summary>
    public JumpDistance JumpDistance { get; init; }
}

/// <summary>
/// Factory and helper methods for CollisionResult structs.
/// Separated from struct to comply with .cursorrules (structs are data-only).
/// </summary>
public static class CollisionResults
{
    /// <summary>Returns true if this is a jump movement.</summary>
    public static bool IsJump(in CollisionResult result) =>
        result.Behavior is MovementBehavior.LedgeJump or MovementBehavior.Hop or MovementBehavior.HighJump;

    /// <summary>Whether movement is allowed for the given collision type.</summary>
    public static bool CanMove(CollisionType type) =>
        type == CollisionType.None || type == CollisionType.LedgeJump;

    public static CollisionResult Blocked(CollisionType type, (int X, int Y) target) => new()
    {
        CollisionType = type,
        TargetCoords = target,
        FinalCoords = target
    };

    public static CollisionResult Success((int X, int Y) target, (float X, float Y) targetPixel) => new()
    {
        CollisionType = CollisionType.None,
        TargetCoords = target,
        TargetPixelPosition = targetPixel,
        FinalCoords = target,
        FinalPixelPosition = targetPixel
    };

    /// <summary>
    /// Creates CollisionResult from a TileCheckResult.
    /// </summary>
    /// <param name="targetCoords">Target grid coordinates.</param>
    /// <param name="targetPixel">Target pixel position.</param>
    /// <param name="tileResult">The tile check result.</param>
    /// <param name="tileWidth">Tile width from IConstantsService.</param>
    /// <param name="tileHeight">Tile height from IConstantsService.</param>
    public static CollisionResult FromTileResult(
        (int X, int Y) targetCoords,
        (float X, float Y) targetPixel,
        TileCheckResult tileResult,
        int tileWidth,
        int tileHeight
    )
    {
        var isJumpBehavior = tileResult.Behavior is MovementBehavior.LedgeJump
            or MovementBehavior.Hop or MovementBehavior.HighJump;

        return new()
        {
            CollisionType = isJumpBehavior ? CollisionType.LedgeJump : CollisionType.None,
            TargetCoords = targetCoords,
            TargetPixelPosition = targetPixel,
            FinalCoords = tileResult.ExtendedMovement ? tileResult.FinalDestination : targetCoords,
            FinalPixelPosition = tileResult.ExtendedMovement
                ? (tileResult.FinalDestination.X * tileWidth, tileResult.FinalDestination.Y * tileHeight)
                : targetPixel,
            ForcedMovement = tileResult.ForcedMovement,
            ForcedDirection = tileResult.ForcedDirection,
            ContinueUntilBlocked = tileResult.ContinueUntilBlocked,
            LockDirection = tileResult.LockDirection,
            Behavior = tileResult.Behavior
        };
    }
}
```

### 11. ICollisionService (Updated)

**File:** `MonoBall.Core/ECS/Services/ICollisionService.cs`

**Purpose:** Updated to return full CollisionResult with dual-tile checking.

```csharp
public interface ICollisionService
{
    /// <summary>
    /// Resolves movement with full collision detection.
    /// Checks: bounds, current tile exit, target tile entry, elevation, entities.
    /// </summary>
    CollisionResult ResolveMovement(
        Entity entity,
        (int X, int Y) currentCoords,
        Direction direction,
        string mapId,
        byte elevation
    );

    /// <summary>
    /// Checks for forced movement on current tile.
    /// Called each frame while entity is standing.
    /// </summary>
    TileCheckResult CheckForcedMovement(
        Entity entity,
        (int X, int Y) currentCoords,
        Direction facingDirection,
        string mapId,
        byte elevation
    );
}
```

### 12. Movement Events (New)

**File:** `MonoBall.Core/ECS/Events/MovementCompletedEvent.cs` (**Update existing**)

**Purpose:** Extend existing event with jump/behavior fields per .cursorrules. The existing event already has Entity, OldPosition, NewPosition, Direction, MapId, MovementTime - we add behavior-related fields.

```csharp
/// <summary>
/// Event fired when an entity completes movement.
/// Published AFTER successful movement.
/// </summary>
/// <remarks>
/// UPDATED: Added Behavior and WasJump fields for tile interaction system.
/// Existing fields (Entity, OldPosition, NewPosition, Direction, MapId, MovementTime) unchanged.
/// </remarks>
public struct MovementCompletedEvent
{
    // === Existing fields (unchanged) ===

    /// <summary>The entity that completed movement.</summary>
    public Entity Entity { get; set; }

    /// <summary>The old position (grid coordinates).</summary>
    public (int X, int Y) OldPosition { get; set; }

    /// <summary>The new position (grid coordinates).</summary>
    public (int X, int Y) NewPosition { get; set; }

    /// <summary>The movement direction.</summary>
    public Direction Direction { get; set; }

    /// <summary>The map identifier (optional).</summary>
    public string? MapId { get; set; }

    /// <summary>The time taken for movement (1.0 / MovementSpeed).</summary>
    public float MovementTime { get; set; }

    // === New fields for tile interaction ===

    /// <summary>The movement behavior that was executed (Normal, LedgeJump, etc.).</summary>
    public MovementBehavior Behavior { get; set; }

    /// <summary>If true, this was a jump movement (shorthand for Behavior == LedgeJump/Hop/HighJump).</summary>
    public bool WasJump { get; set; }
}

/// <summary>
/// Fired when an entity enters a new tile.
/// TileInteractionSystem publishes this after processing OnTileEnter callbacks.
/// </summary>
public struct TileEnteredEvent
{
    public Entity Entity { get; set; }
    public (int X, int Y) Position { get; set; }
    public Direction EntryDirection { get; set; }
    public string MapId { get; set; }
}

/// <summary>
/// Fired when an entity exits a tile.
/// TileInteractionSystem publishes this before processing OnTileExit callbacks.
/// </summary>
public struct TileExitedEvent
{
    public Entity Entity { get; set; }
    public (int X, int Y) Position { get; set; }
    public Direction ExitDirection { get; set; }
    public string MapId { get; set; }
}

/// <summary>
/// Fired when forced movement starts on an entity.
/// </summary>
public struct ForcedMovementStartedEvent
{
    public Entity Entity { get; set; }
    public ForcedMovementType Type { get; set; }
    public Direction Direction { get; set; }
}

/// <summary>
/// Fired when forced movement ends on an entity.
/// </summary>
public struct ForcedMovementEndedEvent
{
    public Entity Entity { get; set; }
    public ForcedMovementType Type { get; set; }
    public bool WasBlocked { get; set; }
}
```

### 13. Animation Events (Updated)

**File:** `MonoBall.Core/ECS/Events/AnimationEvents.cs`

```csharp
public struct SetAnimationEvent
{
    public Entity Entity { get; set; }
    public string AnimationName { get; set; }
    public string? SpriteSheetId { get; set; }
    public bool PlayOnce { get; set; }
    public string? RestoreAnimationName { get; set; }
    public string? RestoreSpriteSheetId { get; set; }
}

public struct AnimationRestorationRequestEvent
{
    public Entity Entity { get; set; }
}
```

### 14. AnimationRestorationComponent (Unchanged)

**File:** `MonoBall.Core/ECS/Components/AnimationRestorationComponent.cs`

```csharp
public struct AnimationRestorationComponent
{
    public string? RestoreSpriteSheetId { get; set; }
    public string? RestoreAnimationName { get; set; }
}
```

### 15. Sound Events (For Tile Scripts)

**File:** `MonoBall.Core/ECS/Events/SoundEvents.cs`

**Purpose:** Events for audio playback. Tile scripts should NOT access EventBus directly - use scripting API instead.

```csharp
public struct PlaySoundEvent
{
    public string SoundId { get; set; }
    public Entity? SourceEntity { get; set; }
    public float Volume { get; set; }
    public float Pitch { get; set; }
}
```

**Note:** Tile scripts should use the scripting API, not EventBus directly:
```csharp
// ❌ Wrong - direct EventBus access
EventBus.Send(new PlaySoundEvent { SoundId = "ice_slide" });

// ✅ Correct - use scripting API
Api.Audio.PlaySound("ice_slide");
```

### 16. IAudioApi (New)

**File:** `MonoBall.Core/Scripting/Api/IAudioApi.cs`

**Purpose:** Script-safe audio API for tile scripts. Wraps EventBus to provide clean API per .cursorrules (scripts use APIs, not direct system access).

```csharp
/// <summary>
/// Script API for audio playback.
/// Provides safe access to audio functionality for tile scripts.
/// </summary>
public interface IAudioApi
{
    /// <summary>
    /// Plays a sound effect by ID.
    /// </summary>
    /// <param name="soundId">The sound ID from sound definitions.</param>
    /// <param name="volume">Volume multiplier (0.0 to 1.0). Default: 1.0</param>
    /// <param name="pitch">Pitch multiplier. Default: 1.0</param>
    void PlaySound(string soundId, float volume = 1.0f, float pitch = 1.0f);

    /// <summary>
    /// Plays a sound effect at an entity's location.
    /// </summary>
    /// <param name="soundId">The sound ID from sound definitions.</param>
    /// <param name="sourceEntity">Entity to play sound from (for spatial audio).</param>
    /// <param name="volume">Volume multiplier (0.0 to 1.0). Default: 1.0</param>
    /// <param name="pitch">Pitch multiplier. Default: 1.0</param>
    void PlaySound(string soundId, Entity sourceEntity, float volume = 1.0f, float pitch = 1.0f);
}
```

**File:** `MonoBall.Core/Scripting/Api/AudioApiImpl.cs`

**Purpose:** Implementation that wraps EventBus internally.

```csharp
/// <summary>
/// Implementation of IAudioApi that sends PlaySoundEvent via EventBus.
/// Internal implementation detail - scripts interact via IAudioApi interface.
/// </summary>
internal class AudioApiImpl : IAudioApi
{
    public void PlaySound(string soundId, float volume = 1.0f, float pitch = 1.0f)
    {
        var evt = new PlaySoundEvent
        {
            SoundId = soundId,
            Volume = volume,
            Pitch = pitch
        };
        EventBus.Send(ref evt);
    }

    public void PlaySound(string soundId, Entity sourceEntity, float volume = 1.0f, float pitch = 1.0f)
    {
        var evt = new PlaySoundEvent
        {
            SoundId = soundId,
            SourceEntity = sourceEntity,
            Volume = volume,
            Pitch = pitch
        };
        EventBus.Send(ref evt);
    }
}
```

**Update to IScriptApiProvider:**

```csharp
// Add to IScriptApiProvider interface:
/// <summary>
/// Gets the audio API for sound playback.
/// </summary>
IAudioApi Audio { get; }
```

**Update to ScriptApiProvider:**

```csharp
// Add to ScriptApiProvider class:
private IAudioApi? _audioApi;

public IAudioApi Audio
{
    get
    {
        if (_audioApi == null)
            _audioApi = new AudioApiImpl();
        return _audioApi;
    }
}
```

---

## Data Flow Details

### Movement Request Flow

```
1. Player Input OR Forced Movement Timer
   └── MovementSystem receives input direction

2. MovementSystem.CheckForcedMovement() [FIRST]
   └── ICollisionService.CheckForcedMovement(entity, currentCoords, facing, mapId, elevation)
       └── ITileInteractionDispatcher.CheckStanding(currentTileScript, entity, ...)
           └── Returns TileCheckResult with ForcedMovement info
   └── If ForcedMovement != None:
       └── Override direction with ForcedDirection (or continue current direction for ice)

3. MovementSystem.ProcessMovement()
   └── ICollisionService.ResolveMovement(entity, currentCoords, direction, mapId, elevation)
       │
       ├── Step 1: Bounds Check
       │   └── If target out of bounds → return Blocked(OutOfBounds)
       │
       ├── Step 2: Current Tile Exit Check
       │   └── ITileInteractionDispatcher.CheckExit(currentTileScript, entity, direction)
       │   └── If BlockedExitDirections has direction → return Blocked(ExitBlocked)
       │
       ├── Step 3: Target Tile Entry Check
       │   └── ITileInteractionDispatcher.CheckEntry(targetTileScript, entity, direction)
       │   └── If BlockedEntryDirections has opposite(direction) → return Blocked(EntryBlocked)
       │   └── If IsJump → return Success with CollisionType.LedgeJump
       │
       ├── Step 4: Elevation Check
       │   └── If elevation mismatch → return Blocked(ElevationMismatch)
       │
       ├── Step 5: Entity Collision Check
       │   └── If entity at target → return Blocked(ObjectEvent, blockingEntity)
       │
       └── Step 6: Return Success with tile result data

4. MovementSystem handles CollisionResult
   │
   ├── If CanMove == false:
   │   └── Set entity facing direction (no movement)
   │
   ├── If CollisionType == LedgeJump:
   │   └── Use FinalCoords instead of TargetCoords
   │   └── Get "ledgeJump" mode from IMovementModeService
   │   └── Publish SetAnimationEvent with jump animation
   │
   ├── If CollisionType == None:
   │   └── Get mode from MovementMode or entity default
   │   └── Publish SetAnimationEvent with walk/run animation
   │
   └── Start time-based movement via GridMovement component

5. GridMovementSystem.Update(deltaTime) [Each Frame]
   │
   ├── Query entities with MovementStateComponent where IsMoving == true
   │
   ├── Update progress: Progress += deltaTime / Duration
   │
   ├── Interpolate position: lerp(StartPixelPosition, TargetPixelPosition, Progress)
   │   └── If has JumpStateComponent: add Y offset from IMovementTimingService.GetJumpOffset(Progress, PeakHeight)
   │
   ├── If SnapToPixel: round rendered position to nearest pixel
   │
   ├── If Progress >= 1.0:
   │   └── Movement complete, set Progress = 1.0
   │   └── Update GridPositionComponent.CurrentCoords to TargetCoords
   │   └── Remove JumpStateComponent if present
   │   └── Publish MovementCompletedEvent (decoupled via events!)
   │   └── Set IsMoving = false

6. TileInteractionSystem handles MovementCompletedEvent
   │
   ├── Get map from MapMembershipComponent
   │
   ├── Call ITileInteractionDispatcher.OnTileEnter(tileScript, entity, position, direction)
   │
   ├── Publish TileEnteredEvent
   │
   ├── Check for forced movement via ICollisionService.CheckForcedMovement()
   │   └── If ForcedMovement != None:
   │       └── Add/update ForcedMovementComponent
   │       └── Publish ForcedMovementStartedEvent
   │       └── Start next movement immediately
   │   └── Else if entity has ForcedMovementComponent:
   │       └── Remove ForcedMovementComponent
   │       └── Publish ForcedMovementEndedEvent

7. SpriteAnimationSystem handles SetAnimationEvent
   └── (unchanged from previous design)
```

### Ledge Jump Sequence (Detailed)

```
1. Player presses DOWN while on tile above ledge
   └── direction = Direction.South

2. ICollisionService.ResolveMovement()
   │
   ├── CheckExit(currentTile, South) → Allow
   │
   ├── CheckEntry(ledgeTile, South)
   │   └── LedgeJumpTileScript.CheckEntry() returns:
   │       TileCheckResult {
   │           AllowMovement = true,
   │           IsJump = true,
   │           ExtendedMovement = true,
   │           FinalDestination = (x, y + 2),  // Land 2 tiles south
   │           MovementMode = "ledgeJump",
   │           AnimationName = "jump_south",
   │           BlockedEntryDirections = North  // Can't climb back up
   │       }
   │
   └── Returns CollisionResult {
           CollisionType = LedgeJump,
           TargetCoords = (x, y + 1),      // Ledge tile
           FinalCoords = (x, y + 2),       // Landing tile
           TileResult = (above),
           MovementMode = "ledgeJump",
           AnimationName = "jump_south"
       }

3. MovementSystem processes LedgeJump
   │
   ├── Gets "ledgeJump" mode: { SpeedLevel = Fast1, JumpType = Normal, JumpDistance = Far }
   │   NOTE: JumpDistance.Far used for 2-tile jumps (landing 2 tiles from start)
   │
   ├── Gets duration from IMovementTimingService:
   │   - JumpDistance.Far → 0.533 sec (32 Pokemon frames at 60 FPS)
   │   - JumpType.Normal → 10px peak height
   │
   ├── Sets GridMovement:
   │   - IsMoving = true
   │   - IsJumping = true
   │   - JumpType = Normal
   │   - Duration = 0.533 sec (2 tiles at extended duration)
   │   - Progress = 0.0
   │   - TargetCoords = FinalCoords (skip ledge tile)
   │   - SnapToPixel = true
   │
   └── Publishes SetAnimationEvent { AnimationName = "jump_south", PlayOnce = true }

4. GridMovementSystem executes time-based movement with sine easing
   │
   │  Using IMovementTimingService.GetJumpOffset(progress, 10px):
   │  Y offset = -10 * sin(π * progress)
   │
   │  Example at 60 FPS (32 updates over 0.533 sec):
   │
   ├── t=0.00s (p=0.00): pos=lerp(start, target, 0.00), Y offset = 0px
   ├── t=0.03s (p=0.06): pos=lerp(start, target, 0.06), Y offset = -1.9px
   ├── t=0.07s (p=0.13): pos=lerp(start, target, 0.13), Y offset = -3.8px
   ├── t=0.10s (p=0.19): pos=lerp(start, target, 0.19), Y offset = -5.6px
   ├── t=0.13s (p=0.25): pos=lerp(start, target, 0.25), Y offset = -7.1px
   ├── t=0.17s (p=0.31): pos=lerp(start, target, 0.31), Y offset = -8.3px
   ├── t=0.20s (p=0.38): pos=lerp(start, target, 0.38), Y offset = -9.2px
   ├── t=0.23s (p=0.44): pos=lerp(start, target, 0.44), Y offset = -9.8px
   ├── t=0.27s (p=0.50): pos=lerp(start, target, 0.50), Y offset = -10px ← peak
   ├── t=0.30s (p=0.56): pos=lerp(start, target, 0.56), Y offset = -9.8px
   ├── t=0.33s (p=0.63): pos=lerp(start, target, 0.63), Y offset = -9.2px
   ├── t=0.37s (p=0.69): pos=lerp(start, target, 0.69), Y offset = -8.3px
   ├── t=0.40s (p=0.75): pos=lerp(start, target, 0.75), Y offset = -7.1px
   ├── t=0.43s (p=0.81): pos=lerp(start, target, 0.81), Y offset = -5.6px
   ├── t=0.47s (p=0.88): pos=lerp(start, target, 0.88), Y offset = -3.8px
   ├── t=0.50s (p=0.94): pos=lerp(start, target, 0.94), Y offset = -1.9px
   └── t=0.53s (p=1.00): pos=target, Y offset = 0px ← landed

   Total: 32px south (2 tiles) over 0.533 sec (frame-rate independent!)

5. Movement complete
   └── OnTileEnter(landingTile)
   └── Play landing sound
```

### Ice Sliding Sequence

```
1. Player moves onto ice tile
   └── OnTileEnter sets initial direction

2. Next frame: MovementSystem.CheckForcedMovement()
   └── IceTileScript.CheckStanding() returns:
       TileCheckResult {
           ForcedMovement = Slide,
           LockDirection = true,
           ContinueUntilBlocked = true
       }

3. MovementSystem overrides input
   └── Uses current facing direction (locked)
   └── Starts movement in that direction

4. Movement completes, still on ice
   └── ContinueForcedMovement = true
   └── Immediately start next movement (no player control)

5. Repeat until:
   └── Hit wall → stop, player regains control
   └── Exit ice → CheckForcedMovement returns None
```

---

## Example Tile Scripts

### Ledge Jump Script (ledge_jump.csx)

```csharp
public class LedgeJumpTileScript : TileScriptBase
{
    public override TileCheckResult CheckEntry(
        Entity entity,
        (int X, int Y) currentPosition,
        (int X, int Y) targetPosition,
        Direction entryDirection,
        byte elevation
    )
    {
        // Tile parameters configure direction and distance only
        // Animation/mode are resolved by IMovementBehaviorResolver based on entity's AvatarState
        var allowedDirection = GetParameterAsDirection("jumpDirection", Direction.South);
        var jumpDistance = GetParameterAsInt("jumpDistance", 1);

        // Allow jump only in configured direction
        if (entryDirection == allowedDirection)
        {
            // FinalDestination must be absolute coordinates
            var landingPosition = DirectionHelper.CalculateAbsolutePosition(
                targetPosition,
                entryDirection,
                jumpDistance
            );

            // NOTE: We specify BEHAVIOR (LedgeJump), not animation details.
            // IMovementBehaviorResolver will determine the correct animation
            // based on entity's AvatarState (walking → walk_jump, biking → bike_jump)
            return new TileCheckResult
            {
                AllowMovement = true,
                Behavior = MovementBehavior.LedgeJump,  // WHAT to do, not HOW
                ExtendedMovement = jumpDistance > 0,
                FinalDestination = landingPosition,
                BlockedEntryDirections = allowedDirection.Opposite().ToFlag()
            };
        }

        // Block entry from opposite direction (can't climb ledge)
        if (entryDirection == allowedDirection.Opposite())
        {
            return TileCheckResults.BlockEntry(entryDirection);
        }

        // Allow perpendicular movement
        return TileCheckResults.Allow();
    }
}
```

**How it works with different avatar states:**

| Player State | Tile Returns | Resolver Returns |
|--------------|--------------|------------------|
| Walking | `Behavior = LedgeJump` | `ModeId = "ledge-jump"`, `Animation = "jump_south"` |
| Mach Bike | `Behavior = LedgeJump` | `ModeId = "bike-ledge-jump"`, `Animation = "bike_jump_south"` |
| Acro Bike | `Behavior = LedgeJump` | `ModeId = "acro-ledge-jump"`, `Animation = "wheelie_jump_south"` |
| Surfing | `Behavior = LedgeJump` | **Blocked** - `IsBehaviorAllowed()` returns false |

### Ice Tile Script (ice_tile.csx)

```csharp
public class IceTileScript : TileScriptBase
{
    public override TileCheckResult CheckStanding(
        Entity entity,
        (int X, int Y) position,
        Direction facingDirection,
        byte elevation
    )
    {
        // All behavior defined here in the script, not hard-coded in framework
        return new TileCheckResult
        {
            AllowMovement = true,
            ForcedMovement = ForcedMovementType.Slide,
            LockDirection = true,
            ContinueUntilBlocked = true
        };
    }

    public override void OnTileEnter(Entity entity, (int X, int Y) position, Direction direction, byte elevation)
    {
        // Use scripting API for audio (NOT direct EventBus access per .cursorrules)
        Api.Audio.PlaySound("ice_slide");
    }
}
```

### Water Current Script (current_tile.csx)

```csharp
public class WaterCurrentTileScript : TileScriptBase
{
    public override TileCheckResult CheckStanding(
        Entity entity,
        (int X, int Y) position,
        Direction facingDirection,
        byte elevation
    )
    {
        // Current direction and speed configured via tile parameters
        var currentDirection = GetParameterAsDirection("direction", Direction.South);
        var movementMode = Context.GetParameter<string>("movementMode");

        return new TileCheckResult
        {
            AllowMovement = true,
            ForcedMovement = ForcedMovementType.Current,
            ForcedDirection = currentDirection,
            MovementMode = movementMode
        };
    }
}
```

### One-Way Gate Script (one_way_gate.csx)

```csharp
public class OneWayGateTileScript : TileScriptBase
{
    public override TileCheckResult CheckEntry(
        Entity entity,
        (int X, int Y) currentPosition,
        (int X, int Y) targetPosition,
        Direction entryDirection,
        byte elevation
    )
    {
        var allowedDirection = GetParameterAsDirection("allowedDirection", Direction.South);

        if (entryDirection == allowedDirection)
            return TileCheckResults.Allow();

        return TileCheckResults.BlockEntry(entryDirection);
    }

    public override TileCheckResult CheckExit(
        Entity entity,
        (int X, int Y) currentPosition,
        Direction exitDirection,
        byte elevation
    )
    {
        var allowedDirection = GetParameterAsDirection("allowedDirection", Direction.South);

        // Can only exit in the same direction as entry is allowed
        if (exitDirection == allowedDirection)
            return TileCheckResults.Allow();

        return TileCheckResults.BlockExit(exitDirection);
    }
}
```

---

## Files to Create/Modify

| File | Changes |
|------|---------|
| **Enums** | |
| `ECS/Components/CollisionType.cs` | **New** - Collision type enum |
| `ECS/Components/SpeedLevel.cs` | **New** - Speed level, JumpType, JumpDistance enums |
| `ECS/Components/AvatarState.cs` | **New** - AvatarState enum (OnFoot, MachBike, AcroBike, Surfing, etc.) |
| `ECS/Components/MovementBehavior.cs` | **New** - MovementBehavior enum (Normal, LedgeJump, Hop, etc.) |
| `ECS/Components/ForcedMovementType.cs` | **New** - Forced movement type enum |
| `ECS/Components/DirectionFlags.cs` | **New** - Direction flags for blocking + DirectionFlagsExtensions |
| **Position & Movement Components (SRP-compliant)** | |
| `ECS/Components/PositionComponent.cs` | **Unchanged** - Current position only (X, Y, PixelX, PixelY) |
| `ECS/Components/PositionHelper.cs` | **New** - Static helper (moved SyncPixelsToGrid from PositionComponent) |
| `ECS/Components/MovementStateComponent.cs` | **New** - Movement state + start/target positions for interpolation |
| `ECS/Components/MovementStateHelper.cs` | **New** - Static helper (StartMovement, CompleteMovement) |
| `ECS/Components/ElevationComponent.cs` | **New** - Elevation layer (0-15), separate from position |
| `ECS/Components/JumpStateComponent.cs` | **New** - Jump arc state (added during jumps) |
| `ECS/Components/ForcedMovementComponent.cs` | **New** - Forced movement state (ice, currents) |
| `ECS/Components/MapMembershipComponent.cs` | **New** - Map ID for entity |
| `ECS/Components/AvatarStateComponent.cs` | **New** - Tracks entity's avatar state for behavior resolution |
| `ECS/Components/GridMovement.cs` | **Refactor** - Move methods to GridMovementHelper static class |
| `ECS/Components/GridMovementHelper.cs` | **New** - Static helper (methods extracted from GridMovement) |
| **Other Components** | |
| `ECS/Components/AnimationRestorationComponent.cs` | **New** - Restoration state |
| **Events** | |
| `ECS/Events/AnimationEvents.cs` | **New** - SetAnimationEvent, AnimationRestorationRequestEvent |
| `ECS/Events/MovementCompletedEvent.cs` | **Update** - Add Behavior, WasJump fields (existing event extended) |
| `ECS/Events/TileEvents.cs` | **New** - TileEnteredEvent, TileExitedEvent |
| `ECS/Events/ForcedMovementEvents.cs` | **New** - ForcedMovementStartedEvent, ForcedMovementEndedEvent |
| `ECS/Events/SoundEvents.cs` | **New** - PlaySoundEvent |
| **Services** | |
| `ECS/Services/IMovementTimingService.cs` | **New** - Duration lookup and sine easing interface |
| `ECS/Services/MovementTimingService.cs` | **New** - Implementation with Pokemon-equivalent timing |
| `ECS/Services/IMovementBehaviorResolver.cs` | **New** - Resolves (AvatarState + Behavior + Direction) → (Mode, Animation) |
| `ECS/Services/MovementBehaviorResolver.cs` | **New** - Data-driven implementation via DefinitionRegistry |
| `ECS/Services/IMovementModeService.cs` | **Update** - Use SpeedLevel, JumpType, SnapToPixel |
| `ECS/Services/MovementModeSettings.cs` | **New** - Pure data struct (no methods per .cursorrules) |
| `ECS/Services/MovementModeSettingsHelper.cs` | **New** - Static helper with `IsJump()` method |
| `ECS/Services/MovementModeSettingsFactory.cs` | **New** - Static factory with `Default()`, `Walk()`, `LedgeJump()`, etc. |
| `ECS/Services/ICollisionService.cs` | **Update** - Return CollisionResult, check IsBehaviorAllowed |
| `ECS/Services/CollisionResult.cs` | **New** - Pure data struct (no methods per .cursorrules) |
| `ECS/Services/CollisionResults.cs` | **New** - Static factory with `IsJump()`, `FromTileResult()` (accepts tile dimensions from IConstantsService) |
| **Systems** | |
| `ECS/Systems/GridMovementSystem.cs` | **Update** - Time-based lerp, sine jump easing, use extended PositionComponent |
| `ECS/Systems/MovementSystem.cs` | **Update** - Use IMovementBehaviorResolver, forced movement, collision types |
| `ECS/Systems/TileInteractionSystem.cs` | **New** - Handle MovementCompletedEvent, publish tile events |
| `ECS/Systems/SpriteAnimationSystem.cs` | **Update** - Handle events, restoration |
| **Tile Scripting** | |
| `Scripting/Tiles/ITileScript.cs` | **New** - Entry/Exit/Standing check interface |
| `Scripting/Tiles/TileScriptBase.cs` | **New** - Base class with defaults |
| `Scripting/Tiles/TileCheckResult.cs` | **New** - Pure data struct with MovementBehavior (not animation) |
| `Scripting/Tiles/TileCheckResults.cs` | **New** - Static factory class + DirectionFlagsExtensions |
| `Scripting/Utilities/DirectionHelper.cs` | **Update** - (Existing) Add `CalculateAbsolutePosition()` method |
| `ECS/Services/ITileInteractionDispatcher.cs` | **Update** - Entry/Exit/Standing checks |
| **Scripting APIs** | |
| `Scripting/Api/IAudioApi.cs` | **New** - Audio API interface for tile scripts |
| `Scripting/Api/AudioApiImpl.cs` | **New** - Audio API implementation (wraps EventBus) |
| `Scripting/Api/IScriptApiProvider.cs` | **Update** - Add `IAudioApi Audio { get; }` property |
| `Scripting/ScriptApiProvider.cs` | **Update** - Add Audio property implementation |
| **Definitions (Mod System)** | |
| `Definitions/MovementModeDefinition.cs` | **New** - JSON definition class with `[JsonPropertyName]` attributes |
| `Definitions/BehaviorMappingDefinition.cs` | **New** - Maps (AvatarState + Behavior) → (Mode, Animation) with `[JsonPropertyName]` |
| `Definitions/EntityTypeDefinition.cs` | **Update** - Add `defaultMovementModeId`, `availableMovementModeIds` |
| `Mods/KnownPathMappings.cs` | **Update** - Add MovementMode and BehaviorMapping path mappings |
| `Mods/pokemon-emerald/Definitions/Behaviors/MovementModes/*.json` | **New** - Movement mode definitions (walk, bike-ledge-jump, etc.) |
| `Mods/pokemon-emerald/Definitions/Behaviors/BehaviorMappings/*.json` | **New** - Behavior mappings (onfoot-ledgejump, machbike-ledgejump, etc.) |
| `Mods/pokemon-emerald/Definitions/Entities/EntityTypes/player.json` | **Update** - Add movement mode references |

---

## Compliance Checklist

### .cursorrules Compliance

| Rule | Compliance |
|------|------------|
| Components are structs, data-only, no methods | ✅ All new components/structs are pure data; methods in static helpers |
| Factory methods in separate static classes | ✅ TileCheckResults, CollisionResults, MovementModeSettingsFactory, PositionHelper, MovementStateHelper, GridMovementHelper |
| Computed properties in helper classes | ✅ `CollisionResults.IsJump()`, `MovementModeSettingsHelper.IsJump()` instead of struct properties |
| Use events for system-to-system communication | ✅ MovementCompletedEvent (extended), TileEnteredEvent, SetAnimationEvent, etc. |
| Dependency injection required | ✅ IMovementModeService, IMovementTimingService, ICollisionService |
| No reference types in components | ✅ Using nullable strings (OK per existing patterns) |
| No allocations in hot paths | ✅ All structs, inline pixel math using IConstantsService tile dimensions, sine math |
| Keep components small and focused | ✅ SRP: PositionComponent (where), MovementStateComponent (how), ElevationComponent (layer) |
| Scripts use APIs, not direct system access | ✅ Tile scripts use Api.Audio (IAudioApi), not EventBus directly |
| No magic numbers | ✅ Tile dimensions from IConstantsService ("TileWidth", "TileHeight") instead of hardcoded 16 |

### SOLID/DRY Compliance

| Principle | Compliance |
|-----------|------------|
| Single Responsibility | ✅ Position extended, movement state separated, jump state optional |
| Open/Closed | ✅ Tile scripts extend TileScriptBase; new tile behaviors don't modify framework |
| Liskov Substitution | ✅ IMovementBehaviorResolver can be replaced with custom implementations |
| DRY | ✅ Reuses existing: DirectionExtensions.Opposite(), PositionComponent, MovementCompletedEvent |
| Interface Segregation | ✅ ITileScript methods optional via base class defaults |

### DRY: Reusing Existing Code

| Existing Code | How Reused |
|---------------|------------|
| `DirectionExtensions.Opposite()` | Used instead of creating new `GetOpposite()` |
| `DirectionExtensions.ToTileDelta()` | Used in `DirectionHelper.CalculateAbsolutePosition()` |
| `DirectionHelper` (Scripting/Utilities) | Extended with `CalculateAbsolutePosition()` method |
| `PositionComponent` | Extended with target fields instead of new GridPositionComponent |
| `MovementCompletedEvent` | Extended with Behavior/WasJump fields |
| `Direction` enum | Reused, added `ToFlag()` extension for DirectionFlags |
| `IConstantsService` | Used for tile dimensions ("TileWidth", "TileHeight") instead of new TileConstants class |
| `IScriptApiProvider` | Extended with `IAudioApi Audio` property for tile script audio playback |

### Refactoring Notes (Existing Code Violations)

| File | Issue | Fix |
|------|-------|-----|
| `GridMovement.cs` | Has methods (StartMovement, CompleteMovement) - violates .cursorrules | Move to `GridMovementHelper` static class |
| `PositionComponent.cs` | Has `SyncPixelsToGrid()` method - violates .cursorrules | Move to `PositionHelper` static class |

### Behavior Resolution Pattern (Pokemon-Style)

| Pattern | Compliance |
|---------|------------|
| Tiles signal WHAT, not HOW | ✅ TileCheckResult.Behavior = MovementBehavior.LedgeJump (not animation name) |
| State-aware animation | ✅ IMovementBehaviorResolver maps (AvatarState + Behavior) → Animation |
| Bike + ledge works | ✅ Same tile script works for walking, Mach Bike, Acro Bike |
| Surf + ledge blocked | ✅ IsBehaviorAllowed() prevents invalid combinations |
| Data-driven mappings | ✅ BehaviorMappingDefinition JSON files (moddable) |
| Per-state spritesheets | ✅ ResolvedMovement includes SpriteSheetId |

### Mod System Compliance

| Pattern | Compliance |
|---------|------------|
| Convention-based file location | ✅ `Definitions/Behaviors/MovementModes/` and `BehaviorMappings/` |
| ID format | ✅ `modId:definition-type:name` (e.g., `pokemon-emerald:behavior-mapping:machbike-ledgejump`) |
| Required metadata fields | ✅ `id`, `name`, `description` on all definitions |
| DefinitionRegistry loading | ✅ `GetById<BehaviorMappingDefinition>()` for typed access |
| Foreign key references | ✅ BehaviorMappings reference movement modes and spritesheets by ID |
| JsonPropertyName attributes | ✅ All definition properties use `[JsonPropertyName]` |
| Moddable/extensible | ✅ New avatar states and behaviors via JSON, no code changes |

### Pokemon Emerald Compatibility

| Feature | Status | Notes |
|---------|--------|-------|
| Movement timing | ✅ Equivalent durations | Time-based (0.267s) = Pokemon's 16 frames at 60 FPS |
| Jump arc shape | ✅ Sine easing ≈ lookup | ~2px max difference, imperceptible |
| Dual-tile collision | ✅ CheckEntry + CheckExit | Same logic as Pokemon |
| Collision types | ✅ CollisionType enum | Matches Pokemon's collision results |
| Ledge jumps | ✅ Extended movement | Sine arc instead of lookup table |
| Ice sliding | ✅ ForcedMovement.Slide | Same behavior |
| Water currents | ✅ ForcedMovement.Current | Same behavior |
| Direction blocking | ✅ DirectionFlags | Same entry/exit blocking |
| Elevation layers | ✅ Elevation in checks | Same 0-15 range |
| Avatar state tracking | ✅ AvatarStateComponent | Matches Pokemon's PLAYER_AVATAR_FLAG |
| Bike + ledge jumps | ✅ IMovementBehaviorResolver | Same pattern as PlayerJumpLedge() |
| Acro Bike sub-states | ✅ AcroBikeState enum | BunnyHop, Wheelie, etc. |
| Variable FPS | ✅ Frame-rate independent | **Enhancement** over Pokemon |
| Pixel snapping | ✅ Optional per-mode | Can match Pokemon's crisp look |

---

## Resolved Design Decisions

### 1. Time-Based Movement (NOT Frame-Based)

**Pokemon's approach:** Fixed 60 FPS with pixel-per-frame stepping and lookup tables.

**Our adaptation:** Time-based movement with delta time interpolation for frame-rate independence.

| Aspect | Pokemon | Our Design |
|--------|---------|------------|
| FPS dependency | Fixed 60 FPS | Any FPS (30, 60, 120, 144) |
| Position update | N pixels per frame | lerp(start, target, progress) |
| Progress tracking | Frame counter (0-15) | Float progress (0.0-1.0) |
| Duration | 16 frames = always 0.267s | 0.267s regardless of FPS |

**Why:** Modern games need to support variable frame rates. A 30 FPS user should have the same movement speed as a 120 FPS user.

### 2. Sine-Based Jump Easing (NOT Lookup Tables)

**Pokemon's approach:** Pre-calculated lookup tables with 16 entries.

**Our adaptation:** `y = -peakHeight * sin(π * progress)` - mathematically equivalent.

| Progress | Sine (10px) | Pokemon Normal | Max Diff |
|----------|-------------|----------------|----------|
| 0.5 (peak) | -10.0 | -10 | 0px |
| 0.75 | -7.1 | -6 | 1.1px |

**Why:**
- Simpler code (one formula vs three arrays)
- Works with any progress value (not just 16 discrete steps)
- ~2px max difference is imperceptible
- No memory allocation for lookup tables

### 3. Pixel Snapping (Optional)

**Pokemon's approach:** Integer pixel positions only (GBA hardware limitation).

**Our adaptation:** Optional `SnapToPixel` flag per movement mode.

```csharp
// In GridMovementSystem
if (movement.SnapToPixel)
{
    renderX = MathF.Round(interpolatedX);
    renderY = MathF.Round(interpolatedY);
}
```

**Why:** Gives us the crisp "retro" look when desired while allowing smooth movement for modern aesthetics.

### 4. Animation Independence

Pokemon syncs animation with movement using leg alternation tables.

**Our approach:** Animation system runs independently. Movement doesn't control animation frames - the animation plays at its own frame rate. This is simpler and matches our existing SpriteAnimationSystem.

### 5. Muddy Slope Escape - Fastest Speed Only

From pokeemerald-expansion: Only `PLAYER_SPEED_FASTEST` allows escaping muddy slopes while moving north.

**Our design:** Only `SpeedLevel.Fastest` (0.033s per tile) allows upward escape.

### 6. Multi-Tile Jumps - Skip Intermediate Tiles

Pokemon skips intermediate tile checks during jumps.

**Our design:** Multi-tile jumps only call `OnTileEnter()` on the landing tile. No collision checks during arc.

### 7. Jump Duration by Distance

| Jump Distance | Duration | Tiles |
|---------------|----------|-------|
| InPlace | 0.267 sec | 0 |
| Normal | 0.267 sec | 1 |
| Far | 0.533 sec | 2 |

Far jumps double the duration to cover more distance at the same visual speed.

---

## Testing Strategy

1. **Unit tests** for MovementTimingService
   - Verify duration values match Pokemon equivalent (16 frames / 60 = 0.267s)
   - Verify sine easing produces correct peak heights
   - Verify jump offset at progress=0.5 equals peak height
2. **Unit tests** for TileCheckResult factory methods
3. **Unit tests** for CollisionResult logic
4. **Integration tests** for dual-tile collision (CheckEntry + CheckExit)
5. **Integration tests** for ledge jump full sequence
   - Verify timing is consistent across 30/60/120 FPS
   - Verify arc shape visually matches expected curve
6. **Integration tests** for ice sliding until obstacle
7. **Integration tests** for water current pushing
8. **Frame-rate independence tests**
   - Run same movement at 30, 60, 120 FPS
   - Verify total duration is consistent (±1ms tolerance)
   - Verify final position is identical
9. **Visual tests** comparing jump arcs to Pokemon Emerald footage
10. **Performance tests** ensuring no allocations in movement hot path
