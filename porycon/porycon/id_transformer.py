"""
Centralized ID transformation for all pokeemerald -> PokeSharp conversions.

This module implements the unified ID format:
    {namespace}:{type}:{category}/{name}

Examples:
    base:map:hoenn/littleroot_town
    base:npc:townfolk/prof_birch
    base:trainer:youngster/joey
    base:mapsec:hoenn/littleroot_town
    base:theme:popup/wood
    base:sprite:player/may

All IDs are:
- Lowercase alphanumeric with underscores
- Self-describing (type is embedded in the ID)
- Namespace-prefixed for mod support
- Human-readable for debugging
"""

import re
from typing import Optional
from .logging_config import get_logger

logger = get_logger('id_transformer')


# Valid ID pattern: namespace:type:category/name OR namespace:type:category/subcategory/name
ID_PATTERN = re.compile(r'^[a-z0-9_]+:[a-z]+:[a-z0-9_]+/[a-z0-9_]+(/[a-z0-9_]+)?$')

# Entity types
class EntityType:
    MAP = "map"
    NPC = "npc"
    TRAINER = "trainer"
    MAPSEC = "mapsec"
    THEME = "theme"
    SPRITE = "sprite"
    ITEM = "item"
    POKEMON = "pokemon"
    WARP = "warp"
    BEHAVIOR = "behavior"
    SCRIPT = "script"
    AUDIO = "audio"


