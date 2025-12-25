"""
Audio Converter - converts pokeemerald MIDI files to OGG format.

Parses midi.cfg to extract track definitions, converts MIDI files to OGG,
and generates audio definitions for PokeSharp.

Handles GBA-style loop markers:
- `[` MIDI marker = loop start point
- `]` MIDI marker = loop end point
- Audio is trimmed at loop end, metadata embedded for seamless looping
"""

import os
import re
import json
import struct
import subprocess
import shutil
from pathlib import Path
from typing import Dict, List, Any, Optional, Tuple, Set
from dataclasses import dataclass, field
from enum import Enum
from concurrent.futures import ThreadPoolExecutor, as_completed
from .logging_config import get_logger

logger = get_logger('audio_converter')


class AudioCategory(Enum):
    """Categories for audio tracks."""
    MUSIC_ROUTE = "Music/Routes"
    MUSIC_TOWN = "Music/Towns"
    MUSIC_BATTLE = "Music/Battle"
    MUSIC_SPECIAL = "Music/Special"
    SFX_UI = "SFX/UI"
    SFX_BATTLE = "SFX/Battle"
    SFX_ENVIRONMENT = "SFX/Environment"
    SFX_POKEMON = "SFX/Pokemon"
    FANFARE = "Music/Fanfares"
    PHONEME = "SFX/Phonemes"


@dataclass
class MidiLoopInfo:
    """Loop information extracted from MIDI markers.

    GBA music uses `[` and `]` MIDI marker events to define loop regions.
    The music plays from start, and when reaching `]`, loops back to `[`.
    """
    loop_start_tick: Optional[int] = None  # Tick position of `[` marker
    loop_end_tick: Optional[int] = None    # Tick position of `]` marker
    loop_start_sec: Optional[float] = None # Time in seconds
    loop_end_sec: Optional[float] = None   # Time in seconds
    division: int = 24                     # Ticks per beat
    tempo_bpm: float = 120.0               # BPM from tempo event

    @property
    def has_loop(self) -> bool:
        """True if track has loop markers."""
        return self.loop_start_tick is not None and self.loop_end_tick is not None

    @property
    def loop_duration_sec(self) -> Optional[float]:
        """Duration of the loop section in seconds."""
        if self.loop_start_sec is not None and self.loop_end_sec is not None:
            return self.loop_end_sec - self.loop_start_sec
        return None

    def get_loop_start_samples(self, sample_rate: int = 44100) -> Optional[int]:
        """Get loop start position in samples."""
        if self.loop_start_sec is not None:
            return int(self.loop_start_sec * sample_rate)
        return None

    def get_loop_length_samples(self, sample_rate: int = 44100) -> Optional[int]:
        """Get loop length in samples (from start to end)."""
        if self.loop_start_sec is not None and self.loop_end_sec is not None:
            return int((self.loop_end_sec - self.loop_start_sec) * sample_rate)
        return None


