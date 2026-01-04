using System.Collections.Generic;
using System.Text.Json.Serialization;
using Microsoft.Xna.Framework;

namespace MonoBall.Core.Maps;

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
    ///     The durations for each frame in seconds.
    /// </summary>
    [JsonPropertyName("frameDurations")]
    public List<double> FrameDurations { get; set; } = new();

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
