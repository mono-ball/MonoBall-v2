# MonoBall Behavior Verification Checklist

## Critical: Must Verify Before Implementation

This document outlines what we **MUST** verify from the MonoBall repository source code before implementing our input/movement/animation system. It's **CRITICAL** that we replicate their exact behavior.

---

## How to Access MonoBall Source Code

1. **Clone the repository**:
   ```bash
   git clone https://github.com/mono-ball/MonoBall.git
   cd MonoBall
   ```

2. **Key directories to examine**:
   - `MonoBallFramework.Game/Engine/` - Core engine systems
   - `MonoBallFramework.Game/Components/` - ECS components
   - `MonoBallFramework.Game/GameSystems/` - Game-specific systems
   - Look for files like: `InputSystem.cs`, `MovementSystem.cs`, `PlayerSystem.cs`, `AnimationSystem.cs`

---

## Verification Checklist

### 1. Input System Behavior

#### Input Handling Method
- [ ] **How does MonoBall capture input?**
  - Direct keyboard polling in Update()?
  - Event-based input handling?
  - Service/manager pattern?
- [ ] **Where is input processed?**
  - In a dedicated InputSystem?
  - In PlayerSystem?
  - In SceneInputSystem?
- [ ] **File to check**: Look for `InputSystem.cs`, `InputManager.cs`, or input handling in `PlayerSystem.cs`

#### Named Input Actions
- [ ] **Does MonoBall use named input actions?**
  - Confirm: Up, Down, Left, Right, Pause, Interaction (from web search)
  - Are there other actions?
  - How are they defined? (enum, constants, config file?)
- [ ] **How are keys mapped to actions?**
  - Hardcoded mappings?
  - Configurable bindings?
  - Service/manager for bindings?
- [ ] **File to check**: Look for `InputAction.cs`, `InputBinding.cs`, `InputManager.cs`

#### Input State Management
- [ ] **How is input state stored?**
  - Component-based (InputComponent)?
  - Service-based (InputService)?
  - Direct in systems?
- [ ] **Does MonoBall track:**
  - Currently pressed keys/actions?
  - Just-pressed (this frame)?
  - Just-released (this frame)?
- [ ] **File to check**: Look for `InputComponent.cs` or input state in `PlayerComponent.cs`

#### Scene Integration
- [ ] **How does input respect scene blocking?**
  - Does InputSystem check scene state?
  - Does SceneInputSystem call InputSystem?
  - How are different scenes handled?
- [ ] **File to check**: `SceneInputSystem.cs`, `SceneManagerSystem.cs`

---

### 2. Movement System Behavior

#### Movement Type
- [ ] **Is movement tile-based or smooth pixel-based?**
  - Verify: Does player move one tile at a time?
  - Or smooth pixel movement?
  - How is tile snapping handled?
- [ ] **File to check**: `MovementSystem.cs`, `PlayerSystem.cs`, look for tile calculations

#### Movement Speed
- [ ] **What is the movement speed?**
  - Pixels per second?
  - Tiles per second?
  - How fast does player move between tiles?
- [ ] **File to check**: Look for speed constants, `GameConstants.cs`, movement calculations

#### Movement State
- [ ] **How is movement state tracked?**
  - Component (VelocityComponent, MovementComponent)?
  - Flags in PlayerComponent?
  - Separate movement state?
- [ ] **Does MonoBall track:**
  - Is moving?
  - Target tile position?
  - Current velocity?
  - Movement direction?
- [ ] **File to check**: Look for movement-related components

#### Input Locking
- [ ] **Does MonoBall lock input while moving?**
  - Can player queue movement?
  - Can player change direction mid-movement?
  - How is input handled during movement?
- [ ] **File to check**: Movement system logic, input handling during movement

#### Tile Grid Alignment
- [ ] **How does MonoBall handle tile grid alignment?**
  - Snap to grid on movement start?
  - Snap on movement completion?
  - Always aligned to grid?
- [ ] **What is the tile size?**
  - 16x16 pixels?
  - Different size?
- [ ] **File to check**: Position calculations, tile size constants

---

### 3. Animation System Behavior

#### Animation Naming Convention
- [ ] **What animation names does MonoBall use?**
  - `face_north`, `face_south`, etc.?
  - `go_north`, `go_south`, etc.?
  - Different naming convention?
  - Check actual sprite definition files
- [ ] **File to check**: 
  - Sprite definition JSON files in `Mods/pokemon-emerald/Definitions/Sprites/`
  - Look for animation name patterns

#### Animation State Machine
- [ ] **When do animations change?**
  - On movement start?
  - On movement stop?
  - On direction change?
  - On input press?
- [ ] **How are animations triggered?**
  - Direct component update?
  - Event-based?
  - System query?
- [ ] **File to check**: `AnimationSystem.cs`, `SpriteAnimationSystem.cs`, `PlayerSystem.cs`

#### Animation Direction Handling
- [ ] **How does MonoBall handle East/West directions?**
  - Separate animations (`face_east`, `face_west`)?
  - Sprite flipping (`face_south` flipped horizontally)?
  - Check sprite definitions and rendering code