class MidiLoopParser:
    """Parser for extracting loop markers from MIDI files.

    GBA Pokemon games use `[` and `]` MIDI marker events to define loop regions.
    This parser extracts these markers and calculates their time positions.
    """

    @staticmethod
    def parse(midi_path: Path) -> MidiLoopInfo:
        """
        Parse MIDI file and extract loop marker information.

        Args:
            midi_path: Path to MIDI file

        Returns:
            MidiLoopInfo with loop marker positions (if found)
        """
        info = MidiLoopInfo()

        try:
            with open(midi_path, 'rb') as f:
                data = f.read()
        except Exception as e:
            logger.warning(f"Failed to read MIDI file {midi_path}: {e}")
            return info

        # Validate MIDI header
        if data[:4] != b'MThd':
            logger.warning(f"Invalid MIDI file: {midi_path}")
            return info

        try:
            # Parse header
            header_len = struct.unpack('>I', data[4:8])[0]
            fmt, num_tracks, division = struct.unpack('>HHH', data[8:14])
            info.division = division

            pos = 14
            tempo_us = 500000  # Default: 120 BPM

            # Parse all tracks looking for markers and tempo
            for track_num in range(num_tracks):
                if pos >= len(data) or data[pos:pos+4] != b'MTrk':
                    break

                track_len = struct.unpack('>I', data[pos+4:pos+8])[0]
                track_data = data[pos+8:pos+8+track_len]

                track_pos = 0
                tick = 0

                while track_pos < len(track_data):
                    # Read variable length delta time
                    delta = 0
                    while track_pos < len(track_data):
                        b = track_data[track_pos]
                        track_pos += 1
                        delta = (delta << 7) | (b & 0x7F)
                        if not (b & 0x80):
                            break
                    tick += delta

                    if track_pos >= len(track_data):
                        break

                    # Read event
                    status = track_data[track_pos]

                    if status == 0xFF:  # Meta event
                        track_pos += 1
                        if track_pos >= len(track_data):
                            break
                        event_type = track_data[track_pos]
                        track_pos += 1

                        # Read variable length
                        length = 0
                        while track_pos < len(track_data):
                            b = track_data[track_pos]
                            track_pos += 1
                            length = (length << 7) | (b & 0x7F)
                            if not (b & 0x80):
                                break

                        if track_pos + length > len(track_data):
                            break

                        event_data = track_data[track_pos:track_pos+length]
                        track_pos += length

                        # Marker event (type 0x06)
                        if event_type == 0x06 and event_data:
                            try:
                                text = event_data.decode('ascii', errors='ignore').strip()
                                if text == '[':
                                    info.loop_start_tick = tick
                                elif text == ']':
                                    info.loop_end_tick = tick
                            except:
                                pass

                        # Tempo event (type 0x51)
                        elif event_type == 0x51 and len(event_data) >= 3:
                            tempo_us = (event_data[0] << 16) | (event_data[1] << 8) | event_data[2]

                        # End of track
                        elif event_type == 0x2F:
                            break
                    else:
                        # Regular MIDI event - skip based on status
                        if status >= 0xF0:
                            track_pos += 1
                        elif (status & 0xF0) in [0xC0, 0xD0]:
                            track_pos += 2
                        elif (status & 0xF0) in [0x80, 0x90, 0xA0, 0xB0, 0xE0]:
                            track_pos += 3
                        else:
                            track_pos += 1

                pos += 8 + track_len

            # Calculate BPM and time positions
            info.tempo_bpm = 60000000 / tempo_us

            def tick_to_sec(t: Optional[int]) -> Optional[float]:
                if t is None:
                    return None
                beats = t / info.division
                time_us = beats * tempo_us
                return time_us / 1000000

            info.loop_start_sec = tick_to_sec(info.loop_start_tick)
            info.loop_end_sec = tick_to_sec(info.loop_end_tick)

            if info.has_loop:
                logger.debug(f"{midi_path.name}: loop [{info.loop_start_sec:.2f}s - {info.loop_end_sec:.2f}s]")

        except Exception as e:
            logger.warning(f"Error parsing MIDI {midi_path}: {e}")

        return info


