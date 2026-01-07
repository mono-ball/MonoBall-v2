# Audio System Quick Reference

## Quick Start

### Playing Music

```csharp
// Fire event to play music (using existing audio definition ID)
var playMusicEvent = new PlayMusicEvent
{
    AudioId = "base:audio:music/towns/mus_littleroot",
    Loop = true,
    FadeInDuration = 1.0f,
    Crossfade = false
};
EventBus.Send(ref playMusicEvent);
```

### Playing Sound Effects

```csharp
// Fire event to play sound effect (using existing audio definition ID)
var playSoundEvent = new PlaySoundEffectEvent
{
    AudioId = "base:audio:sfx/ui/se_select",
    Volume = 1.0f,
    Pitch = 0.0f,
    Pan = 0.0f
};
EventBus.Send(ref playSoundEvent);
```

### Setting Volume

```csharp
// Master volume
var masterVolumeEvent = new SetMasterVolumeEvent { Volume = 0.8f };
EventBus.Send(ref masterVolumeEvent);

// Music volume
var musicVolumeEvent = new SetMusicVolumeEvent { Volume = 0.7f };
EventBus.Send(ref musicVolumeEvent);

// Sound effect volume
var sfxVolumeEvent = new SetSoundEffectVolumeEvent { Volume = 0.9f };
EventBus.Send(ref sfxVolumeEvent);
```

## Map Music

### Adding Music to a Map

**Option 1: In Map Definition JSON**
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

**Option 2: Programmatically**
```csharp
// In MapLoaderSystem after creating map entity
World.Add(mapEntity, new MusicComponent
{
    AudioId = "base:audio:music/towns/mus_littleroot",
    FadeInOnTransition = true,
    FadeDuration = 1.0f
});
```

**Note:** The `audioId` must match an existing audio definition ID from `DefinitionRegistry`.

### How Map Music Works

1. `MapTransitionDetectionSystem` detects map transitions
2. Fires `MapTransitionEvent` with `TargetMapId`
3. `MapMusicSystem` receives event
4. Queries World for map entity with `MusicComponent`
5. Fires `PlayMusicEvent` with appropriate fade settings
6. `AudioEngine` plays music

## Audio Definitions

### Creating an Audio Definition

Audio definitions already exist in `Mods/[YourMod]/Definitions/Audio/`. Create a JSON file following the existing format:

**Example: Music Track**
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

**Example: Sound Effect**
```json
{
  "id": "base:audio:sfx/ui/se_select",
  "name": "Select",
  "audioPath": "Audio/SFX/UI/se_select.ogg",
  "volume": 0.63,
  "loop": false,
  "fadeIn": 0.0,
  "fadeOut": 0.0
}
```

### Audio Definition Fields

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `id` | string | Yes | Unified ID format: `base:audio:category/subcategory/track_id` |
| `name` | string | Yes | Human-readable display name |
| `audioPath` | string | Yes | Path to audio file relative to mod directory |
| `volume` | float | No | Default volume (0-1), default: 1.0 |
| `loop` | bool | No | Whether track should loop, default: true for music |
| `fadeIn` | float | No | Fade-in duration in seconds, default: 0 |
| `fadeOut` | float | No | Fade-out duration in seconds, default: 0 |
| `loopStartSamples` | int | No | Loop start position in samples (44100 Hz), optional |
| `loopLengthSamples` | int | No | Loop length in samples, optional |
| `loopStartSec` | float | No | Loop start position in seconds, optional |
| `loopEndSec` | float | No | Loop end position in seconds, optional |

**Note:** Audio definitions are automatically loaded by `ModLoader` into `DefinitionRegistry`. The `type` field is inferred from the ID format (`base:audio:*`).

## Components

### MusicComponent

Attached to map entities to specify background music.

```csharp
public struct MusicComponent
{
    public string AudioId { get; set; }
    public bool FadeInOnTransition { get; set; }
    public float FadeDuration { get; set; }
}
```

### SoundEffectRequestComponent

Temporary component for requesting sound effect playback. System removes it after processing.

```csharp
public struct SoundEffectRequestComponent
{
    public string AudioId { get; set; }
    public float Volume { get; set; }  // -1 = use definition default
    public float Pitch { get; set; }
    public float Pan { get; set; }
}
```

### AmbientSoundComponent

Attached to entities that emit ambient/looping sounds.

```csharp
public struct AmbientSoundComponent
{
    public string AudioId { get; set; }
    public SoundEffectInstance? Instance { get; set; }  // Managed by system
    public float Volume { get; set; }  // -1 = use definition default
    public bool IsPlaying { get; set; }
}
```

## Events

### PlayMusicEvent

Request to play background music.

```csharp
public struct PlayMusicEvent
{
    public string AudioId { get; set; }
    public bool Loop { get; set; }
    public float FadeInDuration { get; set; }
    public bool Crossfade { get; set; }
    public float CrossfadeDuration { get; set; }
}
```

