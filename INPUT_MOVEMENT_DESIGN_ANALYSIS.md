# Input and Movement System Design Analysis

## Executive Summary

This document analyzes the input and movement system design for architecture issues, Arch ECS/eventing problems, inconsistencies with MonoBall repository behavior, forward-thinking improvements, and scene integration concerns.

---

## Critical Architecture Issues

### 1. ❌ InputSystem Query Includes VelocityComponent Dependency

**Problem**: InputSystem queries for `PlayerComponent + InputComponent`, but then checks `VelocityComponent.IsMovingToTile` in the flow description. This creates an implicit dependency that's not reflected in the query.

**Current Design**:
```csharp
// InputSystem query
Query: WithAll<PlayerComponent, InputComponent>()

// But flow says:
"Check if entity is already moving (via VelocityComponent.IsMovingToTile)"
```

**Issue**: InputSystem would need to check `World.Has<VelocityComponent>()` in the hot path, which is inefficient.

**Solution**: 
- **Option A**: InputSystem queries for `WithAll<PlayerComponent, InputComponent, VelocityComponent>()` - makes dependency explicit
- **Option B**: MovementSystem sets a flag in InputComponent when movement starts (e.g., `InputComponent.IsMovementLocked`)

**Recommendation**: Option B - Keep InputComponent self-contained. MovementSystem sets `InputComponent.IsMovementLocked = true` when movement starts, and clears it when movement completes.

### 2. ❌ InputComponent Contains Redundant State

**Problem**: `InputComponent` has both `MovementDirection` (Vector2) and `FacingDirection` (Direction enum), plus `IsMoving` flag. This creates potential inconsistencies.

**Current Design**:
```csharp
public struct InputComponent
{
    public Vector2 MovementDirection { get; set; }  // Can be (0,0) or direction vector
    public bool IsMoving { get; set; }             // Redundant with MovementDirection != (0,0)
    public Direction FacingDirection { get; set; } // Redundant with MovementDirection
}
```

**Issue**: 
- `IsMoving` can be derived from `MovementDirection != Vector2.Zero`
- `FacingDirection` can be derived from `MovementDirection`
- Multiple sources of truth lead to bugs

**Solution**: Simplify to just `MovementDirection` (Vector2), derive other values when needed:
```csharp
public struct InputComponent
{
    /// <summary>
    /// Movement direction vector. Zero when no input.
    /// Cardinal directions: (0, -1) North, (0, 1) South, (1, 0) East, (-1, 0) West
    /// </summary>
    public Vector2 MovementDirection { get; set; }
    
    /// <summary>
    /// Whether input is locked (movement in progress).
    /// Set by MovementSystem when movement starts, cleared when complete.
    /// </summary>
    public bool IsMovementLocked { get; set; }
}
```

**Recommendation**: Remove redundant fields, derive values when needed.

### 3. ❌ MovementSystem Reads InputComponent Directly

**Problem**: MovementSystem reads `InputComponent` directly instead of reacting to `InputStateChangedEvent`. This breaks event-driven architecture.

**Current Design**:
```
InputSystem → Updates InputComponent → MovementSystem reads InputComponent directly
```

**Issue**: MovementSystem should react to events, not poll components every frame.

**Solution**: 
- InputSystem publishes `InputStateChangedEvent` when input changes
- MovementSystem subscribes to `InputStateChangedEvent` and reacts to it
- MovementSystem still queries for `InputComponent` to get current state, but primarily reacts to events

**Recommendation**: Hybrid approach - MovementSystem subscribes to events for state changes, but also queries InputComponent in Update() for current state (events are for notifications, components are for state).

### 4. ❌ PlayerMovementAnimationSystem Subscribes to Wrong Event

**Problem**: `PlayerMovementAnimationSystem` subscribes to `MovementStateChangedEvent`, but should also handle `InputStateChangedEvent` for facing direction changes while idle.