@dataclass
class MidiTrackInfo:
    """Information about a MIDI track from midi.cfg."""
    filename: str
    track_id: str  # e.g., "mus_littleroot", "se_door"
    voicegroup: Optional[str] = None  # e.g., "_littleroot", "_fanfare"
    volume: int = 80  # 0-127, default from pokeemerald
    reverb: int = 50  # Default R50
    priority: int = 0  # Priority flag from -P
    is_sound_effect: bool = False  # True if starts with "se_"
    is_phoneme: bool = False  # True if starts with "ph_"
    is_music: bool = True  # True if starts with "mus_"
    category: AudioCategory = AudioCategory.MUSIC_SPECIAL

    def __post_init__(self):
        """Determine category based on track name."""
        name_lower = self.track_id.lower()

        if name_lower.startswith("se_"):
            self.is_sound_effect = True
            self.is_music = False
            self._categorize_sfx(name_lower)
        elif name_lower.startswith("ph_"):
            self.is_phoneme = True
            self.is_music = False
            self.category = AudioCategory.PHONEME
        else:
            self.is_music = True
            self._categorize_music(name_lower)

    def _categorize_sfx(self, name: str):
        """Categorize sound effects."""
        if any(x in name for x in ["select", "click", "door", "save", "card", "pc_", "pokenav", "shop", "menu"]):
            self.category = AudioCategory.SFX_UI
        elif any(x in name for x in ["vs_", "effective", "faint", "flee", "ball", "exp", "low_health", "m_"]):
            self.category = AudioCategory.SFX_BATTLE
        elif any(x in name for x in ["rain", "thunder", "downpour", "truck", "elevator", "ship", "bridge"]):
            self.category = AudioCategory.SFX_ENVIRONMENT
        else:
            self.category = AudioCategory.SFX_BATTLE

    def _categorize_music(self, name: str):
        """Categorize music tracks.

        IMPORTANT: Keep in sync with id_transformer.py MUSIC_CATEGORIES
        """
        if any(x in name for x in ["route", "cycling", "surf", "sailing"]):
            self.category = AudioCategory.MUSIC_ROUTE
        elif any(x in name for x in ["town", "city", "village", "littleroot", "oldale", "petalburg",
                                     "rustboro", "dewford", "slateport", "mauville", "verdanturf",
                                     "fallarbor", "lavaridge", "fortree", "lilycove", "mossdeep",
                                     "sootopolis", "pacifidlog", "ever_grande", "pallet", "viridian",
                                     "pewter", "cerulean", "vermillion", "lavender", "celadon",
                                     "fuchsia", "saffron", "cinnabar"]):
            self.category = AudioCategory.MUSIC_TOWN
        elif any(x in name for x in ["vs_", "battle", "encounter", "victory", "intro_battle", "intro_fight"]):
            self.category = AudioCategory.MUSIC_BATTLE
        elif any(x in name for x in ["heal", "obtain", "caught", "evolved", "level_up", "slots_",
                                     "fanfare", "too_bad", "register", "move_deleted"]):
            self.category = AudioCategory.FANFARE
        else:
            # Special category: caves, dungeons, facilities, etc.
            # Includes: museum, pokemon_center, poke_mart, gym, game_corner, safari, contest, trick_house
            self.category = AudioCategory.MUSIC_SPECIAL


class MidiConfigParser:
    """Parser for pokeemerald midi.cfg files."""

    # Regex pattern for midi.cfg lines
    # Format: filename.mid: -E -R50 -G_voicegroup -V080 -P5
    LINE_PATTERN = re.compile(
        r'^([a-zA-Z0-9_]+\.mid):\s*(.*)$'
    )

    # Parameter patterns
    PARAM_PATTERNS = {
        'voicegroup': re.compile(r'-G(_?\w+)'),
        'volume': re.compile(r'-V(\d+)'),
        'reverb': re.compile(r'-R(\d+)'),
        'priority': re.compile(r'-P(\d+)'),
    }

    def __init__(self, pokeemerald_dir: str):
        self.pokeemerald_dir = Path(pokeemerald_dir)
        self.midi_dir = self.pokeemerald_dir / "sound" / "songs" / "midi"
        self.tracks: Dict[str, MidiTrackInfo] = {}

    def parse(self) -> Dict[str, MidiTrackInfo]:
        """
        Parse midi.cfg and return track information.

        Returns:
            Dict mapping track_id to MidiTrackInfo
        """
        cfg_path = self.midi_dir / "midi.cfg"

        if not cfg_path.exists():
            logger.error(f"midi.cfg not found at {cfg_path}")
            return {}

        logger.info(f"Parsing midi.cfg from {cfg_path}")

        with open(cfg_path, 'r', encoding='utf-8') as f:
            lines = f.readlines()

        for line_num, line in enumerate(lines, 1):
            line = line.strip()
            if not line or line.startswith('#'):
                continue

            match = self.LINE_PATTERN.match(line)
            if not match:
                logger.debug(f"Skipping non-matching line {line_num}: {line[:50]}...")
                continue

            filename = match.group(1)
            params_str = match.group(2)

            # Extract track ID from filename
            track_id = filename.replace('.mid', '')

            # Parse parameters
            voicegroup = None
            volume = 80
            reverb = 50
            priority = 0

            vg_match = self.PARAM_PATTERNS['voicegroup'].search(params_str)
            if vg_match:
                voicegroup = vg_match.group(1)

            vol_match = self.PARAM_PATTERNS['volume'].search(params_str)
            if vol_match:
                volume = int(vol_match.group(1))

            rev_match = self.PARAM_PATTERNS['reverb'].search(params_str)
            if rev_match:
                reverb = int(rev_match.group(1))

            pri_match = self.PARAM_PATTERNS['priority'].search(params_str)
            if pri_match:
                priority = int(pri_match.group(1))

            track_info = MidiTrackInfo(
                filename=filename,
                track_id=track_id,
                voicegroup=voicegroup,
                volume=volume,
                reverb=reverb,
                priority=priority
            )

            self.tracks[track_id] = track_info

        logger.info(f"Parsed {len(self.tracks)} tracks from midi.cfg")

        # Log category breakdown
        categories = {}
        for track in self.tracks.values():
            cat = track.category.value
            categories[cat] = categories.get(cat, 0) + 1

        for cat, count in sorted(categories.items()):
            logger.info(f"  {cat}: {count} tracks")

        return self.tracks

    def get_midi_files(self) -> List[Path]:
        """Get all MIDI files in the midi directory."""
        return list(self.midi_dir.glob("*.mid"))

    def get_music_tracks(self) -> Dict[str, MidiTrackInfo]:
        """Get only music tracks (mus_*)."""
        return {k: v for k, v in self.tracks.items() if v.is_music}

    def get_sfx_tracks(self) -> Dict[str, MidiTrackInfo]:
        """Get only sound effect tracks (se_*)."""
        return {k: v for k, v in self.tracks.items() if v.is_sound_effect}

    def get_phoneme_tracks(self) -> Dict[str, MidiTrackInfo]:
        """Get only phoneme tracks (ph_*)."""
        return {k: v for k, v in self.tracks.items() if v.is_phoneme}


