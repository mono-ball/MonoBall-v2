"""
Sprite extractor for Pokemon Emerald sprites.
"""

from pathlib import Path
from typing import Dict, List, Optional, Tuple
from dataclasses import dataclass, field, asdict
from PIL import Image
from .animation_parser import (
    PokeemeraldAnimationParser,
    SpriteAnimationMetadata,
    SpriteSourceInfo
)
from .logging_config import get_logger
from .utils import save_json

logger = get_logger('sprite_extractor')


@dataclass
class FrameInfo:
    """Information about a single frame in a sprite sheet."""
    index: int
    x: int
    y: int
    width: int
    height: int


@dataclass
class AnimationInfo:
    """Information about an animation."""
    name: str
    loop: bool
    frameIndices: List[int]
    frameDurations: List[float]  # Per-frame durations in seconds (one per frame in frameIndices)
    flipHorizontal: bool


@dataclass
class SpriteSheetInfo:
    """Information about a sprite sheet."""
    frame_width: int
    frame_height: int
    frame_count: int


@dataclass
class SpriteManifest:
    """Manifest for a sprite (camelCase for JSON serialization)."""
    id: str
    name: str  # Display name
    type: str
    texturePath: str
    frameWidth: int
    frameHeight: int
    frameCount: int
    frames: List[FrameInfo]
    animations: List[AnimationInfo]


