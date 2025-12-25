"""
Definition format converter - converts pokeemerald/Tiled maps to PokeSharp Definition format.

This produces JSON files in the Definitions folder structure that can be loaded
by PokeSharp's GameDataLoader into EF Core entities.
"""

import json
import base64
from pathlib import Path
from typing import Dict, List, Any, Optional, Set
from .logging_config import get_logger

logger = get_logger('definition_converter')


class DefinitionConverter:
    """
    Converts pokeemerald/Tiled data to PokeSharp Definition format.

    Output structure matches the Mods folder organization:
    - Definitions/Maps/Regions/{Region}/ - Map definitions
    - Definitions/Tilesets/{Region}/ - Tileset definitions
    - Definitions/Weather/ - Weather definitions
    - Definitions/BattleScenes/ - Battle scene definitions
    """

    def __init__(self, output_dir: str):
        self.output_dir = Path(output_dir)
        # Track unique values for generating stub definitions
        self.weather_ids: Set[str] = set()
        self.battle_scene_ids: Set[str] = set()
        self.mapsec_ids: Set[str] = set()

    @staticmethod
    def _to_texture_id(category: str, name: str) -> str:
        """Convert a texture path to standard ID format: base:texture:category/name."""
        # Normalize name: remove extension, lowercase, underscores to lowercase
        name = name.replace(".png", "").replace(".jpg", "").lower()
        return f"base:texture:{category}/{name}"

    @staticmethod
    def _normalize_id_component(value: str) -> str:
        """Normalize a string to a valid ID component (lowercase, underscores, alphanumeric)."""
        if not value:
            return "unknown"
        # Replace spaces and hyphens with underscores, keep alphanumeric and underscores, lowercase
        result = ""
        for c in value.lower():
            if c.isalnum():
                result += c
            elif c in (' ', '-', '_'):
                if result and result[-1] != '_':
                    result += '_'
        # Strip trailing underscore
        return result.rstrip('_') or "unknown"

    @staticmethod
    def _to_script_id(category: str, name: str) -> str:
        """Convert a script path to standard ID format: base:script:category/name."""
        name = name.replace(".csx", "").replace(".lua", "").lower()
        return f"base:script:{category}/{name}"

    def convert_tiled_map_to_definition(
        self,
        tiled_map: Dict[str, Any],
        map_id: str,
        region: str,
        map_connections: Optional[Dict[str, Any]] = None
    ) -> Dict[str, Any]:
        """
        Convert a Tiled map JSON structure to MapEntity-compatible Definition format.

        Args:
            tiled_map: The Tiled map JSON data
            map_id: Map identifier (e.g., "littleroot_town")
            region: Region name (e.g., "hoenn")
            map_connections: Optional dict of north/south/east/west connections

        Returns:
            MapEntity-compatible JSON structure for Definition
        """
        # Extract map properties from Tiled (handle both simple and class properties)
        properties = {}
        class_properties = {}
        for p in tiled_map.get("properties", []):
            prop_name = p.get("name", "")
            prop_type = p.get("type", "")
            if prop_type == "class":
                class_properties[prop_name] = p.get("value", {})
            else:
                properties[prop_name] = p.get("value")

        # Convert weather and battle scene, tracking for later stub generation
        weather_id = self._convert_weather_id(properties.get("weather"), region)
        if weather_id:
            self.weather_ids.add(weather_id)

        battle_scene_id = self._convert_battle_scene_id(properties.get("battleScene"), region)
        if battle_scene_id:
            self.battle_scene_ids.add(battle_scene_id)

        music_id = self._convert_music_id(properties.get("music"), region)

        # Look for regionMapSection (legacy) or mapSection (new)
        mapsec_id = self._convert_mapsec_id(
            properties.get("regionMapSection") or properties.get("mapSection"), region)
        if mapsec_id:
            self.mapsec_ids.add(mapsec_id)

        # Build MapEntity structure matching C# entity exactly
        definition = {
            # Primary key
            "mapId": f"base:map:{region}/{map_id}",

            # BaseEntity fields
            "name": properties.get("displayName", self._format_display_name(map_id)),
            "description": properties.get("description", ""),

            # Region reference
            "regionId": f"base:region:{region}",

            # Map type
            "mapType": properties.get("mapType", self._infer_map_type(map_id)),

            # Dimensions
            "width": tiled_map.get("width", 0),
            "height": tiled_map.get("height", 0),
            "tileWidth": tiled_map.get("tilewidth", 16),
            "tileHeight": tiled_map.get("tileheight", 16),

            # Metadata references
            "musicId": music_id,
            "weatherId": weather_id,
            "battleSceneId": battle_scene_id,
            "mapSectionId": mapsec_id,

            # Map flags
            "showMapName": properties.get("showMapName", True),
            "canFly": properties.get("canFly", False),
            "requiresFlash": properties.get("requiresFlash", False),
            "allowRunning": properties.get("allowRunning", True),
            "allowCycling": properties.get("allowCycling", True),
            "allowEscaping": properties.get("allowEscaping", False),

            # Map connections (structured object)
            "connections": self._build_connections(class_properties, region),

            # Extra data
            "encounterDataJson": properties.get("encounterData"),
            "customPropertiesJson": None,

            # Border data (structured object)
            "border": self._build_border(class_properties),

            # Owned collections
            "layers": [],
            "tilesetRefs": [],
            "warps": [],
            "triggers": [],
            "interactions": [],
            "npcs": []
        }

        # Apply map connections if provided (merges with connections from class properties)
        if map_connections:
            self._apply_connections(definition, map_connections, region)

        # Convert tilesets
        for tileset in tiled_map.get("tilesets", []):
            definition["tilesetRefs"].append(self._convert_tileset_ref(tileset, region))

        # Set border tilesetId from the first tileset if border data exists
        border = definition.get("border")
        if border and definition["tilesetRefs"]:
            has_border_data = (
                any(gid != 0 for gid in (border.get("bottomLayer") or [])) or
                any(gid != 0 for gid in (border.get("topLayer") or []))
            )
            if has_border_data and not border.get("tilesetId"):
                border["tilesetId"] = definition["tilesetRefs"][0].get("tilesetId")

        # Convert layers
        layer_index = 0
        for layer in tiled_map.get("layers", []):
            layer_type = layer.get("type")

            if layer_type == "tilelayer":
                definition["layers"].append(self._convert_tile_layer(layer, map_id, region, layer_index))
                layer_index += 1
            elif layer_type == "objectgroup":
                self._convert_object_layer(layer, definition, region, map_id)

        return definition

    def _format_display_name(self, map_id: str) -> str:
        """Convert map_id to display name (e.g., littleroot_town -> Littleroot Town)."""
        return map_id.replace("_", " ").title()

    def _infer_map_type(self, map_id: str) -> str:
        """Infer map type from map ID."""
        map_lower = map_id.lower()
        if "town" in map_lower:
            return "town"
        elif "city" in map_lower:
            return "city"
        elif "route" in map_lower:
            return "route"
        elif "cave" in map_lower:
            return "cave"
        elif "gym" in map_lower:
            return "gym"
        elif "pokemon_center" in map_lower or "pokecenter" in map_lower:
            return "pokemon_center"
        elif "mart" in map_lower:
            return "mart"
        elif "house" in map_lower or "home" in map_lower:
            return "building"
        elif "lab" in map_lower:
            return "building"
        else:
            return "indoor"

    def _convert_weather_id(self, weather: Optional[str], region: str) -> Optional[str]:
        """Convert weather string to GameWeatherId format."""
        if not weather:
            return None
        # Transform WEATHER_SUNNY to base:weather:outdoor/sunny
        weather_name = weather.lower().replace("weather_", "")
        return f"base:weather:outdoor/{weather_name}"

    def _convert_battle_scene_id(self, battle_scene: Optional[str], region: str) -> Optional[str]:
        """Convert battle scene string to GameBattleSceneId format."""
        if not battle_scene:
            return None
        # Transform MAP_BATTLE_SCENE_NORMAL to base:battlescene:normal/field
        scene_name = battle_scene.lower().replace("map_battle_scene_", "")
        return f"base:battlescene:normal/{scene_name}"

    def _convert_music_id(self, music: Optional[str], region: str) -> Optional[str]:
        """Convert music string to GameAudioId format."""
        if not music:
            return None
        # Already in format like "base:audio:music/towns/mus_littleroot"
        if music.startswith("base:"):
            return music
        # Transform MUS_LITTLEROOT to base:audio:music/towns/mus_littleroot
        music_name = music.lower()
        return f"base:audio:music/{music_name}"

    def _convert_mapsec_id(self, mapsec: Optional[str], region: str) -> Optional[str]:
        """Convert map section to GameMapSectionId format."""
        if not mapsec:
            return None
        if mapsec.startswith("base:"):
            return mapsec
        # Transform MAPSEC_LITTLEROOT_TOWN to base:mapsec:hoenn/littleroot_town
        section_name = mapsec.lower().replace("mapsec_", "")
        return f"base:mapsec:{region}/{section_name}"

    def _build_connections(self, class_properties: Dict[str, Any], region: str) -> Optional[Dict[str, Any]]:
        """
        Build structured connections object from class properties.
        Returns object with north/south/east/west keys containing {mapId, offset} objects.
        """
        connections = {}
        has_any_connection = False

        for direction in ["north", "south", "east", "west"]:
            conn_prop = class_properties.get(f"connection_{direction}", {})
            map_name = conn_prop.get("map", "")
            if map_name:
                # Convert MAP_ROUTE_101 to base:map:hoenn/route_101
                if not map_name.startswith("base:"):
                    map_key = map_name.lower().replace("map_", "")
                    map_id = f"base:map:{region}/{map_key}"
                else:
                    map_id = map_name
                connections[direction] = {
                    "mapId": map_id,
                    "offset": conn_prop.get("offset", 0)
                }
                has_any_connection = True

        return connections if has_any_connection else None

    def _build_border(self, class_properties: Dict[str, Any]) -> Optional[Dict[str, Any]]:
        """
        Build structured border object from class properties.
        Returns object with tilesetId, bottomLayer[4], topLayer[4].
        """
        border_prop = class_properties.get("border", {})

        # Bottom layer: 2x2 pattern [top-left, top-right, bottom-left, bottom-right]
        bottom_layer = [
            border_prop.get("top_left", 0),
            border_prop.get("top_right", 0),
            border_prop.get("bottom_left", 0),
            border_prop.get("bottom_right", 0)
        ]

        # Top layer: 2x2 pattern [top-left, top-right, bottom-left, bottom-right]
        top_layer = [
            border_prop.get("top_left_top", 0),
            border_prop.get("top_right_top", 0),
            border_prop.get("bottom_left_top", 0),
            border_prop.get("bottom_right_top", 0)
        ]

        # Check if any border data exists
        has_border_data = any(gid != 0 for gid in bottom_layer + top_layer)

        if not has_border_data:
            return None

        return {
            "tilesetId": None,  # Will be set from first tileset if border data exists
            "bottomLayer": bottom_layer,
            "topLayer": top_layer
        }

    def _apply_connections(
        self,
        definition: Dict[str, Any],
        connections: Dict[str, Any],
        region: str
    ) -> None:
        """Apply map connections from pokeemerald map.json data (merges into structured connections)."""
        # Ensure connections object exists
        if definition.get("connections") is None:
            definition["connections"] = {}

        for direction in ["north", "south", "east", "west"]:
            conn = connections.get(direction)
            if conn:
                map_name = conn.get("map", "")
                if map_name:
                    # Convert MAP_ROUTE_101 to base:map:hoenn/route_101
                    map_key = map_name.lower().replace("map_", "")
                    definition["connections"][direction] = {
                        "mapId": f"base:map:{region}/{map_key}",
                        "offset": conn.get("offset", 0)
                    }

    def _convert_tileset_ref(self, tileset: Dict[str, Any], region: str) -> Dict[str, Any]:
        """Convert tileset reference to MapTilesetRef format."""
        source = tileset.get("source", "")
        tileset_name = Path(source).stem if source else "unknown"

        return {
            "firstGid": tileset.get("firstgid", 1),
            "tilesetId": f"base:tileset:{region}/{tileset_name}"
        }

    def _convert_tile_layer(self, layer: Dict[str, Any], map_id: str, region: str, layer_index: int) -> Dict[str, Any]:
        """Convert Tiled tile layer to MapLayer format."""
        data = layer.get("data", [])

        # Convert tile data to bytes (uint32 array -> bytes) then base64
        # EF Core will store as byte[] but JSON needs base64 encoding
        tile_bytes = b''.join(
            int.to_bytes(tile_id, 4, byteorder='little', signed=False)
            for tile_id in data
        )
        tile_data_b64 = base64.b64encode(tile_bytes).decode('ascii')

        layer_name = layer.get("name", f"layer_{layer_index}")
        # Normalize layer name to valid ID component
        layer_name_normalized = self._normalize_id_component(layer_name)

        return {
            # GameLayerId format: base:layer:{region}/{map_name}/{layer_name}
            "layerId": f"base:layer:{region}/{map_id}/{layer_name_normalized}",
            "name": layer_name,
            "type": layer.get("type", "tilelayer"),
            "width": layer.get("width", 0),
            "height": layer.get("height", 0),
            "visible": layer.get("visible", True),
            "opacity": layer.get("opacity", 1.0),
            "offsetX": layer.get("offsetx", 0),
            "offsetY": layer.get("offsety", 0),
            "tileData": tile_data_b64,
            "imagePath": layer.get("image")
        }

    def _convert_object_layer(
        self,
        layer: Dict[str, Any],
        definition: Dict[str, Any],
        region: str,
        map_id: str
    ) -> None:
        """Convert Tiled object layer to owned collections."""
        layer_name = layer.get("name", "").lower()
        objects = layer.get("objects", [])

        for obj in objects:
            if layer_name == "warps":
                warp_index = len(definition["warps"])
                warp = self._convert_warp(obj, region, map_id, warp_index)
                if warp:
                    definition["warps"].append(warp)
            elif layer_name == "triggers":
                trigger_index = len(definition["triggers"])
                trigger = self._convert_trigger(obj, region, map_id, trigger_index)
                if trigger:
                    definition["triggers"].append(trigger)
            elif layer_name == "interactions":
                interaction_index = len(definition["interactions"])
                interaction = self._convert_interaction(obj, region, map_id, interaction_index)
                if interaction:
                    definition["interactions"].append(interaction)
            elif layer_name == "npcs":
                npc_index = len(definition["npcs"])
                npc = self._convert_npc(obj, region, map_id, npc_index)
                if npc:
                    definition["npcs"].append(npc)

    def _get_property_value(self, obj: Dict[str, Any], prop_name: str, default=None):
        """Get a property value from object properties."""
        for prop in obj.get("properties", []):
            if prop.get("name") == prop_name:
                return prop.get("value", default)
        return default

    def _get_class_property(self, obj: Dict[str, Any], prop_name: str) -> Optional[Dict]:
        """Get a class-type property value from object properties."""
        for prop in obj.get("properties", []):
            if prop.get("name") == prop_name and prop.get("type") == "class":
                return prop.get("value")
        return None

    def _convert_warp(self, obj: Dict[str, Any], region: str, map_id: str, index: int) -> Optional[Dict[str, Any]]:
        """Convert Tiled warp object to MapWarp format."""
        warp_data = self._get_class_property(obj, "warp")
        if not warp_data:
            return None

        target_map = warp_data.get("map", "")
        if not target_map:
            return None

        target_map_id = target_map if ":" in target_map else f"base:map:{region}/{target_map}"

        # Generate warp identifier from name or index
        warp_name = obj.get("name")
        if warp_name:
            warp_identifier = self._normalize_id_component(warp_name)
        else:
            warp_identifier = f"warp_{index}"

        return {
            # GameWarpId format: base:warp:{region}/{map_name}/{warp_identifier}
            "warpId": f"base:warp:{region}/{map_id}/{warp_identifier}",
            "name": warp_name,
            "x": obj.get("x", 0),
            "y": obj.get("y", 0),
            "width": obj.get("width", 16),
            "height": obj.get("height", 16),
            "targetMapId": target_map_id,
            "targetX": warp_data.get("x", 0),
            "targetY": warp_data.get("y", 0),
            "elevation": self._get_property_value(obj, "elevation", 0)
        }

    def _convert_trigger(self, obj: Dict[str, Any], region: str, map_id: str, index: int) -> Optional[Dict[str, Any]]:
        """Convert Tiled trigger object to MapTrigger format."""
        trigger_data = self._get_class_property(obj, "trigger")
        if not trigger_data:
            return None

        # Read new property names: conditionVariable, expectedValue, triggerId
        # Fallback to old names for backward compatibility with unregenerated Tiled maps
        condition_variable = trigger_data.get("conditionVariable", trigger_data.get("variable", ""))
        expected_value = trigger_data.get("expectedValue", trigger_data.get("value", 0))
        trigger_id = trigger_data.get("triggerId", trigger_data.get("triggerScript", ""))

        if not condition_variable or not trigger_id:
            return None
        
        # Normalize trigger_id: remove .csx extension and convert to proper format if needed
        if trigger_id.endswith(".csx"):
            trigger_id = trigger_id[:-4]
        # Convert old format paths to new format IDs
        if "/" in trigger_id and not trigger_id.startswith("base:script:"):
            # Old format: "hoenn/Triggers/script_name.csx" -> "base:script:trigger/script_name"
            parts = trigger_id.replace(".csx", "").split("/")
            if len(parts) >= 2 and parts[-2].lower() == "triggers":
                trigger_id = f"base:script:trigger/{parts[-1].lower()}"
            elif ":" not in trigger_id:
                # Fallback: just use the script name
                script_name = parts[-1].lower() if parts else trigger_id.lower()
                trigger_id = f"base:script:trigger/{script_name}"

        var_id = f"base:variable:{region}/{condition_variable}" if ":" not in condition_variable else condition_variable

        # Generate trigger identifier from name or index
        trigger_name = obj.get("name")
        if trigger_name:
            trigger_identifier = self._normalize_id_component(trigger_name)
        else:
            trigger_identifier = f"trigger_{index}"

        return {
            "name": trigger_name,
            "x": obj.get("x", 0),
            "y": obj.get("y", 0),
            "width": obj.get("width", 16),
            "height": obj.get("height", 16),
            "variable": var_id,
            "value": expected_value,
            "triggerId": trigger_id if ":" in trigger_id else f"base:script:{region}/{trigger_id}",
            "elevation": self._get_property_value(obj, "elevation", 0)
        }

    def _convert_interaction(self, obj: Dict[str, Any], region: str, map_id: str, index: int) -> Optional[Dict[str, Any]]:
        """Convert Tiled interaction object to MapInteraction format."""
        sign_data = self._get_class_property(obj, "sign")
        if sign_data:
            # Try new property name first, fallback to old for backward compatibility
            interaction_id = sign_data.get("interactionId", sign_data.get("interactionScript", ""))
        else:
            # Try new property name first, fallback to old for backward compatibility
            interaction_id = self._get_property_value(obj, "interactionId", self._get_property_value(obj, "interactionScript", ""))

        if not interaction_id:
            return None
        
        # Normalize interaction_id: remove .csx extension and convert to proper format if needed
        if interaction_id.endswith(".csx"):
            interaction_id = interaction_id[:-4]
        # Convert old format paths to new format IDs
        if "/" in interaction_id and not interaction_id.startswith("base:script:"):
            # Old format: "hoenn/Interactions/script_name.csx" -> "base:script:interaction/script_name"
            parts = interaction_id.replace(".csx", "").split("/")
            if len(parts) >= 2 and parts[-2].lower() == "interactions":
                interaction_id = f"base:script:interaction/{parts[-1].lower()}"
            elif ":" not in interaction_id:
                # Fallback: just use the script name
                script_name = parts[-1].lower() if parts else interaction_id.lower()
                interaction_id = f"base:script:interaction/{script_name}"

        # Generate interaction identifier from name or index
        interaction_name = obj.get("name")
        if interaction_name:
            interaction_identifier = self._normalize_id_component(interaction_name)
        else:
            interaction_identifier = f"interaction_{index}"

        return {
            "name": interaction_name,
            "x": obj.get("x", 0),
            "y": obj.get("y", 0),
            "width": obj.get("width", 16),
            "height": obj.get("height", 16),
            "interactionId": interaction_id if ":" in interaction_id else f"base:script:{region}/{interaction_id}",
            "elevation": self._get_property_value(obj, "elevation", 0)
        }

    def _convert_npc(self, obj: Dict[str, Any], region: str, map_id: str, index: int) -> Optional[Dict[str, Any]]:
        """Convert Tiled NPC object to MapNpc format."""
        sprite_id = self._get_property_value(obj, "spriteId")
        behavior_id = self._get_property_value(obj, "behaviorId")

        if not sprite_id or not behavior_id:
            return None

        sprite = sprite_id if ":" in sprite_id else f"base:sprite:{sprite_id}"
        
        # Convert behavior ID: handle old format (base:behavior:movement/...) and new format (base:script:behavior/...)
        if ":" in behavior_id:
            # Already has namespace, check if it's old format
            if behavior_id.startswith("base:behavior:movement/"):
                # Convert old format to new format
                behavior_name = behavior_id.split("/")[-1]
                behavior = f"base:script:behavior/{behavior_name}"
            elif behavior_id.startswith("base:behavior:interaction/"):
                # This shouldn't happen for behaviorId, but handle it anyway
                behavior_name = behavior_id.split("/")[-1]
                behavior = f"base:script:behavior/{behavior_name}"
            else:
                # Already in correct format or different format, use as-is
                behavior = behavior_id
        else:
            # No namespace, assume it's just the behavior name
            behavior = f"base:script:behavior/{behavior_id}"

        # Try new property name first, fallback to old for backward compatibility
        interaction_id = self._get_property_value(obj, "interactionId", self._get_property_value(obj, "interactionScript", None))
        interaction_id_formatted = None
        if interaction_id:
            # Normalize: remove .csx extension if present
            if interaction_id.endswith(".csx"):
                interaction_id = interaction_id[:-4]
            # Convert old format paths to new format IDs
            if "/" in interaction_id and not interaction_id.startswith("base:script:"):
                parts = interaction_id.split("/")
                if len(parts) >= 2 and parts[-2].lower() == "interactions":
                    interaction_id = f"base:script:interaction/{parts[-1].lower()}"
                elif ":" not in interaction_id:
                    script_name = parts[-1].lower() if parts else interaction_id.lower()
                    interaction_id = f"base:script:interaction/{script_name}"
            interaction_id_formatted = interaction_id if ":" in interaction_id else f"base:script:interaction/{interaction_id}"

        visibility_flag = self._get_property_value(obj, "visibilityFlag")
        flag_id = None
        if visibility_flag:
            flag_id = visibility_flag if ":" in visibility_flag else f"base:flag:{region}/{visibility_flag}"

        # Generate NPC identifier from name or index
        npc_name = obj.get("name")
        if npc_name:
            npc_identifier = self._normalize_id_component(npc_name)
        else:
            npc_identifier = f"npc_{index}"

        return {
            # GameNpcId format: base:npc:{region}/{map_name}/{npc_identifier}
            "npcId": f"base:npc:{region}/{map_id}/{npc_identifier}",
            "name": npc_name,
            "x": obj.get("x", 0),
            "y": obj.get("y", 0),
            "spriteId": sprite,
            "behaviorId": behavior,
            "interactionId": interaction_id_formatted,
            "visibilityFlag": flag_id,
            "direction": self._get_property_value(obj, "direction"),
            "rangeX": self._get_property_value(obj, "rangeX", 0),
            "rangeY": self._get_property_value(obj, "rangeY", 0),
            "elevation": self._get_property_value(obj, "elevation", 0)
        }

    def convert_tileset_to_definition(
        self,
        tileset_json: Dict[str, Any],
        tileset_id: str,
        region: str
    ) -> Dict[str, Any]:
        """Convert a Tiled tileset JSON to TilesetEntity format."""
        # texturePath should be relative from Assets root: Graphics/Tilesets/{Region}/{name}.png
        texture_path = f"Graphics/Tilesets/{region.title()}/{tileset_id}.png"
        definition = {
            "tilesetId": f"base:tileset:{region}/{tileset_id}",
            "name": self._format_display_name(tileset_id),
            "texturePath": texture_path,
            "tileWidth": tileset_json.get("tilewidth", 16),
            "tileHeight": tileset_json.get("tileheight", 16),
            "tileCount": tileset_json.get("tilecount", 0),
            "columns": tileset_json.get("columns", 1),
            "imageWidth": tileset_json.get("imagewidth", 0),
            "imageHeight": tileset_json.get("imageheight", 0),
            "spacing": tileset_json.get("spacing", 0),
            "margin": tileset_json.get("margin", 0),
            "tiles": []
        }

        # Convert tile definitions (only tiles with special properties)
        for tile in tileset_json.get("tiles", []):
            tile_def = self._convert_tile_definition(tile)
            if tile_def:
                definition["tiles"].append(tile_def)

        return definition

    def _convert_tile_definition(self, tile: Dict[str, Any]) -> Optional[Dict[str, Any]]:
        """Convert Tiled tile definition to TileDefinition format."""
        animation = tile.get("animation", [])
        properties = tile.get("properties", [])
        tile_type = tile.get("type")

        has_animation = len(animation) > 0
        has_behavior = any(p.get("name") == "behavior" for p in properties)
        has_type = bool(tile_type)

        if not has_animation and not has_behavior and not has_type:
            return None

        definition = {
            "localTileId": tile.get("id", 0),
            "type": tile_type,
            "tileBehaviorId": None,
            "animation": []
        }

        for prop in properties:
            if prop.get("name") == "behavior":
                behavior_value = prop.get("value")
                if behavior_value:
                    definition["tileBehaviorId"] = f"base:tilebehavior:{behavior_value}"

        for frame in animation:
            definition["animation"].append({
                "tileId": frame.get("tileid", 0),
                "durationMs": frame.get("duration", 100)
            })

        return definition

    def save_map_definition(
        self,
        definition: Dict[str, Any],
        map_id: str,
        region: str
    ) -> Path:
        """Save MapEntity definition to Definitions/Maps/Regions/{Region}/."""
        output_path = self.output_dir / "Definitions" / "Maps" / "Regions" / region.title() / f"{map_id}.json"
        output_path.parent.mkdir(parents=True, exist_ok=True)

        with open(output_path, 'w', encoding='utf-8') as f:
            json.dump(definition, f, indent=2)

        return output_path

    def save_tileset_definition(
        self,
        definition: Dict[str, Any],
        tileset_id: str,
        region: str
    ) -> Path:
        """Save TilesetEntity definition to Definitions/Maps/Tilesets/{Region}/."""
        output_path = self.output_dir / "Definitions" / "Maps" / "Tilesets" / region.title() / f"{tileset_id}.json"
        output_path.parent.mkdir(parents=True, exist_ok=True)

        with open(output_path, 'w', encoding='utf-8') as f:
            json.dump(definition, f, indent=2)

        return output_path

    def generate_weather_definitions(self, region: str) -> int:
        """Generate weather definitions matching WeatherEntity for all referenced weather IDs."""
        count = 0
        weather_dir = self.output_dir / "Definitions" / "Weather"
        weather_dir.mkdir(parents=True, exist_ok=True)

        # Weather-specific configuration with standardized IDs
        weather_config = {
            "sunny": {
                "intensity": 1.0,
                "affectsBattle": True,
                "screenTint": "#FFD700",
                "screenTintOpacity": 0.1
            },
            "sunny_clouds": {
                "intensity": 1.0,
                "affectsBattle": False,
                "effectScriptId": self._to_script_id("weather", "clouds")
            },
            "rain": {
                "intensity": 1.0,
                "affectsBattle": True,
                "effectScriptId": self._to_script_id("weather", "rain"),
                "ambientSoundId": "base:audio:sfx/ambient/rain"
            },
            "rain_thunderstorm": {
                "intensity": 1.5,
                "affectsBattle": True,
                "effectScriptId": self._to_script_id("weather", "thunderstorm"),
                "ambientSoundId": "base:audio:sfx/ambient/thunder",
                "reducesVisibility": True,
                "visibilityRange": 6
            },
            "downpour": {
                "intensity": 2.0,
                "affectsBattle": True,
                "effectScriptId": self._to_script_id("weather", "downpour"),
                "ambientSoundId": "base:audio:sfx/ambient/heavy_rain",
                "reducesVisibility": True,
                "visibilityRange": 4
            },
            "snow": {
                "intensity": 1.0,
                "affectsBattle": True,
                "effectScriptId": self._to_script_id("weather", "snow")
            },
            "sandstorm": {
                "intensity": 1.0,
                "affectsBattle": True,
                "effectScriptId": self._to_script_id("weather", "sandstorm"),
                "reducesVisibility": True,
                "visibilityRange": 5
            },
            "fog_horizontal": {
                "intensity": 0.8,
                "affectsBattle": False,
                "effectScriptId": self._to_script_id("weather", "fog_horizontal"),
                "reducesVisibility": True,
                "visibilityRange": 4
            },
            "fog_diagonal": {
                "intensity": 0.8,
                "affectsBattle": False,
                "effectScriptId": self._to_script_id("weather", "fog_diagonal"),
                "reducesVisibility": True,
                "visibilityRange": 5
            },
            "volcanic_ash": {
                "intensity": 1.0,
                "affectsBattle": False,
                "effectScriptId": self._to_script_id("weather", "ash"),
                "screenTint": "#808080",
                "screenTintOpacity": 0.3
            },
            "underwater_bubbles": {
                "intensity": 0.5,
                "affectsBattle": False,
                "effectScriptId": self._to_script_id("weather", "bubbles"),
                "screenTint": "#0066CC",
                "screenTintOpacity": 0.2
            },
            "shade": {
                "intensity": 0.7,
                "affectsBattle": False,
                "screenTint": "#404040",
                "screenTintOpacity": 0.2
            },
            "drought": {
                "intensity": 2.0,
                "affectsBattle": True,
                "effectScriptId": self._to_script_id("weather", "drought"),
                "screenTint": "#FF6600",
                "screenTintOpacity": 0.15
            },
            "none": {
                "intensity": 0.0,
                "affectsBattle": False
            },
        }

        for weather_id in self.weather_ids:
            # base:weather:outdoor/sunny -> sunny
            parts = weather_id.split("/")
            if len(parts) >= 2:
                weather_name = parts[-1]
                category_parts = weather_id.split(":")
                category = category_parts[2].split("/")[0] if len(category_parts) > 2 else "outdoor"

                config = weather_config.get(weather_name, {})

                definition = {
                    # Primary key
                    "weatherId": weather_id,

                    # BaseEntity fields
                    "name": weather_name.replace("_", " ").title(),
                    "description": f"{weather_name.replace('_', ' ').title()} weather condition",

                    # Weather properties
                    "category": category,
                    "intensity": config.get("intensity", 1.0),
                    "affectsBattle": config.get("affectsBattle", False),
                    "ambientSoundId": config.get("ambientSoundId"),
                    "effectScriptId": config.get("effectScriptId"),
                    "screenTint": config.get("screenTint"),
                    "screenTintOpacity": config.get("screenTintOpacity", 0.0),
                    "reducesVisibility": config.get("reducesVisibility", False),
                    "visibilityRange": config.get("visibilityRange", 10)
                }

                output_path = weather_dir / f"{weather_name}.json"
                if not output_path.exists():
                    with open(output_path, 'w', encoding='utf-8') as f:
                        json.dump(definition, f, indent=2)
                    count += 1

        return count

    def generate_battle_scene_definitions(self, region: str) -> int:
        """Generate battle scene definitions matching BattleSceneEntity for all referenced scene IDs."""
        count = 0
        scene_dir = self.output_dir / "Definitions" / "BattleScenes"
        scene_dir.mkdir(parents=True, exist_ok=True)

        # Battle scene-specific configuration with standardized texture IDs
        # Maps to pokeemerald's graphics/battle_environment/ structure
        scene_config = {
            "normal": {
                "category": "normal",
                "backgroundTextureId": self._to_texture_id("battle/background", "tall_grass"),
                "playerPlatformTextureId": self._to_texture_id("battle/platform", "tall_grass_player"),
                "enemyPlatformTextureId": self._to_texture_id("battle/platform", "tall_grass_enemy"),
            },
            "grass": {
                "category": "normal",
                "backgroundTextureId": self._to_texture_id("battle/background", "tall_grass"),
                "playerPlatformTextureId": self._to_texture_id("battle/platform", "tall_grass_player"),
                "enemyPlatformTextureId": self._to_texture_id("battle/platform", "tall_grass_enemy"),
            },
            "long_grass": {
                "category": "normal",
                "backgroundTextureId": self._to_texture_id("battle/background", "long_grass"),
                "playerPlatformTextureId": self._to_texture_id("battle/platform", "long_grass_player"),
                "enemyPlatformTextureId": self._to_texture_id("battle/platform", "long_grass_enemy"),
            },
            "sand": {
                "category": "normal",
                "backgroundTextureId": self._to_texture_id("battle/background", "sand"),
                "playerPlatformTextureId": self._to_texture_id("battle/platform", "sand_player"),
                "enemyPlatformTextureId": self._to_texture_id("battle/platform", "sand_enemy"),
            },
            "water": {
                "category": "normal",
                "backgroundTextureId": self._to_texture_id("battle/background", "water"),
                "playerPlatformTextureId": self._to_texture_id("battle/platform", "water_player"),
                "enemyPlatformTextureId": self._to_texture_id("battle/platform", "water_enemy"),
                "hasAnimatedBackground": True
            },
            "pond": {
                "category": "normal",
                "backgroundTextureId": self._to_texture_id("battle/background", "pond_water"),
                "playerPlatformTextureId": self._to_texture_id("battle/platform", "pond_player"),
                "enemyPlatformTextureId": self._to_texture_id("battle/platform", "pond_enemy"),
                "hasAnimatedBackground": True
            },
            "cave": {
                "category": "normal",
                "backgroundTextureId": self._to_texture_id("battle/background", "cave"),
                "playerPlatformTextureId": self._to_texture_id("battle/platform", "cave_player"),
                "enemyPlatformTextureId": self._to_texture_id("battle/platform", "cave_enemy"),
            },
            "rock": {
                "category": "normal",
                "backgroundTextureId": self._to_texture_id("battle/background", "rock"),
                "playerPlatformTextureId": self._to_texture_id("battle/platform", "rock_player"),
                "enemyPlatformTextureId": self._to_texture_id("battle/platform", "rock_enemy"),
            },
            "building": {
                "category": "indoor",
                "backgroundTextureId": self._to_texture_id("battle/background", "building"),
                "playerPlatformTextureId": self._to_texture_id("battle/platform", "building_player"),
                "enemyPlatformTextureId": self._to_texture_id("battle/platform", "building_enemy"),
            },
            "gym": {
                "category": "gym",
                "backgroundTextureId": self._to_texture_id("battle/background", "building"),
                "playerPlatformTextureId": self._to_texture_id("battle/platform", "building_player"),
                "enemyPlatformTextureId": self._to_texture_id("battle/platform", "building_enemy"),
                "paletteId": self._to_texture_id("battle/palette", "gym"),
            },
            "frontier": {
                "category": "frontier",
                "backgroundTextureId": self._to_texture_id("battle/background", "building"),
                "playerPlatformTextureId": self._to_texture_id("battle/platform", "building_player"),
                "enemyPlatformTextureId": self._to_texture_id("battle/platform", "building_enemy"),
                "paletteId": self._to_texture_id("battle/palette", "frontier"),
            },
            "aqua": {
                "category": "team",
                "backgroundTextureId": self._to_texture_id("battle/background", "stadium"),
                "playerPlatformTextureId": self._to_texture_id("battle/platform", "stadium_player"),
                "enemyPlatformTextureId": self._to_texture_id("battle/platform", "stadium_enemy"),
                "paletteId": self._to_texture_id("battle/palette", "aqua"),
            },
            "magma": {
                "category": "team",
                "backgroundTextureId": self._to_texture_id("battle/background", "stadium"),
                "playerPlatformTextureId": self._to_texture_id("battle/platform", "stadium_player"),
                "enemyPlatformTextureId": self._to_texture_id("battle/platform", "stadium_enemy"),
                "paletteId": self._to_texture_id("battle/palette", "magma"),
            },
            "sidney": {
                "category": "elite_four",
                "backgroundTextureId": self._to_texture_id("battle/background", "stadium"),
                "playerPlatformTextureId": self._to_texture_id("battle/platform", "stadium_player"),
                "enemyPlatformTextureId": self._to_texture_id("battle/platform", "stadium_enemy"),
                "paletteId": self._to_texture_id("battle/palette", "elite_sidney"),
                "defaultMusicId": "base:audio:music/battle/elite_four"
            },
            "phoebe": {
                "category": "elite_four",
                "backgroundTextureId": self._to_texture_id("battle/background", "stadium"),
                "playerPlatformTextureId": self._to_texture_id("battle/platform", "stadium_player"),
                "enemyPlatformTextureId": self._to_texture_id("battle/platform", "stadium_enemy"),
                "paletteId": self._to_texture_id("battle/palette", "elite_phoebe"),
                "defaultMusicId": "base:audio:music/battle/elite_four"
            },
            "glacia": {
                "category": "elite_four",
                "backgroundTextureId": self._to_texture_id("battle/background", "stadium"),
                "playerPlatformTextureId": self._to_texture_id("battle/platform", "stadium_player"),
                "enemyPlatformTextureId": self._to_texture_id("battle/platform", "stadium_enemy"),
                "paletteId": self._to_texture_id("battle/palette", "elite_glacia"),
                "defaultMusicId": "base:audio:music/battle/elite_four"
            },
            "drake": {
                "category": "elite_four",
                "backgroundTextureId": self._to_texture_id("battle/background", "stadium"),
                "playerPlatformTextureId": self._to_texture_id("battle/platform", "stadium_player"),
                "enemyPlatformTextureId": self._to_texture_id("battle/platform", "stadium_enemy"),
                "paletteId": self._to_texture_id("battle/palette", "elite_drake"),
                "defaultMusicId": "base:audio:music/battle/elite_four"
            },
            "champion": {
                "category": "champion",
                "backgroundTextureId": self._to_texture_id("battle/background", "stadium"),
                "playerPlatformTextureId": self._to_texture_id("battle/platform", "stadium_player"),
                "enemyPlatformTextureId": self._to_texture_id("battle/platform", "stadium_enemy"),
                "paletteId": self._to_texture_id("battle/palette", "champion"),
                "defaultMusicId": "base:audio:music/battle/champion"
            },
        }

        for scene_id in self.battle_scene_ids:
            # base:battlescene:normal/grass -> grass
            parts = scene_id.split("/")
            if len(parts) >= 2:
                scene_name = parts[-1]
                category_parts = scene_id.split(":")
                category = category_parts[2].split("/")[0] if len(category_parts) > 2 else "normal"

                config = scene_config.get(scene_name, {"category": category})

                definition = {
                    # Primary key
                    "battleSceneId": scene_id,

                    # BaseEntity fields
                    "name": scene_name.replace("_", " ").title(),
                    "description": f"{scene_name.replace('_', ' ').title()} battle background",

                    # Battle scene properties
                    "category": config.get("category", category),
                    "backgroundTextureId": config.get("backgroundTextureId", self._to_texture_id("battle/background", scene_name)),
                    "playerPlatformTextureId": config.get("playerPlatformTextureId", self._to_texture_id("battle/platform", f"{scene_name}_player")),
                    "enemyPlatformTextureId": config.get("enemyPlatformTextureId", self._to_texture_id("battle/platform", f"{scene_name}_enemy")),
                    "paletteId": config.get("paletteId"),
                    "defaultMusicId": config.get("defaultMusicId"),
                    "hasAnimatedBackground": config.get("hasAnimatedBackground", False),
                    "backgroundAnimationId": config.get("backgroundAnimationId"),
                    "playerPlatformOffsetY": config.get("playerPlatformOffsetY", 0),
                    "enemyPlatformOffsetY": config.get("enemyPlatformOffsetY", 0)
                }

                output_path = scene_dir / f"{scene_name}.json"
                if not output_path.exists():
                    with open(output_path, 'w', encoding='utf-8') as f:
                        json.dump(definition, f, indent=2)
                    count += 1

        return count

    def generate_region_definition(self, region: str) -> Path:
        """Generate a region definition matching RegionEntity."""
        region_dir = self.output_dir / "Definitions" / "Regions"
        region_dir.mkdir(parents=True, exist_ok=True)

        definition = {
            # Primary key
            "regionId": f"base:region:{region}",

            # BaseEntity fields
            "name": region.title(),
            "displayName": region.title(),
            "description": f"The {region.title()} region",

            # Region properties
            "regionMapTextureId": self._to_texture_id("region/map", region),
            "startingMapId": f"base:map:{region}/littleroot_town",
            "startingX": 5,
            "startingY": 8,
            "startingDirection": "down",
            "defaultFlyMapId": f"base:map:{region}/littleroot_town",
            "defaultFlyX": 5,
            "defaultFlyY": 8,
            "regionalDexId": f"base:pokedex:{region}/regional",
            "sortOrder": 3 if region == "hoenn" else 1,
            "isPlayable": True
        }

        output_path = region_dir / f"{region}.json"
        with open(output_path, 'w', encoding='utf-8') as f:
            json.dump(definition, f, indent=2)

        return output_path
