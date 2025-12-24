# Map Filtering Architecture - Implementation Summary

## Overview
Implemented a reusable map filtering system that addresses both architecture concerns and query iteration performance issues.

---

## 🎯 Problems Solved

### 1. **Architecture: Reusable Map Filtering**
- **Before:** Map filtering logic duplicated in MovementSystem
- **After:** Centralized in `IActiveMapFilterService` for reuse across systems

### 2. **Performance: Query Iteration Overhead**
- **Before:** Arch ECS queries iterate over ALL entities, even with early returns in lambda
- **After:** Use `ActiveMapEntity` tag component to filter at query level, avoiding iteration over inactive entities

---

## 📦 New Components & Services

### 1. `ActiveMapEntity` Component (Tag)
**Location:** `MonoBall.Core.ECS.Components/ActiveMapEntity.cs`

Zero-size tag component that marks entities as being in an active (loaded) map.

**Usage:**
```csharp
// Query only entities in active maps
var query = new QueryDescription().WithAll<PositionComponent, GridMovement, ActiveMapEntity>();
```

**Benefits:**
- Arch ECS only iterates over entities with this component
- No lambda-level filtering needed
- Query-level performance optimization

---

### 2. `IActiveMapFilterService` Interface
**Location:** `MonoBall.Core.ECS.Services/IActiveMapFilterService.cs`

Service interface for map filtering operations.

**Methods:**
- `GetActiveMapIds()` - Returns set of active map IDs (cached)
- `IsEntityInActiveMaps(Entity)` - Checks if entity is in active maps
- `GetEntityMapId(Entity)` - Gets map ID for an entity

---

### 3. `ActiveMapFilterService` Implementation
**Location:** `MonoBall.Core.ECS.Services/ActiveMapFilterService.cs`

**Features:**
- Cached active map IDs (invalidated on map load/unload)
- Efficient component lookups using `TryGet<>`
- Handles NPCs, Player, and Map entities

**Cache Strategy:**
- Caches active map IDs
- Invalidates when map entity count changes
- Can be manually invalidated via `InvalidateCache()`

---

### 4. `ActiveMapManagementSystem`
**Location:** `MonoBall.Core.ECS.Systems/ActiveMapManagementSystem.cs`

**Responsibilities:**
- Manages `ActiveMapEntity` tag component lifecycle
- Adds tag to entities in active maps
- Removes tag when maps are unloaded
- Subscribes to `MapLoadedEvent` and `MapUnloadedEvent`

**Update Logic:**
- Runs every frame to handle entities created after map load
- Adds `ActiveMapEntity` to NPCs in active maps
- Always adds `ActiveMapEntity` to player (player is always in active map)

---

## 🔄 Updated Systems

### MovementSystem
**Changes:**
- ✅ Uses `IActiveMapFilterService` instead of internal map filtering logic
- ✅ Queries include `ActiveMapEntity` tag for query-level filtering
- ✅ Removed duplicate map filtering code
- ✅ Uses service's `GetEntityMapId()` instead of internal method

**Query Updates:**
```csharp
// Before: Query all, filter in lambda
_movementQuery = new QueryDescription().WithAll<PositionComponent, GridMovement>();

// After: Query only active entities at query level
_movementQueryWithActiveMap = new QueryDescription().WithAll<
    PositionComponent,
    GridMovement,
    ActiveMapEntity
>();
```

**Performance Impact:**
- **Before:** Iterates over ALL entities with GridMovement, filters in lambda
- **After:** Arch ECS only iterates over entities with `ActiveMapEntity` tag
- **Savings:** Eliminates iteration over entities in unloaded maps entirely

---

### VisibilityFlagSystem
**Changes:**
- ✅ Query includes `ActiveMapEntity` tag
- ✅ Only processes NPCs in active maps

**Query Update:**
```csharp
// Before:
_queryDescription = new QueryDescription().WithAll<NpcComponent, RenderableComponent>();

// After:
_queryDescription = new QueryDescription().WithAll<
    NpcComponent,
    RenderableComponent,
    ActiveMapEntity
>();
```

---

## 🏗️ System Registration

