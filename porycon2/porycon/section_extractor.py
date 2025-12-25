"""
Extract map section (MAPSEC) definitions and popup theme mappings from pokeemerald.

This module:
1. Reads region_map_sections.json for all MAPSEC definitions
2. Parses map_name_popup.c for the theme mapping (MAPSEC -> popup theme)
3. Generates JSON files for each section with its metadata
"""

import json
import re
from pathlib import Path
from typing import Dict, List, Any, Optional, Tuple
from .logging_config import get_logger
from .id_transformer import IdTransformer

logger = get_logger('section_extractor')


# Theme ID mapping (from pokeemerald/src/map_name_popup.c)
THEME_NAMES = {
    0: "wood",
    1: "marble",
    2: "stone",
    3: "brick",
    4: "underwater",
    5: "stone2"
}


class MapSectionExtractor:
    """Extracts map section definitions and popup themes from pokeemerald."""
    
    def __init__(self, input_dir: str):
        """
        Initialize the extractor.
        
        Args:
            input_dir: Path to pokeemerald root directory
        """
        self.input_dir = Path(input_dir)
        self.sections_file = self.input_dir / "src" / "data" / "region_map" / "region_map_sections.json"
        self.popup_c_file = self.input_dir / "src" / "map_name_popup.c"
        
    def extract_sections(self) -> Dict[str, Any]:
        """
        Extract all map section definitions.
        
        Returns:
            Dictionary mapping MAPSEC ID -> section data
        """
        if not self.sections_file.exists():
            logger.error(f"Map sections file not found: {self.sections_file}")
            return {}
        
        try:
            with open(self.sections_file, 'r', encoding='utf-8') as f:
                data = json.load(f)
            
            sections = {}
            for section in data.get("map_sections", []):
                section_id = section.get("id")
                if section_id:
                    sections[section_id] = section
            
            logger.info(f"Extracted {len(sections)} map sections from JSON")
            return sections
            
        except Exception as e:
            logger.error(f"Failed to parse sections file: {e}")
            return {}
    
    def extract_theme_mapping(self) -> Dict[str, str]:
        """
        Extract the MAPSEC -> popup theme mapping from map_name_popup.c.
        
        Returns:
            Dictionary mapping MAPSEC ID -> theme name (e.g., "wood", "marble")
        """
        if not self.popup_c_file.exists():
            logger.error(f"Popup C file not found: {self.popup_c_file}")
            return {}
        
        try:
            with open(self.popup_c_file, 'r', encoding='utf-8') as f:
                content = f.read()
            
            # Find the sMapSectionToThemeId array
            # Pattern: [MAPSEC_NAME] = MAPPOPUP_THEME_NAME,
            pattern = r'\[MAPSEC_([A-Z0-9_]+)(?:\s*-\s*KANTO_MAPSEC_COUNT)?\]\s*=\s*MAPPOPUP_THEME_([A-Z0-9]+)'
            matches = re.findall(pattern, content)
            
            theme_mapping = {}
            for mapsec_name, theme_name in matches:
                full_mapsec = f"MAPSEC_{mapsec_name}"
                theme_lower = theme_name.lower()
                theme_mapping[full_mapsec] = theme_lower
                
            logger.info(f"Extracted {len(theme_mapping)} theme mappings from C file")
            return theme_mapping
            
        except Exception as e:
            logger.error(f"Failed to parse popup C file: {e}")
            return {}
    
    def merge_section_data(
        self,
        sections: Dict[str, Any],
        theme_mapping: Dict[str, str]
    ) -> Dict[str, Dict[str, Any]]:
        """
        Merge section definitions with theme mappings.

        Args:
            sections: Map section definitions from JSON
            theme_mapping: MAPSEC -> theme mappings from C file

        Returns:
            Complete section data with themes (using unified ID format)
        """
        merged = {}

        for section_id, section_data in sections.items():
            # Transform to unified ID format: base:mapsec:hoenn/name
            unified_id = IdTransformer.mapsec_id(section_id)
            # Transform theme to unified format: base:theme:popup/wood
            theme_name = theme_mapping.get(section_id, "wood")
            unified_theme = IdTransformer.theme_id(theme_name)

            # Build clean entity data with unified IDs
            merged_section = {
                "id": unified_id,
                "name": section_data.get("name"),
                "theme": unified_theme
            }

            # Add region map coordinates if they exist
            if "x" in section_data:
                merged_section["x"] = section_data["x"]
            if "y" in section_data:
                merged_section["y"] = section_data["y"]
            if "width" in section_data:
                merged_section["width"] = section_data["width"]
            if "height" in section_data:
                merged_section["height"] = section_data["height"]

            merged[section_id] = merged_section

        # Also add sections that only exist in theme mapping (edge case)
        for section_id, theme in theme_mapping.items():
            if section_id not in merged:
                logger.warning(f"Section {section_id} has theme but no definition in JSON")
                unified_id = IdTransformer.mapsec_id(section_id)
                unified_theme = IdTransformer.theme_id(theme)
                merged[section_id] = {
                    "id": unified_id,
                    "name": section_id.replace("MAPSEC_", "").replace("_", " "),
                    "theme": unified_theme
                }

        return merged
    
    def save_sections(self, sections: Dict[str, Dict[str, Any]], output_dir: str) -> int:
        """
        Save section definitions as individual JSON files.
        
        Args:
            sections: Complete section data
            output_dir: Output directory (should be Assets folder)
        
        Returns:
            Number of sections saved
        """
        output_path = Path(output_dir) / "Definitions" / "Maps" / "Sections"
        output_path.mkdir(parents=True, exist_ok=True)
        
        count = 0
        for section_id, section_data in sections.items():
            # Create filename from section ID (lowercase)
            filename = section_id.lower() + ".json"
            filepath = output_path / filename
            
            try:
                with open(filepath, 'w', encoding='utf-8') as f:
                    json.dump(section_data, f, indent=2, ensure_ascii=False)
                count += 1
            except Exception as e:
                logger.error(f"Failed to save {section_id}: {e}")
        
        logger.info(f"Saved {count} section files to {output_path}")
        return count
    
    def save_themes(self, sections: Dict[str, Dict[str, Any]], output_dir: str) -> int:
        """
        Save popup theme definitions as individual JSON files.

        Args:
            sections: Complete section data (unused, kept for signature compatibility)
            output_dir: Output directory (should be Assets folder)

        Returns:
            Number of theme files created
        """
        output_path = Path(output_dir) / "Definitions" / "Maps" / "Popups" / "Themes"
        output_path.mkdir(parents=True, exist_ok=True)

        # Theme definitions with unified IDs
        theme_names = ["wood", "marble", "stone", "brick", "underwater", "stone2"]
        theme_display = {
            "wood": ("Wood", "Default wooden frame - used for towns, land routes, woods"),
            "marble": ("Marble", "Marble frame - used for major cities"),
            "stone": ("Stone", "Stone frame - used for caves and dungeons"),
            "brick": ("Brick", "Brick frame - used for some cities"),
            "underwater": ("Underwater", "Underwater frame - used for water routes"),
            "stone2": ("Stone 2", "Stone variant 2 - used for underwater areas")
        }

        count = 0
        for theme_name in theme_names:
            # Use unified ID format: base:theme:popup/wood
            unified_id = IdTransformer.theme_id(theme_name)
            display_name, description = theme_display[theme_name]

            # Use unified IDs for background and outline references
            background_id = IdTransformer.popup_background_id(theme_name)
            outline_id = IdTransformer.popup_outline_id(f"{theme_name}_outline")

            theme_data = {
                "id": unified_id,
                "name": display_name,
                "description": description,
                "background": background_id,
                "outline": outline_id
            }

            theme_file = output_path / f"{theme_name}.json"
            try:
                with open(theme_file, 'w', encoding='utf-8') as f:
                    json.dump(theme_data, f, indent=2, ensure_ascii=False)
                count += 1
            except Exception as e:
                logger.error(f"Failed to save theme {theme_name}: {e}")

        logger.info(f"Saved {count} theme files to {output_path}")
        return count


def extract_sections(input_dir: str, output_dir: str) -> Tuple[int, int]:
    """
    Extract map sections and popup themes from pokeemerald.
    
    Args:
        input_dir: Path to pokeemerald root
        output_dir: Path to output directory (should be Assets folder)
    
    Returns:
        Tuple of (section_count, theme_count)
    """
    extractor = MapSectionExtractor(input_dir)
    
    # Extract section definitions
    logger.info("Extracting map section definitions...")
    sections = extractor.extract_sections()
    
    if not sections:
        logger.error("No sections found")
        return 0, 0
    
    # Extract theme mappings
    logger.info("Extracting popup theme mappings...")
    theme_mapping = extractor.extract_theme_mapping()
    
    # Merge data
    logger.info("Merging section data with theme mappings...")
    complete_sections = extractor.merge_section_data(sections, theme_mapping)
    
    # Save sections file
    logger.info("Saving sections...")
    section_count = extractor.save_sections(complete_sections, output_dir)
    
    # Save theme files
    logger.info("Saving themes...")
    theme_count = extractor.save_themes(complete_sections, output_dir)
    
    return section_count, theme_count

