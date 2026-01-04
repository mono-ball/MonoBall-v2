# Sprite Component Design (Improved ECS Architecture)

## Overview

This document describes the design for adding a `SpriteComponent` that stores sprite rendering data (sprite ID, frame index, flip flags) separate from animation state. This follows true Single Responsibility Principle (SRP) and ECS best practices:

- **SpriteComponent**: Stores sprite data (which sprite, which frame, how to flip)
- **SpriteAnimationComponent**: Stores animation state only (which animation, timing, playback state)
- **SpriteAnimationSystem**: Updates `SpriteComponent.frameIndex` when animation advances

This enables both static sprites (no animation) and animated sprites (with animation) using the same base component, with animation as an optional enhancement.

## Goals

1. **True SRP**: Separate sprite data from animation state
2. **ECS Best Practices**: Components are pure data, systems update components
3. **Unified Rendering**: Single rendering path for all sprites (animated or static)
4. **Backward Compatibility**: Existing animated sprites continue to work (with migration)
5. **Performance**: Efficient rendering path for both static and animated sprites

## Requirements

- All sprite entities must have `SpriteComponent` (sprite data)
- Animated entities additionally have `SpriteAnimationComponent` (animation state)
- `SpriteAnimationSystem` updates `SpriteComponent.frameIndex` when animation advances
- `SpriteRendererSystem` renders using `SpriteComponent` (single rendering path)
- Static sprites work without `SpriteAnimationComponent`
- Frame index validation and error handling

---

## Component Design

### SpriteComponent (Renamed from StaticSpriteComponent)

**Location**: `MonoBall.Core/ECS/Components/SpriteComponent.cs`

**Structure**:
```csharp
namespace MonoBall.Core.ECS.Components;

/// <summary>
///     Component that stores sprite rendering data.
///     Contains sprite ID, current frame index, and flip flags.
///     All sprite entities must have this component.
///     For animated sprites, SpriteAnimationSystem updates FrameIndex when animation advances.
/// </summary>
public struct SpriteComponent
{
    /// <summary>
    ///     The sprite definition ID to render.
    /// </summary>
    public string SpriteId { get; set; }

    /// <summary>
    ///     The current frame index to render (0-based index into sprite sheet frames).
    ///     For static sprites: Set manually and remains constant.
    ///     For animated sprites: Updated by SpriteAnimationSystem based on current animation frame.
    /// </summary>
    public int FrameIndex { get; set; }

    /// <summary>
    ///     Whether to flip the sprite horizontally.
    ///     For animated sprites: Updated by SpriteAnimationSystem from animation manifest.
    /// </summary>
    public bool FlipHorizontal { get; set; }

    /// <summary>
    ///     Whether to flip the sprite vertically.
    ///     For animated sprites: Updated by SpriteAnimationSystem from animation manifest.
    /// </summary>
    public bool FlipVertical { get; set; }
}
```

**Properties**:
- `SpriteId` (string, non-nullable): The sprite definition ID (e.g., `"base:sprite:items/pokeball"`). Cannot be null or empty.
- `FrameIndex` (int): Current frame index (0-based) - updated by animation system if animated. Must be non-negative and within sprite frame count.
- `FlipHorizontal` (bool): Whether to flip sprite horizontally. Updated by `SpriteAnimationSystem` for animated sprites.
- `FlipVertical` (bool): Whether to flip sprite vertically. Updated by `SpriteAnimationSystem` for animated sprites.

**Notes**:
- **Required component** for all sprite entities
- `SpriteId` is validated at render time (throws `InvalidOperationException` if sprite not found)
- `FrameIndex` is validated at render time (throws `ArgumentOutOfRangeException` if out of range)
- For animated sprites, `SpriteAnimationSystem` updates `FrameIndex` and flip flags every frame
- For static sprites, these values are set manually and remain constant

### SpriteAnimationComponent (Refactored - Animation State Only)

**Location**: `MonoBall.Core/ECS/Components/SpriteAnimationComponent.cs`

