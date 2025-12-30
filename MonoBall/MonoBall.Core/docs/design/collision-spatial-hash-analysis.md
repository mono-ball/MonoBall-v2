# Spatial Hash vs Simple Query Analysis

## Current State

### InteractionSystem Approach
- **Method**: Queries ALL entities with `InteractionComponent` + `PositionComponent`
- **Filtering**: Calculates Manhattan distance, filters by `distance == 1`
- **Performance**: O(n) where n = number of interaction entities
- **Use Case**: "Find entities near player" (distance-based)

### Collision Detection Needs
- **Query**: "Are there any entities at position (targetX, targetY)?"
- **Filtering**: Exact position match (`pos.X == targetX && pos.Y == targetY`)
- **Performance**: O(n) if using simple query, O(1) with spatial hash
- **Use Case**: "Find entities at specific position" (position-based)

## Performance Analysis

### Map Scale Considerations
- **Map Size**: 1000+ tiles (e.g., 50x20, 64x16, or larger)
- **Multiple Layers**: 3-5 layers per map (different elevations)
- **Total Tiles**: 1000+ tiles × 3-5 layers = 3000-5000 tile positions
- **Entities**: NPCs, items, player, pushable objects, etc.
- **Query Frequency**: 
  - Collision checks: Every movement request (potentially many per frame)
  - Interaction checks: When player presses interact button
  - Both need position-based queries frequently

### Without Spatial Hash (Simple Query)
```csharp
// Query all entities, filter by position
World.Query(
    in _entityQuery,
    (Entity entity, ref PositionComponent pos, ref CollisionComponent collision) =>
    {
        if (pos.X == targetX && pos.Y == targetY && pos.Elevation == elevation)
        {
            if (collision.IsSolid)
                return false; // Blocked
        }
    }
);
```

**Performance**: O(n) where n = all entities with `CollisionComponent`
- Typical map: ~50-200 NPCs + player + items = ~100-200 entities
- Called: Every movement request (potentially many per frame)
- Cost: ~100-200 iterations per collision check
- **Problem**: Even with only 100 entities, iterating all of them for every collision check is wasteful

### With Spatial Hash
```csharp
// O(1) lookup by position
var entities = _spatialHash.GetEntitiesAt(mapId, targetX, targetY, elevation);
foreach (var entity in entities)
{
    if (collision.IsSolid)
        return false; // Blocked
}
```

**Performance**: O(1) lookup + O(m) iteration where m = entities at that position
- Typical map: ~100-200 entities total, but only 0-2 per tile
- Called: Every movement request
- Cost: ~1-2 iterations per collision check
- **Benefit**: Only checks entities actually at the position, not all entities

### Performance Comparison

**Scenario**: Map with 100 NPCs, checking collision at position (25, 15)

**Without Spatial Hash**:
- Query all 100 NPCs
- Check position match for each
- ~100 iterations (even though only 0-2 NPCs are at that position)

**With Spatial Hash**:
- O(1) lookup to get entities at (25, 15)
- Iterate only entities at that position
- ~0-2 iterations (only entities actually there)

**With frequent collision checks** (e.g., player moving, NPCs moving):
- Without spatial hash: 100 iterations × many checks = thousands of wasted iterations
- With spatial hash: 1-2 iterations × many checks = minimal wasted work

## Recommendation: Use Spatial Hash

### Proposed: `IEntityPositionService` with Spatial Hash

Create a service that both `InteractionSystem` and `CollisionService` can use:

```csharp
public interface IEntityPositionService
{
    /// <summary>
    /// Gets all entities at a specific tile position with matching elevation.
    /// </summary>
    ReadOnlySpan<Entity> GetEntitiesAt(string mapId, int x, int y, int elevation);
    
    /// <summary>
    /// Gets all entities at a specific tile position (any elevation).
    /// </summary>
    ReadOnlySpan<Entity> GetEntitiesAt(string mapId, int x, int y);
    
    /// <summary>
    /// Gets entities within a certain distance of a position.
    /// Useful for interaction system (find entities near player).
    /// </summary>
    ReadOnlySpan<Entity> GetEntitiesInRange(string mapId, int centerX, int centerY, int range, int elevation);
}
```

### Implementation: Spatial Hash System