### StopMusicEvent

Request to stop background music.

```csharp
public struct StopMusicEvent
{
    public float FadeOutDuration { get; set; }
}
```

### PlaySoundEffectEvent

Request to play a sound effect.

```csharp
public struct PlaySoundEffectEvent
{
    public string AudioId { get; set; }
    public float Volume { get; set; }  // -1 = use definition default
    public float Pitch { get; set; }
    public float Pan { get; set; }
}
```

### Volume Events

```csharp
public struct SetMasterVolumeEvent { public float Volume { get; set; } }
public struct SetMusicVolumeEvent { public float Volume { get; set; } }
public struct SetSoundEffectVolumeEvent { public float Volume { get; set; } }
```

## Systems

### MapMusicSystem

- **Priority**: `SystemPriority.Audio` (e.g., 600)
- **Subscribes**: `MapTransitionEvent`, `GameEnteredEvent`
- **Responsibility**: Manages map background music transitions

### SoundEffectSystem

- **Priority**: `SystemPriority.Audio + 10` (e.g., 610)
- **Queries**: Entities with `SoundEffectRequestComponent`
- **Responsibility**: Processes sound effect requests and plays them

### AmbientSoundSystem

- **Priority**: `SystemPriority.Audio + 20` (e.g., 620)
- **Queries**: Entities with `AmbientSoundComponent`
- **Responsibility**: Manages looping ambient sounds

### AudioVolumeSystem

- **Priority**: `SystemPriority.Audio + 30` (e.g., 630)
- **Subscribes**: Volume events
- **Responsibility**: Applies volume settings to audio engine

## File Formats

### Recommended Formats

- **Music**: OGG Vorbis (`.ogg`)
  - Patent-free
  - Good compression
  - Supports metadata (loop points)
  - Cross-platform

- **Sound Effects**: WAV (`.wav`) or OGG (`.ogg`)
  - WAV: Fast loading, no decompression
  - OGG: Smaller file size, requires decompression

### Platform Support

| Format | Windows | Mac | Linux |
|--------|---------|-----|-------|
| OGG Vorbis | ✅ | ✅ | ✅ |
| WAV | ✅ | ✅ | ✅ |
| MP3 | ✅ | ✅ | ⚠️ Limited |

## OGG Vorbis Loop Points

OGG files can contain loop point metadata in Vorbis comments:

```
LOOPSTART=12345
LOOPEND=67890
```

The audio system will:
1. Parse metadata during content loading
2. Extract loop points
3. Use `SoundEffectInstance` instead of `Song` for seamless looping

## Common Patterns

### Playing UI Sound on Button Click

```csharp
var playSoundEvent = new PlaySoundEffectEvent
{
    AudioId = "base:audio:sfx/ui/se_select",
    Volume = 1.0f
};
EventBus.Send(ref playSoundEvent);
```

### Changing Music on Scene Transition

```csharp
var playMusicEvent = new PlayMusicEvent
{
    AudioId = "base:audio:music/battle/mus_vs_wild",
    Loop = true,
    FadeInDuration = 1.0f,
    Crossfade = true,
    CrossfadeDuration = 1.5f
};
EventBus.Send(ref playMusicEvent);
```

### Adding Ambient Sound to Entity

```csharp
World.Add(entity, new AmbientSoundComponent
{
    AudioId = "base:audio:sfx/environment/se_rain",
    Volume = 0.6f
});
```

### Stopping Music with Fade

```csharp
var stopMusicEvent = new StopMusicEvent
{
    FadeOutDuration = 2.0f
};
EventBus.Send(ref stopMusicEvent);
```

## Troubleshooting

### Music Not Playing

1. Check audio definition exists in mod
2. Check audio file exists at `assetPath`
3. Check map has `MusicComponent` with correct `AudioId`
4. Check `MapMusicSystem` is registered in `SystemManager`
5. Check volume settings (not muted, volume > 0)

### Sound Effects Not Playing

1. Check audio definition exists
2. Check audio file exists
3. Check `SoundEffectSystem` is registered
4. Check volume settings
5. Check MonoGame audio device is initialized

### Loop Points Not Working

1. Ensure OGG file has loop point metadata
2. Check `loopStart` and `loopEnd` in audio definition
3. Verify audio system is using `SoundEffectInstance` (not `Song`)

### Cross-Platform Issues

1. Use OGG Vorbis for music (best cross-platform support)
2. Use WAV for sound effects (fastest loading)
3. Avoid MP3 on Linux
4. Test on all target platforms

## System Priorities

Add to `SystemPriority.cs`:

```csharp
// Audio systems
public const int Audio = 600;
```

Then use in systems:

```csharp
public int Priority => SystemPriority.Audio;
```