**Structure** (refactored):
```csharp
namespace MonoBall.Core.ECS.Components;

/// <summary>
///     Component that stores animation state for a sprite.
///     Contains animation name, timing, and playback state.
///     Requires SpriteComponent to be present (updates SpriteComponent.frameIndex).
///     Matches oldmonoball Animation component structure for proper turn-in-place behavior.
/// </summary>
public struct SpriteAnimationComponent
{
    /// <summary>
    ///     The name of the current animation.
    /// </summary>
    public string CurrentAnimationName { get; set; }

    /// <summary>
    ///     The current frame index in the animation sequence (0-based).
    ///     Used internally by SpriteAnimationSystem to track animation progress.
    ///     SpriteAnimationSystem updates SpriteComponent.FrameIndex based on this.
    /// </summary>
    public int CurrentAnimationFrameIndex { get; set; }

    /// <summary>
    ///     Time elapsed on the current frame in seconds.
    /// </summary>
    public float ElapsedTime { get; set; }

    /// <summary>
    ///     Whether the animation is currently playing.
    /// </summary>
    public bool IsPlaying { get; set; }

    /// <summary>
    ///     Whether the animation has completed (for non-looping animations or PlayOnce).
    ///     Used for turn-in-place detection - when IsComplete is true, the turn animation finished.
    /// </summary>
    public bool IsComplete { get; set; }

    /// <summary>
    ///     Whether the animation should play only once regardless of manifest Loop setting.
    ///     When true, the animation will set IsComplete=true after one full cycle.
    ///     Used for turn-in-place animations (Pokemon Emerald WALK_IN_PLACE_FAST behavior).
    /// </summary>
    public bool PlayOnce { get; set; }

    /// <summary>
    ///     Bit field of frame indices that have already triggered their events.
    ///     Used to prevent re-triggering events when frame hasn't changed.
    ///     Reset when animation changes or loops.
    ///     Each bit represents a frame index (supports up to 64 frames).
    ///     Zero-allocation alternative to HashSet.
    /// </summary>
    public ulong TriggeredEventFrames { get; set; }
}
```

