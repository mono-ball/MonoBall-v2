# Bridge Elevation Analysis: Do We Need Special Case?

## Question

If a bridge is at elevation 15 and an entity is at elevation 3, wouldn't collision not happen due to elevation mismatch? Why do we need special handling for elevation 15?

## Analysis

### Current Logic (With Special Case)

```csharp
bool IsElevationMatch(byte entityElevation, byte tileElevation)
{
    // Entity elevation 0 = wildcard (matches any)
    if (entityElevation == 0)
        return true;
    
    // Tile elevation 0 or 15 = no mismatch (special cases)
    if (tileElevation == 0 || tileElevation == 15)
        return true;
    
    // Must match exactly
    return entityElevation == tileElevation;
}
```

### Scenario Analysis

**Scenario 1: Entity at elevation 3, Bridge tile at elevation 15**
- Without special case: `3 != 15` → **MISMATCH** → Blocked ❌
- With special case: `tileElevation == 15` → **NO MISMATCH** → Can walk ✅

**Scenario 2: Entity at elevation 15, Bridge tile at elevation 15**
- Without special case: `15 == 15` → **MATCH** → Can walk ✅
- With special case: `tileElevation == 15` → **NO MISMATCH** → Can walk ✅

**Scenario 3: Entity at elevation 3, Regular tile at elevation 4**
- Without special case: `3 != 4` → **MISMATCH** → Blocked ✅
- With special case: `3 != 4` → **MISMATCH** → Blocked ✅

## The Problem

The special case for elevation 15 allows entities at **any elevation** to walk on bridge tiles, not just walk UNDER them.

**Issue**: An entity at elevation 3 can walk ON a bridge at elevation 15, which doesn't make physical sense.

## What Pokeemerald Actually Does

Looking at `ObjectEventUpdateElevation()`:

```c
void ObjectEventUpdateElevation(struct ObjectEvent *objEvent, struct Sprite *sprite)
{
    u8 curElevation = MapGridGetElevationAt(objEvent->currentCoords.x, objEvent->currentCoords.y);
    u8 prevElevation = MapGridGetElevationAt(objEvent->previousCoords.x, objEvent->previousCoords.y);

    if (curElevation == 15 || prevElevation == 15)
    {
        // Ignore subsprite priorities under bridges
        // so all subsprites will display below it
        if (OW_LARGE_OW_SUPPORT)
            sprite->subspriteMode = SUBSPRITES_IGNORE_PRIORITY;
        return;  // ⚠️ Doesn't update currentElevation!
    }

    objEvent->currentElevation = curElevation;
    // ...
}
```

**Key Insight**: When an entity steps on elevation 15, `currentElevation` is NOT updated. The entity keeps its previous elevation.

**This means**:
- Entity at elevation 3 steps on bridge (elevation 15)
- Entity's `currentElevation` stays at 3 (not updated to 15)
- Entity can walk on bridge because `IsElevationMismatchAt()` returns FALSE for elevation 15 tiles
- But entity's elevation remains 3, so it's "walking under" the bridge from a rendering perspective

## The Real Purpose of Elevation 15

Elevation 15 is used for **rendering priority**, not collision:

1. **Rendering**: Elevation 15 always renders on top (highest priority)
2. **Collision**: Elevation 15 tiles don't cause elevation mismatch (entities can walk under/on them)
3. **Entity Elevation**: Entities don't update their elevation to 15 when stepping on bridges

## Do We Need the Special Case?

**YES**, but the reasoning is different than we thought:

1. **Bridges are passable tiles** - They have collision override = 0 (passable)
2. **Elevation 15 = "always passable"** - The special case allows entities to walk on bridges regardless of their elevation
3. **Rendering vs Collision** - Elevation 15 is primarily for rendering priority, not collision logic

## Alternative Understanding

Maybe bridges work like this:

- **Bridge tiles**: Elevation 15 (for rendering), Collision override = 0 (passable)
- **Entity at elevation 3**: Can walk on bridge because:
  1. Collision override = 0 → Passable ✅
  2. Elevation mismatch check → Special case for 15 → No mismatch ✅
  3. Entity elevation stays 3 (not updated to 15)

So entities "walk on" bridges but remain at their original elevation for rendering purposes.

## Recommendation

**Keep the special case for elevation 15**, but clarify its purpose:

1. **Elevation 15 = Rendering Priority**: Always renders on top
2. **Elevation 15 = Collision Passable**: Entities can walk on bridges regardless of their elevation
3. **Entity Elevation Unchanged**: Entities don't update to elevation 15 when stepping on bridges

The special case is needed to allow entities to walk on bridges. Without it, entities at elevation 3 would be blocked from walking on bridges at elevation 15.

## Updated Design

```csharp
/// <summary>
/// Checks if an entity elevation matches a tile elevation.
/// Handles special cases:
/// - Entity elevation 0 = wildcard (matches any tile elevation)
/// - Tile elevation 0 = ground level (matches any entity elevation)
/// - Tile elevation 15 = bridges/overhead (always passable, no mismatch)
/// - Otherwise, must match exactly.
/// </summary>
/// <remarks>
/// Elevation 15 is special: It's used for rendering priority (always on top)
/// and allows entities at any elevation to walk on bridge/overhead tiles.
/// Entities don't update their elevation to 15 when stepping on these tiles.
/// </remarks>
bool IsElevationMatch(byte entityElevation, byte tileElevation)
{
    // Entity elevation 0 = wildcard (matches any)
    if (entityElevation == 0)
        return true;
    
    // Tile elevation 0 = ground level (matches any entity elevation)
    if (tileElevation == 0)
        return true;
    
    // Tile elevation 15 = bridges/overhead (always passable, no mismatch)
    // This allows entities at any elevation to walk on bridges
    if (tileElevation == 15)
        return true;
    
    // Must match exactly
    return entityElevation == tileElevation;
}
```

## Updated Recommendation (After User Feedback)

**NO, we should NOT have a special case for elevation 15**. Instead:

1. **Check collision override FIRST**: If collision override = 0 (passable), allow movement regardless of elevation mismatch
2. **Check elevation mismatch ONLY if solid**: If collision override > 0 (solid tile), then check elevation mismatch
3. **Natural elevation changes**: Entities can change elevation by moving to tiles with different elevations (stairs, ramps, etc.)
4. **Bridges work naturally**: Bridges have collision override = 0, so they're passable from any elevation

This approach:
- ✅ Allows natural elevation changes via stairs, ramps, etc.
- ✅ Bridges work correctly (passable from any elevation if collision override = 0)
- ✅ No special cases needed
- ✅ Cleaner, more maintainable code

## Conclusion

**Remove the special case for elevation 15**. Instead, check collision override first, and only check elevation mismatch if the tile is solid. This enables natural elevation changes and is a better design than pokeemerald's approach.

