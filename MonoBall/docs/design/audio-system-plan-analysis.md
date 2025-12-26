# Audio System Plan Analysis

## Overview

This document analyzes the implementation plan against the design document to identify inconsistencies, missing dependencies, and architectural issues.

## Critical Issues Found

### 1. ❌ AudioEngine Missing Dependencies

**Issue:** Plan shows `AudioEngine` constructor only takes `IModManager` and `ILogger`, but `AudioEngine` needs to load audio files internally.

**Design Shows:**
- `IAudioEngine.PlayMusic(string audioId, ...)` - takes audioId string
- `IAudioEngine.PlaySound(string audioId, ...)` - takes audioId string
- AudioEngine must resolve audioId → definition → mod manifest → file path → VorbisReader

**Plan Shows:**
```csharp
Constructor: AudioEngine(IModManager modManager, ILogger logger)
```

**Problem:**
- AudioEngine needs `DefinitionRegistry` to get `AudioDefinition` from `audioId`
- AudioEngine needs `IAudioContentLoader` to load `VorbisReader` instances
- OR AudioEngine needs `IModManager` (which provides registry access) and creates its own content loader

**Fix:**
```csharp
// Option 1: AudioEngine receives all dependencies
AudioEngine(DefinitionRegistry registry, IModManager modManager, IAudioContentLoader contentLoader, ILogger logger)

// Option 2: AudioEngine creates content loader internally (like SpriteLoaderService pattern)
AudioEngine(IModManager modManager, ILogger logger)
// Internally creates AudioContentLoader
```

**Recommendation:** Use Option 2 (AudioEngine creates AudioContentLoader internally) to match `SpriteLoaderService` pattern where services are self-contained.

---

### 2. ❌ MusicPlaybackSystem Unnecessary Dependency

**Issue:** Plan shows `MusicPlaybackSystem` takes `IAudioContentLoader`, but design implementation doesn't use it.

**Design Shows:**
```csharp
public MusicPlaybackSystem(
    World world,
    DefinitionRegistry registry,
    IModManager modManager,
    IAudioEngine audioEngine,
    IAudioContentLoader contentLoader,  // ❌ Not used in implementation
    ILogger logger
)
```

**Design Implementation:**
```csharp
private void OnPlayMusic(ref PlayMusicEvent evt)
{
    // Gets definition from registry
    var definition = _registry.GetById<AudioDefinition>(evt.AudioId);
    // Calls AudioEngine directly - doesn't use contentLoader
    _audioEngine.PlayMusic(evt.AudioId, loop, fadeIn);
}
```

**Plan Shows:**
- MusicPlaybackSystem constructor includes `IAudioContentLoader contentLoader`

**Problem:**
- `MusicPlaybackSystem` doesn't need `IAudioContentLoader` - `AudioEngine` handles file loading internally
- `MusicPlaybackSystem` also doesn't need `IModManager` - only needs `DefinitionRegistry` to get definition

**Fix:**
```csharp
public MusicPlaybackSystem(
    World world,
    DefinitionRegistry registry,
    IAudioEngine audioEngine,
    ILogger logger
)
```

---

### 3. ❌ AmbientSoundSystem Unnecessary Dependencies

**Issue:** Plan shows `AmbientSoundSystem` takes `IAudioContentLoader` and `IModManager`, but design implementation doesn't use them.

**Design Shows:**
```csharp
public AmbientSoundSystem(
    World world,
    DefinitionRegistry registry,
    IModManager modManager,  // ❌ Not used
    IAudioEngine audioEngine,
    IAudioContentLoader contentLoader,  // ❌ Not used
    ILogger logger
)
```

**Design Implementation:**
```csharp
// Only uses registry and audioEngine
var definition = _registry.GetById<AudioDefinition>(ambient.AudioId);
var instance = _audioEngine.PlayLoopingSound(ambient.AudioId, volume);
```

**Plan Shows:**
- AmbientSoundSystem constructor includes `IModManager` and `IAudioContentLoader`

