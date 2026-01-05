# Position System Complete Dependency Map

## Overview

This document maps ALL places in MonoBall.Core where position coordinates are used, calculated, or transformed. The analysis covers the complete flow from entity creation through movement to rendering.

---

## 1. PositionComponent Structure

**File**: `/mnt/c/Users/nate0/RiderProjects/MonoBall/MonoBall/MonoBall.Core/ECS/Components/PositionComponent.cs`

```csharp
public struct PositionComponent
{
    public int X { get; set; }      // Grid X coordinate (tile-based)
    public int Y { get; set; }      // Grid Y coordinate (tile-based)
    public float PixelX { get; set; }  // Interpolated pixel X for smooth rendering
    public float PixelY { get; set; }  // Interpolated pixel Y for smooth rendering
    public Vector2 Position { get => new(PixelX, PixelY); set { ... } }
    public void SyncPixelsToGrid(int tileWidth = 16, int tileHeight = 16);
}
```

**Key Insight**: Position coordinates represent the **TOP-LEFT corner** of the entity's sprite. This is consistent throughout the codebase for both logic and rendering.

---

## 2. Position CREATION Locations

### 2.1 Player Creation (PlayerSystem.cs)

**File**: `/mnt/c/Users/nate0/RiderProjects/MonoBall/MonoBall/MonoBall.Core/ECS/Systems/PlayerSystem.cs`
**Lines**: 238-244

```csharp
// Convert pixel position to grid coordinates for PositionComponent
var tileWidth = TileSizeHelper.GetTileWidth(World, _constants);
var tileHeight = TileSizeHelper.GetTileHeight(World, _constants);
var gridX = (int)(position.X / tileWidth);
var gridY = (int)(position.Y / tileHeight);
float pixelX = gridX * tileWidth;
float pixelY = gridY * tileHeight;

new PositionComponent
{
    X = gridX,
    Y = gridY,
    PixelX = pixelX,
    PixelY = pixelY,
}
```

**Assumption**: Position is TOP-LEFT of player sprite
**Purpose**: Logic (collision) + Visuals (rendering)

### 2.2 NPC Creation (MapLoaderSystem.cs)

**File**: `/mnt/c/Users/nate0/RiderProjects/MonoBall/MonoBall/MonoBall.Core/ECS/Systems/MapLoaderSystem.cs`
**Lines**: 907-961

```csharp
// NPC coordinates in JSON are already in pixel coordinates (not tile coordinates)
// Add map pixel position offset to get world pixel position
var mapPixelPosition = new Vector2(
    mapTilePosition.X * mapDefinition.TileWidth,
    mapTilePosition.Y * mapDefinition.TileHeight
);
var npcPixelPosition = new Vector2(
    mapPixelPosition.X + npcDef.X,
    mapPixelPosition.Y + npcDef.Y
);

new PositionComponent { Position = npcPixelPosition }
```

**Assumption**: Position is TOP-LEFT of NPC sprite
**Purpose**: Logic (collision) + Visuals (rendering)

### 2.3 Map Entity Creation (MapLoaderSystem.cs)

**File**: `/mnt/c/Users/nate0/RiderProjects/MonoBall/MonoBall/MonoBall.Core/ECS/Systems/MapLoaderSystem.cs`
**Lines**: 138-144

```csharp
new PositionComponent
{
    Position = new Vector2(
        mapTilePosition.X * mapDefinition.TileWidth,
        mapTilePosition.Y * mapDefinition.TileHeight
    ),
}
```

**Assumption**: Position is TOP-LEFT of map area
**Purpose**: Map world positioning

### 2.4 Tile Chunk Creation (MapLoaderSystem.cs)

**File**: `/mnt/c/Users/nate0/RiderProjects/MonoBall/MonoBall/MonoBall.Core/ECS/Systems/MapLoaderSystem.cs`
**Lines**: 705-732