**Current Design**:
```
PlayerMovementAnimationSystem subscribes to MovementStateChangedEvent only
```

**Issue**: When player changes facing direction while idle (no movement), `MovementStateChangedEvent` is not fired, so animation doesn't update.

**Solution**: Subscribe to both events:
- `MovementStateChangedEvent` - for movement state changes (idle ↔ moving)
- `InputStateChangedEvent` - for facing direction changes while idle

**Recommendation**: Subscribe to both events, handle each appropriately.

### 5. ❌ PositionChangedEvent Not Needed

**Problem**: `PositionChangedEvent` is marked as "optional" but adds unnecessary complexity. Position is already in `PositionComponent`, systems can read it directly.

**Current Design**:
```
MovementSystem publishes PositionChangedEvent (optional)
```

**Issue**: 
- Position changes every frame during movement
- Publishing events every frame is expensive
- Systems can query `PositionComponent` directly when needed

**Solution**: Remove `PositionChangedEvent`. Systems that need position updates can:
- Query `PositionComponent` directly
- Subscribe to `MovementStateChangedEvent` for movement state changes
- CameraSystem already queries player position directly

**Recommendation**: Remove `PositionChangedEvent` entirely.

---

## Arch ECS / Eventing Issues

### 1. ⚠️ Event Subscription in Systems Requires IDisposable

**Problem**: Systems that subscribe to events must implement `IDisposable` to unsubscribe, but the design doesn't mention this.

**Current Design**: `PlayerMovementAnimationSystem` subscribes to events but no disposal mentioned.

**Solution**: Document that systems subscribing to events must implement `IDisposable` and unsubscribe in `Dispose()`.

**Recommendation**: Add disposal pattern documentation to design.

### 2. ⚠️ Event Handler Signature Mismatch

**Problem**: EventBus uses `Action<T>` for handlers, but the design shows handlers accepting events directly. Need to clarify handler signatures.

**Current Design**: Shows `OnMovementStateChanged(MovementStateChangedEvent evt)` but EventBus expects `Action<MovementStateChangedEvent>`.

**Solution**: Document correct handler signatures:
```csharp
// Correct subscription
EventBus.Subscribe<MovementStateChangedEvent>(OnMovementStateChanged);

// Handler signature
private void OnMovementStateChanged(MovementStateChangedEvent evt)
{
    // Handle event
}
```

**Recommendation**: Clarify event handler patterns in design.

### 3. ⚠️ Event Publishing Frequency

**Problem**: `InputStateChangedEvent` could fire every frame if input state is checked every frame, even when nothing changed.

**Current Design**: "If input changed, publish InputStateChangedEvent" - but doesn't specify change detection.

**Solution**: Only publish when state actually changes:
- Store previous input state
- Compare current vs previous
- Publish only if different

**Recommendation**: Add change detection logic to InputSystem.

### 4. ⚠️ Event Data Redundancy

**Problem**: Events contain data that's already in components (e.g., `MovementStateChangedEvent.Velocity` is in `VelocityComponent`).

**Current Design**: Events duplicate component data.

**Solution**: Events should contain minimal data - just entity reference and what changed. Subscribers can query components for full state.

**Recommendation**: Minimize event data, include only what changed.

---

## Inconsistencies with MonoBall Repository

### 1. ❓ Input Handling Architecture Unknown

**Issue**: Cannot verify how MonoBall actually handles input without access to source code. Design assumes keyboard polling, but MonoBall might use different approach.

**Recommendation**: Document assumptions and note that implementation may need adjustment based on MonoBall source code analysis.

### 2. ❓ Tile-Based Movement Confirmation Needed

**Issue**: Design assumes tile-based movement, but need to verify MonoBall uses discrete tile movement vs smooth pixel movement.

**Recommendation**: Verify MonoBall movement behavior before implementation.

### 3. ❓ Animation Naming Convention Unknown

