# Audio System Design Analysis

**Date:** 2025-01-XX  
**Status:** Critical Issues Identified  
**Purpose:** Identify architecture issues, missing features, and ECS/Event problems in the audio system design

---

## Executive Summary

This analysis identifies critical architecture issues in the audio system design, particularly around music zone handling, entity lifecycle management, and missing patterns from oldmonoball. The MusicZone design is indeed half-thought-out and conflates map-level music with zone-level music.

---

## Critical Issues

### 1. MusicZone Design is Fundamentally Flawed

**Problem:** The design conflates two distinct concepts:
- **Map-level music**: Music that plays for an entire map (should be on `MapComponent`)
- **Zone-level music**: Music for sub-areas within a map (should be `MusicZoneComponent`)

**Current Design Issues:**
- `MusicZoneComponent` has `Rectangle Bounds` but no clear way to create/load these zones
- `MusicZoneSystem` queries player position every frame (inefficient)
- No `MusicComponent` for map-level music
- Missing `MapMusicOrchestrator` service pattern from oldmonoball

**What oldmonoball Actually Does:**
```csharp
// Map-level music: Simple component on map entity
public struct Music
{
    public GameAudioId AudioId { get; set; }
}

// MapMusicOrchestrator: Service that subscribes to map events
public class MapMusicOrchestrator : IMapMusicOrchestrator
{
    // Subscribes to MapTransitionEvent and MapRenderReadyEvent
    // Queries map entities for Music component
    // Plays music based on map transitions, not per-frame checks
}
```

**Required Fix:**
1. Add `MusicComponent` for map-level music (simple, just AudioId)
2. Create `MapMusicOrchestrator` service that subscribes to `MapLoadedEvent` and `MapTransitionEvent`
3. Keep `MusicZoneComponent` for sub-areas but make it event-driven, not per-frame
4. Load music zones from map definitions (Tiled properties or JSON)

---

### 2. Missing Map-Level Music Component

**Problem:** Maps should have a `MusicComponent` attached when loaded, but the design doesn't include this.

**Current Design:** Only has `MusicZoneComponent` with Rectangle bounds, which is for sub-areas.

**Required Component:**
```csharp
namespace MonoBall.Core.ECS.Components
{
    /// <summary>
    /// Component for map-level background music.
    /// Attached to map entities when maps are loaded.
    /// </summary>
    public struct MusicComponent
    {
        /// <summary>
        /// The audio definition ID for this map's music.
        /// </summary>
        public string MusicId { get; set; }

        /// <summary>
        /// Whether the music should loop (defaults to definition setting).
        /// </summary>
        public bool? Loop { get; set; }

        /// <summary>
        /// Fade duration when transitioning to this music (seconds).
        /// </summary>
        public float FadeDuration { get; set; }
    }
}
```

**Integration Point:** `MapLoaderSystem` should add `MusicComponent` when loading maps (from map definition).

---

### 3. Missing MapMusicOrchestrator Service

**Problem:** The design has `MusicZoneSystem` but no service for handling map-level music transitions.

**What's Missing:**
- Service that subscribes to `MapLoadedEvent` and `MapTransitionEvent`
- Queries map entities for `MusicComponent`
- Handles initial map load vs warp transitions differently
- Filters out adjacent maps during seamless streaming

**Required Service:**
```csharp
namespace MonoBall.Core.Audio.Services
{
    /// <summary>
    /// Service that manages map background music based on map transitions.
    /// Subscribes to MapLoadedEvent and MapTransitionEvent.
    /// </summary>
    public class MapMusicOrchestrator : IDisposable
    {
        private readonly World _world;
        private readonly IAudioService _audioService;
        private readonly IActiveMapFilterService _activeMapFilter;
        private string? _currentMapMusicId;
        private bool _disposed;

        public MapMusicOrchestrator(
            World world,
            IAudioService audioService,
            IActiveMapFilterService activeMapFilter)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _audioService = audioService ?? throw new ArgumentNullException(nameof(audioService));
            _activeMapFilter = activeMapFilter ?? throw new ArgumentNullException(nameof(activeMapFilter));

            // Subscribe to map events
            EventBus.Subscribe<MapLoadedEvent>(OnMapLoaded);
            EventBus.Subscribe<MapTransitionEvent>(OnMapTransition);
        }

        private void OnMapLoaded(ref MapLoadedEvent evt)
        {
            // Only play music for current/primary map (not adjacent maps)
            if (!_activeMapFilter.IsActiveMap(evt.MapId))
            {
                return;
            }

            PlayMusicForMap(evt.MapId, evt.MapEntity, isWarp: false);
        }

        private void OnMapTransition(ref MapTransitionEvent evt)
        {
            // Skip initial load (handled by MapLoadedEvent)
            if (evt.IsInitialLoad)
            {
                return;
            }

            // Find target map entity and play music
            // Query for MapComponent with matching MapId
            // Then check for MusicComponent
        }

        private void PlayMusicForMap(string mapId, Entity mapEntity, bool isWarp)
        {
            if (!_world.Has<MusicComponent>(mapEntity))
            {
                return;
            }

            var music = _world.Get<MusicComponent>(mapEntity);
            
            // Don't restart if already playing same track
            if (_currentMapMusicId == music.MusicId && _audioService.IsMusicPlaying)
            {
                return;
            }

            float fadeDuration = isWarp ? music.FadeDuration : 0f;
            _audioService.PlayMusic(music.MusicId, music.Loop, fadeDuration);
            _currentMapMusicId = music.MusicId;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                EventBus.Unsubscribe<MapLoadedEvent>(OnMapLoaded);
                EventBus.Unsubscribe<MapTransitionEvent>(OnMapTransition);
                _disposed = true;
            }
        }
    }
}
```

