# Definition vs Component Architecture Decision

## Question

Should `MapMusicSystem` and `MapPopupSystem`:

- **Option A**: Query ECS components (`MusicComponent`, `MapSectionComponent`) from map entities
- **Option B**: Resolve from definitions (`MapDefinition.MusicId`, `MapDefinition.MapSectionId`)

---

## Current State Analysis

### MapComponent Pattern

**What MapComponent Stores**:

```csharp
public struct MapComponent
{
    public string MapId { get; set; }        // Reference to definition
    public int Width { get; set; }           // Duplicated from definition
    public int Height { get; set; }          // Duplicated from definition
    public int TileWidth { get; set; }       // Duplicated from definition
    public int TileHeight { get; set; }      // Duplicated from definition
}
```

**Why MapComponent Exists**:

- Stores `MapId` to reference the definition
- Stores dimensions for **runtime access** (queries need dimensions without resolving definition)
- Used by systems that need to query map entities directly

**Usage Pattern**:

- Systems query `MapComponent` to find map entities
- Systems resolve `MapDefinition` when they need static config

---

## Option A: Use ECS Components

### Architecture

**Create Components**:

```csharp
// MusicComponent (already exists, unused)
public struct MusicComponent
{
    public string AudioId { get; set; }
    public bool FadeInOnTransition { get; set; }
    public float FadeDuration { get; set; }
}

// MapSectionComponent (would need to be created)
public struct MapSectionComponent
{
    public string MapSectionId { get; set; }
    public string PopupThemeId { get; set; }
}
```

**MapLoaderSystem** (already creates MusicComponent):

```csharp
// Create music component if music ID exists
if (!string.IsNullOrEmpty(mapDefinition.MusicId))
{
    var musicComponent = new MusicComponent
    {
        AudioId = mapDefinition.MusicId,
        FadeInOnTransition = true,
        FadeDuration = 0f,
    };
    World.Add(mapEntity, musicComponent);
}

// Would also create MapSectionComponent
if (!string.IsNullOrEmpty(mapDefinition.MapSectionId))
{
    var mapSectionComponent = new MapSectionComponent
    {
        MapSectionId = mapDefinition.MapSectionId,
        PopupThemeId = mapSectionDefinition.PopupTheme,
    };
    World.Add(mapEntity, mapSectionComponent);
}
```

**MapMusicSystem**:

```csharp
private void OnMapTransition(ref MapTransitionEvent evt)
{
    // Find map entity
    Entity? mapEntity = GetMapEntity(evt.TargetMapId);
    if (!mapEntity.HasValue || !World.Has<MusicComponent>(mapEntity.Value))
    {
        return;
    }
    
    ref var musicComponent = ref World.Get<MusicComponent>(mapEntity.Value);
    string musicId = musicComponent.AudioId;
    
    // Play music...
}
```

**MapPopupSystem**:

```csharp
private void ShowPopupForMap(string mapId)
{
    // Find map entity
    Entity? mapEntity = GetMapEntity(mapId);
    if (!mapEntity.HasValue || !World.Has<MapSectionComponent>(mapEntity.Value))
    {
        return;
    }
    
    ref var mapSectionComponent = ref World.Get<MapSectionComponent>(mapEntity.Value);
    string mapSectionId = mapSectionComponent.MapSectionId;
    
    // Resolve MapSectionDefinition for name/theme
    var mapSectionDefinition = _modManager.GetDefinition<MapSectionDefinition>(mapSectionId);
    // Create popup...
}
```

### Pros

✅ **ECS-Native**: Data is in the world, queryable
✅ **Queryable**: Can query "all maps with music X" or "all maps in section Y"
✅ **Runtime Modifiable**: Could change music/section at runtime (unlikely but possible)
✅ **Consistent**: Matches `MapComponent` pattern (stores data on entity)
✅ **Performance**: Direct component access (no definition lookup)
✅ **Separation**: Clear separation between definition (static) and component (runtime)

### Cons