class MidiToOggConverter:
    """Converts MIDI files to OGG format using available tools."""

    # Supported converters in order of preference
    CONVERTERS = ['timidity', 'fluidsynth', 'ffmpeg']

    def __init__(self, soundfont_path: Optional[str] = None):
        """
        Initialize converter.

        Args:
            soundfont_path: Path to soundfont file for better GBA sound reproduction.
                           If None, will try to find a suitable default.
        """
        self.soundfont_path = soundfont_path
        self.converter = self._find_converter()

        if not self.converter:
            logger.warning(
                "No MIDI converter found. Install one of: timidity, fluidsynth, or ffmpeg. "
                "MIDI files will be copied as-is and need manual conversion."
            )

    def _find_converter(self) -> Optional[str]:
        """Find an available MIDI converter."""
        for converter in self.CONVERTERS:
            if shutil.which(converter):
                logger.info(f"Found MIDI converter: {converter}")
                return converter
        return None

    def _find_default_soundfont(self) -> Optional[str]:
        """Try to find a default soundfont."""
        common_paths = [
            "/usr/share/sounds/sf2/FluidR3_GM.sf2",
            "/usr/share/soundfonts/FluidR3_GM.sf2",
            "/usr/share/sounds/sf2/TimGM6mb.sf2",
            "C:/soundfonts/FluidR3_GM.sf2",
            "/opt/soundfonts/FluidR3_GM.sf2",
        ]

        for path in common_paths:
            if os.path.exists(path):
                return path

        return None

    def convert(self, midi_path: Path, output_path: Path, track_info: Optional[MidiTrackInfo] = None) -> Tuple[bool, Optional[MidiLoopInfo]]:
        """
        Convert a MIDI file to OGG format with loop marker support.

        Args:
            midi_path: Path to input MIDI file
            output_path: Path for output OGG file
            track_info: Optional track info for volume adjustment

        Returns:
            Tuple of (success, loop_info) - loop_info contains extracted loop markers
        """
        if not midi_path.exists():
            logger.error(f"MIDI file not found: {midi_path}")
            return False, None

        # Parse loop markers from MIDI
        loop_info = MidiLoopParser.parse(midi_path)

        # Ensure output directory exists (handle race condition in parallel execution)
        try:
            output_path.parent.mkdir(parents=True, exist_ok=True)
        except FileExistsError:
            pass  # Another thread created it

        if not self.converter:
            # No converter available - copy as-is with .mid extension
            # User will need to convert manually
            fallback_path = output_path.with_suffix('.mid')
            shutil.copy2(midi_path, fallback_path)
            logger.warning(f"No converter available. Copied MIDI to {fallback_path}")
            return False, loop_info

        try:
            success = False
            if self.converter == 'timidity':
                success = self._convert_with_timidity(midi_path, output_path, track_info)
            elif self.converter == 'fluidsynth':
                success = self._convert_with_fluidsynth(midi_path, output_path, track_info)
            elif self.converter == 'ffmpeg':
                success = self._convert_with_ffmpeg(midi_path, output_path, track_info)

            return success, loop_info
        except Exception as e:
            logger.error(f"Error converting {midi_path}: {e}")
            return False, loop_info

        return False, loop_info

    def _convert_with_timidity(self, midi_path: Path, output_path: Path,
                                track_info: Optional[MidiTrackInfo]) -> bool:
        """Convert using TiMidity++ with direct OGG Vorbis output for gapless looping."""
        # Volume scaling (timidity uses 0-800%, default 100%)
        volume_pct = 100
        if track_info:
            volume_pct = int((track_info.volume / 127.0) * 100)

        # Convert MIDI directly to OGG Vorbis (no WAV intermediate)
        # This avoids encoder padding issues that cause gaps in looped playback
        # GBA had limited stereo and MIDI pan data causes imbalanced output
        # Using mono gives more faithful reproduction of original GBA audio
        cmd = [
            'timidity',
            str(midi_path),
            '-Ov',  # Output OGG Vorbis directly
            '-o', str(output_path),
            f'--volume={volume_pct}',
            '-s', '44100',  # Sample rate
            '--output-mono',  # Mono output (GBA had minimal stereo separation)
            '-EFreverb=0',  # Disable reverb for cleaner output
        ]

        # Add soundfont if specified (timidity uses -x "soundfont path")
        if self.soundfont_path:
            cmd.extend(['-x', f'soundfont {self.soundfont_path}'])

        result = subprocess.run(cmd, capture_output=True, text=True)
        if result.returncode != 0:
            logger.error(f"TiMidity failed: {result.stderr}")
            return False

        return True

    def _convert_with_fluidsynth(self, midi_path: Path, output_path: Path,
                                  track_info: Optional[MidiTrackInfo]) -> bool:
        """Convert using FluidSynth (requires WAV intermediate, then oggenc)."""
        soundfont = self.soundfont_path or self._find_default_soundfont()

        if not soundfont:
            logger.error("FluidSynth requires a soundfont. Please specify --soundfont path")
            return False

        wav_path = output_path.with_suffix('.wav')

        # FluidSynth gain (0.0-10.0, default 0.2)
        gain = 0.2
        if track_info:
            gain = (track_info.volume / 127.0) * 0.5  # Scale to reasonable range

        cmd = [
            'fluidsynth',
            '-ni',  # Non-interactive
            '-r', '44100',  # Sample rate
            '-o', 'audio.sample-format=16bits',  # 16-bit audio
            '-o', 'synth.chorus.active=no',  # Disable chorus
            '-o', 'synth.reverb.active=no',  # Disable reverb
            soundfont,
            str(midi_path),
            '-F', str(wav_path),  # Output file
            '-g', str(gain)  # Gain
        ]

        result = subprocess.run(cmd, capture_output=True, text=True)
        if result.returncode != 0:
            logger.error(f"FluidSynth failed: {result.stderr}")
            return False

        # Convert WAV to OGG using oggenc (preferred) or ffmpeg
        # oggenc produces better loop-friendly output than ffmpeg
        if shutil.which('oggenc'):
            ogg_cmd = ['oggenc', '-q', '6', '--downmix', '-o', str(output_path), str(wav_path)]
            result = subprocess.run(ogg_cmd, capture_output=True, text=True)
        elif shutil.which('ffmpeg'):
            ogg_cmd = [
                'ffmpeg', '-y', '-i', str(wav_path),
                '-af', 'pan=mono|c0=0.5*c0+0.5*c1',  # Downmix to mono
                '-c:a', 'libvorbis',
                '-q:a', '6',
                '-ac', '1',  # Keep as mono
                str(output_path)
            ]
            result = subprocess.run(ogg_cmd, capture_output=True, text=True)
        else:
            logger.warning(f"No OGG encoder found. WAV file at {wav_path}")
            return True  # WAV is usable

        # Clean up WAV
        if result.returncode == 0 and wav_path.exists():
            wav_path.unlink()

        return result.returncode == 0

    def _convert_with_ffmpeg(self, midi_path: Path, output_path: Path,
                              track_info: Optional[MidiTrackInfo]) -> bool:
        """Convert using FFmpeg (limited MIDI support)."""
        # FFmpeg has limited MIDI support - may not work well for GBA MIDI
        cmd = [
            'ffmpeg',
            '-y',
            '-i', str(midi_path),
            '-c:a', 'libvorbis',
            '-q:a', '6',
            '-ac', '2',  # Force stereo (2 channels)
            '-ar', '44100',  # Sample rate
            str(output_path)
        ]

        result = subprocess.run(cmd, capture_output=True, text=True)
        if result.returncode != 0:
            logger.warning(f"FFmpeg MIDI conversion failed (expected - limited support): {result.stderr[:200]}")
            # Copy MIDI as fallback
            shutil.copy2(midi_path, output_path.with_suffix('.mid'))
            return False

        return True


