# Arch Relationships Utilization Opportunities

**Date:** 2025-01-27  
**Status:** Analysis

---

## Overview

This document identifies areas in the MonoBall codebase where Arch.Extended relationships can replace current patterns that use:
- Entity references stored in components (can become stale)
- Dictionary-based tracking of parent-child relationships
- String-based IDs for entity associations

Following the pattern established in the UI/Scene refactor, relationships provide:
- **Automatic cleanup** when parent entities are destroyed
- **Efficient queries** via relationship iteration
- **Type safety** with relationship types
- **No stale references** - relationships are automatically invalidated

---

## Current Relationship Usage

### UI System (Already Implemented)
- **`OwnsUIElement`**: Scene → UI element ownership
- **`ContainsUIElement`**: Window → child UI element ownership

**Location:** `MonoBall.Core.UI.Relationships/`

### Correct Arch.Extended Relationship API

**Adding Relationships:**
```csharp
// Correct: World.AddRelationship(parent, child, relationship)
World.AddRelationship(mapEntity, chunkEntity, new OwnsTileChunk());
```

**Querying Relationships:**
```csharp
// Correct: World.GetRelationships<T>(entity) returns Dictionary<Entity, T>
var relationships = World.GetRelationships<OwnsTileChunk>(mapEntity);
foreach (var kvp in relationships)
{
    var childEntity = kvp.Key;        // Entity
    var relationship = kvp.Value;     // OwnsTileChunk instance
    
    if (!World.IsAlive(childEntity))
        continue;
    
    // Process child entity
}
```

**Validation Pattern:**
```csharp
// Always validate entities before relationship operations
if (!World.IsAlive(parentEntity))
    throw new InvalidOperationException($"Parent entity {parentEntity.Id} is not alive.");

if (!World.Has<RequiredComponent>(parentEntity))
    throw new InvalidOperationException($"Parent entity {parentEntity.Id} missing required component.");

// Add relationship with error handling
try
{
    World.AddRelationship(parentEntity, childEntity, new RelationshipType());
}
catch (Exception ex)
{
    _logger.Error(ex, "Failed to add relationship");
    // Cleanup on failure
    throw;
}
```

---

## Opportunities for Relationship Usage

### 1. Scene Ownership → Replace `SceneOwnershipComponent`

**Current Pattern:**
```csharp
// SceneOwnershipComponent stores Entity reference
public struct SceneOwnershipComponent
{
    public Entity SceneEntity { get; set; }
}
```

**Used In:**
- `MapPopupSceneSystem` - Links popup entities to scenes
- Shader entities - Per-scene shader association
- Other scene-scoped entities

**Problems:**
- Entity references can become stale if scene is destroyed
- Requires manual cleanup
- No automatic invalidation

**Proposed Solution:**
```csharp
// New relationship type
// Note: Namespace must match folder structure per .cursorrules Rule #9
// Options:
//   - MonoBall.Core.Scenes.Relationships (if in Scenes/Relationships/)
//   - MonoBall.Core.ECS.Relationships (if in ECS/Relationships/)
// Decision: Use MonoBall.Core.Scenes.Relationships to match UI pattern
//           (UI relationships are in UI/Relationships/, so scene relationships should be in Scenes/Relationships/)
namespace MonoBall.Core.Scenes.Relationships;

/// <summary>
/// Relationship type for scene → entity ownership.
/// Used to link scene-scoped entities (windows, shaders, etc.) to their parent scene entity.
/// When the scene entity is destroyed, Arch.Extended automatically removes all relationships.
/// </summary>
public struct OwnsSceneEntity
{
    // Marker relationship - no data needed
    // Can be extended with metadata if needed (e.g., ZOrder, ElementType)
}
```

**Migration:**
- Replace `SceneOwnershipComponent` with `OwnsSceneEntity` relationship immediately
- Update `MapPopupSceneSystem` to use relationships (break existing code, update all call sites)
- Update shader systems to use relationships instead of `RenderingShaderComponent.SceneEntity` (break existing code)
- Remove `SceneOwnershipComponent` immediately (no backward compatibility)

