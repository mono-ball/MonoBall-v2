namespace MonoBall.Core.UI.Relationships;

/// <summary>
///     Relationship type for window → child element ownership.
///     Used to link child UI elements (border, background, content, sprites) to their parent window via Arch.Relationships.
/// </summary>
public struct ContainsUIElement
{
    // Marker relationship - no data needed
    // Can be extended with metadata if needed (e.g., ZOrder, Layout constraints)
}
