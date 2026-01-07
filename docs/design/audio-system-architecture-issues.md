# Audio System Design - Architecture Issues Analysis

## Overview

This document identifies architecture issues, ECS/event issues, .cursorrules violations, and definition/resource loading pattern mismatches in the audio system design.

## Critical Issues

### 1. ❌ Definition Access Pattern Mismatch

**Issue:** Design uses `AudioContentLoader.GetDefinition()` wrapper, but existing code accesses definitions directly from `DefinitionRegistry`.

**Current Pattern (from existing code):**
```csharp
// Systems receive DefinitionRegistry directly
var mapDefinition = _registry.GetById<MapDefinition>(mapId);

// Or via IModManager
var definition = _modManager.GetDefinition<T>(id);
```

**Design Issue:**
```csharp
// ❌ BAD: Wrapper service for definition access
AudioDefinition GetDefinition(string audioId); // Gets definition from registry
```

**Fix:**
- Systems should receive `DefinitionRegistry` directly (like `MapLoaderSystem`)
- Use `_registry.GetById<AudioDefinition>(audioId)` directly
- Remove `AudioContentLoader.GetDefinition()` method
- `AudioContentLoader` should only handle file loading, not definition access

---

### 2. ❌ Resource Loading Pattern Mismatch

**Issue:** Design doesn't follow existing resource loading pattern for resolving mod directories.

**Current Pattern (from SpriteLoaderService/TilesetLoaderService):**
```csharp
// 1. Get definition metadata
var metadata = _modManager.GetDefinitionMetadata(definitionId);

// 2. Get mod manifest by definition ID
var modManifest = _modManager.GetModManifestByDefinitionId(definitionId);

// 3. Combine mod directory with path from definition
string fullPath = Path.Combine(modManifest.ModDirectory, definition.AssetPath);
fullPath = Path.GetFullPath(fullPath);
```

**Design Issue:**
```csharp
// ❌ BAD: Doesn't show how to resolve mod directory
// Resolve the mod directory from the definition's OriginalModId
// Create VorbisReader for the audio file at {modDirectory}/{audioPath}
```

**Fix:**
- `AudioContentLoader` should use `IModManager.GetModManifestByDefinitionId(audioId)`
- Follow exact pattern from `SpriteLoaderService.LoadTexture()`
- Return `null` on failure (matching existing pattern), but throw exceptions for invalid state

---

### 3. ❌ Component Stores Managed Resource

**Issue:** `AmbientSoundComponent` stores `SoundEffectInstance?` which is a managed resource, violating ECS component rules.

**Rule Violation (.cursorrules):**
> **ECS Components**: Value types (`struct`) only, data not behavior, end names with `Component` suffix

**Design Issue:**
```csharp
public struct AmbientSoundComponent
{
    public SoundEffectInstance? Instance { get; set; } // ❌ Managed resource in struct
    public bool IsPlaying { get; set; } // ❌ Behavior state, not data
}
```

**Fix:**
- Remove `Instance` and `IsPlaying` from component
- Store only data: `AudioId`, `Volume`, `Pitch`, `Pan`
- System manages instances in a dictionary: `Dictionary<Entity, SoundEffectInstance>`
- Or use a separate component for instance handle (but this is still problematic)

**Better Approach:**
```csharp
public struct AmbientSoundComponent
{
    public string AudioId { get; set; }
    public float Volume { get; set; } // -1 = use definition default
    public float Pitch { get; set; }
    public float Pan { get; set; }
}

// System manages instances
private readonly Dictionary<Entity, ISoundEffectInstance> _ambientInstances = new();
```

---

### 4. ❌ System Dependencies Pattern Mismatch

**Issue:** Systems should receive `DefinitionRegistry` and `IModManager` directly, not through a content loader service.

**Current Pattern (from MapLoaderSystem):**
```csharp
public MapLoaderSystem(
    World world,
    DefinitionRegistry registry,  // ✅ Direct dependency
    ITilesetLoaderService? tilesetLoader = null,  // ✅ Service for resource loading
    // ...
)
```