**Problem:**
- `AmbientSoundSystem` only needs `DefinitionRegistry` and `IAudioEngine`
- File loading is handled by `AudioEngine` internally

**Fix:**
```csharp
public AmbientSoundSystem(
    World world,
    DefinitionRegistry registry,
    IAudioEngine audioEngine,
    ILogger logger
)
```

---

### 4. ❌ AudioEngine Internal Implementation Not Specified

**Issue:** Plan doesn't specify how `AudioEngine` internally loads audio files.

**Design Shows:**
- `IAudioEngine.PlayMusic(string audioId, ...)` - takes audioId
- But AudioEngine needs to:
  1. Get `AudioDefinition` from registry
  2. Get `ModManifest` from modManager
  3. Load `VorbisReader` via `IAudioContentLoader`
  4. Play audio

**Plan Shows:**
- Phase 1.5: "Create AudioEngine stub implementation"
- Phase 3.3: "Implement AudioEngine Music Playback"
- But doesn't specify AudioEngine needs `DefinitionRegistry` and `IAudioContentLoader` internally

**Fix:**
- Add to Phase 1.5: AudioEngine constructor should create `AudioContentLoader` internally OR receive it as dependency
- Add to Phase 3.3: Specify that AudioEngine internally:
  - Gets definition: `_registry.GetById<AudioDefinition>(audioId)`
  - Gets mod manifest: `_modManager.GetModManifestByDefinitionId(audioId)`
  - Loads VorbisReader: `_contentLoader.CreateVorbisReader(audioId, definition, modManifest)`

---

### 5. ⚠️ System Registration Order

**Issue:** Plan shows registering systems in Phase 7, but some systems depend on others.

**Design Shows:**
- `MapMusicSystem` fires `PlayMusicEvent`
- `MusicPlaybackSystem` handles `PlayMusicEvent`
- Both need to be registered, but order doesn't matter (event-driven)

**Plan Shows:**
- Phase 7.2: Register all systems together

**Status:** ✅ Actually fine - event-driven architecture means order doesn't matter. But should verify all dependencies are available.

---

### 6. ⚠️ Missing ISoundEffectInstance Interface

**Issue:** Plan references `ISoundEffectInstance` but doesn't specify creating it.

**Design Shows:**
- `IAudioEngine.PlaySound()` returns `ISoundEffectInstance?`
- `IAudioEngine.PlayLoopingSound()` returns `ISoundEffectInstance?`
- `AmbientSoundSystem` stores `Dictionary<Entity, ISoundEffectInstance>`

**Plan Shows:**
- Phase 4.2: "Return `ISoundEffectInstance` interface for control"
- But no phase to create `ISoundEffectInstance` interface/implementation

**Fix:**
- Add to Phase 1: Create `ISoundEffectInstance` interface
- Add to Phase 4: Implement `ISoundEffectInstance` class

---

### 7. ⚠️ AudioEngine.Update() Call Location

**Issue:** Plan shows calling `AudioEngine.Update()` in `SystemManager.Update()`, but should verify this is correct.

**Design Shows:**
- `IAudioEngine` has `Update(float deltaTime)` method
- Should be called every frame

**Plan Shows:**
- Phase 7.3: "Call `_audioEngine.Update(deltaTime)` in `SystemManager.Update()`"

**Status:** ✅ Correct - AudioEngine needs to update audio state every frame (fades, crossfades, etc.)

---

### 8. ⚠️ VorbisReader Type Not Defined

**Issue:** Plan references `VorbisReader` but doesn't specify if it's a custom wrapper or NVorbis type.

**Design Shows:**
- `IAudioContentLoader.CreateVorbisReader()` returns `VorbisReader?`
- Design mentions "Custom wrapper around `NVorbis.VorbisReader`"

**Plan Shows:**
- Phase 1.3: "Create/adapt audio core classes: ... VorbisReader (wrapper around NVorbis.VorbisReader)"
- Phase 1.7: "Cache `VorbisReader` instances or metadata"

**Status:** ✅ Actually covered in Phase 1.3, but should be more explicit that VorbisReader is a custom wrapper class.

