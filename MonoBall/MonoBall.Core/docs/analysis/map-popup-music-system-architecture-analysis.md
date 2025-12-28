# MapPopupSystem & MapMusicSystem Architecture Analysis

## Executive Summary

After refactoring to remove `MapPopupOrchestratorSystem` and align both systems, several architectural inconsistencies and violations were identified. This document analyzes all issues and proposes a clear architecture pattern.

---

## 🔴 Critical Architecture Issues

### 1. **Inconsistent Data Source Pattern**

**Problem**: Both systems resolve from definitions, but `MapLoaderSystem` still creates `MusicComponent` on map entities (line 175), which is now unused.

**Current State**:
- `MapMusicSystem` resolves `MapDefinition.MusicId` from definitions ✅
- `MapPopupSystem` resolves `MapDefinition.MapSectionId` from definitions ✅
- `MapLoaderSystem` creates `MusicComponent` on map entities ❌ (dead component)

**Impact**: 
- Dead component creation wastes memory
- Unclear when to use components vs definitions
- Inconsistent pattern across codebase

**Decision Needed**: 
- **Option A**: Remove `MusicComponent` creation from `MapLoaderSystem` (simpler, consistent)
- **Option B**: Keep `MusicComponent` and have `MapMusicSystem` query ECS (more ECS-native)

**Analysis**: See `definition-vs-component-architecture-decision.md` for full analysis.

**Recommendation**: **Option A** - Remove `MusicComponent` creation. Both systems should resolve from definitions for consistency.

**Reasoning**:
- `MusicId` and `MapSectionId` are **static configuration**, not runtime state
- No need to query entities by music/section (systems resolve from map ID)
- Consistent with current `MapPopupSystem` pattern
- Single source of truth (definition is authoritative)
- Simpler architecture (fewer components, less complexity)

---

### 2. **DRY Violation: Duplicate `IsLoadingSceneActive()`**

**Problem**: Both `MapPopupSystem` and `MapMusicSystem` have identical `IsLoadingSceneActive()` methods.

**Current Code** (duplicated in both systems):
```csharp
private bool IsLoadingSceneActive()
{
    bool hasActiveLoadingScene = false;
    World.Query(
        in _loadingSceneQuery,
        (Entity entity, ref SceneComponent scene) =>
        {
            if (scene.IsActive && !scene.IsPaused)
            {
                hasActiveLoadingScene = true;
            }
        }
    );
    return hasActiveLoadingScene;
}
```

**Violation**: DRY principle - same logic in two places

**Solutions**:
1. **Extract to `SceneSystem`** - Add `IsLoadingSceneActive()` method to `SceneSystem` (ISceneManager)
2. **Extract to utility class** - Create `SceneQueryHelper` static class
3. **Use SceneSystem directly** - Both systems already have access to scene management

**Recommendation**: **Option 1** - Add to `SceneSystem` since it already manages scene state and both systems have access to `ISceneManager`.

---

### 3. **SOLID Violations**

#### Single Responsibility Principle (SRP)

**Current**: Both systems handle:
- Event subscription/disposal ✅
- Loading scene checks ✅
- Definition resolution ✅
- Duplicate prevention ✅
- Action execution (music playback / popup creation) ✅

**Analysis**: Each system has a single responsibility (manage music/popups), but they share common concerns:
- Loading scene checks
- Event handling pattern
- Definition resolution pattern

**Recommendation**: Extract shared concerns to base class or helper methods.

#### Open/Closed Principle (OCP)

**Current**: Both systems are closed for modification but open for extension via events ✅

**Status**: ✅ Compliant

---

### 4. **Arch ECS Best Practices**

#### ✅ Query Caching
- Both systems cache `QueryDescription` in constructor ✅
- `_loadingSceneQuery` cached correctly ✅

#### ✅ Event Subscriptions
- Both systems implement `IDisposable` ✅
- Both unsubscribe in `Dispose()` ✅
- Use `new` keyword correctly ✅

#### ⚠️ Query Efficiency Issue

**Problem**: `IsLoadingSceneActive()` query doesn't short-circuit - iterates all loading scenes even after finding one.