**Issue**: Design assumes `face_{direction}` and `go_{direction}` naming, but MonoBall might use different conventions.

**Recommendation**: Verify animation naming in MonoBall sprite definitions before implementation.

---

## Forward-Thinking Improvements

### 1. ✅ Named Input Actions (High Priority)

**Problem**: Current design hardcodes key bindings (WASD, Arrow keys) directly in InputSystem. Not customizable or extensible.

**Proposed Solution**: Abstract input to named actions:

```csharp
// InputAction enum
public enum InputAction
{
    MoveNorth,
    MoveSouth,
    MoveEast,
    MoveWest,
    Interact,
    Pause,
    Menu,
    Run,
    // ... extensible
}

// InputBindingService
public interface IInputBindingService
{
    bool IsActionPressed(InputAction action);
    bool IsActionJustPressed(InputAction action);
    bool IsActionJustReleased(InputAction action);
    void SetBinding(InputAction action, Keys key);
    Keys GetBinding(InputAction action);
}

// InputComponent updated
public struct InputComponent
{
    /// <summary>
    /// Currently pressed input actions (bit flags or HashSet).
    /// </summary>
    public HashSet<InputAction> PressedActions { get; set; }
    
    /// <summary>
    /// Actions that were just pressed this frame.
    /// </summary>
    public HashSet<InputAction> JustPressedActions { get; set; }
    
    /// <summary>
    /// Actions that were just released this frame.
    /// </summary>
    public HashSet<InputAction> JustReleasedActions { get; set; }
}
```

**Benefits**:
- Configurable key bindings (load from settings/mod)
- Support for multiple input sources (keyboard, gamepad, touch)
- Extensible for new actions (Run, Interact, etc.)
- Testable (mock IInputBindingService)

**Recommendation**: Implement named input actions from the start.

### 2. ✅ Input Action State Component

**Problem**: Current design mixes raw input (keys) with processed input (direction). Should separate concerns.

**Proposed Solution**: Two-component approach:

```csharp
// RawInputComponent - stores raw key/button states (optional, for debugging)
public struct RawInputComponent
{
    public KeyboardState KeyboardState { get; set; }
    public GamePadState GamePadState { get; set; }
}

// InputComponent - stores processed input actions
public struct InputComponent
{
    public HashSet<InputAction> PressedActions { get; set; }
    public HashSet<InputAction> JustPressedActions { get; set; }
    public HashSet<InputAction> JustReleasedActions { get; set; }
}
```

**Benefits**:
- Separation of concerns (raw input vs processed actions)
- Easier to add new input sources
- Better testability

**Recommendation**: Consider two-component approach for future extensibility.

### 3. ✅ Input Context System

**Problem**: Different scenes/contexts need different input handling (gameplay vs menu vs dialog).

**Proposed Solution**: Input context system:

```csharp
// InputContext enum
public enum InputContext
{
    Gameplay,    // Movement, interact, pause
    Menu,        // Navigation, select, cancel
    Dialog,      // Advance text, skip
    Inventory,   // Navigation, use item
}

// InputComponent
public struct InputComponent
{
    public InputContext CurrentContext { get; set; }
    public HashSet<InputAction> PressedActions { get; set; }
    // ...
}

// InputSystem processes input based on context
// Different contexts map same keys to different actions
```

**Benefits**:
- Context-aware input handling
- Prevents input conflicts (e.g., movement keys in menu)
- Extensible for new contexts

**Recommendation**: Consider input context system for scene integration.

### 4. ✅ Movement Request Component Pattern

**Problem**: MovementSystem reads InputComponent directly, creating tight coupling.

**Proposed Solution**: Movement request component pattern:

```csharp
// MovementRequestComponent - added when movement requested, removed when processed
public struct MovementRequestComponent
{
    public Direction Direction { get; set; }
    public float SpeedMultiplier { get; set; } // For running, etc.
}

// Flow:
// InputSystem detects input → Adds MovementRequestComponent
// MovementSystem processes MovementRequestComponent → Removes component, starts movement
```