```csharp
/// <summary>
/// System that maintains spatial hash of entity positions.
/// Re-indexes dynamic entities each frame for accurate collision/interaction queries.
/// </summary>
public class SpatialHashSystem : BaseSystem<World, float>, IEntityPositionService
{
    // Map ID -> (X, Y, Elevation) -> List<Entity>
    private readonly Dictionary<string, Dictionary<(int x, int y, int elevation), List<Entity>>> _spatialHash;
    
    // Reusable buffer for range queries
    private readonly List<Entity> _rangeQueryBuffer = new();
    
    private readonly QueryDescription _positionQuery;
    private readonly IActiveMapFilterService _activeMapFilterService;
    
    public SpatialHashSystem(
        World world,
        IActiveMapFilterService activeMapFilterService
    ) : base(world)
    {
        _activeMapFilterService = activeMapFilterService 
            ?? throw new ArgumentNullException(nameof(activeMapFilterService));
        _spatialHash = new Dictionary<string, Dictionary<(int x, int y, int elevation), List<Entity>>>();
        _positionQuery = new QueryDescription()
            .WithAll<PositionComponent, ActiveMapEntity>();
    }
    
    public override void Update(in float deltaTime)
    {
        // Re-index all dynamic entities each frame
        ReindexEntities();
    }
    
    private void ReindexEntities()
    {
        // Clear hash
        foreach (var mapHash in _spatialHash.Values)
        {
            mapHash.Clear();
        }
        
        // Index all entities with positions
        World.Query(
            in _positionQuery,
            (Entity entity, ref PositionComponent pos) =>
            {
                string? mapId = _activeMapFilterService.GetEntityMapId(entity);
                if (mapId == null)
                    return;
                
                int elevation = GetEntityElevation(entity);
                var key = (pos.X, pos.Y, elevation);
                
                if (!_spatialHash.TryGetValue(mapId, out var mapHash))
                {
                    mapHash = new Dictionary<(int x, int y, int elevation), List<Entity>>();
                    _spatialHash[mapId] = mapHash;
                }
                
                if (!mapHash.TryGetValue(key, out var entities))
                {
                    entities = new List<Entity>();
                    mapHash[key] = entities;
                }
                
                entities.Add(entity);
            }
        );
    }
    
    public ReadOnlySpan<Entity> GetEntitiesAt(string mapId, int x, int y, int elevation)
    {
        if (!_spatialHash.TryGetValue(mapId, out var mapHash))
            return ReadOnlySpan<Entity>.Empty;
        
        var key = (x, y, elevation);
        if (!mapHash.TryGetValue(key, out var entities))
            return ReadOnlySpan<Entity>.Empty;
        
        return CollectionsMarshal.AsSpan(entities);
    }
    
    public ReadOnlySpan<Entity> GetEntitiesAt(string mapId, int x, int y)
    {
        if (!_spatialHash.TryGetValue(mapId, out var mapHash))
            return ReadOnlySpan<Entity>.Empty;
        
        _rangeQueryBuffer.Clear();
        
        // Check all elevations at this position
        for (int elevation = 0; elevation <= 15; elevation++)
        {
            var key = (x, y, elevation);
            if (mapHash.TryGetValue(key, out var entities))
            {
                _rangeQueryBuffer.AddRange(entities);
            }
        }
        
        return CollectionsMarshal.AsSpan(_rangeQueryBuffer);
    }
    
    public ReadOnlySpan<Entity> GetEntitiesInRange(string mapId, int centerX, int centerY, int range, int elevation)
    {
        if (!_spatialHash.TryGetValue(mapId, out var mapHash))
            return ReadOnlySpan<Entity>.Empty;
        
        _rangeQueryBuffer.Clear();
        
        // Check all positions within range (Manhattan distance)
        for (int dx = -range; dx <= range; dx++)
        {
            for (int dy = -range; dy <= range; dy++)
            {
                int distance = Math.Abs(dx) + Math.Abs(dy);
                if (distance > range)
                    continue; // Outside range
                
                var key = (centerX + dx, centerY + dy, elevation);
                if (mapHash.TryGetValue(key, out var entities))
                {
                    _rangeQueryBuffer.AddRange(entities);
                }
            }
        }
        
        return CollectionsMarshal.AsSpan(_rangeQueryBuffer);
    }
    
    private int GetEntityElevation(Entity entity)
    {
        // Check PlayerComponent
        if (World.TryGet<PlayerComponent>(entity, out _))
        {
            // Would need IConstantsService injected
            return 3; // Default player elevation
        }
        
        // Check NpcComponent
        if (World.TryGet<NpcComponent>(entity, out var npc))
        {
            return npc.Elevation;
        }
        
        return 0; // Default elevation
    }
}
```

### Benefits of Spatial Hash Approach

1. **DRY**: Both systems use same service
2. **Efficient**: O(1) lookup instead of O(n) iteration
3. **Scalable**: Performance doesn't degrade with more entities
4. **Range Queries**: Easy to implement `GetEntitiesInRange()` for interactions
5. **Testable**: Interface allows mocking
6. **Future-Proof**: Handles large maps and many entities efficiently

### Updated Collision Design

**Remove**: `SpatialHashSystem` from Phase 2
**Add**: `IEntityPositionService` to Phase 2

```csharp
public class CollisionService : ICollisionService
{
    private readonly IEntityPositionService _entityPositionService;
    
    private bool CheckEntityCollision(string mapId, int x, int y, int elevation, Entity movingEntity)
    {
        var entitiesAtPosition = _entityPositionService.GetEntitiesAt(mapId, x, y, elevation);
        
        foreach (var entity in entitiesAtPosition)
        {
            if (entity == movingEntity)
                continue; // Skip self
            
            if (World.TryGet<CollisionComponent>(entity, out var collision))
            {
                if (collision.IsSolid && !collision.AllowPassThrough)
                    return false; // Blocked by entity
            }
        }
        
        return true; // No blocking entities
    }
}
```

## Conclusion

**Recommendation**: Use `IEntityPositionService` with spatial hash implementation. This:
- ✅ Reuses code between InteractionSystem and CollisionService
- ✅ Provides O(1) lookups instead of O(n) iteration
- ✅ Scales well with large maps (1000+ tiles) and many entities
- ✅ Efficient for frequent collision checks (every movement request)
- ✅ Supports range queries for interaction system
- ✅ Worth the complexity given map scale and query frequency

**Rationale**:
- Maps have 1000+ tiles with multiple layers
- Collision checks happen frequently (every movement request)
- Interaction checks also benefit from spatial hash
- Even with ~100 entities, O(n) iteration is wasteful when only 0-2 entities are at each position
- Spatial hash provides significant performance benefit for minimal complexity

