"""
Parser for Pokemon Emerald animation metadata from source code.
"""

import re
from pathlib import Path
from typing import Dict, List, Optional, Tuple
from dataclasses import dataclass, field
from .logging_config import get_logger

logger = get_logger('animation_parser')


@dataclass
class AnimFrame:
    """Single frame in an animation sequence."""
    frame_index: int
    duration: int
    flip_horizontal: bool = False


@dataclass
class AnimationDefinition:
    """Animation definition with name and frames."""
    name: str
    frames: List[AnimFrame] = field(default_factory=list)


@dataclass
class SpriteAnimationMetadata:
    """Metadata for a sprite's animations."""
    sprite_name: str
    animation_table: str
    logical_frame_count: int
    physical_frame_mapping: Optional[List[int]] = None
    animation_definitions: List[AnimationDefinition] = field(default_factory=list)


@dataclass
class SpriteSourceInfo:
    """Information about a sprite source file."""
    pic_name: str
    file_path: str
    start_frame: int
    frame_count: int


class PokeemeraldAnimationParser:
    """Parses animation metadata from pokeemerald source code."""
    
    def __init__(self, pokeemerald_path: str):
        """
        Initialize parser.
        
        Args:
            pokeemerald_path: Path to pokeemerald root directory
        """
        self.pokeemerald_path = Path(pokeemerald_path)
        self.filename_mapping: Dict[str, str] = {}
    
    def get_filename_mapping(self) -> Dict[str, str]:
        """Get the filename to sprite name mapping."""
        return self.filename_mapping
    
    def parse_animation_data(self) -> Dict[str, SpriteAnimationMetadata]:
        """
        Parse animation data from pokeemerald source.
        
        Returns:
            Dictionary mapping sprite name to animation metadata
        """
        logger.info("Parsing animation data from pokeemerald source...")
        
        metadata: Dict[str, SpriteAnimationMetadata] = {}
        
        # Paths to source files
        graphics_info_path = self.pokeemerald_path / "src" / "data" / "object_events" / "object_event_graphics_info.h"
        graphics_path = self.pokeemerald_path / "src" / "data" / "object_events" / "object_event_graphics.h"
        pic_tables_path = self.pokeemerald_path / "src" / "data" / "object_events" / "object_event_pic_tables.h"
        anims_path = self.pokeemerald_path / "src" / "data" / "object_events" / "object_event_anims.h"
        
        if not all(p.exists() for p in [graphics_info_path, pic_tables_path, anims_path]):
            logger.warning("Could not find pokeemerald source files. Using default animations.")
            return metadata
        
        # Parse filename -> sprite name mapping
        filename_mapping = self._parse_filename_mapping(graphics_path)
        
        # Parse animation tables
        animation_tables = self._parse_animation_tables(anims_path)
        
        # Parse sprite graphics info
        graphics_info = self._parse_graphics_info(graphics_info_path)
        
        # Parse frame counts and mappings from pic tables
        frame_counts = self._parse_frame_counts(pic_tables_path)
        frame_mappings = self._parse_frame_mappings(pic_tables_path)
        
        # Combine the data
        for sprite_name, anim_table in graphics_info.items():
            if sprite_name in frame_counts:
                frame_count = frame_counts[sprite_name]
                metadata[sprite_name] = SpriteAnimationMetadata(
                    sprite_name=sprite_name,
                    animation_table=anim_table,
                    logical_frame_count=frame_count,
                    physical_frame_mapping=frame_mappings.get(sprite_name),
                    animation_definitions=animation_tables.get(anim_table, [])
                )
        
        logger.info(f"Parsed animation data for {len(metadata)} sprites")
        logger.info(f"Parsed {len(filename_mapping)} filename->sprite mappings")
        
        # Store filename mapping
        self.filename_mapping = filename_mapping
        
        return metadata
    
    def _parse_filename_mapping(self, file_path: Path) -> Dict[str, str]:
        """
        Parse filename -> sprite name mapping from object_event_graphics.h
        
        Returns:
            Dictionary mapping "directory/filename" -> sprite name
        """
        result: Dict[str, str] = {}
        if not file_path.exists():
            return result
        
        content = file_path.read_text(encoding='utf-8')
        
        # Match: const u32 gObjectEventPic_<SpriteName>[] = INCBIN_U32("graphics/object_events/pics/people/<path>/<filename>.4bpp");
        pattern = re.compile(
            r'gObjectEventPic_(\w+)\[\]\s*=\s*INCBIN_U32\("graphics/object_events/pics/people/([^"]+)\.4bpp"\)'
        )
        
        for match in pattern.finditer(content):
            sprite_name = match.group(1)  # e.g. "BrendanNormal", "MayRunning"
            sprite_file_path = match.group(2)  # e.g. "brendan/walking", "may/running"
            
            # Extract just the filename without path
            path_obj = Path(sprite_file_path)
            filename = path_obj.name  # e.g. "walking", "running"
            directory = str(path_obj.parent).replace("\\", "/")  # e.g. "brendan", "may"
            
            # Create key as "directory/filename" to match our extraction
            key = f"{directory}/{filename}" if directory else filename
            result[key] = sprite_name
            
            logger.debug(f"  Filename mapping: {key} -> {sprite_name}")
        
        return result
    
    def parse_pic_table_sources(
        self,
        pic_tables_path: Path,
        graphics_path: Path
    ) -> Dict[str, List[SpriteSourceInfo]]:
        """
        Parse which PNG files belong to which sPicTable.
        
        Returns:
            Dictionary mapping sPicTable name -> list of SpriteSourceInfo
        """
        result: Dict[str, List[SpriteSourceInfo]] = {}
        
        if not pic_tables_path.exists() or not graphics_path.exists():
            return result
        
        pic_tables_content = pic_tables_path.read_text(encoding='utf-8')
        graphics_content = graphics_path.read_text(encoding='utf-8')
        
        # First, build a map of gObjectEventPic names to file paths
        pic_to_file: Dict[str, str] = {}
        file_pattern = re.compile(
            r'gObjectEventPic_(\w+)\[\]\s*=\s*INCBIN_U32\("graphics/object_events/pics/people/([^"]+)\.4bpp"\)'
        )
        
        for match in file_pattern.finditer(graphics_content):
            pic_to_file[match.group(1)] = match.group(2)
        
        # Parse each sPicTable and find which gObjectEventPic_* it references
        table_pattern = re.compile(
            r'sPicTable_(\w+)\[\]\s*=\s*\{([^}]+)\}',
            re.DOTALL
        )
        
        for table_match in table_pattern.finditer(pic_tables_content):
            table_name = table_match.group(1)
            table_content = table_match.group(2)
            
            sources: List[SpriteSourceInfo] = []
            seen_pics: Dict[str, SpriteSourceInfo] = {}
            
            # Find all gObjectEventPic references in this table
            frame_pattern = re.compile(
                r'(?:overworld_frame|obj_frame_tiles)\(gObjectEventPic_(\w+)'
            )
            
            for frame_match in frame_pattern.finditer(table_content):
                pic_name = frame_match.group(1)
                
                if pic_name not in seen_pics:
                    source_info = SpriteSourceInfo(
                        pic_name=pic_name,
                        file_path=pic_to_file.get(pic_name, ""),
                        start_frame=sum(s.frame_count for s in seen_pics.values()),
                        frame_count=0
                    )
                    seen_pics[pic_name] = source_info
                    sources.append(source_info)
                
                seen_pics[pic_name].frame_count += 1
            
            if sources:
                result[table_name] = sources
        
        return result
    
    def _parse_graphics_info(self, file_path: Path) -> Dict[str, str]:
        """
        Parse sprite graphics info to get animation table assignments.
        
        Returns:
            Dictionary mapping sprite name -> animation table name
        """
        result: Dict[str, str] = {}
        if not file_path.exists():
            return result
        
        content = file_path.read_text(encoding='utf-8')
        
        # Match: const struct ObjectEventGraphicsInfo gObjectEventGraphicsInfo_<Name> = { ... .anims = sAnimTable_<AnimTable>, ... }
        pattern = re.compile(
            r'gObjectEventGraphicsInfo_(\w+)\s*=\s*\{[^}]*\.anims\s*=\s*sAnimTable_(\w+)',
            re.DOTALL
        )
        
        for match in pattern.finditer(content):
            sprite_name = match.group(1)
            anim_table = match.group(2)
            result[sprite_name] = anim_table
        
        return result
    
    def _parse_frame_counts(self, file_path: Path) -> Dict[str, int]:
        """
        Parse frame counts from pic tables.
        
        Returns:
            Dictionary mapping sprite name -> frame count
        """
        result: Dict[str, int] = {}
        if not file_path.exists():
            return result
        
        content = file_path.read_text(encoding='utf-8')
        
        # Match: sPicTable_<Name>[] = { ... }
        pattern = re.compile(r'sPicTable_(\w+)\[\]\s*=\s*\{([^}]+)\}', re.DOTALL)
        
        for match in pattern.finditer(content):
            sprite_name = match.group(1)
            table_content = match.group(2)
            
            # Count the number of overworld_frame or obj_frame_tiles entries
            frame_matches = re.findall(r'overworld_frame|obj_frame_tiles', table_content)
            result[sprite_name] = len(frame_matches)
        
        return result
    
    def _parse_frame_mappings(self, file_path: Path) -> Dict[str, List[int]]:
        """
        Parse physical frame mappings from pic tables.
        
        CRITICAL FIX: When a pic table references multiple PNG files (e.g., walking.png + running.png),
        we need to adjust frame indices based on which PNG they come from:
        - Frames from walking.png (first PNG): use frame_index as-is (0-8)
        - Frames from running.png (second PNG): add offset of 9 (walking frame count) to get (9-17)
        
        Returns:
            Dictionary mapping sprite name -> list of physical frame indices in the combined spritesheet
        """
        result: Dict[str, List[int]] = {}
        if not file_path.exists():
            return result
        
        content = file_path.read_text(encoding='utf-8')
        
        # First, parse pic table sources to know frame offsets
        graphics_path = file_path.parent / "object_event_graphics.h"
        pic_table_sources = self.parse_pic_table_sources(file_path, graphics_path)
        
        # Match: sPicTable_<Name>[] = { ... }
        pattern = re.compile(r'sPicTable_(\w+)\[\]\s*=\s*\{([^}]+)\}', re.DOTALL)
        
        for match in pattern.finditer(content):
            sprite_name = match.group(1)
            table_content = match.group(2)
            
            # Get source info for this sprite (to know frame offsets)
            sources = pic_table_sources.get(sprite_name, [])
            
            # Build a map: pic_name -> start_frame_offset
            pic_to_offset: Dict[str, int] = {}
            for source in sources:
                pic_to_offset[source.pic_name] = source.start_frame
            
            # Parse overworld_frame(gObjectEventPic_<PicName>, ..., frame_index_in_png)
            frame_mapping: List[int] = []
            frame_pattern = re.compile(r'overworld_frame\(gObjectEventPic_(\w+),\s*\d+,\s*\d+,\s*(\d+)\)')
            
            for frame_match in frame_pattern.finditer(table_content):
                pic_name = frame_match.group(1)
                frame_index_in_png = int(frame_match.group(2))
                
                # Adjust frame index based on which PNG this came from
                offset = pic_to_offset.get(pic_name, 0)
                physical_frame_index = offset + frame_index_in_png
                frame_mapping.append(physical_frame_index)
            
            # Handle obj_frame_tiles (single frame sprites)
            if not frame_mapping and 'obj_frame_tiles' in table_content:
                frame_mapping.append(0)  # Single frame, index 0
            
            if frame_mapping:
                result[sprite_name] = frame_mapping
        
        return result
    
    def _parse_animation_tables(self, file_path: Path) -> Dict[str, List[AnimationDefinition]]:
        """
        Parse animation tables from object_event_anims.h.
        
        Returns:
            Dictionary mapping animation table name -> list of AnimationDefinition
        """
        result: Dict[str, List[AnimationDefinition]] = {}
        if not file_path.exists():
            return result
        
        content = file_path.read_text(encoding='utf-8')
        
        # Parse individual animation sequences
        anim_sequences: Dict[str, List[AnimFrame]] = {}
        seq_pattern = re.compile(r'sAnim_(\w+)\[\]\s*=\s*\{([^}]+)\}', re.DOTALL)
        
        for match in seq_pattern.finditer(content):
            anim_name = match.group(1)
            anim_content = match.group(2)
            frames: List[AnimFrame] = []
            
            # Parse ANIMCMD_FRAME(frameIndex, duration, .hFlip = true/false)
            frame_pattern = re.compile(
                r'ANIMCMD_FRAME\((\d+),\s*(\d+)(?:,\s*\.hFlip\s*=\s*(TRUE|FALSE))?\)'
            )
            
            for frame_match in frame_pattern.finditer(anim_content):
                frames.append(AnimFrame(
                    frame_index=int(frame_match.group(1)),
                    duration=int(frame_match.group(2)),
                    flip_horizontal=frame_match.group(3) == "TRUE"
                ))
            
            if frames:
                anim_sequences[f"sAnim_{anim_name}"] = frames
        
        # Parse animation tables that reference sequences
        table_pattern = re.compile(
            r'sAnimTable_(\w+)\[\]\s*=\s*\{([^}]+)\}',
            re.DOTALL
        )
        
        for match in table_pattern.finditer(content):
            table_name = match.group(1)
            table_content = match.group(2)
            animations: List[AnimationDefinition] = []
            
            # Parse [ANIM_*] = sAnim_<Name>,
            # Matches both ANIM_STD_* and other patterns
            entry_pattern = re.compile(r'\[ANIM_(?:STD_)?([A-Z_]+)\]\s*=\s*sAnim_(\w+)')
            
            for entry_match in entry_pattern.finditer(table_content):
                anim_type = entry_match.group(1)
                anim_seq_name = f"sAnim_{entry_match.group(2)}"
                
                if anim_seq_name in anim_sequences:
                    animations.append(AnimationDefinition(
                        name=self._convert_anim_type_name(anim_type),
                        frames=anim_sequences[anim_seq_name]
                    ))
            
            if animations:
                result[table_name] = animations
        
        return result
    
    def _convert_anim_type_name(self, emerald_name: str) -> str:
        """
        Convert pokeemerald's SCREAMING_SNAKE_CASE to lowercase_snake_case.
        
        Examples:
            FACE_SOUTH -> face_south
            GO_SOUTH -> go_south
            TAKE_OUT_ROD_SOUTH -> take_out_rod_south
        """
        return emerald_name.lower()

