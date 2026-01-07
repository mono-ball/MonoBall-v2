# Audio System Design Summary

## Key Design Decisions

### 1. PortAudio + NVorbis (Not MonoGame Audio)

**Decision:** Use PortAudioSharp for cross-platform audio output and NVorbis for OGG Vorbis file reading.

**Rationale:**
- MonoGame's audio APIs don't support OGG Vorbis loop points
- MonoGame's `Song` class doesn't support custom loop points
- MonoGame doesn't support true crossfading
- PortAudio provides better cross-platform support
- NVorbis provides streaming OGG support with loop point metadata

**Trade-offs:**
- Additional dependencies (PortAudioSharp, NVorbis)
- More complex than MonoGame APIs
- But necessary for required features (loop points, crossfading, streaming)

### 2. ECS-Based Architecture

**Decision:** Manage audio state through ECS components and systems, not services.

**Rationale:**
- Consistent with MonoBall's architecture
- Enables mods to attach audio to entities
- Better separation of concerns
- Easier to test and extend

**Components:**
- `MusicComponent`: Maps specify background music
- `SoundEffectRequestComponent`: Request sound effect playback
- `AmbientSoundComponent`: Entities emit ambient sounds

**Systems:**
- `MapMusicSystem`: Handles map music transitions
- `SoundEffectSystem`: Processes sound effect requests
- `AmbientSoundSystem`: Manages ambient sounds
- `AudioVolumeSystem`: Manages volume settings

### 3. Event-Driven Operations

**Decision:** All audio operations triggered via events.

**Rationale:**
- Decouples audio from game logic
- Enables mods to trigger audio
- Consistent with MonoBall's event architecture
- Easy to add audio logging/debugging

**Events:**
- `PlayMusicEvent`: Request music playback
- `StopMusicEvent`: Request music stop
- `PlaySoundEffectEvent`: Request sound effect playback
- Volume events: `SetMasterVolumeEvent`, `SetMusicVolumeEvent`, `SetSoundEffectVolumeEvent`

### 4. Mod-Based Audio Definitions

**Decision:** Audio definitions loaded from mod JSON files, like other definitions.

**Rationale:**
- Consistent with MonoBall's mod system
- Enables mods to add custom audio
- Centralized audio metadata (volume, fade times, loop points)
- Easy to extend with new audio types

**Definition Format:**
```json
{
  "id": "route_101_music",
  "type": "audio",
  "assetPath": "Audio/Music/route_101.ogg",
  "audioType": "music",
  "volume": 0.8,
  "fadeIn": 1.0,
  "fadeOut": 1.5,
  "loopStart": 0,
  "loopEnd": 0
}
```

### 5. OGG Vorbis with Metadata Support

**Decision:** Support OGG Vorbis files with loop point metadata.

**Rationale:**
- OGG is patent-free and well-supported
- Loop points enable seamless music looping
- Metadata can be stored in Vorbis comments

**Implementation:**
- Parse OGG metadata using NVorbis (or similar)
- Extract loop points from Vorbis comments
- Use `SoundEffectInstance` for music with loop points (instead of `Song`)

### 6. Fail-Fast Error Handling

**Decision:** Throw clear exceptions instead of silent fallbacks.

**Rationale:**
- Consistent with MonoBall's architecture principles
- Easier to debug audio issues
- Prevents silent failures

**Examples:**
- `ArgumentNullException` for null dependencies
- `InvalidOperationException` for invalid state
- `FileNotFoundException` for missing audio files

## Architecture Comparison

### oldmonoball Issues

1. **Complex Service Layer**: Multiple services (`AudioService`, `MusicPlayer`, `SoundEffectManager`) with unclear responsibilities
2. **Tight Coupling**: Services directly called from game logic
3. **Poor Extensibility**: Hard to add new audio types or behaviors
4. **Service-Based**: Not integrated with ECS architecture

### MonoBall Improvements

1. **ECS-Based**: Audio state managed through components, systems handle logic
2. **Event-Driven**: All operations via events, decoupled systems
3. **Same Audio Libraries**: Still uses PortAudio + NVorbis (required for features)
4. **Mod-Extensible**: Audio definitions loaded like other mod definitions
5. **Clear Separation**: Each system has a single responsibility
6. **Better Integration**: Fits into MonoBall's ECS/event architecture

## Implementation Priority

### Phase 1: Core Infrastructure (Critical)
- Audio engine and content loader
- Audio definition loading
- Basic volume management

### Phase 2: Music System (High)
- Map music component and system
- Music playback with transitions
- OGG metadata parsing

### Phase 3: Sound Effects (High)
- Sound effect component and system
- Sound effect playback

### Phase 4: Advanced Features (Medium)
- Crossfading
- Ambient sounds
- Volume persistence

## Key Files

### Core Services
- `Audio/Services/IAudioEngine.cs`: Low-level audio playback interface
- `Audio/Services/AudioEngine.cs`: MonoGame audio implementation
- `Audio/Services/IAudioContentLoader.cs`: Audio asset loading interface
- `Audio/Services/AudioContentLoader.cs`: Mod-based audio loading

### ECS Components
- `ECS/Components/Audio/MusicComponent.cs`: Map background music
- `ECS/Components/Audio/SoundEffectRequestComponent.cs`: Sound effect requests
- `ECS/Components/Audio/AmbientSoundComponent.cs`: Ambient sounds

### ECS Systems
- `ECS/Systems/Audio/MapMusicSystem.cs`: Map music management
- `ECS/Systems/Audio/SoundEffectSystem.cs`: Sound effect processing
- `ECS/Systems/Audio/AmbientSoundSystem.cs`: Ambient sound management
- `ECS/Systems/Audio/AudioVolumeSystem.cs`: Volume management

### Events
- `ECS/Events/Audio/PlayMusicEvent.cs`: Music playback request
- `ECS/Events/Audio/StopMusicEvent.cs`: Music stop request
- `ECS/Events/Audio/PlaySoundEffectEvent.cs`: Sound effect playback request
- Volume events: `SetMasterVolumeEvent`, `SetMusicVolumeEvent`, `SetSoundEffectVolumeEvent`

## Cross-Platform Support

### PortAudio for Audio Output
- **Windows**: PortAudio uses DirectSound/XAudio2
- **Mac**: PortAudio uses CoreAudio
- **Linux**: PortAudio uses ALSA/PulseAudio

PortAudio abstracts platform differences and provides consistent API.

### NVorbis for OGG Vorbis
- **OGG Vorbis**: Fully supported via NVorbis library
  - Streaming support (reads samples on-demand)
  - Thread-safe reading
  - Seek support (by sample or time)
  - Loop point metadata support

### File Formats
- **OGG Vorbis**: Fully supported via NVorbis (recommended, already standard)
- **WAV**: Could be added via custom reader (not currently used)
- **MP3**: Not supported (use OGG Vorbis instead)

## Testing Strategy

1. **Unit Tests**: Audio engine, content loader, metadata parsing
2. **Integration Tests**: Systems with mock world
3. **Manual Testing**: Cross-platform playback, loop points, crossfading

## Future Enhancements

1. **3D Audio**: Positional sound effects
2. **Audio Zones**: Different music in map regions
3. **Dynamic Music**: Music changes based on game state
4. **Audio Debug UI**: Visualize playing sounds, volumes

