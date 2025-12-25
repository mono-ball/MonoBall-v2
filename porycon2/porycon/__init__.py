"""
Porycon - Pokemon Emerald to Tiled Converter

Converts pokeemerald decompilation maps to Tiled JSON format,
replacing metatiles with individual tile layers.

Also extracts and converts audio from MIDI to OGG format.
"""

__version__ = "0.2.0"

from .audio_converter import (
    AudioConverter,
    MidiConfigParser,
    MidiToOggConverter,
    AudioDefinitionGenerator,
    extract_audio,
)