**Benefits**:
- Decouples input from movement
- Supports queued movement requests
- Easier to add movement modifiers (running, etc.)

**Recommendation**: Consider component-based movement requests for better decoupling.

### 5. ✅ Input Cooldown/Throttling

**Problem**: No mechanism to prevent input spam or implement input cooldowns.

**Proposed Solution**: Add input cooldown support:

```csharp
// InputComponent
public struct InputComponent
{
    public Dictionary<InputAction, float> ActionCooldowns { get; set; }
    // ...
}

// InputSystem applies cooldowns before processing
```

**Benefits**:
- Prevents input spam
- Supports game mechanics (attack cooldowns, etc.)
- Configurable per action

**Recommendation**: Add input cooldown support for future game mechanics.

---

## Scene Integration Issues

### 1. ❌ InputSystem Doesn't Respect Scene Blocking

**Problem**: InputSystem checks "if game scene is active" but doesn't properly integrate with SceneInputSystem's blocking mechanism.

**Current Design**:
```
InputSystem: "Check if game scene is active and allows input"
SceneInputSystem: "Processes input for scenes in priority order, respects BlocksInput"
```

**Issue**: Two separate input systems (InputSystem and SceneInputSystem) don't coordinate.

**Solution**: Integrate InputSystem with SceneInputSystem:

**Option A**: InputSystem is called by SceneInputSystem
```csharp
// SceneInputSystem.ProcessSceneInput() calls InputSystem for GameScene
if (sceneComponent.SceneId == "GameScene")
{
    _inputSystem.ProcessInput(keyboardState, mouseState, gamePadState);
}
```

**Option B**: InputSystem queries scene system
```csharp
// InputSystem checks scene blocking before processing
if (!_sceneManagerSystem.ShouldProcessGameplayInput())
{
    return; // Scene is blocking input
}
```

**Recommendation**: Option A - SceneInputSystem calls InputSystem for gameplay scenes. Keeps scene system in control.

### 2. ❌ No Input Context Based on Scene

**Problem**: Design doesn't specify how input changes based on active scene (gameplay vs menu vs dialog).

**Current Design**: InputSystem always processes movement input, regardless of scene.