**Design Issue:**
```csharp
// ❌ BAD: Content loader wraps definition access
public SoundEffectSystem(
    World world,
    IAudioEngine audioEngine,
    IAudioContentLoader audioContentLoader,  // ❌ Should receive DefinitionRegistry + IModManager
    ILogger logger
)
```

**Fix:**
```csharp
public SoundEffectSystem(
    World world,
    DefinitionRegistry registry,  // ✅ Direct dependency
    IModManager modManager,  // ✅ For resolving mod paths
    IAudioEngine audioEngine,
    ILogger logger
)
```

---

### 5. ❌ Error Handling Inconsistency

**Issue:** Design shows exceptions, but existing resource loaders return `null` on failure.

**Current Pattern (from SpriteLoaderService):**
```csharp
// Returns null on failure, logs warning
if (!File.Exists(texturePath))
{
    _logger.Warning("Sprite texture file not found: {TexturePath}", texturePath);
    return null;
}
```

**Design Issue:**
```csharp
// ❌ Shows exceptions, but should return null for missing files
throw new FileNotFoundException($"Audio file not found: {fullPath}", fullPath);
```

**Fix:**
- Return `null` for missing files (matching existing pattern)
- Throw exceptions only for invalid state (null dependencies, invalid arguments)
- Log warnings for missing files

**Clarification:**
- **Fail-Fast**: For invalid state (null dependencies, invalid arguments) → throw exceptions
- **Graceful Degradation**: For missing resources → return null, log warning

---

### 6. ❌ Missing IDisposable Implementation

**Issue:** `AudioVolumeSystem` subscribes to events but doesn't show `IDisposable` implementation.

**Rule Violation (.cursorrules):**
> **Event Subscriptions**: MUST implement `IDisposable` and unsubscribe in `Dispose()` to prevent leaks

**Design Issue:**
```csharp
// ❌ Missing IDisposable
public class AudioVolumeSystem : BaseSystem<World, float>
{
    // Subscribes to volume events
    // But no Dispose() method shown
}
```

**Fix:**
```csharp
public class AudioVolumeSystem : BaseSystem<World, float>, IDisposable
{
    private bool _disposed = false;
    
    public AudioVolumeSystem(World world, IAudioEngine audioEngine, ILogger logger) : base(world)
    {
        EventBus.Subscribe<SetMasterVolumeEvent>(OnMasterVolumeChanged);
        EventBus.Subscribe<SetMusicVolumeEvent>(OnMusicVolumeChanged);
        EventBus.Subscribe<SetSoundEffectVolumeEvent>(OnSoundEffectVolumeChanged);
    }
    
    public new void Dispose()
    {
        if (!_disposed)
        {
            EventBus.Unsubscribe<SetMasterVolumeEvent>(OnMasterVolumeChanged);
            EventBus.Unsubscribe<SetMusicVolumeEvent>(OnMusicVolumeChanged);
            EventBus.Unsubscribe<SetSoundEffectVolumeEvent>(OnSoundEffectVolumeChanged);
            _disposed = true;
        }
    }
}
```

---

### 7. ❌ Definition Type String Unknown

**Issue:** Design doesn't specify what definition type string is used for audio definitions.

**Current Pattern:**
- Maps use `"map"` (from `MapDefinition`)
- Sprites use `"sprite"` (from `SpriteDefinition`)
- Audio definitions likely use `"audio"` but needs verification

**Design Issue:**
```csharp
// ❌ Unclear what DefinitionType string is used
// Audio definitions are already loaded by ModLoader into DefinitionRegistry with type "audio"
```

**Fix:**
- Verify definition type string from existing audio definitions
- Document it clearly: `DefinitionType = "audio"`
- Use `_registry.GetByType("audio")` if needed

---

### 8. ❌ System Registration Pattern Mismatch

**Issue:** Design shows creating services in `SystemManager.Initialize()`, but should follow existing pattern.