---

### 4. Entity Lifecycle Management Issues

**Problem:** `AudioSystem` stores `Dictionary<Entity, ILoopingSoundHandle>` but doesn't handle entity destruction.

**Current Code Issue:**
```csharp
// AudioSystem stores handles but never checks if entities are destroyed
private readonly Dictionary<Entity, ILoopingSoundHandle> _activeSounds;

// Problem: If entity is destroyed, handle leaks
```

**Required Fix:**
- Check `World.IsAlive(entity)` before accessing handles
- Clean up handles when entities are destroyed
- Subscribe to entity destruction events OR check entity validity in Update

**Fixed Code:**
```csharp
public override void Update(in float deltaTime)
{
    // ... existing code ...

    // Clean up handles for destroyed entities
    var destroyedEntities = _activeSounds.Keys
        .Where(entity => !World.IsAlive(entity))
        .ToList();

    foreach (var entity in destroyedEntities)
    {
        if (_activeSounds.TryGetValue(entity, out var handle))
        {
            handle?.Dispose();
            _activeSounds.Remove(entity);
        }
    }

    // ... rest of update ...
}
```

---

### 5. Position Synchronization Issues

**Problem:** `AudioSourceComponent` has its own `Position` field but doesn't sync with entity's `PositionComponent`.

**Current Design:**
```csharp
public struct AudioSourceComponent
{
    public Vector2 Position { get; set; } // Separate from PositionComponent
    // ...
}
```

**Issues:**
- Audio sources must manually update position
- No automatic sync with entity movement
- Duplicate position data

**Required Fix:**
- `AudioSystem` should query entities with BOTH `AudioSourceComponent` AND `PositionComponent`
- Use `PositionComponent.Position` for calculations, not `AudioSourceComponent.Position`
- Remove `Position` from `AudioSourceComponent` (or make it optional override)

**Fixed Component:**
```csharp
public struct AudioSourceComponent
{
    public string AudioId { get; set; }
    // Remove Position - use PositionComponent instead
    public float MaxDistance { get; set; }
    // ... rest of fields ...
}
```

**Fixed System:**
```csharp
// Query for entities with BOTH components
_sourceQuery = new QueryDescription()
    .WithAll<AudioSourceComponent, PositionComponent>();

World.Query(in _sourceQuery, (Entity entity, ref AudioSourceComponent source, ref PositionComponent pos) =>
{
    // Use pos.Position, not source.Position
    float distance = Vector2.Distance(pos.Position, listenerPosition);
    // ...
});
```

---

### 6. Inefficient Per-Frame Queries

**Problem:** `MusicZoneSystem` queries player position every frame instead of using events.

**Current Design:**
```csharp
public override void Update(in float deltaTime)
{
    // Queries player position EVERY FRAME
    World.Query(in _playerQuery, (ref PositionComponent pos) => { ... });
    
    // Queries all zones EVERY FRAME
    World.Query(in _zoneQuery, (ref MusicZoneComponent zone) => { ... });
}
```

**Issues:**
- Unnecessary work when player isn't moving
- Should only check when player position changes significantly
- Should use events or spatial queries

**Required Fix:**
- Subscribe to `MovementCompletedEvent` or `PositionChangedEvent`
- Only check zones when player moves
- Use spatial hash for zone lookups (if available)

**Alternative:** Keep per-frame but optimize:
- Cache player position, only query zones when position changes
- Use spatial partitioning for zone lookups
- Early exit if no zones exist

---

### 7. Missing AudioListenerComponent Synchronization

**Problem:** `AudioListenerComponent` has its own `Position` but doesn't sync with player's `PositionComponent`.

**Current Design:**
```csharp
public struct AudioListenerComponent
{
    public Vector2 Position { get; set; } // Separate from PositionComponent
}
```