**Solution**: Use input context system (see Forward-Thinking Improvements #3):

```csharp
// SceneInputSystem determines input context based on active scene
InputContext context = DetermineInputContext(sceneComponent);

// InputSystem processes input based on context
_inputSystem.ProcessInput(keyboardState, mouseState, gamePadState, context);
```

**Recommendation**: Integrate input context with scene system.

### 3. ❌ SceneInputSystem Placeholder Not Addressed

**Problem**: SceneInputSystem has TODO placeholder for scene-specific input handling, but design doesn't address how InputSystem integrates with it.

**Current Design**: SceneInputSystem.ProcessSceneInput() is a placeholder.

**Solution**: Document integration:
- SceneInputSystem.ProcessSceneInput() calls InputSystem for GameScene
- Other scene types handle input differently (menu navigation, dialog advancement, etc.)
- InputSystem only processes gameplay input (movement, interact, etc.)

**Recommendation**: Document scene-specific input handling integration.

### 4. ⚠️ Input Processing Order

**Problem**: Design shows InputSystem in update loop, but SceneInputSystem also processes input. Order matters.

**Current Design**:
```
Update Order:
- InputSystem (in update loop)
- MovementSystem
- ...
- SceneInputSystem (in update loop)
```

**Issue**: InputSystem processes input before SceneInputSystem checks scene blocking.

**Solution**: Process input through SceneInputSystem first:
```
Update Order:
- SceneInputSystem.ProcessInput() (called from Game.Update())
  - Calls InputSystem.ProcessInput() for GameScene if not blocked
- MovementSystem (reacts to input)
```

**Recommendation**: Process input through SceneInputSystem, not directly in update loop.

---

## Component Design Improvements

### 1. ✅ VelocityComponent Naming

**Problem**: `VelocityComponent` name suggests continuous velocity, but it's used for tile-based movement.

**Proposed Solution**: Rename to `TileMovementComponent`:

```csharp
public struct TileMovementComponent
{
    public Vector2 CurrentVelocity { get; set; }
    public float MovementSpeed { get; set; }
    public Vector2 TargetTilePosition { get; set; }
    public bool IsMovingToTile { get; set; }
}
```

**Benefits**:
- Clearer intent (tile-based movement)
- Distinguishes from smooth movement systems
- Better documentation

**Recommendation**: Rename to `TileMovementComponent` for clarity.

### 2. ✅ Direction Helper Methods

**Problem**: No helper methods for converting between Direction enum and Vector2.

**Proposed Solution**: Add Direction extension methods:

```csharp
public static class DirectionExtensions
{
    public static Vector2 ToVector2(this Direction direction)
    {
        return direction switch
        {
            Direction.North => new Vector2(0, -1),
            Direction.South => new Vector2(0, 1),
            Direction.East => new Vector2(1, 0),
            Direction.West => new Vector2(-1, 0),
            _ => Vector2.Zero
        };
    }
    
    public static Direction FromVector2(Vector2 vector)
    {
        // Normalize and determine direction
        // ...
    }
    
    public static string ToAnimationSuffix(this Direction direction)
    {
        return direction switch
        {
            Direction.North => "north",
            Direction.South => "south",
            Direction.East => "east",
            Direction.West => "west",
            _ => "south"
        };
    }
}
```

**Benefits**:
- Reusable conversion logic
- Consistent direction handling
- Easier animation name generation

**Recommendation**: Add Direction helper methods.

---

## System Design Improvements

### 1. ✅ InputSystem Should Not Be in Update Loop

**Problem**: InputSystem is in update loop, but should be called explicitly by SceneInputSystem.

**Current Design**: InputSystem.Update() processes input.

**Solution**: InputSystem has ProcessInput() method called by SceneInputSystem:

```csharp
public class InputSystem : BaseSystem<World, float>
{
    public override void Update(in float deltaTime)
    {
        // No-op - input processed via ProcessInput() called by SceneInputSystem
    }
    
    public void ProcessInput(KeyboardState keyboardState, MouseState mouseState, GamePadState gamePadState)
    {
        // Process input
    }
}
```

**Benefits**:
- Scene system controls input processing
- Respects scene blocking
- Clearer control flow

**Recommendation**: InputSystem.ProcessInput() called by SceneInputSystem, not in update loop.

### 2. ✅ MovementSystem Should Query for InputComponent Changes

**Problem**: MovementSystem reads InputComponent every frame, but should react to events.

**Solution**: Hybrid approach:
- Subscribe to InputStateChangedEvent for notifications
- Query InputComponent in Update() for current state
- Only process movement when input changes or movement completes

**Recommendation**: Hybrid event + query approach.

### 3. ✅ PlayerMovementAnimationSystem Should Query Components Directly

**Problem**: PlayerMovementAnimationSystem only reacts to events, but should also check component state in Update().

**Solution**: Query components in Update() to handle edge cases:
- Check if animation needs updating based on current component state
- Events are for notifications, components are source of truth

**Recommendation**: Query components in Update(), use events for notifications.

---

## Performance Considerations

### 1. ⚠️ Event Publishing in Hot Path

**Problem**: Publishing events every frame (even if state changed) can be expensive.

**Solution**: Only publish when state actually changes:
- Store previous state
- Compare before publishing
- Use change detection

**Recommendation**: Add change detection to prevent unnecessary event publishing.

### 2. ⚠️ HashSet Allocations in InputComponent

**Problem**: Using `HashSet<InputAction>` in InputComponent creates allocations.

**Solution**: Use bit flags or fixed-size array:
```csharp
// Option A: Bit flags
[Flags]
public enum InputAction : uint
{
    MoveNorth = 1 << 0,
    MoveSouth = 1 << 1,
    // ...
}
public uint PressedActions { get; set; } // Bit flags

// Option B: Fixed-size array (if actions are limited)
public bool[] PressedActions { get; set; } // Indexed by InputAction
```

**Recommendation**: Use bit flags for performance-critical input state.

### 3. ⚠️ Query Frequency

**Problem**: Multiple systems query same components every frame.

**Solution**: Cache query results when possible, batch operations.

**Recommendation**: Optimize queries, cache results when appropriate.

---

## Summary of Critical Issues

### Must Fix Before Implementation

1. **InputComponent Redundancy**: Remove `IsMoving` and `FacingDirection`, derive from `MovementDirection`
2. **InputSystem Query Dependency**: Add `IsMovementLocked` flag to InputComponent instead of checking VelocityComponent
3. **Scene Integration**: InputSystem should be called by SceneInputSystem, not in update loop
4. **PositionChangedEvent**: Remove unnecessary event
5. **Event Subscription Disposal**: Document IDisposable pattern for systems

### Should Fix / Improve

1. **Named Input Actions**: Implement input action abstraction for customizability
2. **Input Context System**: Add context-aware input handling for scenes
3. **VelocityComponent Naming**: Rename to `TileMovementComponent`
4. **Direction Helpers**: Add extension methods for Direction enum
5. **Movement Request Pattern**: Consider component-based movement requests

### Nice to Have

1. **Input Cooldowns**: Add cooldown support for future mechanics
2. **Raw Input Component**: Separate raw input from processed actions
3. **Performance Optimizations**: Bit flags for input actions, query caching

---

## Recommended Design Changes

### Priority 1: Critical Fixes

1. Simplify InputComponent (remove redundant fields)
2. Add IsMovementLocked flag to InputComponent
3. Integrate InputSystem with SceneInputSystem
4. Remove PositionChangedEvent
5. Document IDisposable pattern for event subscriptions

### Priority 2: Architecture Improvements

1. Implement named input actions (InputAction enum, IInputBindingService)
2. Add input context system for scene-aware input
3. Rename VelocityComponent to TileMovementComponent
4. Add Direction helper methods

### Priority 3: Future Enhancements

1. Input cooldown system
2. Movement request component pattern
3. Performance optimizations (bit flags, query caching)

---

## Updated System Flow

```
Game.Update()
  └─ SceneInputSystem.ProcessInput(keyboardState, mouseState, gamePadState)
      └─ For each active scene (priority order):
          └─ If GameScene and not blocked:
              └─ InputSystem.ProcessInput(keyboardState, mouseState, gamePadState)
                  └─ Updates InputComponent
                  └─ Publishes InputStateChangedEvent (if changed)
          └─ If scene blocks input, stop iterating

Game.Update() (continued)
  └─ MovementSystem.Update(deltaTime)
      └─ Subscribes to InputStateChangedEvent
      └─ Queries InputComponent + VelocityComponent + PositionComponent
      └─ Processes movement, updates components
      └─ Publishes MovementStateChangedEvent (if changed)

  └─ PlayerMovementAnimationSystem.Update(deltaTime)
      └─ Subscribes to InputStateChangedEvent + MovementStateChangedEvent
      └─ Queries PlayerComponent + SpriteSheetComponent + SpriteAnimationComponent
      └─ Updates animation based on movement state
```

---

## Conclusion

The design is solid but needs several critical fixes before implementation:

1. **Component simplification** (remove redundancy)
2. **Scene integration** (proper input blocking)
3. **Event-driven architecture** (proper event usage)
4. **Forward-thinking improvements** (named input actions, input context)

With these changes, the system will be more maintainable, extensible, and aligned with our architecture standards.

