"""
Entity format converter - converts pokeemerald maps to PokeSharp EF Core entity format.

This produces JSON files that can be loaded directly into the EF Core database
without runtime Tiled parsing.
"""

import json
import gzip
import base64
from pathlib import Path
from typing import Dict, List, Any, Optional, Tuple
from .logging_config import get_logger
from .id_transformer import IdTransformer

logger = get_logger('entity_converter')


class EntityConverter:
    """
    Converts pokeemerald/Tiled data to PokeSharp EF Core entity format.

    Output format matches:
    - MapEntity with owned collections (Layers, TilesetRefs, Warps, Triggers, etc.)
    - TilesetEntity with TileDefinition and TileAnimationFrame
    """

    def __init__(self, output_dir: str):
        self.output_dir = Path(output_dir)

    def convert_tiled_map_to_entity(
        self,
        tiled_map: Dict[str, Any],
        map_id: str,
        region: str
    ) -> Dict[str, Any]:
        """
        Convert a Tiled map JSON structure to MapEntity format.

        Args:
            tiled_map: The Tiled map JSON data
            map_id: Map identifier (e.g., "littleroot_town")
            region: Region name (e.g., "hoenn")

        Returns:
            MapEntity JSON structure
        """
        # Extract map properties
        properties = {p["name"]: p["value"] for p in tiled_map.get("properties", [])}

        # Build MapEntity structure
        entity = {
            "mapId": f"base:map:{region}/{map_id}",
            "displayName": properties.get("displayName", map_id.replace("_", " ").title()),
            "width": tiled_map.get("width", 0),
            "height": tiled_map.get("height", 0),
            "tileWidth": tiled_map.get("tilewidth", 16),
            "tileHeight": tiled_map.get("tileheight", 16),
            "regionId": f"base:region:{region}",
            "weatherId": self._convert_weather_id(properties.get("weather"), region),
            "battleSceneId": self._convert_battle_scene_id(properties.get("battleScene"), region),
            "music": properties.get("music"),
            "allowCycling": properties.get("allowCycling", True),
            "allowRunning": properties.get("allowRunning", True),
            "allowEscaping": properties.get("allowEscaping", True),
            "showMapName": properties.get("showMapName", True),
            "layers": [],
            "tilesetRefs": [],
            "warps": [],
            "triggers": [],
            "interactions": [],
            "npcSpawns": []
        }

        # Convert tilesets
        for tileset in tiled_map.get("tilesets", []):
            entity["tilesetRefs"].append(self._convert_tileset_ref(tileset, region))

        # Convert layers
        layer_id = 1
        for layer in tiled_map.get("layers", []):
            layer_type = layer.get("type")

            if layer_type == "tilelayer":
                entity["layers"].append(self._convert_tile_layer(layer, layer_id))
                layer_id += 1
            elif layer_type == "objectgroup":
                self._convert_object_layer(layer, entity, region)

        return entity

    def _convert_weather_id(self, weather: Optional[str], region: str) -> Optional[str]:
        """Convert weather string to GameWeatherId format."""
        if not weather:
            return None
        # Transform WEATHER_SUNNY to base:weather:hoenn/sunny
        weather_name = weather.lower().replace("weather_", "")
        return f"base:weather:{region}/{weather_name}"

    def _convert_battle_scene_id(self, battle_scene: Optional[str], region: str) -> Optional[str]:
        """Convert battle scene string to GameBattleSceneId format."""
        if not battle_scene:
            return None
        # Transform to base:battlescene:hoenn/field
        scene_name = battle_scene.lower().replace("map_battle_scene_", "")
        return f"base:battlescene:{region}/{scene_name}"

    def _convert_tileset_ref(self, tileset: Dict[str, Any], region: str) -> Dict[str, Any]:
        """Convert tileset reference to MapTilesetRef format."""
        source = tileset.get("source", "")
        # Extract tileset name from path
        tileset_name = Path(source).stem if source else "unknown"

        return {
            "firstGid": tileset.get("firstgid", 1),
            "tilesetId": f"base:tileset:{region}/{tileset_name}"
        }

    def _convert_tile_layer(self, layer: Dict[str, Any], layer_id: int) -> Dict[str, Any]:
        """Convert Tiled tile layer to MapLayer format with compressed tile data."""
        data = layer.get("data", [])

        # Convert tile data to bytes (uint32 array -> bytes)
        tile_bytes = b''.join(
            int.to_bytes(tile_id, 4, byteorder='little', signed=False)
            for tile_id in data
        )

        # Compress with gzip and base64 encode
        compressed = gzip.compress(tile_bytes)
        tile_data_b64 = base64.b64encode(compressed).decode('ascii')

        return {
            "layerId": layer_id,
            "name": layer.get("name", f"Layer{layer_id}"),
            "type": "tilelayer",
            "width": layer.get("width", 0),
            "height": layer.get("height", 0),
            "visible": layer.get("visible", True),
            "opacity": layer.get("opacity", 1.0),
            "offsetX": layer.get("offsetx", 0),
            "offsetY": layer.get("offsety", 0),
            "tileData": tile_data_b64,  # Base64-encoded gzip-compressed tile data
            "compression": "gzip"
        }

    def _convert_object_layer(
        self,
        layer: Dict[str, Any],
        entity: Dict[str, Any],
        region: str
    ) -> None:
        """
        Convert Tiled object layer to entity owned collections.

        Object layers are categorized by name:
        - "Warps" -> entity["warps"]
        - "Triggers" -> entity["triggers"]
        - "Interactions" -> entity["interactions"]
        - "NPCs" -> entity["npcSpawns"]
        """
        layer_name = layer.get("name", "").lower()
        objects = layer.get("objects", [])

        for obj in objects:
            if layer_name == "warps":
                warp = self._convert_warp(obj, region)
                if warp:
                    entity["warps"].append(warp)
            elif layer_name == "triggers":
                trigger = self._convert_trigger(obj, region)
                if trigger:
                    entity["triggers"].append(trigger)
            elif layer_name == "interactions":
                interaction = self._convert_interaction(obj, region)
                if interaction:
                    entity["interactions"].append(interaction)
            elif layer_name == "npcs":
                npc = self._convert_npc_spawn(obj, region)
                if npc:
                    entity["npcSpawns"].append(npc)

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

    def _convert_warp(self, obj: Dict[str, Any], region: str) -> Optional[Dict[str, Any]]:
        """Convert Tiled warp object to MapWarp format."""
        warp_data = self._get_class_property(obj, "warp")
        if not warp_data:
            return None

        target_map = warp_data.get("map", "")
        if not target_map:
            return None

        return {
            "objectId": obj.get("id", 0),
            "name": obj.get("name", ""),
            "x": obj.get("x", 0),
            "y": obj.get("y", 0),
            "width": obj.get("width", 16),
            "height": obj.get("height", 16),
            "targetMapId": target_map if ":" in target_map else f"base:map:{region}/{target_map}",
            "targetX": warp_data.get("x", 0),
            "targetY": warp_data.get("y", 0),
            "elevation": self._get_property_value(obj, "elevation", 0)
        }

    def _convert_trigger(self, obj: Dict[str, Any], region: str) -> Optional[Dict[str, Any]]:
        """Convert Tiled trigger object to MapTrigger format."""
        trigger_data = self._get_class_property(obj, "trigger")
        if not trigger_data:
            return None

        # Read new property names: conditionVariable, expectedValue, triggerId
        condition_variable = trigger_data.get("conditionVariable", trigger_data.get("variable", ""))
        expected_value = trigger_data.get("expectedValue", trigger_data.get("value", 0))
        trigger_id = trigger_data.get("triggerId", "")

        if not condition_variable or not trigger_id:
            return None

        return {
            "objectId": obj.get("id", 0),
            "name": obj.get("name", ""),
            "x": obj.get("x", 0),
            "y": obj.get("y", 0),
            "width": obj.get("width", 16),
            "height": obj.get("height", 16),
            "variable": f"base:variable:{region}/{condition_variable}" if ":" not in condition_variable else condition_variable,
            "value": expected_value,
            "triggerId": trigger_id,
            "elevation": self._get_property_value(obj, "elevation", 0)
        }

    def _convert_interaction(self, obj: Dict[str, Any], region: str) -> Optional[Dict[str, Any]]:
        """Convert Tiled interaction object to MapInteraction format."""
        # Check for "sign" property (most common interaction type)
        sign_data = self._get_class_property(obj, "sign")
        if sign_data:
            interaction_id = sign_data.get("interactionId", "")
        else:
            interaction_id = self._get_property_value(obj, "interactionId", "")

        if not interaction_id:
            return None

        return {
            "objectId": obj.get("id", 0),
            "name": obj.get("name", ""),
            "x": obj.get("x", 0),
            "y": obj.get("y", 0),
            "width": obj.get("width", 16),
            "height": obj.get("height", 16),
            "interactionId": interaction_id,
            "elevation": self._get_property_value(obj, "elevation", 0)
        }

    def _convert_npc_spawn(self, obj: Dict[str, Any], region: str) -> Optional[Dict[str, Any]]:
        """Convert Tiled NPC object to MapNpcSpawn format."""
        sprite_id = self._get_property_value(obj, "spriteId")
        behavior_id = self._get_property_value(obj, "behaviorId")

        if not sprite_id or not behavior_id:
            return None

        interaction_id = self._get_property_value(obj, "interactionId")
        visibility_flag = self._get_property_value(obj, "visibilityFlag")

        return {
            "objectId": obj.get("id", 0),
            "name": obj.get("name", ""),
            "x": obj.get("x", 0),
            "y": obj.get("y", 0),
            "spriteId": sprite_id if ":" in sprite_id else f"base:sprite:{sprite_id}",
            "behaviorId": behavior_id if ":" in behavior_id else f"base:behavior:{behavior_id}",
            "interactionId": f"base:script:{region}/{interaction_id}" if interaction_id and ":" not in interaction_id else interaction_id,
            "visibilityFlag": f"base:flag:{region}/{visibility_flag}" if visibility_flag and ":" not in visibility_flag else visibility_flag,
            "direction": self._get_property_value(obj, "direction"),
            "rangeX": self._get_property_value(obj, "rangeX", 0),
            "rangeY": self._get_property_value(obj, "rangeY", 0),
            "elevation": self._get_property_value(obj, "elevation", 0)
        }

    def convert_tileset_to_entity(
        self,
        tileset_json: Dict[str, Any],
        tileset_id: str,
        region: str
    ) -> Dict[str, Any]:
        """
        Convert a Tiled tileset JSON to TilesetEntity format.

        Args:
            tileset_json: The Tiled tileset JSON data
            tileset_id: Tileset identifier (e.g., "general")
            region: Region name (e.g., "hoenn")

        Returns:
            TilesetEntity JSON structure
        """
        entity = {
            "tilesetId": f"base:tileset:{region}/{tileset_id}",
            "texturePath": tileset_json.get("image", ""),
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
                entity["tiles"].append(tile_def)

        return entity

    def _convert_tile_definition(self, tile: Dict[str, Any]) -> Optional[Dict[str, Any]]:
        """Convert Tiled tile definition to TileDefinition format."""
        animation = tile.get("animation", [])
        properties = tile.get("properties", [])
        tile_type = tile.get("type")

        # Only include tiles with animations, behaviors, or types
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

        # Extract behavior
        for prop in properties:
            if prop.get("name") == "behavior":
                behavior_value = prop.get("value")
                if behavior_value:
                    definition["tileBehaviorId"] = f"base:tilebehavior:{behavior_value}"

        # Convert animation frames
        for frame in animation:
            definition["animation"].append({
                "tileId": frame.get("tileid", 0),
                "durationMs": frame.get("duration", 100)
            })

        return definition

    def save_map_entity(
        self,
        entity: Dict[str, Any],
        map_id: str,
        region: str
    ) -> Path:
        """
        Save MapEntity to JSON file.

        Output path: {output_dir}/Entities/Maps/{region}/{map_id}.json
        """
        output_path = self.output_dir / "Entities" / "Maps" / region / f"{map_id}.json"
        output_path.parent.mkdir(parents=True, exist_ok=True)

        with open(output_path, 'w', encoding='utf-8') as f:
            json.dump(entity, f, indent=2)

        return output_path

    def save_tileset_entity(
        self,
        entity: Dict[str, Any],
        tileset_id: str,
        region: str
    ) -> Path:
        """
        Save TilesetEntity to JSON file.

        Output path: {output_dir}/Entities/Tilesets/{region}/{tileset_id}.json
        """
        output_path = self.output_dir / "Entities" / "Tilesets" / region / f"{tileset_id}.json"
        output_path.parent.mkdir(parents=True, exist_ok=True)

        with open(output_path, 'w', encoding='utf-8') as f:
            json.dump(entity, f, indent=2)

        return output_path