---

### 9. ⚠️ AudioEngine Constructor Pattern Inconsistency

**Issue:** Plan shows different constructor patterns for AudioEngine vs other services.

**Existing Pattern (SpriteLoaderService):**
```csharp
SpriteLoaderService(GraphicsDevice graphicsDevice, IModManager modManager, ...)
// Creates services internally or receives them
```

**Plan Shows:**
```csharp
AudioEngine(IModManager modManager, ILogger logger)
// Should create AudioContentLoader internally
```

**Status:** ✅ Actually consistent - AudioEngine should create AudioContentLoader internally like SpriteLoaderService pattern. But need to verify AudioEngine also needs DefinitionRegistry access.

**Fix:**
- AudioEngine needs `IModManager` (provides registry via `ModManager.Registry`)
- AudioEngine creates `AudioContentLoader` internally
- AudioEngine uses `_modManager.Registry` to get definitions

---

### 10. ⚠️ Missing AudioEngine Disposal

**Issue:** Plan doesn't specify disposing AudioEngine and AudioContentLoader.

**Design Shows:**
- Systems implement `IDisposable`
- Services should also be disposed

**Plan Shows:**
- Phase 7.4: "Dispose audio systems and services"
- But doesn't specify AudioEngine/ContentLoader disposal

**Fix:**
- Add to Phase 7.4: Ensure `AudioEngine` and `AudioContentLoader` implement `IDisposable` if they have resources
- Dispose them in `SystemManager.Dispose()`

---

## Summary of Required Plan Fixes

1. **Phase 1.5**: Update AudioEngine constructor to create AudioContentLoader internally OR receive it as dependency. Specify AudioEngine needs access to DefinitionRegistry (via IModManager).

2. **Phase 1.7**: Clarify that AudioContentLoader is created by AudioEngine internally (not passed to systems).

3. **Phase 3.2**: Remove `IAudioContentLoader` and `IModManager` from MusicPlaybackSystem constructor. Only needs `DefinitionRegistry` and `IAudioEngine`.

4. **Phase 3.3**: Specify that AudioEngine internally:
   - Gets definition from registry
   - Gets mod manifest from modManager
   - Loads VorbisReader via contentLoader
   - Plays audio

5. **Phase 4**: Add creation of `ISoundEffectInstance` interface and implementation.

6. **Phase 5.1**: Remove `IAudioContentLoader` and `IModManager` from AmbientSoundSystem constructor. Only needs `DefinitionRegistry` and `IAudioEngine`.

7. **Phase 7.4**: Add disposal of AudioEngine and AudioContentLoader.

8. **Phase 1.3**: Clarify that VorbisReader is a custom wrapper class around NVorbis.VorbisReader.

---

## Recommended Plan Updates

### Update Phase 1.5 (AudioEngine Stub)

**Current:**
```
Constructor: AudioEngine(IModManager modManager, ILogger logger)
Initialize PortAudio (following oldmonoball pattern)
Stub methods throw NotImplementedException initially
```

**Updated:**
```
Constructor: AudioEngine(IModManager modManager, ILogger logger)
- Store _modManager for registry access (_modManager.Registry)
- Create AudioContentLoader internally: _contentLoader = new AudioContentLoader(_modManager, logger)
- Initialize PortAudio (following oldmonoball pattern)
- Stub methods throw NotImplementedException initially
- Note: AudioEngine will internally resolve audioId → definition → mod manifest → VorbisReader
```

### Update Phase 3.2 (MusicPlaybackSystem)

**Current:**
```
Constructor: MusicPlaybackSystem(World world, DefinitionRegistry registry, IModManager modManager, IAudioEngine audioEngine, IAudioContentLoader contentLoader, ILogger logger)
```

**Updated:**
```
Constructor: MusicPlaybackSystem(World world, DefinitionRegistry registry, IAudioEngine audioEngine, ILogger logger)
- Remove IModManager and IAudioContentLoader (not needed - AudioEngine handles file loading)
```