```csharp
// Calculate world position for chunk (relative to map's tile position)
var chunkPosition = new Vector2(
    (mapTilePosition.X + chunkStartX) * mapDefinition.TileWidth,
    (mapTilePosition.Y + chunkStartY) * mapDefinition.TileHeight
);

new PositionComponent { Position = chunkPosition }
```

**Assumption**: Position is TOP-LEFT of tile chunk
**Purpose**: Rendering + Culling

---

## 3. Position READ Locations

### 3.1 Debug Display (DebugBarSceneSystem.cs, DebugBarRendererSystem.cs)

**Files**:
- `/mnt/c/Users/nate0/RiderProjects/MonoBall/MonoBall/MonoBall.Core/Scenes/Systems/DebugBarSceneSystem.cs` (Lines 355-359)
- `/mnt/c/Users/nate0/RiderProjects/MonoBall/MonoBall/MonoBall.Core/Scenes/Systems/DebugBarRendererSystem.cs` (Lines 211-215)

```csharp
playerX = position.X;           // Grid X
playerY = position.Y;           // Grid Y
playerPixelPos = new Vector2(position.PixelX, position.PixelY);  // Pixel position
```

**Purpose**: Display current player position for debugging

### 3.2 Collision Queries (CollisionService.cs, SpatialHashSystem.cs)

**File**: `/mnt/c/Users/nate0/RiderProjects/MonoBall/MonoBall/MonoBall.Core/ECS/Services/CollisionService.cs`
```csharp
// Uses grid coordinates (position.X, position.Y) for tile-based collision
```

**File**: `/mnt/c/Users/nate0/RiderProjects/MonoBall/MonoBall/MonoBall.Core/ECS/Systems/SpatialHashSystem.cs` (Line 192)
```csharp
var key = (position.X, position.Y, (int)elevation.Value);
```

**Assumption**: Grid coordinates represent tile the entity occupies
**Purpose**: Collision detection (logic only)

### 3.3 Interaction Checks (InteractionSystem.cs)

**File**: `/mnt/c/Users/nate0/RiderProjects/MonoBall/MonoBall/MonoBall.Core/ECS/Systems/InteractionSystem.cs` (Lines 126-128, 169-170)

```csharp
// Use tile coordinates directly from PositionComponent
playerTileX = pos.X;
playerTileY = pos.Y;
...
var interactionTileX = pos.X;
var interactionTileY = pos.Y;
```

**Assumption**: Grid coordinates represent tile entity is standing on
**Purpose**: Determining if player is adjacent to interaction target

### 3.4 Camera Following (CameraSystem.cs)

**File**: `/mnt/c/Users/nate0/RiderProjects/MonoBall/MonoBall/MonoBall.Core/ECS/Systems/CameraSystem.cs` (Lines 160-170)

```csharp
ref var targetPos = ref World.Get<PositionComponent>(followEntity);

// Calculate the center point of the entity's sprite for proper camera centering
var entityCenter = CalculateEntityCenter(followEntity, targetPos.Position);

// position is treated as TOP-LEFT, center is calculated by adding half frame dimensions
return new Vector2(
    position.X + frameRect.Width / 2f,
    position.Y + frameRect.Height / 2f
);
```

**Assumption**: Position is TOP-LEFT; adds half-sprite dimensions to find center
**Purpose**: Camera centering on entity

### 3.5 Script API (ScriptApiProvider.cs)

**File**: `/mnt/c/Users/nate0/RiderProjects/MonoBall/MonoBall/MonoBall.Core/Scripting/ScriptApiProvider.cs` (Lines 494-502)

```csharp
var npcPos = _world.Get<PositionComponent>(npc);
var targetPos = _world.Get<PositionComponent>(target);

var direction = DirectionHelper.GetDirectionTo(
    npcPos.X, npcPos.Y,
    targetPos.X, targetPos.Y
);
```

**Assumption**: Grid coordinates represent tile positions for direction calculations
**Purpose**: NPC facing logic

### 3.6 Active Map Filter (ActiveMapFilterService.cs)

**File**: `/mnt/c/Users/nate0/RiderProjects/MonoBall/MonoBall/MonoBall.Core/ECS/Services/ActiveMapFilterService.cs` (Line 156)