**Current Pattern (from SystemManager):**
```csharp
// Services created before systems
_spriteLoader = new SpriteLoaderService(_graphicsDevice, _modManager, _logger);

// Systems created with dependencies
_mapLoaderSystem = new MapLoaderSystem(
    _world,
    _modManager.Registry,  // ✅ Direct registry access
    _tilesetLoader,
    _spriteLoader,
    // ...
);
RegisterUpdateSystem(_mapLoaderSystem);
```

**Design Issue:**
```csharp
// ❌ Creates services inline, doesn't show proper dependency injection
var audioEngine = new AudioEngine(contentManager, logger);
var audioContentLoader = new AudioContentLoader(modManager, contentManager, logger);
```

**Fix:**
```csharp
// Create audio services (similar to sprite/tileset loaders)
_audioEngine = new AudioEngine(_modManager, _logger);
_audioContentLoader = new AudioContentLoader(_modManager, _logger);

// Create audio systems
_mapMusicSystem = new MapMusicSystem(
    _world,
    _modManager.Registry,  // ✅ Direct registry
    _logger
);
RegisterUpdateSystem(_mapMusicSystem);

_soundEffectSystem = new SoundEffectSystem(
    _world,
    _modManager.Registry,  // ✅ Direct registry
    _modManager,  // ✅ For path resolution
    _audioEngine,
    _logger
);
RegisterUpdateSystem(_soundEffectSystem);
```

---

### 9. ❌ Namespace Structure

**Issue:** Design shows `Audio/Services/` but should match folder structure for namespace.

**Rule Violation (.cursorrules):**
> **Namespace**: Match folder structure, root is `MonoBall.Core`

**Design Issue:**
```
MonoBall.Core/
├── Audio/
│   ├── Services/
│   │   ├── IAudioEngine.cs
│   │   └── AudioEngine.cs
```

**Fix:**
- Namespace should be `MonoBall.Core.Audio.Services`
- Or follow existing pattern: `MonoBall.Core.Audio` (if Audio is top-level)
- Check existing services: `MonoBall.Core.Maps` for `SpriteLoaderService`

**Existing Pattern:**
- `MonoBall.Core.Maps.SpriteLoaderService` → `Maps/SpriteLoaderService.cs`
- `MonoBall.Core.Maps.TilesetLoaderService` → `Maps/TilesetLoaderService.cs`

**Recommendation:**
- `MonoBall.Core.Audio.AudioEngine` → `Audio/AudioEngine.cs`
- `MonoBall.Core.Audio.AudioContentLoader` → `Audio/AudioContentLoader.cs`

---

### 10. ❌ QueryDescription Caching

**Issue:** Design shows `QueryDescription` creation but doesn't emphasize caching in constructor.

**Rule Violation (.cursorrules):**
> **ECS Systems**: Cache `QueryDescription` in constructor, never create queries in Update/Render

**Design Issue:**
```csharp
// ✅ Good: Cached in constructor
private readonly QueryDescription _mapMusicQuery;

public MapMusicSystem(World world, ILogger logger) : base(world)
{
    _mapMusicQuery = new QueryDescription()
        .WithAll<MapComponent, MusicComponent>();
}
```

**Status:** ✅ Actually correct in design, but should emphasize this is required.

---

### 11. ❌ Missing IPrioritizedSystem Implementation

**Issue:** Systems should implement `IPrioritizedSystem` if they have a priority.

**Current Pattern:**
```csharp
public class MapLoaderSystem : BaseSystem<World, float>, IPrioritizedSystem
{
    public int Priority => SystemPriority.MapLoader;
}
```

**Design Issue:**
```csharp
// ❌ Missing IPrioritizedSystem
public class MapMusicSystem : BaseSystem<World, float>, IDisposable
{
    // Priority mentioned but not implemented
}
```

**Fix:**
```csharp
public class MapMusicSystem : BaseSystem<World, float>, IPrioritizedSystem, IDisposable
{
    public int Priority => SystemPriority.Audio;
    // ...
}
```