**Required Fix:**
- `AudioSystem` should query for entities with BOTH `AudioListenerComponent` AND `PositionComponent`
- Use `PositionComponent.Position` for calculations
- Remove `Position` from `AudioListenerComponent` (or make it optional override)

**Fixed Component:**
```csharp
public struct AudioListenerComponent
{
    // Empty struct - just a marker component
    // Position comes from PositionComponent
}
```

**Fixed System:**
```csharp
_listenerQuery = new QueryDescription()
    .WithAll<AudioListenerComponent, PositionComponent>();

World.Query(in _listenerQuery, (ref AudioListenerComponent listener, ref PositionComponent pos) =>
{
    listenerPosition = pos.Position;
    hasListener = true;
});
```

---

### 8. Missing ContinueMusicAcrossMaps Feature

**Problem:** oldmonoball has `ContinueMusicAcrossMaps` flag for seamless music transitions, but design doesn't include this.

**Use Case:** When transitioning between maps with the same music, don't restart the track.

**Required Addition:**
```csharp
public struct MusicComponent
{
    public string MusicId { get; set; }
    public bool? Loop { get; set; }
    public float FadeDuration { get; set; }
    
    /// <summary>
    /// If true, music continues playing when transitioning to another map with the same music.
    /// </summary>
    public bool ContinueMusicAcrossMaps { get; set; }
}
```

**Implementation in MapMusicOrchestrator:**
```csharp
private void PlayMusicForMap(string mapId, Entity mapEntity, bool isWarp)
{
    if (!_world.Has<MusicComponent>(mapEntity))
    {
        return;
    }

    var music = _world.Get<MusicComponent>(mapEntity);
    
    // Check if music should continue
    if (music.ContinueMusicAcrossMaps && 
        _currentMapMusicId == music.MusicId && 
        _audioService.IsMusicPlaying)
    {
        return; // Keep current music playing
    }

    // ... rest of playback logic ...
}
```

---

### 9. Missing Music Zone Definition Loading

**Problem:** `MusicZoneComponent` has `Rectangle Bounds` but no clear way to create these zones from map definitions.

**Required Solution:**
- Load music zones from map definitions (Tiled properties or JSON)
- Create zone entities when maps are loaded
- Store zone bounds in map definition format

**Map Definition Addition:**
```json
{
  "id": "base:map:littleroot_town",
  "music": "base:audio:music/towns/mus_littleroot",
  "musicZones": [
    {
      "musicId": "base:audio:music/special/mus_pokemon_center",
      "bounds": { "x": 10, "y": 10, "width": 5, "height": 5 },
      "priority": 10
    }
  ]
}
```

**MapLoaderSystem Integration:**
```csharp
// After creating map entity
if (mapDefinition.MusicZones != null)
{
    foreach (var zoneDef in mapDefinition.MusicZones)
    {
        var zoneEntity = World.Create(
            new MusicZoneComponent(
                zoneDef.MusicId,
                new Rectangle(
                    zoneDef.Bounds.X * mapDefinition.TileWidth,
                    zoneDef.Bounds.Y * mapDefinition.TileHeight,
                    zoneDef.Bounds.Width * mapDefinition.TileWidth,
                    zoneDef.Bounds.Height * mapDefinition.TileHeight
                ),
                zoneDef.Priority
            ),
            new PositionComponent { Position = mapPosition }
        );
    }
}
```

---

### 10. AudioSystem Handle Management Issues

**Problem:** `AudioSystem` stores `ILoopingSoundHandle` in dictionary but doesn't handle volume updates for active sounds.

**Current Design:**
- Creates handle once when sound starts
- Never updates volume/pan for active sounds
- Doesn't handle dynamic volume changes

**Required Fix:**
- Store handle AND last calculated volume/pan
- Update handle volume/pan when values change
- Handle pitch changes for dynamic effects

**Improved Design:**
```csharp
private struct ActiveSound
{
    public ILoopingSoundHandle Handle;
    public float LastVolume;
    public float LastPan;
    public Vector2 LastPosition;
}

private readonly Dictionary<Entity, ActiveSound> _activeSounds;

// In Update:
if (_activeSounds.TryGetValue(entity, out var activeSound))
{
    // Check if volume/pan changed
    if (Math.Abs(activeSound.LastVolume - volume) > 0.01f ||
        Math.Abs(activeSound.LastPan - pan) > 0.01f)
    {
        // Update handle volume/pan (if IAudioService supports it)
        // Or recreate handle with new values
        activeSound.LastVolume = volume;
        activeSound.LastPan = pan;
        _activeSounds[entity] = activeSound;
    }
}
```

**Note:** This requires `ILoopingSoundHandle` to support volume/pan updates, or we need to recreate handles.

---

### 11. Missing AudioVolumeChangedEvent

**Problem:** Design mentions `AudioVolumeChangedEvent` in architecture diagram but doesn't define it.