```csharp
playerPixelPos = new Vector2(position.PixelX, position.PixelY);
```

**Purpose**: Determine which maps should be active based on player position

### 3.7 Entity Query Service (EntityQueryService.cs)

**File**: `/mnt/c/Users/nate0/RiderProjects/MonoBall/MonoBall/MonoBall.Core/ECS/Services/EntityQueryService.cs` (Lines 29-37)

```csharp
public (int X, int Y) GetEntityPosition(Entity entity)
{
    if (!_world.TryGet<PositionComponent>(entity, out var position))
        throw new InvalidOperationException(...);
    return (position.X, position.Y);
}
```

**Purpose**: Get grid position for collision/interaction logic

---

## 4. Position WRITE/MODIFY Locations

### 4.1 Movement System - Grid Position Update (MovementSystem.cs)

**File**: `/mnt/c/Users/nate0/RiderProjects/MonoBall/MonoBall/MonoBall.Core/ECS/Systems/MovementSystem.cs` (Lines 224-225)

```csharp
// Update grid position immediately (for collision/lookup)
position.X = targetX;
position.Y = targetY;
```

**Key Behavior**: Grid position is updated IMMEDIATELY when movement starts (not when it completes)
**Purpose**: Collision system uses target tile during movement

### 4.2 Movement System - Pixel Interpolation (MovementSystem.cs)

**File**: `/mnt/c/Users/nate0/RiderProjects/MonoBall/MonoBall/MonoBall.Core/ECS/Systems/MovementSystem.cs` (Lines 468-480)

```csharp
private void InterpolatePosition(ref PositionComponent position, ref GridMovement movement)
{
    var progress = MathHelper.Clamp(movement.MovementProgress, 0f, 1f);
    position.PixelX = MathHelper.Lerp(
        movement.StartPosition.X,
        movement.TargetPosition.X,
        progress
    );
    position.PixelY = MathHelper.Lerp(
        movement.StartPosition.Y,
        movement.TargetPosition.Y,
        progress
    );
}
```

**Purpose**: Smooth visual movement between tiles

### 4.3 Movement System - Snap to Target (MovementSystem.cs)

**File**: `/mnt/c/Users/nate0/RiderProjects/MonoBall/MonoBall/MonoBall.Core/ECS/Systems/MovementSystem.cs` (Lines 424-427)

```csharp
// Snap to target position
position.PixelX = movement.TargetPosition.X;
position.PixelY = movement.TargetPosition.Y;
SyncPositionToGrid(ref position);
```

**Purpose**: Ensure exact tile alignment when movement completes

### 4.4 Movement System - Sync Pixels to Grid (MovementSystem.cs)

**File**: `/mnt/c/Users/nate0/RiderProjects/MonoBall/MonoBall/MonoBall.Core/ECS/Systems/MovementSystem.cs` (Lines 487-498)

```csharp
private void SyncPositionToGrid(ref PositionComponent position)
{
    var tileWidth = TileSizeHelper.GetTileWidth(World, _constants);
    var tileHeight = TileSizeHelper.GetTileHeight(World, _constants);
    position.SyncPixelsToGrid(tileWidth, tileHeight);
}
```

**Purpose**: Ensure grid coordinates match pixel coordinates

---

## 5. Grid-to-Pixel and Pixel-to-Grid Conversions

### 5.1 PositionComponent.SyncPixelsToGrid()

**File**: `/mnt/c/Users/nate0/RiderProjects/MonoBall/MonoBall/MonoBall.Core/ECS/Components/PositionComponent.cs` (Lines 68-74)

```csharp
public void SyncPixelsToGrid(int tileWidth = 16, int tileHeight = 16)
{
    X = (int)(PixelX / tileWidth);
    Y = (int)(PixelY / tileHeight);
    // NOTE: Do NOT snap PixelX/PixelY - this breaks smooth movement interpolation
}
```

**Formula**: Grid = Pixel / TileSize (integer division)