---

### 12. ❌ AudioDefinition Class Not Defined

**Issue:** Design references `AudioDefinition` class but doesn't define its structure.

**Design Issue:**
```csharp
// ❌ Referenced but not defined
AudioDefinition GetDefinition(string audioId);
var definition = _registry.GetById<AudioDefinition>(audioId);
```

**Fix:**
- Define `AudioDefinition` class matching JSON structure:
```csharp
namespace MonoBall.Core.Audio
{
    public class AudioDefinition
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string AudioPath { get; set; } = string.Empty;
        public float Volume { get; set; } = 1.0f;
        public bool Loop { get; set; } = true;
        public float FadeIn { get; set; }
        public float FadeOut { get; set; }
        public int? LoopStartSamples { get; set; }
        public int? LoopLengthSamples { get; set; }
        public float? LoopStartSec { get; set; }
        public float? LoopEndSec { get; set; }
    }
}
```

---

## Summary of Required Fixes

1. ✅ Remove `AudioContentLoader.GetDefinition()` - systems access `DefinitionRegistry` directly
2. ✅ Update resource loading to use `IModManager.GetModManifestByDefinitionId()` pattern
3. ✅ Fix `AmbientSoundComponent` - remove managed resources, system manages instances
4. ✅ Systems receive `DefinitionRegistry` and `IModManager` directly
5. ✅ Clarify error handling - return null for missing files, throw for invalid state
6. ✅ Add `IDisposable` to `AudioVolumeSystem` and `MapMusicSystem`
7. ✅ Verify definition type string (`"audio"`)
8. ✅ Update system registration to follow existing pattern
9. ✅ Fix namespace structure to match folder structure
10. ✅ Emphasize `QueryDescription` caching requirement
11. ✅ Add `IPrioritizedSystem` implementation to systems
12. ✅ Define `AudioDefinition` class structure

---

## Recommended Architecture Changes

### AudioContentLoader Should Only Load Files

```csharp
public interface IAudioContentLoader
{
    /// <summary>
    /// Creates a VorbisReader for the specified audio definition.
    /// </summary>
    /// <param name="audioId">The audio definition ID.</param>
    /// <returns>The VorbisReader, or null if file not found.</returns>
    VorbisReader? CreateVorbisReader(string audioId);
    
    /// <summary>
    /// Unloads cached metadata for the specified audio.
    /// </summary>
    void Unload(string audioId);
}
```

### Systems Access Definitions Directly

```csharp
public class SoundEffectSystem : BaseSystem<World, float>
{
    private readonly DefinitionRegistry _registry;
    private readonly IModManager _modManager;
    private readonly IAudioEngine _audioEngine;
    private readonly IAudioContentLoader _contentLoader;
    
    public SoundEffectSystem(
        World world,
        DefinitionRegistry registry,
        IModManager modManager,
        IAudioEngine audioEngine,
        IAudioContentLoader contentLoader,
        ILogger logger
    ) : base(world)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _modManager = modManager ?? throw new ArgumentNullException(nameof(modManager));
        _audioEngine = audioEngine ?? throw new ArgumentNullException(nameof(audioEngine));
        _contentLoader = contentLoader ?? throw new ArgumentNullException(nameof(contentLoader));
        
        _soundEffectQuery = new QueryDescription()
            .WithAll<SoundEffectRequestComponent>();
    }
    
    public override void Update(in float deltaTime)
    {
        World.Query(in _soundEffectQuery, (Entity entity, ref SoundEffectRequestComponent request) =>
        {
            // Get definition directly from registry
            var definition = _registry.GetById<AudioDefinition>(request.AudioId);
            if (definition == null)
            {
                _logger.Warning("Audio definition not found: {AudioId}", request.AudioId);
                World.Remove<SoundEffectRequestComponent>(entity);
                return;
            }
            
            // Play sound effect
            // ...
            
            // Remove component
            World.Remove<SoundEffectRequestComponent>(entity);
        });
    }
}
```