**Files to Update:**
- `MonoBall.Core/Scenes/Components/SceneOwnershipComponent.cs` (remove)
- `MonoBall.Core/Scenes/Systems/MapPopupSceneSystem.cs`
- `MonoBall.Core/ECS/Components/RenderingShaderComponent.cs` (remove `SceneEntity` field)
- `MonoBall.Core/ECS/Systems/ShaderManager.cs`
- Any other systems using `SceneOwnershipComponent`

---

### 2. Map Hierarchy → Replace Dictionary Tracking

**Current Pattern:**
```csharp
// MapLoaderSystem tracks child entities in dictionaries
private readonly Dictionary<string, List<Entity>> _mapChunkEntities = new();
private readonly Dictionary<string, List<Entity>> _mapConnectionEntities = new();
private readonly Dictionary<string, List<Entity>> _mapNpcEntities = new();
```

**Problems:**
- Manual dictionary management
- No automatic cleanup when map entity is destroyed
- Requires map ID lookup to find children
- Dictionary can become out of sync with actual entities

**Proposed Solution:**
```csharp
// New relationship types
// Note: Namespace must match folder structure per .cursorrules Rule #9
// Options:
//   - MonoBall.Core.ECS.Relationships (if in ECS/Relationships/)
//   - MonoBall.Core.Maps.Relationships (if in Maps/Relationships/)
// Decision: Use MonoBall.Core.ECS.Relationships since these are ECS-level relationships
//           and Maps/ folder contains definition classes, not ECS components
namespace MonoBall.Core.ECS.Relationships;

/// <summary>
/// Relationship type for map → tile chunk ownership.
/// Used to link tile chunk entities to their parent map entity.
/// When the map entity is destroyed, Arch.Extended automatically removes all relationships.
/// </summary>
public struct OwnsTileChunk
{
    // Marker relationship - no data needed
    // Can be extended with metadata if needed (e.g., chunk priority, layer index)
}

/// <summary>
/// Relationship type for map → connection ownership.
/// Used to link map connection entities to their parent map entity.
/// When the map entity is destroyed, Arch.Extended automatically removes all relationships.
/// </summary>
public struct OwnsMapConnection
{
    // Marker relationship - no data needed
    // Can be extended with metadata if needed (e.g., connection priority, direction)
}

/// <summary>
/// Relationship type for map → NPC ownership.
/// Used to link NPC entities to their parent map entity.
/// When the map entity is destroyed, Arch.Extended automatically removes all relationships.
/// </summary>
public struct OwnsNpc
{
    // Marker relationship - no data needed
    // Can be extended with metadata if needed (e.g., NPC spawn order, priority)
}
```

**Migration:**
- Replace dictionary tracking with relationships in `MapLoaderSystem`
- Update `UnloadMap()` to iterate relationships instead of dictionaries
- Remove dictionary fields from `MapLoaderSystem`
- Update any systems that query map children to use relationships

**Benefits:**
- Automatic cleanup when map is destroyed (Arch.Extended removes relationships when parent entity is destroyed)
- Efficient queries: `World.GetRelationships<OwnsTileChunk>(mapEntity)` returns `Dictionary<Entity, OwnsTileChunk>`
- No manual dictionary synchronization
- Type-safe relationship queries

**Files to Update:**
- `MonoBall.Core/ECS/Systems/MapLoaderSystem.cs`
- `MonoBall.Core/ECS/Services/ActiveMapFilterService.cs` (if it queries map children)
- Any other systems that need to find map children

---

### 3. Shader Scene Association → Replace `RenderingShaderComponent.SceneEntity`

**Current Pattern:**
```csharp
// RenderingShaderComponent stores Entity? reference
public struct RenderingShaderComponent
{
    public Entity? SceneEntity { get; set; } // null = global, set = per-scene
}
```

**Problems:**
- Entity reference can become stale
- Requires manual null checks
- No automatic cleanup