### 5.2 Movement Target Calculation (MovementSystem.cs)

**File**: `/mnt/c/Users/nate0/RiderProjects/MonoBall/MonoBall/MonoBall.Core/ECS/Systems/MovementSystem.cs` (Lines 192-195)

```csharp
var tileWidth = TileSizeHelper.GetTileWidth(World, _constants);
var tileHeight = TileSizeHelper.GetTileHeight(World, _constants);
var startPosition = new Vector2(position.PixelX, position.PixelY);
var targetPosition = new Vector2(targetX * tileWidth, targetY * tileHeight);
```

**Formula**: PixelTarget = GridTarget * TileSize

### 5.3 Player Creation (PlayerSystem.cs)

**File**: `/mnt/c/Users/nate0/RiderProjects/MonoBall/MonoBall/MonoBall.Core/ECS/Systems/PlayerSystem.cs` (Lines 212-215)

```csharp
var gridX = (int)(position.X / tileWidth);
var gridY = (int)(position.Y / tileHeight);
float pixelX = gridX * tileWidth;  // Snapped to tile
float pixelY = gridY * tileHeight; // Snapped to tile
```

**Formula**: Snaps pixel position to nearest tile boundary

### 5.4 Camera Pixel-to-Tile Conversion (CameraSystem.cs)

**File**: `/mnt/c/Users/nate0/RiderProjects/MonoBall/MonoBall/MonoBall.Core/ECS/Systems/CameraSystem.cs` (Lines 114-126)

```csharp
private static Vector2 ConvertPixelToTile(Vector2 pixelPosition, int tileWidth, int tileHeight)
{
    return new Vector2(pixelPosition.X / tileWidth, pixelPosition.Y / tileHeight);
}
```

**Note**: This returns float tile coordinates (not integer)

---

## 6. Position Usage in Rendering

### 6.1 Sprite Rendering (SpriteRenderer.cs)

**File**: `/mnt/c/Users/nate0/RiderProjects/MonoBall/MonoBall/MonoBall.Core/ECS/Rendering/SpriteRenderer.cs` (Lines 106-116)

```csharp
spriteBatch.Draw(
    spriteTexture,
    pos.Position,    // Uses PixelX, PixelY (TOP-LEFT of sprite)
    frameRect,
    color,
    0.0f,
    Vector2.Zero,    // Origin at TOP-LEFT (no offset)
    1.0f,
    spriteEffects,
    0.0f
);
```

**Critical**: `pos.Position` returns `(PixelX, PixelY)` which is the TOP-LEFT corner
**Origin**: `Vector2.Zero` means no offset from position

### 6.2 Elevation-Based Sorting (ElevationRendererSystem.cs)

**File**: `/mnt/c/Users/nate0/RiderProjects/MonoBall/MonoBall/MonoBall.Core/ECS/Systems/ElevationRendererSystem.cs`

For tile chunks (Lines 372-373):
```csharp
// Use bottom edge of chunk for Y-sorting
var chunkBottomY = pos.Position.Y + chunk.ChunkHeight * tilesetDef.TileHeight;
```

For sprites (Lines 480-481):
```csharp
// Use bottom edge of sprite for Y-sorting
var spriteBottomY = pos.Position.Y + spriteDef.FrameHeight;
```

**Important**: Y-sorting uses BOTTOM edge (`position.Y + height`), not top

### 6.3 Sprite Bounds for Culling (ElevationRendererSystem.cs)

**File**: `/mnt/c/Users/nate0/RiderProjects/MonoBall/MonoBall/MonoBall.Core/ECS/Systems/ElevationRendererSystem.cs` (Lines 470-475)

```csharp
var spriteBounds = new Rectangle(
    (int)pos.Position.X,      // TOP-LEFT X
    (int)pos.Position.Y,      // TOP-LEFT Y
    spriteDef.FrameWidth,
    spriteDef.FrameHeight
);
```

**Assumption**: Position is TOP-LEFT; sprite extends RIGHT and DOWN from that point