class AudioDefinitionGenerator:
    """Generates audio definition files for PokeSharp."""

    def __init__(self, output_dir: str):
        self.output_dir = Path(output_dir)
        self.definitions_dir = self.output_dir / "Definitions" / "Audio"

    def generate(self, tracks: Dict[str, MidiTrackInfo], audio_dir: Path,
                 loop_info_cache: Optional[Dict[str, 'MidiLoopInfo']] = None) -> int:
        """
        Generate audio definitions from track info.
        Creates one JSON file per track, mirroring the Audio directory structure.

        Args:
            tracks: Dict of track_id -> MidiTrackInfo
            audio_dir: Path to where audio files will be placed
            loop_info_cache: Optional dict of track_id -> MidiLoopInfo for loop metadata

        Returns:
            Number of definitions generated
        """
        count = 0
        loop_info_cache = loop_info_cache or {}

        for track_id, track_info in tracks.items():
            # Create definition file path mirroring audio structure
            # Audio: Audio/Music/Towns/mus_dewford.ogg
            # Definition: Definitions/Audio/Music/Towns/mus_dewford.json
            category_path = track_info.category.value  # e.g., "Music/Towns"
            def_dir = self.definitions_dir / category_path

            try:
                def_dir.mkdir(parents=True, exist_ok=True)
            except FileExistsError:
                pass  # Handle race condition

            def_path = def_dir / f"{track_id}.json"

            # Create ID in format: base:audio:category/track_id
            # e.g., base:audio:music/towns/mus_dewford
            category_id = category_path.lower().replace('/', '/')
            asset_id = f"base:audio:{category_id}/{track_id}"

            # Create human-readable name
            display_name = self._create_display_name(track_id)

            # Get loop info if available
            loop_info = loop_info_cache.get(track_id)
            has_loop_markers = loop_info is not None and loop_info.has_loop

            # Determine if track should loop
            # Use MIDI loop markers as the source of truth, fall back to heuristics
            should_loop = has_loop_markers or (
                track_info.is_music and track_info.category not in [
                    AudioCategory.FANFARE,
                    AudioCategory.SFX_UI,
                    AudioCategory.SFX_BATTLE,
                    AudioCategory.SFX_ENVIRONMENT
                ]
            )

            # Audio file path relative to Assets
            audio_path = f"Audio/{category_path}/{track_id}.ogg"

            # Build definition matching PokeSharp format
            definition = {
                "id": asset_id,
                "name": display_name,
                "audioPath": audio_path,
                "volume": round(track_info.volume / 127.0, 3),
                "loop": should_loop,
                "fadeIn": 0.5 if track_info.is_music else 0.0,
                "fadeOut": 0.5 if track_info.is_music else 0.0,
            }

            # Add loop metadata if available (embedded in OGG, also stored in definition)
            if has_loop_markers:
                definition["loopStartSamples"] = loop_info.get_loop_start_samples(44100)
                definition["loopLengthSamples"] = loop_info.get_loop_length_samples(44100)
                definition["loopStartSec"] = round(loop_info.loop_start_sec, 3)
                definition["loopEndSec"] = round(loop_info.loop_end_sec, 3)

            # Write individual definition file
            with open(def_path, 'w', encoding='utf-8') as f:
                json.dump(definition, f, indent=2)

            count += 1

        logger.info(f"Generated {count} audio definition files")
        return count

    def _create_display_name(self, track_id: str) -> str:
        """Create a human-readable display name from track ID."""
        # Remove prefix
        name = track_id
        for prefix in ['mus_', 'se_', 'ph_']:
            if name.startswith(prefix):
                name = name[len(prefix):]
                break

        # Convert underscores to spaces and title case
        name = name.replace('_', ' ').title()

        # Special case handling
        replacements = {
            'Rg ': 'RG ',  # FireRed/LeafGreen
            'Vs ': 'VS ',
            'B ': 'Battle ',
            'Mt ': 'Mt. ',
            'Pc ': 'PC ',
            'Tm': 'TM',
            'Hm': 'HM',
        }

        for old, new in replacements.items():
            name = name.replace(old, new)

        return name