class IdTransformer:
    """
    Transforms pokeemerald IDs to PokeSharp unified format.

    The namespace is always "base" for pokeemerald conversions.
    Mods would use their mod ID as the namespace.
    """

    NAMESPACE = "base"
    DEFAULT_REGION = "hoenn"

    @classmethod
    def validate_id(cls, entity_id: str) -> bool:
        """Validate that an ID matches the expected format."""
        return bool(ID_PATTERN.match(entity_id))

    @classmethod
    def parse_id(cls, entity_id: str) -> Optional[dict]:
        """
        Parse an ID into its components.

        Returns:
            Dict with keys: namespace, type, category, name, subcategory (optional)
            None if invalid format
        """
        if not cls.validate_id(entity_id):
            return None

        # Split namespace:type:category/[subcategory/]name
        namespace, rest = entity_id.split(':', 1)
        entity_type, path = rest.split(':', 1)
        parts = path.split('/')

        if len(parts) == 3:
            # Has subcategory: category/subcategory/name
            category, subcategory, name = parts
            return {
                "namespace": namespace,
                "type": entity_type,
                "category": category,
                "subcategory": subcategory,
                "name": name
            }
        else:
            # No subcategory: category/name
            category, name = parts
            return {
                "namespace": namespace,
                "type": entity_type,
                "category": category,
                "subcategory": None,
                "name": name
            }

    @classmethod
    def create_id(cls, entity_type: str, category: str, name: str,
                  namespace: Optional[str] = None,
                  subcategory: Optional[str] = None) -> str:
        """
        Create a properly formatted ID.

        Args:
            entity_type: The entity type (map, npc, trainer, etc.)
            category: The category within the type (hoenn, townfolk, etc.)
            name: The specific name (littleroot_town, prof_birch, etc.)
            namespace: Optional namespace override (defaults to "base")
            subcategory: Optional subcategory (e.g., "generic" for generic NPCs)

        Returns:
            Formatted ID string: namespace:type:category/[subcategory/]name
        """
        ns = namespace or cls.NAMESPACE

        # Normalize all parts to lowercase with underscores
        entity_type = cls._normalize(entity_type)
        category = cls._normalize(category)
        name = cls._normalize(name)

        if subcategory:
            subcategory = cls._normalize(subcategory)
            return f"{ns}:{entity_type}:{category}/{subcategory}/{name}"

        return f"{ns}:{entity_type}:{category}/{name}"

    @classmethod
    def _normalize(cls, value: str) -> str:
        """Normalize a string to lowercase with underscores."""
        # Convert CamelCase to snake_case
        s1 = re.sub('(.)([A-Z][a-z]+)', r'\1_\2', value)
        s2 = re.sub('([a-z0-9])([A-Z])', r'\1_\2', s1)
        # Replace spaces and hyphens with underscores
        s3 = re.sub(r'[\s\-]+', '_', s2)
        # Remove any non-alphanumeric characters except underscore
        s4 = re.sub(r'[^a-z0-9_]', '', s3.lower())
        # Collapse multiple underscores
        s5 = re.sub(r'_+', '_', s4)
        # Remove leading/trailing underscores
        s6 = s5.strip('_')
        # Fix floor suffixes - handles Pokemon map floor naming conventions
        # Pattern: _1_f -> _1f, _2_r -> _2r (above ground floors)
        s7 = re.sub(r'_(\d+)_([fr])($|_)', r'_\1\2\3', s6)
        # Pattern: _b1_f -> _b1f, _b2_f -> _b2f (basement floors)
        s8 = re.sub(r'_b(\d+)_([fr])($|_)', r'_b\1\2\3', s7)
        return s8

    # =========================================================================
    # MAP IDs
    # =========================================================================

    @classmethod
    def map_id(cls, pokeemerald_map_id: str, region: Optional[str] = None) -> str:
        """
        Transform pokeemerald map ID to unified format.

        Args:
            pokeemerald_map_id: e.g., "MAP_LITTLEROOT_TOWN", "MAP_ROUTE101"
            region: Optional region override (defaults to "hoenn")

        Returns:
            e.g., "base:map:hoenn/littleroot_town"
        """
        # Remove MAP_ prefix
        name = pokeemerald_map_id
        if name.startswith("MAP_"):
            name = name[4:]

        name = cls._normalize(name)
        category = region or cls.DEFAULT_REGION

        return cls.create_id(EntityType.MAP, category, name)

    @classmethod
    def map_id_from_filename(cls, filename: str, region: Optional[str] = None) -> str:
        """
        Transform a map filename to unified format.

        Args:
            filename: e.g., "littleroot_town.json", "route101"
            region: Optional region override

        Returns:
            e.g., "base:map:hoenn/littleroot_town"
        """
        # Remove extension
        name = filename
        if '.' in name:
            name = name.rsplit('.', 1)[0]

        name = cls._normalize(name)
        category = region or cls.DEFAULT_REGION

        return cls.create_id(EntityType.MAP, category, name)

    @classmethod
    def map_name_from_id(cls, entity_id: str) -> str:
        """
        Extract just the map name from a full ID.

        Args:
            entity_id: e.g., "base:map:hoenn/littleroot_town"

        Returns:
            e.g., "littleroot_town"
        """
        parsed = cls.parse_id(entity_id)
        if parsed:
            return parsed["name"]
        # Fallback: assume it's already just a name
        return cls._normalize(entity_id)

    # =========================================================================
    # MAP SECTION IDs
    # =========================================================================

    @classmethod
    def mapsec_id(cls, pokeemerald_mapsec: str, region: Optional[str] = None) -> str:
        """
        Transform pokeemerald MAPSEC to unified format.

        Args:
            pokeemerald_mapsec: e.g., "MAPSEC_LITTLEROOT_TOWN"
            region: Optional region override

        Returns:
            e.g., "base:mapsec:hoenn/littleroot_town"
        """
        name = pokeemerald_mapsec
        if name.startswith("MAPSEC_"):
            name = name[7:]

        name = cls._normalize(name)
        category = region or cls.DEFAULT_REGION

        return cls.create_id(EntityType.MAPSEC, category, name)

    # =========================================================================
    # THEME IDs
    # =========================================================================

    @classmethod
    def theme_id(cls, theme_name: str) -> str:
        """
        Transform theme name to unified format.

        Args:
            theme_name: e.g., "wood", "marble", "MAPPOPUP_THEME_WOOD"

        Returns:
            e.g., "base:theme:popup/wood"
        """
        name = theme_name
        if name.startswith("MAPPOPUP_THEME_"):
            name = name[15:]

        name = cls._normalize(name)

        return cls.create_id(EntityType.THEME, "popup", name)

    @classmethod
    def popup_background_id(cls, bg_name: str) -> str:
        """
        Transform popup background name to unified format.

        Args:
            bg_name: e.g., "wood", "marble"

        Returns:
            e.g., "base:popup:background/wood"
        """
        name = cls._normalize(bg_name)
        return cls.create_id("popup", "background", name)

    @classmethod
    def popup_outline_id(cls, outline_name: str) -> str:
        """
        Transform popup outline name to unified format.

        Args:
            outline_name: e.g., "wood_outline", "marble_outline"

        Returns:
            e.g., "base:popup:outline/wood_outline"
        """
        name = cls._normalize(outline_name)
        return cls.create_id("popup", "outline", name)

    # =========================================================================
    # NPC IDs (for future use)
    # =========================================================================

    @classmethod
    def npc_id(cls, npc_type: str, name: str) -> str:
        """
        Create NPC ID in unified format.

        Args:
            npc_type: Category like "townfolk", "trainer", "shopkeeper"
            name: Specific name like "prof_birch", "rival_may"

        Returns:
            e.g., "base:npc:townfolk/prof_birch"
        """
        npc_type = cls._normalize(npc_type)
        name = cls._normalize(name)

        return cls.create_id(EntityType.NPC, npc_type, name)

    @classmethod
    def npc_id_from_object_event(cls, object_event: dict, map_name: str) -> str:
        """
        Generate NPC ID from pokeemerald object event data.

        Args:
            object_event: Object event dict from pokeemerald map.json
            map_name: The map name for context

        Returns:
            e.g., "base:npc:littleroot_town/obj_1"
        """
        local_id = object_event.get("local_id", 0)
        graphics_id = object_event.get("graphics_id", "")

        # Try to derive a meaningful name from graphics_id
        # e.g., "OBJ_EVENT_GFX_BIRCH" -> "birch"
        if graphics_id.startswith("OBJ_EVENT_GFX_"):
            name = graphics_id[14:].lower()
        else:
            name = f"obj_{local_id}"

        map_name = cls._normalize(map_name)

        return cls.create_id(EntityType.NPC, map_name, name)

    # =========================================================================
    # TRAINER IDs (for future use)
    # =========================================================================

    @classmethod
    def trainer_id(cls, trainer_type: str, encounter_id: Optional[str] = None) -> str:
        """
        Create trainer ID in unified format.

        Args:
            trainer_type: e.g., "TRAINER_YOUNGSTER_JOEY", "youngster"
            encounter_id: Optional specific encounter identifier

        Returns:
            e.g., "base:trainer:youngster/joey"
        """
        name = trainer_type
        if name.startswith("TRAINER_"):
            name = name[8:]

        parts = cls._normalize(name).split('_', 1)
        if len(parts) == 2:
            category, trainer_name = parts
        else:
            category = parts[0]
            trainer_name = encounter_id or "default"

        return cls.create_id(EntityType.TRAINER, category, trainer_name)

    # =========================================================================
    # SPRITE IDs
    # =========================================================================

    @classmethod
    def sprite_id(cls, category: str, name: str) -> str:
        """
        Create sprite ID in unified format.

        Args:
            category: e.g., "player", "npc", "pokemon"
            name: e.g., "may", "boy_1", "bulbasaur"

        Returns:
            e.g., "base:sprite:player/may"
        """
        category = cls._normalize(category)
        name = cls._normalize(name)

        return cls.create_id(EntityType.SPRITE, category, name)

    @classmethod
    def sprite_id_from_graphics(cls, graphics_id: str) -> str:
        """
        Transform pokeemerald graphics ID to sprite ID.

        Args:
            graphics_id: e.g., "OBJ_EVENT_GFX_MAY_NORMAL"

        Returns:
            e.g., "base:sprite:character/may_normal"
        """
        name = graphics_id
        if name.startswith("OBJ_EVENT_GFX_"):
            name = name[14:]

        name = cls._normalize(name)

        return cls.create_id(EntityType.SPRITE, "character", name)

    @classmethod
    def sprite_id_simple(cls, graphics_id: str) -> str:
        """
        Transform pokeemerald graphics ID to full sprite ID for Tiled objects.

        Args:
            graphics_id: e.g., "OBJ_EVENT_GFX_BOY_1", "OBJ_EVENT_GFX_BIRCH"

        Returns:
            e.g., "base:sprite:npcs/generic/boy_1", "base:sprite:npcs/generic/birch"

        The sprite ID format uses subcategory to match the folder structure:
            - base:sprite:npcs/generic/{name} - Generic NPCs
            - base:sprite:npcs/gym_leaders/{name} - Gym leaders
            - base:sprite:npcs/elite_four/{name} - Elite Four
            - base:sprite:npcs/team_aqua/{name} - Team Aqua
            - base:sprite:npcs/team_magma/{name} - Team Magma
            - base:sprite:npcs/frontier_brains/{name} - Frontier Brains
            - base:sprite:players/brendan/{variant} - Brendan
            - base:sprite:players/may/{variant} - May
        """
        if not graphics_id:
            return cls.create_id(EntityType.SPRITE, "npcs", "unknown", subcategory="generic")

        name = graphics_id
        if name.startswith("OBJ_EVENT_GFX_"):
            name = name[14:]

        name = cls._normalize(name)

        # Determine the top-level category and sub-category
        sub_category = cls._infer_sprite_category(name)

        # Player characters go under "players" top-level with character name as subcategory
        if sub_category in ("brendan", "may"):
            # For players, extract variant: brendan_normal -> subcategory=brendan, name=normal
            # The name already starts with brendan_ or may_
            prefix = f"{sub_category}_"
            if name.startswith(prefix):
                variant = name[len(prefix):]
                return cls.create_id(EntityType.SPRITE, "players", variant, subcategory=sub_category)
            return cls.create_id(EntityType.SPRITE, "players", name, subcategory=sub_category)

        # All NPCs go under "npcs" top-level with sub_category as subcategory
        # e.g., "twin" with sub_category "generic" -> "npcs/generic/twin"
        return cls.create_id(EntityType.SPRITE, "npcs", name, subcategory=sub_category)

    @classmethod
    def _infer_sprite_category(cls, sprite_name: str) -> str:
        """
        Infer sprite category from the normalized sprite name.

        Categories match the actual sprite folder structure in Assets/Sprites/:
        - Players: brendan/, may/ (with variants like normal, surfing, machbike)
        - NPCs: generic/, elite_four/, gym_leaders/, team_aqua/, team_magma/, frontier_brains/

        Args:
            sprite_name: Normalized name like "may_normal", "boy_1", "birch"

        Returns:
            Category matching the sprite folder name
        """
        # Elite Four (NPCs/elite_four/)
        elite_four = ["sidney", "phoebe", "glacia", "drake"]
        for name in elite_four:
            if sprite_name == name or sprite_name.startswith(f"{name}_"):
                return "elite_four"

        # Gym Leaders (NPCs/gym_leaders/)
        gym_leaders = ["brawly", "flannery", "juan", "liza", "norman",
                       "roxanne", "tate", "wattson", "winona"]
        for name in gym_leaders:
            if sprite_name == name or sprite_name.startswith(f"{name}_"):
                return "gym_leaders"

        # Frontier Brains (NPCs/frontier_brains/)
        frontier_brains = ["anabel", "brandon", "greta", "lucy",
                           "noland", "spenser", "tucker"]
        for name in frontier_brains:
            if sprite_name == name or sprite_name.startswith(f"{name}_"):
                return "frontier_brains"

        # Team Aqua (NPCs/team_aqua/)
        if sprite_name in ["archie", "aqua_member_f", "aqua_member_m"]:
            return "team_aqua"
        if sprite_name.startswith(("aqua_", "archie_")):
            return "team_aqua"

        # Team Magma (NPCs/team_magma/)
        if sprite_name in ["maxie", "magma_member_f", "magma_member_m"]:
            return "team_magma"
        if sprite_name.startswith(("magma_", "maxie_")):
            return "team_magma"

        # Player characters (Players/brendan/ or Players/may/)
        # These need special handling - the category is the character name
        # and the sprite name becomes the variant (normal, surfing, etc.)
        # But for now, we'll handle this in sprite_id_simple
        if sprite_name.startswith("brendan"):
            return "brendan"
        if sprite_name.startswith("may"):
            return "may"

        # Everything else goes to generic (NPCs/generic/)
        # This includes: mom, prof_birch, steven, wallace, wally, scott,
        # and all generic NPCs like boy_1, girl_2, hiker, etc.
        return "generic"

    # =========================================================================
    # WARP DESTINATION IDs
    # =========================================================================

    @classmethod
    def warp_destination(cls, dest_map_id: str, dest_x: int, dest_y: int,
                        region: Optional[str] = None) -> dict:
        """
        Create warp destination data with proper map ID format.

        Args:
            dest_map_id: Pokeemerald map ID (e.g., "MAP_LITTLEROOT_TOWN")
            dest_x: Destination X coordinate
            dest_y: Destination Y coordinate
            region: Optional region override

        Returns:
            Dict with "map" (unified ID), "x", "y"
        """
        return {
            "map": cls.map_id(dest_map_id, region),
            "x": dest_x,
            "y": dest_y
        }

    # =========================================================================
    # BEHAVIOR IDs
    # =========================================================================

    @classmethod
    def behavior_id(cls, behavior_type: str, behavior_name: str) -> str:
        """
        Create behavior ID in unified format.

        Args:
            behavior_type: e.g., "movement", "interaction", "special"
            behavior_name: e.g., "stationary", "wander", "look_around"

        Returns:
            e.g., "base:behavior:movement/stationary"
        """
        behavior_type = cls._normalize(behavior_type)
        behavior_name = cls._normalize(behavior_name)

        return cls.create_id(EntityType.BEHAVIOR, behavior_type, behavior_name)

    @classmethod
    def behavior_id_from_movement_type(cls, behavior_name: str) -> str:
        """
        Create behavior ID from a short behavior name.

        Most movement-based behaviors use the "movement" category.

        Args:
            behavior_name: e.g., "stationary", "wander", "look_around"

        Returns:
            e.g., "base:behavior:movement/stationary"
        """
        behavior_name = cls._normalize(behavior_name)

        return cls.create_id(EntityType.BEHAVIOR, "movement", behavior_name)

    # =========================================================================
    # FLAG IDs
    # =========================================================================

    # Flag category prefixes and their corresponding categories
    FLAG_PREFIXES = {
        "FLAG_HIDE_": "visibility",
        "FLAG_HIDDEN_ITEM_": "hidden_item",
        "FLAG_ITEM_": "item",
        "FLAG_TEMP_": "temporary",
        "FLAG_DECORATION_": "decoration",
        "FLAG_DEFEATED_": "defeated",
        "FLAG_TRAINER_": "trainer",
        "FLAG_BADGE_": "badge",
        "FLAG_RECEIVED_": "received",
        "FLAG_DAILY_": "daily",
        "FLAG_ENCOUNTERED_": "encountered",
        "FLAG_UNLOCKED_": "unlock",
        "FLAG_COMPLETED_": "story",
        "FLAG_TRIGGERED_": "trigger",
        "FLAG_INTERACTED_": "interaction",
        "FLAG_CAUGHT_": "collection",
    }

    @classmethod
    def flag_id(cls, pokeemerald_flag: str, region: Optional[str] = None) -> str:
        """
        Transform pokeemerald flag to unified format.

        Args:
            pokeemerald_flag: e.g., "FLAG_HIDE_LITTLEROOT_TOWN_FAT_MAN"
            region: Optional region (unused, kept for API compatibility)

        Returns:
            e.g., "base:flag:visibility/littleroot_town_fat_man"

        Flag categories:
            - visibility: FLAG_HIDE_* - NPC/object visibility toggles
            - hidden_item: FLAG_HIDDEN_ITEM_* - Hidden item collection state
            - item: FLAG_ITEM_* - Visible item pickup state
            - temporary: FLAG_TEMP_* - Temporary state flags
            - decoration: FLAG_DECORATION_* - Secret base decorations
            - defeated: FLAG_DEFEATED_* - Defeated trainer/Pokemon
            - trainer: FLAG_TRAINER_* - Trainer battle state
            - badge: FLAG_BADGE_* - Gym badge acquisition
            - received: FLAG_RECEIVED_* - Items/gifts received
            - daily: FLAG_DAILY_* - Daily reset events
            - encountered: FLAG_ENCOUNTERED_* - Pokemon/event encounters
            - unlock: FLAG_UNLOCKED_* - Area/feature unlocks
            - story: FLAG_COMPLETED_* - Story progression
            - trigger: FLAG_TRIGGERED_* - One-time triggers
            - interaction: FLAG_INTERACTED_* - Object interactions
            - collection: FLAG_CAUGHT_* - Pokemon collection state
            - misc: Uncategorized flags
        """
        if not pokeemerald_flag or pokeemerald_flag == "0":
            return ""

        flag_name = pokeemerald_flag
        category = "misc"

        # Determine category from prefix
        for prefix, cat in cls.FLAG_PREFIXES.items():
            if flag_name.startswith(prefix):
                flag_name = flag_name[len(prefix):]
                category = cat
                break
        else:
            # Handle generic FLAG_ prefix
            if flag_name.startswith("FLAG_"):
                flag_name = flag_name[5:]

        # Normalize the flag name
        flag_name = cls._normalize(flag_name)

        return cls.create_id("flag", category, flag_name)

    @classmethod
    def flag_id_from_raw(cls, raw_flag: str, map_name: str,
                         region: Optional[str] = None) -> str:
        """
        Transform a flag reference, with map context fallback.

        Args:
            raw_flag: Flag string from pokeemerald data
            map_name: Current map name for context
            region: Optional region override

        Returns:
            Unified flag ID or empty string if invalid
        """
        if not raw_flag or raw_flag == "0" or raw_flag == "NULL":
            return ""

        return cls.flag_id(raw_flag, region)

    # =========================================================================
    # SCRIPT IDs
    # =========================================================================

    @classmethod
    def script_id(cls, script_category: str, script_name: str) -> str:
        """
        Create script ID in unified format.

        Args:
            script_category: e.g., "map", "event", "trainer"
            script_name: e.g., "littleroot_town_event_script_twin"

        Returns:
            e.g., "base:script:map/littleroot_town_event_script_twin"
        """
        script_category = cls._normalize(script_category)
        script_name = cls._normalize(script_name)

        return cls.create_id(EntityType.SCRIPT, script_category, script_name)

    @classmethod
    def script_id_from_pokeemerald(cls, pokeemerald_script: str, map_name: str) -> str:
        """
        Transform pokeemerald script reference to script ID.

        Args:
            pokeemerald_script: e.g., "LittlerootTown_EventScript_Twin"
            map_name: The map name for categorization

        Returns:
            e.g., "base:script:map/littleroot_town_event_script_twin"
        """
        if not pokeemerald_script or pokeemerald_script == "NULL":
            return ""

        script_name = cls._normalize(pokeemerald_script)

        return cls.create_id(EntityType.SCRIPT, "map", script_name)

    @classmethod
    def interaction_id_from_pokeemerald(cls, pokeemerald_script: str, map_name: str) -> str:
        """
        Transform pokeemerald script reference to interaction ID.

        Args:
            pokeemerald_script: e.g., "LittlerootTown_EventScript_Twin"
            map_name: The map name for categorization (unused, kept for compatibility)

        Returns:
            e.g., "base:behavior:interaction/littleroot_town_event_script_twin"
        """
        if not pokeemerald_script or pokeemerald_script == "NULL":
            return ""

        script_name = cls._normalize(pokeemerald_script)

        return cls.create_id(EntityType.BEHAVIOR, "interaction", script_name)

    # =========================================================================
    # AUDIO IDs
    # =========================================================================

    # Music category keywords for categorization
    # IMPORTANT: Keep in sync with audio_converter.py MidiTrackInfo._categorize_music()
    MUSIC_CATEGORIES = {
        "towns": ["town", "city", "village", "littleroot", "oldale", "petalburg",
                  "rustboro", "dewford", "slateport", "mauville", "verdanturf",
                  "fallarbor", "lavaridge", "fortree", "lilycove", "mossdeep",
                  "sootopolis", "pacifidlog", "ever_grande", "pallet", "viridian",
                  "pewter", "cerulean", "vermillion", "lavender", "celadon",
                  "fuchsia", "saffron", "cinnabar"],
        "routes": ["route", "cycling", "surf", "sailing", "diving", "underwater"],
        "battle": ["battle", "vs_", "encounter", "trainer_battle", "wild_battle",
                   "gym_leader", "elite", "champion", "frontier", "victory"],
        "fanfares": ["fanfare", "jingle", "level_up", "evolution", "heal",
                     "obtained", "pokemon_get", "badge_get", "intro"],
        "special": ["cave", "forest", "desert", "abandoned", "team_aqua",
                    "team_magma", "legendary", "credits", "title", "ending",
                    "hall_of_fame", "mystery", "weather_institute", "space_center",
                    "mt_pyre", "sealed_chamber", "sky_pillar", "meteor_falls",
                    "museum", "pokemon_center", "poke_mart", "mart", "gym",
                    "game_corner", "safari", "contest", "trick_house"]
    }

    @classmethod
    def audio_id(cls, pokeemerald_music: str) -> str:
        """
        Transform pokeemerald music constant to unified audio format.

        Args:
            pokeemerald_music: e.g., "MUS_LITTLEROOT", "MUS_ROUTE101", "MUS_VS_WILD"

        Returns:
            e.g., "base:audio:music/towns/mus_littleroot"
        """
        if not pokeemerald_music:
            return ""

        # Normalize the name (convert to lowercase with underscores)
        name = cls._normalize(pokeemerald_music)

        # Determine category based on keywords
        subcategory = cls._categorize_music(name)

        # Use "music" as category and the music type as subcategory
        # e.g., base:audio:music/towns/mus_littleroot
        return cls.create_id(EntityType.AUDIO, "music", name, subcategory=subcategory)

    @classmethod
    def _categorize_music(cls, name: str) -> str:
        """
        Categorize music track based on name keywords.

        Args:
            name: Normalized track name (e.g., "mus_littleroot", "mus_route101")

        Returns:
            Category string: "towns", "routes", "battle", "fanfares", or "special"
        """
        # Check each category's keywords
        for category, keywords in cls.MUSIC_CATEGORIES.items():
            for keyword in keywords:
                if keyword in name:
                    return category

        # Default to special for uncategorized tracks
        return "special"


# Convenience functions for direct import
def map_id(pokeemerald_id: str, region: Optional[str] = None) -> str:
    """Shortcut for IdTransformer.map_id()"""
    return IdTransformer.map_id(pokeemerald_id, region)

def mapsec_id(pokeemerald_mapsec: str, region: Optional[str] = None) -> str:
    """Shortcut for IdTransformer.mapsec_id()"""
    return IdTransformer.mapsec_id(pokeemerald_mapsec, region)

def theme_id(theme_name: str) -> str:
    """Shortcut for IdTransformer.theme_id()"""
    return IdTransformer.theme_id(theme_name)

def normalize(value: str) -> str:
    """Shortcut for IdTransformer._normalize()"""
    return IdTransformer._normalize(value)

def flag_id(pokeemerald_flag: str, region: Optional[str] = None) -> str:
    """Shortcut for IdTransformer.flag_id()"""
    return IdTransformer.flag_id(pokeemerald_flag, region)

def audio_id(pokeemerald_music: str) -> str:
    """Shortcut for IdTransformer.audio_id()"""
    return IdTransformer.audio_id(pokeemerald_music)
