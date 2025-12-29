namespace MonoBall.Core.Resources;

/// <summary>
///     Resource type enumeration for cache management.
/// </summary>
public enum ResourceType
{
    /// <summary>
    ///     Texture resource (from SpriteDefinition or TilesetDefinition).
    /// </summary>
    Texture,

    /// <summary>
    ///     Font resource (from FontDefinition).
    /// </summary>
    Font,

    /// <summary>
    ///     Audio resource (from AudioDefinition).
    /// </summary>
    Audio,

    /// <summary>
    ///     Shader resource (from ShaderDefinition).
    /// </summary>
    Shader,
}