**Current**:
```csharp
bool hasActiveLoadingScene = false;
World.Query(in _loadingSceneQuery, (Entity entity, ref SceneComponent scene) =>
{
    if (scene.IsActive && !scene.IsPaused)
    {
        hasActiveLoadingScene = true; // Doesn't stop iteration
    }
});
```

**Issue**: Arch ECS queries don't support early return from lambda. This is inefficient if there are many loading scenes.

**Solution**: Use `SceneSystem` which already tracks active scenes, or accept the inefficiency (loading scenes are rare).

**Recommendation**: Use `SceneSystem.IsLoadingSceneActive()` if we add it, or keep current implementation (loading scenes are rare).

---

### 5. **.cursorrules Compliance**

#### ✅ Component Design
- Components are value types (`struct`) ✅
- Components are pure data (no methods) ✅

#### ✅ System Design
- Systems inherit from `BaseSystem<World, float>` ✅
- QueryDescription cached in constructor ✅
- Event subscriptions disposed properly ✅

#### ⚠️ Missing XML Documentation

**Issue**: `MapMusicSystem.OnMapTransition()` and `OnGameEntered()` are private methods without XML docs.

**Current**:
```csharp
private void OnMapTransition(ref MapTransitionEvent evt) // No XML doc
```

**Requirement**: All public APIs should have XML docs. Private methods should have XML docs if they're non-trivial.

