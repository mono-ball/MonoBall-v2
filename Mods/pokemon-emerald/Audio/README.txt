POKESHARP AUDIO ASSETS
======================

Directory Structure:
-------------------

Audio/
├── Music/              - Background music tracks (OGG format recommended)
│   ├── Routes/         - Overworld route themes (route_1.ogg, route_101.ogg, etc.)
│   ├── Towns/          - Town and city themes (pallet_town.ogg, viridian_city.ogg, etc.)
│   ├── Battle/         - Battle music (wild_battle.ogg, trainer_battle.ogg, gym_leader.ogg, etc.)
│   └── Special/        - Special event music (intro.ogg, credits.ogg, pokemon_center.ogg, etc.)
│
├── SFX/                - Sound effects (WAV format recommended for instant playback)
│   ├── UI/             - User interface sounds
│   │   └── menu_select.wav, menu_back.wav, menu_move.wav, error_buzzer.wav, text_blip.wav
│   ├── NPC/            - NPC interaction sounds
│   │   └── exclamation.wav, question.wav, dialogue_start.wav
│   ├── Environment/    - Environmental sounds
│   │   └── door_open.wav, door_close.wav, grass_step.wav, water_splash.wav, ledge_jump.wav
│   ├── Footsteps/      - Footstep variations
│   │   └── step_grass.wav, step_sand.wav, step_wood.wav, step_tile.wav, step_snow.wav
│   ├── Items/          - Item interaction sounds
│   │   └── item_pickup.wav, item_use.wav, pokeball_throw.wav, pokeball_shake.wav, pokeball_catch.wav
│   └── Battle/         - Battle sound effects
│       └── hit_normal.wav, hit_super.wav, hit_weak.wav, stat_up.wav, stat_down.wav
│
└── Cries/              - Pokemon cries (WAV format, named by National Dex number)
    └── 001.wav (Bulbasaur), 025.wav (Pikachu), etc.

Audio Format Guidelines:
-----------------------

MUSIC (Background tracks):
- Format: OGG Vorbis (preferred) or MP3
- Sample Rate: 44100 Hz
- Bit Depth: 16-bit
- Channels: Stereo
- Note: OGG is patent-free and has good compression

SOUND EFFECTS (Short sounds):
- Format: WAV PCM (preferred for instant loading)
- Sample Rate: 44100 Hz or 22050 Hz
- Bit Depth: 16-bit
- Channels: Mono (required for 3D/positional audio) or Stereo

POKEMON CRIES:
- Format: WAV PCM
- Sample Rate: 11025 Hz (authentic) or 22050 Hz (higher quality)
- Bit Depth: 8-bit (authentic) or 16-bit (higher quality)
- Channels: Mono
- Duration: ~1-2 seconds typical

Naming Convention:
-----------------
- Use lowercase with underscores: route_1.ogg, menu_select.wav
- Pokemon cries: Use National Dex number (001.wav, 025.wav, etc.)
- Battle music: wild_battle.ogg, trainer_battle.ogg, champion.ogg
- Route music: route_1.ogg, route_101.ogg (match map MusicId)

Usage in Code:
-------------
// Play a sound effect
_audioService.PlaySound("SFX/UI/menu_select");

// Play music (with crossfade)
_audioService.PlayMusic("Music/Routes/route_1", loop: true, fadeDuration: 1.0f);

// Play Pokemon cry
_eventBus.Publish(new PlayPokemonCryEvent { SpeciesId = 25 }); // Pikachu

Map Music Configuration:
-----------------------
In your map definition (JSON/Tiled), set the MusicId property:
{
    "name": "Route 1",
    "musicId": "Music/Routes/route_1"
}

The MapMusicSystem will automatically play this music when the map loads.