### Update Phase 3.3 (AudioEngine Music Implementation)

**Current:**
```
Implement PlayMusic(): Load audio via IAudioContentLoader, create VorbisReader, set up looping
```

**Updated:**
```
Implement PlayMusic(string audioId, bool loop, float fadeInDuration):
1. Get definition: _modManager.Registry.GetById<AudioDefinition>(audioId)
2. Get mod manifest: _modManager.GetModManifestByDefinitionId(audioId)
3. Load VorbisReader: _contentLoader.CreateVorbisReader(audioId, definition, modManifest)
4. Set up looping with loop points from definition
5. Play via PortAudioOutput
```

### Update Phase 4 (Sound Effects)

**Add new task:**
```
Phase 4.0: Create ISoundEffectInstance Interface
- File: MonoBall.Core/Audio/ISoundEffectInstance.cs
- Interface with: IsPlaying, Volume, Pitch, Pan properties
- Methods: Stop(), Pause(), Resume()
- Namespace: MonoBall.Core.Audio
```

**Update Phase 4.2:**
```
Implement PlaySound(): Load audio (same pattern as PlayMusic), create ISoundEffectInstance implementation
Return ISoundEffectInstance for control
```

### Update Phase 5.1 (AmbientSoundSystem)

**Current:**
```
Constructor: AmbientSoundSystem(World world, DefinitionRegistry registry, IModManager modManager, IAudioEngine audioEngine, IAudioContentLoader contentLoader, ILogger logger)
```

**Updated:**
```
Constructor: AmbientSoundSystem(World world, DefinitionRegistry registry, IAudioEngine audioEngine, ILogger logger)
- Remove IModManager and IAudioContentLoader (not needed - AudioEngine handles file loading)
```

### Update Phase 7.4 (Disposal)

**Current:**
```
Dispose audio systems and services in Dispose() method
```

**Updated:**
```
Dispose audio systems and services in Dispose() method:
- Dispose all audio systems (they implement IDisposable)
- Dispose AudioEngine (if it implements IDisposable)
- Dispose AudioContentLoader (if it implements IDisposable)
- Follow existing disposal pattern
```

---

## Architecture Clarification

### AudioEngine Internal Flow

```
AudioEngine.PlayMusic(audioId)
  ↓
1. Get definition: _modManager.Registry.GetById<AudioDefinition>(audioId)
  ↓
2. Get mod manifest: _modManager.GetModManifestByDefinitionId(audioId)
  ↓
3. Load VorbisReader: _contentLoader.CreateVorbisReader(audioId, definition, modManifest)
  ↓
4. Set up looping with loop points from definition
  ↓
5. Play via PortAudioOutput
```

### System Dependencies (Corrected)

```
MapMusicSystem
  ├── World
  ├── DefinitionRegistry (for querying MusicComponent)
  └── ILogger

MusicPlaybackSystem
  ├── World
  ├── DefinitionRegistry (for getting AudioDefinition)
  ├── IAudioEngine (for playback)
  └── ILogger

SoundEffectSystem
  ├── World
  ├── DefinitionRegistry (for getting AudioDefinition)
  ├── IAudioEngine (for playback)
  └── ILogger

AmbientSoundSystem
  ├── World
  ├── DefinitionRegistry (for getting AudioDefinition)
  ├── IAudioEngine (for playback)
  └── ILogger

AudioVolumeSystem
  ├── World
  ├── IAudioEngine (for volume control)
  └── ILogger

AudioEngine (Service)
  ├── IModManager (for registry and mod manifest access)
  ├── IAudioContentLoader (created internally)
  └── ILogger

AudioContentLoader (Service)
  ├── IModManager (for mod manifest access)
  └── ILogger
```

---

## Conclusion

The plan is mostly correct but has several dependency issues:
1. Systems have unnecessary dependencies (`IAudioContentLoader`, `IModManager`)
2. AudioEngine internal implementation not fully specified
3. Missing `ISoundEffectInstance` interface creation
4. Missing disposal specification

These issues should be fixed before implementation to ensure correct architecture.

