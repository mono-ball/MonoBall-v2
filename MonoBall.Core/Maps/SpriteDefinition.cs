using System.Collections.Generic;
using System.Text.Json.Serialization;
using Microsoft.Xna.Framework;

namespace MonoBall.Core.Maps;

/// <summary>
///     Defines the capabilities of a sprite for runtime feature detection.
///     Used by the game engine to determine what animations and behaviors are supported.
/// </summary>
public class SpriteCapabilities
{
    /// <summary>
    ///     Whether the sprite has directional animations (face_*, go_* for north/south/east/west).
    ///     If false, the sprite uses a single animation regardless of facing direction.
    /// </summary>
    [JsonPropertyName("directional")]
    public bool Directional { get; set; }

    /// <summary>
    ///     Whether the sprite's animation changes when moving (go_* animations).
    ///     If false, the sprite keeps its default animation while moving (like a pushed boulder).
    ///     Note: This is separate from GridMovement - a sprite can move without animated movement.
    /// </summary>
    [JsonPropertyName("movementAnimated")]
    public bool MovementAnimated { get; set; }
}

/// <summary>
///     Represents a sprite definition loaded from JSON.
/// </summary>
public class SpriteDefinition
{
    /// <summary>
    ///     The unique identifier for the sprite.
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    ///     The name of the sprite.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    ///     The type of sprite definition.
    /// </summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    /// <summary>
    ///     The path to the texture image, relative to the mod directory.
    /// </summary>
    [JsonPropertyName("texturePath")]
    public string TexturePath { get; set; } = string.Empty;

    /// <summary>
    ///     The width of each frame in pixels.
    /// </summary>
    [JsonPropertyName("frameWidth")]
    public int FrameWidth { get; set; }

    /// <summary>
    ///     The height of each frame in pixels.
    /// </summary>
    [JsonPropertyName("frameHeight")]
    public int FrameHeight { get; set; }

    /// <summary>
    ///     The total number of frames in the sprite sheet.
    /// </summary>
    [JsonPropertyName("frameCount")]
    public int FrameCount { get; set; }

    /// <summary>
    ///     The movement profile ID that defines movement speeds and animation type mappings for this sprite.
    ///     Optional - only required for sprites that have movement animations (Capabilities.MovementAnimated = true).
    /// </summary>
    [JsonPropertyName("movementProfileId")]
    public string? MovementProfileId { get; set; }

    /// <summary>
    ///     The animation profile ID that defines animation durations for this sprite.
    ///     Required field - must reference an existing animation profile (e.g., "pokeemerald:profile:animation/standard").
    /// </summary>
    [JsonPropertyName("animationProfileId")]
    public string AnimationProfileId { get; set; } = string.Empty;

    /// <summary>
    ///     The default animation to play when the sprite is spawned or idle.
    ///     Required field - must reference an animation in the Animations list.
    /// </summary>
    [JsonPropertyName("defaultAnimation")]
    public string DefaultAnimation { get; set; } = string.Empty;

    /// <summary>
    ///     The capabilities of this sprite (directional, movement animated, etc.).
    ///     Used by the engine to determine what features are available for this sprite.
    /// </summary>
    [JsonPropertyName("capabilities")]
    public SpriteCapabilities Capabilities { get; set; } = new();

    /// <summary>
    ///     The list of frame definitions.
    /// </summary>
    [JsonPropertyName("frames")]
    public List<SpriteFrame> Frames { get; set; } = new();

    /// <summary>
    ///     The list of animations.
    /// </summary>
    [JsonPropertyName("animations")]
    public List<SpriteAnimation> Animations { get; set; } = new();
}

/// <summary>
///     Represents a single frame in a sprite sheet.
/// </summary>
public class SpriteFrame
{
    /// <summary>
    ///     The index of this frame.
    /// </summary>
    [JsonPropertyName("index")]
    public int Index { get; set; }

    /// <summary>
    ///     The X coordinate of the frame in the texture.
    /// </summary>
    [JsonPropertyName("x")]
    public int X { get; set; }

    /// <summary>
    ///     The Y coordinate of the frame in the texture.
    /// </summary>
    [JsonPropertyName("y")]
    public int Y { get; set; }

    /// <summary>
    ///     The width of the frame in pixels.
    /// </summary>
    [JsonPropertyName("width")]
    public int Width { get; set; }

    /// <summary>
    ///     The height of the frame in pixels.
    /// </summary>
    [JsonPropertyName("height")]
    public int Height { get; set; }
}

/// <summary>
///     Represents an animation sequence for a sprite.
/// </summary>
public class SpriteAnimation
{
    /// <summary>
    ///     The name of the animation.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    ///     The animation type that references an animation definition in the animation profile (e.g., "face", "go", "go_fast", "run").
    ///     Required field - used to lookup duration from the sprite's animation profile.
    ///     The animation name typically follows the pattern "{animationType}_{direction}" (e.g., "go_south", "go_fast_north").
    /// </summary>
    [JsonPropertyName("animationType")]
    public string AnimationType { get; set; } = string.Empty;

    /// <summary>
    ///     Whether this animation loops.
    /// </summary>
    [JsonPropertyName("loop")]
    public bool Loop { get; set; }

    /// <summary>
    ///     The indices of frames in this animation sequence.
    /// </summary>
    [JsonPropertyName("frameIndices")]
    public List<int> FrameIndices { get; set; } = new();

    /// <summary>
    ///     Optional per-frame durations in seconds (overrides profile's frameSequence or duration for each frame).
    ///     If specified, length must match the number of frames in FrameIndices.
    ///     Values are in seconds (same unit as animation profile durations).
    ///     If null, durations are calculated from the sprite's animation profile.
    /// </summary>
    [JsonPropertyName("frameSequence")]
    public double[]? FrameSequence { get; set; }

    /// <summary>
    ///     Whether to flip the sprite horizontally for this animation.
    /// </summary>
    [JsonPropertyName("flipHorizontal")]
    public bool FlipHorizontal { get; set; }

    /// <summary>
    ///     Whether to flip the sprite vertically for this animation.
    /// </summary>
    [JsonPropertyName("flipVertical")]
    public bool FlipVertical { get; set; }
}

/// <summary>
///     Represents a cached animation frame with precomputed rectangle and duration.
/// </summary>
public class SpriteAnimationFrame
{
    /// <summary>
    ///     The source rectangle for this frame in the texture.
    /// </summary>
    public Rectangle SourceRectangle { get; set; }

    /// <summary>
    ///     The duration of this frame in seconds.
    /// </summary>
    public float DurationSeconds { get; set; }

    /// <summary>
    ///     The sprite sheet frame index (from SpriteDefinition.Frames[].Index).
    ///     Stored during precomputation to enable O(1) frame lookup in animation system.
    /// </summary>
    public int FrameIndex { get; set; }
}
