"""
Constants for Pokemon Emerald format.

This module contains all magic numbers and constants used throughout the codebase
to make the code more maintainable and self-documenting.
"""

# Tileset constants
NUM_METATILES_IN_PRIMARY = 512
NUM_TILES_IN_PRIMARY_VRAM = 512

# Tile dimensions
TILE_SIZE = 8  # 8x8 pixels
METATILE_SIZE = 16  # 16x16 pixels (2x2 tiles)
NUM_TILES_PER_METATILE = 8
METATILE_WIDTH = 2  # Metatiles are 2 tiles wide
METATILE_HEIGHT = 2  # Metatiles are 2 tiles tall

# Bit masks for metatile data
METATILE_ID_MASK = 0x03FF  # Bits 0-9: Metatile ID
COLLISION_MASK = 0x0C00    # Bits 10-11: Collision type
ELEVATION_MASK = 0xF000    # Bits 12-15: Elevation
PALETTE_MASK = 0xF000      # Bits 12-15: Palette index (same bits as elevation in different context)

# Flip flags
FLIP_HORIZONTAL = 0x01
FLIP_VERTICAL = 0x02

# Palette constants
NUM_PALETTES_PER_TILESET = 16
PALETTE_COLORS = 16

# Animation constants
DEFAULT_ANIMATION_DURATION_MS = 200
STANDARD_ANIMATION_FRAMES = 8

# Tileset image layout
TILES_PER_ROW_DEFAULT = 16  # Default tiles per row in tileset images

# =============================================================================
# NPC/Object Event Movement Types
# =============================================================================
# Maps pokeemerald MOVEMENT_TYPE_* constants to (behaviorId, default_params)
# The behaviorId references a behavior definition in Assets/Definitions/Behaviors/
# The default_params dict provides defaults that can be overridden by map properties

