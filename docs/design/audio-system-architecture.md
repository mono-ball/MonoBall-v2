# Audio System Architecture Diagram

## System Overview

```
┌─────────────────────────────────────────────────────────────────┐
│                         Game Logic                               │
│  (Maps, NPCs, UI, etc.)                                          │
└───────────────────────────┬─────────────────────────────────────┘
                            │ Events
                            ▼
┌─────────────────────────────────────────────────────────────────┐
│                      Event Bus                                   │
│  - PlayMusicEvent                                                │
│  - StopMusicEvent                                                │
│  - PlaySoundEffectEvent                                          │
│  - Volume Events                                                 │
└───────────────────────────┬─────────────────────────────────────┘
                            │
                ┌───────────┴───────────┐
                │                       │
                ▼                       ▼
┌───────────────────────────┐  ┌───────────────────────────┐
│    ECS Audio Systems       │  │   Audio Engine Services    │
│                            │  │                            │
│  ┌─────────────────────┐  │  │  ┌─────────────────────┐  │
│  │ MapMusicSystem      │  │  │  │ IAudioEngine        │  │
│  │ - Listens to events │  │  │  │ - PlayMusic()       │  │
│  │ - Queries maps      │  │  │  │ - PlaySound()       │  │
│  │ - Fires events      │  │  │  │ - Volume control    │  │
│  └─────────────────────┘  │  │  └─────────────────────┘  │
│                            │  │                            │
│  ┌─────────────────────┐  │  │  ┌─────────────────────┐  │
│  │ SoundEffectSystem   │  │  │  │ IAudioContentLoader │  │
│  │ - Processes requests│  │  │  │ - LoadSoundEffect() │  │
│  │ - Plays sounds      │  │  │  │ - LoadSong()        │  │
│  └─────────────────────┘  │  │  │ - GetMetadata()     │  │
│                            │  │  └─────────────────────┘  │
│  ┌─────────────────────┐  │  │                            │
│  │ AmbientSoundSystem  │  │  │                            │
│  │ - Manages loops     │  │  │                            │
│  └─────────────────────┘  │  │                            │
│                            │  │                            │
│  ┌─────────────────────┐  │  │                            │
│  │ AudioVolumeSystem   │  │  │                            │
│  │ - Volume events     │  │  │                            │
│  └─────────────────────┘  │  │                            │
└───────────────────────────┘  └───────────────────────────┘
            │                              │
            │                              │
            ▼                              ▼
┌───────────────────────────┐  ┌───────────────────────────┐
│    ECS Components         │  │   Audio Libraries         │
│                            │  │                            │
│  ┌─────────────────────┐  │  │  ┌─────────────────────┐  │
│  │ MusicComponent      │  │  │  │ PortAudioOutput     │  │
│  │ - audioId           │  │  │  │ (cross-platform)    │  │
│  │ - fadeDuration       │  │  │  └─────────────────────┘  │
│  └─────────────────────┘  │  │                            │
│                            │  │  ┌─────────────────────┐  │
│  ┌─────────────────────┐  │  │  │ VorbisReader        │  │
│  │ SoundEffectRequest   │  │  │  │ (NVorbis wrapper)  │  │
│  │ Component           │  │  │  └─────────────────────┘  │
│  └─────────────────────┘  │  │                            │
│                            │  │  ┌─────────────────────┐  │
│  ┌─────────────────────┐  │  │  │ AudioMixer         │  │
│  │ AmbientSound        │  │  │  │ (crossfading)      │  │
│  │ Component          │  │  │  └─────────────────────┘  │
│  └─────────────────────┘  │  │                            │
└───────────────────────────┘  └───────────────────────────┘
            │                              │
            │                              │
            └──────────────┬───────────────┘
                           │
                           ▼
                ┌───────────────────────┐
                │   Mod Definitions     │
                │   - Audio definitions │
                │   - Map definitions   │
                │   (with MusicComponent)│
                └───────────────────────┘
```

## Event Flow: Map Music Transition