class AudioConverter:
    """Main class for audio conversion pipeline."""

    def __init__(self, pokeemerald_dir: str, output_dir: str, soundfont_path: Optional[str] = None):
        """
        Initialize the audio converter.

        Args:
            pokeemerald_dir: Path to pokeemerald decompilation
            output_dir: Path to output directory (PokeSharp Assets folder)
            soundfont_path: Optional path to soundfont for better audio quality
        """
        self.pokeemerald_dir = Path(pokeemerald_dir)
        self.output_dir = Path(output_dir)
        self.soundfont_path = soundfont_path

        # Audio output directories
        self.audio_dir = self.output_dir / "Audio"

        # Initialize components
        self.parser = MidiConfigParser(pokeemerald_dir)
        self.converter = MidiToOggConverter(soundfont_path)
        self.definition_generator = AudioDefinitionGenerator(output_dir)

        # Track conversion statistics
        self.stats = {
            'total': 0,
            'converted': 0,
            'failed': 0,
            'skipped': 0
        }

        # Cache loop info for generating audio definitions
        self.loop_info_cache: Dict[str, MidiLoopInfo] = {}

    def convert_all(self,
                    include_music: bool = True,
                    include_sfx: bool = True,
                    include_phonemes: bool = False,
                    parallel: bool = True,
                    max_workers: int = 4) -> Dict[str, int]:
        """
        Convert all audio from pokeemerald.

        Args:
            include_music: Include music tracks (mus_*)
            include_sfx: Include sound effects (se_*)
            include_phonemes: Include phoneme tracks (ph_*)
            parallel: Use parallel conversion
            max_workers: Maximum parallel workers

        Returns:
            Dict with conversion statistics
        """
        # Parse midi.cfg
        tracks = self.parser.parse()

        if not tracks:
            logger.error("No tracks found in midi.cfg")
            return self.stats

        # Filter tracks based on options
        filtered_tracks = {}
        for track_id, track_info in tracks.items():
            if track_info.is_music and include_music:
                filtered_tracks[track_id] = track_info
            elif track_info.is_sound_effect and include_sfx:
                filtered_tracks[track_id] = track_info
            elif track_info.is_phoneme and include_phonemes:
                filtered_tracks[track_id] = track_info

        self.stats['total'] = len(filtered_tracks)
        logger.info(f"Converting {self.stats['total']} audio tracks...")

        # Create output directories
        self._create_output_directories()

        # Convert tracks
        if parallel and max_workers > 1:
            self._convert_parallel(filtered_tracks, max_workers)
        else:
            self._convert_sequential(filtered_tracks)

        # Generate definitions with loop info
        logger.info("Generating audio definitions...")
        num_definitions = self.definition_generator.generate(
            filtered_tracks, self.audio_dir, self.loop_info_cache
        )
        logger.info(f"Generated {num_definitions} audio definitions")
        logger.info(f"Tracks with loop markers: {len(self.loop_info_cache)}")

        # Summary
        logger.info(f"Conversion complete:")
        logger.info(f"  Total: {self.stats['total']}")
        logger.info(f"  Converted: {self.stats['converted']}")
        logger.info(f"  Failed: {self.stats['failed']}")
        logger.info(f"  Skipped: {self.stats['skipped']}")

        return self.stats

    def _create_output_directories(self):
        """Create output directory structure."""
        # Create category directories (handle race conditions)
        for category in AudioCategory:
            cat_dir = self.audio_dir / category.value
            try:
                cat_dir.mkdir(parents=True, exist_ok=True)
            except FileExistsError:
                pass  # Already exists

    def _convert_sequential(self, tracks: Dict[str, MidiTrackInfo]):
        """Convert tracks sequentially."""
        midi_dir = self.parser.midi_dir

        for track_id, track_info in tracks.items():
            midi_path = midi_dir / track_info.filename
            output_path = self.audio_dir / track_info.category.value / f"{track_id}.ogg"

            if not midi_path.exists():
                logger.warning(f"MIDI file not found: {midi_path}")
                self.stats['skipped'] += 1
                continue

            success, loop_info = self.converter.convert(midi_path, output_path, track_info)
            # Store loop info for audio definitions
            if loop_info and loop_info.has_loop:
                self.loop_info_cache[track_id] = loop_info

            if success:
                self.stats['converted'] += 1
            else:
                self.stats['failed'] += 1

    def _convert_parallel(self, tracks: Dict[str, MidiTrackInfo], max_workers: int):
        """Convert tracks in parallel."""
        midi_dir = self.parser.midi_dir

        def convert_single(args):
            track_id, track_info = args
            midi_path = midi_dir / track_info.filename
            output_path = self.audio_dir / track_info.category.value / f"{track_id}.ogg"

            if not midi_path.exists():
                return ('skipped', track_id, None)

            success, loop_info = self.converter.convert(midi_path, output_path, track_info)
            return ('converted' if success else 'failed', track_id, loop_info)

        with ThreadPoolExecutor(max_workers=max_workers) as executor:
            futures = {executor.submit(convert_single, item): item[0]
                      for item in tracks.items()}

            for future in as_completed(futures):
                status, track_id, loop_info = future.result()
                self.stats[status] += 1
                # Store loop info for audio definitions
                if loop_info and loop_info.has_loop:
                    self.loop_info_cache[track_id] = loop_info

                if self.stats['converted'] % 50 == 0:
                    logger.info(f"Progress: {self.stats['converted']}/{self.stats['total']} converted")

    def list_tracks(self) -> List[Dict[str, Any]]:
        """
        List all tracks from midi.cfg without converting.

        Returns:
            List of track information dicts
        """
        tracks = self.parser.parse()

        result = []
        for track_id, track_info in tracks.items():
            result.append({
                'id': track_id,
                'filename': track_info.filename,
                'category': track_info.category.value,
                'volume': track_info.volume,
                'is_music': track_info.is_music,
                'is_sfx': track_info.is_sound_effect,
                'voicegroup': track_info.voicegroup
            })

        return result


def extract_audio(input_dir: str, output_dir: str,
                  include_music: bool = True,
                  include_sfx: bool = True,
                  include_phonemes: bool = False,
                  soundfont: Optional[str] = None,
                  parallel: bool = True) -> Dict[str, int]:
    """
    Extract and convert audio from pokeemerald.

    This is the main entry point for audio extraction.

    Args:
        input_dir: Path to pokeemerald root directory
        output_dir: Path to output directory (PokeSharp Assets folder)
        include_music: Include music tracks
        include_sfx: Include sound effects
        include_phonemes: Include phoneme tracks
        soundfont: Path to soundfont file for conversion
        parallel: Use parallel conversion

    Returns:
        Dict with conversion statistics
    """
    converter = AudioConverter(input_dir, output_dir, soundfont)
    return converter.convert_all(
        include_music=include_music,
        include_sfx=include_sfx,
        include_phonemes=include_phonemes,
        parallel=parallel
    )