### 6.4 Tile Chunk Bounds for Culling (ElevationRendererSystem.cs)

**File**: `/mnt/c/Users/nate0/RiderProjects/MonoBall/MonoBall/MonoBall.Core/ECS/Systems/ElevationRendererSystem.cs` (Lines 362-367)

```csharp
var chunkBounds = new Rectangle(
    (int)pos.Position.X,
    (int)pos.Position.Y,
    chunk.ChunkWidth * tilesetDef.TileWidth,
    chunk.ChunkHeight * tilesetDef.TileHeight
);
```

---

## 7. Position Usage in Collision

### 7.1 SpatialHashSystem - Entity Position Tracking

**File**: `/mnt/c/Users/nate0/RiderProjects/MonoBall/MonoBall/MonoBall.Core/ECS/Systems/SpatialHashSystem.cs` (Line 192)

```csharp
var key = (position.X, position.Y, (int)elevation.Value);
```

**Uses**: Grid coordinates (X, Y) NOT pixel coordinates
**Assumption**: Entity occupies the tile at grid position (X, Y)

### 7.2 CollisionService - Tile Collision

**File**: `/mnt/c/Users/nate0/RiderProjects/MonoBall/MonoBall/MonoBall.Core/ECS/Services/CollisionService.cs`

All collision checks use grid coordinates (targetX, targetY)
**Assumption**: Entity occupies a single tile at grid position

### 7.3 InteractionSystem - Adjacency Check

**File**: `/mnt/c/Users/nate0/RiderProjects/MonoBall/MonoBall/MonoBall.Core/ECS/Systems/InteractionSystem.cs` (Lines 173-176)

```csharp
var tileDistanceX = Math.Abs(playerTileX - interactionTileX);
var tileDistanceY = Math.Abs(playerTileY - interactionTileY);
var tileDistance = tileDistanceX + tileDistanceY; // Manhattan distance
```

**Uses**: Grid coordinates for tile-based distance calculation

---

## 8. Movement Request to Position Update Flow

### Complete Flow Diagram:

```
1. InputSystem (Input)
   - Detects directional input
   - Creates MovementRequest component with direction

2. MovementSystem.ProcessMovementRequests()
   - Reads current position.X, position.Y (grid)
   - Calculates targetX, targetY (grid + delta)
   - Calls CollisionService.CanMoveTo(targetX, targetY)
   - If allowed:
     a. Creates start/target pixel positions
     b. Calls movement.StartMovement(startPixel, targetPixel)
     c. IMMEDIATELY updates position.X = targetX, position.Y = targetY

3. MovementSystem.UpdateMovements()
   - If movement.IsMoving:
     a. Updates movement.MovementProgress
     b. Calls InterpolatePosition()
        - Lerps position.PixelX between start and target
        - Lerps position.PixelY between start and target
   - When movement.MovementProgress >= 1.0:
     a. Snaps position.PixelX/Y to target
     b. Calls SyncPositionToGrid()
     c. Calls movement.CompleteMovement()

4. Rendering
   - ElevationRendererSystem collects sprites
   - Uses position.Position (PixelX, PixelY) for drawing
   - Sorts by position.Y + height for depth ordering
```

---

## 9. Coordinate System Assumptions Summary

| System | Uses Grid (X, Y) | Uses Pixel (PixelX, PixelY) | Position Meaning |
|--------|------------------|----------------------------|------------------|
| CollisionService | YES | NO | Tile entity occupies |
| SpatialHashSystem | YES | NO | Tile entity occupies |
| InteractionSystem | YES | NO | Tile for distance calc |
| MovementSystem | BOTH | BOTH | Grid for logic, Pixel for movement |
| SpriteRenderer | NO | YES | TOP-LEFT of sprite |
| ElevationRenderer | NO | YES | TOP-LEFT + height for sorting |
| CameraSystem | NO | YES (via Position) | TOP-LEFT, calculates center |
| DebugBar | BOTH | BOTH | Display purposes |

---

## 10. Key Findings

### 10.1 Position Represents TOP-LEFT