class SpriteExtractor:
    """Extracts sprites from Pokemon Emerald source."""
    
    def __init__(
        self,
        pokeemerald_path: str,
        output_path: str,
        animation_data: Dict[str, SpriteAnimationMetadata],
        filename_mapping: Dict[str, str],
        pic_table_sources: Dict[str, List[SpriteSourceInfo]]
    ):
        """
        Initialize sprite extractor.
        
        Args:
            pokeemerald_path: Path to pokeemerald root
            output_path: Path to output directory
            animation_data: Parsed animation metadata
            filename_mapping: Mapping from filename to sprite name
            pic_table_sources: Mapping from pic table name to source info
        """
        self.pokeemerald_path = Path(pokeemerald_path)
        self.output_path = Path(output_path)
        self.animation_data = animation_data
        self.filename_mapping = filename_mapping
        self.pic_table_sources = pic_table_sources
        
        self.sprites_path = self.pokeemerald_path / "graphics" / "object_events" / "pics" / "people"
        self.palettes_path = self.pokeemerald_path / "graphics" / "object_events" / "palettes"

        # Split output paths for data and graphics
        self.data_output_path = self.output_path / "Definitions" / "Sprites"
        self.graphics_output_path = self.output_path / "Graphics" / "Sprites"
        self.data_output_path.mkdir(parents=True, exist_ok=True)
        self.graphics_output_path.mkdir(parents=True, exist_ok=True)
    
    def extract_all_sprites(self) -> None:
        """Extract all sprites from pokeemerald."""
        logger.info(f"Found {len(self.pic_table_sources)} sPicTable definitions")
        
        success_count = 0
        processed_files = set()
        
        # Extract sprites based on sPicTable definitions
        for pic_table_name, sources in self.pic_table_sources.items():
            # Skip pic tables that have no valid sources (all have empty file paths)
            valid_sources = [s for s in sources if s.file_path and s.file_path.strip()]
            if not valid_sources:
                logger.debug(f"Skipping {pic_table_name}: no valid source files (object has no graphics)")
                continue
            
            try:
                manifest = self._extract_sprite_from_pic_table(pic_table_name, valid_sources)
                if manifest:
                    success_count += 1
                    for source in valid_sources:
                        if source.file_path:
                            processed_files.add(source.file_path)
            except Exception as ex:
                logger.error(f"Error processing {pic_table_name}: {ex}")
        
        # Also extract any standalone sprites not in sPicTables
        if self.sprites_path.exists():
            all_png_files = list(self.sprites_path.rglob("*.png"))
            for png_file in all_png_files:
                relative_path = png_file.relative_to(self.sprites_path)
                path_without_ext = relative_path.with_suffix("")
                path_str = str(path_without_ext).replace("\\", "/")
                
                if path_str not in processed_files:
                    try:
                        manifest = self._extract_standalone_png(png_file)
                        if manifest:
                            success_count += 1
                    except Exception as ex:
                        logger.error(f"Error processing {png_file.name}: {ex}")
        
        logger.info(f"\nExtracted {success_count} sprites")
        logger.info("Sprite data in Assets/Definitions/Sprites/, graphics in Assets/Graphics/Sprites/")
    
    def _extract_sprite_from_pic_table(
        self,
        pic_table_name: str,
        sources: List[SpriteSourceInfo]
    ) -> Optional[SpriteManifest]:
        """Extract sprite from a pic table definition."""
        if not sources:
            return None
        
        # Determine the category and base name from the first source file
        # Filter out empty file paths first
        valid_sources = [s for s in sources if s.file_path and s.file_path.strip()]
        if not valid_sources:
            return None
        
        first_file_path = valid_sources[0].file_path
        directory = str(Path(first_file_path).parent).replace("\\", "/") if "/" in first_file_path else ""
        is_player_sprite = (
            directory.lower().startswith("may") or
            directory.lower().startswith("brendan")
        )
        category = directory.split("/")[0] if directory else "generic"
        
        # Use picTableName as the sprite name
        sprite_name = self._convert_pic_table_name_to_sprite_name(pic_table_name, category)
        
        logger.info(
            f"Processing: {pic_table_name} -> {sprite_name} ({category}) "
            f"[Player: {is_player_sprite}]"
        )
        logger.info(
            f"  Combining {len(valid_sources)} source file(s): "
            f"{', '.join(Path(s.file_path).name for s in valid_sources)}"
        )
        
        # Load all source PNGs
        source_images: List[Image.Image] = []
        total_width = 0
        max_height = 0
        total_physical_frames = 0
        
        for source in valid_sources:
            # Skip sources with empty file paths (these are objects without graphics)
            if not source.file_path or source.file_path.strip() == "":
                logger.debug(f"  Skipping {pic_table_name}: empty file path (object has no graphics)")
                continue
            
            png_path = self.sprites_path / f"{source.file_path}.png"
            if not png_path.exists():
                logger.debug(f"  Source file not found: {png_path} (may be in different directory)")
                continue
            
            img = Image.open(png_path)
            source_images.append(img)
            total_width += img.width
            max_height = max(max_height, img.height)
            
            # Determine frame count for this source
            src_frame_info = self._analyze_sprite_sheet(
                img,
                Path(source.file_path).stem
            )
            total_physical_frames += src_frame_info.frame_count
        
        if not source_images:
            # Don't log as error - many objects don't have graphics files (items, special objects, etc.)
            logger.debug(f"  Skipping {pic_table_name}: no valid source images found (object may not have graphics)")
            return None
        
        # Detect mask color from first source image
        mask_color = self._detect_mask_color(source_images[0])
        
        # Combine images horizontally with transparency applied
        # Track frame positions: which physical frame index corresponds to which X position
        combined = Image.new("RGBA", (total_width, max_height), (0, 0, 0, 0))
        current_x = 0
        physical_frame_positions = []  # List of (physical_index, x_position) tuples
        
        for img_idx, img in enumerate(source_images):
            # Determine frame count for this source image
            src_frame_info = self._analyze_sprite_sheet(
                img,
                Path(valid_sources[img_idx].file_path).stem
            )
            
            # Convert to RGBA with proper transparency handling (palette mode, etc.)
            rgba_img = self._convert_to_rgba_with_transparency(img)
            
            # Apply additional mask color transparency if detected
            if mask_color:
                self._apply_transparency(rgba_img, mask_color)
            
            # Also check for magenta (#FF00FF) as a common transparency mask
            self._apply_magenta_transparency(rgba_img)
            
            combined.paste(rgba_img, (current_x, 0), rgba_img)
            
            # Track frame positions: frames from this image start at current_x
            # Each frame is frame_width pixels wide
            for frame_idx in range(src_frame_info.frame_count):
                physical_frame_index = len(physical_frame_positions)
                frame_x = current_x + (frame_idx * src_frame_info.frame_width)
                physical_frame_positions.append((physical_frame_index, frame_x))
            
            current_x += img.width
        
        if mask_color:
            logger.info(f"  Applied mask color {mask_color} for transparency")
        
        # Determine frame layout - use consistent frame width/height across all sources
        # All source images for the same sprite should have the same frame dimensions
        frame_info = self._analyze_sprite_sheet(
            source_images[0],
            Path(valid_sources[0].file_path).stem
        )
        
        # Verify all source images have the same frame dimensions
        for img_idx, img in enumerate(source_images[1:], 1):
            src_frame_info = self._analyze_sprite_sheet(
                img,
                Path(valid_sources[img_idx].file_path).stem
            )
            if src_frame_info.frame_width != frame_info.frame_width or \
               src_frame_info.frame_height != frame_info.frame_height:
                logger.warning(
                    f"  Frame size mismatch in {pic_table_name}: "
                    f"first image {frame_info.frame_width}x{frame_info.frame_height}, "
                    f"image {img_idx+1} {src_frame_info.frame_width}x{src_frame_info.frame_height}"
                )
        
        # Create output directories
        base_folder = "players" if is_player_sprite else "npcs"
        sprite_type = base_folder

        if is_player_sprite:
            sprite_category = category
        else:
            sprite_category = directory if directory else "generic"

        # Graphics directory
        graphics_dir = self.graphics_output_path / sprite_type / sprite_category
        graphics_dir.mkdir(parents=True, exist_ok=True)

        # Definitions directory
        data_dir = self.data_output_path / sprite_type / sprite_category
        data_dir.mkdir(parents=True, exist_ok=True)

        # Save combined spritesheet to Graphics directory
        graphics_path = graphics_dir / f"{sprite_name}.png"
        combined.save(graphics_path, "PNG")
        
        # Get physical frame mapping from pokeemerald (maps logical -> physical frame indices)
        physical_frame_mapping = self._get_physical_frame_mapping(pic_table_name)
        
        # Create frame info
        # Build a lookup: physical_index -> x_position in combined image
        physical_to_x = {idx: x for idx, x in physical_frame_positions}
        
        frames: List[FrameInfo] = []
        if physical_frame_mapping:
            # Use pokeemerald's physical frame mapping
            for logical_index, physical_index in enumerate(physical_frame_mapping):
                # Get X position from our combined image
                if physical_index in physical_to_x:
                    frame_x = physical_to_x[physical_index]
                else:
                    # Fallback: calculate based on frame width
                    frame_x = physical_index * frame_info.frame_width
                    logger.warning(
                        f"  Physical frame {physical_index} not found in combined image for {pic_table_name}, "
                        f"using calculated position"
                    )
                
                frames.append(FrameInfo(
                    index=logical_index,
                    x=frame_x,
                    y=0,
                    width=frame_info.frame_width,
                    height=frame_info.frame_height
                ))
        else:
            # No mapping: frames are sequential in combined image
            for i, (_, frame_x) in enumerate(physical_frame_positions):
                frames.append(FrameInfo(
                    index=i,
                    x=frame_x,
                    y=0,
                    width=frame_info.frame_width,
                    height=frame_info.frame_height
                ))
        
        logical_frame_count = len(frames)
        animations = self._generate_animations(
            pic_table_name,
            directory,
            frame_info
        )

        # Generate ID and DisplayName
        # EntityId format: base:sprite:category/subcategory/name (subcategory is optional)
        # Use sprite_type as category (npcs or players)
        # Use sprite_category as subcategory (generic, gym_leaders, brendan, may, etc.)
        sprite_id = f"base:sprite:{sprite_type}/{sprite_category}/{sprite_name}"
        display_name = sprite_name.replace("_", " ").title()

        # Generate TexturePath (keeps the original directory structure)
        texture_path = f"Graphics/Sprites/{sprite_type}/{sprite_category}/{sprite_name}.png"

        # Create manifest (camelCase for JSON serialization)
        manifest = SpriteManifest(
            id=sprite_id,
            name=display_name,
            type="Sprite",
            texturePath=texture_path,
            frameWidth=frame_info.frame_width,
            frameHeight=frame_info.frame_height,
            frameCount=logical_frame_count,
            frames=frames,
            animations=animations
        )

        # Save manifest to Definitions directory
        manifest_path = data_dir / f"{sprite_name}.json"
        manifest_dict = asdict(manifest)
        save_json(manifest_dict, str(manifest_path))

        logger.info(
            f"  ✓ Extracted {sprite_name}: {len(frames)} frames, "
            f"{len(animations)} animations"
        )
        
        # Cleanup
        combined.close()
        for img in source_images:
            img.close()
        
        return manifest
    
    def _extract_standalone_png(self, sprite_file_path: Path) -> Optional[SpriteManifest]:
        """Extract a standalone PNG sprite."""
        relative_path = sprite_file_path.relative_to(self.sprites_path)
        sprite_name = sprite_file_path.stem
        directory = str(relative_path.parent).replace("\\", "/") if relative_path.parent != Path(".") else ""

        # Determine sprite type and category
        is_player_sprite = (
            directory.lower().startswith("may") or
            directory.lower().startswith("brendan")
        )
        category = directory.split("/")[0] if directory else "generic"

        logger.info(f"Processing: {sprite_name} ({category}) [Player: {is_player_sprite}]")

        image = Image.open(sprite_file_path)

        # Analyze sprite sheet
        frame_info = self._analyze_sprite_sheet(image, sprite_name)

        # Create output directories
        base_folder = "players" if is_player_sprite else "npcs"
        sprite_type = base_folder
        sprite_category = directory if directory else "generic"

        # Graphics directory
        graphics_dir = self.graphics_output_path / sprite_type / sprite_category
        graphics_dir.mkdir(parents=True, exist_ok=True)

        # Definitions directory
        data_dir = self.data_output_path / sprite_type / sprite_category
        data_dir.mkdir(parents=True, exist_ok=True)

        # Get physical frame mapping
        physical_frame_mapping = self._get_physical_frame_mapping(sprite_name)

        # Create frame info
        frames: List[FrameInfo] = []
        if physical_frame_mapping:
            for logical_index, physical_index in enumerate(physical_frame_mapping):
                frames.append(FrameInfo(
                    index=logical_index,
                    x=physical_index * frame_info.frame_width,
                    y=0,
                    width=frame_info.frame_width,
                    height=frame_info.frame_height
                ))
        else:
            for i in range(frame_info.frame_count):
                frames.append(FrameInfo(
                    index=i,
                    x=i * frame_info.frame_width,
                    y=0,
                    width=frame_info.frame_width,
                    height=frame_info.frame_height
                ))

        # Save sprite sheet with transparency to Graphics directory
        graphics_path = graphics_dir / f"{sprite_name}.png"

        # Detect mask color
        mask_color = self._detect_mask_color(image)

        # Convert to RGBA with proper transparency handling (palette mode, etc.)
        rgba_image = self._convert_to_rgba_with_transparency(image)

        # Apply additional mask color transparency if detected
        if mask_color:
            logger.info(f"  Applying mask color {mask_color} for transparency")
            self._apply_transparency(rgba_image, mask_color)
        
        # Also check for magenta (#FF00FF) as a common transparency mask
        self._apply_magenta_transparency(rgba_image)

        # Save as RGBA PNG
        rgba_image.save(graphics_path, "PNG")

        # Get animation data
        animations = self._generate_animations(sprite_name, directory, frame_info)

        logical_frame_count = len(frames)

        # Generate ID and DisplayName
        # EntityId format: base:sprite:category/subcategory/name (subcategory is optional)
        # Use sprite_type as category (npcs or players)
        # Use sprite_category as subcategory (generic, gym_leaders, brendan, may, etc.)
        sprite_id = f"base:sprite:{sprite_type}/{sprite_category}/{sprite_name}"
        display_name = sprite_name.replace("_", " ").title()

        # Generate TexturePath (keeps the original directory structure)
        texture_path = f"Graphics/Sprites/{sprite_type}/{sprite_category}/{sprite_name}.png"

        # Create manifest (camelCase for JSON serialization)
        manifest = SpriteManifest(
            id=sprite_id,
            name=display_name,
            type="Sprite",
            texturePath=texture_path,
            frameWidth=frame_info.frame_width,
            frameHeight=frame_info.frame_height,
            frameCount=logical_frame_count,
            frames=frames,
            animations=animations
        )

        # Save manifest to Definitions directory
        manifest_path = data_dir / f"{sprite_name}.json"
        manifest_dict = asdict(manifest)
        save_json(manifest_dict, str(manifest_path))

        image.close()
        rgba_image.close()

        return manifest
    
    def _analyze_sprite_sheet(self, image: Image.Image, sprite_name: str) -> SpriteSheetInfo:
        """
        Analyze sprite sheet to determine frame layout.
        
        Pokemon Emerald sprites are typically:
        - 16x16 tiles (small NPCs)
        - 16x32 tiles (normal NPCs) - most common
        - 32x32 tiles (larger NPCs or special sprites)
        """
        width = image.width
        height = image.height
        
        # Detect frame size based on sprite sheet dimensions
        if height == 64 and width % 64 == 0:
            # 32x32 sprites
            frame_width = 32
            frame_height = 32
            frame_count = width // 32
        elif height == 32 and width % 16 == 0:
            # 16x32 sprites (most NPCs)
            frame_width = 16
            frame_height = 32
            frame_count = width // 16
        elif height == 32 and width % 32 == 0:
            # 32x32 sprites stored in 32-height sheet
            frame_width = 32
            frame_height = 32
            frame_count = width // 32
        elif height == 16 and width % 16 == 0:
            # 16x16 sprites (small NPCs)
            frame_width = 16
            frame_height = 16
            frame_count = width // 16
        else:
            # Default: try to detect based on width
            frame_width = height  # Assume square frames
            frame_height = height
            frame_count = width // frame_width if frame_width > 0 else 1
        
        return SpriteSheetInfo(
            frame_width=frame_width,
            frame_height=frame_height,
            frame_count=frame_count
        )
    
    def _get_physical_frame_mapping(self, sprite_name: str) -> Optional[List[int]]:
        """Get physical frame mapping for a sprite."""
        # Try to find the frame mapping from pokeemerald data
        possible_names = [
            sprite_name,
            f"May{self._to_pascal_case(sprite_name)}",
            f"Brendan{self._to_pascal_case(sprite_name)}",
            f"May{sprite_name}",
            f"Brendan{sprite_name}",
        ]
        
        for name in possible_names:
            metadata = self.animation_data.get(name)
            if metadata and metadata.physical_frame_mapping:
                return metadata.physical_frame_mapping
        
        return None
    
    def _generate_animations(
        self,
        pic_table_or_sprite_name: str,
        directory: str,
        info: SpriteSheetInfo
    ) -> List[AnimationInfo]:
        """Generate animations for a sprite."""
        # Build possible sprite lookup names
        possible_names = [
            pic_table_or_sprite_name,
            self._strip_common_suffixes(pic_table_or_sprite_name),
            f"May{self._to_pascal_case(pic_table_or_sprite_name)}",
            f"Brendan{self._to_pascal_case(pic_table_or_sprite_name)}",
        ]
        
        metadata: Optional[SpriteAnimationMetadata] = None
        for name in possible_names:
            if not name:
                continue
            if name in self.animation_data:
                metadata = self.animation_data[name]
                logger.info(
                    f"  Found animation data for {pic_table_or_sprite_name} "
                    f"as {name} ({metadata.animation_table})"
                )
                break
        
        if metadata and metadata.animation_definitions:
            animations: List[AnimationInfo] = []
            max_valid_frame_index = metadata.logical_frame_count - 1
            
            for anim_def in metadata.animation_definitions:
                # Convert from pokeemerald animation frames to our format
                frame_indices = [f.frame_index for f in anim_def.frames]
                
                # Check if ALL frame indices are valid
                if any(idx > max_valid_frame_index for idx in frame_indices):
                    continue  # Skip this animation
                
                # Check if any frame uses horizontal flip
                uses_flip = any(f.flip_horizontal for f in anim_def.frames)
                
                # Extract per-frame durations (GBA runs at ~60 fps, so duration/60 = seconds)
                frame_durations = [f.duration / 60.0 for f in anim_def.frames]
                
                # Check if frame durations vary (for logging)
                has_variable_durations = len(set(f.duration for f in anim_def.frames)) > 1 if anim_def.frames else False
                if has_variable_durations:
                    logger.debug(
                        f"  Animation {anim_def.name} has variable frame durations: "
                        f"{[f.duration for f in anim_def.frames]} ticks "
                        f"({[f'{d:.4f}' for d in frame_durations]}s)"
                    )
                
                animations.append(AnimationInfo(
                    name=anim_def.name,
                    loop=True,
                    frameIndices=frame_indices,
                    frameDurations=frame_durations,  # Per-frame durations
                    flipHorizontal=uses_flip
                ))
            
            return animations
        
        # Missing animation data is expected for many sprites (items, special objects, etc.)
        logger.debug(
            f"  No animation data found for {pic_table_or_sprite_name} "
            f"in pokeemerald source (this is normal for many objects)"
        )
        return []
    
    def _strip_common_suffixes(self, name: str) -> str:
        """Strip common suffixes from sprite names."""
        if name.endswith("Normal"):
            return name[:-6]
        if name.endswith("Running"):
            return name[:-7]
        return name
    
    def _to_pascal_case(self, input_str: str) -> str:
        """Convert snake_case or lowercase to PascalCase."""
        if not input_str:
            return input_str
        
        parts = input_str.split("_")
        return "".join(p.capitalize() if p else "" for p in parts)
    
    def _convert_pic_table_name_to_sprite_name(self, pic_table_name: str, category: str) -> str:
        """Convert pic table name to sprite name."""
        # Remove category prefix
        prefixes = ["Brendan", "May", "RubySapphireBrendan", "RubySapphireMay"]
        for prefix in prefixes:
            if pic_table_name.startswith(prefix):
                suffix = pic_table_name[len(prefix):]
                return suffix.lower() if suffix else self._pascal_to_snake_case(pic_table_name)
        
        # Fallback: convert PascalCase to snake_case
        return self._pascal_to_snake_case(pic_table_name)
    
    def _pascal_to_snake_case(self, input_str: str) -> str:
        """Convert PascalCase to snake_case."""
        if not input_str:
            return input_str
        
        result = []
        for i, c in enumerate(input_str):
            if i > 0 and (c.isupper() or (c.isdigit() and i > 0 and not input_str[i-1].isdigit())):
                result.append('_')
            result.append(c.lower())
        
        return ''.join(result)
    
    def _convert_to_rgba_with_transparency(self, image: Image.Image) -> Image.Image:
        """
        Convert image to RGBA with proper transparency handling.
        
        Handles:
        - Palette mode (P): Palette index 0 is transparent (GBA convention)
        - Other modes: Standard conversion
        
        Note: Magenta (#FF00FF) transparency is handled separately by _apply_magenta_transparency()
        """
        # Handle palette mode images (common in GBA graphics)
        if image.mode == 'P':
            # Get the original palette index data before conversion
            original_data = list(image.getdata())
            
            # Set transparency info for palette index 0 (GBA convention)
            if 'transparency' not in image.info:
                image.info['transparency'] = 0
            
            # Convert to RGBA - PIL will honor the transparency index
            rgba_image = image.convert('RGBA')
            
            # Manually make pixels that were palette index 0 transparent
            pixels = list(rgba_image.getdata())
            new_pixels = []
            transparent_count = 0
            
            for orig_idx, pixel in zip(original_data, pixels):
                if orig_idx == 0:  # Palette index 0 is transparent in GBA
                    new_pixels.append((0, 0, 0, 0))
                    transparent_count += 1
                else:
                    new_pixels.append(pixel)
            
            rgba_image.putdata(new_pixels)
            if transparent_count > 0:
                logger.debug(f"  Made {transparent_count} pixels transparent from palette index 0")
            return rgba_image
        elif image.mode != 'RGBA':
            return image.convert('RGBA')
        else:
            return image.copy()
    
    def _apply_transparency(self, image: Image.Image, mask_color_hex: str) -> None:
        """Apply transparency by replacing mask color with transparent pixels."""
        # Parse hex color
        mask_color_hex = mask_color_hex.lstrip('#')
        r = int(mask_color_hex[0:2], 16)
        g = int(mask_color_hex[2:4], 16)
        b = int(mask_color_hex[4:6], 16)
        
        # Ensure image is RGBA
        if image.mode != "RGBA":
            rgba_image = image.convert("RGBA")
        else:
            rgba_image = image
        
        # Get pixel data
        pixels = rgba_image.load()
        transparent_count = 0
        
        for y in range(rgba_image.height):
            for x in range(rgba_image.width):
                pixel = pixels[x, y]
                # Check if pixel matches mask color (RGB only, ignore alpha)
                if len(pixel) >= 3 and pixel[0] == r and pixel[1] == g and pixel[2] == b:
                    pixels[x, y] = (0, 0, 0, 0)  # Fully transparent
                    transparent_count += 1
        
        logger.debug(f"  Made {transparent_count} pixels transparent from mask color")
    
    def _apply_magenta_transparency(self, image: Image.Image) -> None:
        """Apply transparency for magenta (#FF00FF) pixels, a common GBA transparency mask."""
        if image.mode != "RGBA":
            return
        
        pixels = image.load()
        transparent_count = 0
        
        for y in range(image.height):
            for x in range(image.width):
                r, g, b, a = pixels[x, y]
                # Magenta (#FF00FF) is commonly used as transparency mask in GBA graphics
                if r == 255 and g == 0 and b == 255 and a > 0:
                    pixels[x, y] = (0, 0, 0, 0)  # Fully transparent
                    transparent_count += 1
        
        if transparent_count > 0:
            logger.debug(f"  Made {transparent_count} magenta pixels transparent")
    
    def _detect_mask_color(self, image: Image.Image) -> Optional[str]:
        """
        Detect the mask color used for transparency.
        
        Returns the most common color in the image (usually the background).
        """
        # Count pixel colors to find the most common (background) color
        color_counts: Dict[Tuple[int, int, int], int] = {}
        
        # Convert to RGB if needed
        if image.mode != "RGB":
            rgb_image = image.convert("RGB")
        else:
            rgb_image = image
        
        pixels = rgb_image.load()
        for y in range(rgb_image.height):
            for x in range(rgb_image.width):
                pixel = pixels[x, y]
                color_counts[pixel] = color_counts.get(pixel, 0) + 1
        
        # Find the most common color
        if not color_counts:
            return None
        
        most_common_color = max(color_counts.items(), key=lambda x: x[1])
        
        # If the most common color appears in more than 40% of pixels, it's probably background
        total_pixels = image.width * image.height
        if most_common_color[1] > total_pixels * 0.4:
            r, g, b = most_common_color[0]
            return f"#{r:02X}{g:02X}{b:02X}"
        
        return None