**Proposed Solution:**
- Use `OwnsSceneEntity` relationship (from #1)
- Global shaders: No relationship
- Per-scene shaders: Add `OwnsSceneEntity` relationship from scene to shader

**Migration:**
- Remove `SceneEntity` field from `RenderingShaderComponent` immediately (break existing code)
- Update `ShaderManager.UpdateActiveShaders()` to query relationships using `World.GetRelationships<OwnsSceneEntity>(sceneEntity)` which returns `Dictionary<Entity, OwnsSceneEntity>`
- Update shader creation code to add relationships using `World.AddRelationship(sceneEntity, shaderEntity, new OwnsSceneEntity())` for per-scene shaders
- Global shaders: No relationship (null check becomes "no relationship exists")
- Update all call sites immediately (no backward compatibility)

**Files to Update:**
- `MonoBall.Core/ECS/Components/RenderingShaderComponent.cs`
- `MonoBall.Core/ECS/Systems/ShaderManager.cs`
- `MonoBall.Core/Scenes/Systems/ShaderCycleSystem.cs`
- Any other systems creating shader entities

---

### 4. NPC Map Association → Replace `NpcComponent.MapId` String

**Current Pattern:**
```csharp
// NpcComponent stores map ID as string
public struct NpcComponent
{
    public string MapId { get; set; }
}
```

**Problems:**
- String-based lookup (requires map entity lookup)
- No direct entity relationship
- Map ID can become invalid if map is unloaded

**Proposed Solution:**
```csharp
// Use OwnsNpc relationship (from #2)
// Keep MapId in NpcComponent for definition/loading purposes
// Add relationship from map entity to NPC entity
```

**Migration:**
- Keep `MapId` in `NpcComponent` for definition/loading purposes (used by map definition parsing)
- Add `OwnsNpc` relationship when creating NPCs in `MapLoaderSystem` (break existing code, update all call sites)
- Update systems that need to find NPCs by map to use relationships (break existing code)
- Update `ActiveMapFilterService` to use relationships for NPC queries (break existing code)

**Benefits:**
- Direct entity relationship (no string lookup needed for runtime queries)
- Automatic cleanup when map is destroyed (Arch.Extended removes relationships when parent entity is destroyed)
- Efficient queries: `World.GetRelationships<OwnsNpc>(mapEntity)` returns `Dictionary<Entity, OwnsNpc>`

**Files to Update:**
- `MonoBall.Core/ECS/Systems/MapLoaderSystem.cs` (CreateNpcs method)
- `MonoBall.Core/ECS/Services/ActiveMapFilterService.cs`
- `MonoBall.Core/ECS/Services/EntityQueryService.cs`
- Any other systems querying NPCs by map

---

### 5. Tile Chunk Map Association → Replace `MapComponent` on Chunks

**Current Pattern:**
```csharp
// Tile chunks have MapComponent with MapId string
Entity chunkEntity = World.Create(
    new MapComponent { MapId = mapDefinition.Id },
    new TileChunkComponent { ... }
);
```

**Problems:**
- String-based association (requires lookup)
- Chunks have `MapComponent` but aren't map entities themselves
- No direct parent-child relationship

**Proposed Solution:**
- Use `OwnsTileChunk` relationship (from #2)
- Remove `MapComponent` from chunk entities (chunks aren't maps)
- Add relationship from map entity to chunk entity

**Migration:**
- Remove `MapComponent` from chunk creation in `MapLoaderSystem` immediately (break existing code)
- Add `OwnsTileChunk` relationship using `World.AddRelationship(mapEntity, chunkEntity, new OwnsTileChunk())` when creating chunks
- Update any systems that query chunks by map to use `World.GetRelationships<OwnsTileChunk>(mapEntity)` which returns `Dictionary<Entity, OwnsTileChunk>`
- Update all call sites immediately (no backward compatibility)

**Benefits:**
- Clearer semantics: chunks aren't maps, they belong to maps
- Direct relationship queries
- Automatic cleanup

**Files to Update:**
- `MonoBall.Core/ECS/Systems/MapLoaderSystem.cs` (CreateTileChunks method)
- Any systems querying chunks by map ID

---

### 6. Map Connection Association → Replace `MapComponent` on Connections

**Current Pattern:**
```csharp
// Connection entities have MapComponent with MapId string
var connectionEntity = World.Create(
    new MapComponent { MapId = mapDefinition.Id },
    new MapConnectionComponent { ... }
);
```

**Problems:**
- Same as tile chunks: connections aren't maps
- String-based association

**Proposed Solution:**
- Use `OwnsMapConnection` relationship (from #2)
- Remove `MapComponent` from connection entities
- Add relationship from map entity to connection entity

**Migration:**
- Remove `MapComponent` from connection creation immediately (break existing code)
- Add `OwnsMapConnection` relationship using `World.AddRelationship(mapEntity, connectionEntity, new OwnsMapConnection())`
- Update `ActiveMapFilterService` to use `World.GetRelationships<OwnsMapConnection>(mapEntity)` which returns `Dictionary<Entity, OwnsMapConnection>` for connection queries
- Update all call sites immediately (no backward compatibility)

**Files to Update:**
- `MonoBall.Core/ECS/Systems/MapLoaderSystem.cs` (CreateConnections method)
- `MonoBall.Core/ECS/Services/ActiveMapFilterService.cs`
- `MonoBall.Core/ECS/Systems/MapConnectionSystem.cs`

---

## Implementation Priority

### High Priority (Core Functionality)
1. **Map Hierarchy** (#2) - Most impactful, replaces dictionary tracking
2. **Scene Ownership** (#1) - Already partially used in UI, extend to other scene entities
3. **Shader Scene Association** (#3) - Clean up shader system

### Medium Priority (Data Integrity)
4. **NPC Map Association** (#4) - Improves NPC queries
5. **Tile Chunk Map Association** (#5) - Cleaner semantics
6. **Map Connection Association** (#6) - Consistent with chunks

---

## Migration Strategy

### Phase 1: Create Relationship Types
- Create relationship structs in appropriate namespaces
- Follow existing pattern from `OwnsUIElement` / `ContainsUIElement`
- Add XML documentation to all relationship types

### Phase 2: Update Creation Code (Break Existing Code)
- Update systems that create entities to add relationships using `World.AddRelationship(parent, child, new RelationshipType())`
- Remove old patterns immediately (no backward compatibility)
- Update all call sites in same phase
- Add entity validation and error handling

### Phase 3: Update Query Code (Break Existing Code)
- Update systems that query entities to use `World.GetRelationships<T>(entity)` which returns `Dictionary<Entity, T>`
- Replace dictionary lookups with relationship iteration
- Replace string-based queries with relationship queries
- Add entity validation before relationship operations
- Update all call sites in same phase

### Phase 4: Remove Old Patterns (Immediate)
- Remove `SceneOwnershipComponent` (done in Phase 2)
- Remove `SceneEntity` field from `RenderingShaderComponent` (done in Phase 2)
- Remove dictionary tracking from `MapLoaderSystem` (done in Phase 2)
- Remove `MapComponent` from chunks/connections (done in Phase 2, keep on map entities)

### Phase 5: Testing & Validation
- Verify automatic cleanup works: Destroy parent entity, verify relationships are automatically removed by Arch.Extended
- Verify child entities must be destroyed manually (relationships are removed, but entities are not)
- Verify queries work correctly with `Dictionary<Entity, T>` iteration pattern
- Performance testing: Compare relationship queries vs dictionary lookups
- Test edge cases: Destroying child before parent, invalid entities, etc.

---

## Example: Map Hierarchy Migration

### Before (Dictionary Tracking)
```csharp
// MapLoaderSystem.cs
private readonly Dictionary<string, List<Entity>> _mapChunkEntities = new();

// Create chunk
var chunkEntity = World.Create(/* components */);
_mapChunkEntities[mapId].Add(chunkEntity);

// Unload map
foreach (var chunkEntity in _mapChunkEntities[mapId])
{
    World.Destroy(chunkEntity);
}
_mapChunkEntities.Remove(mapId);
```

### After (Relationships)
```csharp
// MapLoaderSystem.cs
// No dictionary needed!

// Validate map entity before operations
if (!World.IsAlive(mapEntity))
    throw new InvalidOperationException($"Map entity {mapEntity.Id} is not alive.");

if (!World.Has<MapComponent>(mapEntity))
    throw new InvalidOperationException($"Map entity {mapEntity.Id} does not have MapComponent.");

// Create chunk
var chunkEntity = World.Create(
    new TileChunkComponent { /* ... */ },
    new PositionComponent { /* ... */ },
    new RenderableComponent { /* ... */ }
);

if (!World.IsAlive(chunkEntity))
    throw new InvalidOperationException("Failed to create chunk entity.");

// Validate chunk has required components
if (!World.Has<TileChunkComponent>(chunkEntity))
    throw new InvalidOperationException("Chunk entity missing required TileChunkComponent.");

// Add relationship using correct API
try
{
    World.AddRelationship(mapEntity, chunkEntity, new OwnsTileChunk());
}
catch (Exception ex)
{
    _logger.Error(ex, "Failed to add relationship from map {MapId} to chunk {ChunkId}", 
        mapEntity.Id, chunkEntity.Id);
    World.Destroy(chunkEntity); // Cleanup on failure
    throw;
}

// Unload map
// Note: Arch.Extended automatically removes relationships when parent entity is destroyed,
// but child entities must be destroyed manually
var relationships = World.GetRelationships<OwnsTileChunk>(mapEntity);
foreach (var kvp in relationships)
{
    var chunkEntity = kvp.Key;
    
    if (!World.IsAlive(chunkEntity))
        continue;
    
    // Destroy child entities before parent
    World.Destroy(chunkEntity);
}

// Destroy parent (relationships automatically cleaned up by Arch.Extended)
World.Destroy(mapEntity);
```

---

## Benefits Summary

1. **Automatic Cleanup**: Arch.Extended automatically removes relationships when parent entities are destroyed (child entities must be destroyed manually)
2. **Type Safety**: Relationship types provide compile-time safety and clear semantics
3. **Efficient Queries**: `World.GetRelationships<T>(entity)` returns `Dictionary<Entity, T>` for direct iteration without allocations
4. **No Stale References**: Relationships are automatically invalidated when parent is destroyed, unlike Entity references stored in components
5. **Cleaner Code**: No manual dictionary synchronization, relationships are managed by Arch.Extended
6. **Better Semantics**: Clear parent-child relationships in code, follows ECS best practices
7. **Fail-Fast Validation**: Can validate entities are alive before relationship operations, throw clear exceptions

## Relationship Cleanup Behavior

### How Arch.Extended Handles Relationship Cleanup

**When Parent Entity is Destroyed:**
- Arch.Extended **automatically removes all relationships** from the destroyed entity
- This means `World.GetRelationships<T>(destroyedEntity)` will return an empty dictionary
- **Child entities are NOT automatically destroyed** - they become orphaned if not destroyed manually

**Best Practice - Destroy Order:**
```csharp
// 1. Get all child entities via relationships
var relationships = World.GetRelationships<OwnsTileChunk>(mapEntity);

// 2. Destroy all child entities first
foreach (var kvp in relationships)
{
    var childEntity = kvp.Key;
    if (World.IsAlive(childEntity))
        World.Destroy(childEntity);
}

// 3. Destroy parent entity (relationships automatically cleaned up)
World.Destroy(mapEntity);
```

**Edge Cases:**
- **Child destroyed before parent**: Arch.Extended automatically removes the relationship
- **Invalid entity in relationship**: Always check `World.IsAlive(entity)` before using relationship entities
- **Missing components**: Validate required components exist before relationship operations

---

## Notes

### Relationship Cleanup Behavior
- **Arch.Extended automatically removes relationships** when the parent entity is destroyed
- **Child entities are NOT automatically destroyed** - they must be destroyed manually before destroying the parent
- **Destroy order**: Destroy all child entities first, then destroy parent entity
- **Invalid relationships**: If a child entity is destroyed before parent, the relationship is automatically removed by Arch.Extended

### String IDs vs Relationships
- **Keep String IDs**: Some components (like `NpcComponent.MapId`) should keep string IDs for definition/loading purposes (used by map definition parsing)
- **Add Relationships**: Use relationships for runtime queries and entity navigation
- **Dual Purpose**: String IDs for serialization/definitions, relationships for runtime ECS queries

### Performance Considerations
- **Relationship queries**: `World.GetRelationships<T>(entity)` returns `Dictionary<Entity, T>` - efficient iteration
- **No allocations**: Relationship iteration doesn't allocate (uses existing dictionary)
- **Comparison**: Relationship queries should be comparable or faster than dictionary lookups by map ID

### Testing Requirements
- Verify relationship cleanup: Destroy parent entity, verify relationships are automatically removed
- Verify child entity cleanup: Destroy child entities manually before destroying parent
- Test edge cases: Destroying child before parent, invalid entities, missing components
- Performance testing: Compare relationship queries vs dictionary lookups in hot paths