**Key Changes**:
- ❌ **Removed**: `FlipHorizontal`, `FlipVertical` (moved to `SpriteComponent`)
- ❌ **Removed**: `SpriteId` (moved to `SpriteComponent`)
- ✅ **Renamed**: `CurrentFrameIndex` → `CurrentAnimationFrameIndex` (clarifies it's animation sequence index, not sprite frame index)
- ✅ **Kept**: Animation state only (name, timing, playback state)

**Responsibilities**:
- Stores animation sequence state (which animation, which frame in sequence, timing)
- `SpriteAnimationSystem` uses this to update `SpriteComponent.FrameIndex` and flip flags

### SpriteSheetComponent (Unchanged)

**Location**: `MonoBall.Core/ECS/Components/SpriteSheetComponent.cs`

**Purpose**: Tracks current sprite sheet for entities that support multiple sprite sheets (e.g., players).

**Note**: This component remains unchanged. It's used alongside `SpriteComponent` for entities that can switch sprite sheets.

### SpriteAnimationFrame (Enhanced for Performance)

**Location**: `MonoBall.Core/Maps/SpriteDefinition.cs`

**Structure** (enhanced):
```csharp
/// <summary>
///     Represents a cached animation frame with precomputed rectangle and duration.
/// </summary>
public class SpriteAnimationFrame
{
    /// <summary>
    ///     The source rectangle for this frame in the texture.
    /// </summary>
    public Rectangle SourceRectangle { get; set; }

    /// <summary>
    ///     The duration of this frame in seconds.
    /// </summary>
    public float DurationSeconds { get; set; }

    /// <summary>
    ///     The sprite sheet frame index (from SpriteDefinition.Frames[].Index).
    ///     Stored during precomputation to enable O(1) frame lookup in animation system.
    /// </summary>
    public int FrameIndex { get; set; }
}
```

**Key Addition**:
- ✅ **Added**: `FrameIndex` property - stores the sprite sheet frame index during precomputation
- **Purpose**: Enables O(1) frame index lookup in `SpriteAnimationSystem` instead of O(n) rectangle comparison
- **Set During**: `ResourceManager.PrecomputeAnimationFrames()` when creating animation frames

---

## System Changes

### SpriteAnimationSystem

**Location**: `MonoBall.Core/ECS/Systems/SpriteAnimationSystem.cs`

#### Key Changes

1. **Requires SpriteComponent**: Animation system now requires both `SpriteAnimationComponent` and `SpriteComponent`
2. **Updates SpriteComponent**: When animation advances, updates `SpriteComponent.FrameIndex` and flip flags
3. **Animation Frame Mapping**: Maps `CurrentAnimationFrameIndex` (animation sequence) to actual sprite `FrameIndex` (sprite sheet frame)

#### Updated Query Structure

```csharp
// NPCs with animation (requires both components)
_npcQuery = new QueryDescription().WithAll<
    NpcComponent,
    SpriteComponent,           // NEW: Required for sprite data
    SpriteAnimationComponent,  // Animation state
    ActiveMapEntity
>();

// Players with animation (requires both components)
_playerQuery = new QueryDescription().WithAll<
    PlayerComponent,
    SpriteSheetComponent,      // For sprite sheet switching
    SpriteComponent,           // NEW: Required for sprite data
    SpriteAnimationComponent,  // Animation state
    PositionComponent,
    RenderableComponent
>();
```

#### Updated Update Method

```csharp
public override void Update(in float deltaTime)
{
    var dt = deltaTime; // Copy to avoid ref parameter in lambda
    
    // Update NPC animations
    World.Query(in _npcQuery, (Entity entity, ref NpcComponent npc, ref SpriteComponent sprite, ref SpriteAnimationComponent anim) =>
    {
        // Defensive check: Ensure SpriteComponent exists (query should prevent this, but fail fast)
        if (!World.Has<SpriteComponent>(entity))
        {
            _logger.Warning(
                "SpriteAnimationSystem.Update: Entity {EntityId} has SpriteAnimationComponent but missing SpriteComponent",
                entity.Id
            );
            return;
        }
        
        // Get animation frames from cache
        var frames = _resourceManager.GetAnimationFrames(sprite.SpriteId, anim.CurrentAnimationName);
        
        // Update animation timing (existing logic)
        UpdateAnimationTiming(ref anim, dt, frames);
        
        // Update SpriteComponent based on animation state
        UpdateSpriteFromAnimation(entity, sprite.SpriteId, ref sprite, ref anim, frames);
    });
    
    // Similar for players...
}

/// <summary>
///     Updates SpriteComponent based on current animation state.
///     Maps animation frame index to sprite frame index and updates flip flags.
/// </summary>
/// <param name="entity">The entity being updated.</param>
/// <param name="spriteId">The sprite ID.</param>
/// <param name="sprite">The sprite component to update.</param>
/// <param name="anim">The animation component.</param>
/// <param name="frames">The precomputed animation frames.</param>
private void UpdateSpriteFromAnimation(
    Entity entity,
    string spriteId,
    ref SpriteComponent sprite,
    ref SpriteAnimationComponent anim,
    IReadOnlyList<SpriteAnimationFrame> frames
)
{
    if (frames == null || frames.Count == 0)
        return;
    
    // Defensive check: Ensure SpriteComponent exists (query should prevent this, but fail fast if it happens)
    if (!World.Has<SpriteComponent>(entity))
    {
        _logger.Warning(
            "SpriteAnimationSystem.UpdateSpriteFromAnimation: Entity {EntityId} has SpriteAnimationComponent but missing SpriteComponent",
            entity.Id
        );
        return;
    }
    
    // Validate animation frame index is within bounds
    if (anim.CurrentAnimationFrameIndex < 0 || anim.CurrentAnimationFrameIndex >= frames.Count)
    {
        _logger.Warning(
            "SpriteAnimationSystem.UpdateSpriteFromAnimation: Animation frame index {FrameIndex} out of range for animation {AnimationName} (frame count: {FrameCount})",
            anim.CurrentAnimationFrameIndex,
            anim.CurrentAnimationName,
            frames.Count
        );
        return;
    }
    
    // Get current animation frame
    var animationFrame = frames[anim.CurrentAnimationFrameIndex];
    
    // Update SpriteComponent with sprite frame index (O(1) - frame index stored during precomputation)
    // SpriteAnimationFrame.FrameIndex is set during ResourceManager.PrecomputeAnimationFrames()
    sprite.FrameIndex = animationFrame.FrameIndex;
    
    // Update flip flags from animation manifest
    sprite.FlipHorizontal = _resourceManager.GetAnimationFlipHorizontal(spriteId, anim.CurrentAnimationName);
    sprite.FlipVertical = _resourceManager.GetAnimationFlipVertical(spriteId, anim.CurrentAnimationName);
}
```

**Frame Index Mapping**:
- `CurrentAnimationFrameIndex`: Index into animation sequence (0, 1, 2, ... for animation frames)
- `SpriteComponent.FrameIndex`: Index into sprite sheet frames (actual frame index from `SpriteDefinition.Frames`)
- **Example**: Animation has frames `[5, 3, 7]` (sprite sheet frame indices)
  - `CurrentAnimationFrameIndex = 1` → animation is on second frame
  - `SpriteComponent.FrameIndex = 3` → sprite sheet frame index 3 (from animation frame)

### SpriteRendererSystem

**Location**: `MonoBall.Core/ECS/Systems/SpriteRendererSystem.cs`

#### Simplified Query Structure

**Single Unified Query** (no separate static/animated queries needed):
```csharp
// NPCs with sprites (all sprites use SpriteComponent)
_npcQuery = new QueryDescription().WithAll<
    NpcComponent,
    SpriteComponent,           // All sprites have this
    PositionComponent,
    RenderableComponent,
    ActiveMapEntity
>();

// Players with sprites (all sprites use SpriteComponent)
_playerQuery = new QueryDescription().WithAll<
    PlayerComponent,
    SpriteSheetComponent,      // For sprite sheet switching (optional)
    SpriteComponent,           // All sprites have this
    PositionComponent,
    RenderableComponent
>();
```

**Key Improvement**: No need for `WithNone<SpriteAnimationComponent>()` filter - all sprites use the same component!

#### Unified Rendering

**Single Rendering Method** (works for both static and animated):
```csharp
/// <summary>
///     Renders a sprite using SpriteComponent data.
///     Works for both static sprites and animated sprites (SpriteComponent is updated by SpriteAnimationSystem).
/// </summary>
private void RenderSprite(
    string spriteId,
    SpriteComponent sprite,
    PositionComponent pos,
    RenderableComponent render
)
{
    // Get sprite texture
    Texture2D spriteTexture = _resourceManager.LoadTexture(spriteId);

    // Get frame rectangle directly from SpriteComponent
    Rectangle frameRect;
    try
    {
        frameRect = _resourceManager.GetSpriteFrameRectangle(spriteId, sprite.FrameIndex);
    }
    catch (Exception ex)
    {
        _logger.Warning(
            ex,
            "SpriteRendererSystem.RenderSprite: Failed to get frame rectangle for sprite {SpriteId}, frame {FrameIndex}",
            spriteId,
            sprite.FrameIndex
        );
        return;
    }

    // Calculate color with opacity
    var color = Color.White * render.Opacity;

    // Determine sprite effects (can combine horizontal and vertical flips)
    var spriteEffects = SpriteEffects.None;
    if (sprite.FlipHorizontal)
        spriteEffects |= SpriteEffects.FlipHorizontally;
    if (sprite.FlipVertical)
        spriteEffects |= SpriteEffects.FlipVertically;

    // Draw the sprite
    _spriteBatch!.Draw(
        spriteTexture,
        pos.Position,
        frameRect,
        color,
        0.0f,           // rotation (default: no rotation)
        Vector2.Zero,   // origin (default: top-left)
        1.0f,           // scale (default: 1.0 = no scaling)
        spriteEffects,
        0.0f            // layerDepth (default: 0.0 = front layer)
    );
}
```

**Benefits**:
- ✅ Single rendering path for all sprites
- ✅ No conditional logic (animated vs static)
- ✅ Simpler queries (no precedence rules needed)
- ✅ True SRP: Rendering system only renders, animation system only animates

---

## ResourceManager Extensions

### Enhanced: PrecomputeAnimationFrames (Update Existing Method)

**Location**: `MonoBall.Core/Resources/ResourceManager.cs`

**Change**: Store `FrameIndex` in `SpriteAnimationFrame` during precomputation:

```csharp
private void PrecomputeAnimationFrames(string spriteId, SpriteDefinition definition)
{
    // ... existing code ...
    
    for (var i = 0; i < animation.FrameIndices.Count; i++)
    {
        var frameIndex = animation.FrameIndices[i];
        // ... existing duration calculation ...
        
        // Find the frame definition
        var frameDef = definition.Frames.FirstOrDefault(f => f.Index == frameIndex);
        if (frameDef != null)
        {
            var animationFrame = new SpriteAnimationFrame
            {
                SourceRectangle = new Rectangle(
                    frameDef.X,
                    frameDef.Y,
                    frameDef.Width,
                    frameDef.Height
                ),
                DurationSeconds = durationSeconds,
                FrameIndex = frameDef.Index  // NEW: Store frame index for O(1) lookup
            };
            frameList.Add(animationFrame);
        }
    }
    
    // ... existing cache storage ...
}
```

**Performance Benefit**: Enables O(1) frame index lookup in `SpriteAnimationSystem` instead of O(n) rectangle comparison.

### New Method: GetSpriteFrameRectangle

**Location**: `MonoBall.Core/Resources/ResourceManager.cs` and `IResourceManager.cs`

**Signature**:
```csharp
/// <summary>
///     Gets the source rectangle for a specific frame index in a sprite definition.
///     This method directly accesses frame definitions without animation lookup.
/// </summary>
/// <param name="spriteId">The sprite definition ID.</param>
/// <param name="frameIndex">The frame index (0-based) into the sprite sheet.</param>
/// <returns>The source rectangle for the frame.</returns>
/// <exception cref="ArgumentException">Thrown when spriteId is null/empty or frameIndex is negative.</exception>
/// <exception cref="InvalidOperationException">Thrown when sprite definition not found or frame index out of range.</exception>
Rectangle GetSpriteFrameRectangle(string spriteId, int frameIndex);
```

**Implementation Logic**:
1. Validate `spriteId` is not null/empty (throws `ArgumentException`)
2. Validate `frameIndex` is non-negative (throws `ArgumentException`)
3. Get `SpriteDefinition` (throws `InvalidOperationException` if not found)
4. Validate `frameIndex < definition.Frames.Count` (throws `ArgumentOutOfRangeException` if out of range)
5. **Frame Lookup Strategy**:
   - **If frames are sequential** (Index 0, 1, 2, ...): Use direct array access `definition.Frames[frameIndex]` (O(1))
   - **If frames can have non-sequential indices**: Use `FirstOrDefault(f => f.Index == frameIndex)` (O(n))
   - **Note**: Validate frame `Index` property matches array position for safety
6. Return `Rectangle(frame.X, frame.Y, frame.Width, frame.Height)`

**Performance Note**: 
- Direct array access is O(1) and preferred if frames are always sequential
- Linear search is O(n) but acceptable for small frame counts (< 100 frames typical)
- Frame lookups are cached in `SpriteAnimationFrame` during precomputation for animation system

**Error Handling**:
- Throws `ArgumentException` for null/empty `spriteId` or negative `frameIndex`
- Throws `InvalidOperationException` if sprite definition not found
- Throws `ArgumentOutOfRangeException` if `frameIndex` is out of range

---

## Architecture Benefits

### Single Responsibility Principle ✅

- **SpriteComponent**: Stores sprite data only (sprite ID, frame index, flip flags)
- **SpriteAnimationComponent**: Stores animation state only (animation name, timing, playback)
- **SpriteAnimationSystem**: Updates sprite data based on animation state
- **SpriteRendererSystem**: Renders sprites using sprite data (no animation knowledge)

### ECS Best Practices ✅

- **Components are Pure Data**: No behavior in components
- **Systems Update Components**: `SpriteAnimationSystem` updates `SpriteComponent`
- **Single Source of Truth**: `SpriteComponent` is the authoritative source for rendering data
- **Composition Over Inheritance**: Animation is optional enhancement (add `SpriteAnimationComponent` to enable)

### Unified Rendering ✅

- **Single Rendering Path**: All sprites use `SpriteComponent`, single `RenderSprite()` method
- **No Conditional Logic**: No "if animated, else static" checks in renderer
- **Simpler Queries**: No need for precedence rules or `WithNone<>` filters

---

## Migration Strategy

### Existing Entities

**Current State**:
- NPCs/Players have `SpriteAnimationComponent` with `CurrentFrameIndex`, `FlipHorizontal`, `SpriteId` (for players, in `SpriteSheetComponent`)

**Migration Steps**:
1. Add `SpriteComponent` to all existing sprite entities
2. Copy `SpriteId` from `SpriteSheetComponent.CurrentSpriteSheetId` (players) or `NpcComponent.SpriteId` (NPCs) to `SpriteComponent.SpriteId`
3. Copy `CurrentFrameIndex` from `SpriteAnimationComponent` to `SpriteComponent.FrameIndex`
4. Copy flip flags from `SpriteAnimationComponent` to `SpriteComponent`
5. Rename `SpriteAnimationComponent.CurrentFrameIndex` → `CurrentAnimationFrameIndex`
6. Remove `FlipHorizontal`, `FlipVertical` from `SpriteAnimationComponent`
7. Update `SpriteAnimationSystem` to update `SpriteComponent` instead of storing in animation component

**Migration Code** (one-time migration):
```csharp
// Migrate existing entities
World.Query(new QueryDescription().WithAll<SpriteAnimationComponent>(), (Entity entity, ref SpriteAnimationComponent anim) =>
{
    // Get sprite ID (from SpriteSheetComponent for players, NpcComponent for NPCs)
    string spriteId = /* get from appropriate component */;
    
    // Add SpriteComponent
    World.Add(entity, new SpriteComponent
    {
        SpriteId = spriteId,
        FrameIndex = anim.CurrentFrameIndex, // Copy current frame
        FlipHorizontal = anim.FlipHorizontal,
        FlipVertical = anim.FlipVertical
    });
    
    // Rename property (requires component replacement)
    var newAnim = new SpriteAnimationComponent
    {
        CurrentAnimationName = anim.CurrentAnimationName,
        CurrentAnimationFrameIndex = anim.CurrentFrameIndex, // Renamed
        ElapsedTime = anim.ElapsedTime,
        IsPlaying = anim.IsPlaying,
        IsComplete = anim.IsComplete,
        PlayOnce = anim.PlayOnce,
        TriggeredEventFrames = anim.TriggeredEventFrames
        // FlipHorizontal and FlipVertical removed
    };
    World.Remove<SpriteAnimationComponent>(entity);
    World.Add(entity, newAnim);
});
```

---

## Usage Examples

### Example 1: Static Sprite (No Animation)

```csharp
// Create a Pokeball item entity with static sprite
var pokeballEntity = World.Create(
    new SpriteComponent
    {
        SpriteId = "base:sprite:items/pokeball",
        FrameIndex = 0, // First frame of pokeball sprite sheet
        FlipHorizontal = false,
        FlipVertical = false
    },
    new PositionComponent { Position = new Vector2(100, 200) },
    new RenderableComponent
    {
        IsVisible = true,
        RenderOrder = 50,
        Opacity = 1.0f
    }
);
```

### Example 2: Animated Sprite

```csharp
// Create NPC with animated sprite
var npcEntity = World.Create(
    new NpcComponent { NpcId = "npc:trainer", ... },
    new SpriteComponent
    {
        SpriteId = "base:sprite:npcs/trainer",
        FrameIndex = 0, // Initial frame (will be updated by SpriteAnimationSystem)
        FlipHorizontal = false, // Will be updated by SpriteAnimationSystem
        FlipVertical = false    // Will be updated by SpriteAnimationSystem
    },
    new SpriteAnimationComponent
    {
        CurrentAnimationName = "face_south",
        CurrentAnimationFrameIndex = 0,
        ElapsedTime = 0.0f,
        IsPlaying = true,
        IsComplete = false,
        PlayOnce = false,
        TriggeredEventFrames = 0
    },
    new PositionComponent { Position = ... },
    new RenderableComponent { IsVisible = true, ... },
    new ActiveMapEntity()
);
```

### Example 3: Switching from Animated to Static

```csharp
// Entity starts as animated (has both components)
var entity = World.Create(
    new SpriteComponent { SpriteId = "...", FrameIndex = 0, ... },
    new SpriteAnimationComponent { CurrentAnimationName = "walk_south", ... },
    ...
);

// Later: Freeze animation by removing animation component
// SpriteComponent remains with current frame index
World.Remove<SpriteAnimationComponent>(entity);
// Entity now renders as static sprite at the frame where animation stopped
```

### Example 4: Player with Sprite Sheet Switching

```csharp
// Player entity with sprite sheet switching support
var playerEntity = World.Create(
    new PlayerComponent { PlayerId = "player:main", ... },
    new SpriteSheetComponent { CurrentSpriteSheetId = "base:sprite:player/may" },
    new SpriteComponent
    {
        SpriteId = "base:sprite:player/may", // Initial sprite (matches SpriteSheetComponent)
        FrameIndex = 0,
        FlipHorizontal = false,
        FlipVertical = false
    },
    new SpriteAnimationComponent { CurrentAnimationName = "face_south", ... },
    ...
);

// When sprite sheet changes (via SpriteSheetSystem):
// 1. SpriteSheetComponent.CurrentSpriteSheetId is updated
// 2. SpriteComponent.SpriteId should be updated to match
// 3. SpriteAnimationComponent continues with new sprite sheet
```

---

## Component Relationships

### Entity Component Requirements

**All Sprite Entities**:
- ✅ `SpriteComponent` (required) - Sprite data

**Animated Sprite Entities**:
- ✅ `SpriteComponent` (required) - Sprite data
- ✅ `SpriteAnimationComponent` (required) - Animation state

**Players (with sprite sheet switching)**:
- ✅ `SpriteComponent` (required) - Sprite data
- ✅ `SpriteSheetComponent` (optional) - Sprite sheet tracking
- ✅ `SpriteAnimationComponent` (optional) - Animation state

### Component Dependencies

```
SpriteComponent (independent)
    └─ Can exist alone (static sprite)

SpriteAnimationComponent (depends on SpriteComponent)
    └─ Requires SpriteComponent to update
    └─ SpriteAnimationSystem updates SpriteComponent

SpriteSheetComponent (independent, used with SpriteComponent)
    └─ Tracks which sprite sheet is active
    └─ SpriteComponent.SpriteId should match CurrentSpriteSheetId
```

---

## System Responsibilities

### SpriteAnimationSystem

**Responsibilities**:
- Update animation timing (`ElapsedTime`, `CurrentAnimationFrameIndex`)
- Map animation frame to sprite frame index
- Update `SpriteComponent.FrameIndex` based on current animation frame
- Update `SpriteComponent.FlipHorizontal` and `FlipVertical` from animation manifest
- Handle animation completion, looping, PlayOnce mode

**Does NOT**:
- ❌ Store sprite ID (that's in `SpriteComponent`)
- ❌ Store current frame index for rendering (that's in `SpriteComponent`)
- ❌ Store flip flags (that's in `SpriteComponent`)

### SpriteRendererSystem

**Responsibilities**:
- Query entities with `SpriteComponent`
- Render sprites using `SpriteComponent` data
- Handle culling, sorting, batching

**Does NOT**:
- ❌ Know about animations (no `SpriteAnimationComponent` dependency)
- ❌ Update frame indices (that's `SpriteAnimationSystem`'s job)
- ❌ Handle animation state (that's `SpriteAnimationSystem`'s job)

---

## Backward Compatibility

### Breaking Changes

**⚠️ This is a breaking change** (but follows "no backward compatibility" rule):

1. **Component Structure Changed**: `SpriteAnimationComponent` loses `FlipHorizontal`, `FlipVertical`, `CurrentFrameIndex` (renamed)
2. **New Required Component**: All sprite entities must have `SpriteComponent`
3. **System Behavior Changed**: `SpriteAnimationSystem` now updates `SpriteComponent` instead of storing in animation component

### Migration Required

- **All existing entities** must be migrated to new component structure
- **All call sites** that access `SpriteAnimationComponent.FlipHorizontal` must use `SpriteComponent.FlipHorizontal`
- **All call sites** that access `SpriteAnimationComponent.CurrentFrameIndex` must use `SpriteComponent.FrameIndex`

### Update All Call Sites

Following the "no backward compatibility" rule, update all code that:
- Accesses `SpriteAnimationComponent.FlipHorizontal` → Use `SpriteComponent.FlipHorizontal`
- Accesses `SpriteAnimationComponent.FlipVertical` → Use `SpriteComponent.FlipVertical`
- Accesses `SpriteAnimationComponent.CurrentFrameIndex` → Use `SpriteComponent.FrameIndex` (for rendering) or `SpriteAnimationComponent.CurrentAnimationFrameIndex` (for animation logic)

---

## Performance Considerations

### Query Efficiency

- **Unified Queries**: Single query for all sprites (no separate static/animated queries)
- **No Precedence Checks**: No `WithNone<>` filters needed
- **Simpler Logic**: Renderer doesn't need to check for animation component

### Rendering Efficiency

- **Single Rendering Path**: All sprites use same rendering method
- **No Conditional Logic**: No "if animated, else static" checks in hot path
- **Direct Frame Access**: `GetSpriteFrameRectangle()` is O(1) if frames are sequential

### Memory

- **Component Size**: `SpriteComponent` is small (string + 2 ints + 2 bools = ~26 bytes)
- **No Duplication**: Sprite data stored once in `SpriteComponent` (not duplicated in animation component)

---

## Error Handling

### Validation Points

1. **Component Creation**: No validation (components are pure data)
2. **Animation System**: Validates `SpriteComponent` exists before updating
3. **Render Time**: Validate frame index and sprite existence at render time (fail fast)

### Error Cases

**Missing SpriteComponent**:
```csharp
// Entity has SpriteAnimationComponent but no SpriteComponent
// Result: SpriteAnimationSystem logs warning and skips entity (defensive check in Update())
// SpriteRendererSystem won't render (entity doesn't match query - requires SpriteComponent)
```

**Invalid Frame Index**:
```csharp
// SpriteComponent.FrameIndex = 100 but sprite only has 10 frames
// Result: ArgumentOutOfRangeException thrown in GetSpriteFrameRectangle()
// SpriteRendererSystem catches exception, logs warning, sprite not rendered
```

**Missing Sprite Definition**:
```csharp
// SpriteComponent.SpriteId = "invalid:sprite:id"
// Result: InvalidOperationException thrown in GetSpriteFrameRectangle()
// SpriteRendererSystem catches exception, logs warning, sprite not rendered
```

**Invalid Animation Frame Index**:
```csharp
// SpriteAnimationComponent.CurrentAnimationFrameIndex = 100 but animation only has 5 frames
// Result: SpriteAnimationSystem logs warning and skips update (defensive check in UpdateSpriteFromAnimation())
// SpriteComponent.FrameIndex remains unchanged (last valid frame)
```

---

## Testing Considerations

### Unit Tests

1. **SpriteComponent**:
   - Component can be created with valid data
   - Component properties can be modified

2. **SpriteAnimationComponent** (refactored):
   - Component can be created with animation state only
   - No sprite data properties (moved to SpriteComponent)

3. **ResourceManager.GetSpriteFrameRectangle()**:
   - Valid frame index returns correct rectangle
   - Invalid frame index throws `ArgumentOutOfRangeException`
   - Missing sprite throws `InvalidOperationException`
   - Null/empty spriteId throws `ArgumentException`

### Integration Tests

1. **Static Sprite Rendering**:
   - Entity with only `SpriteComponent` renders correctly
   - Frame index updates manually work correctly

2. **Animated Sprite Rendering**:
   - Entity with both components renders correctly
   - `SpriteAnimationSystem` updates `SpriteComponent.FrameIndex` correctly
   - `SpriteAnimationSystem` updates `SpriteComponent` flip flags correctly

3. **Animation System**:
   - Animation advances update `SpriteComponent.FrameIndex`
   - Animation manifest flip flags update `SpriteComponent` flip flags
   - Missing `SpriteComponent` doesn't crash animation system (logs warning)

---

## Implementation Checklist

### Phase 1: Component Refactoring

- [ ] Rename `StaticSpriteComponent` → `SpriteComponent`
- [ ] Refactor `SpriteAnimationComponent` (remove sprite data, rename `CurrentFrameIndex` → `CurrentAnimationFrameIndex`)
- [ ] Add `FrameIndex` property to `SpriteAnimationFrame` class
- [ ] Update `ResourceManager.PrecomputeAnimationFrames()` to store `FrameIndex` in `SpriteAnimationFrame`
- [ ] Add `GetSpriteFrameRectangle()` to `IResourceManager` and `ResourceManager`
- [ ] Add XML documentation for all components (including `<exception>` tags)
- [ ] Add unit tests for components

### Phase 2: System Updates

- [ ] Update `SpriteAnimationSystem` queries to require `SpriteComponent`
- [ ] Add defensive checks in `SpriteAnimationSystem.Update()` for missing `SpriteComponent`
- [ ] Update `SpriteAnimationSystem.UpdateSpriteFromAnimation()` to use `SpriteAnimationFrame.FrameIndex` (O(1) lookup)
- [ ] Add validation in `UpdateSpriteFromAnimation()` for animation frame index bounds
- [ ] Update `SpriteRendererSystem` queries to use `SpriteComponent` only
- [ ] Simplify `SpriteRendererSystem` rendering (single unified method)
- [ ] Remove precedence logic (no longer needed)
- [ ] Add error handling and logging for invalid frame indices

### Phase 3: Entity Migration

- [ ] Create migration code for existing entities
- [ ] Update `MapLoaderSystem` to create entities with `SpriteComponent`
- [ ] Update `PlayerSystem` to create entities with `SpriteComponent`
- [ ] Update all call sites that access old component properties
- [ ] Test migration with existing game data

### Phase 4: Documentation and Examples

- [ ] Update system documentation
- [ ] Add usage examples to relevant documentation
- [ ] Document component relationships clearly
- [ ] Create migration guide

---

## Future Enhancements (Out of Scope)

1. **Custom Source Rectangle**: Allow overriding frame rectangle with custom coordinates in `SpriteComponent`
2. **Frame Index Animation**: Simple frame index changes without full animation system (could be separate system)
3. **Rotation/Scale**: Add rotation and scale properties to `SpriteComponent`
4. **Sprite Sheet Auto-Sync**: Automatically sync `SpriteComponent.SpriteId` with `SpriteSheetComponent.CurrentSpriteSheetId`

---

## Summary

This improved design follows true Single Responsibility Principle and ECS best practices:

- **SpriteComponent**: Stores sprite data (sprite ID, frame index, flip flags) - required for all sprites
- **SpriteAnimationComponent**: Stores animation state only (animation name, timing, playback) - optional enhancement
- **SpriteAnimationSystem**: Updates `SpriteComponent` based on animation state
- **SpriteRendererSystem**: Renders all sprites using `SpriteComponent` (unified rendering path)

**Key Benefits**:
- ✅ True SRP: Clear separation of sprite data and animation state
- ✅ ECS Best Practices: Components are pure data, systems update components
- ✅ Unified Rendering: Single rendering path for all sprites (no conditional logic)
- ✅ Simpler Queries: No precedence rules or `WithNone<>` filters needed
- ✅ Better Composition: Animation is optional enhancement (add component to enable)
- ✅ Performance: Single rendering path, no conditional checks in hot path

**Trade-offs**:
- ⚠️ Breaking Change: Requires migration of all existing entities
- ⚠️ More Components: Entities need both `SpriteComponent` and `SpriteAnimationComponent` for animation
- ✅ But: Better architecture, clearer responsibilities, easier to maintain