MOVEMENT_TYPE_TO_BEHAVIOR: dict[str, tuple[str, dict]] = {
    # Stationary - facing a single direction
    "MOVEMENT_TYPE_FACE_DOWN": ("stationary", {"direction": "down"}),
    "MOVEMENT_TYPE_FACE_UP": ("stationary", {"direction": "up"}),
    "MOVEMENT_TYPE_FACE_LEFT": ("stationary", {"direction": "left"}),
    "MOVEMENT_TYPE_FACE_RIGHT": ("stationary", {"direction": "right"}),

    # Look around - changes facing direction periodically
    "MOVEMENT_TYPE_LOOK_AROUND": ("look_around", {}),

    # Two-way facing (alternates between two directions)
    "MOVEMENT_TYPE_FACE_DOWN_AND_LEFT": ("look_two_ways", {"directions": ["down", "left"]}),
    "MOVEMENT_TYPE_FACE_DOWN_AND_RIGHT": ("look_two_ways", {"directions": ["down", "right"]}),
    "MOVEMENT_TYPE_FACE_UP_AND_LEFT": ("look_two_ways", {"directions": ["up", "left"]}),
    "MOVEMENT_TYPE_FACE_UP_AND_RIGHT": ("look_two_ways", {"directions": ["up", "right"]}),
    "MOVEMENT_TYPE_FACE_LEFT_AND_RIGHT": ("look_two_ways", {"directions": ["left", "right"]}),
    "MOVEMENT_TYPE_FACE_DOWN_AND_UP": ("look_two_ways", {"directions": ["down", "up"]}),
    "MOVEMENT_TYPE_FACE_UP_LEFT_AND_RIGHT": ("look_two_ways", {"directions": ["up", "left", "right"]}),
    "MOVEMENT_TYPE_FACE_DOWN_LEFT_AND_RIGHT": ("look_two_ways", {"directions": ["down", "left", "right"]}),
    "MOVEMENT_TYPE_FACE_DOWN_UP_AND_RIGHT": ("look_two_ways", {"directions": ["down", "up", "right"]}),

    # Wander - random movement within range
    "MOVEMENT_TYPE_WANDER_AROUND": ("wander", {}),
    "MOVEMENT_TYPE_WANDER_LEFT_AND_RIGHT": ("wander_horizontal", {}),
    "MOVEMENT_TYPE_WANDER_UP_AND_DOWN": ("wander_vertical", {}),

    # Walk patterns (walking back and forth)
    "MOVEMENT_TYPE_WALK_DOWN_AND_UP": ("patrol", {"axis": "vertical"}),
    "MOVEMENT_TYPE_WALK_UP_AND_DOWN": ("patrol", {"axis": "vertical"}),
    "MOVEMENT_TYPE_WALK_LEFT_AND_RIGHT": ("patrol", {"axis": "horizontal"}),
    "MOVEMENT_TYPE_WALK_RIGHT_AND_LEFT": ("patrol", {"axis": "horizontal"}),

    # Walk in place (animation without movement)
    "MOVEMENT_TYPE_WALK_IN_PLACE_DOWN": ("walk_in_place", {"direction": "down"}),
    "MOVEMENT_TYPE_WALK_IN_PLACE_UP": ("walk_in_place", {"direction": "up"}),
    "MOVEMENT_TYPE_WALK_IN_PLACE_LEFT": ("walk_in_place", {"direction": "left"}),
    "MOVEMENT_TYPE_WALK_IN_PLACE_RIGHT": ("walk_in_place", {"direction": "right"}),
    "MOVEMENT_TYPE_WALK_SLOWLY_IN_PLACE_DOWN": ("walk_in_place", {"direction": "down", "slow": True}),
    "MOVEMENT_TYPE_WALK_SLOWLY_IN_PLACE_UP": ("walk_in_place", {"direction": "up", "slow": True}),
    "MOVEMENT_TYPE_WALK_SLOWLY_IN_PLACE_LEFT": ("walk_in_place", {"direction": "left", "slow": True}),
    "MOVEMENT_TYPE_WALK_SLOWLY_IN_PLACE_RIGHT": ("walk_in_place", {"direction": "right", "slow": True}),
    "MOVEMENT_TYPE_JOG_IN_PLACE_DOWN": ("walk_in_place", {"direction": "down", "jog": True}),
    "MOVEMENT_TYPE_JOG_IN_PLACE_UP": ("walk_in_place", {"direction": "up", "jog": True}),
    "MOVEMENT_TYPE_JOG_IN_PLACE_LEFT": ("walk_in_place", {"direction": "left", "jog": True}),
    "MOVEMENT_TYPE_JOG_IN_PLACE_RIGHT": ("walk_in_place", {"direction": "right", "jog": True}),
    "MOVEMENT_TYPE_RUN_IN_PLACE_DOWN": ("walk_in_place", {"direction": "down", "run": True}),
    "MOVEMENT_TYPE_RUN_IN_PLACE_UP": ("walk_in_place", {"direction": "up", "run": True}),
    "MOVEMENT_TYPE_RUN_IN_PLACE_LEFT": ("walk_in_place", {"direction": "left", "run": True}),
    "MOVEMENT_TYPE_RUN_IN_PLACE_RIGHT": ("walk_in_place", {"direction": "right", "run": True}),

    # Rotate behaviors
    "MOVEMENT_TYPE_ROTATE_CLOCKWISE": ("rotate_clockwise", {}),
    "MOVEMENT_TYPE_ROTATE_COUNTERCLOCKWISE": ("rotate_counterclockwise", {}),

    # Square patrol patterns (walk in a square)
    "MOVEMENT_TYPE_WALK_SEQUENCE_UP_RIGHT_DOWN_LEFT": ("patrol", {"pattern": "square_cw"}),
    "MOVEMENT_TYPE_WALK_SEQUENCE_RIGHT_DOWN_LEFT_UP": ("patrol", {"pattern": "square_cw"}),
    "MOVEMENT_TYPE_WALK_SEQUENCE_DOWN_LEFT_UP_RIGHT": ("patrol", {"pattern": "square_cw"}),
    "MOVEMENT_TYPE_WALK_SEQUENCE_LEFT_UP_RIGHT_DOWN": ("patrol", {"pattern": "square_cw"}),
    "MOVEMENT_TYPE_WALK_SEQUENCE_UP_LEFT_DOWN_RIGHT": ("patrol", {"pattern": "square_ccw"}),
    "MOVEMENT_TYPE_WALK_SEQUENCE_LEFT_DOWN_RIGHT_UP": ("patrol", {"pattern": "square_ccw"}),
    "MOVEMENT_TYPE_WALK_SEQUENCE_DOWN_RIGHT_UP_LEFT": ("patrol", {"pattern": "square_ccw"}),
    "MOVEMENT_TYPE_WALK_SEQUENCE_RIGHT_UP_LEFT_DOWN": ("patrol", {"pattern": "square_ccw"}),
    "MOVEMENT_TYPE_WALK_SEQUENCE_UP_LEFT_RIGHT_DOWN": ("patrol", {"pattern": "zigzag"}),
    "MOVEMENT_TYPE_WALK_SEQUENCE_UP_RIGHT_LEFT_DOWN": ("patrol", {"pattern": "zigzag"}),
    "MOVEMENT_TYPE_WALK_SEQUENCE_DOWN_RIGHT_LEFT_UP": ("patrol", {"pattern": "zigzag"}),

    # Copy player behaviors
    "MOVEMENT_TYPE_COPY_PLAYER": ("copy_player", {"mode": "normal"}),
    "MOVEMENT_TYPE_COPY_PLAYER_OPPOSITE": ("copy_player", {"mode": "opposite"}),
    "MOVEMENT_TYPE_COPY_PLAYER_CLOCKWISE": ("copy_player", {"mode": "clockwise"}),
    "MOVEMENT_TYPE_COPY_PLAYER_COUNTERCLOCKWISE": ("copy_player", {"mode": "counterclockwise"}),
    "MOVEMENT_TYPE_COPY_PLAYER_IN_GRASS": ("copy_player", {"mode": "normal", "inGrass": True}),
    "MOVEMENT_TYPE_COPY_PLAYER_OPPOSITE_IN_GRASS": ("copy_player", {"mode": "opposite", "inGrass": True}),
    "MOVEMENT_TYPE_COPY_PLAYER_CLOCKWISE_IN_GRASS": ("copy_player", {"mode": "clockwise", "inGrass": True}),
    "MOVEMENT_TYPE_COPY_PLAYER_COUNTERCLOCKWISE_IN_GRASS": ("copy_player", {"mode": "counterclockwise", "inGrass": True}),

    # Special behaviors
    "MOVEMENT_TYPE_INVISIBLE": ("hidden", {}),
    "MOVEMENT_TYPE_BURIED": ("hidden", {"buried": True}),
    "MOVEMENT_TYPE_TREE_DISGUISE": ("hidden", {"disguise": "tree"}),
    "MOVEMENT_TYPE_MOUNTAIN_DISGUISE": ("hidden", {"disguise": "mountain"}),
    "MOVEMENT_TYPE_BERRY_TREE_GROWTH": ("stationary", {"berryTree": True}),
}

# Default behavior for unknown movement types
DEFAULT_BEHAVIOR = ("stationary", {"direction": "down"})