### SystemManager Updates
**Changes:**
1. Creates `ActiveMapFilterService` before MovementSystem
2. Creates `ActiveMapManagementSystem` 
3. Updates MovementSystem constructor to include `IActiveMapFilterService`
4. Adds `ActiveMapManagementSystem` to update systems (runs early, before systems that filter)

**Order Matters:**
- `ActiveMapManagementSystem` runs early to tag entities
- Other systems can then query with `ActiveMapEntity` tag

---

## 📊 Performance Improvements

### Query Iteration
| System | Before | After | Improvement |
|--------|--------|-------|-------------|
| MovementSystem | Iterates all GridMovement entities | Only iterates ActiveMapEntity + GridMovement | **Eliminates inactive entities** |
| VisibilityFlagSystem | Iterates all NPCs | Only iterates ActiveMapEntity + NPCs | **Eliminates inactive NPCs** |

### Component Access
- All `World.Has<>()` calls replaced with `World.TryGet<>()` (more efficient)
- Service provides cached active map IDs (avoids per-frame queries)

---

## 🔌 Usage in Other Systems

### Example: Adding Map Filtering to a New System

```csharp
public class MySystem : BaseSystem<World, float>
{
    private readonly IActiveMapFilterService _activeMapFilterService;
    private readonly QueryDescription _myQuery;

    public MySystem(World world, IActiveMapFilterService activeMapFilterService)
        : base(world)
    {
        _activeMapFilterService = activeMapFilterService;
        
        // Include ActiveMapEntity tag for query-level filtering
        _myQuery = new QueryDescription().WithAll<
            MyComponent,
            ActiveMapEntity  // Only query entities in active maps
        >();
    }

    public override void Update(in float deltaTime)
    {
        // Arch ECS only iterates over entities in active maps
        World.Query(in _myQuery, (Entity entity, ref MyComponent comp) =>
        {
            // No need to filter - query already filtered at query level
            // Process entity...
        });
    }
}
```

---

## ✅ Benefits

1. **Reusable:** Map filtering logic centralized in service
2. **Performant:** Query-level filtering eliminates iteration overhead
3. **Maintainable:** Single source of truth for active map logic
4. **Extensible:** Easy to add map filtering to new systems
5. **Type-Safe:** Service interface provides clear contract

---

## 🎯 Future Improvements (Optional)

1. **Spatial Hash Integration:** Could integrate with spatial hash for even better performance
2. **Event-Based Cache:** Could use events for more precise cache invalidation
3. **Component-Based Filtering:** Could add more tag components for different filtering needs

---

## 📝 Files Created/Modified

### Created:
- `MonoBall.Core.ECS.Components/ActiveMapEntity.cs`
- `MonoBall.Core.ECS.Services/IActiveMapFilterService.cs`
- `MonoBall.Core.ECS.Services/ActiveMapFilterService.cs`
- `MonoBall.Core.ECS.Systems/ActiveMapManagementSystem.cs`

### Modified:
- `MonoBall.Core.ECS.Systems/MovementSystem.cs`
- `MonoBall.Core.ECS.Systems/VisibilityFlagSystem.cs`
- `MonoBall.Core.ECS.Systems/SpriteRendererSystem.cs`
- `MonoBall.Core.ECS.Systems/SpriteAnimationSystem.cs`
- `MonoBall.Core.ECS.Systems/MapLoaderSystem.cs` (adds ActiveMapEntity tag when creating NPCs)
- `MonoBall.Core.ECS.Systems/ActiveMapManagementSystem.cs` (optimized to use TryGet<>)
- `MonoBall.Core.ECS/SystemManager.cs`

---

## 🚀 Next Steps

1. ✅ **DONE:** Create map filtering service
2. ✅ **DONE:** Create ActiveMapEntity tag component
3. ✅ **DONE:** Create ActiveMapManagementSystem
4. ✅ **DONE:** Update MovementSystem to use service and tag
5. ✅ **DONE:** Update VisibilityFlagSystem to use tag
6. ✅ **DONE:** Register systems in SystemManager
7. ✅ **DONE:** Update other systems that query NPCs (SpriteRendererSystem, SpriteAnimationSystem) to use ActiveMapEntity tag
8. ⚠️ **OPTIONAL:** Add unit tests for ActiveMapFilterService