Throughout the codebase, position consistently represents the TOP-LEFT corner of the entity's sprite:
- Rendering draws at Position with no origin offset
- Camera calculates center by adding half-dimensions to Position
- Bounds rectangles start at Position and extend right/down
- Y-sorting uses Position.Y + Height (bottom edge)

### 10.2 Grid vs Pixel Coordinate Separation

- **Grid coordinates (X, Y)**: Used for game logic (collision, interaction, spatial queries)
- **Pixel coordinates (PixelX, PixelY)**: Used for smooth rendering during movement

### 10.3 Movement Updates Grid Position Immediately

When movement starts, the grid position (X, Y) is updated to the TARGET position immediately. This means:
- Collision checking uses the target tile during movement
- Entity is considered to occupy the target tile while moving toward it
- Pixel coordinates interpolate smoothly from start to target

### 10.4 No Position Offset for "Feet" Position

The codebase does NOT currently implement a "feet position" offset. All systems use the TOP-LEFT corner as the reference point. If a "feet at tile" system is desired, it would need to be added consistently across:
- Movement target calculation
- Collision checking
- Interaction distance calculation
- Y-sorting for depth ordering
- Rendering offset

---

## 11. Files Referenced

1. `/mnt/c/Users/nate0/RiderProjects/MonoBall/MonoBall/MonoBall.Core/ECS/Components/PositionComponent.cs`
2. `/mnt/c/Users/nate0/RiderProjects/MonoBall/MonoBall/MonoBall.Core/ECS/Components/GridMovement.cs`
3. `/mnt/c/Users/nate0/RiderProjects/MonoBall/MonoBall/MonoBall.Core/ECS/Systems/PlayerSystem.cs`
4. `/mnt/c/Users/nate0/RiderProjects/MonoBall/MonoBall/MonoBall.Core/ECS/Systems/MovementSystem.cs`
5. `/mnt/c/Users/nate0/RiderProjects/MonoBall/MonoBall/MonoBall.Core/ECS/Systems/InputSystem.cs`
6. `/mnt/c/Users/nate0/RiderProjects/MonoBall/MonoBall/MonoBall.Core/ECS/Systems/MapLoaderSystem.cs`
7. `/mnt/c/Users/nate0/RiderProjects/MonoBall/MonoBall/MonoBall.Core/ECS/Systems/InteractionSystem.cs`
8. `/mnt/c/Users/nate0/RiderProjects/MonoBall/MonoBall/MonoBall.Core/ECS/Systems/SpatialHashSystem.cs`
9. `/mnt/c/Users/nate0/RiderProjects/MonoBall/MonoBall/MonoBall.Core/ECS/Systems/CameraSystem.cs`
10. `/mnt/c/Users/nate0/RiderProjects/MonoBall/MonoBall/MonoBall.Core/ECS/Systems/ElevationRendererSystem.cs`
11. `/mnt/c/Users/nate0/RiderProjects/MonoBall/MonoBall/MonoBall.Core/ECS/Rendering/SpriteRenderer.cs`
12. `/mnt/c/Users/nate0/RiderProjects/MonoBall/MonoBall/MonoBall.Core/ECS/Rendering/RenderableItem.cs`
13. `/mnt/c/Users/nate0/RiderProjects/MonoBall/MonoBall/MonoBall.Core/ECS/Services/CollisionService.cs`
14. `/mnt/c/Users/nate0/RiderProjects/MonoBall/MonoBall/MonoBall.Core/ECS/Services/EntityQueryService.cs`
15. `/mnt/c/Users/nate0/RiderProjects/MonoBall/MonoBall/MonoBall.Core/Scripting/ScriptApiProvider.cs`
16. `/mnt/c/Users/nate0/RiderProjects/MonoBall/MonoBall/MonoBall.Core/Scenes/Systems/DebugBarSceneSystem.cs`
17. `/mnt/c/Users/nate0/RiderProjects/MonoBall/MonoBall/MonoBall.Core/ECS/Services/ActiveMapFilterService.cs`