❌ **Data Duplication**: MusicId/MapSectionId stored in both definition and component
❌ **More Complex**: Need to find map entity before accessing data
❌ **More Components**: Need `MapSectionComponent` (doesn't exist yet)
❌ **Maintenance**: Must keep component in sync with definition
❌ **Memory**: Extra components consume memory (minimal but still overhead)

---

## Option B: Resolve from Definitions (Current)

### Architecture

**MapLoaderSystem**:

```csharp
// Don't create MusicComponent or MapSectionComponent
// Just create MapComponent with MapId reference
var mapEntity = World.Create(
    new MapComponent { MapId = mapId, ... }
);
```

**MapMusicSystem**:

```csharp
private void OnMapTransition(ref MapTransitionEvent evt)
{
    // Resolve directly from definition
    var mapDefinition = _modManager.GetDefinition<MapDefinition>(evt.TargetMapId);
    if (mapDefinition == null || string.IsNullOrEmpty(mapDefinition.MusicId))
    {
        return;
    }
    
    string musicId = mapDefinition.MusicId;
    // Play music...
}
```

**MapPopupSystem**:

```csharp
private void ShowPopupForMap(string mapId)
{
    // Resolve directly from definition
    var mapDefinition = _modManager.GetDefinition<MapDefinition>(mapId);
    if (mapDefinition == null || string.IsNullOrEmpty(mapDefinition.MapSectionId))
    {
        return;
    }
    
    string mapSectionId = mapDefinition.MapSectionId;
    var mapSectionDefinition = _modManager.GetDefinition<MapSectionDefinition>(mapSectionId);
    // Create popup...
}
```

### Pros

✅ **Simple**: Direct definition lookup, no entity finding needed
✅ **No Duplication**: Data only stored in definition
✅ **Consistent**: Matches current `MapPopupSystem` pattern
✅ **Less Memory**: No extra components
✅ **Single Source of Truth**: Definition is authoritative

### Cons

❌ **Not Queryable**: Can't query "all maps with music X" (would need to iterate definitions)
❌ **Not ECS-Native**: Data not in world, can't query entities
❌ **Inconsistent**: `MapComponent` stores dimensions, but music/section don't get components
❌ **Definition Lookup**: O(1) dictionary lookup, but still an extra step

---

## Architectural Principles

### When to Use Components

**Use Components When**:

1. **Runtime State**: Data changes during gameplay
2. **Queryable**: Need to query entities by this data
3. **Entity-Specific**: Data is attached to specific entities
4. **Performance Critical**: Need direct component access in hot paths

**Examples**:

- `PositionComponent` - runtime state, queryable
- `SpriteComponent` - runtime state, queryable
- `SceneComponent` - runtime state (IsActive, IsPaused), queryable
- `MapComponent` - runtime state (dimensions might change), queryable

### When to Use Definitions

**Use Definitions When**:

1. **Static Config**: Data doesn't change at runtime
2. **Moddable**: Loaded from JSON, moddable
3. **Reference Data**: Used to resolve other definitions
4. **Not Queryable**: Don't need to query entities by this data

**Examples**:

- `MapDefinition.MusicId` - static config
- `MapDefinition.MapSectionId` - static config
- `MapSectionDefinition` - static config
- `PopupThemeDefinition` - static config

---

## Recommendation: **Option B (Definitions)**

### Reasoning

1. **MusicId and MapSectionId are Static Config**
    - They don't change at runtime
    - They're configuration, not runtime state
    - No need to query entities by music/section

2. **Consistent with Current Pattern**
    - `MapPopupSystem` already uses definitions
    - Both systems should be consistent
    - Simpler architecture

3. **MapComponent Stores Runtime Data**
    - `MapComponent` stores dimensions for runtime access
    - Dimensions might be modified (unlikely but possible)
    - Dimensions are queried by rendering systems

4. **No Query Need**
    - Systems don't need to query "all maps with music X"
    - Systems resolve from map ID (from event)
    - Direct definition lookup is simpler

5. **Single Source of Truth**
    - Definition is authoritative
    - No sync issues between component and definition
    - Less maintenance

### Exception: If Querying Becomes Important

If we later need to query "all maps with music X", we can:

1. Add `MusicComponent` at that time
2. Or create a service that caches music-to-map mappings

**Don't optimize prematurely** - add components when there's a clear need.

---

## Decision Matrix

| Criteria             | Option A (Components)               | Option B (Definitions) |
|----------------------|-------------------------------------|------------------------|
| **Simplicity**       | ❌ More complex                      | ✅ Simpler              |
| **Consistency**      | ⚠️ Inconsistent with MapPopupSystem | ✅ Consistent           |
| **Queryability**     | ✅ Queryable                         | ❌ Not queryable        |
| **Data Duplication** | ❌ Duplicated                        | ✅ Single source        |
| **ECS-Native**       | ✅ ECS-native                        | ⚠️ Less ECS-native     |
| **Performance**      | ✅ Direct access                     | ✅ O(1) lookup          |
| **Memory**           | ❌ Extra components                  | ✅ No overhead          |
| **Maintenance**      | ❌ Sync required                     | ✅ Single source        |

---

## Final Recommendation

**Use Option B (Definitions)** for the following reasons:

1. **MusicId and MapSectionId are static configuration**, not runtime state
2. **No query need** - systems resolve from map ID, don't query by music/section
3. **Consistency** - both systems should use the same pattern
4. **Simplicity** - fewer components, less complexity
5. **Single source of truth** - definition is authoritative

**Remove `MusicComponent` creation** from `MapLoaderSystem` (it's dead code).

**Keep current definition resolution pattern** in both `MapMusicSystem` and `MapPopupSystem`.

---

## If We Need Components Later

If we later need to query maps by music/section, we can:

1. Add components at that time (YAGNI - don't add until needed)
2. Or create a service that maintains music-to-map mappings
3. Or add components but keep definition as source of truth (component mirrors definition)

**Don't add components now** - add them when there's a clear architectural need.