```
1. Player crosses map boundary
   │
   ▼
2. MapTransitionDetectionSystem detects transition
   │
   ▼
3. Fires MapTransitionEvent
   │
   ▼
4. MapMusicSystem receives event
   │
   ▼
5. Queries World for target map's MusicComponent
   │
   ▼
6. Fires PlayMusicEvent with audioId and fade settings
   │
   ▼
7. AudioEngine receives event (via system or direct call)
   │
   ▼
8. AudioContentLoader loads audio file from mods
   │
   ▼
9. AudioEngine plays music via MonoGame APIs
   │
   ▼
10. Music plays with fade-in/crossfade
```

## Event Flow: Sound Effect Playback

```
1. Game logic needs to play sound
   │
   ▼
2. Fires PlaySoundEffectEvent
   │
   ▼
3. SoundEffectSystem receives event
   │
   ▼
4. Creates entity with SoundEffectRequestComponent
   │
   ▼
5. SoundEffectSystem.Update() processes request
   │
   ▼
6. AudioContentLoader loads sound effect
   │
   ▼
7. AudioEngine plays sound via MonoGame SoundEffect
   │
   ▼
8. Removes SoundEffectRequestComponent
   │
   ▼
9. Sound plays (fire-and-forget)
```

## Component Relationships

```
Map Entity
├── MapComponent (id: "route_101")
└── MusicComponent (audioId: "route_101_music", fadeDuration: 1.0)
    │
    └───► Audio Definition
          ├── id: "route_101_music"
          ├── assetPath: "Audio/Music/route_101.ogg"
          ├── audioType: "music"
          ├── volume: 0.8
          ├── fadeIn: 1.0
          ├── fadeOut: 1.5
          └── loopStart/loopEnd: (from OGG metadata)

NPC Entity
├── NpcComponent
└── AmbientSoundComponent (audioId: "npc_chatter", volume: 0.5)
    │
    └───► Audio Definition
          ├── id: "npc_chatter"
          ├── assetPath: "Audio/SFX/npc_chatter.ogg"
          ├── audioType: "ambient"
          └── volume: 0.5
```

## System Dependencies

```
SystemManager
├── AudioEngine (IAudioEngine)
│   └── ContentManager (MonoGame)
├── AudioContentLoader (IAudioContentLoader)
│   ├── ModManager (for mod paths)
│   └── DefinitionRegistry (for audio definitions)
├── MapMusicSystem
│   ├── World (ECS)
│   ├── EventBus (for MapTransitionEvent, GameEnteredEvent)
│   └── IAudioEngine (for music playback)
├── SoundEffectSystem
│   ├── World (ECS)
│   ├── IAudioEngine (for sound playback)
│   └── IAudioContentLoader (for loading sounds)
├── AmbientSoundSystem
│   ├── World (ECS)
│   ├── IAudioEngine (for ambient playback)
│   └── IAudioContentLoader (for loading sounds)
└── AudioVolumeSystem
    ├── World (ECS)
    ├── EventBus (for volume events)
    └── IAudioEngine (for applying volumes)
```

## Mod Definition Structure

```
Mods/
└── pokemon-emerald/
    ├── mod.json
    ├── Definitions/
    │   └── Audio/
    │       ├── route_101_music.json
    │       ├── pokemon_center_music.json
    │       └── menu_select_sfx.json
    └── Audio/
        ├── Music/
        │   ├── route_101.ogg
        │   └── pokemon_center.ogg
        └── SFX/
            └── menu_select.ogg
```

## Audio Definition JSON Example

Audio definitions already exist in `Mods/pokemon-emerald/Definitions/Audio/`. Example:

```json
{
  "id": "base:audio:music/towns/mus_littleroot",
  "name": "Littleroot",
  "audioPath": "Audio/Music/Towns/mus_littleroot.ogg",
  "volume": 0.787,
  "loop": true,
  "fadeIn": 0.5,
  "fadeOut": 0.5,
  "loopStartSamples": 36749,
  "loopLengthSamples": 2351997,
  "loopStartSec": 0.833,
  "loopEndSec": 54.167
}
```

**Note:** Audio definitions are automatically loaded by `ModLoader` into `DefinitionRegistry` with `DefinitionType = "audio"`.

## Map Definition with Music

```json
{
  "id": "map_littleroot_town",
  "type": "map",
  "music": {
    "audioId": "base:audio:music/towns/mus_littleroot",
    "fadeInOnTransition": true,
    "fadeDuration": 1.0
  }
}
```

This creates a `MusicComponent` on the map entity when loaded. The `audioId` must match an existing audio definition ID.