**Required Event:**
```csharp
namespace MonoBall.Core.ECS.Events
{
    /// <summary>
    /// Event fired when audio volume settings change.
    /// </summary>
    public struct AudioVolumeChangedEvent
    {
        /// <summary>
        /// The audio category that changed.
        /// </summary>
        public AudioCategory Category { get; set; }

        /// <summary>
        /// The new volume (0.0 to 1.0).
        /// </summary>
        public float NewVolume { get; set; }
    }
}
```

**Usage:** Systems can subscribe to update active sounds when volume changes.

---

### 12. Missing PauseMusicEvent and ResumeMusicEvent

**Problem:** Design has `StopMusicEvent` but no pause/resume events.

**Required Events:**
```csharp
namespace MonoBall.Core.ECS.Events
{
    /// <summary>
    /// Event to request pausing background music.
    /// </summary>
    public struct PauseMusicEvent
    {
    }

    /// <summary>
    /// Event to request resuming paused music.
    /// </summary>
    public struct ResumeMusicEvent
    {
    }
}
```

**Integration:** `AudioEventSystem` should subscribe to these events.

---

### 13. Missing Audio Definition Loading Integration

**Problem:** Design mentions `IAudioDefinitionRegistry` but doesn't show how definitions are loaded from JSON files.

**Required Integration:**
- Audio definitions should be loaded during mod initialization
- Registry should be populated from `Definitions/Audio/**/*.json` files
- Registry should integrate with existing definition loading system

**Missing Pattern:**
```csharp
// In GameInitializationService or ModManager
var audioDefinitions = LoadDefinitions<AudioDefinition>("Definitions/Audio");
foreach (var def in audioDefinitions)
{
    _audioDefinitionRegistry.Register(def);
}
```

---

## Missing Features from oldmonoball

### 1. DuckingController
- **Missing:** Music ducking when Pokemon cries play
- **Required:** Service that temporarily reduces music volume during cries

### 2. SoundEffectPool
- **Missing:** Pooling for sound effect instances
- **Required:** Efficient reuse of `SoundEffectInstance` objects

### 3. Preloading Strategy
- **Missing:** Clear preloading strategy for audio assets
- **Required:** Preload music for adjacent maps, preload common SFX

### 4. Audio Configuration
- **Missing:** `AudioConfiguration` class with default volumes, fade durations
- **Required:** Centralized audio settings

### 5. Crossfade Support
- **Missing:** Explicit crossfade API in `IAudioService`
- **Required:** `CrossfadeMusic(string newMusicId, float duration)` method

---

## Arch ECS/Event Issues

### 1. Query Caching
✅ **Good:** Design correctly caches `QueryDescription` in constructors

### 2. Event Subscription Disposal
✅ **Good:** Systems implement `IDisposable` and unsubscribe

### 3. RefAction Pattern
✅ **Good:** Uses `RefAction<T>` for event handlers

### 4. Component Purity
❌ **Issue:** `AudioSourceComponent` has `CalculateVolume` and `CalculatePan` methods
- **Fix:** Remove methods, move calculations to `AudioSystem`

### 5. Entity References
✅ **Good:** Uses `Entity` as dictionary keys (value type, safe)

### 6. System Priority
✅ **Good:** Systems implement `IPrioritizedSystem`

---

## Recommended Fixes Priority

### Critical (Must Fix Before Implementation)
1. ✅ Add `MusicComponent` for map-level music
2. ✅ Create `MapMusicOrchestrator` service
3. ✅ Fix entity lifecycle management in `AudioSystem`
4. ✅ Fix position synchronization (use `PositionComponent`)

### High Priority
5. ✅ Optimize `MusicZoneSystem` (event-driven or spatial queries)
6. ✅ Add `ContinueMusicAcrossMaps` support
7. ✅ Define music zone loading from map definitions
8. ✅ Add missing events (`PauseMusicEvent`, `ResumeMusicEvent`, `AudioVolumeChangedEvent`)

### Medium Priority
9. ✅ Add ducking controller for Pokemon cries
10. ✅ Add sound effect pooling
11. ✅ Add crossfade API
12. ✅ Integrate audio definition loading

### Low Priority
13. ✅ Add audio configuration class
14. ✅ Add preloading strategy
15. ✅ Improve handle management (volume updates)

---

## Summary

The audio system design has several critical architecture issues:

1. **MusicZone is half-thought-out** - Conflates map-level music with zone-level music
2. **Missing map-level music component** - No `MusicComponent` for maps
3. **Missing MapMusicOrchestrator** - No service for map music transitions
4. **Entity lifecycle issues** - No cleanup for destroyed entities
5. **Position synchronization** - Duplicate position data, no sync
6. **Inefficient queries** - Per-frame checks instead of events
7. **Missing features** - Ducking, pooling, crossfade, etc.

The design needs significant revision before implementation, particularly around music handling and entity lifecycle management.