- [ ] **File to check**: 
  - Sprite definitions (check if `face_east`/`face_west` exist)
  - Rendering system (check for `FlipHorizontal` usage)

#### Animation Frame Updates
- [ ] **How are animation frames updated?**
  - Frame-by-frame in Update()?
  - Time-based?
  - Event-driven?
- [ ] **File to check**: `SpriteAnimationSystem.cs`, animation update logic

#### Animation Synchronization
- [ ] **How are animations synchronized with movement?**
  - Animation changes when movement starts?
  - Animation changes when movement stops?
  - Animation changes on direction change while idle?
- [ ] **File to check**: Look for connections between movement and animation systems

---

### 4. System Integration

#### System Update Order
- [ ] **What is the update order in MonoBall?**
  - InputSystem → MovementSystem → AnimationSystem?
  - Different order?
  - Check SystemManager or main game loop
- [ ] **File to check**: `SystemManager.cs`, `Game.cs` Update() method

#### Event Usage
- [ ] **What events does MonoBall use for input/movement/animation?**
  - InputStateChangedEvent?
  - MovementStateChangedEvent?
  - AnimationChangedEvent?
  - Check EventBus usage
- [ ] **File to check**: 
  - Event definitions in `Events/` directory
  - EventBus.Send() calls in systems

#### Component Structure
- [ ] **What components does MonoBall use?**
  - InputComponent?
  - VelocityComponent?
  - MovementComponent?
  - Check component definitions
- [ ] **File to check**: `Components/` directory, component definitions

---

### 5. Player Entity Structure

#### Player Components
- [ ] **What components does the player entity have?**
  - PlayerComponent?
  - InputComponent?
  - PositionComponent?
  - SpriteAnimationComponent?
  - SpriteSheetComponent?
  - Check player creation code
- [ ] **File to check**: `PlayerSystem.cs`, player entity creation

#### Player Initialization
- [ ] **How is player initialized?**
  - Spawn position?
  - Initial animation?
  - Initial sprite sheet?
- [ ] **File to check**: `PlayerSystem.cs`, initialization logic

---

## Verification Process

### Step 1: Clone and Explore
```bash
git clone https://github.com/mono-ball/MonoBall.git
cd MonoBall
# Explore directory structure
```

### Step 2: Find Key Files
- Search for input/movement/animation related files
- Check `MonoBallFramework.Game/` directory structure
- Look for system files, component files, event files

### Step 3: Read Source Code
- Read InputSystem implementation
- Read MovementSystem implementation
- Read AnimationSystem implementation
- Read PlayerSystem implementation
- Read component definitions
- Read event definitions

### Step 4: Test Behavior (if possible)
- Run MonoBall game
- Observe input behavior
- Observe movement behavior
- Observe animation behavior
- Compare with our design

### Step 5: Document Findings
- Document exact behavior
- Document component structure
- Document event usage
- Document system update order
- Update our design document

---

## Critical Questions to Answer

1. **Input System**:
   - How exactly does MonoBall handle input?
   - What is the exact flow from key press to movement?

2. **Movement System**:
   - Is movement truly tile-based?
   - What is the exact movement speed?
   - How is input locked during movement?

3. **Animation System**:
   - What are the exact animation names?
   - How are animations synchronized with movement?
   - How are East/West directions handled?

4. **System Integration**:
   - What is the exact update order?
   - How do systems communicate (events vs direct calls)?
   - How does scene system integrate?

---

## Next Steps

1. **Immediate**: Clone MonoBall repository and examine source code
2. **Document**: Create detailed notes on MonoBall's implementation
3. **Compare**: Compare MonoBall behavior with our design
4. **Update**: Update our design document to match MonoBall behavior exactly
5. **Verify**: Ensure our architecture improvements don't change behavior

---

## Notes

- **Behavior is Critical**: We must replicate MonoBall's exact behavior
- **Architecture is Flexible**: We can improve architecture while maintaining behavior
- **Test Thoroughly**: After implementation, test against MonoBall to verify behavior matches

---

## Files to Examine (Priority Order)

1. **High Priority** (Core behavior):
   - `InputSystem.cs` or input handling code
   - `MovementSystem.cs` or movement handling code
   - `PlayerSystem.cs` (player entity and behavior)
   - `SpriteAnimationSystem.cs` or animation handling code

2. **Medium Priority** (Structure):
   - Component definitions (`InputComponent.cs`, `VelocityComponent.cs`, etc.)
   - Event definitions (input/movement/animation events)
   - `SystemManager.cs` (system update order)

3. **Low Priority** (Configuration):
   - `GameConstants.cs` (constants like tile size, movement speed)
   - Sprite definition files (animation names)
   - Configuration files

---

## Expected Findings (Based on Web Search)

From web search, we know MonoBall uses:
- **Named inputs**: Up, Down, Left, Right, Pause, Interaction
- **Tile-based movement**: Discrete tile steps (Pokemon-style)
- **Event-driven architecture**: Decoupled systems

We need to verify:
- Exact implementation details
- Component structure
- Event usage
- System update order
- Animation naming and synchronization

---

**CRITICAL**: Do not implement until we verify MonoBall's exact behavior!