**Recommendation**: Add XML docs to event handlers (they're non-trivial).

#### ⚠️ Variable Name Inconsistency

**Issue**: `MapMusicSystem.OnMapTransition()` references `targetMapId` on line 106, but variable is `evt.TargetMapId`.

**Current** (line 106):
```csharp
_logger.Debug(
    "Map {TargetMapId} already playing music {MusicId}, skipping music change",
    targetMapId, // ❌ Variable doesn't exist - should be evt.TargetMapId
    musicId
);
```

**Status**: ❌ Compilation error (variable name issue)

---

## 📋 Definition vs ECS Query Pattern

### Current Inconsistency

**MapMusicSystem**: Resolves from definitions (`IModManager.GetDefinition<MapDefinition>()`)
**MapPopupSystem**: Resolves from definitions (`IModManager.GetDefinition<MapDefinition>()`)
**MapLoaderSystem**: Creates `MusicComponent` on map entities (unused)

### Proposed Pattern

**Rule**: **Use Definitions for Static Data, ECS for Runtime State**

#### ✅ Use Definitions When:
- Data is **static** (doesn't change at runtime)
- Data is **configuration** (map properties, themes, sections)
- Data is **moddable** (loaded from JSON)
- Examples: `MapDefinition`, `MapSectionDefinition`, `PopupThemeDefinition`, `MusicId`

#### ✅ Use ECS Components When:
- Data is **runtime state** (changes during gameplay)
- Data needs to be **queried** (find all entities with X)
- Data is **entity-specific** (attached to specific entities)
- Examples: `PositionComponent`, `SpriteComponent`, `MapComponent` (runtime map state)

#### ⚠️ Edge Cases:

**MusicId**: 
- **Current**: Static in `MapDefinition`, but `MapLoaderSystem` creates `MusicComponent`
- **Decision**: Should be definition-only (no component needed)
- **Reason**: Music ID doesn't change at runtime, doesn't need to be queried

**MapSectionId**:
- **Current**: Static in `MapDefinition`, resolved via definitions
- **Decision**: ✅ Correct - definition-only
- **Reason**: Map section is configuration, not runtime state

**Loading Scene Check**:
- **Current**: Queries ECS for `LoadingSceneComponent`
- **Decision**: ✅ Correct - queries ECS
- **Reason**: Scene state is runtime, changes during gameplay

---

## 🔧 Recommended Fixes

### Priority 1: Critical Issues ✅ FIXED

1. ✅ **Fix compilation error** in `MapMusicSystem.OnMapTransition()` (line 106) - **FIXED**
   - Changed `targetMapId` → `evt.TargetMapId`
   - Changed `initialMapId` → `evt.InitialMapId`
   - Added XML documentation to event handlers

2. ✅ **Use `MusicComponent` and `MapSectionComponent`** (runtime modification support)
   - **Status**: Both components now created and queried ✅
   - **Decision**: Keep components for runtime modification (see `definition-vs-component-architecture-decision.md`)
   - **Reasoning**: Components allow runtime modification of music/popups via scripts/events
   - **Implementation**: 
     - `MapLoaderSystem` creates both `MusicComponent` and `MapSectionComponent`
     - `MapMusicSystem` queries `MusicComponent` from map entities
     - `MapPopupSystem` queries `MapSectionComponent` from map entities

3. ✅ **Extract `IsLoadingSceneActive()`** to `SceneSystem` - **FIXED**
   - **Status**: Extracted to `ISceneManager` interface and `SceneSystem` implementation ✅
   - **Implementation**: 
     - Added `IsLoadingSceneActive()` to `ISceneManager` interface
     - Implemented in `SceneSystem` using `_sceneStack` for efficient lookup
     - Removed duplicate methods from both systems
     - Both systems now use `_sceneManager.IsLoadingSceneActive()`

### Priority 2: Architecture Improvements ✅ COMPLETE

4. ✅ **Add XML documentation** to event handlers - **FIXED**
   - Added XML docs to `OnMapTransition()` and `OnGameEntered()` in both systems

5. ⚠️ **Create base class** for map transition systems (if more systems need this pattern)
   - **Status**: Not implemented (YAGNI - only 2 systems currently)
   - **Recommendation**: Add if more systems need this pattern

6. ✅ **Document the Definition vs ECS pattern** in architecture docs - **FIXED**
   - Created `definition-vs-component-architecture-decision.md` with full analysis

### Priority 3: Code Quality

7. **Standardize variable naming** (use `mapId` consistently)
8. **Add unit tests** for duplicate prevention logic
9. **Consider extracting shared logic** to utility class

---

## 📐 Proposed Architecture Pattern

### When to Query Definitions

```csharp
// ✅ CORRECT: Static configuration data
var mapDefinition = _modManager.GetDefinition<MapDefinition>(mapId);
var musicId = mapDefinition.MusicId; // Static config
var mapSectionId = mapDefinition.MapSectionId; // Static config
```

### When to Query ECS

```csharp
// ✅ CORRECT: Runtime state
World.Query(in _loadingSceneQuery, (Entity e, ref SceneComponent scene) => {
    if (scene.IsActive && !scene.IsPaused) { /* ... */ }
});

// ✅ CORRECT: Entity-specific runtime data
World.Query(in _mapQuery, (ref MapComponent map) => {
    // MapComponent contains runtime state (position, loaded chunks, etc.)
});
```

### Hybrid Pattern (When Needed)

```csharp
// ✅ CORRECT: Resolve definition, then query ECS for runtime state
var mapDefinition = _modManager.GetDefinition<MapDefinition>(mapId);
var mapEntity = GetMapEntity(mapId); // Query ECS for runtime entity
if (World.Has<MusicComponent>(mapEntity)) {
    // Runtime component (if we decide to keep it)
}
```

---

## 🎯 Consistency Checklist

- [x] Both systems subscribe to same events (`MapTransitionEvent`, `GameEnteredEvent`)
- [x] Both systems check loading scene before processing
- [x] Both systems use try-catch for error handling
- [x] Both systems prevent duplicates
- [x] Both systems query components for runtime data (`MusicComponent`, `MapSectionComponent`)
- [x] Both systems use shared `IsLoadingSceneActive()` method via `ISceneManager` ✅
- [x] Both systems have XML docs on event handlers ✅
- [x] Components created and used correctly (no dead code) ✅

---

## 📝 Summary

**Current State**: All critical issues fixed ✅
- ✅ DRY violation fixed (`IsLoadingSceneActive()` extracted to `SceneSystem`)
- ✅ Components properly used (`MusicComponent` and `MapSectionComponent` queried)
- ✅ Compilation errors fixed (variable names corrected)
- ✅ XML documentation added to event handlers
- ✅ Consistent component query pattern (both systems query from map entities)
- ✅ Proper event handling and disposal
- ✅ Query caching compliance
- ✅ Centralized scene state checks via `ISceneManager`

**Architecture**: 
- Components for runtime-modifiable data (music, popup themes)
- Definitions for static configuration (display names, theme definitions)
- Centralized scene state management via `ISceneManager`

**Status**: ✅ All Priority 1 and Priority 2 issues resolved. Systems are architecturally consistent and follow SOLID/DRY principles.

