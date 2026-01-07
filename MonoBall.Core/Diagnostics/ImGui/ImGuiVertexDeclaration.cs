namespace MonoBall.Core.Diagnostics.ImGui;

using Microsoft.Xna.Framework.Graphics;

/// <summary>
/// Vertex declaration for ImGui vertices.
/// ImDrawVert layout: pos (Vector2) + uv (Vector2) + col (uint packed RGBA).
/// </summary>
internal static class ImGuiVertexDeclaration
{
    /// <summary>
    /// Total size of an ImDrawVert in bytes.
    /// </summary>
    public const int VertexSize = 20;

    /// <summary>
    /// The vertex declaration describing ImDrawVert layout.
    /// </summary>
    public static readonly VertexDeclaration Declaration = new(
        VertexSize,
        new VertexElement(0, VertexElementFormat.Vector2, VertexElementUsage.Position, 0),
        new VertexElement(8, VertexElementFormat.Vector2, VertexElementUsage.TextureCoordinate, 0),
        new VertexElement(16, VertexElementFormat.Color, VertexElementUsage.Color, 0)
    );
}
